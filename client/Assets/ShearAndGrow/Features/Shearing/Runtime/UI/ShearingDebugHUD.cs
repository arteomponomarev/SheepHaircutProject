using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// QA overlay (spec §12.1, §14.3): FPS and frame time p50/p95/p99, GC heap & collections, active soft bodies,
    /// hanging/falling, floor clump counts (individual + merged), last new cut delta, haptic calls.
    /// </summary>
    public sealed class ShearingDebugHUD : MonoBehaviour
    {
        [SerializeField] private float _refreshInterval = 0.25f;
        [SerializeField] private int _frameWindow = 240;

        ShearingSessionController _session;
        Text _text;
        GameObject _root;
        float[] _frames;
        float[] _sorted;
        int _frameIndex, _frameCount;
        float _timer;
        int _maxFloor;
        readonly StringBuilder _sb = new StringBuilder(1024);
        int _gcAtStart;

        public void Initialise(ShearingSessionController session)
        {
            _session = session;
            _frames = new float[_frameWindow];
            _sorted = new float[_frameWindow];
            _gcAtStart = System.GC.CollectionCount(0);

            var canvasGo = new GameObject("DebugCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo;

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(16f, -270f);
            prt.sizeDelta = new Vector2(560f, 540f);
            var img = panel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.45f);
            img.raycastTarget = false;

            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(panel.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 8f); trt.offsetMax = new Vector2(-12f, -8f);
            _text = tgo.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 24;
            _text.color = new Color(0.85f, 1f, 0.85f, 1f);
            _text.raycastTarget = false;
            _text.alignment = TextAnchor.UpperLeft;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        void Update()
        {
            if (_session == null || _frames == null) return;
            _frames[_frameIndex] = Time.unscaledDeltaTime * 1000f;
            _frameIndex = (_frameIndex + 1) % _frames.Length;
            _frameCount = Mathf.Min(_frameCount + 1, _frames.Length);
            _maxFloor = Mathf.Max(_maxFloor, _session.Floor.TotalFloorClumps);

            if (_root == null || !_root.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = _refreshInterval;

            System.Array.Copy(_frames, _sorted, _frameCount);
            System.Array.Sort(_sorted, 0, _frameCount);
            float p50 = Percentile(0.5f), p95 = Percentile(0.95f), p99 = Percentile(0.99f);
            float avg = 0f;
            for (int i = 0; i < _frameCount; i++) avg += _sorted[i];
            avg /= Mathf.Max(1, _frameCount);

            var hm = _session.Haptics != null ? _session.Haptics.Manager : null;
            _sb.Clear();
            _sb.Append("FPS ").Append((1000f / Mathf.Max(0.01f, avg)).ToString("0")).Append("  avg ").Append(avg.ToString("0.0")).Append(" ms\n");
            _sb.Append("frame p50/p95/p99: ").Append(p50.ToString("0.0")).Append(" / ").Append(p95.ToString("0.0")).Append(" / ").Append(p99.ToString("0.0")).Append(" ms\n");
            _sb.Append("GC heap ").Append((Profiler.GetMonoUsedSizeLong() / (1024f * 1024f)).ToString("0.0")).Append(" MB, gen0 GCs ").Append(System.GC.CollectionCount(0) - _gcAtStart).Append('\n');
            _sb.Append("Total alloc ").Append((Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f)).ToString("0")).Append(" MB\n");
            _sb.Append("progress ").Append((_session.Progress * 100f).ToString("0.00")).Append("%  cutEvents ").Append(_session.CutEventCount).Append('\n');
            _sb.Append("newCutDelta ").Append((_session.LastFrameNewArea * 100f).ToString("0.000")).Append("%  contact ").Append(_session.InContact ? "Y" : "n").Append('\n');
            _sb.Append("soft active ").Append(_session.Clumps.ActiveCount).Append(" (hang ").Append(_session.Clumps.HangingCount).Append(", fall ").Append(_session.Clumps.FallingCount).Append(")\n");
            _sb.Append("floor visible ").Append(_session.Floor.TotalFloorClumps).Append(" (indiv ").Append(_session.Floor.IndividualSettledCount)
               .Append(", merged ").Append(_session.Floor.MergedClumpCount).Append(" in ").Append(_session.Floor.MergedPileMeshes).Append(" piles)\n");
            _sb.Append("max floor ").Append(_maxFloor).Append("  detached ").Append(_session.Clumps.TotalDetached).Append("  pool ").Append(_session.Clumps.PoolAvailable).Append('\n');
            if (hm != null)
            {
                _sb.Append("haptics: ").Append(hm.Enabled ? "ON" : "OFF").Append(" ").Append(hm.Platform.PlatformName)
                   .Append("  profile ").Append(hm.Profile != null ? hm.Profile.DisplayName : "-")
                   .Append("  native ").Append(hm.Platform.NativeCallCount)
                   .Append("  events ").Append(hm.SemanticEvents).Append("  limited ").Append(hm.SuppressedEvents)
                   .Append(hm.ContinuousRunning ? "  [texture]" : "").Append('\n');
            }
            _sb.Append("audio cut ").Append(_session.AudioDirector.CutIntensity.ToString("0.00")).Append("  oneshots ").Append(_session.AudioDirector.OneShotsPlayed).Append('\n');
            _sb.Append("session #").Append(_session.SessionIndex).Append("  ").Append(_session.LastRestoreInfo).Append('\n');
            if (_session.State != null)
                _sb.Append("credited ").Append((_session.State.creditedCutArea * 100f).ToString("0.0")).Append("%  commits ").Append(_session.State.commitCount)
                   .Append("  flags ").Append(_session.State.completionFlags);
            _text.text = _sb.ToString();
        }

        float Percentile(float p)
        {
            if (_frameCount == 0) return 0f;
            int idx = Mathf.Clamp(Mathf.CeilToInt(p * _frameCount) - 1, 0, _frameCount - 1);
            return _sorted[idx];
        }
    }
}
