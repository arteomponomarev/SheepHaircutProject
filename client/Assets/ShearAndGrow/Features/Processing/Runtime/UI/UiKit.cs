using System;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Tiny uGUI-from-code toolkit with the warm palette of the existing shearing HUD (legacy Text + built-in font,
    /// Screen Space Overlay, Scale With Screen Size 1080×1920, safe area). Used by the processing HUD and the Hub.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Panel = new Color(0.18f, 0.13f, 0.10f, 0.88f);
        public static readonly Color PanelLight = new Color(0.32f, 0.24f, 0.18f, 0.95f);
        public static readonly Color Accent = new Color(0.98f, 0.66f, 0.30f, 1f);
        public static readonly Color AccentDark = new Color(0.78f, 0.47f, 0.18f, 1f);
        public static readonly Color TextColor = new Color(1f, 0.96f, 0.9f, 1f);
        public static readonly Color TextMuted = new Color(1f, 0.96f, 0.9f, 0.45f);
        public static readonly Color Disabled = new Color(0.40f, 0.36f, 0.33f, 0.65f);
        public static readonly Color Water = new Color(0.45f, 0.72f, 0.95f, 1f);
        public static readonly Color Air = new Color(0.62f, 0.86f, 0.80f, 1f);
        public static readonly Color Coin = new Color(1f, 0.82f, 0.30f, 1f);

        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateCanvas(Transform parent, string name, int order, out RectTransform safeRoot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            safeRoot = Rect("SafeArea", go.transform);
            Stretch(safeRoot);
            go.AddComponent<SafeAreaFitter>().Target = safeRoot;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Point-anchored placement (anchorMin == anchorMax) with explicit size.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static Image Image(Transform parent, string name, Color c, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(Transform parent, string name, string text, int size, TextAnchor align, Color? color = null)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.text = text; t.alignment = align;
            t.color = color ?? TextColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string name, string label, int size, Color bg, Action onClick, out Text text)
        {
            var img = Image(parent, name, bg, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.6f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            text = Text(img.transform, "Label", label, size, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return b;
        }

        public static Shadow SoftShadow(Graphic g)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.45f);
            s.effectDistance = new Vector2(2f, -3f);
            return s;
        }
    }
}
