using System.Collections;
using System.Collections.Generic;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// One bottom sheet at a time (≤ ~40% of the screen, the selected building is framed above it). Content is
    /// rebuilt on open from current data – cheap (a few UI elements), nothing updates per frame.
    /// </summary>
    public sealed class FarmPanels : MonoBehaviour
    {
        const float SheetHeight = 720f;
        const float MaxSheetHeight = 1500f;
        const float SideSheetWidth = 940f;
        const float TopReserve = 150f; // top HUD bar stays visible above any sheet

        FarmHubController _hub;
        RectTransform _safe, _sheet, _content, _list;
        Text _title, _subtitle;
        Coroutine _anim;
        readonly List<GameObject> _rows = new List<GameObject>();
        string _openId;
        Button _washButton, _dryButton;
        bool _landscape;
        RectTransform _grabber, _close;

        public bool IsOpen => _sheet != null && _sheet.gameObject.activeSelf;
        public string OpenId => IsOpen ? _openId : null;
        /// <summary>Viewport Y at which the selected building should sit while the sheet is open (portrait).</summary>
        public float FocusViewportY => 0.66f;
        /// <summary>Viewport position for the selected building: above the bottom sheet (portrait) / left of the side sheet (landscape).</summary>
        public Vector2 FocusViewport => _landscape ? new Vector2(0.3f, 0.45f) : new Vector2(0.5f, FocusViewportY);
        public bool IsSideSheet => _landscape;

        public void Build(FarmHubController hub, RectTransform safe)
        {
            _hub = hub;
            _safe = safe;
            var sheet = UiKit.Image(safe, "BottomSheet", UiKit.Panel, true);
            _sheet = sheet.rectTransform;
            var accent = UiKit.Image(_sheet, "Grabber", new Color(1f, 1f, 1f, 0.25f));
            _grabber = accent.rectTransform;
            UiKit.Place(accent.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(120f, 10f));
            _title = UiKit.Text(_sheet, "Title", "", 56, TextAnchor.MiddleLeft);
            _title.rectTransform.anchorMin = new Vector2(0f, 1f); _title.rectTransform.anchorMax = new Vector2(1f, 1f); _title.rectTransform.pivot = new Vector2(0f, 1f);
            _title.rectTransform.offsetMin = new Vector2(40f, -116f); _title.rectTransform.offsetMax = new Vector2(-150f, -36f);
            _title.resizeTextForBestFit = true; _title.resizeTextMinSize = 34; _title.resizeTextMaxSize = 56;
            _subtitle = UiKit.Text(_sheet, "Subtitle", "", 32, TextAnchor.UpperLeft, UiKit.TextMuted);
            _subtitle.rectTransform.anchorMin = new Vector2(0f, 1f); _subtitle.rectTransform.anchorMax = new Vector2(1f, 1f); _subtitle.rectTransform.pivot = new Vector2(0f, 1f);
            _subtitle.rectTransform.offsetMin = new Vector2(40f, -206f); _subtitle.rectTransform.offsetMax = new Vector2(-40f, -116f);
            var close = UiKit.Button(_sheet, "Close", "✕", 50, UiKit.PanelLight, () => _hub.ClosePanels(), out _);
            _close = (RectTransform)close.transform;
            UiKit.Place(_close, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(110f, 110f));

            // scrollable rows: a long barn list or a small landscape screen never pushes actions off-screen
            _content = UiKit.Rect("Content", _sheet);
            _content.anchorMin = new Vector2(0f, 0f); _content.anchorMax = new Vector2(1f, 1f);
            _content.offsetMin = new Vector2(30f, 30f); _content.offsetMax = new Vector2(-30f, -220f);
            var viewport = _content.gameObject.AddComponent<Image>();
            viewport.color = new Color(0f, 0f, 0f, 0.001f); viewport.raycastTarget = true;
            _content.gameObject.AddComponent<RectMask2D>();
            _list = UiKit.Rect("List", _content);
            _list.anchorMin = new Vector2(0f, 1f); _list.anchorMax = new Vector2(1f, 1f); _list.pivot = new Vector2(0.5f, 1f);
            _list.offsetMin = Vector2.zero; _list.offsetMax = Vector2.zero;
            var col = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 16f; col.childControlWidth = col.childControlHeight = true; col.childForceExpandWidth = true; col.childForceExpandHeight = false;
            col.childAlignment = TextAnchor.UpperCenter;
            var fit = _list.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = _content.gameObject.AddComponent<ScrollRect>();
            scroll.content = _list; scroll.viewport = _content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
            _sheet.gameObject.SetActive(false);

            ApplyOrientation(ShearAndGrow.Economy.ScreenLayout.IsLandscape, true);
            ShearAndGrow.Economy.ScreenLayout.Changed += OnScreenChanged;
        }

        void OnDestroy() => ShearAndGrow.Economy.ScreenLayout.Changed -= OnScreenChanged;

        void OnScreenChanged() => ApplyOrientation(ShearAndGrow.Economy.ScreenLayout.IsLandscape, false);

        /// <summary>
        /// Portrait: bottom sheet (full width, height grows with rows). Landscape: side sheet on the right (full height
        /// below the top HUD) so the farm stays visible on the left. An open sheet keeps its content on rotation.
        /// </summary>
        void ApplyOrientation(bool landscape, bool force)
        {
            if (!force && landscape == _landscape) { FitHeight(); return; }
            _landscape = landscape;
            if (_anim != null) { StopCoroutine(_anim); _anim = null; }
            if (landscape)
            {
                _sheet.anchorMin = new Vector2(1f, 0f); _sheet.anchorMax = new Vector2(1f, 1f); _sheet.pivot = new Vector2(1f, 0.5f);
                _sheet.offsetMin = new Vector2(-SideSheetWidth - 16f, 16f); _sheet.offsetMax = new Vector2(-16f, -TopReserve);
                _grabber.gameObject.SetActive(false);
            }
            else
            {
                _sheet.anchorMin = new Vector2(0f, 0f); _sheet.anchorMax = new Vector2(1f, 0f); _sheet.pivot = new Vector2(0.5f, 0f);
                _sheet.offsetMin = new Vector2(16f, 0f); _sheet.offsetMax = new Vector2(-16f, SheetHeight);
                _grabber.gameObject.SetActive(true);
                FitHeight();
            }
            _shownPos = _sheet.anchoredPosition; // offsets above define the open position
            if (IsOpen && _closing) _sheet.gameObject.SetActive(false);
            _closing = false;
        }

        Vector2 _shownPos;
        bool _closing;

        /// <summary>Hidden (slid out) position: below the screen (portrait) / right of the screen (landscape).</summary>
        Vector2 HiddenPos => _landscape ? _shownPos + new Vector2(SideSheetWidth + 40f, 0f) : _shownPos - new Vector2(0f, CurrentHeight + 20f);

        void Open(string id, string title, string subtitle)
        {
            foreach (var r in _rows) Destroy(r);
            _rows.Clear();
            if (!_landscape) _sheet.offsetMax = new Vector2(-16f, SheetHeight);
            _list.anchoredPosition = Vector2.zero; // scroll to top
            _washButton = _dryButton = null;
            _cProgress = null; _cRemaining = null; _cFill = null; _cText = null;
            _openId = id;
            _title.text = title;
            _subtitle.text = subtitle;
            _sheet.gameObject.SetActive(true);
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Slide(true));
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (_anim != null) StopCoroutine(_anim);
            _closing = true;
            _anim = StartCoroutine(Slide(false));
            _openId = null;
        }

        IEnumerator Slide(bool show)
        {
            float a = 0f;
            Vector2 from = show ? HiddenPos : _sheet.anchoredPosition, to = show ? _shownPos : HiddenPos;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.22f);
                float e = 1f - Mathf.Pow(1f - a, 3f);
                _sheet.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                yield return null;
            }
            if (!show) { _sheet.gameObject.SetActive(false); _closing = false; }
            _anim = null;
        }

        GameObject Row(float height)
        {
            var rt = UiKit.Rect("Row", _list);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            _rows.Add(rt.gameObject);
            FitHeight();
            return rt.gameObject;
        }

        /// <summary>
        /// Portrait: the sheet grows with its rows (barn with 4 sheep + actions) up to ~78 % of the reference height and
        /// never above the top HUD; anything beyond scrolls. Landscape: fixed full-height side sheet (rows scroll).
        /// </summary>
        void FitHeight()
        {
            if (_landscape || _sheet == null) return;
            float sum = 0f;
            foreach (var r in _rows) { var le = r != null ? r.GetComponent<LayoutElement>() : null; if (le != null) sum += le.preferredHeight + 16f; }
            float available = _safe != null && _safe.rect.height > 1f ? _safe.rect.height - TopReserve : MaxSheetHeight;
            float max = Mathf.Max(SheetHeight * 0.6f, Mathf.Min(MaxSheetHeight, available));
            float h = Mathf.Clamp(260f + sum, Mathf.Min(SheetHeight, max), max);
            _sheet.offsetMax = new Vector2(-16f, h);
        }

        float CurrentHeight => _sheet.offsetMax.y;

        Button ActionRow(string title, string sub, ProceduralIcons.Kind icon, Color color, bool enabled, System.Action onClick)
        {
            var row = Row(150f);
            var b = UiKit.Button(row.transform, "Action", "", 10, UiKit.PanelLight, onClick, out var lbl);
            lbl.gameObject.SetActive(false);
            UiKit.Stretch((RectTransform)b.transform);
            var ic = UiKit.Image(b.transform, "Icon", Color.white);
            ic.sprite = ProceduralIcons.Get(icon, color);
            UiKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(96f, 96f));
            var t = UiKit.Text(b.transform, "Title", title, 46, TextAnchor.LowerLeft);
            t.rectTransform.anchorMin = new Vector2(0f, 0.5f); t.rectTransform.anchorMax = new Vector2(1f, 0.5f); t.rectTransform.pivot = new Vector2(0f, 0f);
            t.rectTransform.offsetMin = new Vector2(140f, 0f); t.rectTransform.offsetMax = new Vector2(-100f, 66f);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 30; t.resizeTextMaxSize = 46;
            var s = UiKit.Text(b.transform, "Sub", sub, 30, TextAnchor.UpperLeft, UiKit.TextMuted);
            s.rectTransform.anchorMin = new Vector2(0f, 0.5f); s.rectTransform.anchorMax = new Vector2(1f, 0.5f); s.rectTransform.pivot = new Vector2(0f, 1f);
            s.rectTransform.offsetMin = new Vector2(140f, -50f); s.rectTransform.offsetMax = new Vector2(-100f, -2f);
            s.resizeTextForBestFit = true; s.resizeTextMinSize = 22; s.resizeTextMaxSize = 30;
            var arrow = UiKit.Text(b.transform, "Arrow", enabled ? "›" : "", 80, TextAnchor.MiddleCenter, color);
            UiKit.Place(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 6f), new Vector2(80f, 110f));
            b.interactable = enabled;
            var cg = b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = enabled ? 1f : 0.55f;
            return b;
        }

        // ------------------------------------------------------------------ panels

        public void ShowHouse(FarmInteractable house, int level)
        {
            Open(house.BuildingId, house.DisplayName, "Level " + level + "  ·  The heart of your farm.");
            var row = Row(200f);
            var t = UiKit.Text(row.transform, "Info", "Your cosy farmhouse. Upgrading it will unlock new buildings, more sheep and farm decorations in future updates.", 34, TextAnchor.UpperLeft);
            UiKit.Stretch(t.rectTransform);
            ActionRow("Upgrade", "Coming soon", ProceduralIcons.Kind.Check, UiKit.Accent, false, null);
        }

        public void ShowBarn(FarmInteractable barn, IReadOnlyList<FarmSheepInfo> sheep)
        {
            int withWool = 0;
            foreach (var s in sheep) if (s.RemainingWool > 0.01f) withWool++;
            Open(barn.BuildingId, barn.DisplayName, sheep.Count + " sheep · " + withWool + " ready to shear");
            foreach (var s in sheep)
            {
                string id = s.SheepId;
                bool canShear = true; // any amount; bare patches simply pay nothing until they regrow
                var b = ActionRow(s.DisplayName + (s.Preserved ? "  [style]" : ""), "Wool " + s.WoolPercent + "%  ·  " + s.StatusText,
                    ProceduralIcons.Kind.Scissors, UiKit.Accent, canShear, () => _hub.StartShearing(id));
                // wool bar
                var bar = UiKit.Image(b.transform, "WoolBar", new Color(1f, 1f, 1f, 0.12f));
                UiKit.Place(bar.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-110f, 22f), new Vector2(160f, 14f));
                var fill = UiKit.Image(bar.transform, "Fill", new Color(1f, 0.98f, 0.92f, 1f));
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(s.RemainingWool, 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            }
        }

        /// <summary>Sheep card: wool, regrowth status, hairstyle lock; Shear and (when locked) Unlock hairstyle.</summary>
        public void ShowSheep(FarmInteractable sheep, FarmSheepInfo info)
        {
            Open(sheep.BuildingId, info.DisplayName, "Wool " + info.WoolPercent + "%  ·  " + info.StatusText);
            var bar = Row(40f);
            var back = UiKit.Image(bar.transform, "WoolBar", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Place(back.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 22f));
            var fill = UiKit.Image(back.transform, "Fill", info.Preserved ? UiKit.Accent : new Color(1f, 0.98f, 0.92f, 1f));
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(info.RemainingWool, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            string id = info.SheepId;
            _shearButton = ActionRow("Shear", info.Preserved ? "Edit the hairstyle (stays saved)" : info.RemainingWool > 0.01f ? "Collect " + info.WoolPercent + "% wool" : "Bare for now - wait for regrowth",
                ProceduralIcons.Kind.Scissors, UiKit.Accent, true, () => _hub.StartShearing(id));
            _unlockButton = info.Preserved
                ? ActionRow("Unlock hairstyle", "Wool starts regrowing from this shape", ProceduralIcons.Kind.Wool, UiKit.Air, true, () => _hub.SetHairstylePreserved(id, false))
                : null;
        }

        Button _shearButton, _unlockButton;
        public bool HasUnlock => _unlockButton != null && IsOpen;
        public void QA_Shear() { if (_shearButton != null) _shearButton.onClick.Invoke(); }
        public void QA_Unlock() { if (_unlockButton != null) _unlockButton.onClick.Invoke(); }

        public void ShowShed(FarmInteractable shed)
        {
            var e = WoolEconomy.Instance;
            int dirty = e.Available(WoolType.Dirty), wet = e.Available(WoolType.Wet);
            Open(shed.BuildingId, shed.DisplayName, "Wash dirty wool, then dry it for a better price.");
            _washButton = ActionRow("Wash Wool", dirty > 0 ? dirty + " dirty wool ready" : "No dirty wool — shear a sheep first",
                ProceduralIcons.Kind.Droplet, UiKit.Water, dirty > 0, () => _hub.StartActivity(SceneNavigator.Wash));
            _dryButton = ActionRow("Dry Wool", wet > 0 ? wet + " wet wool ready" : "No wet wool — wash some wool first",
                ProceduralIcons.Kind.Air, UiKit.Air, wet > 0, () => _hub.StartActivity(SceneNavigator.Dry));
        }

        // ------------------------------------------------------------------ building system sheets

        public struct SheetAction
        {
            public string Title, Sub; public ProceduralIcons.Kind Icon; public Color Color; public bool Enabled; public System.Action Click;
            public SheetAction(string title, string sub, ProceduralIcons.Kind icon, Color color, bool enabled, System.Action click)
            { Title = title; Sub = sub; Icon = icon; Color = color; Enabled = enabled; Click = click; }
        }

        readonly List<Button> _actionButtons = new List<Button>();
        public IReadOnlyList<Button> ActionButtons => _actionButtons;

        /// <summary>Generic sheet for a building / decoration: title, subtitle and up to a few action rows.</summary>
        public void ShowActions(string id, string title, string subtitle, IList<SheetAction> actions)
        {
            Open(id, title, subtitle);
            _actionButtons.Clear();
            foreach (var a in actions) _actionButtons.Add(ActionRow(a.Title, a.Sub, a.Icon, a.Color, a.Enabled, a.Click));
        }

        /// <summary>Adds one more action row to the sheet that is currently open (e.g. "Move" under a building panel).</summary>
        public Button AppendAction(SheetAction a)
        {
            if (!IsOpen) return null;
            var b = ActionRow(a.Title, a.Sub, a.Icon, a.Color, a.Enabled, a.Click);
            _actionButtons.Add(b);
            return b;
        }

        /// <summary>Changes the subtitle of the open sheet (adds building info).</summary>
        public void SetSubtitle(string s) { if (_subtitle != null) _subtitle.text = s; }

        System.Func<float> _cProgress;
        System.Func<double> _cRemaining;
        Image _cFill;
        Text _cText;
        int _cShownSecond = -1;

        /// <summary>Under-construction sheet with a live progress bar and remaining time (updated once per second).</summary>
        public void ShowConstruction(string id, string title, System.Func<float> progress, System.Func<double> remaining, string note)
        {
            Open(id, title, note);
            _actionButtons.Clear();
            _cProgress = progress; _cRemaining = remaining; _cShownSecond = -1;
            var row = Row(120f);
            var back = UiKit.Image(row.transform, "Progress", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Place(back.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(840f, 30f));
            _cFill = UiKit.Image(back.transform, "Fill", UiKit.Accent);
            _cFill.rectTransform.anchorMin = Vector2.zero; _cFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _cFill.rectTransform.offsetMin = _cFill.rectTransform.offsetMax = Vector2.zero;
            _cText = UiKit.Text(row.transform, "Time", "", 38, TextAnchor.MiddleCenter);
            UiKit.Place(_cText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(840f, 64f));
            UpdateConstruction();
        }

        public bool ConstructionOpen => IsOpen && _cProgress != null;
        public string ConstructionText => _cText != null ? _cText.text : "";

        void UpdateConstruction()
        {
            if (_cProgress == null || _cFill == null) return;
            float p = Mathf.Clamp01(_cProgress());
            _cFill.rectTransform.anchorMax = new Vector2(p, 1f);
            int sec = Mathf.CeilToInt((float)_cRemaining());
            if (sec != _cShownSecond)
            {
                _cShownSecond = sec;
                _cText.text = Mathf.RoundToInt(p * 100f) + "%  ·  " + (sec > 0 ? PlacementText.Time(sec) + " left" : "Finishing…");
            }
        }

        void Update()
        {
            if (!IsOpen || _cProgress == null) return;
            UpdateConstruction();
        }

        /// <summary>Re-renders the open panel (resources changed while it is open).</summary>
        public void RefreshOpen(FarmInteractable target)
        {
            if (!IsOpen || target == null || target.BuildingId != _openId) return;
            target.OnTap(_hub);
        }

        // QA
        public bool WashEnabled => _washButton != null && _washButton.interactable;
        public bool DryEnabled => _dryButton != null && _dryButton.interactable;
        public void QA_Wash() { if (_washButton != null) _washButton.onClick.Invoke(); }
        public void QA_Dry() { if (_dryButton != null) _dryButton.onClick.Invoke(); }
    }
}
