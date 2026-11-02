using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Audio for one processing mini-game. Two reusable loops (tool stream + "working on wool" layer driven only by
    /// real processing), a small pool of one-shot voices with rate limits, and a completion cue. Starts/stops with
    /// the tool in the same frame (short ramps only prevent clicks). Shares the global sound setting.
    /// </summary>
    public sealed class ProcessingAudioDirector : MonoBehaviour
    {
        const int Voices = 6;

        ProcessingConfig _cfg;
        AudioSource _stream, _impact;
        AudioSource[] _voices;
        int _next;
        AudioClip[] _contact, _accentHits, _ambient;
        AudioClip _sparkle, _finish;
        bool _toolOn;
        float _work01;
        float _lastContact = -10f, _lastAmbient = -10f, _lastAccent = -10f;
        float _streamPitch = 1f;
        System.Random _rng = new System.Random(5);

        public bool SoundEnabled { get; private set; } = true;
        public float StreamVolume => _stream != null ? _stream.volume : 0f;
        public float ImpactVolume => _impact != null ? _impact.volume : 0f;
        public int OneShots { get; private set; }

        public void Initialise(ProcessingConfig cfg)
        {
            _cfg = cfg;
            SoundEnabled = PlayerPrefs.GetInt(ShearAndGrow.Shearing.ShearingAudioDirector.PrefKey, 1) == 1;
            bool wash = cfg.Stage == ProcessingStage.Wash;
            _stream = Create("StreamLoop", wash ? ProcessingAudioLibrary.WaterStreamLoop() : ProcessingAudioLibrary.DryerLoop(), true);
            _impact = Create("OnWoolLoop", wash ? ProcessingAudioLibrary.WaterOnWoolLoop() : ProcessingAudioLibrary.AirOnWoolLoop(), true);
            _contact = wash ? ProcessingAudioLibrary.Splash(3) : ProcessingAudioLibrary.AirPuff(3);
            _accentHits = wash ? ProcessingAudioLibrary.Squish(2) : ProcessingAudioLibrary.AirPuff(2);
            _ambient = wash ? ProcessingAudioLibrary.Drip(3) : ProcessingAudioLibrary.AirPuff(1);
            _sparkle = ProcessingAudioLibrary.Sparkle();
            _finish = ShearAndGrow.Shearing.ProceduralAudioLibrary.Finish();
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) _voices[i] = Create("Voice_" + i, null, false);
            _stream.Play();
            _impact.Play();
        }

        AudioSource Create(string n, AudioClip clip, bool loop)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = loop; s.volume = 0f; s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0f;
            return s;
        }

        public void SetSoundEnabled(bool on)
        {
            SoundEnabled = on;
            PlayerPrefs.SetInt(ShearAndGrow.Shearing.ShearingAudioDirector.PrefKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Tool pressed/released (stream sound follows the finger exactly).</summary>
        public void SetTool(bool on) { _toolOn = on; if (!on) _work01 = 0f; }

        /// <summary>Normalised real processing intensity this frame (0 over finished wool or when missing it).</summary>
        public void SetWork(float work01) => _work01 = Mathf.Clamp01(work01);

        public void StopNow()
        {
            _toolOn = false; _work01 = 0f;
            if (_stream != null) _stream.volume = 0f;
            if (_impact != null) _impact.volume = 0f;
        }

        void OnApplicationPause(bool p) { if (p) StopNow(); }
        void OnApplicationFocus(bool f) { if (!f) StopNow(); }

        public void PlayContact() { if (Time.time - _lastContact > 0.25f) { _lastContact = Time.time; Shot(_contact, 0.8f, 0.92f, 1.08f); } }

        public void PlayAccent()
        {
            if (Time.time - _lastAccent < 0.35f) return;
            _lastAccent = Time.time;
            Shot(_accentHits, 0.7f, 0.95f, 1.05f);
            Shot(new[] { _sparkle }, 0.45f, 0.98f, 1.04f);
        }

        public void PlayFinish() => Shot(new[] { _finish }, 1f, 1f, 1f);

        void Shot(AudioClip[] set, float gain, float pMin, float pMax)
        {
            if (!SoundEnabled || set == null || set.Length == 0) return;
            var v = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            v.Stop();
            v.clip = set[_rng.Next(0, set.Length)];
            v.pitch = Mathf.Lerp(pMin, pMax, (float)_rng.NextDouble());
            v.volume = _cfg.OneShotVolume * _cfg.MasterVolume * gain;
            v.Play();
            OneShots++;
        }

        void Update()
        {
            if (_cfg == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float master = SoundEnabled ? _cfg.MasterVolume : 0f;
            float ramp = dt / Mathf.Max(0.005f, _cfg.LoopRampSeconds);
            float streamTarget = (_toolOn ? _cfg.LoopVolume : 0f) * master;
            _stream.volume = Mathf.MoveTowards(_stream.volume, streamTarget, ramp * _cfg.LoopVolume);
            // the stream changes character while it is actually working on wool
            _streamPitch = Mathf.Lerp(_streamPitch, _toolOn ? Mathf.Lerp(1f, 0.93f, _work01) : 1f, 1f - Mathf.Exp(-8f * dt));
            _stream.pitch = _streamPitch;
            float impactTarget = (_toolOn ? Mathf.Sqrt(_work01) * _cfg.ImpactLayerVolume : 0f) * master;
            _impact.volume = Mathf.MoveTowards(_impact.volume, impactTarget, ramp * _cfg.ImpactLayerVolume);
            // occasional drips / air flutters while working
            if (_toolOn && _work01 > 0.1f && Time.time - _lastAmbient > 0.45f && _rng.NextDouble() < dt * 3.0)
            {
                _lastAmbient = Time.time;
                Shot(_ambient, 0.35f, 0.9f, 1.15f);
            }
        }
    }
}
