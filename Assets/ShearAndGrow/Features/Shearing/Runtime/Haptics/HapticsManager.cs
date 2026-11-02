using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Platform-agnostic haptics brain: settings (enabled / profile / strength, persisted), output scaling per platform,
    /// priorities (completion &gt; chunk &gt; contact &gt; texture), pulse rate limiting and the single continuous
    /// cutting effect. Plain C# with injected time so it is deterministic and testable. No allocations per call.
    /// </summary>
    public sealed class HapticsManager
    {
        public const string PrefEnabled = "ShearAndGrow.Haptics.Enabled";
        public const string PrefProfile = "ShearAndGrow.Haptics.Profile";
        public const string PrefStrength = "ShearAndGrow.Haptics.Strength";

        enum Priority { Texture = 0, Contact = 1, Chunk = 2, Completion = 3 }

        readonly IHapticsPlatform _platform;
        readonly HapticsConfig _config;
        readonly bool _isIOS;
        HapticProfile _profile;

        float _now;
        float _lastPulseTime = -10f;
        Priority _lastPriority;
        float _lastChunkTime = -10f;
        float _accentBusyUntil = -10f;

        bool _cutting;
        float _density01;
        bool _continuousRunning;
        float _nextContinuousUpdate;
        float _nextTexturePulse;
        bool _paused;

        public bool Enabled { get; private set; }
        public int ProfileIndex { get; private set; }
        /// <summary>Player strength multiplier (debug/settings slider), 0..1.5.</summary>
        public float Strength { get; private set; }
        public HapticProfile Profile => _profile;
        public IHapticsPlatform Platform => _platform;
        public bool IsCutting => _cutting;
        public bool ContinuousRunning => _continuousRunning;
        public int SemanticEvents { get; private set; }
        public int SuppressedEvents { get; private set; }

        public HapticsManager(IHapticsPlatform platform, HapticsConfig config, bool isIOS)
        {
            _platform = platform;
            _config = config;
            _isIOS = isIOS;
            Enabled = PlayerPrefs.GetInt(PrefEnabled, 1) == 1;
            Strength = Mathf.Clamp(PlayerPrefs.GetFloat(PrefStrength, 1f), 0f, 1.5f);
            SetProfile(PlayerPrefs.GetInt(PrefProfile, config.DefaultProfile), false);
        }

        // ------------------------------------------------------------------ settings

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            PlayerPrefs.SetInt(PrefEnabled, enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (!enabled) StopAll();
        }

        public void SetProfile(int index, bool save = true)
        {
            var profiles = _config.Profiles;
            index = Mathf.Clamp(index, 0, profiles.Length - 1);
            if (profiles[index] == null) index = Mathf.Clamp(_config.DefaultProfile, 0, profiles.Length - 1);
            ProfileIndex = index;
            _profile = profiles[index];
            if (save) { PlayerPrefs.SetInt(PrefProfile, index); PlayerPrefs.Save(); }
            if (_continuousRunning) _nextContinuousUpdate = 0f; // re-parameterise immediately
        }

        public void SetStrength(float strength)
        {
            Strength = Mathf.Clamp(strength, 0f, 1.5f);
            PlayerPrefs.SetFloat(PrefStrength, Strength);
            PlayerPrefs.Save();
        }

        bool CanPlay => Enabled && !_paused && _profile != null && _platform.IsSupported;

        float Scale(float value)
        {
            float platformScale = _isIOS ? _profile.IOSScale : _profile.AndroidScale;
            return Mathf.Clamp01(value * _profile.MainIntensity * Strength * platformScale);
        }

        // ------------------------------------------------------------------ discrete accents

        bool TryReserve(Priority priority)
        {
            float since = _now - _lastPulseTime;
            if (since < _profile.MinIntervalBetweenPulses && priority <= _lastPriority)
            {
                SuppressedEvents++;
                return false;
            }
            _lastPulseTime = _now;
            _lastPriority = priority;
            return true;
        }

        void AfterAccent(float busySeconds)
        {
            _accentBusyUntil = _now + busySeconds;
            // On Android an accent replaces the running waveform; resume the texture once the accent is felt.
            if (_platform.ImpactInterruptsContinuous && _continuousRunning)
            {
                _continuousRunning = false;
                _nextContinuousUpdate = _accentBusyUntil;
            }
            _nextTexturePulse = Mathf.Max(_nextTexturePulse, _accentBusyUntil);
        }

        public void PlayContact()
        {
            if (!CanPlay || !TryReserve(Priority.Contact)) return;
            _platform.PlayImpact(HapticImpact.Contact, Scale(_profile.ContactImpactStrength), Mathf.Clamp01(_profile.Sharpness + 0.15f));
            SemanticEvents++;
            AfterAccent(0.07f);
        }

        public void PlayChunk(float relativeSize)
        {
            if (!CanPlay) return;
            if (_now - _lastChunkTime < _profile.ChunkCooldown) { SuppressedEvents++; return; }
            if (!TryReserve(Priority.Chunk)) return;
            _lastChunkTime = _now;
            float sizeBoost = Mathf.Lerp(0.85f, 1f, Mathf.InverseLerp(1.3f, 1.8f, relativeSize));
            _platform.PlayImpact(HapticImpact.Chunk, Scale(_profile.ChunkImpactStrength * sizeBoost), Mathf.Clamp01(_profile.Sharpness - 0.2f));
            SemanticEvents++;
            AfterAccent(0.1f);
        }

        public void PlayCompletion()
        {
            if (!CanPlay) return;
            StopCutting();
            _lastPulseTime = _now;
            _lastPriority = Priority.Completion;
            _platform.PlayCompletion(Scale(_profile.CompletionStrength), Mathf.Clamp01(_profile.Sharpness + 0.1f), _profile.CompletionGap);
            SemanticEvents++;
            _accentBusyUntil = _now + _profile.CompletionGap + 0.12f;
        }

        // ------------------------------------------------------------------ continuous cutting

        /// <summary>Called every frame while NEW wool is being removed. density01 = normalised removal rate.</summary>
        public void SetCutting(float density01)
        {
            _density01 = Mathf.Clamp01(density01);
            if (!_cutting)
            {
                _cutting = true;
                _nextContinuousUpdate = Mathf.Max(_now, _accentBusyUntil);
                _nextTexturePulse = Mathf.Max(_now, _accentBusyUntil);
            }
        }

        /// <summary>Immediate stop of the cutting texture (release, bare area, timeout, leaving the sheep).</summary>
        public void StopCutting()
        {
            _cutting = false;
            _density01 = 0f;
            if (_continuousRunning)
            {
                _platform.StopContinuous();
                _continuousRunning = false;
            }
        }

        float TextureIntensity()
        {
            float d = Mathf.Lerp(_profile.TextureFloor, 1f, _density01);
            return Scale(_profile.ContinuousIntensity * d);
        }

        float TextureSharpness() => Mathf.Clamp01(_profile.Sharpness + (_density01 - 0.5f) * 0.2f);

        public void Tick(float now)
        {
            _now = now;
            if (!_cutting) return;
            if (!CanPlay)
            {
                if (_continuousRunning) { _platform.StopContinuous(); _continuousRunning = false; }
                return;
            }
            if (now < _accentBusyUntil) return;

            if (_platform.HasNativeContinuous)
            {
                if (!_continuousRunning)
                {
                    _platform.StartContinuous(TextureIntensity(), TextureSharpness());
                    _continuousRunning = true;
                    _nextContinuousUpdate = now + _profile.ContinuousUpdateInterval;
                }
                else if (now >= _nextContinuousUpdate)
                {
                    _platform.UpdateContinuous(TextureIntensity(), TextureSharpness());
                    _nextContinuousUpdate = now + _profile.ContinuousUpdateInterval;
                }
            }
            else if (now >= _nextTexturePulse)
            {
                // Pulse-based devices: light crisp ticks; denser cutting -> slightly faster rhythm.
                float interval = _profile.ContinuousUpdateInterval * Mathf.Lerp(1.25f, 0.85f, _density01);
                interval = Mathf.Max(interval, _profile.MinIntervalBetweenPulses);
                _nextTexturePulse = now + interval;
                if (TryReserve(Priority.Texture))
                {
                    _platform.PlayImpact(HapticImpact.Texture, TextureIntensity(), TextureSharpness());
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public void StopAll()
        {
            _cutting = false;
            _continuousRunning = false;
            _density01 = 0f;
            _platform.StopAll();
        }

        public void OnApplicationPause(bool paused)
        {
            _paused = paused;
            StopAll();
            _platform.OnApplicationPause(paused);
        }

        public void Dispose()
        {
            StopAll();
            _platform.Dispose();
        }
    }
}
