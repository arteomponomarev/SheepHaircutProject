using System;
using System.Collections;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Clean top HUD for the farm: Coins + Dirty/Wet/Clean chips + a small settings button (Sound, Haptics and – in
    /// Editor / Development builds only – dev tools). All actions happen in the world; no big menu buttons.
    /// Values are pushed on economy change only.
    /// </summary>
    public sealed class FarmHUD : MonoBehaviour
    {
        public event Action<bool> SoundToggled, HapticsToggled;
        /// <summary>The player picked a screen orientation in Settings.</summary>
        public event Action<ShearAndGrow.Economy.OrientationMode> OrientationPicked;

        RectTransform _safe, _coinIcon;
        Text _coins, _toast, _soundLabel, _hapticsLabel;
        readonly Text[] _chip = new Text[3];
        readonly RectTransform[] _chipRt = new RectTransform[3];
        GameObject _settings;
        Image _fade;
        float _toastTimer;
        bool _sound, _haptics;
        long _shownCoins = long.MinValue;
        readonly int[] _shown = { -1, -1, -1 };

        public RectTransform Safe => _safe;
        public RectTransform CoinTarget => _coinIcon;
        public bool SettingsOpen => _settings != null && _settings.activeSelf;

        static readonly Color[] Tint = { new Color(0.86f, 0.76f, 0.56f), new Color(0.62f, 0.78f, 0.92f), new Color(1f, 0.98f, 0.94f) };

        public void Build(bool soundOn, bool hapticsOn, bool devTools)
        {
            _sound = soundOn; _haptics = hapticsOn;
            var canvas = UiKit.CreateCanvas(transform, "FarmHUDCanvas", 10, out _safe);

            // Stretches with the screen (portrait phones are narrower than the 1080 reference): horizontal layout.
            var bar = UiKit.Image(_safe, "TopBar", UiKit.Panel);
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(16f, -136f); brt.offsetMax = new Vector2(-16f, -16f); // 120 tall, 16 margins
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(14, 12, 12, 12); row.spacing = 10f;
            row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = true;
            row.childAlignment = TextAnchor.MiddleLeft;

            var coinGroup = UiKit.Rect("CoinGroup", bar.transform);
            var cle = coinGroup.gameObject.AddComponent<LayoutElement>(); cle.minWidth = 170f; cle.flexibleWidth = 1f;
            var coin = UiKit.Image(coinGroup, "CoinIcon", Color.white);
            coin.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(72f, 72f));
            _coinIcon = coin.rectTransform;
            _coins = UiKit.Text(coinGroup, "Coins", "0", 46, TextAnchor.MiddleLeft, UiKit.Coin);
            _coins.rectTransform.anchorMin = new Vector2(0f, 0f); _coins.rectTransform.anchorMax = new Vector2(1f, 1f);
            _coins.rectTransform.offsetMin = new Vector2(80f, 0f); _coins.rectTransform.offsetMax = Vector2.zero;
            _coins.resizeTextForBestFit = true; _coins.resizeTextMinSize = 26; _coins.resizeTextMaxSize = 46;

            for (int i = 0; i < 3; i++)
            {
                var chip = UiKit.Image(bar.transform, "Chip" + i, new Color(1f, 1f, 1f, 0.07f));
                var le = chip.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 172f; le.minWidth = 140f;
                _chipRt[i] = chip.rectTransform;
                var ic = UiKit.Image(chip.transform, "Icon", Color.white);
                ic.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Wool, Tint[i]);
                UiKit.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(64f, 64f));
                _chip[i] = UiKit.Text(chip.transform, "Count", "0", 40, TextAnchor.MiddleLeft);
                _chip[i].rectTransform.anchorMin = Vector2.zero; _chip[i].rectTransform.anchorMax = Vector2.one;
                _chip[i].rectTransform.offsetMin = new Vector2(72f, 0f); _chip[i].rectTransform.offsetMax = new Vector2(-4f, 0f);
                _chip[i].resizeTextForBestFit = true; _chip[i].resizeTextMinSize = 22; _chip[i].resizeTextMaxSize = 40;
            }

            var gear = UiKit.Button(bar.transform, "Settings", "•••", 34, UiKit.PanelLight, ToggleSettings, out _); // legacy font has no ⚙ glyph
            var gle = gear.gameObject.AddComponent<LayoutElement>(); gle.preferredWidth = 92f; gle.minWidth = 92f;

            BuildSettings(devTools);

            _toast = UiKit.Text(_safe, "Toast", "", 46, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1000f, 70f));
            UiKit.SoftShadow(_toast);
            _toast.color = new Color(1, 1, 1, 0);

            _fade = UiKit.Image(canvas.transform, "Fade", new Color(0.08f, 0.06f, 0.05f, 1f), true);
            UiKit.Stretch(_fade.rectTransform);
        }

        readonly Button[] _orientationButtons = new Button[3];
        GameObject _devButton;
        RectTransform _settingsRt;

        void BuildSettings(bool devTools)
        {
            var pop = UiKit.Image(_safe, "SettingsPopover", UiKit.Panel, true);
            _settingsRt = pop.rectTransform;
            UiKit.Place(pop.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -150f), new Vector2(460f, 100f));
            var col = pop.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(18, 18, 18, 18); col.spacing = 12f;
            col.childControlWidth = col.childControlHeight = true; col.childForceExpandWidth = true; col.childForceExpandHeight = false;
            var fit = pop.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiKit.Button(pop.transform, "Sound", "", 34, UiKit.PanelLight, () => { _sound = !_sound; Refresh(); SoundToggled?.Invoke(_sound); }, out _soundLabel).gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;
            UiKit.Button(pop.transform, "Haptics", "", 34, UiKit.PanelLight, () => { _haptics = !_haptics; Refresh(); HapticsToggled?.Invoke(_haptics); }, out _hapticsLabel).gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;

            // screen orientation (player setting, persisted by OrientationService)
            var head = UiKit.Text(pop.transform, "ScreenLabel", "Screen", 28, TextAnchor.LowerLeft, UiKit.TextMuted);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var row = UiKit.Rect("OrientationRow", pop.transform);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 92f;
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = true;
            for (int i = 0; i < 3; i++)
            {
                var mode = (ShearAndGrow.Economy.OrientationMode)i;
                _orientationButtons[i] = UiKit.Button(row, "Orientation" + mode, ShearAndGrow.Economy.OrientationService.Label(mode), 27, UiKit.PanelLight,
                    () => { OrientationPicked?.Invoke(mode); Refresh(); }, out _);
            }

            // Developer Panel entry: exists only when the DevTools assembly is compiled in (Editor / Development build)
            if (devTools)
            {
                var dev = new Color(0.36f, 0.16f, 0.12f, 0.95f);
                var b = UiKit.Button(pop.transform, "DeveloperPanel", "DEV · Developer Panel", 28, dev, () => { CloseSettings(); ShearAndGrow.Economy.DevToolsGate.Open(); }, out _);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
                _devButton = b.gameObject;
                ShearAndGrow.Economy.DevToolsGate.AvailabilityChanged += RefreshDevButton;
            }
            _settings = pop.gameObject;
            _settings.SetActive(false);
            Refresh();
        }

        void OnDestroy() => ShearAndGrow.Economy.DevToolsGate.AvailabilityChanged -= RefreshDevButton;

        void RefreshDevButton() { if (_devButton != null) _devButton.SetActive(ShearAndGrow.Economy.DevToolsGate.IsAvailable); }

        void Refresh()
        {
            _soundLabel.text = (_sound ? "● " : "○ ") + "Sound";
            _hapticsLabel.text = (_haptics ? "● " : "○ ") + "Haptics";
            var current = ShearAndGrow.Economy.OrientationService.Mode;
            for (int i = 0; i < 3; i++)
                if (_orientationButtons[i] != null) _orientationButtons[i].image.color = (int)current == i ? UiKit.Accent : UiKit.PanelLight;
            RefreshDevButton();
        }

        public void ToggleSettings() { if (!_settings.activeSelf) Refresh(); _settings.SetActive(!_settings.activeSelf); }
        public void CloseSettings() { if (_settings != null) _settings.SetActive(false); }

        public void SetCoins(long coins)
        {
            if (coins == _shownCoins) return;
            _shownCoins = coins;
            _coins.text = coins.ToString("N0");
        }

        public void SetWool(int dirty, int wet, int clean)
        {
            int[] v = { dirty, wet, clean };
            for (int i = 0; i < 3; i++)
            {
                if (_shown[i] == v[i]) continue;
                bool pulse = _shown[i] >= 0;
                _shown[i] = v[i];
                _chip[i].text = v[i].ToString();
                if (pulse) StartCoroutine(Pulse(_chipRt[i]));
            }
        }

        public void PulseCoins() => StartCoroutine(Pulse(_coinIcon));

        IEnumerator Pulse(RectTransform t)
        {
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.3f);
                t.localScale = Vector3.one * (1f + Mathf.Sin(a * Mathf.PI) * 0.15f);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        public void ShowToast(string text, float seconds = 2.2f) { _toast.text = text; _toastTimer = seconds; }
        public string ToastText => _toast != null ? _toast.text : "";

        void Update()
        {
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                var c = UiKit.Accent; c.a = Mathf.Clamp01(_toastTimer / 0.4f);
                _toast.color = c;
            }
        }

        public IEnumerator FadeTo(float to, float seconds)
        {
            _fade.raycastTarget = true;
            float from = _fade.color.a, t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                var c = _fade.color; c.a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)); _fade.color = c;
                yield return null;
            }
            var cc = _fade.color; cc.a = to; _fade.color = cc;
            _fade.raycastTarget = to > 0.01f;
        }
    }
}
