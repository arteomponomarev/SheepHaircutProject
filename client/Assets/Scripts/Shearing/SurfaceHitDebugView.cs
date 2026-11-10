using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Optional Scene/Game view gizmos; has no effect on hit detection.</summary>
    public sealed class SurfaceHitDebugView : MonoBehaviour
    {
        [SerializeField] private TrimmerPointerDriver source;
        [SerializeField] private bool showHit = true;
        [SerializeField, Min(0.001f)] private float pointRadius = 0.025f;
        [SerializeField, Min(0.01f)] private float normalLength = 0.35f;

        private void OnDrawGizmos()
        {
            if (!showHit || source == null || !source.HasContact)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(source.CurrentHit.point, pointRadius);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(source.CurrentHit.point,
                source.CurrentHit.point + source.CurrentHit.normal * normalLength);
        }
    }
}
