using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Clash-of-Clans-style farm camera. Orthographic, rotation fixed forever (classic 3/4 isometric) – only the
    /// ground focus point and the orthographic size change, so the same framing works for 3D models today and
    /// pre-rendered 2.5D sprites later.
    ///   • Drag: the ground point under the finger stays under the finger (1:1 at any zoom), light inertia.
    ///   • Pinch / wheel: zoom toward the fingers / cursor (that ground point stays put).
    ///   • Focus point is clamped so the visible area stays inside the farm bounds (soft spring at the edge).
    /// Pure transform math – no physics, no extra cameras.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FarmCameraController : MonoBehaviour
    {
        [Header("Fixed angle (never changes at runtime)")]
        [Tooltip("3/4 top-down pitch. Portrait phones read best with yaw 0 (farm depth runs up the screen).")]
        [SerializeField] private float _pitch = 48f;
        [SerializeField] private float _yaw = 0f;
        [SerializeField] private float _distance = 40f;
        [Tooltip("Area that must fit on screen at maximum zoom-out (all buildings + pen), incl. building height.")]
        [SerializeField] private Bounds _coreBounds = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(12f, 3f, 18f));
        [Tooltip("Share of the screen height covered by the top HUD (kept free at max zoom-out).")]
        [SerializeField, Range(0f, 0.3f)] private float _topUiFraction = 0.15f;

        [Header("Zoom (orthographic size)")]
        [SerializeField] private float _minSize = 3.2f;
        [Tooltip("Hard upper limit; the effective maximum is exactly 'whole farm visible' when smaller.")]
        [SerializeField] private float _maxSize = 20f;
        [SerializeField] private float _wheelStep = 0.12f;
        [SerializeField] private float _zoomSmoothing = 18f;

        [Header("Pan")]
        [Tooltip("Inertia decay per second after a drag (higher = stops sooner).")]
        [SerializeField] private float _inertiaDamping = 5.5f;
        [SerializeField] private float _maxInertiaSpeed = 30f;
        [Tooltip("How fast the focus springs back inside the bounds.")]
        [SerializeField] private float _edgeSpring = 12f;
        [SerializeField] private float _focusSmoothTime = 0.28f;

        [Header("Bounds")]
        [SerializeField] private Bounds _farmBounds = new Bounds(Vector3.zero, new Vector3(24f, 1f, 28f));
        [Tooltip("Extra margin (m) the view may show beyond the farm edge.")]
        [SerializeField] private float _edgeMargin = 1.5f;

        Camera _cam;
        Vector3 _focus;            // ground point at the screen centre (y = 0)
        float _size, _targetSize;
        Vector3 _velocity;
        bool _dragging;
        bool _focusing;
        Vector3 _focusTarget, _focusVel;

        public Vector3 Focus => _focus;
        /// <summary>Where the camera is heading (focus target while auto-framing) – used to save/restore the view.</summary>
        public Vector3 SettledFocus => ClampFocus(_focusing ? _focusTarget : _focus, _targetSize);
        public float Size => _size;
        public float TargetSize => _targetSize;
        public float MinSize => _minSize;
        public float MaxSize => EffectiveMaxSize();
        public Quaternion FixedRotation => Quaternion.Euler(_pitch, _yaw, 0f);
        public Camera Camera => _cam != null ? _cam : (_cam = GetComponent<Camera>());
        public bool IsMoving => _dragging || _velocity.sqrMagnitude > 0.01f || _focusing || Mathf.Abs(_size - _targetSize) > 0.002f;
        public Bounds FarmBounds { get => _farmBounds; set => _farmBounds = value; }

        void Awake() => Initialise(_farmBounds, _coreBounds);

        /// <summary>
        /// Sets the pannable farm bounds and the core area (both from <see cref="FarmLayout"/>) and shows the whole farm.
        /// </summary>
        public void Initialise(Bounds farmBounds, Bounds coreBounds)
        {
            _farmBounds = farmBounds;
            _coreBounds = coreBounds;
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _velocity = Vector3.zero; _focusing = false; _dragging = false;
            ShowWholeFarm(true);
        }

        // ------------------------------------------------------------------ math helpers

        /// <summary>Ground-plane point under a screen position for the CURRENT camera pose.</summary>
        public Vector3 ScreenToGround(Vector2 screen)
        {
            Ray r = Camera.ScreenPointToRay(screen);
            float t = -r.origin.y / (Mathf.Abs(r.direction.y) < 1e-5f ? -1e-5f : r.direction.y);
            return r.origin + r.direction * t;
        }

        /// <summary>Half extents of the visible ground area along the camera's flat right / forward axes.</summary>
        Vector2 VisibleHalfExtents(float size)
        {
            float aspect = Mathf.Max(0.1f, Camera.aspect);
            float halfW = size * aspect;
            float halfH = size / Mathf.Max(0.2f, Mathf.Sin(_pitch * Mathf.Deg2Rad)); // ground foreshortening
            return new Vector2(halfW, halfH);
        }

        /// <summary>Orthographic size at which the whole core area (all buildings, incl. their height) fits on screen.</summary>
        public float FitCoreSize()
        {
            float aspect = Mathf.Max(0.1f, Camera.aspect);
            Quaternion rot = FixedRotation;
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
            float maxR = 0f, maxUp = 0f;
            Vector3 c = _coreBounds.center, e = _coreBounds.extents;
            Vector3 centreGround = new Vector3(c.x, 0f, c.z);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 d = p - centreGround;
                maxR = Mathf.Max(maxR, Mathf.Abs(Vector3.Dot(d, right)));
                maxUp = Mathf.Max(maxUp, Mathf.Abs(Vector3.Dot(d, up)));
            }
            float usableH = 1f - _topUiFraction;
            return Mathf.Max(maxR / aspect, maxUp / usableH) * 1.06f;
        }

        /// <summary>Zoom-out limit: exactly "whole farm visible" (never more than the serialized hard maximum).</summary>
        float EffectiveMaxSize() => Mathf.Clamp(FitCoreSize(), _minSize, _maxSize);

        /// <summary>Default view: whole farm, centred on the core (with the HUD band kept free).</summary>
        public void ShowWholeFarm(bool immediate)
        {
            float s = EffectiveMaxSize();
            Vector3 c = new Vector3(_coreBounds.center.x, 0f, _coreBounds.center.z);
            // shift down a little so the top HUD band doesn't cover the farthest buildings
            Vector3 fwd = Vector3.ProjectOnPlane(FixedRotation * Vector3.forward, Vector3.up).normalized;
            c += fwd * (_topUiFraction * VisibleHalfExtents(s).y);
            if (immediate) SetView(c, s);
            else { _targetSize = s; _focusTarget = c; _focusing = true; }
        }

        public Bounds CoreBounds { get => _coreBounds; set => _coreBounds = value; }

        Vector3 ClampFocus(Vector3 f, float size)
        {
            float yaw = _yaw * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(yaw)), sn = Mathf.Abs(Mathf.Sin(yaw));
            Vector2 h = VisibleHalfExtents(size);
            float viewX = h.x * c + h.y * sn, viewZ = h.x * sn + h.y * c;
            float limX = Mathf.Max(0f, _farmBounds.extents.x + _edgeMargin - viewX);
            float limZ = Mathf.Max(0f, _farmBounds.extents.z + _edgeMargin - viewZ);
            return new Vector3(Mathf.Clamp(f.x, _farmBounds.center.x - limX, _farmBounds.center.x + limX), 0f,
                               Mathf.Clamp(f.z, _farmBounds.center.z - limZ, _farmBounds.center.z + limZ));
        }

        void Apply()
        {
            Quaternion rot = FixedRotation;
            transform.SetPositionAndRotation(_focus - rot * Vector3.forward * _distance, rot);
            Camera.orthographicSize = _size;
        }

        // ------------------------------------------------------------------ input API

        public void BeginDrag() { _dragging = true; _velocity = Vector3.zero; _focusing = false; }

        /// <summary>Pan so that the ground under <paramref name="from"/> moves to <paramref name="to"/> (1:1).</summary>
        public void Drag(Vector2 from, Vector2 to, float dt)
        {
            Vector3 delta = ScreenToGround(from) - ScreenToGround(to);
            delta.y = 0f;
            _focus += delta;
            Apply();
            if (dt > 1e-4f) _velocity = Vector3.Lerp(_velocity, Vector3.ClampMagnitude(delta / dt, _maxInertiaSpeed), 0.5f);
        }

        public void EndDrag() { _dragging = false; }

        /// <summary>Pans by a ground-plane delta without inertia (edge pan while placing). Clamped to the farm.</summary>
        public void PanBy(Vector3 worldDelta)
        {
            _focusing = false; _velocity = Vector3.zero;
            _focus = ClampFocus(_focus + new Vector3(worldDelta.x, 0f, worldDelta.z), _size);
            Apply();
        }

        /// <summary>Flat (ground) right / forward axes of the fixed camera.</summary>
        public void GroundAxes(out Vector3 right, out Vector3 forward)
        {
            right = Vector3.ProjectOnPlane(FixedRotation * Vector3.right, Vector3.up).normalized;
            forward = Vector3.ProjectOnPlane(FixedRotation * Vector3.forward, Vector3.up).normalized;
        }

        /// <summary>Multiplicative zoom about a screen point (ratio &gt; 1 = zoom in). The ground point stays fixed.</summary>
        public void ZoomAt(Vector2 screen, float ratio, bool immediate)
        {
            if (ratio <= 0f || float.IsNaN(ratio)) return;
            _focusing = false;
            _targetSize = Mathf.Clamp(_targetSize / ratio, _minSize, EffectiveMaxSize());
            _zoomAnchor = screen;
            _hasZoomAnchor = true;
            if (immediate) { SetSizeKeepingAnchor(_targetSize, screen); Apply(); }
        }

        public void Wheel(Vector2 screen, float wheel) => ZoomAt(screen, 1f + Mathf.Sign(wheel) * _wheelStep, false);

        Vector2 _zoomAnchor;
        bool _hasZoomAnchor;

        void SetSizeKeepingAnchor(float newSize, Vector2 screen)
        {
            Vector3 before = ScreenToGround(screen);
            _size = newSize;
            Apply();
            Vector3 after = ScreenToGround(screen);
            _focus += new Vector3(before.x - after.x, 0f, before.z - after.z);
        }

        /// <summary>Smoothly frames a world bounds (selected building). <paramref name="viewportY"/> shifts it up (bottom sheet).</summary>
        public void FocusOn(Bounds b, float viewportY = 0.6f, bool zoomIn = true)
        {
            _velocity = Vector3.zero;
            if (zoomIn) _targetSize = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 2.2f, _minSize, Mathf.Min(EffectiveMaxSize(), Mathf.Max(_minSize, _size)));
            // ground point that must appear at (0.5, viewportY): offset the focus along the camera's flat forward
            Vector3 fwd = Vector3.ProjectOnPlane(FixedRotation * Vector3.forward, Vector3.up).normalized;
            float halfH = VisibleHalfExtents(_targetSize).y;
            _focusTarget = ClampFocus(new Vector3(b.center.x, 0f, b.center.z) - fwd * ((viewportY - 0.5f) * 2f * halfH), _targetSize);
            _focusing = true;
        }

        public void SetView(Vector3 focus, float size)
        {
            _size = _targetSize = Mathf.Clamp(size, _minSize, EffectiveMaxSize());
            _focus = ClampFocus(new Vector3(focus.x, 0f, focus.z), _size);
            _velocity = Vector3.zero; _focusing = false;
            Apply();
        }

        public void StopMotion() { _velocity = Vector3.zero; _focusing = false; }

        // ------------------------------------------------------------------ update

        void LateUpdate() => Step(Time.unscaledDeltaTime);

        /// <summary>One simulation step (also used by deterministic tests).</summary>
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            // zoom
            _targetSize = Mathf.Clamp(_targetSize, _minSize, EffectiveMaxSize());
            if (Mathf.Abs(_size - _targetSize) > 1e-4f)
            {
                float s = Mathf.Lerp(_size, _targetSize, 1f - Mathf.Exp(-_zoomSmoothing * dt));
                if (Mathf.Abs(s - _targetSize) < 1e-3f) s = _targetSize;
                if (_hasZoomAnchor && !_focusing) SetSizeKeepingAnchor(s, _zoomAnchor);
                else _size = s;
            }
            else _hasZoomAnchor = false;

            if (_focusing)
            {
                _focus = Vector3.SmoothDamp(_focus, _focusTarget, ref _focusVel, _focusSmoothTime, Mathf.Infinity, dt);
                if ((_focus - _focusTarget).sqrMagnitude < 1e-4f) _focusing = false;
            }
            else if (!_dragging && _velocity.sqrMagnitude > 1e-4f)
            {
                _focus += _velocity * dt;
                _velocity *= Mathf.Exp(-_inertiaDamping * dt);
                if (_velocity.sqrMagnitude < 0.0025f) _velocity = Vector3.zero;
            }

            // soft edge: spring back inside the bounds when not actively dragging
            Vector3 clamped = ClampFocus(_focus, _size);
            if (_dragging) _focus = Vector3.Lerp(_focus, clamped, 0.5f); // resist beyond the edge while dragging
            else _focus = Vector3.Lerp(_focus, clamped, 1f - Mathf.Exp(-_edgeSpring * dt));
            if ((_focus - clamped).sqrMagnitude > 0.0001f && !_dragging) _velocity *= 0.5f;
            Apply();
        }
    }
}
