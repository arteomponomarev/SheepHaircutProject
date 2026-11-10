using System.Collections;
using System.Collections.Generic;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    // (building feature integration lives in FarmHubController.Building.cs)
    /// <summary>
    /// Farm Hub composition root (the game's home screen). Wires data (economy, farm state, sheep roster), the fixed-
    /// angle camera, input, world interactables, HUD and panels, and runs transitions to the activities. All
    /// activities return here via <see cref="SceneNavigator.Home"/>; <see cref="LaunchContext"/> carries the sheep
    /// to shear, the camera view and the panel to reopen.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed partial class FarmHubController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private FarmLayout _layout;
        [SerializeField] private DefaultGameState _defaultState;
        [Tooltip("Shared wool regrowth settings.")]
        [SerializeField] private WoolGrowthConfig _growth;
        [Tooltip("Shearing feel config (wool surface version for save compatibility).")]
        [SerializeField] private ShearingFeelConfig _feel;
        [Header("Scene")]
        [SerializeField] private FarmCameraController _camera;
        [SerializeField] private FarmInputRouter _input;
        [SerializeField] private FarmSelectionFeedback _selection;
        [SerializeField] private FarmHUD _hud;
        [SerializeField] private FarmPanels _panels;
        [SerializeField] private FarmWorldLabels _labels;
        [SerializeField] private SellPanel _sell;
        [SerializeField] private ProcessingHapticsDriver _haptics;
        [SerializeField] private HapticsConfig _hapticsConfig;
        [SerializeField] private Transform _interactablesRoot;
        [SerializeField] private LayerMask _interactableMask = 1 << 8;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.7f;

        readonly List<FarmInteractable> _interactables = new List<FarmInteractable>();
        readonly List<FarmSheepInfo> _sheep = new List<FarmSheepInfo>();
        readonly Dictionary<string, SheepInteraction> _sheepViews = new Dictionary<string, SheepInteraction>();
        [Header("Building")]
        [SerializeField] private FarmBuildingDirector _director;
        [SerializeField] private BuildUI _buildUI;
        WoolEconomy _econ;
        AudioSource _sfx, _ambience;
        AudioClip _tap, _open, _coin, _finish;
        bool _leaving;
        long _displayCoins, _targetCoins;
        float _lastCoinSound;

        public FarmPanels Panels => _panels;
        public FarmCameraController CameraController => _camera;
        public IReadOnlyList<FarmSheepInfo> SheepInfos => _sheep;
        public IReadOnlyList<FarmInteractable> Interactables => _interactables;
        public FarmState State => _director != null && _director.Service != null ? _director.Service.State : null;
        public FarmBuildingDirector Buildings => _director;
        public BuildUI BuildUI => _buildUI;
        public bool IsLeaving => _leaving;
        public string LastStartedScene { get; private set; }
        public bool SoundOn => PlayerPrefs.GetInt(ShearingAudioDirector.PrefKey, 1) == 1;

        public bool AnySheepWithWool { get { foreach (var s in _sheep) if (s.RemainingWool > 0.01f) return true; return false; } }

        public FarmSheepInfo SheepInfo(string id)
        {
            foreach (var s in _sheep) if (s.SheepId == id) return s;
            return new FarmSheepInfo { SheepId = id, RemainingWool = 1f };
        }

        public int LevelOf(string instanceId) { var b = State != null ? State.Find(instanceId) : null; return b != null ? b.level : 1; }

        // ------------------------------------------------------------------ startup

        void Start()
        {
            _econ = WoolEconomy.Instance;
            if (_econ == null) { Debug.LogError("[Farm] Economy not initialised (EconomyBootstrap missing)."); return; }
            if (_quality != null) Application.targetFrameRate = _quality.Active.TargetFrameRate;

            // farm world (buildings + decorations) from the building save → views spawned from grid data
            _director.SoundOn = () => SoundOn;
            _director.Initialise(_layout, _econ, _camera, _input, _econ.Balance.EconomySaveEpoch);

            ReadSheep();
            InitProgression();
            _interactables.Clear();
            SpawnHerdViews();
            foreach (var v in _director.Presenter.Views) { var it = v.GetComponent<FarmInteractable>(); if (it != null) _interactables.Add(it); }
            PlaceSheepInPen(true);
            PushCoats(true);

            _haptics.Initialise(_hapticsConfig);
            SetupAudio();

            _camera.Initialise(_layout.WorldBounds, CoreBounds());
            if (LaunchContext.ConsumeFarmView(out var focus, out var zoom)) _camera.SetView(focus, zoom);

            _hud.Build(SoundOn, _haptics.Manager != null && _haptics.Manager.Enabled, Debug.isDebugBuild);
            _panels.Build(this, _hud.Safe);
            _buildUI.Build(this, _director, _hud.Safe);
            _sell.Build(_hud.Safe, _hud.CoinTarget);
            _labels.Build(this, _camera.Camera, _hud.Safe, _interactables);
            WireBuildingEvents();

            _hud.SoundToggled += on => { PlayerPrefs.SetInt(ShearingAudioDirector.PrefKey, on ? 1 : 0); PlayerPrefs.Save(); ApplySound(); Play(_tap); };
            _hud.HapticsToggled += on => { _haptics.Manager?.SetEnabled(on); Play(_tap); };
            _hud.DevAddDirty += () => _econ.DevAdd(WoolType.Dirty, 100);
            _hud.DevAddWet += () => _econ.DevAdd(WoolType.Wet, 100);
            _hud.DevReset += DevReset;
            _hud.DevAddCoins += () => _econ.DevAddCoins(100);
            _hud.DevFinishBuild += () => _director.DevFinish();
            _sell.Sold += OnSold;
            _sell.CoinArrived += OnCoinArrived;
            _sell.Closed += () => _selection.Deselect();
            _input.Tapped += OnTap;
            _input.GestureStarted += () => _hud.CloseSettings();
            _econ.Changed += OnEconomyChanged;

            _displayCoins = _targetCoins = _econ.Coins;
            OnEconomyChanged();

            int gained = _econ.ConsumeUnseenShearedWool();
            if (gained > 0) _hud.ShowToast("+" + gained + " Dirty Wool");
            string reopen = LaunchContext.ConsumeReturnPanel();
            StartCoroutine(_hud.FadeTo(0f, 0.35f));
            if (!string.IsNullOrEmpty(reopen)) { var t = Find(reopen); if (t != null) Select(t); }
        }

        [SerializeField] private GraphicsQualityConfig _quality;

        /// <summary>Whole buildable lawn (+ building height): buildings can be moved anywhere on it.</summary>
        Bounds CoreBounds()
        {
            var w = _layout.WorldBounds;
            var b = new Bounds(new Vector3(w.center.x, 1.5f, w.center.z), new Vector3(w.size.x, 3f, w.size.z));
            b.Expand(new Vector3(-1f, 0f, -1f));
            return b;
        }

        FarmSheepStates _states;
        public FarmSheepStates SheepStates => _states;

        /// <summary>Loads every sheep's authoritative wool state once (growth since the last visit is applied).</summary>
        void ReadSheep()
        {
            int epoch = _defaultState != null ? _defaultState.SaveEpoch : 0;
            _states = new FarmSheepStates(_growth, epoch, _feel != null ? _feel.WoolSurfaceVersion : 1);
            foreach (var s in _director.Service.State.herd) _states.Add(s.sheepId, s.displayName);
            RefreshSheepInfos();
        }

        void RefreshSheepInfos()
        {
            _sheep.Clear();
            foreach (var e in _states.Entries) _sheep.Add(_states.Info(e));
        }

        /// <summary>Pushes each sheep's current fleece into its farm view (changed rows only).</summary>
        void PushCoats(bool force)
        {
            foreach (var e in _states.Entries)
            {
                if (!_sheepViews.TryGetValue(e.SheepId, out var it) || it.View == null) continue;
                if (e.Mask != null) it.View.SetMask(e.Mask, force);
                else it.View.SetRemainingWool(1f);
            }
        }

        /// <summary>Unlocks (or locks) a sheep's hairstyle from the farm. No resources change.</summary>
        public void SetHairstylePreserved(string sheepId, bool preserved)
        {
            if (!_states.SetPreserved(sheepId, preserved)) return;
            RefreshSheepInfos();
            PushCoats(false);
            _labels.RefreshTexts();
            _hud.ShowToast(preserved ? "Hairstyle saved" : "Hairstyle unlocked · wool regrows");
            Play(_open, 0.4f);
            var it = Find(sheepId);
            if (it != null && _panels.OpenId == sheepId) it.OnTap(this);
        }


        void SetupAudio()
        {
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false; _sfx.spatialBlend = 0f;
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.playOnAwake = false; _ambience.loop = true; _ambience.spatialBlend = 0f;
            _ambience.clip = ProceduralAudioLibrary.Ambience();
            _tap = ProcessingAudioLibrary.Tap();
            _open = ProcessingAudioLibrary.Sparkle();
            _coin = ProcessingAudioLibrary.Coin();
            _finish = ProceduralAudioLibrary.Finish();
            ApplySound();
            _ambience.Play();
        }

        void ApplySound() { if (_ambience != null) _ambience.volume = SoundOn ? _volume * 0.35f : 0f; }

        void Play(AudioClip c, float gain = 1f)
        {
            if (!SoundOn || c == null) return;
            _sfx.pitch = 1f;
            _sfx.PlayOneShot(c, _volume * gain);
        }

        // ------------------------------------------------------------------ data → views

        void OnEconomyChanged()
        {
            _targetCoins = _econ.Coins;
            if (!_sell.IsOpen) _displayCoins = _targetCoins;
            _hud.SetCoins(_displayCoins);
            _hud.SetWool(_econ.Get(WoolType.Dirty), _econ.Get(WoolType.Wet), _econ.Get(WoolType.Clean));
            _labels.RefreshTexts();
            if (_sell.IsOpen) _sell.RefreshValues();
        }

        FarmInteractable Find(string id)
        {
            foreach (var i in _interactables) if (i.BuildingId == id) return i;
            foreach (var i in _interactables) if (i.TypeId == id) return i; // type id (e.g. return panel "trader")
            return null;
        }

        // ------------------------------------------------------------------ interaction

        void OnTap(Vector2 screen)
        {
            if (_leaving) return;
            _hud.CloseSettings();
            Ray ray = _camera.Camera.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, _interactableMask, QueryTriggerInteraction.Collide))
            {
                var target = hit.collider.GetComponentInParent<FarmInteractable>();
                if (target != null) { Select(target); return; }
            }
            ClosePanels();
        }

        /// <summary>Selects an interactable: feedback + its action (panel or activity).</summary>
        public void Select(FarmInteractable target)
        {
            if (_leaving || target == null) return;
            if (_director.Placement.IsActive) return;
            if (_editMode) { EditSelect(target); return; }
            if (_buildUI.CatalogOpen) _buildUI.CloseCatalog();
            if (_sell.IsOpen && !(target is TraderInteraction)) _sell.Close();
            Play(_tap, 0.8f);
            _haptics.ToolStarted();
            if (target is SheepInteraction)
            {
                _selection.Pulse(target);
                _panels.Close();
            }
            else
            {
                _selection.Select(target);
                _camera.FocusOn(target.ViewBounds, _panels.FocusViewportY, false);
                Play(_open, 0.35f);
            }
            // a building still under construction only shows its progress (no functional actions yet)
            var inst = target.InstanceId != null ? State.Find(target.InstanceId) : null;
            if (inst != null && inst.status == BuildingStatus.UnderConstruction) { ShowConstruction(inst); return; }
            // an upgrading building keeps working (barn → shearing); only the house (no function) shows the progress sheet
            if (inst != null && inst.status == BuildingStatus.Upgrading && target is HouseInteraction) { ShowConstruction(inst); return; }
            if (inst != null && !inst.completionSeen) { _director.Service.MarkCompletionSeen(inst.instanceId); _labels.RefreshTexts(); }
            target.OnTap(this);
            AppendBuildingRows(target, inst);
        }

        public void ClosePanels()
        {
            _panels.Close();
            if (_sell.IsOpen) _sell.Close();
            _selection.Deselect();
        }

        public void OpenTrader(FarmInteractable trader)
        {
            _panels.Close();
            _sell.Open();
        }

        // ------------------------------------------------------------------ activities

        public void StartShearing(string sheepId)
        {
            if (_leaving) return;
            LaunchContext.SheepId = sheepId;
            StartActivity(SceneNavigator.Shearing);
        }

        public void StartActivity(string scene)
        {
            if (_leaving) return;
            if (!SceneNavigator.IsInBuild(scene)) { _hud.ShowToast("Scene '" + scene + "' is not in Build Settings"); return; }
            if (_director.Placement.IsActive) _director.Placement.Cancel();
            if (_editMode) SetEditMode(false);
            _director.SaveNow();
            _leaving = true;
            LastStartedScene = scene;
            LaunchContext.StoreFarmView(_camera.SettledFocus, _camera.TargetSize);
            _input.SetEnabled(false);
            _haptics.StopAll();
            StartCoroutine(GoRoutine(scene));
        }

        IEnumerator GoRoutine(string scene)
        {
            yield return _hud.FadeTo(1f, 0.25f);
            if (!SceneNavigator.Go(scene))
            {
                _leaving = false;
                LaunchContext.SheepId = null;
                _input.SetEnabled(true);
                yield return _hud.FadeTo(0f, 0.2f);
            }
        }

        // ------------------------------------------------------------------ selling feedback

        void OnSold(long earned)
        {
            _targetCoins = _econ.Coins;
            _displayCoins = _targetCoins - earned;
            _hud.SetCoins(_displayCoins);
            Play(_finish, 0.6f);
            _haptics.Completed();
        }

        void OnCoinArrived()
        {
            long step = System.Math.Max(1, (long)Mathf.CeilToInt((_targetCoins - _displayCoins) * 0.35f));
            _displayCoins = System.Math.Min(_targetCoins, _displayCoins + step);
            _hud.SetCoins(_displayCoins);
            _hud.PulseCoins();
            if (SoundOn && Time.unscaledTime - _lastCoinSound > 0.045f)
            {
                _lastCoinSound = Time.unscaledTime;
                _sfx.pitch = Random.Range(0.95f, 1.12f);
                _sfx.PlayOneShot(_coin, _volume * 0.55f);
            }
        }

        void Update()
        {
            if (_econ != null && !_sell.IsOpen && _displayCoins != _targetCoins) { _displayCoins = _targetCoins; _hud.SetCoins(_displayCoins); }
            // regional regrowth at a limited rate (RegrowthUpdateInterval), coats update only when something grew
            if (_states != null && _states.Tick(Time.unscaledTime))
            {
                RefreshSheepInfos();
                PushCoats(false);
                _labels.RefreshTexts();
            }
            UpdateBuildButtons();
        }

        // ------------------------------------------------------------------ dev

        void DevReset()
        {
            if (!Debug.isDebugBuild) return;
            SheepWoolStateStore.DeleteAll();
            FarmStateStore.DeleteAll();
            _econ.DevReset();
            _devResetting = true; // don't let OnDestroy / pause write the old farm back
            // reload the farm: views, grid, builder and sheep are rebuilt from the fresh new-player data
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        void OnDestroy() { if (_econ != null) _econ.Changed -= OnEconomyChanged; }

        // ------------------------------------------------------------------ QA
        public void QA_Tap(Vector2 screen) => OnTap(screen);
        public FarmWorldLabels Labels => _labels;
        public WoolGrowthConfig GrowthConfig => _growth;
        public SellPanel Sell => _sell;
        public FarmHUD HUD => _hud;
    }
}
