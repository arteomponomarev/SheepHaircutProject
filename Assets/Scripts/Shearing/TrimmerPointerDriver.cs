using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Wires pointer gestures to surface tracking. No wool, reward or sheep-state logic.</summary>
    public sealed class TrimmerPointerDriver : MonoBehaviour
    {
        [SerializeField] private ShearingGestureRouter gestures;
        [SerializeField] private ShearingSurfaceRaycaster raycaster;
        [SerializeField] private TrimmerSurfaceView trimmerView;
        [SerializeField, Min(0f)] private float followTime = 0.025f;

        public bool HasContact { get; private set; }
        public RaycastHit CurrentHit { get; private set; }
        public event Action<RaycastHit> SurfaceSampled;
        public event Action ContactEnded;

        private Vector2 targetPosition;
        private Vector2 smoothedPosition;
        private bool gestureActive;

        private void OnEnable()
        {
            if (gestures == null || raycaster == null || trimmerView == null)
            {
                Debug.LogError("TrimmerPointerDriver requires gestures, raycaster and trimmer view references.", this);
                enabled = false;
                return;
            }
            gestures.ShearPosition += TrackPointer;
            gestures.GestureEnded += EndGesture;
            ClearContact();
        }

        private void TrackPointer(Vector2 position)
        {
            targetPosition = position;
            gestureActive = true;
        }

        private void LateUpdate()
        {
            if (!gestureActive || !gestures.IsShearing)
                return;
            // Test the real pointer first: leaving the sheep drops contact immediately.
            if (!raycaster.TryGetHit(targetPosition, out _))
            {
                ClearContact();
                return;
            }

            // Smooth in screen space and re-project, rather than cutting through curved geometry.
            float blend = followTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / followTime);
            smoothedPosition = HasContact ? Vector2.Lerp(smoothedPosition, targetPosition, blend) : targetPosition;
            if (!raycaster.TryGetHit(smoothedPosition, out RaycastHit hit))
            {
                ClearContact();
                return;
            }

            CurrentHit = hit;
            HasContact = true;
            trimmerView.ShowAt(hit.point, hit.normal, raycaster.ViewUp);
            SurfaceSampled?.Invoke(hit);
        }

        private void EndGesture()
        {
            gestureActive = false;
            ClearContact();
        }

        private void ClearContact()
        {
            bool hadContact = HasContact;
            HasContact = false;
            CurrentHit = default;
            if (trimmerView != null)
                trimmerView.Hide();
            if (hadContact)
                ContactEnded?.Invoke();
        }

        private void OnDisable()
        {
            if (gestures != null)
            {
                gestures.ShearPosition -= TrackPointer;
                gestures.GestureEnded -= EndGesture;
            }
            EndGesture();
        }
    }
}
