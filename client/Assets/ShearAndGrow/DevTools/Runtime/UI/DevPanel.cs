using System.Collections.Generic;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>One page of the Developer Panel. New pages register commands once and build their rows when shown.</summary>
    public abstract class DevTab
    {
        public abstract string Title { get; }
        protected DevPanel Panel { get; private set; }
        internal void Attach(DevPanel p) => Panel = p;
        /// <summary>Called once at startup: register this page's commands in <see cref="DevCommands"/>.</summary>
        public abstract void RegisterCommands();
        /// <summary>Builds the page (called every time it becomes visible – content always reflects current data).</summary>
        public abstract void Build(DevUi ui);
        /// <summary>Updates dynamic labels (~2× per second while visible, and after every command).</summary>
        public virtual void Refresh() { }
        protected DevResult Run(string id, string arg = null) => Panel.Run(id, arg);
    }

    /// <summary>
    /// The in-game Developer Panel (Editor / Development builds only – this assembly does not exist in release builds).
    /// Phone-first: a bottom half-sheet in portrait, a right half in landscape, scrollable pages, large touch targets.
    /// Touches on the panel never reach the farm or the mini-games (their input routers ignore pointers over UI).
    /// Persists across scenes; opened from Settings → "DEV · Developer Panel", the small DEV button, or the Editor menu.
    /// </summary>
    public sealed class DevPanel : MonoBehaviour
    {
        const string FloatingButtonPref = "ShearAndGrow.Dev.ShowDevButton";
        const string FpsPref = "ShearAndGrow.Dev.ShowFps";
        const string LastTabPref = "ShearAndGrow.Dev.LastTab";

        static DevPanel _instance;
        public static DevPanel Instance => _instance;

        readonly List<DevTab> _tabs = new List<DevTab>();
        readonly List<Button> _tabButtons = new List<Button>();
        RectTransform _safe, _root, _list, _tabBar;
        ScrollRect _scroll;
        Text _status, _fpsText;
        GameObject _devButton;
        int _current = -1;
        float _refreshTimer;
        FpsMeter _fps;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool ShowFloatingButton { get => PlayerPrefs.GetInt(FloatingButtonPref, 1) == 1; set { PlayerPrefs.SetInt(FloatingButtonPref, value ? 1 : 0); RefreshDevButton(); } }
        public bool ShowFps { get => PlayerPrefs.GetInt(FpsPref, 0) == 1; set { PlayerPrefs.SetInt(FpsPref, value ? 1 : 0); ApplyFps(); } }
        public FpsMeter Fps => _fps;
        public string CurrentTabTitle => _current >= 0 ? _tabs[_current].Title : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!DevAccess.IsAllowed(out _) || _instance != null) return;
            var go = new GameObject("[DeveloperPanel]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DevPanel>();
        }

        void Awake()
        {
            _fps = gameObject.AddComponent<FpsMeter>();
            gameObject.AddComponent<DevCameraPrefs>();
            gameObject.AddComponent<DevWorldOverlay>();
            _tabs.Add(new EconomyTab());
            _tabs.Add(new SheepTab());
            _tabs.Add(new BuildingsTab());
            _tabs.Add(new TimeTab());
            _tabs.Add(new CameraDisplayTab());
            _tabs.Add(new SaveResetTab());
            _tabs.Add(new DiagnosticsTab());
            foreach (var t in _tabs) { t.Attach(this); t.RegisterCommands(); }
            BuildUi();
            DevCommands.Executed += OnExecuted;
            ScreenLayout.Changed += ApplyLayout;
            ShearAndGrow.Farm.FarmHubController.ActiveChanged += OnHubChanged;
            DevToolsGate.Register(Open);
        }

        void OnHubChanged(ShearAndGrow.Farm.FarmHubController hub) { if (IsOpen) Rebuild(); }

        void OnDestroy()
        {
            DevCommands.Executed -= OnExecuted;
            ScreenLayout.Changed -= ApplyLayout;
            ShearAndGrow.Farm.FarmHubController.ActiveChanged -= OnHubChanged;
            if (_instance == this) _instance = null;
        }

        // ------------------------------------------------------------------ UI shell

        void BuildUi()
        {
            var canvas = UiKit.CreateCanvas(transform, "DevCanvas", 500, out _safe);
            canvas.gameObject.name = "DevCanvas";

            // floating DEV button (small, left edge – away from the farm HUD buttons)
            var db = UiKit.Button(_safe, "DevButton", "DEV", 28, new Color(0.55f, 0.16f, 0.12f, 0.7f), Open, out var dbl);
            dbl.fontStyle = FontStyle.Bold;
            UiKit.Place((RectTransform)db.transform, new Vector2(0f, 0.62f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(96f, 92f));
            db.gameObject.AddComponent<DevDragHandle>(); // drag along the edge if it covers something
            _devButton = db.gameObject;

            _fpsText = UiKit.Text(_safe, "Fps", "", 26, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.7f, 1f));
            UiKit.Place(_fpsText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -6f), new Vector2(380f, 40f));
            UiKit.SoftShadow(_fpsText);

            var bg = UiKit.Image(_safe, "Panel", DevUi.Bg, true);
            _root = bg.rectTransform;

            // header
            var title = UiKit.Text(_root, "Title", "Developer Panel", 36, TextAnchor.MiddleLeft, DevUi.Section);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f); title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.offsetMin = new Vector2(24f, -96f); title.rectTransform.offsetMax = new Vector2(-260f, 0f);
            var close = UiKit.Button(_root, "Close", "✕", 40, DevUi.ButtonColor, Close, out _);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -8f), new Vector2(110f, 84f));
            var mini = UiKit.Button(_root, "Minimise", "–", 44, DevUi.ButtonColor, Close, out _);
            UiKit.Place((RectTransform)mini.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-132f, -8f), new Vector2(110f, 84f));

            // tab bar (horizontal scroll)
            var tabView = UiKit.Image(_root, "TabBar", new Color(0f, 0f, 0f, 0.25f), true);
            var tvr = tabView.rectTransform;
            tvr.anchorMin = new Vector2(0f, 1f); tvr.anchorMax = new Vector2(1f, 1f); tvr.pivot = new Vector2(0.5f, 1f);
            tvr.offsetMin = new Vector2(0f, -196f); tvr.offsetMax = new Vector2(0f, -100f);
            tabView.gameObject.AddComponent<RectMask2D>();
            _tabBar = UiKit.Rect("Tabs", tvr);
            _tabBar.anchorMin = new Vector2(0f, 0f); _tabBar.anchorMax = new Vector2(0f, 1f); _tabBar.pivot = new Vector2(0f, 0.5f);
            _tabBar.offsetMin = _tabBar.offsetMax = Vector2.zero;
            var hl = _tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f; hl.padding = new RectOffset(10, 10, 8, 8);
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            _tabBar.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tabScroll = tabView.gameObject.AddComponent<ScrollRect>();
            tabScroll.content = _tabBar; tabScroll.viewport = tvr; tabScroll.horizontal = true; tabScroll.vertical = false;
            tabScroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                var b = UiKit.Button(_tabBar, "Tab_" + _tabs[i].Title, _tabs[i].Title, 28, DevUi.ButtonColor, () => ShowTab(idx), out _);
                var le = b.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 250f; le.minWidth = 220f;
                _tabButtons.Add(b);
            }

            // content (vertical scroll)
            var view = UiKit.Image(_root, "Viewport", new Color(0f, 0f, 0f, 0.001f), true);
            var vr = view.rectTransform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(16f, 70f); vr.offsetMax = new Vector2(-16f, -204f);
            view.gameObject.AddComponent<RectMask2D>();
            _list = UiKit.Rect("List", vr);
            _list.anchorMin = new Vector2(0f, 1f); _list.anchorMax = new Vector2(1f, 1f); _list.pivot = new Vector2(0.5f, 1f);
            _list.offsetMin = _list.offsetMax = Vector2.zero;
            var vl = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10f; vl.padding = new RectOffset(6, 6, 6, 24);
            vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _list; _scroll.viewport = vr; _scroll.horizontal = false; _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40f;

            // status line
            _status = UiKit.Text(_root, "Status", "Ready · " + DevAccess.BuildType, 26, TextAnchor.MiddleLeft, UiKit.TextMuted);
            _status.rectTransform.anchorMin = new Vector2(0f, 0f); _status.rectTransform.anchorMax = new Vector2(1f, 0f); _status.rectTransform.pivot = new Vector2(0f, 0f);
            _status.rectTransform.offsetMin = new Vector2(24f, 8f); _status.rectTransform.offsetMax = new Vector2(-24f, 64f);
            _status.resizeTextForBestFit = true; _status.resizeTextMinSize = 18; _status.resizeTextMaxSize = 26;

            _root.gameObject.SetActive(false);
            ApplyLayout();
            RefreshDevButton();
            ApplyFps();
        }

        /// <summary>Portrait: bottom ~60 % (farm stays visible above). Landscape: right half (farm visible on the left).</summary>
        void ApplyLayout()
        {
            if (_root == null) return;
            if (ScreenLayout.IsLandscape) { _root.anchorMin = new Vector2(0.5f, 0f); _root.anchorMax = new Vector2(1f, 1f); }
            else { _root.anchorMin = new Vector2(0f, 0f); _root.anchorMax = new Vector2(1f, 0.6f); }
            _root.offsetMin = Vector2.zero; _root.offsetMax = Vector2.zero;
        }

        void RefreshDevButton() { if (_devButton != null) _devButton.SetActive(!IsOpen && ShowFloatingButton); }
        void ApplyFps() { if (_fpsText != null) _fpsText.gameObject.SetActive(ShowFps); if (_fps != null) _fps.enabled = ShowFps || IsOpen; }

        // ------------------------------------------------------------------ open / close / tabs

        public void Open()
        {
            if (!DevAccess.IsAllowed(out _)) return;
            EnsureEventSystem();
            _root.gameObject.SetActive(true);
            RefreshDevButton();
            ApplyFps();
            ShowTab(_current >= 0 ? _current : Mathf.Clamp(PlayerPrefs.GetInt(LastTabPref, 0), 0, _tabs.Count - 1));
        }

        public void Close()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            RefreshDevButton();
            ApplyFps();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void ShowTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _current = index;
            PlayerPrefs.SetInt(LastTabPref, index);
            for (int i = 0; i < _tabButtons.Count; i++) _tabButtons[i].image.color = i == index ? new Color(0.78f, 0.47f, 0.18f, 1f) : DevUi.ButtonColor;
            Rebuild();
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Rebuilds the visible page from current data (after scene changes / big operations).</summary>
        public void Rebuild()
        {
            if (_current < 0 || _list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            _list.DetachChildren();
            _tabs[_current].Build(new DevUi(_list));
            _tabs[_current].Refresh();
        }

        public DevResult Run(string id, string arg = null) => DevCommands.Run(id, arg);

        void OnExecuted(DevCommand c, DevResult r)
        {
            if (_status != null)
            {
                _status.text = (r.Ok ? "✓ " : "✗ ") + r.Message;
                _status.color = r.Ok ? new Color(0.7f, 1f, 0.7f, 1f) : new Color(1f, 0.65f, 0.55f, 1f);
            }
            if (IsOpen && _current >= 0) _tabs[_current].Refresh();
        }

        public void SetStatus(string text, bool ok = true)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = ok ? UiKit.TextMuted : new Color(1f, 0.65f, 0.55f, 1f);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem (DevTools)");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        void Update()
        {
            if (_fpsText != null && _fpsText.gameObject.activeSelf && _fps.TextChanged) _fpsText.text = _fps.Text;
            if (IsOpen)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; } // Android back / Esc
                _refreshTimer -= Time.unscaledDeltaTime;
                if (_refreshTimer <= 0f) { _refreshTimer = 0.5f; if (_current >= 0) _tabs[_current].Refresh(); }
            }
        }
    }

    /// <summary>
    /// Light FPS meter: exponential moving average of unscaled frame time, text rebuilt twice per second only.
    /// Disabled (zero cost) unless the FPS overlay or the panel is visible.
    /// </summary>
    public sealed class FpsMeter : MonoBehaviour
    {
        float _ema = 1f / 60f, _next;
        float _worst, _worstWindow;
        public float Fps => 1f / Mathf.Max(1e-4f, _ema);
        public string Text { get; private set; } = "";
        public bool TextChanged { get; private set; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 1f) return;
            _ema = Mathf.Lerp(_ema, dt, 0.05f);
            _worstWindow = Mathf.Max(_worstWindow, dt);
            TextChanged = false;
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.5f;
                _worst = _worstWindow; _worstWindow = 0f;
                Text = Mathf.RoundToInt(Fps) + " FPS  ·  worst " + Mathf.RoundToInt(_worst * 1000f) + " ms";
                TextChanged = true;
            }
        }
    }
}
