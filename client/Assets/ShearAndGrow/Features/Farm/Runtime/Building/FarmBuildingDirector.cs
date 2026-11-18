using System;
using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Composition of the building feature inside the Farm Hub: loads the farm state into a
    /// <see cref="BuildingService"/>, spawns views (<see cref="FarmWorldPresenter"/>), owns placement, grid overlay,
    /// the construction site and the builder NPC, ticks construction timers only while something is being built,
    /// and saves on pause / quit. UI and hub talk to this; game rules stay in the service.
    /// </summary>
    public sealed class FarmBuildingDirector : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private BuildingCatalog _catalog;
        [SerializeField] private ConstructionConfig _config;
        [SerializeField] private FarmProgressionConfig _progression;
        public FarmProgressionConfig Progression => _progression;
        public void SetProgression(FarmProgressionConfig p) => _progression = p;
        [Header("Scene")]
        [SerializeField] private Transform _buildingsRoot;
        [SerializeField] private GridOverlay _overlay;
        [SerializeField] private PlacementController _placement;
        [SerializeField] private ConstructionSiteView _site;
        [SerializeField] private BuilderNpcController _builder;
        [SerializeField, Range(0f, 1f)] private float _knockVolume = 0.35f;

        BuildingService _svc;
        FarmWorldPresenter _presenter;
        FarmCameraController _cam;
        FarmInputRouter _input;
        FarmLayout _layout;
        float _tickTimer;
        string _siteInstanceId;
        AudioSource _sfx;
        AudioClip _knock;
        float _lastKnock;

        public BuildingService Service => _svc;
        public FarmWorldPresenter Presenter => _presenter;
        public PlacementController Placement => _placement;
        public GridOverlay Overlay => _overlay;
        public BuilderNpcController Builder => _builder;
        public ConstructionSiteView Site => _site;
        public BuildingCatalog Catalog => _catalog;
        public Func<bool> SoundOn;

        /// <summary>(instance, definition) — a construction just finished while the farm is open.</summary>
        public event Action<BuildingInstanceState, FarmBuildingDefinition> ConstructionCompleted;
        public event Action<BuildingInstanceState, FarmBuildingDefinition> ConstructionStarted;
        public event Action<BuildingInstanceState, FarmBuildingDefinition> UpgradeStarted;

        public void Configure(BuildingCatalog catalog, ConstructionConfig config, Transform root, GridOverlay overlay, PlacementController placement, ConstructionSiteView site, BuilderNpcController builder)
        {
            _catalog = catalog; _config = config; _buildingsRoot = root; _overlay = overlay; _placement = placement; _site = site; _builder = builder;
        }

        public void Initialise(FarmLayout layout, WoolEconomy economy, FarmCameraController cam, FarmInputRouter input, int epoch)
        {
            _layout = layout; _cam = cam; _input = input;
            _svc = new BuildingService(layout, _catalog, _config, economy, new SystemBuildingClock(), null, epoch, _progression);
            FarmStateStore.TryLoad(epoch, out var loaded);
            if (loaded == null) Debug.Log("[Farm] " + FarmStateStore.LastLoadInfo + " → new player farm.");
            _svc.Load(loaded);

            _presenter = gameObject.AddComponent<FarmWorldPresenter>();
            _presenter.Initialise(_svc, _buildingsRoot);
            _overlay.Initialise(_svc.Grid, layout);
            _placement.Initialise(_svc, _presenter, cam, _overlay);
            _placement.Started += () => _input.SetInterceptor(_placement);
            _placement.Finished += (ok, inst) => { if (!_placement.IsActive) _input.SetInterceptor(null); };

            _svc.StatusChanged += OnStatusChanged;
            _svc.InstanceAdded += OnInstanceAdded;
            _svc.UpgradeStarted += b => { SyncSite(false); UpgradeStarted?.Invoke(b, _svc.Definition(b.typeId)); };
            if (_builder != null)
            {
                _builder.SetTuning(_config.BuilderWalkSpeed, _config.BuilderRepositionSeconds, _config.HammerSwingSeconds);
                _builder.Impact += OnHammer;
                _builder.HideNow();
            }
            if (_site != null) _site.Hide();

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false; _sfx.spatialBlend = 0f;
            _knock = MakeKnock();
            SyncSite(true);
        }

        // ------------------------------------------------------------------ construction

        void OnInstanceAdded(BuildingInstanceState b)
        {
            if (b.status != BuildingStatus.UnderConstruction) return;
            SyncSite(false);
            ConstructionStarted?.Invoke(b, _svc.Definition(b.typeId));
        }

        void OnStatusChanged(BuildingInstanceState b)
        {
            if (b.status != BuildingStatus.Completed) return;
            var def = _svc.Definition(b.typeId);
            Vector2 half = HalfSize(def);
            if (_site != null) { _site.Sparkle(_svc.WorldCenter(b), half); }
            if (b.instanceId == _siteInstanceId)
            {
                _siteInstanceId = null;
                if (_site != null) _site.Hide();
                if (_builder != null) _builder.Finish();
            }
            SyncSite(false);
            ConstructionCompleted?.Invoke(b, def);
        }

        Vector2 HalfSize(FarmBuildingDefinition def)
        {
            var fp = def != null ? def.SafeFootprint : Vector2Int.one;
            return new Vector2(fp.x * _layout.CellSize * 0.5f, fp.y * _layout.CellSize * 0.5f);
        }

        /// <summary>Puts the site dressing + builder on the running construction (if any).</summary>
        void SyncSite(bool arriveImmediately)
        {
            var b = _svc.ActiveConstruction;
            if (b == null)
            {
                if (_siteInstanceId != null && _site != null) _site.Hide();
                _siteInstanceId = null;
                return;
            }
            if (_siteInstanceId == b.instanceId) return;
            _siteInstanceId = b.instanceId;
            var def = _svc.Definition(b.typeId);
            var center = _svc.WorldCenter(b);
            var half = HalfSize(def);
            if (_site != null) { _site.Show(center, half); _site.SetStage(_config.StageOf(_svc.Progress(b), out _)); }
            if (_builder != null)
                _builder.AssignSite(center, half, _layout.CellToWorld(_layout.GateCell), arriveImmediately, IsBlockedForBuilder);
        }

        bool IsBlockedForBuilder(Vector3 world)
        {
            var c = _layout.WorldToCell(world);
            var id = _svc.Grid.OccupantAt(c.x, c.y);
            if (id == null || id == _siteInstanceId) return false;
            var inst = _svc.State.Find(id);
            var def = inst != null ? _svc.Definition(inst.typeId) : null;
            return def != null && !def.IsDecoration;
        }

        void OnHammer(Vector3 at)
        {
            if (_site != null && _siteInstanceId != null) _site.Puff(at);
            if (SoundOn != null && !SoundOn()) return;
            if (Time.unscaledTime - _lastKnock < 0.2f || _knock == null) return;
            // only when the site is on screen
            var vp = _cam != null ? _cam.Camera.WorldToViewportPoint(at) : new Vector3(0.5f, 0.5f, 1f);
            if (vp.z < 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return;
            _lastKnock = Time.unscaledTime;
            _sfx.pitch = UnityEngine.Random.Range(0.92f, 1.1f);
            _sfx.PlayOneShot(_knock, _knockVolume);
        }

        void Update()
        {
            if (_svc == null) return;
            if (_svc.ActiveConstructionCount == 0) return; // nothing to tick (no per-frame work when idle)
            _tickTimer -= Time.unscaledDeltaTime;
            if (_tickTimer > 0f) return;
            _tickTimer = _config.TickInterval;
            _svc.Tick();
            _presenter.RefreshConstruction();
            var b = _svc.ActiveConstruction;
            if (b != null && _site != null) _site.SetStage(_config.StageOf(_svc.Progress(b), out _));
        }

        void OnApplicationPause(bool paused)
        {
            if (_svc == null) return;
            if (paused) _svc.Save();
            else { _svc.Tick(); _presenter.RefreshConstruction(); SyncSite(true); }
        }

        void OnApplicationQuit() { if (_svc != null) _svc.Save(); }

        public void SaveNow() { if (_svc != null) _svc.Save(); }

        /// <summary>Development: finish running constructions immediately.</summary>
        public void DevFinish() { _svc?.DevFinishConstructions(); }

        /// <summary>
        /// Stops this director from ever writing farm.json again (tick, pause, quit). Used right before a developer
        /// reset deletes the saves and reloads the scene, so the old farm can never be written back.
        /// </summary>
        public void SuspendForReset()
        {
            if (_svc != null) { _svc.StatusChanged -= OnStatusChanged; _svc.InstanceAdded -= OnInstanceAdded; }
            _svc = null;
            enabled = false;
        }

        /// <summary>Re-syncs construction visuals (site dressing, builder, building stage) after an out-of-band change.</summary>
        public void RefreshConstructionVisuals()
        {
            if (_svc == null) return;
            _svc.Tick();
            _presenter.RefreshConstruction();
            SyncSite(false);
            var b = _svc.ActiveConstruction;
            if (b != null && _site != null) _site.SetStage(_config.StageOf(_svc.Progress(b), out _));
        }

        static AudioClip MakeKnock()
        {
            const int rate = 22050; int n = (int)(rate * 0.09f);
            var data = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 55f);
                float body = Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 410f * t) * 0.3f;
                float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 300f);
                data[i] = (body * env + click * 0.6f) * 0.7f;
            }
            var clip = AudioClip.Create("Knock", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}