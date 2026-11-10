using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// PLACEHOLDER AUDIO: synthesises every required layer (spec §9.1) at startup so the feature is testable without
    /// authored sound assets. Assign real clips in <see cref="ShearingAudioConfig"/> to override.
    /// </summary>
    public static class ProceduralAudioLibrary
    {
        const int Rate = 22050;

        struct Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad BandPass(float freq, float q)
            {
                float w = 2f * Mathf.PI * freq / Rate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    b0 = alpha / a0, b1 = 0f, b2 = -alpha / a0,
                    a1 = -2f * Mathf.Cos(w) / a0, a2 = (1f - alpha) / a0,
                };
            }

            public static Biquad LowPass(float freq, float q = 0.707f)
            {
                float w = 2f * Mathf.PI * freq / Rate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float cw = Mathf.Cos(w);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    b0 = (1f - cw) * 0.5f / a0, b1 = (1f - cw) / a0, b2 = (1f - cw) * 0.5f / a0,
                    a1 = -2f * cw / a0, a2 = (1f - alpha) / a0,
                };
            }

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        static float Noise(System.Random r) => (float)r.NextDouble() * 2f - 1f;

        static AudioClip Make(string name, float[] data)
        {
            float peak = 1e-5f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float g = 0.85f / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= g;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Crossfades the tail into the head so the clip loops without clicks.</summary>
        static float[] MakeLoop(float[] raw, int fade)
        {
            int n = raw.Length - fade;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = raw[i];
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                o[i] = raw[i] * t + raw[n + i] * (1f - t);
            }
            return o;
        }

        public static AudioClip MotorLoop()
        {
            var r = new System.Random(11);
            int n = Rate; // 1 s, integer cycles of every partial → seamless
            var d = new float[n];
            var lp = Biquad.LowPass(900f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float baseF = 110f;
                float s = Mathf.Sin(2f * Mathf.PI * baseF * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * baseF * 2f * t) * 0.28f
                        + Mathf.Sin(2f * Mathf.PI * baseF * 3f * t) * 0.14f
                        + Mathf.Sin(2f * Mathf.PI * baseF * 5f * t) * 0.05f;
                float am = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 25f * t);
                d[i] = lp.Process(s * am + Noise(r) * 0.06f);
            }
            return Make("TrimmerMotorLoop", d);
        }

        public static AudioClip CutLoop()
        {
            var r = new System.Random(23);
            int fade = Rate / 5;
            var raw = new float[Rate + fade];
            var bp = Biquad.BandPass(2400f, 0.8f);
            var bp2 = Biquad.BandPass(5200f, 1.4f);
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)Rate;
                float am = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 9f * t));
                float n = Noise(r);
                float grain = r.NextDouble() < 0.002 ? Noise(r) * 2f : 0f;
                raw[i] = (bp.Process(n) * 0.8f + bp2.Process(n) * 0.35f) * am + grain * 0.2f;
            }
            return Make("TrimmerCutLoop", MakeLoop(raw, fade));
        }

        public static AudioClip[] WoolTear(int count)
        {
            var clips = new AudioClip[count];
            for (int k = 0; k < count; k++)
            {
                var r = new System.Random(100 + k);
                int n = (int)(Rate * (0.22f + 0.05f * k));
                var d = new float[n];
                var bp = Biquad.BandPass(1300f + 450f * k, 0.9f);
                var lp = Biquad.LowPass(3200f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.45f)), 1.6f);
                    float crackle = r.NextDouble() < 0.02 ? Noise(r) * 1.8f : 0f;
                    d[i] = lp.Process(bp.Process(Noise(r)) + crackle * 0.25f) * env;
                }
                clips[k] = Make("WoolTear_" + k, d);
            }
            return clips;
        }

        public static AudioClip[] WoolFluff(int count)
        {
            var clips = new AudioClip[count];
            for (int k = 0; k < count; k++)
            {
                var r = new System.Random(200 + k);
                int n = (int)(Rate * (0.45f + 0.1f * k));
                var d = new float[n];
                var lp = Biquad.LowPass(700f + 250f * k);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float env = Mathf.Sin(Mathf.PI * t) * Mathf.Exp(-t * 1.5f);
                    d[i] = lp.Process(Noise(r)) * env;
                }
                clips[k] = Make("WoolFluff_" + k, d);
            }
            return clips;
        }

        public static AudioClip[] WoolLanding(int count)
        {
            var clips = new AudioClip[count];
            for (int k = 0; k < count; k++)
            {
                var r = new System.Random(300 + k);
                int n = (int)(Rate * 0.16f);
                var d = new float[n];
                var lp = Biquad.LowPass(380f + 90f * k);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float env = Mathf.Min(1f, t * 40f) * Mathf.Exp(-t * 9f);
                    d[i] = (lp.Process(Noise(r)) + Mathf.Sin(2f * Mathf.PI * (70f + 10f * k) * i / Rate) * 0.12f) * env;
                }
                clips[k] = Make("WoolLanding_" + k, d);
            }
            return clips;
        }

        public static AudioClip[] SheepMurmur()
        {
            var clips = new AudioClip[3];
            for (int k = 0; k < 2; k++)
            {
                var r = new System.Random(400 + k);
                int n = (int)(Rate * (0.85f + 0.15f * k));
                var d = new float[n];
                var f1 = Biquad.BandPass(720f + 60f * k, 4f);
                var f2 = Biquad.BandPass(1150f + 80f * k, 5f);
                var f3 = Biquad.BandPass(2600f, 6f);
                float phase = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float f0 = (230f + 25f * k) * (1f + 0.035f * Mathf.Sin(2f * Mathf.PI * 7f * i / Rate)) * (1f - 0.08f * t);
                    phase += f0 / Rate;
                    float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                    float src = saw + Noise(r) * 0.08f;
                    float env = Mathf.Min(1f, t * 12f) * Mathf.Pow(1f - t, 1.3f);
                    d[i] = (f1.Process(src) + f2.Process(src) * 0.7f + f3.Process(src) * 0.15f) * env;
                }
                clips[k] = Make("SheepBaa_" + k, d);
            }
            {
                var r = new System.Random(450);
                int n = (int)(Rate * 1.6f);
                var d = new float[n];
                var lp = Biquad.LowPass(500f);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float env = Mathf.Sin(Mathf.PI * t);
                    d[i] = lp.Process(Noise(r)) * env * env;
                }
                clips[2] = Make("SheepBreath", d);
            }
            return clips;
        }

        public static AudioClip Ambience()
        {
            var r = new System.Random(500);
            int fade = Rate;
            int n = Rate * 9 + fade;
            var raw = new float[n];
            float brown = 0f;
            var lp = Biquad.LowPass(420f);
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + Noise(r) * 0.02f, -1f, 1f) * 0.998f;
                raw[i] = lp.Process(brown) * 0.6f;
            }
            // birds: short chirp sweeps
            for (int b = 0; b < 9; b++)
            {
                int start = r.Next(0, n - Rate);
                float f0 = 2600f + (float)r.NextDouble() * 1600f;
                int chirps = 2 + r.Next(0, 3);
                for (int c = 0; c < chirps; c++)
                {
                    int s0 = start + c * (int)(Rate * 0.12f);
                    int len = (int)(Rate * 0.07f);
                    float ph = 0f;
                    for (int i = 0; i < len && s0 + i < n; i++)
                    {
                        float t = i / (float)len;
                        ph += (f0 + 900f * Mathf.Sin(Mathf.PI * t)) / Rate;
                        raw[s0 + i] += Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Sin(Mathf.PI * t) * 0.07f;
                    }
                }
            }
            return Make("RoomAmbience", MakeLoop(raw, fade));
        }

        public static AudioClip Finish()
        {
            int n = (int)(Rate * 1.6f);
            var d = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float start = k * 0.07f;
                    if (t < start) continue;
                    float tt = t - start;
                    float env = Mathf.Min(1f, tt * 60f) * Mathf.Exp(-tt * 2.6f);
                    s += (Mathf.Sin(2f * Mathf.PI * notes[k] * tt) + 0.25f * Mathf.Sin(4f * Mathf.PI * notes[k] * tt)) * env;
                }
                d[i] = s;
            }
            return Make("FinishCue", d);
        }
    }
}
