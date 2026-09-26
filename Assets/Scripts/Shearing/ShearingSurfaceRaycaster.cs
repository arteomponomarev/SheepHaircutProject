using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Maps screen coordinates to one explicitly assigned shearable surface.</summary>
    public sealed class ShearingSurfaceRaycaster : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Collider surfaceCollider;
        [SerializeField, Min(0.1f)] private float maxDistance = 30f;

        public Vector3 ViewUp => viewCamera.transform.up;

        public bool TryGetHit(Vector2 screenPosition, out RaycastHit hit)
        {
            hit = default;
            if (viewCamera == null || surfaceCollider == null || !surfaceCollider.enabled ||
                !surfaceCollider.gameObject.activeInHierarchy || !viewCamera.pixelRect.Contains(screenPosition))
                return false;

            // Collider.Raycast deliberately excludes the floor, head, trimmer and unrelated objects.
            return surfaceCollider.Raycast(viewCamera.ScreenPointToRay(screenPosition), out hit, maxDistance);
        }
    }
}
