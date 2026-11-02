using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// PLACEHOLDER AUDIO for the processing mini-games and the sell screen, synthesised once at startup so the loop is
    /// testable without authored assets. Every clip is meant to be replaced by recorded sound (see report).
    /// Kept soft: no harsh highs, no dryer whine.
    /// </summary>
    public static class ProcessingAudioLibrary
    {
        const int Rate = 22050;

        struct LowPass
        {
            float a, y;
            public LowPass(float cutoff) { a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate); y = 0f; }
            public float Process(float x) { y += a * (x - y); return y; }
        }

        struct BandPass
        {
            LowPass lo, hi;
            public BandPass(float low, float high) { lo = new LowPass(high); hi = new LowPass(low); }
            public float Process(float x) { float l = lo.Process(x); return l - hi.Process(l); }
        }

        static float N(System.Random r) => (float)r.NextDouble() * 2f - 1f;

        static AudioClip Make(string name, float[] d, float peakTarget = 0.85f)
        {
            float peak = 1e-5f;
            for (int i = 0; i < d.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            float g = peakTarget / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            var clip = AudioClip.Create(name, d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>Crossfades the tail into the head so the clip loops seamlessly.</summary>
        static float[] Loopify(float[] src, int fade)
        {
            int n = src.Length - fade;
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = src[i];
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                d[i] = src[i] * t + src[n + i] * (1f - t);
            }
            return d;
        }

        // ---------------------------------------------------------------- washing

        public static AudioClip WaterStreamLoop()
        {
            var r = new System.Random(11);
            int len = Rate * 2, fade = Rate / 4;
            var d = new float[len + fade];
            var bp = new BandPass(900f, 5200f);
            var lp = new LowPass(7000f);
            var bubble = new LowPass(6f);
            for (int i = 0; i < d.Length; i++)
            {
                float mod = 0.75f + 0.25f * (bubble.Process(N(r)) * 6f);
                d[i] = lp.Process(bp.Process(N(r))) * mod;
            }
            return Make("Proc_WaterStreamLoop", Loopify(d, fade), 0.6f);
        }

        /// <summary>Muffled patter of water soaking into wool (layer driven by real cleaning).</summary>
        public static AudioClip WaterOnWoolLoop()
        {
            var r = new System.Random(12);
            int len = Rate * 2, fade = Rate / 4;
            var d = new float[len + fade];
            var lp = new LowPass(1100f);
            var lp2 = new LowPass(500f);
            float env = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                if (r.NextDouble() < 0.004) env = 0.6f + (float)r.NextDouble() * 0.4f; // soft droplets hitting fibres
                env *= 0.9992f;
                d[i] = lp.Process(N(r)) * (0.45f + env) + lp2.Process(N(r)) * 0.4f;
            }
            return Make("Proc_WaterOnWoolLoop", Loopify(d, fade), 0.55f);
        }

        public static AudioClip[] Splash(int count)
        {
            var clips = new AudioClip[count];
            for (int c = 0; c < count; c++)
            {
                var r = new System.Random(100 + c);
                int len = (int)(Rate * 0.35f);
                var d = new float[len];
                var bp = new BandPass(500f + c * 150f, 3200f);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 11f);
                    d[i] = bp.Process(N(r)) * env;
                }
                clips[c] = Make("Proc_Splash_" + c, d, 0.7f);
            }
            return clips;
        }

        public static AudioClip[] Drip(int count)
        {
            var clips = new AudioClip[count];
            for (int c = 0; c < count; c++)
            {
                int len = (int)(Rate * 0.16f);
                var d = new float[len];
                float f0 = 650f + c * 140f, phase = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float f = f0 * (1f + 1.1f * Mathf.Min(1f, t / 0.035f)); // rising "plip"
                    phase += 2f * Mathf.PI * f / Rate;
                    d[i] = Mathf.Sin(phase) * Mathf.Min(1f, t / 0.002f) * Mathf.Exp(-t * 32f);
                }
                clips[c] = Make("Proc_Drip_" + c, d, 0.5f);
            }
            return clips;
        }

        public static AudioClip[] Squish(int count)
        {
            var clips = new AudioClip[count];
            for (int c = 0; c < count; c++)
            {
                var r = new System.Random(200 + c);
                int len = (int)(Rate * 0.22f);
                var d = new float[len];
                var lp = new LowPass(380f + c * 60f);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Sin(Mathf.Clamp01(t / 0.22f) * Mathf.PI);
                    d[i] = lp.Process(N(r)) * env * env;
                }
                clips[c] = Make("Proc_Squish_" + c, d, 0.6f);
            }
            return clips;
        }

        // ---------------------------------------------------------------- drying

        /// <summary>Warm, steady dryer airflow: low-passed noise + soft motor hum, no high whine.</summary>
        public static AudioClip DryerLoop()
        {
            var r = new System.Random(21);
            int len = Rate * 2, fade = Rate / 4;
            var d = new float[len + fade];
            var lp = new LowPass(1600f);
            var lp2 = new LowPass(2600f);
            float ph1 = 0f, ph2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                ph1 += 2f * Mathf.PI * 118f / Rate;
                ph2 += 2f * Mathf.PI * 236f / Rate;
                float hum = Mathf.Sin(ph1) * 0.10f + Mathf.Sin(ph2) * 0.04f;
                float air = lp.Process(N(r)) * 0.8f + (lp2.Process(N(r)) - lp.Process(0f)) * 0.15f;
                d[i] = air + hum;
            }
            return Make("Proc_DryerLoop", Loopify(d, fade), 0.6f);
        }

        /// <summary>Softer "fwoosh" when the air actually works through wet wool.</summary>
        public static AudioClip AirOnWoolLoop()
        {
            var r = new System.Random(22);
            int len = Rate * 2, fade = Rate / 4;
            var d = new float[len + fade];
            var bp = new BandPass(300f, 1400f);
            var wob = new LowPass(3f);
            for (int i = 0; i < d.Length; i++)
            {
                float m = 0.7f + Mathf.Clamp(wob.Process(N(r)) * 8f, -0.3f, 0.3f);
                d[i] = bp.Process(N(r)) * m;
            }
            return Make("Proc_AirOnWoolLoop", Loopify(d, fade), 0.5f);
        }

        public static AudioClip[] AirPuff(int count)
        {
            var clips = new AudioClip[count];
            for (int c = 0; c < count; c++)
            {
                var r = new System.Random(300 + c);
                int len = (int)(Rate * 0.4f);
                var d = new float[len];
                var bp = new BandPass(250f, 1800f + c * 300f);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)len;
                    float env = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.4f);
                    d[i] = bp.Process(N(r)) * env;
                }
                clips[c] = Make("Proc_AirPuff_" + c, d, 0.55f);
            }
            return clips;
        }

        // ---------------------------------------------------------------- shared

        /// <summary>Soft two-note "tidy" accent for a notably processed area.</summary>
        public static AudioClip Sparkle()
        {
            int len = (int)(Rate * 0.5f);
            var d = new float[len];
            float[] f = { 1318.5f, 1975.5f };
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < f.Length; k++)
                {
                    float tk = t - k * 0.07f;
                    if (tk < 0f) continue;
                    s += Mathf.Sin(2f * Mathf.PI * f[k] * tk) * Mathf.Exp(-tk * 9f) * Mathf.Min(1f, tk / 0.004f);
                }
                d[i] = s;
            }
            return Make("Proc_Sparkle", d, 0.4f);
        }

        /// <summary>Coin clink (sell screen).</summary>
        public static AudioClip Coin()
        {
            int len = (int)(Rate * 0.45f);
            var d = new float[len];
            float[] f = { 2093f, 3136f, 4186f, 5274f };
            float[] a = { 1f, 0.6f, 0.35f, 0.2f };
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < f.Length; k++) s += Mathf.Sin(2f * Mathf.PI * f[k] * t) * a[k] * Mathf.Exp(-t * (8f + k * 4f));
                d[i] = s * Mathf.Min(1f, t / 0.002f);
            }
            return Make("Proc_Coin", d, 0.5f);
        }

        /// <summary>Soft UI tap.</summary>
        public static AudioClip Tap()
        {
            int len = (int)(Rate * 0.06f);
            var d = new float[len];
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 70f);
            }
            return Make("Proc_Tap", d, 0.35f);
        }
    }
}
