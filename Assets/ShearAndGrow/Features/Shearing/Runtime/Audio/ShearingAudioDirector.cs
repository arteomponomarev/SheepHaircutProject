using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Layered audio (spec §9): motor loop on contact, cut layer driven ONLY by new-area-per-second, rate-limited pooled
    /// one-shots (tear / fluff / landing), ambience, rare sheep murmurs and a single finish cue. Feedback stops logically
    /// at release in the same frame; a very short volume ramp only prevents clicks.
    /// </summary>
    public sealed class ShearingAudioDirector : MonoBehaviour
    {
        public const string PrefKey = "ShearAndGrow.SoundEnabled";

        ShearingAudioConfig _cfg;
        AudioSource _motor, _cut, _ambience, _murmur;
        AudioSource[] _voices;
        int _nextVoice;
        AudioClip[] _tear, _fluff, _landing, _murmurs;
        AudioClip _finish;
        bool _contact;
        float _newAreaThisFrame;
        float _cutRateEma;
        float _lastTear = -10f, _lastFluff = -10f, _lastLanding = -10f;
        float _nextMurmur;
        System.Random _rng;
        Transform _listenerAnchor;

        public bool SoundEnabled { get; private set; } = true;
        public float CutIntensity => _cut != null ? _cut.volume : 0f;
        public bool MotorAudible => _motor != null && _motor.volume > 0.001f;
        public int OneShotsPlayed { get; private set; }

        public void Initialise(ShearingAudioConfig cfg, ShearingEventHub hub, Transform listenerAnchor, int seed)
        {
            _cfg = cfg;
            _rng = new System.Random(seed);
            _listenerAnchor = listenerAnchor;
            SoundEnabled = PlayerPrefs.GetInt(PrefKey, 1) == 1;

            _tear = cfg.WoolTear != null && cfg.WoolTear.Length > 0 ? cfg.WoolTear : ProceduralAudioLibrary.WoolTear(5);
            _fluff = cfg.WoolFluff != null && cfg.WoolFluff.Length > 0 ? cfg.WoolFluff : ProceduralAudioLibrary.WoolFluff(3);
            _landing = cfg.WoolLanding != null && cfg.WoolLanding.Length > 0 ? cfg.WoolLanding : ProceduralAudioLibrary.WoolLanding(3);
            _murmurs = cfg.SheepMurmur != null && cfg.SheepMurmur.Length > 0 ? cfg.SheepMurmur : ProceduralAudioLibrary.SheepMurmur();
            _finish = cfg.Finish != null ? cfg.Finish : ProceduralAudioLibrary.Finish();

            _motor = CreateSource("MotorLoop", cfg.MotorLoop != null ? cfg.MotorLoop : ProceduralAudioLibrary.MotorLoop(), true, 0f);
            _cut = CreateSource("CutLoop", cfg.CutLoop != null ? cfg.CutLoop : ProceduralAudioLibrary.CutLoop(), true, 0f);
            _ambience = CreateSource("Ambience", cfg.Ambience != null ? cfg.Ambience : ProceduralAudioLibrary.Ambience(), true, 0f);
            _murmur = CreateSource("SheepMurmur", null, false, 0f);
            _murmur.spatialBlend = 0.5f;
            _voices = new AudioSource[Mathf.Max(1, cfg.OneShotVoices)];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = CreateSource("Voice_" + i, null, false, 0f);
                _voices[i].spatialBlend = 0.55f;
                _voices[i].minDistance = 1.5f;
            }
            _motor.Play();
            _cut.Play();
            _ambience.Play();
            _nextMurmur = Time.time + Range(cfg.MurmurInterval) * 0.5f;

            hub.ContactChanged += OnContact;
            hub.CutDelta += e => _newAreaThisFrame += e.NewAreaNormalized;
            hub.Clump += OnClump;
            hub.Completed += OnCompleted;
            hub.SessionClosing += StopFeedbackNow;
        }

        AudioSource CreateSource(string name, AudioClip clip, bool loop, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = loop;
            s.volume = volume;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            return s;
        }

        float Range(Vector2 r) => r.x + (float)_rng.NextDouble() * (r.y - r.x);

        public void SetSoundEnabled(bool enabled)
        {
            SoundEnabled = enabled;
            PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        void OnContact(bool contact)
        {
            _contact = contact;
            if (!contact) _cutRateEma = 0f; // logical stop this frame; ramp handles the click
        }

        void StopFeedbackNow()
        {
            _contact = false;
            _cutRateEma = 0f;
            if (_motor != null) _motor.volume = 0f;
            if (_cut != null) _cut.volume = 0f;
        }

        void OnApplicationPause(bool paused) { if (paused) StopFeedbackNow(); }
        void OnApplicationFocus(bool focus) { if (!focus) StopFeedbackNow(); }

        void Update()
        {
            if (_cfg == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float master = SoundEnabled ? _cfg.MasterVolume : 0f;

            // cut density from NEW area only (speed alone never creates rustle)
            float rate = _newAreaThisFrame / dt;
            _newAreaThisFrame = 0f;
            _cutRateEma = Mathf.Lerp(_cutRateEma, rate, 1f - Mathf.Exp(-14f * dt));
            float cutLevel = _contact ? Mathf.Clamp01(_cutRateEma / Mathf.Max(1e-5f, _cfg.CutDensityForMax)) : 0f;

            float motorTarget = _contact ? Mathf.Lerp(_cfg.MotorIdleVolume, _cfg.MotorVolume, Mathf.Sqrt(cutLevel)) : 0f;
            _motor.volume = Mathf.MoveTowards(_motor.volume, motorTarget * master, dt / Mathf.Max(0.005f, _cfg.MotorRampSeconds) * _cfg.MotorVolume);
            _motor.pitch = Mathf.Lerp(0.94f, 1.06f, cutLevel); // load changes the motor texture slightly
            _cut.volume = Mathf.MoveTowards(_cut.volume, cutLevel * _cfg.CutVolume * master, dt / Mathf.Max(0.005f, _cfg.CutRampSeconds) * _cfg.CutVolume);
            _ambience.volume = _cfg.AmbienceVolume * master;

            if (Time.time >= _nextMurmur)
            {
                _nextMurmur = Time.time + Range(_cfg.MurmurInterval);
                if (master > 0f && _murmurs.Length > 0)
                {
                    _murmur.pitch = Range(_cfg.PitchJitter);
                    _murmur.PlayOneShot(_murmurs[_rng.Next(0, _murmurs.Length)], _cfg.OneShotVolume * 0.6f * master);
                }
            }
        }

        void PlayOneShot(AudioClip[] set, Vector3 pos, float gainMul)
        {
            if (!SoundEnabled || set == null || set.Length == 0) return;
            var v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length; // voice limit: oldest voice is reused
            v.transform.position = pos;
            v.spatialBlend = 0.55f;
            v.pitch = Range(_cfg.PitchJitter);
            v.Stop();
            v.clip = set[_rng.Next(0, set.Length)];
            v.volume = _cfg.OneShotVolume * Range(_cfg.GainJitter) * gainMul * _cfg.MasterVolume;
            v.Play();
            OneShotsPlayed++;
        }

        void OnClump(ClumpEvent e)
        {
            float now = Time.time;
            switch (e.Type)
            {
                case ClumpEventType.Detached:
                    if (now - _lastTear >= _cfg.TearMinInterval) { _lastTear = now; PlayOneShot(_tear, e.Position, Mathf.Lerp(0.7f, 1f, e.Size - 1f)); }
                    if (e.Size > 1.3f && now - _lastFluff >= _cfg.FluffMinInterval) { _lastFluff = now; PlayOneShot(_fluff, e.Position, 0.6f); }
                    break;
                case ClumpEventType.Landed:
                    if (e.Size > 1.3f && now - _lastLanding >= _cfg.LandingMinInterval) { _lastLanding = now; PlayOneShot(_landing, e.Position, 0.5f); }
                    break;
            }
        }

        void OnCompleted()
        {
            if (!SoundEnabled) return;
            var v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.spatialBlend = 0f;
            v.pitch = 1f;
            v.Stop();
            v.clip = _finish;
            v.volume = _cfg.OneShotVolume * _cfg.MasterVolume;
            v.Play();
            v.spatialBlend = 0.55f;
        }
    }
}
