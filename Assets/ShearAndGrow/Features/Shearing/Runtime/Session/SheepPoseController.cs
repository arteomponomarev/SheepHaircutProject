using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Lets the player reach the belly: the sheep rolls around its long axis (like a shearer tipping it) so the
    /// underside turns toward the camera, while its side rests on the floor. The whole sheep root is rotated, and the
    /// shear mask, collider, clump anchors and soft-body collisions are all in body-local space, so shearing, physics
    /// and persistence keep working unchanged in any roll pose.
    /// </summary>
    [DefaultExecutionOrder(-20)] // before the camera reads the body centre in LateUpdate
    public sealed class SheepPoseController : MonoBehaviour
    {
        [Tooltip("Roll angle at full belly pose (degrees).")]
        [SerializeField, Range(30f, 95f)] private float _maxRoll = 82f;
        [Tooltip("Height of the body centre when lying on the side (wool resting on the floor).")]
        [SerializeField] private float _lyingCenterHeight = 0.66f;
        [Tooltip("How quickly the pose follows its target (higher = snappier).")]
        [SerializeField] private float _sharpness = 9f;

        Transform _root;
        Vector3 _basePos;
        Quaternion _baseRot;
        Vector3 _centerLocal;
        float _roll01;
        float _target01;
        int _side = 1;
        bool _ready;

        /// <summary>Current (smoothed) roll 0 = standing, 1 = belly toward the camera.</summary>
        public float Roll01 => _roll01;
        public float Target01 => _target01;
        public int Side => _side;
        public bool IsBellyMode => _target01 > 0.5f;
        public Vector3 BodyCenterWorld => _ready ? _root.TransformPoint(_centerLocal) : transform.position;
        /// <summary>Sheep facing (yaw) in the standing pose, degrees.</summary>
        public float BaseYaw => _ready ? _baseRot.eulerAngles.y : 0f;

        public void Initialise(Transform root, Vector3 bodyCenterLocal)
        {
            _root = root;
            _basePos = root.position;
            _baseRot = root.rotation;
            _centerLocal = bodyCenterLocal;
            _roll01 = _target01 = 0f;
            _ready = true;
        }

        /// <summary>Which flank goes down: chosen so the belly turns toward the camera. Only changes while standing.</summary>
        public void ChooseSideFacing(Vector3 cameraWorldPos)
        {
            if (!_ready || _roll01 > 0.02f || _target01 > 0.02f) return;
            Vector3 local = Quaternion.Inverse(_baseRot) * (cameraWorldPos - _basePos);
            _side = local.z >= 0f ? 1 : -1;
        }

        /// <summary>Adds roll (0..1 units). Returns the part that could not be applied (for the camera to use).</summary>
        public float AddRoll(float delta01)
        {
            float before = _target01;
            _target01 = Mathf.Clamp01(_target01 + delta01);
            return delta01 - (_target01 - before);
        }

        public void SetTarget(float target01) => _target01 = Mathf.Clamp01(target01);

        public void ResetPose(bool instant)
        {
            _target01 = 0f;
            if (instant) { _roll01 = 0f; Apply(); }
        }

        void LateUpdate()
        {
            if (!_ready) return;
            if (Mathf.Abs(_roll01 - _target01) < 1e-4f && _roll01 == _target01) return;
            _roll01 = Mathf.Lerp(_roll01, _target01, 1f - Mathf.Exp(-_sharpness * Time.deltaTime));
            if (Mathf.Abs(_roll01 - _target01) < 1e-3f) _roll01 = _target01;
            Apply();
        }

        void Apply()
        {
            float t = Mathf.SmoothStep(0f, 1f, _roll01);
            // Rotation about the sheep's long axis (local X). Sign: belly (-Y) swings toward the chosen camera side.
            Quaternion rot = _baseRot * Quaternion.Euler(-_side * _maxRoll * t, 0f, 0f);
            Vector3 centerBase = _basePos + _baseRot * _centerLocal;
            Vector3 center = new Vector3(centerBase.x, Mathf.Lerp(centerBase.y, _basePos.y + _lyingCenterHeight, t), centerBase.z);
            _root.SetPositionAndRotation(center - rot * _centerLocal, rot);
        }
    }
}
