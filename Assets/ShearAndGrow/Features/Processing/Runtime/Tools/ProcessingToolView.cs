using System.Collections.Generic;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// The visible tool. Wash: stylised garden-hose nozzle + soft water jet (LineRenderer). Dry: hand-held hair dryer
    /// + faint air cone. The tool smoothly follows the finger with an offset toward the camera / right so the worked
    /// spot stays visible; processing itself happens at the real finger hit (responsive). Procedural placeholder art.
    /// </summary>
    public sealed class ProcessingToolView : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _accentMaterial;
        [SerializeField] private Material _streamMaterial;
        [SerializeField] private Material _hoseMaterial;

        [Header("Follow")]
        [Tooltip("Distance from the aim point back toward the camera (m).")]
        [SerializeField] private float _standOff = 0.22f;
        [SerializeField] private float _sideOffset = 0.13f;
        [SerializeField] private float _lift = 0.03f;
        [SerializeField] private float _followTime = 0.07f;
        [Tooltip("Uniform scale of the tool model (keeps the work area visible on small screens).")]
        [SerializeField] private float _modelScale = 0.6f;

        ProcessingStage _stage;
        Transform _root, _tip, _spinner;
        LineRenderer _jet, _hose;
        MeshRenderer _cone;
        Vector3 _vel;
        Vector3 _aim;
        float _flow;           // 0..1 stream intensity (ramps)
        bool _on;
        Camera _cam;
        Vector3 _restPos;
        readonly List<Mesh> _meshes = new List<Mesh>();

        public Vector3 TipPosition => _tip != null ? _tip.position : transform.position;
        public Vector3 StreamDirection => _tip != null ? (_aim - _tip.position).normalized : Vector3.down;
        public bool IsOn => _on;

        public void Build(ProcessingStage stage, Camera cam, Vector3 restPosition)
        {
            _stage = stage;
            _cam = cam;
            _restPos = restPosition;
            _root = new GameObject("ToolModel").transform;
            _root.SetParent(transform, false);
            _root.localScale = Vector3.one * _modelScale;
            if (stage == ProcessingStage.Wash) BuildNozzle(); else BuildDryer();
            transform.position = restPosition;
            _aim = restPosition + Vector3.down;
            transform.rotation = Quaternion.LookRotation(Vector3.down, cam.transform.forward);
        }

        GameObject Part(string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            _meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent != null ? parent : _root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // tools never cast realtime shadows (mobile)
            return go;
        }

        // Model space: local +Z is the stream direction (LookRotation toward the aim point).
        void BuildNozzle()
        {
            var toZ = Quaternion.Euler(90f, 0f, 0f);
            Part("Grip", ProceduralMeshFactory.Cylinder("NozzleGrip", 0.024f, 0.021f, 0.085f, 18), _bodyMaterial, new Vector3(0, 0, -0.12f), toZ);
            Part("Ring", ProceduralMeshFactory.Cylinder("NozzleRing", 0.026f, 0.026f, 0.014f, 18), _accentMaterial, new Vector3(0, 0, -0.04f), toZ);
            Part("Tip", ProceduralMeshFactory.Cylinder("NozzleTip", 0.018f, 0.009f, 0.04f, 18), _accentMaterial, new Vector3(0, 0, -0.035f), toZ);
            var trig = Part("Trigger", ProceduralMeshFactory.Box("NozzleTrigger", new Vector3(0.012f, 0.035f, 0.05f)), _accentMaterial, new Vector3(0, -0.03f, -0.1f), Quaternion.Euler(12f, 0, 0));
            _tip = new GameObject("Tip").transform; _tip.SetParent(_root, false); _tip.localPosition = new Vector3(0, 0, 0.006f);
            _jet = CreateLine("WaterJet", _streamMaterial, 0.012f, 0.03f, new Color(0.75f, 0.9f, 1f, 0.6f), new Color(0.85f, 0.95f, 1f, 0.25f));
            _hose = CreateLine("Hose", _hoseMaterial, 0.028f, 0.028f, Color.white, Color.white);
            _hose.positionCount = 8;
            _hose.numCapVertices = 2;
        }

        void BuildDryer()
        {
            var toZ = Quaternion.Euler(90f, 0f, 0f);
            Part("Body", ProceduralMeshFactory.Ellipsoid("DryerBody", new Vector3(0.05f, 0.05f, 0.085f), 20, 14), _bodyMaterial, new Vector3(0, 0, -0.13f), Quaternion.identity);
            Part("Barrel", ProceduralMeshFactory.Cylinder("DryerBarrel", 0.036f, 0.03f, 0.075f, 20), _bodyMaterial, new Vector3(0, 0, -0.085f), toZ);
            Part("Nozzle", ProceduralMeshFactory.Cylinder("DryerNozzle", 0.031f, 0.026f, 0.02f, 20), _accentMaterial, new Vector3(0, 0, -0.012f), toZ);
            Part("Handle", ProceduralMeshFactory.Ellipsoid("DryerHandle", new Vector3(0.022f, 0.065f, 0.026f), 14, 10), _bodyMaterial, new Vector3(0, -0.07f, -0.155f), Quaternion.Euler(-14f, 0, 0));
            var back = Part("BackGrille", ProceduralMeshFactory.Cylinder("DryerGrille", 0.032f, 0.032f, 0.006f, 20), _accentMaterial, new Vector3(0, 0, -0.218f), toZ);
            _spinner = back.transform;
            _tip = new GameObject("Tip").transform; _tip.SetParent(_root, false); _tip.localPosition = new Vector3(0, 0, 0.01f);
            // faint air cone (open cylinder, unlit transparent, low overdraw)
            var coneGo = Part("AirCone", ProceduralMeshFactory.Cylinder("AirCone", 0.022f, 0.06f, 1f, 20, false), _streamMaterial, Vector3.zero, toZ, _tip);
            _cone = coneGo.GetComponent<MeshRenderer>();
            _cone.enabled = false;
        }

        LineRenderer CreateLine(string name, Material mat, float w0, float w1, Color c0, Color c1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform.parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.positionCount = 2;
            lr.startWidth = w0; lr.endWidth = w1;
            lr.startColor = c0; lr.endColor = c1;
            lr.useWorldSpace = true;
            lr.numCapVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Tool state for this frame. <paramref name="aim"/> is the world point the stream lands on.</summary>
        public void SetState(bool on, Vector3 aim)
        {
            _on = on;
            if (on) _aim = aim;
        }

        void LateUpdate()
        {
            if (_root == null) return;
            float dt = Time.deltaTime;
            _flow = Mathf.MoveTowards(_flow, _on ? 1f : 0f, dt * (_on ? 9f : 12f));
            Vector3 camPos = _cam.transform.position;
            Vector3 target;
            if (_on)
            {
                Vector3 toCam = (camPos - _aim).normalized;
                target = _aim + toCam * _standOff + _cam.transform.right * _sideOffset + Vector3.up * _lift;
            }
            else target = _restPos;
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _vel, _on ? _followTime : 0.18f);
            Vector3 look = _on ? (_aim - transform.position) : (_restPos + Vector3.down * 0.4f + _cam.transform.forward * 0.2f - transform.position);
            if (look.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-18f * dt));

            if (_jet != null)
            {
                _jet.enabled = _flow > 0.02f;
                if (_jet.enabled)
                {
                    Vector3 a = _tip.position;
                    Vector3 b = Vector3.Lerp(a, _aim, _flow);
                    _jet.SetPosition(0, a);
                    _jet.SetPosition(1, b);
                    float wob = 1f + Mathf.Sin(Time.time * 37f) * 0.08f;
                    _jet.startWidth = 0.010f * wob; _jet.endWidth = 0.028f * wob * _flow;
                }
                UpdateHose();
            }
            if (_cone != null)
            {
                _cone.enabled = _flow > 0.02f;
                if (_cone.enabled)
                {
                    float len = Mathf.Max(0.05f, Vector3.Distance(_tip.position, _aim)) * _flow;
                    _cone.transform.localScale = new Vector3(1f + Mathf.Sin(Time.time * 23f) * 0.05f, len, 1f);
                }
                if (_spinner != null && _flow > 0f) _spinner.Rotate(0f, 1400f * _flow * dt, 0f, Space.Self);
            }
        }

        // hose hangs from the grip toward the bottom of the screen (simple catenary-like curve)
        void UpdateHose()
        {
            if (_hose == null) return;
            _hose.enabled = true;
            Vector3 start = _root.TransformPoint(new Vector3(0, 0, -0.165f));
            // the hose runs off the right edge of the table at roughly the tool's depth (never toward the lens)
            Vector3 right = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
            Vector3 toCam = Vector3.ProjectOnPlane(_cam.transform.position - start, Vector3.up).normalized;
            // drops down behind the nozzle first, then leaves over the near-right table edge
            Vector3 end = start + right * 0.45f + toCam * 0.35f + Vector3.down * 0.45f;
            Vector3 ctrl = start + toCam * 0.12f + right * 0.05f + Vector3.down * 0.02f;
            for (int i = 0; i < _hose.positionCount; i++)
            {
                float t = i / (float)(_hose.positionCount - 1);
                Vector3 p = Vector3.Lerp(Vector3.Lerp(start, ctrl, t), Vector3.Lerp(ctrl, end, t), t);
                _hose.SetPosition(i, p);
            }
        }

        void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            if (_jet != null) Destroy(_jet.gameObject);
            if (_hose != null) Destroy(_hose.gameObject);
        }
    }
}
