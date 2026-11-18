using System;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Builds Developer Panel rows into a vertical list (same uGUI-from-code style as the game's UiKit). All controls
    /// have phone-sized touch targets (≥ 88 units high ≈ 9 mm) and readable text.
    /// </summary>
    public sealed class DevUi
    {
        public const float RowHeight = 92f;
        public static readonly Color Bg = new Color(0.10f, 0.08f, 0.07f, 1f);
        public static readonly Color ButtonColor = new Color(0.30f, 0.22f, 0.17f, 1f);
        public static readonly Color Danger = new Color(0.55f, 0.16f, 0.12f, 1f);
        public static readonly Color Good = new Color(0.20f, 0.42f, 0.30f, 1f);
        public static readonly Color Section = new Color(0.98f, 0.66f, 0.30f, 1f);

        readonly RectTransform _list;
        public RectTransform List => _list;

        public DevUi(RectTransform list) { _list = list; }

        RectTransform Row(float height)
        {
            var rt = UiKit.Rect("Row", _list);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            return rt;
        }

        public Text Header(string text)
        {
            var row = Row(64f);
            var t = UiKit.Text(row, "Header", text, 34, TextAnchor.LowerLeft, Section);
            t.fontStyle = FontStyle.Bold;
            UiKit.Stretch(t.rectTransform);
            return t;
        }

        /// <summary>Multi-line info text; height grows with content.</summary>
        public Text Label(string text, int size = 28, Color? color = null)
        {
            var rt = UiKit.Rect("Label", _list);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiKit.Font; t.fontSize = size; t.text = text; t.color = color ?? UiKit.TextColor;
            t.alignment = TextAnchor.UpperLeft; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }

        public Button Button(string label, Action onClick, Color? color = null) => Buttons((label, onClick, color))[0];

        /// <summary>A row of equally wide buttons.</summary>
        public Button[] Buttons(params (string label, Action onClick, Color? color)[] items)
        {
            var row = Row(RowHeight);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = true;
            var result = new Button[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                var b = UiKit.Button(row, "Btn_" + it.label, it.label, 28, it.color ?? ButtonColor, it.onClick, out var t);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 18; t.resizeTextMaxSize = 28;
                result[i] = b;
            }
            return result;
        }

        public Button[] Buttons(params (string label, Action onClick)[] items)
        {
            var conv = new (string, Action, Color?)[items.Length];
            for (int i = 0; i < items.Length; i++) conv[i] = (items[i].label, items[i].onClick, null);
            return Buttons(conv);
        }

        /// <summary>Input field + action button in one row. Returns the field.</summary>
        public InputField InputWithButton(string placeholder, InputField.ContentType type, string buttonLabel, Action<string> onSubmit, Color? color = null)
        {
            var row = Row(RowHeight);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 10f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var field = CreateInput(row, placeholder, type);
            field.gameObject.AddComponent<LayoutElement>().flexibleWidth = 2f;
            var b = UiKit.Button(row, "Btn_" + buttonLabel, buttonLabel, 28, color ?? ButtonColor, () => onSubmit(field.text), out var t);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 18; t.resizeTextMaxSize = 28;
            b.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return field;
        }

        public static InputField CreateInput(Transform parent, string placeholder, InputField.ContentType type)
        {
            var bg = UiKit.Image(parent, "Input", new Color(1f, 1f, 1f, 0.10f), true);
            var field = bg.gameObject.AddComponent<InputField>();
            var text = UiKit.Text(bg.transform, "Text", "", 32, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(18f, 4f); text.rectTransform.offsetMax = new Vector2(-18f, -4f);
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = UiKit.Text(bg.transform, "Placeholder", placeholder, 30, TextAnchor.MiddleLeft, UiKit.TextMuted);
            ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one;
            ph.rectTransform.offsetMin = new Vector2(18f, 4f); ph.rectTransform.offsetMax = new Vector2(-18f, -4f);
            ph.fontStyle = FontStyle.Italic;
            field.textComponent = text; field.placeholder = ph; field.targetGraphic = bg;
            field.contentType = type; field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        /// <summary>Labelled slider (label + live value on top, slider below). Returns the slider.</summary>
        public Slider Slider(string label, float min, float max, float value, Func<float, string> format, Action<float> onChanged, out Text valueText)
        {
            var row = Row(128f);
            var title = UiKit.Text(row, "Label", label, 28, TextAnchor.UpperLeft);
            title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(0.6f, 1f); title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.offsetMin = new Vector2(4f, -44f); title.rectTransform.offsetMax = new Vector2(0f, 0f);
            var val = UiKit.Text(row, "Value", format(value), 28, TextAnchor.UpperRight, Section);
            val.rectTransform.anchorMin = new Vector2(0.6f, 1f); val.rectTransform.anchorMax = new Vector2(1f, 1f); val.rectTransform.pivot = new Vector2(1f, 1f);
            val.rectTransform.offsetMin = new Vector2(0f, -44f); val.rectTransform.offsetMax = new Vector2(-4f, 0f);
            valueText = val;

            var sRt = UiKit.Rect("Slider", row);
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(1f, 0f); sRt.pivot = new Vector2(0.5f, 0f);
            sRt.offsetMin = new Vector2(20f, 6f); sRt.offsetMax = new Vector2(-20f, 76f);
            var back = UiKit.Image(sRt, "Background", new Color(1f, 1f, 1f, 0.12f), true);
            back.rectTransform.anchorMin = new Vector2(0f, 0.38f); back.rectTransform.anchorMax = new Vector2(1f, 0.62f);
            back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
            var fillArea = UiKit.Rect("Fill Area", sRt);
            fillArea.anchorMin = new Vector2(0f, 0.38f); fillArea.anchorMax = new Vector2(1f, 0.62f);
            fillArea.offsetMin = new Vector2(10f, 0f); fillArea.offsetMax = new Vector2(-10f, 0f);
            var fill = UiKit.Image(fillArea, "Fill", Section);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = UiKit.Rect("Handle Slide Area", sRt);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(30f, 0f); handleArea.offsetMax = new Vector2(-30f, 0f);
            var handle = UiKit.Image(handleArea, "Handle", UiKit.TextColor, true);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f); handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(60f, 0f);

            var s = sRt.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            s.onValueChanged.AddListener(v => { val.text = format(v); onChanged(v); });
            return s;
        }

        public Toggle Toggle(string label, bool value, Action<bool> onChanged)
        {
            var row = Row(RowHeight);
            var bg = UiKit.Image(row, "ToggleBg", new Color(1f, 1f, 1f, 0.06f), true);
            UiKit.Stretch(bg.rectTransform);
            var box = UiKit.Image(bg.transform, "Box", new Color(1f, 1f, 1f, 0.18f));
            UiKit.Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(60f, 60f));
            var check = UiKit.Image(box.transform, "Check", Section);
            UiKit.Place(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
            var t = UiKit.Text(bg.transform, "Label", label, 30, TextAnchor.MiddleLeft);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(96f, 0f); t.rectTransform.offsetMax = new Vector2(-12f, 0f);
            var toggle = bg.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bg; toggle.graphic = check;
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
        }

        public void Space(float h = 16f) => Row(h);
    }
}
