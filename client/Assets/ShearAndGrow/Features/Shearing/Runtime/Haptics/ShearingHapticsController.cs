using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Translates shearing gameplay events into haptic commands. Everything is driven by <see cref="CutDeltaEvent"/>,
    /// which the session only raises for NEW unique wool removed from the authoritative mask — finger movement, raycast
    /// hits or passes over bare wool never produce haptics.
    /// </summary>
    public sealed class ShearingHapticsController : MonoBehaviour
    {
        [Tooltip("Smoothing of the cut-density signal (higher = faster response).")]
        [SerializeField] private float _densityResponse = 14f;
        [Tooltip("Minimum time between two 'first contact' impacts, e.g. when re-entering dense wool in one swipe.")]
        [SerializeField] private float _contactCooldown = 0.25f;

        HapticsConfig _config;
        HapticsManager _manager;
        bool _inContact;
        bool _cutActive;
        float _frameArea;
        float _lastCutTime = -10f;
        float _lastContactTime = -10f;
        float _densityEma;

        public HapticsManager Manager => _manager;
        public HapticsConfig Config => _config;
        public bool Enabled => _manager != null && _manager.Enabled;

        public void Initialise(HapticsConfig config, ShearingEventHub hub)
        {
            _config = config;
            var platform = HapticsPlatformFactory.Create(config);
            bool isIOS = Application.platform == RuntimePlatform.IPhonePlayer;
            _manager = new HapticsManager(platform, config, isIOS);
            if (!platform.IsSupported)
                Debug.Log("[Haptics] Not available on this device: " + platform.LastError + " | " + platform.Capabilities);

            hub.CutDelta += OnCut;
            hub.ContactChanged += OnContact;
            hub.BareContactChanged += OnBare;
            hub.Clump += OnClump;
            hub.Completed += OnCompleted;
            hub.SessionClosing += StopAll;
        }

        public void SetEnabled(bool enabled) => _manager?.SetEnabled(enabled);

        void OnContact(bool contact)
        {
            _inContact = contact;
            if (!contact) EndCut();
        }

        void OnBare(bool bare)
        {
            if (bare) EndCut(); // gliding over sheared wool: silence immediately
        }

        void OnCut(CutDeltaEvent e)
        {
            if (!_inContact || e.NewAreaNormalized <= 0f) return;
            _frameArea += e.NewAreaNormalized;
            _lastCutTime = Time.unscaledTime;
            if (!_cutActive && _frameArea >= _config.MinContactArea)
            {
                _cutActive = true;
                _densityEma = 0f;
                if (Time.unscaledTime - _lastContactTime >= _contactCooldown)
                {
                    _lastContactTime = Time.unscaledTime;
                    _manager.Tick(Time.unscaledTime);
                    _manager.PlayContact(); // same frame as the visual gap and the motor audio start
                }
            }
        }

        void OnClump(ClumpEvent e)
        {
            if (e.Type != ClumpEventType.Detached || e.Size < _config.MinChunkSize) return;
            _manager.Tick(Time.unscaledTime);
            _manager.PlayChunk(e.Size);
        }

        void OnCompleted()
        {
            EndCut();
            _manager.Tick(Time.unscaledTime);
            _manager.PlayCompletion();
        }

        void EndCut()
        {
            _cutActive = false;
            _frameArea = 0f;
            _densityEma = 0f;
            _manager?.StopCutting();
        }

        void StopAll()
        {
            _cutActive = false;
            _frameArea = 0f;
            _manager?.StopAll();
        }

        void Update()
        {
            if (_manager == null) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            if (_cutActive)
            {
                float rate = _frameArea / dt;
                _densityEma = Mathf.Lerp(_densityEma, rate, 1f - Mathf.Exp(-_densityResponse * dt));
                if (now - _lastCutTime > _config.StopTimeout)
                {
                    EndCut(); // short gaps in new wool are bridged; longer ones stop the texture
                }
                else
                {
                    var p = _manager.Profile;
                    float density01 = p != null ? Mathf.Clamp01(_densityEma / Mathf.Max(1e-5f, p.DensityForFullTexture)) : 0f;
                    _manager.SetCutting(density01);
                }
            }
            _frameArea = 0f;
            _manager.Tick(now);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) StopAll();
        }

        void OnApplicationPause(bool paused)
        {
            _cutActive = false;
            _manager?.OnApplicationPause(paused);
        }

        void OnDisable() => StopAll();

        void OnDestroy()
        {
            _manager?.Dispose();
            _manager = null;
        }
    }
}
