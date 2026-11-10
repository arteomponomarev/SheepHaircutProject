using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Minimal HUD for Wash / Dry: Back, small progress pill with the completion tick, first-use hint with an
    /// animated finger, leave-confirmation, result card and empty state. Updates text only when values change.
    /// </summary>
    public sealed class ProcessingHUD : MonoBehaviour
    {
        public event Action BackPressed;
        public event Action LeaveConfirmed;
        public event Action ContinuePressed;   // result card: next step (Dry now / Sell)
        public event Action MenuPressed;

        RectTransform _safe;
        Button _back;
        Text _progressText, _amountText, _resultTitle, _resultBody, _hintText;
        RectTransform _barFill, _finger;
        GameObject _confirm, _result, _empty, _hint;
        Button _continueButton;
        Text _continueLabel;
        Image _fade;
        Color _accent;
        int _lastPct = -1;
        string _label = "";
        float _hintT;

        public bool IsConfirmOpen => _confirm != null && _confirm.activeSelf;

        public void Build(string title, string progressLabel, string hint, Color accent)
        {
            _label = progressLabel;
            _accent = accent;
            UiKit.CreateCanvas(transform, "ProcessingCanvas", 10, out _safe);

            _back = UiKit.Button(_safe, "Back", "← Back", 36, UiKit.Panel, () => BackPressed?.Invoke(), out _);
            UiKit.Place((RectTransform)_back.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(230f, 96f));

            // progress pill (top centre, compact – the wool keeps the screen)
            var pill = UiKit.Image(_safe, "ProgressPill", UiKit.Panel);
            UiKit.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(110f, -30f), new Vector2(560f, 96f));
            _progressText = UiKit.Text(pill.transform, "Label", progressLabel + " 0%", 32, TextAnchor.UpperCenter);
            UiKit.Place(_progressText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(540f, 44f));
            var barBack = UiKit.Image(pill.transform, "BarBack", new Color(1f, 1f, 1f, 0.15f));
            UiKit.Place(barBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(500f, 18f));
            var fill = UiKit.Image(barBack.transform, "Fill", accent);
            _barFill = fill.rectTransform;
            _barFill.anchorMin = Vector2.zero; _barFill.anchorMax = new Vector2(0f, 1f); _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.offsetMin = _barFill.offsetMax = Vector2.zero;
            _amountText = UiKit.Text(_safe, "Amount", "", 30, TextAnchor.MiddleCenter, UiKit.TextMuted);
            UiKit.Place(_amountText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(110f, -132f), new Vector2(560f, 40f));

            // first-use hint
            _hint = UiKit.Rect("Hint", _safe).gameObject;
            var hintRt = (RectTransform)_hint.transform;
            UiKit.Place(hintRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(900f, 200f));
            _hintText = UiKit.Text(hintRt, "Text", hint, 44, TextAnchor.LowerCenter);
            UiKit.Place(_hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 70f));
            UiKit.SoftShadow(_hintText);
            var finger = UiKit.Image(hintRt, "Finger", new Color(1f, 1f, 1f, 0.85f));
            _finger = finger.rectTransform;
            UiKit.Place(_finger, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(56f, 56f));
            var ring = UiKit.Image(_finger, "Ring", new Color(1f, 1f, 1f, 0.35f));
            UiKit.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 90f));
            ring.transform.SetAsFirstSibling();

            _confirm = BuildConfirm();
            _result = BuildResult(out _resultTitle, out _resultBody);
            _empty = BuildEmpty(title);
            _confirm.SetActive(false); _result.SetActive(false); _empty.SetActive(false);

            var fade = UiKit.Image(transform.GetChild(0), "Fade", new Color(0.08f, 0.06f, 0.05f, 1f), true);
            UiKit.Stretch(fade.rectTransform);
            _fade = fade;
        }

        GameObject BuildConfirm()
        {
            var dim = UiKit.Image(_safe, "ConfirmDim", new Color(0f, 0f, 0f, 0.5f), true);
            UiKit.Stretch(dim.rectTransform);
            var card = UiKit.Image(dim.transform, "Card", UiKit.Panel, true);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(840f, 460f));
            var t = UiKit.Text(card.transform, "Title", "Leave now?", 52, TextAnchor.MiddleCenter);
            UiKit.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(780f, 80f));
            var b = UiKit.Text(card.transform, "Body", "Your progress here resets.\nYour wool stays safe in storage.", 34, TextAnchor.MiddleCenter, UiKit.TextMuted);
            UiKit.Place(b.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(780f, 110f));
            var stay = UiKit.Button(card.transform, "Stay", "Keep going", 36, UiKit.Accent, () => _confirm.SetActive(false), out _);
            UiKit.Place((RectTransform)stay.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-15f, 40f), new Vector2(360f, 110f));
            var leave = UiKit.Button(card.transform, "Leave", "Leave", 36, UiKit.PanelLight, () => { _confirm.SetActive(false); LeaveConfirmed?.Invoke(); }, out _);
            UiKit.Place((RectTransform)leave.transform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(15f, 40f), new Vector2(360f, 110f));
            return dim.gameObject;
        }

        GameObject BuildResult(out Text title, out Text body)
        {
            var card = UiKit.Image(_safe, "ResultCard", UiKit.Panel, true);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(900f, 470f));
            title = UiKit.Text(card.transform, "Title", "", 56, TextAnchor.MiddleCenter);
            UiKit.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(860f, 80f));
            body = UiKit.Text(card.transform, "Body", "", 46, TextAnchor.MiddleCenter, UiKit.Accent);
            UiKit.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(860f, 70f));
            _continueButton = UiKit.Button(card.transform, "Continue", "", 38, UiKit.Accent, () => ContinuePressed?.Invoke(), out _continueLabel);
            UiKit.Place((RectTransform)_continueButton.transform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-15f, 40f), new Vector2(410f, 120f));
            var menu = UiKit.Button(card.transform, "Menu", "Menu", 38, UiKit.PanelLight, () => MenuPressed?.Invoke(), out _);
            UiKit.Place((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(15f, 40f), new Vector2(410f, 120f));
            return card.gameObject;
        }

        GameObject BuildEmpty(string title)
        {
            var card = UiKit.Image(_safe, "EmptyCard", UiKit.Panel, true);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 420f));
            var t = UiKit.Text(card.transform, "Title", title, 52, TextAnchor.MiddleCenter);
            UiKit.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 80f));
            var b = UiKit.Text(card.transform, "Body", "", 36, TextAnchor.MiddleCenter, UiKit.TextMuted);
            UiKit.Place(b.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(800f, 120f));
            b.name = "EmptyBody";
            var menu = UiKit.Button(card.transform, "Menu", "Back to menu", 38, UiKit.Accent, () => MenuPressed?.Invoke(), out _);
            UiKit.Place((RectTransform)menu.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(520f, 120f));
            return card.gameObject;
        }

        public void SetAmount(string text) => _amountText.text = text;

        public void SetProgress(float progress01, float threshold)
        {
            // show progress relative to the completion threshold, so the bar is full exactly at completion
            float shown = Mathf.Clamp01(progress01 / Mathf.Max(0.01f, threshold));
            int pct = Mathf.FloorToInt(shown * 100f);
            _barFill.anchorMax = new Vector2(shown, 1f);
            if (pct != _lastPct) { _lastPct = pct; _progressText.text = _label + " " + pct + "%"; }
        }

        public void ShowHint(bool show) { if (_hint != null && _hint.activeSelf != show) _hint.SetActive(show); }
        public void ShowConfirm() => _confirm.SetActive(true);
        public void HideConfirm() => _confirm.SetActive(false);

        public void ShowEmpty(string body)
        {
            _empty.SetActive(true);
            _empty.transform.Find("EmptyBody").GetComponent<Text>().text = body;
            ShowHint(false);
        }

        public void ShowResult(string title, string body, string continueLabel)
        {
            _resultTitle.text = title;
            _resultBody.text = body;
            _continueLabel.text = continueLabel;
            _continueButton.gameObject.SetActive(!string.IsNullOrEmpty(continueLabel));
            _result.SetActive(true);
            _back.gameObject.SetActive(false);
            ShowHint(false);
            StartCoroutine(Pop(_result.transform));
        }

        IEnumerator Pop(Transform t)
        {
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.28f);
                float s = 1f + Mathf.Sin(a * Mathf.PI) * 0.06f;
                t.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Mathf.SmoothStep(0f, 1f, a)) * s;
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        public IEnumerator Fade(float to, float seconds)
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

        void Update()
        {
            if (_hint != null && _hint.activeSelf)
            {
                _hintT += Time.unscaledDeltaTime;
                float x = Mathf.Sin(_hintT * 2.2f) * 180f;
                _finger.anchoredPosition = new Vector2(x, 130f + Mathf.Abs(Mathf.Cos(_hintT * 2.2f)) * 14f);
            }
        }
    }
}
