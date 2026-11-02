using System.Collections;
using ShearAndGrow.Economy;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Temporary main menu controller. Shows Coins and the three wool stocks (event-driven), launches the mini-games
    /// (later: farm buildings call the same SceneNavigator / WoolEconomy APIs) and runs the sell flow feedback.
    /// </summary>
    public sealed class HubController : MonoBehaviour
    {
        [SerializeField] private HubUI _ui;
        [SerializeField] private SellPanel _sell;
        [SerializeField] private HubShowcase _showcase;
        [SerializeField] private ProcessingHapticsDriver _haptics;
        [SerializeField] private HapticsConfig _hapticsConfig;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.7f;

        AudioSource _sfx;
        AudioClip _coin, _tap, _finish;
        long _displayCoins;
        long _targetCoins;
        float _lastCoinSound = -1f;
        bool _leaving;
        WoolEconomy _econ;

        bool SoundOn => PlayerPrefs.GetInt(ShearingAudioDirector.PrefKey, 1) == 1;

        void Start()
        {
            _econ = WoolEconomy.Instance;
            if (_econ == null) { Debug.LogError("[Hub] Economy not initialised (EconomyBootstrap missing)."); return; }
            _haptics.Initialise(_hapticsConfig);
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false; _sfx.spatialBlend = 0f;
            _coin = ProcessingAudioLibrary.Coin();
            _tap = ProcessingAudioLibrary.Tap();
            _finish = ProceduralAudioLibrary.Finish();

            bool dev = Debug.isDebugBuild;
            _ui.Build(SoundOn, _haptics.Manager != null && _haptics.Manager.Enabled, dev);
            _sell.Build(_ui.Safe, _ui.CoinTarget);
            if (_showcase != null) _showcase.Build();

            _ui.ShearPressed += () => Go(SceneNavigator.Shearing);
            _ui.WashPressed += () => Go(SceneNavigator.Wash);
            _ui.DryPressed += () => Go(SceneNavigator.Dry);
            _ui.SellPressed += () => { Tap(); _sell.Open(); };
            _ui.SoundToggled += on => { PlayerPrefs.SetInt(ShearingAudioDirector.PrefKey, on ? 1 : 0); PlayerPrefs.Save(); Tap(); };
            _ui.HapticsToggled += on => { _haptics.Manager?.SetEnabled(on); Tap(); };
            _ui.DevAddDirty += () => _econ.DevAdd(WoolType.Dirty, 100);
            _ui.DevAddWet += () => _econ.DevAdd(WoolType.Wet, 100);
            _ui.DevReset += () => { SheepWoolStateStore.DeleteAll(); _econ.DevReset(); _ui.ShowToast("Save reset (new player)"); };
            _sell.Sold += OnSold;
            _sell.CoinArrived += OnCoinArrived;

            _econ.Changed += Refresh;
            _displayCoins = _targetCoins = _econ.Coins;
            Refresh();

            int gained = _econ.ConsumeUnseenShearedWool();
            if (gained > 0)
            {
                _ui.ShowToast("+" + gained + " Dirty Wool");
                _ui.PulseChip(0);
            }
            StartCoroutine(_ui.FadeTo(0f, 0.35f));
        }

        void OnDestroy() { if (_econ != null) _econ.Changed -= Refresh; }

        [Tooltip("Showcase table centre the camera frames.")]
        [SerializeField] private Vector3 _showcaseCentre = new Vector3(0f, 0.05f, 0.05f);
        [Tooltip("Half width (m) of the showcase that must stay visible.")]
        [SerializeField] private float _showcaseHalfWidth = 0.68f;
        [Tooltip("Viewport height (0 bottom .. 1 top) where the showcase should appear: the free band between chips and buttons.")]
        [SerializeField, Range(0f, 1f)] private float _showcaseViewportY = 0.56f;
        float _lastAspect;

        /// <summary>Frames the wool showcase into the empty middle band for any aspect (portrait phones first).</summary>
        void FrameShowcase()
        {
            var cam = Camera.main;
            if (cam == null || Mathf.Approximately(cam.aspect, _lastAspect)) return;
            _lastAspect = cam.aspect;
            const float pitch = 22f;
            float vFov = cam.fieldOfView * Mathf.Deg2Rad;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * cam.aspect);
            float dist = _showcaseHalfWidth / Mathf.Tan(hFov * 0.5f);
            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            // place the target at the desired viewport height by tilting the view down from the target
            float yOffsetAngle = (_showcaseViewportY - 0.5f) * cam.fieldOfView;
            Quaternion look = Quaternion.Euler(pitch - yOffsetAngle, 0f, 0f);
            cam.transform.SetPositionAndRotation(_showcaseCentre - look * Vector3.forward * dist, rot);
        }

        void LateUpdate() => FrameShowcase();

        void Refresh()
        {
            int d = _econ.Get(WoolType.Dirty), w = _econ.Get(WoolType.Wet), c = _econ.Get(WoolType.Clean);
            _ui.SetResources(d, w, c, _econ.QuoteSale(SellSelection.All));
            if (_showcase != null) _showcase.SetAmounts(d, w, c);
            _targetCoins = _econ.Coins;
            if (!_sell.IsOpen) _displayCoins = _targetCoins;
            _ui.SetCoins(_displayCoins);
            if (_sell.IsOpen) _sell.RefreshValues();
        }

        void OnSold(long earned)
        {
            // coins are already credited & saved; the counter catches up as the coins land
            _targetCoins = _econ.Coins;
            _displayCoins = _targetCoins - earned;
            _ui.SetCoins(_displayCoins);
            if (SoundOn) _sfx.PlayOneShot(_finish, _volume * 0.6f);
            _haptics.Completed();
        }

        void OnCoinArrived()
        {
            long step = Mathf.Max(1, Mathf.CeilToInt((_targetCoins - _displayCoins) * 0.35f));
            _displayCoins = System.Math.Min(_targetCoins, _displayCoins + step);
            _ui.SetCoins(_displayCoins);
            _ui.PulseCoins();
            if (SoundOn && Time.unscaledTime - _lastCoinSound > 0.045f)
            {
                _lastCoinSound = Time.unscaledTime;
                _sfx.pitch = Random.Range(0.95f, 1.12f);
                _sfx.PlayOneShot(_coin, _volume * 0.55f);
            }
        }

        void Update()
        {
            // settle the counter exactly when the animation is done
            if (!_sell.IsOpen && _displayCoins != _targetCoins) { _displayCoins = _targetCoins; _ui.SetCoins(_displayCoins); }
        }

        void Tap()
        {
            if (SoundOn) { _sfx.pitch = 1f; _sfx.PlayOneShot(_tap, _volume * 0.5f); }
        }

        void Go(string scene)
        {
            if (_leaving) return;
            _leaving = true;
            Tap();
            StartCoroutine(GoRoutine(scene));
        }

        IEnumerator GoRoutine(string scene)
        {
            yield return _ui.FadeTo(1f, 0.25f);
            if (!SceneNavigator.Go(scene))
            {
                _leaving = false;
                yield return _ui.FadeTo(0f, 0.2f);
                _ui.ShowToast("Scene '" + scene + "' is not in Build Settings");
            }
        }
    }
}
