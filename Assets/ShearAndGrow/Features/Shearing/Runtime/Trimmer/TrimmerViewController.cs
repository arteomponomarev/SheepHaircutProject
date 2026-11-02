using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Always-visible trimmer (spec §7.1). The cutter edge is placed EXACTLY at the contact point (no lag);
    /// only the handle gets smoothing/inertia. Blades oscillate while the motor runs, faster on fresh wool.
    /// PLACEHOLDER ART: built procedurally (denim/graphite body, brass band, metal comb with teeth).
    /// </summary>
    public sealed class TrimmerViewController : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _accentMaterial;
        [SerializeField] private Material _metalMaterial;
        [SerializeField] private Material _gripMaterial;

        [Header("Pose")]
        [Tooltip("Rest pose in viewport coordinates (x, y) and distance from the camera.")]
        [SerializeField] private Vector3 _restViewport = new Vector3(0.8f, 0.13f, 1.7f);
        [SerializeField] private float _rotationSharpness = 20f;
        [Tooltip("Rest pose is kept above the bottom buttons.")]
        [SerializeField] private float _restMinViewportY = 0.24f;
        [SerializeField] private float _restSharpness = 7f;
        [SerializeField] private float _handleLag = 10f;
        [SerializeField] private float _hoverOffset = 0.012f;
        [SerializeField] private float _scale = 1f;

        [Header("Motor animation")]
        [SerializeField] private float _bladeFrequency = 46f;
        [SerializeField] private float _bladeAmplitude = 0.004f;

        Camera _camera;
        Transform _handlePivot;
        Transform _blade;
        Vector3 _bladeRest;
        bool _contact;
        bool _cutting;
        bool _motorOn;
        Vector3 _targetPos;
        Quaternion _targetRot;
        Vector3 _lastTangent = Vector3.forward;
        Quaternion _handleLagRot = Quaternion.identity;
        Quaternion _prevRot;
        float _motorLevel;

        public Transform CutterTip => transform;
        public bool InContact => _contact;

        public void Initialise(Camera cam)
        {
            _camera = cam;
            BuildModel();
            SnapToRest();
        }

        void BuildModel()
        {
            transform.localScale = Vector3.one * _scale;
            // Comb head – origin is the cutting edge centre. Local: +Z = stroke direction, +Y = surface normal.
            var head = Part("HeadBlock", transform, ProceduralMeshFactory.SuperEllipsoid("TrimHead", new Vector3(0.088f, 0.03f, 0.05f), 3.5f, 24, 14), _bodyMaterial, new Vector3(0f, 0.04f, -0.03f), Quaternion.identity);
            Part("Comb", transform, ProceduralMeshFactory.Box("Comb", new Vector3(0.17f, 0.008f, 0.05f)), _metalMaterial, new Vector3(0f, 0.012f, 0.008f), Quaternion.identity);
            var tooth = ProceduralMeshFactory.Box("Tooth", new Vector3(0.0065f, 0.007f, 0.035f));
            for (int i = 0; i < 15; i++)
            {
                float x = Mathf.Lerp(-0.08f, 0.08f, i / 14f);
                Part("Tooth_" + i, transform, tooth, _metalMaterial, new Vector3(x, 0.006f, 0.045f), Quaternion.Euler(8f, 0f, 0f));
            }
            _blade = Part("Blade", transform, ProceduralMeshFactory.Box("Blade", new Vector3(0.15f, 0.006f, 0.035f)), _metalMaterial, new Vector3(0f, 0.02f, 0.012f), Quaternion.identity).transform;
            _bladeRest = _blade.localPosition;

            // Handle trails behind the stroke and rises away from the surface so it never hides the fresh line ahead.
            _handlePivot = new GameObject("HandlePivot").transform;
            _handlePivot.SetParent(transform, false);
            _handlePivot.localPosition = new Vector3(0f, 0.05f, -0.06f);
            var bodyMesh = ProceduralMeshFactory.SuperEllipsoid("TrimBody", new Vector3(0.062f, 0.2f, 0.052f), 2.6f, 28, 18);
            Part("Body", _handlePivot, bodyMesh, _bodyMaterial, new Vector3(0f, 0.12f, -0.14f), Quaternion.Euler(-55f, 0f, 0f));
            var band = ProceduralMeshFactory.Cylinder("Band", 0.066f, 0.066f, 0.03f, 24);
            Part("BrassBand", _handlePivot, band, _accentMaterial, new Vector3(0f, 0.07f, -0.07f), Quaternion.Euler(35f, 0f, 0f));
            var grip = ProceduralMeshFactory.SuperEllipsoid("Grip", new Vector3(0.04f, 0.09f, 0.03f), 2.2f, 20, 12);
            Part("Grip", _handlePivot, grip, _gripMaterial, new Vector3(0f, 0.19f, -0.16f), Quaternion.Euler(-55f, 0f, 0f)).transform.localPosition += new Vector3(0f, 0.025f, 0.02f);
            var btn = ProceduralMeshFactory.Ellipsoid("Button", new Vector3(0.016f, 0.008f, 0.016f));
            Part("Button", _handlePivot, btn, _accentMaterial, new Vector3(0f, 0.17f, -0.115f), Quaternion.Euler(35f, 0f, 0f));
            var tail = ProceduralMeshFactory.Cylinder("CableBoot", 0.022f, 0.016f, 0.06f, 14);
            Part("CableBoot", _handlePivot, tail, _gripMaterial, new Vector3(0f, 0.28f, -0.27f), Quaternion.Euler(-55f, 0f, 0f));
            head.transform.SetAsFirstSibling();
        }

        GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        void SnapToRest()
        {
            ComputeRest(out _targetPos, out _targetRot);
            transform.SetPositionAndRotation(_targetPos, _targetRot);
            _prevRot = _targetRot;
        }

        void ComputeRest(out Vector3 pos, out Quaternion rot)
        {
            if (_camera == null) { pos = transform.position; rot = transform.rotation; return; }
            Vector3 vp = _restViewport;
            vp.y = Mathf.Max(vp.y, _restMinViewportY);
            pos = _camera.ViewportToWorldPoint(vp);
            // lying slightly lifted, comb pointing up-left into the scene
            Vector3 fwd = (_camera.transform.forward * 0.6f + _camera.transform.up * 0.5f - _camera.transform.right * 0.45f).normalized;
            rot = Quaternion.LookRotation(fwd, -_camera.transform.forward + _camera.transform.up * 0.3f);
        }

        /// <summary>
        /// Called by the input/stroke pipeline every frame with the exact contact. <paramref name="cutting"/> is true only
        /// when new wool was actually removed this frame.
        /// </summary>
        public void SetContact(bool contact, Vector3 worldPoint, Vector3 normal, Vector3 tangent, bool cutting)
        {
            bool began = contact && !_contact;
            _contact = contact;
            _cutting = cutting;
            if (!contact) return;
            if (tangent.sqrMagnitude > 1e-6f) _lastTangent = Vector3.Lerp(_lastTangent, tangent.normalized, 0.5f);
            Vector3 fwd = Vector3.ProjectOnPlane(_lastTangent, normal);
            if (fwd.sqrMagnitude < 1e-6f && _camera != null) fwd = Vector3.ProjectOnPlane(_camera.transform.up, normal);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
            _targetRot = Quaternion.LookRotation(fwd.normalized, normal);
            _targetPos = worldPoint + normal * _hoverOffset;
            // The cutting edge is exact; no positional interpolation.
            transform.position = _targetPos;
            if (began) transform.rotation = _targetRot;
        }

        public void SetMotor(bool on) => _motorOn = on;

        void LateUpdate()
        {
            if (_handlePivot == null) return;
            float dt = Time.deltaTime;
            if (!_contact)
            {
                ComputeRest(out _targetPos, out _targetRot);
                float k = 1f - Mathf.Exp(-_restSharpness * dt);
                transform.position = Vector3.Lerp(transform.position, _targetPos, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, k);
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, 1f - Mathf.Exp(-_rotationSharpness * dt));
            }

            // Handle inertia: lags behind angular motion slightly (visual only).
            Quaternion delta = transform.rotation * Quaternion.Inverse(_prevRot);
            _prevRot = transform.rotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && Mathf.Abs(angle) > 0.01f)
                _handleLagRot = Quaternion.AngleAxis(-Mathf.Clamp(angle, -8f, 8f) * 0.8f, transform.InverseTransformDirection(axis)) * _handleLagRot;
            _handleLagRot = Quaternion.Slerp(_handleLagRot, Quaternion.identity, 1f - Mathf.Exp(-_handleLag * dt));
            _handlePivot.localRotation = _handleLagRot;

            // Motor: blade oscillation; stronger & faster while effectively cutting fresh wool.
            float target = _motorOn ? (_cutting ? 1f : 0.55f) : 0f;
            _motorLevel = Mathf.MoveTowards(_motorLevel, target, dt * 10f);
            float freq = _bladeFrequency * (0.8f + 0.4f * _motorLevel);
            float off = Mathf.Sin(Time.time * freq * Mathf.PI * 2f) * _bladeAmplitude * _motorLevel;
            _blade.localPosition = _bladeRest + new Vector3(off, 0f, 0f);
            _handlePivot.localPosition = new Vector3(0f, 0.05f, -0.06f) + new Vector3(off * 0.08f, Mathf.Abs(off) * 0.05f, 0f);
        }
    }
}
