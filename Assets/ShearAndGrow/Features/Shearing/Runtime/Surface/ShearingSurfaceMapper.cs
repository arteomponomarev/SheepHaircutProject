using UnityEngine;

namespace ShearAndGrow.Shearing
{
    public struct SurfaceHit
    {
        public float U, V;
        public Vector3 LocalPoint;   // on the base (undercoat) surface, body object space
        public Vector3 LocalNormal;
        public Vector3 WorldPoint;
        public Vector3 WorldNormal;
    }

    /// <summary>
    /// Raycast → surface parametrisation → interpolated brush stamps (spec §5.3). Stamps are spaced at
    /// <see cref="ShearingFeelConfig.StampSpacing"/> × radius so fast swipes never leave holes. Segments never bridge
    /// through empty space: a miss or an implausible jump starts a new segment.
    /// </summary>
    public sealed class ShearingSurfaceMapper
    {
        public delegate void StampHandler(Vector3 localPoint, Vector3 localNormal, Vector3 localTangent, int strokeId);

        readonly Camera _camera;
        readonly Collider _collider;
        readonly Transform _body;
        readonly SheepBodySurface _surface;
        readonly ShearingFeelConfig _feel;

        bool _hasLast;
        Vector3 _lastLocal;
        Vector3 _lastNormal;
        Vector3 _lastTangent;
        int _strokeId;

        public int StrokeId => _strokeId;
        public bool HasActiveSegment => _hasLast;
        public Vector3 LastTangentLocal => _lastTangent;

        public ShearingSurfaceMapper(Camera camera, Collider collider, Transform body, SheepBodySurface surface, ShearingFeelConfig feel)
        {
            _camera = camera;
            _collider = collider;
            _body = body;
            _surface = surface;
            _feel = feel;
        }

        public bool Raycast(Vector2 screen, out SurfaceHit hit)
        {
            hit = default;
            Ray ray = _camera.ScreenPointToRay(screen);
            if (!_collider.Raycast(ray, out RaycastHit rh, 50f)) return false;
            Vector2 uv = rh.textureCoord;
            hit.U = uv.x;
            hit.V = uv.y;
            hit.LocalPoint = _surface.GetPoint(uv.x, uv.y);
            hit.LocalNormal = _surface.GetNormal(uv.x, uv.y);
            hit.WorldPoint = _body.TransformPoint(hit.LocalPoint);
            hit.WorldNormal = _body.TransformDirection(hit.LocalNormal).normalized;
            return true;
        }

        /// <summary>Ends the current segment (release, leaving the model, focus loss).</summary>
        public void EndSegment() => _hasLast = false;

        /// <summary>
        /// Feeds the exact (unsmoothed) contact for this frame and emits interpolated stamps.
        /// </summary>
        public void Feed(in SurfaceHit hit, StampHandler onStamp)
        {
            float radius = _feel.BrushRadius;
            if (!_hasLast)
            {
                _strokeId++;
                _hasLast = true;
                _lastLocal = hit.LocalPoint;
                _lastNormal = hit.LocalNormal;
                onStamp(hit.LocalPoint, hit.LocalNormal, _lastTangent, _strokeId);
                return;
            }

            Vector3 delta = hit.LocalPoint - _lastLocal;
            float dist = delta.magnitude;
            if (dist > _feel.MaxSegmentJoinDistance)
            {
                // implausible jump (e.g. across the silhouette) – new segment, never draw a line through air
                _strokeId++;
                _lastLocal = hit.LocalPoint;
                _lastNormal = hit.LocalNormal;
                onStamp(hit.LocalPoint, hit.LocalNormal, _lastTangent, _strokeId);
                return;
            }

            float step = Mathf.Max(1e-4f, radius * _feel.StampSpacing);
            if (dist < step * 0.5f) return; // wait for meaningful movement; the last stamp already covers this spot
            Vector3 tangent = delta / dist;
            _lastTangent = tangent;
            int steps = Mathf.CeilToInt(dist / step);
            for (int s = 1; s <= steps; s++)
            {
                float t = s / (float)steps;
                Vector3 p = Vector3.Lerp(_lastLocal, hit.LocalPoint, t);
                Vector3 n = Vector3.Slerp(_lastNormal, hit.LocalNormal, t);
                onStamp(p, n, tangent, _strokeId);
            }
            _lastLocal = hit.LocalPoint;
            _lastNormal = hit.LocalNormal;
        }
    }
}
