using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Camera presentation only. The sheep, mask, collider and physics never rotate.</summary>
    public sealed class SheepOrbitCamera : MonoBehaviour
    {
        [SerializeField] private ShearingGestureRouter gestures;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Collider target;
        [SerializeField, Range(90f, 720f)] private float degreesPerScreen = 240f;
        [SerializeField, Range(60f, 89f)] private float elevationLimit = 85f;
        [Tooltip("Hidden for underside views only; its collider stays active.")]
        [SerializeField] private Renderer ground;
        private float yaw, elevation, distance;
        private bool initialized, groundWasEnabled;

        private void OnEnable()
        {
            if (gestures == null || viewCamera == null || target == null)
            { Debug.LogError("Orbit camera requires gesture, camera and target references.", this); enabled = false; return; }
            Vector3 offset = viewCamera.transform.position - target.bounds.center;
            distance = Mathf.Max(0.1f, offset.magnitude);
            yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            elevation = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            groundWasEnabled = ground != null && ground.enabled; initialized = true;
            gestures.OrbitDelta += Orbit;
        }
        private void Orbit(Vector2 delta)
        {
            float pixels = Mathf.Max(1f, Mathf.Min(viewCamera.pixelWidth, viewCamera.pixelHeight));
            yaw = Mathf.Repeat(yaw - delta.x * degreesPerScreen / pixels, 360f);
            elevation = Mathf.Clamp(elevation - delta.y * degreesPerScreen / pixels, -elevationLimit, elevationLimit);
            float pitch = elevation * Mathf.Deg2Rad, heading = yaw * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(heading) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(heading) * Mathf.Cos(pitch));
            viewCamera.transform.SetPositionAndRotation(target.bounds.center + direction * distance, Quaternion.LookRotation(-direction, Vector3.up));
            if (ground != null) ground.enabled = groundWasEnabled && viewCamera.transform.position.y > ground.bounds.max.y + 0.05f;
        }
        private void OnDisable()
        {
            if (gestures != null) gestures.OrbitDelta -= Orbit;
            if (initialized && ground != null) ground.enabled = groundWasEnabled;
        }
    }
}
