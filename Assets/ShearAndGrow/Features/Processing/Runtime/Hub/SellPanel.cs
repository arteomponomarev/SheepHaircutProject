using System;
using System.Collections;
using ShearAndGrow.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Sell overlay: Dirty / Wet / Clean rows (toggle, amount × price → value), live total, Sell Selected / Sell All.
    /// The sale is a single atomic economy call; the coin-fly animation, clink sounds and haptic confirmation are
    /// purely presentational (pooled coin images, no allocations per frame).
    /// </summary>
    public sealed class SellPanel : MonoBehaviour
    {
        public event Action<long> Sold;            // coins earned
        public event Action CoinArrived;           // each flying coin reaching the counter
        public event Action Closed;

        GameObject _root;
        readonly Toggle[] _toggles = new Toggle[3];
        readonly Text[] _rowText = new Text[3];
        readonly Text[] _rowValue = new Text[3];
        Text _total;
        Button _sellSelected, _sellAll;
        RectTransform _coinTarget, _safe;
        RectTransform[] _coinPool;
        bool _busy;

        static readonly string[] Names = { "Dirty", "Wet", "Clean" };
        static readonly Color[] Tints = { new Color(0.86f, 0.76f, 0.56f), new Color(0.62f, 0.78f, 0.92f), new Color(1f, 0.98f, 0.94f) };

        public bool IsOpen => _root != null && _root.activeSelf;

        public void Build(RectTransform safe, RectTransform coinTarget)
        {
            _safe = safe;
            _coinTarget = coinTarget;
            var dim = UiKit.Image(safe, "SellDim", new Color(0f, 0f, 0f, 0.55f), true);
            UiKit.Stretch(dim.rectTransform);
            _root = dim.gameObject;
            var card = UiKit.Image(dim.transform, "SellCard", UiKit.Panel, true);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(960f, 1040f));
            var title = UiKit.Text(card.transform, "Title", "Sell Wool", 64, TextAnchor.MiddleLeft);
            UiKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -30f), new Vector2(600f, 100f));
            var close = UiKit.Button(card.transform, "Close", "✕", 52, UiKit.PanelLight, Close, out _);
            UiKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(100f, 100f));

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var row = UiKit.Image(card.transform, Names[i] + "Row", UiKit.PanelLight, true);
                UiKit.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f - i * 190f), new Vector2(880f, 170f));
                var toggle = row.gameObject.AddComponent<Toggle>();
                toggle.targetGraphic = row;
                var box = UiKit.Image(row.transform, "Box", new Color(1f, 1f, 1f, 0.15f));
                UiKit.Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(70f, 70f));
                var check = UiKit.Image(box.transform, "Check", Color.white);
                check.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Check, UiKit.Accent);
                UiKit.Place(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
                toggle.graphic = check;
                toggle.isOn = true;
                toggle.onValueChanged.AddListener(_ => RefreshValues());
                _toggles[i] = toggle;
                var icon = UiKit.Image(row.transform, "Icon", Color.white);
                icon.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Wool, Tints[i]);
                UiKit.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(116f, 0f), new Vector2(110f, 110f));
                _rowText[i] = UiKit.Text(row.transform, "Text", "", 38, TextAnchor.MiddleLeft);
                UiKit.Place(_rowText[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(246f, 0f), new Vector2(390f, 140f));
                _rowValue[i] = UiKit.Text(row.transform, "Value", "", 46, TextAnchor.MiddleRight, UiKit.Coin);
                UiKit.Place(_rowValue[i].rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(230f, 140f));
            }

            _total = UiKit.Text(card.transform, "Total", "", 52, TextAnchor.MiddleCenter, UiKit.Coin);
            UiKit.Place(_total.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 200f), new Vector2(880f, 80f));
            _sellSelected = UiKit.Button(card.transform, "SellSelected", "Sell Selected", 40, UiKit.Accent, () => DoSell(false), out _);
            UiKit.Place((RectTransform)_sellSelected.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-15f, 50f), new Vector2(425f, 130f));
            _sellAll = UiKit.Button(card.transform, "SellAll", "Sell All", 40, UiKit.AccentDark, () => DoSell(true), out _);
            UiKit.Place((RectTransform)_sellAll.transform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(15f, 50f), new Vector2(425f, 130f));

            // pooled flying coins (on top of everything inside the safe area)
            _coinPool = new RectTransform[14];
            for (int i = 0; i < _coinPool.Length; i++)
            {
                var c = UiKit.Image(safe, "FlyCoin_" + i, Color.white);
                c.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Coin);
                UiKit.Place(c.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 72f));
                c.gameObject.SetActive(false);
                _coinPool[i] = c.rectTransform;
            }
            _root.SetActive(false);
        }

        public void Open()
        {
            for (int i = 0; i < 3; i++) _toggles[i].SetIsOnWithoutNotify(WoolEconomy.Instance.Available((WoolType)i) > 0);
            RefreshValues();
            _root.SetActive(true);
        }

        public void Close()
        {
            if (_busy) return;
            _root.SetActive(false);
            Closed?.Invoke();
        }

        SellSelection Selection => new SellSelection { Dirty = _toggles[0].isOn, Wet = _toggles[1].isOn, Clean = _toggles[2].isOn };

        public void RefreshValues()
        {
            var e = WoolEconomy.Instance;
            for (int i = 0; i < 3; i++)
            {
                var t = (WoolType)i;
                int amount = e.Available(t);
                float price = e.Balance.UnitPrice(t);
                _rowText[i].text = Names[i] + " Wool\n<size=30>" + amount + " × " + price.ToString("0.0#") + " (x" + e.Balance.Multiplier(t).ToString("0.0") + ")</size>";
                _rowValue[i].text = e.Balance.SellValue(t, amount) + " ¢";
                _toggles[i].interactable = amount > 0;
                if (amount <= 0) _toggles[i].SetIsOnWithoutNotify(false);
            }
            long selected = e.QuoteSale(Selection);
            _total.text = "Total: " + selected + " coins";
            _sellSelected.interactable = selected > 0 && !_busy;
            _sellAll.interactable = e.QuoteSale(SellSelection.All) > 0 && !_busy;
        }

        void DoSell(bool all)
        {
            if (_busy) return;                       // double taps during the animation do nothing
            var sel = all ? SellSelection.All : Selection;
            long earned = WoolEconomy.Instance.Sell(sel); // atomic: wool removed + coins added + saved once
            RefreshValues();
            if (earned <= 0) return;
            Sold?.Invoke(earned);
            StartCoroutine(FlyCoins(earned));
        }

        IEnumerator FlyCoins(long earned)
        {
            _busy = true;
            RefreshValues();
            int count = (int)Mathf.Clamp(earned / 10 + 4, 4, _coinPool.Length);
            Vector3 from = _total.rectTransform.position;
            Vector3 to = _coinTarget.position;
            float stagger = 0.05f, dur = 0.55f;
            float start = Time.unscaledTime;
            for (int i = 0; i < count; i++) { _coinPool[i].gameObject.SetActive(true); _coinPool[i].position = from; }
            var arrived = new bool[count];
            int done = 0;
            while (done < count)
            {
                float now = Time.unscaledTime - start;
                for (int i = 0; i < count; i++)
                {
                    if (arrived[i]) continue;
                    float t = Mathf.Clamp01((now - i * stagger) / dur);
                    if (t <= 0f) continue;
                    float e = t * t * (3f - 2f * t);
                    float side = ((i * 37) % 11 - 5) * 26f;
                    Vector3 ctrl = (from + to) * 0.5f + new Vector3(side, 260f, 0f) * _safe.lossyScale.x;
                    Vector3 p = Vector3.Lerp(Vector3.Lerp(from, ctrl, e), Vector3.Lerp(ctrl, to, e), e);
                    _coinPool[i].position = p;
                    _coinPool[i].localScale = Vector3.one * Mathf.Lerp(1.1f, 0.7f, e);
                    if (t >= 1f) { arrived[i] = true; done++; _coinPool[i].gameObject.SetActive(false); CoinArrived?.Invoke(); }
                }
                yield return null;
            }
            _busy = false;
            RefreshValues();
        }
    }
}
