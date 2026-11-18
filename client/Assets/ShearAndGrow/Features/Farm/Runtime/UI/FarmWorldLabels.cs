using System.Collections.Generic;
using ShearAndGrow.Processing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Small floating name tags above interactables (tells the player what can be tapped) with an attention dot when
    /// something can be done there. One pooled uGUI element per interactable; positions update only when the camera
    /// or a sheep moved; texts update only on data change (<see cref="RefreshTexts"/>).
    /// </summary>
    public sealed class FarmWorldLabels : MonoBehaviour
    {
        sealed class Tag { public FarmInteractable Target; public RectTransform Rt; public Text Text; public Image Badge; public CanvasGroup Group; }

        readonly List<Tag> _tags = new List<Tag>();
        RectTransform _root;
        Camera _cam;
        FarmHubController _hub;
        Vector3 _lastCamPos;
        float _lastSize = -1f;
        Quaternion _lastCamRot;
        bool _lastOrtho;
        float _lastFov;

        public void Build(FarmHubController hub, Camera cam, RectTransform safe, IEnumerable<FarmInteractable> targets)
        {
            _hub = hub; _cam = cam;
            _root = UiKit.Rect("WorldLabels", safe);
            UiKit.Stretch(_root);
            _root.SetAsFirstSibling(); // below the HUD and panels
            foreach (var t in targets) Add(t, false);
            RefreshTexts();
        }

        /// <summary>Removes the tag of a despawned / stored building.</summary>
        public void Remove(FarmInteractable t)
        {
            for (int i = _tags.Count - 1; i >= 0; i--)
                if (_tags[i].Target == t) { Destroy(_tags[i].Rt.gameObject); _tags.RemoveAt(i); }
        }

        /// <summary>Adds a tag for a newly spawned building (no-op if it already has one).</summary>
        public void Add(FarmInteractable t, bool refresh = true)
        {
            if (_root == null || t == null || !t.ShowLabel) return;
            foreach (var e in _tags) if (e.Target == t) return;
            {
                var bg = UiKit.Image(_root, "Tag_" + t.BuildingId, new Color(0.18f, 0.13f, 0.1f, 0.78f));
                var rt = bg.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(260f, 58f);
                var txt = UiKit.Text(rt, "Text", t.DisplayName, 32, TextAnchor.MiddleCenter);
                UiKit.Stretch(txt.rectTransform);
                var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                var hl = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.padding = new RectOffset(22, 22, 6, 6); hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = false;
                var badge = UiKit.Image(rt, "Badge", UiKit.Accent);
                badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                var brt = badge.rectTransform;
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = new Vector2(-4f, -4f); brt.sizeDelta = new Vector2(26f, 26f);
                badge.sprite = ProceduralIcons.Get(ProceduralIcons.Kind.Coin, UiKit.Accent);
                var group = bg.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false; group.interactable = false;
                _tags.Add(new Tag { Target = t, Rt = rt, Text = txt, Badge = badge, Group = group });
            }
            if (refresh) RefreshTexts();
        }

        public void RefreshTexts()
        {
            foreach (var t in _tags)
            {
                t.Text.text = t.Target.LabelText(_hub);
                t.Badge.enabled = t.Target.HasAttention(_hub);
            }
            _lastSize = -1f; // force reposition
        }

        public void SetVisible(bool on) { if (_root != null) _root.gameObject.SetActive(on); }

        RectTransform _canvasRt;

        Vector2 _lastCanvasSize;
        float _lastScaleFactor, _lastAspect;
        Vector2Int _lastScreen;
        Rect _lastSafeArea;

        /// <summary>
        /// A tag's screen position depends on the camera pose AND on the canvas it is drawn in. The canvas only gets
        /// its final size/scale after the CanvasScaler / safe-area pass (after our first LateUpdate on a cold start),
        /// so every input of the projection is tracked; any change repositions all tags. (Bug: positions used to be
        /// refreshed on camera moves only → wrong until the first touch moved the camera.)
        /// </summary>
        void OnEnable() => Canvas.willRenderCanvases += OnWillRenderCanvases;
        void OnDisable() => Canvas.willRenderCanvases -= OnWillRenderCanvases;

        // Runs after the CanvasScaler / layout pass of this frame, right before UI is drawn: if the canvas size or
        // scale changed after LateUpdate (cold start), tags are corrected before they are ever rendered.
        void OnWillRenderCanvases() => LateUpdate();

        void LateUpdate()
        {
            if (_cam == null || _root == null) return;
            if (_canvasRt == null) _canvasRt = (RectTransform)_root.GetComponentInParent<Canvas>().rootCanvas.transform;
            var canvas = _canvasRt.GetComponent<Canvas>();
            bool camMoved = (_cam.transform.position - _lastCamPos).sqrMagnitude > 1e-6f || Mathf.Abs(_cam.orthographicSize - _lastSize) > 1e-4f
                            || Mathf.Abs(_cam.aspect - _lastAspect) > 1e-5f || _cam.transform.rotation != _lastCamRot
                            || _cam.orthographic != _lastOrtho || Mathf.Abs(_cam.fieldOfView - _lastFov) > 1e-4f;
            _lastCamRot = _cam.transform.rotation; _lastOrtho = _cam.orthographic; _lastFov = _cam.fieldOfView;
            bool layoutChanged = _canvasRt.rect.size != _lastCanvasSize || Mathf.Abs(canvas.scaleFactor - _lastScaleFactor) > 1e-5f
                                 || _lastScreen.x != Screen.width || _lastScreen.y != Screen.height || Screen.safeArea != _lastSafeArea;
            _lastCamPos = _cam.transform.position; _lastSize = _cam.orthographicSize; _lastAspect = _cam.aspect;
            _lastCanvasSize = _canvasRt.rect.size; _lastScaleFactor = canvas.scaleFactor;
            _lastScreen = new Vector2Int(Screen.width, Screen.height); _lastSafeArea = Screen.safeArea;
            Reposition(!(camMoved || layoutChanged));
        }

        /// <summary>Forces a full reposition on the next LateUpdate.</summary>
        public void MarkDirty()=> _lastCanvasSize = new Vector2(-1f, -1f);

        /// <summary>
        /// Places tags from viewport coordinates relative to the root canvas, so it works for any resolution, aspect,
        /// safe area or canvas render mode. <paramref name="movingOnly"/> limits the work to moving targets (sheep).
        /// </summary>
        public void Reposition(bool movingOnly = false)
        {
            if (_canvasRt == null) _canvasRt = (RectTransform)_root.GetComponentInParent<Canvas>().rootCanvas.transform;
            Vector2 canvasSize = _canvasRt.rect.size;
            for (int i = 0; i < _tags.Count; i++)
            {
                var t = _tags[i];
                if (t.Target == null) continue;
                if (movingOnly && !t.Target.MovesEveryFrame) continue;
                if (!t.Target.isActiveAndEnabled) { t.Group.alpha = 0f; continue; }
                Vector3 vp = _cam.WorldToViewportPoint(t.Target.LabelPosition);
                bool visible = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
                t.Group.alpha = visible ? 1f : 0f;
                if (!visible) continue;
                Vector2 canvasLocal = new Vector2((vp.x - 0.5f) * canvasSize.x, (vp.y - 0.5f) * canvasSize.y + 8f);
                t.Rt.position = _canvasRt.TransformPoint(canvasLocal);
            }
        }
    }
}
