using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// The ONE farm camera controller (nothing else writes the camera transform). Players always get the serialized
    /// angle below ("Original": orthographic 3/4 view); the angle never changes during normal play. Developer tools
    /// may switch presets at runtime through <see cref="ApplySettings"/> (yaw / pitch / projection / FOV / zoom
    /// limits, all clamped), which only changes the VIEW — grid, building positions and input mapping are derived
    /// from ground raycasts and work for any angle or projection.
    ///   • Drag: the ground point under the finger stays under the finger (1:1 at any zoom), light inertia.
    ///   • Pinch / wheel: zoom toward the fingers / cursor (that ground point stays put).
    ///   • Zoom = visible half height at the focus point (orthographic size; perspective derives its distance from
    ///     it), so switching projection keeps about the same visible area.
    ///   • Focus point is clamped so the visible area stays inside the farm bounds (soft spring at the edge).
    ///   • Portrait / landscape: framing reserves the HUD bands of the current layout; world scale never changes.
    /// Pure transform math – no physics, no extra cameras.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FarmCameraController : MonoBehaviour
    {
        // hard safety limits for any runtime change (camera never flips, never looks along / under the ground)
        public const float MinPitchLimit = 25f, MaxPitchLimit = 80f;
        public const float MinFovLimit = 15f, MaxFovLimit = 60f;
        const float MinCameraHeight = 2f;

        [Header("Original angle (shipped view – used by every player)")]
        [Tooltip("3/4 top-down pitch. Portrait phones read best with yaw 0 (farm depth runs up the screen).")]
        [SerializeField] private float _pitch = 48f;
        [SerializeField] private float _yaw = 0f;
        [SerializeField] private float _distance = 40f;
        [Tooltip("Experimental presets for the Developer Panel (never applied automatically).")]
        [SerializeField] private FarmCameraPresetLibrary _presets;
        [Tooltip("Area that must fit on screen at maximum zoom-out (all buildings + pen), incl. building height.")]
        [SerializeField] private Bounds _coreBounds = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(12f, 3f, 18f));
        [Tooltip("Share of the screen height covered by the top HUD (kept free at max zoom-out).")]
        [SerializeField, Range(0f, 0.3f)] private float _topUiFraction = 0.15f;

        [Header("Landscape framing (display only – world scale never changes)")]
        [Tooltip("Share of the screen height covered by the top HUD in landscape.")]
        [SerializeField, Range(0f, 0.35f)] private float _landscapeTopUiFraction = 0.16f;
        [Tooltip("Share of the screen width covered by the right action column in landscape.")]
        [SerializeField, Range(0f, 0.3f)] private float _landscapeRightUiFraction = 0.12f;

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

        // runtime view settings (start = Original)
        FarmCameraProjection _projection = FarmCameraProjection.Orthographic;
        float _fov = 25f;
        FarmCameraPreset _original;
        string _activePresetId = "original";
        float _origNear = 1f, _origFar = 90f;
        bool _wasAtMaxZoom = true;

        public Vector3 Focus => _focus;
        /// <summary>Where the camera is heading (focus target while auto-framing) – used to save/restore the view.</summary>
        public Vector3 SettledFocus => ClampFocus(_focusing ? _focusTarget : _focus, _targetSize);
        public float Size => _size;
        public float TargetSize => _targetSize;
        public float MinSize => _minSize;
        public float MaxSize => EffectiveMaxSize();
        /// <summary>Current view rotation (name kept for existing callers; fixed during normal play).</summary>
        public Quaternion FixedRotation => Quaternion.Euler(_pitch, _yaw, 0f);
        public Camera Camera => _cam != null ? _cam : (_cam = GetComponent<Camera>());
        public bool IsMoving => _dragging || _velocity.sqrMagnitude > 0.01f || _focusing || Mathf.Abs(_size - _targetSize) > 0.002f;
        public Bounds FarmBounds { get => _farmBounds; set => _farmBounds = value; }

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public FarmCameraProjection Projection => _projection;
        public float FieldOfView => _fov;
        public float OrthoDistance => _distance;
        public string ActivePresetId => _activePresetId;
        public FarmCameraPresetLibrary PresetLibrary => _presets;
        /// <summary>The shipped player view, captured from the serialized values before anything can change them.</summary>
        public FarmCameraPreset OriginalPreset { get { CaptureOriginal(); return _original.Clone(); } }
        /// <summary>Distance from the focus point to the lens (perspective: derived from zoom and FOV).</summary>
        public float CurrentDistance => _projection == FarmCameraProjection.Perspective ? PerspectiveDistance(_size) : _distance;
        /// <summary>View settings changed (preset / angle / projection) – UI refresh hint.</summary>
        public event System.Action SettingsChanged;

        void Awake()
        {
            CaptureOriginal();
            Initialise(_farmBounds, _coreBounds);
        }

        void OnEnable() => ShearAndGrow.Economy.ScreenLayout.Changed += OnScreenChanged;
        void OnDisable() => ShearAndGrow.Economy.ScreenLayout.Changed -= OnScreenChanged;

        void CaptureOriginal()
        {
            if (_original != null) return;
            var cam = GetComponent<Camera>();
            _origNear = cam.nearClipPlane; _origFar = cam.farClipPlane;
            _original = new FarmCameraPreset
            {
                Id = "original", DisplayName = "Original", Yaw = _yaw, Pitch = _pitch, Projection = FarmCameraProjection.Orthographic,
                FieldOfView = 25f, OrthoDistance = _distance, MinSize = _minSize, MaxSize = _maxSize,
            };
            _fov = 25f;
        }

        /// <summary>
        /// Sets the pannable farm bounds and the core area (both from <see cref="FarmLayout"/>) and shows the whole farm.
        /// </summary>
        public void Initialise(Bounds farmBounds, Bounds coreBounds)
        {
            _farmBounds = farmBounds;
            _coreBounds = coreBounds;
            _cam = GetComponent<Camera>();
            ApplyProjection();
            _velocity = Vector3.zero; _focusing = false; _dragging = false;
            ShowWholeFarm(true);
        }

        // ------------------------------------------------------------------ view settings (developer experiments)

        /// <summary>Snapshot of the current view settings (for saving a Custom preset / copying as text).</summary>
        public FarmCameraPreset CurrentSettings => new FarmCameraPreset
        {
            Id = _activePresetId, DisplayName = _activePresetId, Yaw = _yaw, Pitch = _pitch, Projection = _projection,
            FieldOfView = _fov, OrthoDistance = _distance, MinSize = _minSize, MaxSize = _maxSize,
        };

        /// <summary>
        /// Applies a preset immediately (no scene reload). Values are clamped to safe ranges; the focus point and the
        /// visible area are kept, so the farm does not jump. Grid / placement / taps keep working (ray based).
        /// </summary>
        public void ApplySettings(FarmCameraPreset p)
        {
            if (p == null) return;
            CaptureOriginal();
            _activePresetId = string.IsNullOrEmpty(p.Id) ? "custom" : p.Id;
            _yaw = Mathf.Repeat(p.Yaw + 180f, 360f) - 180f;
            _projection = p.Projection;
            _fov = Mathf.Clamp(p.FieldOfView, MinFovLimit, MaxFovLimit);
            _pitch = ClampPitch(p.Pitch);
            _distance = Mathf.Clamp(p.OrthoDistance, 10f, 200f);
            _minSize = Mathf.Clamp(p.MinSize, 1f, 30f);
            _maxSize = Mathf.Clamp(Mathf.Max(p.MaxSize, _minSize + 0.5f), 2f, 60f);
            AfterSettingsChange();
        }

        public void RestoreOriginal() => ApplySettings(OriginalPreset);

        public void SetYaw(float yaw) { _yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f; MarkCustom(); }
        public void SetPitch(float pitch) { _pitch = ClampPitch(pitch); MarkCustom(); }
        public void SetFieldOfView(float fov) { _fov = Mathf.Clamp(fov, MinFovLimit, MaxFovLimit); _pitch = ClampPitch(_pitch); MarkCustom(); }
        public void SetProjection(FarmCameraProjection p) { _projection = p; _pitch = ClampPitch(_pitch); MarkCustom(); }

        /// <summary>Zoom as 0 (fully out) … 1 (fully in). Keeps the screen-centre focus.</summary>
        public float Zoom01
        {
            get { float max = EffectiveMaxSize(); return max - _minSize < 1e-3f ? 1f : Mathf.InverseLerp(max, _minSize, _targetSize); }
        }

        public void SetZoom01(float t)
        {
            float s = Mathf.Lerp(EffectiveMaxSize(), _minSize, Mathf.Clamp01(t));
            _focusing = false; _hasZoomAnchor = false;
            _size = _targetSize = s;
            _focus = ClampFocus(_focus, _size);
            Apply();
        }

        /// <summary>Direct zoom value (orthographic size / perspective-equivalent half height), clamped to the limits.</summary>
        public void SetSize(float size) => SetZoom01(Mathf.InverseLerp(EffectiveMaxSize(), _minSize, size));

        void MarkCustom() { _activePresetId = "custom"; AfterSettingsChange(); }

        /// <summary>Perspective needs the top screen ray well below the horizon (stable ground raycasts, no sky).</summary>
        float ClampPitch(float pitch)
        {
            float min = MinPitchLimit;
            if (_projection == FarmCameraProjection.Perspective) min = Mathf.Max(min, _fov * 0.5f + 12f);
            return Mathf.Clamp(pitch, min, MaxPitchLimit);
        }

        void AfterSettingsChange()
        {
            bool wasOverview = _wasAtMaxZoom;
            ApplyProjection();
            _velocity = Vector3.zero;
            _hasZoomAnchor = false;
            if (wasOverview) { ShowWholeFarm(true); SettingsChanged?.Invoke(); return; } // overview stays an overview for the new angle
            if (_focusing) _focusTarget = ClampFocus(_focusTarget, _targetSize);
            _size = _targetSize = Mathf.Clamp(_targetSize, _minSize, EffectiveMaxSize());
            _focus = ClampFocus(_focus, _size);
            Apply();
            SettingsChanged?.Invoke();
        }

        void ApplyProjection()
        {
            var cam = Camera;
            bool persp = _projection == FarmCameraProjection.Perspective;
            cam.orthographic = !persp;
            if (persp) cam.fieldOfView = _fov;
            else { cam.nearClipPlane = _origNear; cam.farClipPlane = _origFar; }
        }

        float PerspectiveDistance(float size)
        {
            float d = size / Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            float minD = MinCameraHeight / Mathf.Max(0.2f, Mathf.Sin(_pitch * Mathf.Deg2Rad));
            return Mathf.Max(d, minD);
        }

        // ------------------------------------------------------------------ orientation

        float TopUi => ShearAndGrow.Economy.ScreenLayout.IsLandscape ? _landscapeTopUiFraction : _topUiFraction;
        float RightUi => ShearAndGrow.Economy.ScreenLayout.IsLandscape ? _landscapeRightUiFraction : 0f;

        /// <summary>
        /// Rotation / resize: the view is re-clamped for the new aspect. If the player was looking at the whole farm,
        /// the new orientation shows the whole farm again (landscape can show more). The world never changes.
        /// </summary>
        void OnScreenChanged()
        {
            if (_cam == null) return;
            _dragging = false; _velocity = Vector3.zero; _hasZoomAnchor = false;
            if (_wasAtMaxZoom) ShowWholeFarm(true);
            else { _size = _targetSize = Mathf.Clamp(_targetSize, _minSize, EffectiveMaxSize()); _focus = ClampFocus(_focus, _size); Apply(); }
        }

        // ------------------------------------------------------------------ math helpers

        /// <summary>Ground-plane point under a screen position for the CURRENT camera pose.</summary>
        public Vector3 ScreenToGround(Vector2 screen)
        {
            Ray r = Camera.ScreenPointToRay(screen);
            if (r.direction.y > -0.02f)
            {
                // perspective ray at / above the horizon (cannot happen inside the pitch limits): far ground point
                Vector3 flat = new Vector3(r.direction.x, 0f, r.direction.z);
                if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
                return new Vector3(r.origin.x, 0f, r.origin.z) + flat.normalized * 200f;
            }
            float t = -r.origin.y / r.direction.y;
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
            float usableH = 1f - TopUi, usableW = 1f - RightUi;
            return Mathf.Max(maxR / (aspect * usableW), maxUp / usableH) * 1.06f;
        }

        /// <summary>Zoom-out limit: exactly "whole farm visible" (never more than the serialized hard maximum).</summary>
        float EffectiveMaxSize() => Mathf.Clamp(FitCoreSize(), _minSize, _maxSize);

        /// <summary>Default view: whole farm, centred on the core (with the HUD band kept free).</summary>
        public void ShowWholeFarm(bool immediate)
        {
            float s = EffectiveMaxSize();
            Vector3 c = new Vector3(_coreBounds.center.x, 0f, _coreBounds.center.z);
            // shift down a little so the top HUD band doesn't cover the farthest buildings
            GroundAxes(out var right, out var fwd);
            var half = VisibleHalfExtents(s);
            c += fwd * (TopUi * half.y);
            // landscape: keep the farm left of the right-hand action column
            c += right * (RightUi * half.x);
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
            var cam = Camera;
            if (_projection == FarmCameraProjection.Perspective)
            {
                float d = PerspectiveDistance(_size);
                transform.SetPositionAndRotation(_focus - rot * Vector3.forward * d, rot);
                cam.fieldOfView = _fov;
                cam.nearClipPlane = Mathf.Clamp(d * 0.05f, 0.3f, 5f);
                cam.farClipPlane = d + 90f;
            }
            else
            {
                transform.SetPositionAndRotation(_focus - rot * Vector3.forward * _distance, rot);
                cam.orthographicSize = _size;
            }
            _wasAtMaxZoom = _targetSize >= EffectiveMaxSize() * 0.985f;
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
        public void FocusOn(Bounds b, float viewportY = 0.6f, bool zoomIn = true) => FocusOn(b, new Vector2(0.5f, viewportY), zoomIn);

        /// <summary>Smoothly frames a world bounds at a viewport position (portrait bottom sheet: (0.5, 0.66); landscape side sheet: left of it).</summary>
        public void FocusOn(Bounds b, Vector2 viewport, bool zoomIn)
        {
            _velocity = Vector3.zero;
            if (zoomIn) _targetSize = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 2.2f, _minSize, Mathf.Min(EffectiveMaxSize(), Mathf.Max(_minSize, _size)));
            // ground point that must appear at the viewport position: offset the focus along the flat camera axes
            GroundAxes(out var right, out var fwd);
            var half = VisibleHalfExtents(_targetSize);
            Vector3 target = new Vector3(b.center.x, 0f, b.center.z) - fwd * ((viewport.y - 0.5f) * 2f * half.y) - right * ((viewport.x - 0.5f) * 2f * half.x);
            _focusTarget = ClampFocus(target, _targetSize);
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
