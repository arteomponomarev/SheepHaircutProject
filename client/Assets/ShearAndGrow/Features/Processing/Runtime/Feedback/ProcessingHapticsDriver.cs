using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Haptics for a processing mini-game on the EXISTING Haptics 2.0 stack (HapticsManager + IOSHaptics Core Haptics /
    /// AndroidHaptics / NullHaptics). Only the profile set differs (Wash or Dry HapticsConfig):
    ///   first real contact → light contact pulse; real processing → soft continuous texture scaled by work done;
    ///   notable area → small accent; completion → positive double tap. Finished wool produces nothing
    ///   (texture stops after the configured timeout). Stops on release, pause, focus loss and disable.
    /// </summary>
    public sealed class ProcessingHapticsDriver : MonoBehaviour
    {
        HapticsManager _manager;
        HapticsConfig _config;
        float _lastWorkTime = -10f;
        bool _working;

        public HapticsManager Manager => _manager;

        public void Initialise(HapticsConfig config)
        {
            _config = config;
            var platform = HapticsPlatformFactory.Create(config);
            _manager = new HapticsManager(platform, config, Application.platform == RuntimePlatform.IPhonePlayer);
        }

        public void ToolStarted() { _manager?.Tick(Time.unscaledTime); _manager?.PlayContact(); }

        /// <summary>Real processing this frame (normalised 0..1). Zero over finished wool.</summary>
        public void Work(float density01)
        {
            if (_manager == null) return;
            if (density01 > 0.001f)
            {
                _lastWorkTime = Time.unscaledTime;
                _working = true;
                _manager.SetCutting(Mathf.Clamp01(density01));
            }
        }

        public void Accent() { _manager?.Tick(Time.unscaledTime); _manager?.PlayChunk(1.6f); }
        public void Completed() { _manager?.Tick(Time.unscaledTime); _manager?.PlayCompletion(); }

        public void ToolStopped()
        {
            _working = false;
            _manager?.StopCutting();
        }

        public void StopAll() { _working = false; _manager?.StopAll(); }

        void Update()
        {
            if (_manager == null) return;
            if (_working && Time.unscaledTime - _lastWorkTime > _config.StopTimeout)
            {
                _working = false;
                _manager.StopCutting(); // brief gaps are bridged, longer ones (finished wool) go silent
            }
            _manager.Tick(Time.unscaledTime);
        }

        void OnApplicationFocus(bool focus) { if (!focus) StopAll(); }
        void OnApplicationPause(bool paused) { _working = false; _manager?.OnApplicationPause(paused); }
        void OnDisable() => StopAll();
        void OnDestroy() { _manager?.Dispose(); _manager = null; }
    }
}
