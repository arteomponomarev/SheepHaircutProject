using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// On-device haptics test panel: profiles, strength, test buttons for every event, enable toggle and diagnostics.
    /// Exists only in the Editor and Development builds; in release builds the UI code is compiled out and the
    /// component removes itself, so regular players never see it.
    /// </summary>
    public sealed class HapticsDebugPanel : MonoBehaviour
    {
        [SerializeField] private ShearingHapticsController _haptics;
        [SerializeField] private float _testDensity = 0.7f;

        GameObject _panel;
        UnityEngine.UI.Text _info;
        UnityEngine.UI.Text _strengthLabel;
        UnityEngine.UI.Text _enabledLabel;
        UnityEngine.UI.Slider _strength;
        readonly UnityEngine.UI.Image[] _profileButtons = new UnityEngine.UI.Image[3];
        Font _font;
        bool _holdContinuous;
        float _infoTimer;
        readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(512);

        static readonly Color Panel = new Color(0.1f, 0.08f, 0.07f, 0.94f);
        static readonly Color ButtonColor = new Color(0.32f, 0.24f, 0.18f, 1f);
        static readonly Color Selected = new Color(1f, 0.62f, 0.25f, 1f);
        static readonly Color TextColor = new Color(1f, 0.96f, 0.9f, 1f);

        void Start()
        {
            // Debug.isDebugBuild is true in the Editor and Development builds, false in release builds:
            // regular players never get the panel or its button.
            if (!Debug.isDebugBuild) { Destroy(this); return; }
            if (_haptics == null || _haptics.Manager == null) { enabled = false; return; }
            Build();
        }

        HapticsManager M => _haptics.Manager;

        void Update()
        {
            if (_holdContinuous && M != null) M.SetCutting(_testDensity);
            if (_panel == null || !_panel.activeSelf) return;
            _infoTimer -= Time.unscaledDeltaTime;
            if (_infoTimer <= 0f) { _infoTimer = 0.5f; RefreshInfo(); }
        }

        void RefreshInfo()
        {
            var p = M.Platform;
            _sb.Clear();
            _sb.Append("Platform: ").Append(p.PlatformName).Append('\n');
            _sb.Append("Haptics supported: ").Append(p.IsSupported ? "YES" : "NO").Append("   Enabled: ").Append(M.Enabled ? "ON" : "OFF").Append('\n');
            _sb.Append("Profile: ").Append(M.Profile != null ? M.Profile.DisplayName : "-").Append("   Strength x").Append(M.Strength.ToString("0.00")).Append('\n');
            _sb.Append("Native continuous: ").Append(p.HasNativeContinuous ? "yes" : "no (scheduled ticks)").Append('\n');
            _sb.Append("Native calls: ").Append(p.NativeCallCount).Append("   events: ").Append(M.SemanticEvents).Append("   rate-limited: ").Append(M.SuppressedEvents).Append('\n');
            _sb.Append("Capabilities: ").Append(p.Capabilities).Append('\n');
            string err = p.LastError;
            _sb.Append("Last error: ").Append(string.IsNullOrEmpty(err) ? "none" : err);
            _info.text = _sb.ToString();
            for (int i = 0; i < _profileButtons.Length; i++) _profileButtons[i].color = i == M.ProfileIndex ? Selected : ButtonColor;
            _enabledLabel.text = M.Enabled ? "Haptics: ON" : "Haptics: OFF";
        }

        // ------------------------------------------------------------------ UI construction

        void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("HapticsDebugCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var safe = Rect("SafeArea", canvasGo.transform);
            Rect sa = Screen.safeArea;
            safe.anchorMin = new Vector2(sa.xMin / Mathf.Max(1, Screen.width), sa.yMin / Mathf.Max(1, Screen.height));
            safe.anchorMax = new Vector2(sa.xMax / Mathf.Max(1, Screen.width), sa.yMax / Mathf.Max(1, Screen.height));
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            // Open button: top-right, below the existing toggle row / wool selector.
            var open = Button(safe, "Haptics ⚙", 30, () => { _panel.SetActive(!_panel.activeSelf); _infoTimer = 0f; });
            Place((RectTransform)open.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -345f), new Vector2(240f, 72f));

            var panel = Rect("HapticsTestPanel", safe);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(960f, 1240f));
            Image(panel.gameObject, Panel);
            var layout = panel.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 16f;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            _panel = panel.gameObject;

            Fixed(Label(panel, "Haptics Test (development build only)", 38, TextAnchor.MiddleCenter).gameObject, 56f);
            _info = Label(panel, "", 24, TextAnchor.UpperLeft);
            Fixed(_info.gameObject, 300f);

            var profiles = Row(panel, 96f);
            string[] names = { "Soft", "Satisfying", "Powerful" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = Button(profiles, names[i], 32, () => { M.SetProfile(idx); RefreshInfo(); });
                _profileButtons[i] = b.GetComponent<UnityEngine.UI.Image>();
            }

            _strengthLabel = Label(panel, "Strength", 28, TextAnchor.MiddleLeft);
            Fixed(_strengthLabel.gameObject, 40f);
            _strength = Slider(panel, 0f, 1.5f, M.Strength);
            _strength.onValueChanged.AddListener(v => { M.SetStrength(v); _strengthLabel.text = "Strength x" + v.ToString("0.00"); });
            _strengthLabel.text = "Strength x" + M.Strength.ToString("0.00");

            var tests = Row(panel, 110f);
            Button(tests, "First contact", 28, () => M.PlayContact());
            Button(tests, "Big clump", 28, () => M.PlayChunk(1.8f));
            Button(tests, "Completion", 28, () => M.PlayCompletion());

            var hold = Row(panel, 130f);
            var holdBtn = Button(hold, "HOLD: continuous cutting", 30, null);
            var trigger = holdBtn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerDown, () => _holdContinuous = true);
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerUp, StopHold);
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerExit, StopHold);

            var bottom = Row(panel, 96f);
            var toggle = Button(bottom, "Haptics: ON", 30, () => { M.SetEnabled(!M.Enabled); RefreshInfo(); });
            _enabledLabel = toggle.GetComponentInChildren<UnityEngine.UI.Text>();
            Button(bottom, "Stop all", 30, () => { StopHold(); M.StopAll(); });
            Button(bottom, "Close", 30, () => { StopHold(); _panel.SetActive(false); });

            _panel.SetActive(false);
        }

        void StopHold()
        {
            if (!_holdContinuous) return;
            _holdContinuous = false;
            M.StopCutting(); // release stops the texture immediately
        }

        static void AddTrigger(UnityEngine.EventSystems.EventTrigger t, UnityEngine.EventSystems.EventTriggerType type, System.Action a)
        {
            var e = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            t.triggers.Add(e);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static void Fixed(GameObject go, float height)
        {
            var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
            le.minHeight = le.preferredHeight = height;
        }

        static UnityEngine.UI.Image Image(GameObject go, Color c)
        {
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = c;
            return img;
        }

        UnityEngine.UI.Text Label(Transform parent, string text, int size, TextAnchor align)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = _font; t.fontSize = size; t.text = text; t.alignment = align; t.color = TextColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        RectTransform Row(Transform parent, float height)
        {
            var rt = Rect("Row", parent);
            Fixed(rt.gameObject, height);
            var h = rt.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            return rt;
        }

        UnityEngine.UI.Button Button(Transform parent, string label, int size, System.Action onClick)
        {
            var rt = Rect("Button_" + label, parent);
            var img = Image(rt.gameObject, ButtonColor);
            var b = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Label(rt, label, size, TextAnchor.MiddleCenter);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return b;
        }

        UnityEngine.UI.Slider Slider(Transform parent, float min, float max, float value)
        {
            var root = Rect("StrengthSlider", parent);
            Fixed(root.gameObject, 64f);
            var bg = Rect("Background", root);
            bg.anchorMin = new Vector2(0f, 0.35f); bg.anchorMax = new Vector2(1f, 0.65f);
            Image(bg.gameObject, ButtonColor).raycastTarget = false;
            var fillArea = Rect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.35f); fillArea.anchorMax = new Vector2(1f, 0.65f);
            var fill = Rect("Fill", fillArea);
            Image(fill.gameObject, Selected).raycastTarget = false;
            var handleArea = Rect("Handle Slide Area", root);
            handleArea.offsetMin = new Vector2(24f, 0f); handleArea.offsetMax = new Vector2(-24f, 0f);
            var handle = Rect("Handle", handleArea);
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(48f, 0f);
            var handleImg = Image(handle.gameObject, TextColor);
            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = handleImg;
            s.minValue = min; s.maxValue = max; s.value = value;
            return s;
        }
    }
}
