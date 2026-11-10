using System;
using System.Collections;
using ShearAndGrow.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Temporary main menu (portrait): coins + sound/haptics toggles, three resource chips, four big action buttons
    /// with icon, title, count and disabled state, and development-only tools. Values are pushed on change only.
    /// </summary>
    public sealed class HubUI : MonoBehaviour
    {
        public event Action ShearPressed, WashPressed, DryPressed, SellPressed;
        public event Action<bool> SoundToggled, HapticsToggled;
        public event Action DevAddDirty, DevAddWet, DevReset;

        RectTransform _safe;
        Text _coins;
        RectTransform _coinIcon;
        readonly Text[] _chipCount = new Text[3];
        readonly RectTransform[] _chips = new RectTransform[3];
        Button _wash, _dry, _sell;
        Text _washSub, _drySub, _sellSub, _soundLabel, _hapticsLabel, _toast;
        Image _fade;
        bool _sound, _haptics;
        float _toastTimer;
        long _shownCoins = -1;

        public RectTransform CoinTarget => _coinIcon;
        public RectTransform Safe => _safe;
        public Image Fade => _fade;

        static readonly Color[] WoolTint =
        {
            new Color(0.86f, 0.76f, 0.56f), // dirty
            new Color(0.62f, 0.78f, 0.92f), // wet
            new Color(1f, 0.98f, 0.94f),    // clean
        };
        static readonly string[] WoolName = { "Dirty", "Wet", "Clean" };

        public void Build(bool soundOn, bool hapticsOn, bool devTools)
        {
            _sound = soundOn; _haptics = hapticsOn;
            var canvas = UiKit.CreateCanvas(transform, "HubCanvas", 10, out _safe);

            // ---- top bar: coins + toggles
            var coinBar = UiKit.Image(_safe, "CoinBar", UiKit.Panel);
            UiKit.Place(coinBar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(420f, 110f));
            var coinIcon = UiKit.Image(coinBar.transform, "CoinIcon", Color.white);
            coinIcon.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Coin);
            UiKit.Place(coinIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(80f, 80f));
            _coinIcon = coinIcon.rectTransform;
            _coins = UiKit.Text(coinBar.transform, "Coins", "0", 56, TextAnchor.MiddleLeft, UiKit.Coin);
            UiKit.Place(_coins.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(112f, 0f), new Vector2(300f, 90f));

            var sound = UiKit.Button(_safe, "Sound", "", 30, UiKit.Panel, () => { _sound = !_sound; RefreshToggles(); SoundToggled?.Invoke(_sound); }, out _soundLabel);
            UiKit.Place((RectTransform)sound.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(230f, 110f));
            var hap = UiKit.Button(_safe, "Haptics", "", 30, UiKit.Panel, () => { _haptics = !_haptics; RefreshToggles(); HapticsToggled?.Invoke(_haptics); }, out _hapticsLabel);
            UiKit.Place((RectTransform)hap.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-276f, -30f), new Vector2(230f, 110f));
            RefreshToggles();

            // ---- resource chips
            for (int i = 0; i < 3; i++)
            {
                var chip = UiKit.Image(_safe, WoolName[i] + "Chip", UiKit.Panel);
                float x = (i - 1) * 330f;
                UiKit.Place(chip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -170f), new Vector2(310f, 150f));
                _chips[i] = chip.rectTransform;
                var icon = UiKit.Image(chip.transform, "Icon", Color.white);
                icon.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Wool, WoolTint[i]);
                UiKit.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(92f, 92f));
                var name = UiKit.Text(chip.transform, "Name", WoolName[i] + " Wool", 28, TextAnchor.UpperLeft, UiKit.TextMuted);
                UiKit.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -16f), new Vector2(190f, 40f));
                _chipCount[i] = UiKit.Text(chip.transform, "Count", "0", 54, TextAnchor.LowerLeft);
                UiKit.Place(_chipCount[i].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(116f, 14f), new Vector2(190f, 76f));
            }

            // ---- main actions (bottom half – thumb reach)
            float y0 = 150f;
            BuildAction("Shear Sheep", "Earn dirty wool", ProceduralIcons.Kind.Scissors, UiKit.Accent, y0 + 3 * 205f, () => ShearPressed?.Invoke(), out _);
            _wash = BuildAction("Wash Wool", "", ProceduralIcons.Kind.Droplet, UiKit.Water, y0 + 2 * 205f, () => WashPressed?.Invoke(), out _washSub);
            _dry = BuildAction("Dry Wool", "", ProceduralIcons.Kind.Air, UiKit.Air, y0 + 205f, () => DryPressed?.Invoke(), out _drySub);
            _sell = BuildAction("Sell Wool", "", ProceduralIcons.Kind.Coin, UiKit.Coin, y0, () => SellPressed?.Invoke(), out _sellSub);

            if (devTools)
            {
                var row = UiKit.Rect("DevRow", _safe);
                UiKit.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1000f, 90f));
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = 14f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = true;
                var devColor = new Color(0.36f, 0.16f, 0.12f, 0.9f);
                UiKit.Button(row, "DevDirty", "DEV +100 Dirty", 26, devColor, () => DevAddDirty?.Invoke(), out _);
                UiKit.Button(row, "DevWet", "DEV +100 Wet", 26, devColor, () => DevAddWet?.Invoke(), out _);
                UiKit.Button(row, "DevReset", "DEV Reset Save", 26, devColor, () => DevReset?.Invoke(), out _);
            }

            _toast = UiKit.Text(_safe, "Toast", "", 46, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(1000f, 70f));
            UiKit.SoftShadow(_toast);
            _toast.color = new Color(1, 1, 1, 0);

            _fade = UiKit.Image(canvas.transform, "Fade", new Color(0.08f, 0.06f, 0.05f, 1f), true);
            UiKit.Stretch(_fade.rectTransform);
        }

        Button BuildAction(string title, string sub, ProceduralIcons.Kind icon, Color color, float y, Action onClick, out Text subText)
        {
            var b = UiKit.Button(_safe, title.Replace(" ", ""), "", 10, UiKit.Panel, onClick, out var lbl);
            lbl.gameObject.SetActive(false);
            var rt = (RectTransform)b.transform;
            UiKit.Place(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(960f, 185f));
            var stripe = UiKit.Image(rt, "Stripe", color);
            UiKit.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(16f, 185f));
            var badge = UiKit.Image(rt, "IconBack", new Color(color.r, color.g, color.b, 0.22f));
            UiKit.Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(130f, 130f));
            var ic = UiKit.Image(badge.transform, "Icon", Color.white);
            ic.sprite = ProceduralIcons.Get(icon, color);
            UiKit.Place(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            var t = UiKit.Text(rt, "Title", title, 54, TextAnchor.LowerLeft);
            UiKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(205f, 2f), new Vector2(560f, 76f));
            subText = UiKit.Text(rt, "Sub", sub, 32, TextAnchor.UpperLeft, UiKit.TextMuted);
            UiKit.Place(subText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(205f, -4f), new Vector2(560f, 50f));
            var arrow = UiKit.Text(rt, "Arrow", "›", 90, TextAnchor.MiddleCenter, color);
            UiKit.Place(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 6f), new Vector2(80f, 120f));
            return b;
        }

        void RefreshToggles()
        {
            _soundLabel.text = (_sound ? "● " : "○ ") + "Sound";
            _hapticsLabel.text = (_haptics ? "● " : "○ ") + "Haptics";
        }

        /// <summary>Pushes resource values (call on economy change only).</summary>
        public void SetResources(int dirty, int wet, int clean, long coinsValueOfAll)
        {
            _chipCount[0].text = dirty.ToString();
            _chipCount[1].text = wet.ToString();
            _chipCount[2].text = clean.ToString();
            SetAction(_wash, _washSub, dirty > 0, dirty > 0 ? dirty + " dirty wool ready" : "Shear a sheep first");
            SetAction(_dry, _drySub, wet > 0, wet > 0 ? wet + " wet wool ready" : "Wash wool first");
            bool any = dirty + wet + clean > 0;
            SetAction(_sell, _sellSub, any, any ? "Worth up to " + coinsValueOfAll + " coins" : "Nothing to sell yet");
        }

        static void SetAction(Button b, Text sub, bool enabled, string text)
        {
            b.interactable = enabled;
            sub.text = text;
            var cg = b.GetComponent<CanvasGroup>();
            if (cg == null) cg = b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = enabled ? 1f : 0.5f;
        }

        public void SetCoins(long coins)
        {
            if (coins == _shownCoins) return;
            _shownCoins = coins;
            _coins.text = coins.ToString("N0");
        }

        public void PulseChip(int index) => StartCoroutine(Pulse(_chips[index]));
        public void PulseCoins() => StartCoroutine(Pulse(_coinIcon));

        IEnumerator Pulse(RectTransform t)
        {
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.3f);
                t.localScale = Vector3.one * (1f + Mathf.Sin(a * Mathf.PI) * 0.12f);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        public void ShowToast(string text, float seconds = 2f) { _toast.text = text; _toastTimer = seconds; }

        void Update()
        {
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(_toastTimer / 0.4f);
                _toast.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, a);
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
