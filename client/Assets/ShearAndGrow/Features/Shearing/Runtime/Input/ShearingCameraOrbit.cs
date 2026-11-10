using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Orbit camera for the shearing mini-game.
    ///  • Portrait-aware framing: keeps a minimum HORIZONTAL field of view, so in 9:19.5 the sheep is framed from ~3 m
    ///    (the old fit used the ~16° horizontal FOV and placed the camera 7.6–8.9 m away — outside the barn walls).
    ///  • Collision: analytic sphere-vs-AABB test against the barn walls/props, floor height and the playable area.
    ///    Pulls in instantly (never clips), eases back out smoothly. No physics, no extra layers — negligible cost.
    ///  • Full 360° yaw, pitch from near-floor to near-top-down; dragging below the lowest pitch rolls the sheep
    ///    (<see cref="SheepPoseController"/>) so the belly can be sheared.
    ///  • Pinch / mouse-wheel zoom within limits; sensitivity is normalised to screen size (same feel on every DPI).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(10)]
    public sealed class ShearingCameraOrbit : MonoBehaviour
    {
        [Header("Default view")]
        [SerializeField] private float _defaultYaw = -38f;
        [SerializeField] private float _defaultPitch = 20f;
        [Tooltip("Offset from the sheep body centre to the orbit pivot (world). Slight +X keeps the head in frame.")]
        [SerializeField] private Vector3 _pivotOffset = new Vector3(0.12f, -0.08f, 0f);
        [Tooltip("Fallback pivot when no pose controller is assigned.")]
        [SerializeField] private Vector3 _fallbackPivot = new Vector3(0.05f, 0.82f, 0f);

        [Header("Rotation")]
        [SerializeField] private Vector2 _pitchLimits = new Vector2(2f, 80f);
        [Tooltip("Degrees of yaw for a swipe across the full screen width.")]
        [SerializeField] private float _yawPerScreenWidth = 300f;
        [Tooltip("Degrees of pitch for a swipe across the full screen height.")]
        [SerializeField] private float _pitchPerScreenHeight = 170f;
        [Tooltip("Degrees of extra downward drag (past the lowest pitch) that roll the sheep fully onto its side.")]
        [SerializeField] private float _rollDragDegrees = 70f;
        [Tooltip("Smoothing of rotation (higher = more direct, less inertia after release).")]
        [SerializeField] private float _rotationSharpness = 18f;

        [Header("Framing / zoom")]
        [SerializeField] private float _baseVerticalFov = 40f;
        [Tooltip("Never let the horizontal FOV drop below this (portrait screens).")]
        [SerializeField] private float _minHorizontalFov = 44f;
        [SerializeField] private float _maxVerticalFov = 72f;
        [Tooltip("Radius around the pivot that must fit on screen at zoom 1.")]
        [SerializeField] private float _fitRadius = 1.05f;
        [SerializeField] private Vector2 _zoomLimits = new Vector2(0.62f, 1.3f);
        [SerializeField] private Vector2 _distanceLimits = new Vector2(1.45f, 5.2f);
        [SerializeField] private float _zoomSharpness = 12f;
        [SerializeField] private float _introDuration = 1.2f;
        [SerializeField] private float _introExtraDistance = 0.12f;

        [Header("Collision")]
        [Tooltip("Environment root whose renderers are camera obstacles (walls, props).")]
        [SerializeField] private Transform _environment;
        [Tooltip("Renderers with these names are walkable ground, not obstacles; their bounds define the playable area.")]
        [SerializeField] private string[] _groundNames = { "WoodFloor", "Rug" };
        [SerializeField] private float _collisionRadius = 0.3f;
        [SerializeField] private float _minCameraHeight = 0.2f;
        [SerializeField] private float _maxCameraHeight = 4.0f;
        [Tooltip("Time (s) to ease back out after a prop no longer blocks the view.")]
        [SerializeField] private float _easeOutTime = 0.45f;
        [Tooltip("Time (s) to pull in when a prop blocks the view (walls are always a hard limit).")]
        [SerializeField] private float _pullInTime = 0.14f;
        float _distanceVelocity;

        Camera _cam;
        SheepPoseController _pose;
        readonly List<Bounds> _obstacles = new List<Bounds>(8);  // walls: hard limit, enforced every frame
        readonly List<Bounds> _props = new List<Bounds>(16);     // small props: smooth pull-in (no snapping)
        [Tooltip("Obstacles at least this long (m) are walls (hard limit); shorter ones are props (smooth pull-in).")]
        [SerializeField] private float _wallMinLength = 4f;
        Bounds _playArea;
        bool _hasPlayArea;
        float _yaw, _pitch, _targetYaw, _targetPitch;
        float _zoom = 1f, _targetZoom = 1f;
        float _distance = -1f;
        float _introT;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float TargetPitch => _targetPitch;
        public float Zoom => _zoom;
        public float CurrentDistance => _distance;
        public float DesiredDistance { get; private set; }
        public bool CollisionActive { get; private set; }
        public Vector3 Pivot { get; private set; }
        public Vector2 PitchLimits => _pitchLimits;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            ResetView(true);
            _introT = 0f;
        }

        public void Initialise(SheepPoseController pose)
        {
            _pose = pose;
            CollectObstacles();
        }

        void CollectObstacles()
        {
            _obstacles.Clear();
            _props.Clear();
            _hasPlayArea = false;
            if (_environment == null) return;
            foreach (var r in _environment.GetComponentsInChildren<Renderer>())
            {
                bool ground = false;
                foreach (var n in _groundNames) if (r.name == n) { ground = true; break; }
                if (ground)
                {
                    if (!_hasPlayArea) { _playArea = r.bounds; _hasPlayArea = true; }
                    else _playArea.Encapsulate(r.bounds);
                }
                else if (Mathf.Max(r.bounds.size.x, r.bounds.size.z) >= _wallMinLength) _obstacles.Add(r.bounds); // walls: hard
                else _props.Add(r.bounds); // props: soft, smooth pull-in
            }
        }

        // ------------------------------------------------------------------ input

        public void ResetView(bool instant = false)
        {
            _targetYaw = _defaultYaw;
            _targetPitch = _defaultPitch;
            _targetZoom = 1f;
            if (_pose != null) _pose.ResetPose(instant);
            if (instant) { _yaw = _targetYaw; _pitch = _targetPitch; _zoom = _targetZoom; }
        }

        /// <summary>Orbit by a screen-space drag delta (pixels). Drag up lowers the camera; past the lowest pitch it rolls the sheep.</summary>
        public void AddOrbit(Vector2 screenDelta)
        {
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            _targetYaw += screenDelta.x / w * _yawPerScreenWidth;
            float pitchDelta = -screenDelta.y / h * _pitchPerScreenHeight;
            if (_pose == null) { _targetPitch = Mathf.Clamp(_targetPitch + pitchDelta, _pitchLimits.x, _pitchLimits.y); return; }

            if (pitchDelta < 0f)
            {
                // lowering: use pitch first, then roll the sheep (belly toward the camera)
                float room = _targetPitch - _pitchLimits.x;
                float usePitch = Mathf.Max(pitchDelta, -room);
                _targetPitch += usePitch;
                float excess = pitchDelta - usePitch; // negative
                if (excess < 0f)
                {
                    _pose.ChooseSideFacing(transform.position);
                    _pose.AddRoll(-excess / Mathf.Max(1f, _rollDragDegrees));
                }
            }
            else if (pitchDelta > 0f)
            {
                // raising: un-roll first, then raise the camera
                float rollDeg = _pose.Target01 * _rollDragDegrees;
                float useRoll = Mathf.Min(pitchDelta, rollDeg);
                if (useRoll > 0f) _pose.AddRoll(-useRoll / Mathf.Max(1f, _rollDragDegrees));
                _targetPitch = Mathf.Min(_pitchLimits.y, _targetPitch + pitchDelta - useRoll);
            }
        }

        /// <summary>Multiplicative zoom: ratio &gt; 1 moves closer.</summary>
        public void AddZoom(float ratio)
        {
            if (ratio <= 0f || float.IsNaN(ratio)) return;
            _targetZoom = Mathf.Clamp(_targetZoom / ratio, _zoomLimits.x, _zoomLimits.y);
        }

        /// <summary>Toggle the belly pose: roll the sheep and move to the matching side view.</summary>
        public void ToggleBellyView()
        {
            if (_pose == null) return;
            if (_pose.IsBellyMode) { _pose.SetTarget(0f); _targetPitch = _defaultPitch; return; }
            _pose.ChooseSideFacing(transform.position);
            _pose.SetTarget(1f);
            // Side view: camera on the flank the belly turns toward.
            float sideYaw = _pose.BaseYaw + (_pose.Side > 0 ? 180f : 0f);
            _targetYaw = _yaw + Mathf.DeltaAngle(_yaw, sideYaw);
            _targetPitch = Mathf.Max(_pitchLimits.x, 8f);
        }

        // ------------------------------------------------------------------ camera solve

        void UpdateFov(out float limitingHalfFov)
        {
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            float halfH = _minHorizontalFov * 0.5f * Mathf.Deg2Rad;
            float vNeeded = 2f * Mathf.Atan(Mathf.Tan(halfH) / aspect) * Mathf.Rad2Deg;
            float vFov = Mathf.Clamp(Mathf.Max(_baseVerticalFov, vNeeded), 20f, _maxVerticalFov);
            _cam.fieldOfView = vFov;
            float halfV = vFov * 0.5f * Mathf.Deg2Rad;
            float actualHalfH = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            limitingHalfFov = Mathf.Min(halfV, actualHalfH);
        }

        /// <summary>Largest distance along <paramref name="dir"/> from <paramref name="pivot"/> that keeps a sphere of the collision radius free.</summary>
        float FreeDistance(Vector3 pivot, Vector3 dir, float wanted)
        {
            float free = wanted;
            float r = _collisionRadius;
            // floor & ceiling
            if (dir.y < -1e-4f) free = Mathf.Min(free, (pivot.y - _minCameraHeight) / -dir.y);
            if (dir.y > 1e-4f) free = Mathf.Min(free, (_maxCameraHeight - pivot.y) / dir.y);
            // stay inside the playable area (floor extents), shrunk by the radius
            if (_hasPlayArea)
            {
                Vector3 min = _playArea.min + new Vector3(r, 0f, r), max = _playArea.max - new Vector3(r, 0f, r);
                if (dir.x > 1e-4f) free = Mathf.Min(free, (max.x - pivot.x) / dir.x);
                if (dir.x < -1e-4f) free = Mathf.Min(free, (min.x - pivot.x) / dir.x);
                if (dir.z > 1e-4f) free = Mathf.Min(free, (max.z - pivot.z) / dir.z);
                if (dir.z < -1e-4f) free = Mathf.Min(free, (min.z - pivot.z) / dir.z);
            }
            // walls: ray vs AABB expanded by the radius (Minkowski sum ≈ sphere cast). Walls are large planes, so this
            // limit changes continuously with the angle and can be enforced every frame without snapping.
            free = Mathf.Min(free, RayFree(pivot, dir, free, _obstacles));
            return Mathf.Max(0.05f, free);
        }

        /// <summary>Closest hit of the ray against the given boxes (expanded by the collision radius), or <paramref name="free"/>.</summary>
        float RayFree(Vector3 pivot, Vector3 dir, float free, List<Bounds> boxes) => RayFree(pivot, dir, free, boxes, _collisionRadius);

        float RayFree(Vector3 pivot, Vector3 dir, float free, List<Bounds> boxes, float radius)
        {
            var ray = new Ray(pivot, dir);
            for (int i = 0; i < boxes.Count; i++)
            {
                Bounds b = boxes[i];
                b.Expand(radius * 2f);
                if (b.Contains(pivot)) continue; // never trap the camera when the pivot itself is inside a box
                if (b.IntersectRay(ray, out float hit) && hit < free) free = hit;
            }
            return free;
        }

        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>QA hook: runs one solver step with an explicit delta time (deterministic tests, works in edit mode).</summary>
        public void QA_Step(float dt)
        {
            if (_cam == null) { _cam = GetComponent<Camera>(); _introT = 1f; }
            Step(dt);
        }

        /// <summary>QA hook: environment/pose setup without Awake (edit-mode tests).</summary>
        public void QA_Setup(SheepPoseController pose, Transform environment)
        {
            _environment = environment;
            _cam = GetComponent<Camera>();
            ResetView(true);
            _introT = 1f;
            _distance = -1f;
            Initialise(pose);
        }

        void Step(float dt)
        {
            if (dt <= 0f) return;
            float k = 1f - Mathf.Exp(-_rotationSharpness * dt);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, k);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, k);
            _zoom = Mathf.Lerp(_zoom, _targetZoom, 1f - Mathf.Exp(-_zoomSharpness * dt));

            UpdateFov(out float halfFov);
            _introT = Mathf.Min(1f, _introT + dt / Mathf.Max(0.01f, _introDuration));
            float intro = 1f + (1f - Mathf.SmoothStep(0f, 1f, _introT)) * _introExtraDistance;
            float desired = _fitRadius / Mathf.Sin(halfFov) * _zoom * intro;
            desired = Mathf.Clamp(desired, _distanceLimits.x, _distanceLimits.y);
            DesiredDistance = desired;

            Vector3 pivot = (_pose != null ? _pose.BodyCenterWorld : _fallbackPivot) + _pivotOffset;
            Pivot = pivot;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 back = rot * Vector3.back;
            // Hard limit (walls, floor, ceiling, playable area): continuous in the angle -> enforced every frame,
            // never clips, never snaps.
            float hard = FreeDistance(pivot, back, desired);
            // Soft limit (small props): a ray sliding off a prop corner changes this abruptly, so the camera pulls
            // in smoothly instead of snapping. Props are low and near the walls; the collision radius gives a buffer.
            float allowed = Mathf.Min(hard, RayFree(pivot, back, hard, _props));
            // Last resort: the smoothed pull-in may never put the lens inside a prop's real (unexpanded) box.
            // The 0.3 m buffer above normally pulls in early enough that this never engages.
            hard = Mathf.Min(hard, RayFree(pivot, back, hard, _props, _cam.nearClipPlane + 0.02f));
            CollisionActive = allowed < desired - 1e-3f;

            // Critically damped: velocity starts at zero, so pulling in/out eases in instead of jolting on frame 1.
            if (_distance < 0f) { _distance = allowed; _distanceVelocity = 0f; }
            else
            {
                float smoothTime = allowed < _distance ? _pullInTime : _easeOutTime;
                _distance = Mathf.SmoothDamp(_distance, allowed, ref _distanceVelocity, smoothTime, Mathf.Infinity, dt);
            }
            if (_distance > hard) { _distance = hard; _distanceVelocity = Mathf.Min(0f, _distanceVelocity); }

            transform.SetPositionAndRotation(pivot + back * _distance, rot);
        }
    }
}
