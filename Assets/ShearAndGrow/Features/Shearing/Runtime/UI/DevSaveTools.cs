using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Development-only "Reset Save" button (Editor + Development builds). Simulates a first launch on a clean device:
    /// deletes all sheep saves of this install and restarts from <see cref="DefaultGameState"/>. Requires a second tap
    /// within 3 s to confirm. In release builds the component removes itself before creating any UI.
    /// </summary>
    public sealed class DevSaveTools : MonoBehaviour
    {
        [SerializeField] private ShearingSessionController _session;
        [SerializeField] private float _confirmWindow = 3f;

        UnityEngine.UI.Text _label;
        float _armedUntil = -1f;

        void Start()
        {
            if (!Debug.isDebugBuild || _session == null) { Destroy(this); return; }
            Build();
        }

        void Update()
        {
            if (_label != null && _armedUntil > 0f && Time.unscaledTime > _armedUntil)
            {
                _armedUntil = -1f;
                _label.text = "DEV: Reset Save";
            }
        }

        void OnPressed()
        {
            if (_armedUntil < 0f)
            {
                _armedUntil = Time.unscaledTime + _confirmWindow;
                _label.text = "Tap again to WIPE";
                return;
            }
            _armedUntil = -1f;
            int n = _session.DevResetSave();
            _label.text = "Reset (" + n + " file" + (n == 1 ? "" : "s") + ")";
            _armedUntil = Time.unscaledTime + 1.5f;
        }

        void Build()
        {
            var canvasGo = new GameObject("DevSaveCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 39;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var safeGo = new GameObject("SafeArea", typeof(RectTransform));
            safeGo.transform.SetParent(canvasGo.transform, false);
            var safe = (RectTransform)safeGo.transform;
            Rect sa = Screen.safeArea;
            safe.anchorMin = new Vector2(sa.xMin / Mathf.Max(1, Screen.width), sa.yMin / Mathf.Max(1, Screen.height));
            safe.anchorMax = new Vector2(sa.xMax / Mathf.Max(1, Screen.width), sa.yMax / Mathf.Max(1, Screen.height));
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            var btnGo = new GameObject("DevResetSave", typeof(RectTransform));
            btnGo.transform.SetParent(safe, false);
            var rt = (RectTransform)btnGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -430f); // below the "Haptics" dev button
            rt.sizeDelta = new Vector2(240f, 72f);
            var img = btnGo.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.45f, 0.12f, 0.1f, 0.9f);
            var btn = btnGo.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnPressed);

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(rt, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            _label = txtGo.AddComponent<UnityEngine.UI.Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 26;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = new Color(1f, 0.95f, 0.9f);
            _label.raycastTarget = false;
            _label.text = "DEV: Reset Save";
        }
    }
}
