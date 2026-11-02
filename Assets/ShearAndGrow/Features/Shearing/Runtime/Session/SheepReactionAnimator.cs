using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Procedural micro-animations (spec §4.2): breathing, blinking, ears, calm head interest in the trimmer,
    /// comfort cues and a happy completion. Breathing scales the body pivot uniformly so the mask and
    /// clump anchors (stored in body-local space) never slide.
    /// </summary>
    public sealed class SheepReactionAnimator : MonoBehaviour
    {
        [Header("Breathing")]
        [SerializeField] private float _breathRate = 0.28f;
        [SerializeField] private float _breathAmount = 0.012f;
        [Header("Blink")]
        [SerializeField] private Vector2 _blinkInterval = new Vector2(2.2f, 5.5f);
        [SerializeField] private float _blinkDuration = 0.14f;
        [Header("Head")]
        [SerializeField] private float _headFollow = 2.2f;
        [SerializeField] private float _maxHeadYaw = 28f;
        [SerializeField] private float _maxHeadPitch = 14f;

        SheepRigBuilder _rig;
        Transform _trimmerTarget;
        Transform _cameraTarget;
        Quaternion _headRest;
        Quaternion _leftEarRest, _rightEarRest, _tailRest;
        float _nextBlink;
        float _blinkT = -1f;
        float _earTwitch;
        float _comfort;
        float _happyTimer;
        bool _trimmerOn;
        float _stretchTimer;
        System.Random _rng;

        public void Initialise(SheepRigBuilder rig, Transform trimmerTarget, Transform cameraTarget, ShearingEventHub hub, int seed)
        {
            _rig = rig;
            _trimmerTarget = trimmerTarget;
            _cameraTarget = cameraTarget;
            _rng = new System.Random(seed);
            _headRest = rig.HeadPivot.localRotation;
            _leftEarRest = rig.LeftEar.localRotation;
            _rightEarRest = rig.RightEar.localRotation;
            _tailRest = rig.Tail.localRotation;
            _nextBlink = Time.time + Range(_blinkInterval);
            hub.ContactChanged += c => { _trimmerOn = c; if (c) _earTwitch = 1f; };
            hub.CutDelta += e => _comfort = Mathf.Min(1f, _comfort + e.NewAreaNormalized * 25f);
            hub.Completed += () => _happyTimer = 2.4f;
        }

        float Range(Vector2 r) => r.x + (float)_rng.NextDouble() * (r.y - r.x);

        void Update()
        {
            if (_rig == null) return;
            float t = Time.time;

            // Breathing: uniform pivot scale (mask is in body-local parametrisation, so it never slides).
            float breath = Mathf.Sin(t * _breathRate * Mathf.PI * 2f);
            float s = 1f + breath * _breathAmount + Mathf.Sin(_stretchTimer * Mathf.PI) * 0.015f;
            _rig.BodyPivot.localScale = new Vector3(1f + (s - 1f) * 0.4f, s, s);

            // Blink
            if (_blinkT < 0f && t >= _nextBlink) _blinkT = 0f;
            float eyeOpen = 1f;
            if (_blinkT >= 0f)
            {
                _blinkT += Time.deltaTime / _blinkDuration;
                eyeOpen = Mathf.Abs(_blinkT * 2f - 1f);
                if (_blinkT >= 1f) { _blinkT = -1f; _nextBlink = t + Range(_blinkInterval); }
            }
            if (_happyTimer > 0f) eyeOpen = Mathf.Min(eyeOpen, 0.35f); // happy squint
            float eyeY = Mathf.Lerp(0.08f, 1f, eyeOpen);
            _rig.LeftEye.localScale = new Vector3(1f, eyeY, 1f);
            _rig.RightEye.localScale = new Vector3(1f, eyeY, 1f);

            // Head: calm interest in the trimmer, or look at the player on completion.
            Quaternion target = _headRest;
            Transform look = _happyTimer > 0f ? _cameraTarget : (_trimmerOn ? _trimmerTarget : null);
            if (look != null)
            {
                Vector3 dir = _rig.HeadPivot.parent.InverseTransformDirection(look.position - _rig.HeadPivot.position);
                float yaw = Mathf.Clamp(-Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg, -_maxHeadYaw, _maxHeadYaw);
                float pitch = Mathf.Clamp(Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -_maxHeadPitch, _maxHeadPitch);
                target = _headRest * Quaternion.Euler(0f, yaw, pitch);
            }
            float idleYaw = Mathf.Sin(t * 0.21f) * 6f + Mathf.Sin(t * 0.53f) * 2f;
            float idleTilt = Mathf.Sin(t * 0.17f + 1.3f) * 4f;
            target *= Quaternion.Euler(idleTilt, idleYaw, 0f);
            _rig.HeadPivot.localRotation = Quaternion.Slerp(_rig.HeadPivot.localRotation, target, 1f - Mathf.Exp(-_headFollow * Time.deltaTime));

            // Ears: independent sway, twitch when the motor starts nearby.
            _earTwitch = Mathf.MoveTowards(_earTwitch, 0f, Time.deltaTime * 2.5f);
            float twitch = Mathf.Sin(t * 30f) * _earTwitch * 12f;
            _rig.LeftEar.localRotation = _leftEarRest * Quaternion.Euler(Mathf.Sin(t * 0.9f) * 6f + twitch, 0f, Mathf.Sin(t * 0.7f) * 5f);
            _rig.RightEar.localRotation = _rightEarRest * Quaternion.Euler(Mathf.Sin(t * 0.8f + 2f) * 6f - twitch * 0.6f, 0f, Mathf.Sin(t * 0.65f + 1f) * 5f);

            // Comfort: after gentle shearing the sheep relaxes / stretches a little.
            _comfort = Mathf.MoveTowards(_comfort, 0f, Time.deltaTime * 0.15f);
            if (!_trimmerOn && _comfort > 0.5f && _stretchTimer <= 0f) { _stretchTimer = 1f; _comfort = 0f; }
            if (_stretchTimer > 0f) _stretchTimer = Mathf.Max(0f, _stretchTimer - Time.deltaTime * 0.6f);

            // Tail: small wag, energetic on completion.
            float wag = _happyTimer > 0f ? Mathf.Sin(t * 18f) * 28f : Mathf.Sin(t * 1.3f) * 6f;
            _rig.Tail.localRotation = _tailRest * Quaternion.Euler(0f, wag, 0f);
            if (_happyTimer > 0f) _happyTimer -= Time.deltaTime;
        }
    }
}
