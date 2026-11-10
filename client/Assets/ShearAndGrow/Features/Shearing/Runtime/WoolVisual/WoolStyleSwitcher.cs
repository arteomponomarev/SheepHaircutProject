using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [DefaultExecutionOrder(100)]
    public sealed class WoolStyleSwitcher : MonoBehaviour
    {
        [Header("Whole wool appearance")]
        [SerializeField] private WoolStylePreset[] _presets;
        [SerializeField] private WoolLook _defaultLook = WoolLook.ReferencePuffs;
        [SerializeField] private WoolVisualRenderer _wool;
        [SerializeField] private ShearingSessionController _session;
        [SerializeField] private Material _clumpMaterial;
        [SerializeField] private Material _decorMaterial;
        [SerializeField] private bool _showSelector = true;
        private const string PrefKey = "ShearAndGrow.WoolLook.v2";
        private Material _clumpInstance, _decorInstance;
        private UnityEngine.UI.Dropdown _dropdown;
        private GameObject _selectorCanvas;
        private readonly List<MeshRenderer> _renderers= new List<MeshRenderer>();
        public WoolLook CurrentLook { get; private set; }

        private void Start()
        {
            _clumpInstance = new Material(_clumpMaterial);
            _decorInstance = new Material(_decorMaterial);
            _session.Hub.Clump += OnClump;
            _session.Hub.SessionClosing += HideSelector;
            _session.Hub.SessionStarted += ShowSelector;
            if (_showSelector) BuildSelector();
            SetLook((WoolLook)PlayerPrefs.GetInt(PrefKey, (int)_defaultLook));
        }

        public void SetLook(WoolLook look)
        {
            WoolStylePreset preset = null;
            foreach (var p in _presets) if (p != null && p.Look == look) { preset = p; break; }
            if (preset == null) return;
            CurrentLook = look;
            _wool.ApplyStyle(preset);
            foreach (var material in new[] { _clumpInstance, _decorInstance })
            {
                material.SetColor("_BaseColor", preset.TipColor);
                material.SetColor("_ShadeColor", preset.RootColor);
                material.SetFloat("_FuzzStrength", look == WoolLook.RealisticFleece ? 0.8f : look == WoolLook.PlushWool ? 0.45f : 0.22f);
                material.SetFloat("_CurlScale", look == WoolLook.RealisticFleece ? 80f : 32f);
                material.SetFloat("_FuzzScale", look == WoolLook.RealisticFleece ? 160f : 85f);
                material.SetFloat("_SpecStrength", 0.04f);
                material.SetFloat("_RimStrength", 0.18f);
            }
            RefreshWoolMaterials();
            if (_dropdown != null) _dropdown.SetValueWithoutNotify((int)look);
            PlayerPrefs.SetInt(PrefKey, (int)look);
            PlayerPrefs.Save();
        }

        private void HideSelector() { if (_selectorCanvas != null) _selectorCanvas.SetActive(false); }
        private void ShowSelector() { if (_selectorCanvas != null) _selectorCanvas.SetActive(true); }

        private void OnClump(ClumpEvent e)
        {
            // A static merged pile is created only on settling: give that new renderer the same live material.
            if (e.Type == ClumpEventType.Settled) RefreshWoolMaterials();
        }

        private void RefreshWoolMaterials()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                _renderers.Clear();
                root.GetComponentsInChildren(true, _renderers);
                foreach (var renderer in _renderers)
                {
                    if (renderer.sharedMaterial == _clumpMaterial) renderer.sharedMaterial = _clumpInstance;
                    else if (renderer.sharedMaterial == _decorMaterial) renderer.sharedMaterial = _decorInstance;
                }
            }
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        private static UnityEngine.UI.Text Label(Transform parent, string text, Font font, int size)
        {
            var rt = Rect("Label", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 60f));
            var label = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = font; label.fontSize = size; label.text = text;
            label.color = new Color(1f, 0.96f, 0.87f); label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private void BuildSelector()
        {
            // Existing HUD is Canvas-based; a separate small Canvas leaves its hierarchy/layout untouched.
            var go = new GameObject("WoolStyleCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _selectorCanvas = go;
            var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 21;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f); scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var safe = Rect("SafeArea", go.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var area = Screen.safeArea;
            safe.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safe.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
            var rt = Rect("Wool Style", safe, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -255f), new Vector2(540f, 76f));
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.19f, 0.14f, 0.10f, 0.95f);
            _dropdown = rt.gameObject.AddComponent<UnityEngine.UI.Dropdown>(); _dropdown.targetGraphic = image;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _dropdown.captionText = Label(rt, "Wool: Cartoon Curls  ▾", font, 30);
            var template = Rect("Template", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(540f, 234f));
            var bg = template.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(0.19f, 0.14f, 0.10f, 1f);
            var scroll = template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = false;
            var viewport = Rect("Viewport", template, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(540f, 312f));
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content = Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(540f, 78f));
            var item = Rect("Item", content, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -39f), new Vector2(540f, 78f));
            var itemBg = item.gameObject.AddComponent<UnityEngine.UI.Image>(); itemBg.color = new Color(0.36f, 0.26f, 0.18f);
            var toggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>(); toggle.targetGraphic = itemBg;
            var tick = Rect("Selected", item, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(12f, 12f));
            var tickImage = tick.gameObject.AddComponent<UnityEngine.UI.Image>(); tickImage.color = new Color(1f, 0.7f, 0.32f); tickImage.raycastTarget = false;
            toggle.graphic = tickImage;
            _dropdown.itemText = Label(item, "", font, 30);
            scroll.viewport = viewport; scroll.content = content; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            _dropdown.template = template;
            _dropdown.options = new List<UnityEngine.UI.Dropdown.OptionData>
            {
                new UnityEngine.UI.Dropdown.OptionData("Wool: Realistic Fleece"),
                new UnityEngine.UI.Dropdown.OptionData("Wool: Cartoon Curls"),
                new UnityEngine.UI.Dropdown.OptionData("Wool: Plush Wool"),
                new UnityEngine.UI.Dropdown.OptionData("Wool: Reference Puffs")
            };
            template.gameObject.SetActive(false);
            _dropdown.onValueChanged.AddListener(index => SetLook((WoolLook)index));
        }

        private void OnDestroy()
        {
            if (_session != null && _session.Hub != null)
            {
                _session.Hub.Clump -= OnClump;
                _session.Hub.SessionClosing -= HideSelector;
                _session.Hub.SessionStarted -= ShowSelector;
            }
            if (_clumpInstance != null) Destroy(_clumpInstance);
            if (_decorInstance != null) Destroy(_decorInstance);
        }
    }
}
