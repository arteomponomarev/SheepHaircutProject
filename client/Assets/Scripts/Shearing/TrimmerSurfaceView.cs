using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Presentation only. The model's local +Y points away from the surface.</summary>
    public sealed class TrimmerSurfaceView : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField, Min(0f)] private float surfaceOffset = 0.035f;

        public void ShowAt(Vector3 point, Vector3 normal, Vector3 viewUp)
        {
            Vector3 forward = Vector3.ProjectOnPlane(viewUp, normal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(normal, Vector3.right);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.Cross(normal, Vector3.forward);

            transform.SetPositionAndRotation(point + normal * surfaceOffset,
                Quaternion.LookRotation(forward.normalized, normal));
            if (!visualRoot.gameObject.activeSelf)
                visualRoot.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (visualRoot != null && visualRoot.gameObject.activeSelf)
                visualRoot.gameObject.SetActive(false);
        }
    }
}
