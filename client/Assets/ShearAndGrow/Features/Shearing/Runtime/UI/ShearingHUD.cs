using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Minimal portrait UI (spec §3.2, §7.3): compact progress on top, one-line hint until the first real gap,
    /// Back/Save and Finish at the bottom outside the finger's working area, a toggles row (haptics / sound / debug /
    /// reset view), fade overlay for session transitions and a calm completion panel.
    /// </summary>
    public sealed class ShearingHUD : MonoBehaviour
    {
        [Header("Style")]
        [SerializeField] private Color _panelColor = new Color(0.16f, 0.12f, 0.1f, 0.55f);
        [SerializeField] private Color _accentColor = new Color(1f, 0.66f, 0.3f, 1f);
        [SerializeField] private Color _barBackColor = new Color(1f, 1f, 1f, 0.18f);
        [SerializeField] private Color _textColor = new Color(1f, 0.97f, 0.92f, 1f);
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1080f, 1920f);

        public event Action BackPressed;
        public event Action FinishPressed;
        public event Action ResetViewPressed;
        public event Action RegrowPressed;
        public event Action ReenterPressed;
        public event Action<bool> HapticsToggled;
        public event Action<bool> SoundToggled;
        public event Action<bool> DebugToggled;
        public event Action BellyPressed;

        bool _devTools;
        Text _bellyLabel;

        Font _font;
        RectTransform _safeRoot;
        Image _barFill;
        Text _progressLabel;
        Text _hint;
        Button _finishButton;
        Image _finishImage;
        Button _regrowButton;
        Button _backButton;
        Image _fade;
        GameObject _completePanel;
        Text _toast;
        Text _hapticsLabel, _soundLabel, _debugLabel;
        bool _haptics, _sound, _debug;
        float _toastTimer;
        float _sparkle;
        Rect _lastSafeArea;
        Image _sparkleImage;

        public bool IsFading { get; private set; }

        /// <param name="devTools">Show developer controls (Debug overlay toggle, regrow). Pass Debug.isDebugBuild.</param>
        public void Build(string hintText, bool hapticsOn, bool soundOn, bool debugOn, bool devTools)
        {
            _devTools = devTools;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _haptics = hapticsOn; _sound = soundOn; _debug = debugOn;

            var canvasGo = new GameObject("ShearingCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<OrientationCanvasScaler>().PortraitReference = _referenceResolution;
            canvasGo.AddComponent<GraphicRaycaster>();

            _safeRoot = NewRect("SafeArea", canvasGo.transform);
            Stretch(_safeRoot);
            ApplySafeArea();

            // --- top: progress
            var top = NewRect("ProgressPanel", _safeRoot);
            _progressPanel = top;
            Anchor(top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(760f, 120f), new Vector2(0.5f, 1f));
            AddImage(top.gameObject, _panelColor);
            _progressLabel = AddText("Label", top, "Wool sheared: 0%", 40, TextAnchor.UpperCenter);
            Anchor(_progressLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), new Vector2(0f, 56f), new Vector2(0.5f, 1f));
            var barBack = NewRect("BarBack", top);
            Anchor(barBack, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(680f, 26f), new Vector2(0.5f, 0f));
            AddImage(barBack.gameObject, _barBackColor);
            var fill = NewRect("BarFill", barBack);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            _barFill = AddImage(fill.gameObject, _accentColor);
            // threshold tick
            var tick = NewRect("ThresholdTick", barBack);
            tick.anchorMin = tick.anchorMax = new Vector2(0.95f, 0.5f);
            tick.sizeDelta = new Vector2(4f, 40f);
            AddImage(tick.gameObject, _textColor);
            var sparkle = NewRect("Sparkle", top);
            Anchor(sparkle, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(46f, 46f), new Vector2(0.5f, 0.5f));
            _sparkleImage = AddImage(sparkle.gameObject, new Color(1f, 0.95f, 0.6f, 0f));
            sparkle.localRotation = Quaternion.Euler(0, 0, 45f);

            // --- toggles row
            var row = NewRect("TogglesRow", _safeRoot);
            Anchor(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -175f), new Vector2(900f, 70f), new Vector2(0.5f, 1f));
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 14f; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true; hl.childControlWidth = true; hl.childControlHeight = true;
            _toggleRow = row;
            _toggleColumn = NewRect("TogglesColumn", _safeRoot);
            var vl = _toggleColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 12f; vl.childForceExpandWidth = true; vl.childForceExpandHeight = true; vl.childControlWidth = true; vl.childControlHeight = true;
            _toggleColumn.gameObject.SetActive(false);
            _hapticsLabel = AddButton(row, "Haptics", 28,() => { _haptics = !_haptics; RefreshToggleLabels(); HapticsToggled?.Invoke(_haptics); }).GetComponentInChildren<Text>();
            _soundLabel = AddButton(row, "Sound", 28,() => { _sound = !_sound; RefreshToggleLabels(); SoundToggled?.Invoke(_sound); }).GetComponentInChildren<Text>();
            if (_devTools)
                _debugLabel = AddButton(row, "Debug", 28, () => { _debug = !_debug; RefreshToggleLabels(); DebugToggled?.Invoke(_debug); }).GetComponentInChildren<Text>();
            _bellyLabel = AddButton(row, "Belly ↻", 28, () => BellyPressed?.Invoke()).GetComponentInChildren<Text>();
            AddButton(row, "View ⟲", 28, () => ResetViewPressed?.Invoke());
            RefreshToggleLabels();

            // --- hint
            _hint = AddText("Hint", _safeRoot, hintText, 46, TextAnchor.MiddleCenter);
            Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(900f, 90f), new Vector2(0.5f, 0f));
            var shadow = _hint.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            shadow.effectDistance = new Vector2(2f, -2f);

            // --- bottom buttons (outside the finger working area)
            _backButton = AddButton(_safeRoot, "← Back", 36, () => BackPressed?.Invoke());
            Anchor((RectTransform)_backButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 36f), new Vector2(430f, 120f), new Vector2(0f, 0f));
            // "Done ▾" opens a compact menu: Finish shearing (summary) / Save hairstyle (lock the look).
            _finishButton = AddButton(_safeRoot, "Done ▲", 40, ToggleDoneMenu); // built-in font has ▲ (not ▾/✓)
            Anchor((RectTransform)_finishButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(330f, 120f), new Vector2(1f, 0f));
            _finishImage = _finishButton.GetComponent<Image>();
            SetFinishAvailable(false);
            _doneMenu = BuildDoneMenu();
            _doneMenu.SetActive(false);
            // Development builds only: instant regrow (natural regrowth replaces it for players).
            _regrowButton = AddButton(_safeRoot, "DEV Regrow ↻", 30, () => RegrowPressed?.Invoke());
            Anchor((RectTransform)_regrowButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 170f), new Vector2(330f, 80f), new Vector2(1f, 0f));
            _regrowButton.GetComponent<Image>().color = new Color(0.36f, 0.16f, 0.12f, 0.9f);
            _regrowButton.gameObject.SetActive(devTools);

            // --- toast
            _toast = AddText("Toast", _safeRoot, "", 38, TextAnchor.MiddleCenter);
            Anchor(_toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(900f, 80f), new Vector2(0.5f, 0.5f));
            _toast.color = new Color(1, 1, 1, 0);

            // --- completion panel
            _completePanel = BuildCompletePanel();
            _completePanel.SetActive(false);

            // --- fade overlay (topmost)
            var fade = NewRect("Fade", canvasGo.transform);
            Stretch(fade);
            _fade = AddImage(fade.gameObject, new Color(0.08f, 0.06f, 0.05f, 0f));
            _fade.raycastTarget = false;

            ShearAndGrow.Economy.ScreenLayout.Changed += OnScreenChanged;
            ApplyLayout(ShearAndGrow.Economy.ScreenLayout.IsLandscape);
        }

        // ------------------------------------------------------------------ portrait / landscape layouts

        RectTransform _progressPanel, _toggleRow, _toggleColumn;
        bool? _landscape;

        public bool IsLandscapeLayout => _landscape == true;

        void OnDestroy() => ShearAndGrow.Economy.ScreenLayout.Changed -= OnScreenChanged;

        void OnScreenChanged()
        {
            ApplySafeArea();
            ApplyLayout(ShearAndGrow.Economy.ScreenLayout.IsLandscape);
        }

        /// <summary>
        /// Portrait: progress + toggles row on top, Back / Done at the bottom (unchanged original layout).
        /// Landscape: progress top-centre, toggles as a column on the left edge so the sheep keeps the wide centre,
        /// Back / Done in the bottom corners, completion card centred and scaled to the available height.
        /// Only positions change – no state, events or session logic are touched.
        /// </summary>
        void ApplyLayout(bool landscape)
        {
            if (_safeRoot == null) return;
            bool changed = _landscape != landscape;
            _landscape = landscape;
            if (changed)
            {
                var from = landscape ? _toggleRow : _toggleColumn;
                var to = landscape ? _toggleColumn : _toggleRow;
                while (from.childCount > 0) from.GetChild(0).SetParent(to, false);
                _toggleRow.gameObject.SetActive(!landscape);
                _toggleColumn.gameObject.SetActive(landscape);
            }
            int toggles = _toggleColumn.childCount + _toggleRow.childCount;
            if (landscape)
            {
                Anchor(_progressPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(760f, 120f), new Vector2(0.5f, 1f));
                Anchor(_toggleColumn, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(250f, toggles * 82f), new Vector2(0f, 1f));
                Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(900f, 90f), new Vector2(0.5f, 0f));
                Anchor((RectTransform)_backButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(330f, 116f), new Vector2(0f, 0f));
                Anchor((RectTransform)_finishButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(300f, 116f), new Vector2(1f, 0f));
                Anchor((RectTransform)_regrowButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 160f), new Vector2(300f, 80f), new Vector2(1f, 0f));
                Anchor((RectTransform)_doneMenu.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 160f), new Vector2(640f, 330f), new Vector2(1f, 0f));
                Anchor(_toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(900f, 80f), new Vector2(0.5f, 1f));
                Anchor((RectTransform)_completePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 680f), new Vector2(0.5f, 0.5f));
                if (_preservedChip != null) Anchor(_preservedChip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(760f, 50f), new Vector2(0.5f, 1f));
            }
            else
            {
                Anchor(_progressPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(760f, 120f), new Vector2(0.5f, 1f));
                Anchor(_toggleRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -175f), new Vector2(900f, 70f), new Vector2(0.5f, 1f));
                Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(900f, 90f), new Vector2(0.5f, 0f));
                Anchor((RectTransform)_backButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(36f, 36f), new Vector2(430f, 120f), new Vector2(0f, 0f));
                Anchor((RectTransform)_finishButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(330f, 120f), new Vector2(1f, 0f));
                Anchor((RectTransform)_regrowButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 170f), new Vector2(330f, 80f), new Vector2(1f, 0f));
                Anchor((RectTransform)_doneMenu.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 170f), new Vector2(640f, 330f), new Vector2(1f, 0f));
                Anchor(_toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(900f, 80f), new Vector2(0.5f, 0.5f));
                Anchor((RectTransform)_completePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(860f, 680f), new Vector2(0.5f, 0.5f));
                if (_preservedChip != null) Anchor(_preservedChip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(760f, 50f), new Vector2(0.5f, 1f));
            }
            Canvas.ForceUpdateCanvases();
            UiFit.ScaleToFit((RectTransform)_completePanel.transform, _safeRoot);
            UiFit.ScaleToFit((RectTransform)_doneMenu.transform, _safeRoot);
        }

        GameObject BuildCompletePanel()
        {
            var panel = NewRect("CompletePanel", _safeRoot);
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(860f, 680f), new Vector2(0.5f, 0.5f));
            AddImage(panel.gameObject, new Color(0.18f, 0.13f, 0.1f, 0.85f));
            var title = AddText("Title", panel, "Shearing finished!", 58, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), new Vector2(0f, 90f), new Vector2(0.5f, 1f));
            _completeWool = AddText("Wool", panel, "", 46, TextAnchor.MiddleCenter);
            Anchor(_completeWool.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -140f), new Vector2(0f, 64f), new Vector2(0.5f, 1f));
            _completeWool.color = _accentColor;
            _completeSub = AddText("Sub", panel, "", 30, TextAnchor.UpperCenter);
            Anchor(_completeSub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -214f), new Vector2(-60f, 90f), new Vector2(0.5f, 1f));
            _completeSub.color = new Color(_textColor.r, _textColor.g, _textColor.b, 0.7f);
            var menu = AddButton(panel, "Back to farm", 38, () => MenuPressed?.Invoke());
            Anchor((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(700f, 110f), new Vector2(0.5f, 0f));
            menu.GetComponent<Image>().color = _accentColor;
            var back = AddButton(panel, "Keep shearing", 34, () => ReenterPressed?.Invoke());
            Anchor((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(700f, 110f), new Vector2(0.5f, 0f));
            return panel.gameObject;
        }

        Text _completeWool, _completeSub, _preservedChip;
        GameObject _doneMenu;
        public event Action MenuPressed;
        public event Action SaveHairstylePressed;

        public bool IsDoneMenuOpen => _doneMenu != null && _doneMenu.activeSelf;

        void ToggleDoneMenu() { if (_doneMenu != null) _doneMenu.SetActive(!_doneMenu.activeSelf); }
        public void CloseDoneMenu() { if (_doneMenu != null) _doneMenu.SetActive(false); }

        /// <summary>Compact menu above "Done": explains the difference between finishing and saving a hairstyle.</summary>
        GameObject BuildDoneMenu()
        {
            var panel = NewRect("DoneMenu", _safeRoot);
            Anchor(panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 170f), new Vector2(640f, 330f), new Vector2(1f, 0f));
            AddImage(panel.gameObject, new Color(0.18f, 0.13f, 0.1f, 0.94f));
            var finish = AddButton(panel, "", 10, () => { CloseDoneMenu(); FinishPressed?.Invoke(); });
            Anchor((RectTransform)finish.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -15f), new Vector2(-30f, 145f), new Vector2(0.5f, 1f));
            MenuEntry(finish.transform, "Finish shearing", "Show results & go back to the farm");
            var style = AddButton(panel, "", 10, () => { CloseDoneMenu(); SaveHairstylePressed?.Invoke(); });
            Anchor((RectTransform)style.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 15f), new Vector2(-30f, 145f), new Vector2(0.5f, 0f));
            MenuEntry(style.transform, "Save hairstyle", "Keep this look - wool stops regrowing");
            return panel.gameObject;
        }

        void MenuEntry(Transform button, string title, string sub)
        {
            var old = button.GetComponentInChildren<Text>();
            if (old != null) old.gameObject.SetActive(false);
            var t = AddText("Title", button, title, 40, TextAnchor.LowerLeft);
            Anchor(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(26f, 2f), new Vector2(-40f, 60f), new Vector2(0f, 0f));
            var s = AddText("Sub", button, sub, 28, TextAnchor.UpperLeft);
            s.color = new Color(_textColor.r, _textColor.g, _textColor.b, 0.6f);
            Anchor(s.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(26f, -4f), new Vector2(-40f, 50f), new Vector2(0f, 1f));
        }

        /// <summary>Shows the "Hairstyle saved" chip (growth paused) while editing a preserved sheep.</summary>
        public void SetPreserved(bool preserved)
        {
            if (_preservedChip == null)
            {
                _preservedChip = AddText("PreservedChip", _safeRoot, "Hairstyle saved · no regrowth", 30, TextAnchor.MiddleCenter);
                Anchor(_preservedChip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(760f, 50f), new Vector2(0.5f, 1f));
                _preservedChip.color = _accentColor;
                var sh = _preservedChip.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
                ApplyLayout(_landscape ?? false);
            }
            _preservedChip.text = "Hairstyle saved · no regrowth";
            _preservedChip.gameObject.SetActive(preserved);
        }

        /// <summary>Finish summary: wool earned in this session and wool left on the sheep.</summary>
        public void SetCompleteWool(int sessionWool, float woolLeft)
        {
            if (_completeWool == null) return;
            _completeWool.text = sessionWool > 0 ? "+" + sessionWool + " Dirty Wool" : "No new wool this time";
            if (_completeSub != null) _completeSub.text = "Wool left on the sheep: " + Mathf.RoundToInt(woolLeft * 100f) + "% · sheared areas regrow over time";
        }

        void RefreshToggleLabels()
        {
            _hapticsLabel.text = (_haptics ? "● " : "○ ") + "Haptics";
            _soundLabel.text = (_sound ? "● " : "○ ") + "Sound";
            if (_debugLabel != null) _debugLabel.text = (_debug ? "● " : "○ ") + "Debug";
        }

        /// <summary>Updates the belly button label (rolled = offer to stand the sheep back up).</summary>
        public void SetBellyMode(bool rolled)
        {
            if (_bellyLabel != null) _bellyLabel.text = rolled ? "Stand ↺" : "Belly ↻";
        }

        public void SetProgress(float fraction, float threshold)
        {
            fraction = Mathf.Clamp01(fraction);
            ((RectTransform)_barFill.transform).anchorMax = new Vector2(fraction, 1f);
            int left = Mathf.CeilToInt((1f - fraction) * 100f - 0.0001f);
            _progressLabel.text = "Wool left: " + left + "%";
            if (fraction >= threshold && _sparkle <= 0f) _sparkle = 1.2f;
        }

        public void HideHint() { if (_hint != null) _hint.gameObject.SetActive(false); }
        public void ShowHint(bool show) { if (_hint != null) _hint.gameObject.SetActive(show); }

        public void SetFinishAvailable(bool available)
        {
            _finishButton.interactable = available;
            _finishImage.color = available ? _accentColor : new Color(0.4f, 0.36f, 0.33f, 0.6f);
        }

        public bool IsCompletePanelVisible => _completePanel != null && _completePanel.activeSelf;

        /// <summary>DEV regrow button visibility (development builds only; "Done" always stays available).</summary>
        public void SetRegrowAvailable(bool available)
        {
            _regrowButton.gameObject.SetActive(_devTools);
            _finishButton.gameObject.SetActive(true);
        }

        public void SetButtonsInteractable(bool interactable)
        {
            _backButton.interactable = interactable;
            _regrowButton.interactable = interactable;
            if (!interactable) { _finishButton.interactable = false; CloseDoneMenu(); }
        }

        public void ShowToast(string text, float seconds = 1.6f)
        {
            _toast.text = text;
            _toastTimer = seconds;
        }

        public void ShowCompletePanel(bool show) => _completePanel.SetActive(show);

        public IEnumerator Fade(float to, float duration)
        {
            IsFading = true;
            _fade.raycastTarget = true;
            float from = _fade.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                var c = _fade.color; c.a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)); _fade.color = c;
                yield return null;
            }
            var cc = _fade.color; cc.a = to; _fade.color = cc;
            _fade.raycastTarget = to > 0.01f;
            IsFading = false;
        }

        void Update()
        {
            if (_safeRoot == null) return;
            if (Screen.safeArea != _lastSafeArea) ApplySafeArea();
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                _toast.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_toastTimer * 3f));
            }
            if (_hint != null && _hint.gameObject.activeSelf)
            {
                float a = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
                _hint.color = new Color(_textColor.r, _textColor.g, _textColor.b, a);
            }
            if (_sparkle > 0f)
            {
                _sparkle -= Time.unscaledDeltaTime;
                float s = Mathf.Clamp01(_sparkle);
                _sparkleImage.color = new Color(1f, 0.95f, 0.6f, Mathf.Sin(s * Mathf.PI));
                _sparkleImage.transform.localScale = Vector3.one * (0.6f + 0.8f * (1f - s));
                _sparkleImage.transform.localRotation = Quaternion.Euler(0, 0, 45f + (1f - s) * 90f);
            }
        }

        void ApplySafeArea()
        {
            Rect sa = Screen.safeArea;
            _lastSafeArea = sa;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            _safeRoot.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            _safeRoot.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------- helpers
        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        Image AddImage(GameObject go, Color c)
        {
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        Text AddText(string name, Transform parent, string text, int size, TextAnchor align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = _textColor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Button AddButton(Transform parent, string label, int size, Action onClick)
        {
            var rt = NewRect("Button_" + label, parent);
            var img = AddImage(rt.gameObject, _panelColor);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.8f, 0.75f, 1f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            var t = AddText("Text", rt, label, size, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            return b;
        }
    }
}
