using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Stylised builder who visualises a running construction. PURELY VISUAL: construction state comes from saved
    /// time in <see cref="BuildingService"/>, never from where this character is. Straight-line walking with
    /// perimeter routing around the site, code-driven animation (walk, hammer, cheer), no NavMesh / physics /
    /// Animator. If the way is blocked by another building it simply fades out and reappears at the destination.
    /// </summary>
    public sealed class BuilderNpcController : MonoBehaviour
    {
        public enum State { Hidden, WalkIn, Working, Reposition, Celebrate, WalkOut }

        [Header("Rig parts (built by the farm scene builder)")]
        [SerializeField] private Transform _model;
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _armLeft, _armRight, _legLeft, _legRight;
        [Header("Feel")]
        [SerializeField] private float _walkSpeed = 1.6f;
        [SerializeField] private Vector2 _repositionSeconds = new Vector2(3f, 6f);
        [SerializeField] private float _swingSeconds = 0.55f;
        [SerializeField] private float _sideOffset = 0.55f;

        readonly List<Vector3> _path = new List<Vector3>(8);
        readonly Vector3[] _corners = new Vector3[4];
        int _pathIndex;
        State _state;
        Vector3 _siteCenter, _gate;
        Vector2 _siteHalf;
        int _workSide;
        float _stateTime, _nextReposition, _fade = 1f, _phase;
        bool _fadingOut, _teleportPending;
        Vector3 _teleportTarget;
        System.Random _rng = new System.Random(7);
        Func<Vector3, bool> _isBlocked;

        /// <summary>Hammer hit (world position) — the site plays a dust puff / knock.</summary>
        public event Action<Vector3> Impact;

        public State CurrentState => _state;
        public bool IsVisible => _state != State.Hidden;

        public void Configure(Transform model, Transform body, Transform armL, Transform armR, Transform legL, Transform legR)
        {
            _model = model; _body = body; _armLeft = armL; _armRight = armR; _legLeft = legL; _legRight = legR;
        }

        public void SetTuning(float walkSpeed, Vector2 repositionSeconds, float swingSeconds)
        {
            _walkSpeed = walkSpeed; _repositionSeconds = repositionSeconds; _swingSeconds = swingSeconds;
        }

/// <summary>Starts (or continues) working on a site. <paramref name="arriveImmediately"/>: already on site (after a reload).</summary>
        public void AssignSite(Vector3 center, Vector2 halfSizeWorld, Vector3 gate, bool arriveImmediately, Func<Vector3, bool> isBlocked)
        {
            _siteCenter = center; _siteHalf = halfSizeWorld; _gate = gate; _isBlocked = isBlocked;
            ComputeCorners();
            gameObject.SetActive(true);
            _workSide = NearestSide(arriveImmediately ? center + Vector3.back * 10f : gate);
            if (arriveImmediately || _state == State.Working || _state == State.Reposition)
            {
                transform.position = WorkPoint(_workSide);
                FaceSite();
                EnterWorking();
                return;
            }
            transform.position = gate;
            _fade = 0f; _fadingOut = false;
            ApplyFade();
            PlanPath(WorkPoint(_workSide));
            SetState(State.WalkIn);
        }

        /// <summary>Construction finished: cheer, then walk back to the gate and disappear.</summary>
        public void Finish()
        {
            if (_state == State.Hidden) return;
            SetState(State.Celebrate);
        }

        public void HideNow()
        {
            SetState(State.Hidden);
            gameObject.SetActive(false);
        }

        void SetState(State s) { _state = s; _stateTime = 0f; }

        void EnterWorking()
        {
            SetState(State.Working);
            _nextReposition = Range(_repositionSeconds);
        }

        float Range(Vector2 r) => r.x + (float)_rng.NextDouble() * (r.y - r.x);

        // ------------------------------------------------------------------ geometry

        void ComputeCorners()
        {
            float hx = _siteHalf.x + _sideOffset, hz = _siteHalf.y + _sideOffset;
            _corners[0] = _siteCenter + new Vector3(-hx, 0f, -hz);
            _corners[1] = _siteCenter + new Vector3(hx, 0f, -hz);
            _corners[2] = _siteCenter + new Vector3(hx, 0f, hz);
            _corners[3] = _siteCenter + new Vector3(-hx, 0f, hz);
        }

        /// <summary>Sides: 0 south (−z, toward the camera), 1 east, 2 north, 3 west.</summary>
        Vector3 WorkPoint(int side)
        {
            float hx = _siteHalf.x + _sideOffset, hz = _siteHalf.y + _sideOffset;
            float jitter = ((float)_rng.NextDouble() - 0.5f) * 0.8f;
            switch (side)
            {
                case 0: return _siteCenter + new Vector3(Mathf.Clamp(jitter * hx, -hx + 0.3f, hx - 0.3f), 0f, -hz);
                case 1: return _siteCenter + new Vector3(hx, 0f, Mathf.Clamp(jitter * hz, -hz + 0.3f, hz - 0.3f));
                case 2: return _siteCenter + new Vector3(Mathf.Clamp(jitter * hx, -hx + 0.3f, hx - 0.3f), 0f, hz);
                default: return _siteCenter + new Vector3(-hx, 0f, Mathf.Clamp(jitter * hz, -hz + 0.3f, hz - 0.3f));
            }
        }

        int NearestSide(Vector3 from)
        {
            Vector3 d = from - _siteCenter;
            if (Mathf.Abs(d.x) / Mathf.Max(0.1f, _siteHalf.x) > Mathf.Abs(d.z) / Mathf.Max(0.1f, _siteHalf.y)) return d.x > 0f ? 1 : 3;
            return d.z > 0f ? 2 : 0;
        }

        bool SegmentHitsSite(Vector3 a, Vector3 b)
        {
            float hx = _siteHalf.x + _sideOffset * 0.6f, hz = _siteHalf.y + _sideOffset * 0.6f;
            for (int i = 1; i < 12; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 12f) - _siteCenter;
                if (Mathf.Abs(p.x) < hx && Mathf.Abs(p.z) < hz) return true;
            }
            return false;
        }

        /// <summary>Straight line, or around the site's corners if the line would cross it.</summary>
        void PlanPath(Vector3 target)
        {
            _path.Clear(); _pathIndex = 0;
            Vector3 from = transform.position;
            if (SegmentHitsSite(from, target))
            {
                int ci = NearestCorner(from), cj = NearestCorner(target);
                _path.Add(_corners[ci]);
                if (ci != cj)
                {
                    int fwd = (cj - ci + 4) % 4, back = (ci - cj + 4) % 4;
                    int step = fwd <= back ? 1 : -1;
                    for (int k = ci; k != cj;) { k = (k + step + 4) % 4; _path.Add(_corners[k]); }
                }
            }
            _path.Add(target);
            // blocked by another building → fade out here and reappear at the target
            if (_isBlocked != null)
                for (int i = 0; i < _path.Count; i++)
                {
                    Vector3 a = i == 0 ? from : _path[i - 1], b = _path[i];
                    for (int s = 1; s <= 6; s++)
                        if (_isBlocked(Vector3.Lerp(a, b, s / 6f))) { _teleportPending = true; _teleportTarget = target; _fadingOut = true; return; }
                }
        }

        int NearestCorner(Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < 4; i++) { float d = (p - _corners[i]).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        void FaceSite()
        {
            Vector3 d = _siteCenter - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (_state == State.Hidden) return;
            float dt = Time.deltaTime;
            _stateTime += dt;
            _phase += dt;
            UpdateFade(dt);
            if (_fadingOut || _teleportPending) { Pose(0f, 0f, 0f); return; }

            switch (_state)
            {
                case State.WalkIn:
                case State.Reposition:
                    if (Walk(dt)) { FaceSite(); EnterWorking(); }
                    break;
                case State.Working:
                    Hammer();
                    if (_stateTime >= _nextReposition)
                    {
                        _workSide = (_workSide + (_rng.Next(2) == 0 ? 1 : 3)) % 4;
                        PlanPath(WorkPoint(_workSide));
                        SetState(State.Reposition);
                    }
                    break;
                case State.Celebrate:
                    Cheer();
                    if (_stateTime > 1.3f) { PlanPath(_gate); SetState(State.WalkOut); }
                    break;
                case State.WalkOut:
                    if (Walk(dt)) { _fadingOut = true; }
                    break;
            }
        }

        /// <summary>Moves along the path; returns true when arrived.</summary>
        bool Walk(float dt)
        {
            if (_pathIndex >= _path.Count) { Pose(0f, 0f, 0f); return true; }
            Vector3 target = _path[_pathIndex];
            Vector3 to = target - transform.position; to.y = 0f;
            float step = _walkSpeed * dt;
            if (to.magnitude <= step) { transform.position = new Vector3(target.x, 0f, target.z); _pathIndex++; }
            else transform.position += to.normalized * step;
            if (to.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-10f * dt));
            float s = Mathf.Sin(_phase * 11f);
            Pose(s * 28f, -s * 22f, Mathf.Abs(s) * 0.05f);
            return _pathIndex >= _path.Count;
        }

        float _lastSwingPhase;

        void Hammer()
        {
            float p = (_stateTime % _swingSeconds) / _swingSeconds; // 0..1
            // raise slowly (0..0.7), strike fast (0.7..0.82), recover
            // arm rotates about X: −150° = raised overhead, −50° = hammer forward at the work surface
            float ang = p < 0.7f ? Mathf.Lerp(-50f, -150f, Mathf.SmoothStep(0f, 1f, p / 0.7f))
                      : p < 0.82f ? Mathf.Lerp(-150f, -42f, (p - 0.7f) / 0.12f)
                      : Mathf.Lerp(-42f, -50f, (p - 0.82f) / 0.18f);
            if (_lastSwingPhase < 0.82f && p >= 0.82f) Impact?.Invoke(transform.position + transform.forward * 0.55f);
            _lastSwingPhase = p;
            if (_armRight != null) _armRight.localRotation = Quaternion.Euler(ang, 0f, 0f);
            if (_armLeft != null) _armLeft.localRotation = Quaternion.Euler(-25f, 0f, 8f);
            if (_legLeft != null) _legLeft.localRotation = Quaternion.identity;
            if (_legRight != null) _legRight.localRotation = Quaternion.identity;
            float squash = p >= 0.82f && p < 0.92f ? 0.06f : 0f;
            if (_body != null) { _body.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash); _body.localPosition = new Vector3(0f, Mathf.Sin(_phase * 2f) * 0.01f, 0f); }
        }

        void Cheer()
        {
            float j = Mathf.Abs(Mathf.Sin(_stateTime * Mathf.PI * 2.2f)) * 0.22f;
            if (_body != null) { _body.localPosition = new Vector3(0f, j, 0f); _body.localScale = Vector3.one; }
            if (_armLeft != null) _armLeft.localRotation = Quaternion.Euler(-170f, 0f, 15f);
            if (_armRight != null) _armRight.localRotation = Quaternion.Euler(-170f, 0f, -15f);
            Vector3 d = Camera.main != null ? Camera.main.transform.position - transform.position : Vector3.back;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-3f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), 0.2f);
        }

        void Pose(float legSwing, float armSwing, float bob)
        {
            if (_legLeft != null) _legLeft.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
            if (_legRight != null) _legRight.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
            if (_armLeft != null) _armLeft.localRotation = Quaternion.Euler(armSwing, 0f, 0f);
            if (_armRight != null) _armRight.localRotation = Quaternion.Euler(-armSwing - 20f, 0f, 0f);
            if (_body != null) { _body.localPosition = new Vector3(0f, bob, 0f); _body.localScale = Vector3.one; }
        }

        void UpdateFade(float dt)
        {
            if (_fadingOut)
            {
                _fade = Mathf.Max(0f, _fade - dt / 0.25f);
                ApplyFade();
                if (_fade <= 0f)
                {
                    _fadingOut = false;
                    if (_teleportPending)
                    {
                        _teleportPending = false;
                        transform.position = _teleportTarget;
                        _path.Clear(); _pathIndex = 0;
                        if (_state == State.WalkOut) { HideNow(); return; }
                        FaceSite();
                        EnterWorking();
                    }
                    else if (_state == State.WalkOut) { HideNow(); }
                }
            }
            else if (_fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + dt / 0.25f);
                ApplyFade();
            }
        }

        void ApplyFade()
        {
            if (_model != null) _model.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, _fade));
        }
    }
}