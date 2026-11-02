using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// PLACEHOLDER ICONS drawn at runtime with signed-distance shapes (anti-aliased, cached): scissors, droplet,
    /// dryer air, coin, wool puff. Replace with authored sprites later.
    /// </summary>
    public static class ProceduralIcons
    {
        public enum Kind { Scissors, Droplet, Air, Coin, Wool, Check }

        static readonly Dictionary<(Kind, int), Sprite> Cache = new Dictionary<(Kind, int), Sprite>();
        const int Size = 128;

        public static Sprite Get(Kind kind, Color tint)
        {
            int key = ((int)(tint.r * 255) << 24) ^ ((int)(tint.g * 255) << 16) ^ ((int)(tint.b * 255) << 8);
            if (Cache.TryGetValue((kind, key), out var s) && s != null) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon_" + kind, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    float d = Dist(kind, p, out float shade);
                    float a = Mathf.Clamp01(0.5f - d * Size * 0.5f);
                    Color c = tint * shade;
                    c.a = a;
                    px[y * Size + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            Cache[(kind, key)] = s;
            return s;
        }

        static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;
        static float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Dist(Kind k, Vector2 p, out float shade)
        {
            shade = 1f;
            switch (k)
            {
                case Kind.Scissors:
                {
                    float d = Mathf.Min(Ring(p, new Vector2(-0.38f, -0.48f), 0.2f, 0.06f), Ring(p, new Vector2(0.38f, -0.48f), 0.2f, 0.06f));
                    d = Mathf.Min(d, Seg(p, new Vector2(-0.25f, -0.32f), new Vector2(0.42f, 0.78f), 0.075f));
                    d = Mathf.Min(d, Seg(p, new Vector2(0.25f, -0.32f), new Vector2(-0.42f, 0.78f), 0.075f));
                    d = Mathf.Min(d, Circle(p, new Vector2(0f, 0.05f), 0.08f));
                    return d;
                }
                case Kind.Droplet:
                {
                    // teardrop: circle + cone to a tip
                    float c = Circle(p, new Vector2(0f, -0.25f), 0.52f);
                    float cone = Mathf.Max(Mathf.Abs(p.x) - (0.82f - p.y) * 0.52f, -p.y - 0.1f);
                    cone = Mathf.Max(cone, p.y - 0.85f);
                    float d = Mathf.Min(c, cone);
                    if (Circle(p, new Vector2(-0.2f, -0.12f), 0.13f) < 0f) shade = 1.35f; // highlight
                    return d;
                }
                case Kind.Air:
                {
                    float d = Seg(p, new Vector2(-0.8f, 0.45f), new Vector2(0.35f, 0.45f), 0.07f);
                    d = Mathf.Min(d, Ring(p, new Vector2(0.35f, 0.6f), 0.15f, 0.07f) + (p.y < 0.6f && p.x < 0.35f ? 1f : 0f));
                    d = Mathf.Min(d, Seg(p, new Vector2(-0.8f, 0f), new Vector2(0.6f, 0f), 0.07f));
                    d = Mathf.Min(d, Seg(p, new Vector2(-0.8f, -0.45f), new Vector2(0.2f, -0.45f), 0.07f));
                    d = Mathf.Min(d, Ring(p, new Vector2(0.2f, -0.6f), 0.15f, 0.07f) + (p.y > -0.6f && p.x < 0.2f ? 1f : 0f));
                    return d;
                }
                case Kind.Coin:
                {
                    float d = Circle(p, Vector2.zero, 0.82f);
                    if (Ring(p, Vector2.zero, 0.6f, 0.05f) < 0f) shade = 0.78f;
                    if (Circle(p, new Vector2(-0.3f, 0.35f), 0.16f) < 0f) shade = 1.3f;
                    return d;
                }
                case Kind.Wool:
                {
                    float d = Circle(p, new Vector2(0f, 0.18f), 0.42f);
                    d = Mathf.Min(d, Circle(p, new Vector2(-0.45f, -0.15f), 0.36f));
                    d = Mathf.Min(d, Circle(p, new Vector2(0.45f, -0.15f), 0.36f));
                    d = Mathf.Min(d, Circle(p, new Vector2(0f, -0.38f), 0.38f));
                    if (Circle(p, new Vector2(-0.12f, 0.32f), 0.14f) < 0f) shade = 1.2f;
                    return d;
                }
                default: // check mark
                    return Mathf.Min(Seg(p, new Vector2(-0.6f, 0f), new Vector2(-0.15f, -0.5f), 0.12f), Seg(p, new Vector2(-0.15f, -0.5f), new Vector2(0.65f, 0.55f), 0.12f));
            }
        }
    }
}
