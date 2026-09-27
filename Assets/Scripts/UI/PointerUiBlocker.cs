using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShearAndGrow
{
    /// <summary>Immediate UI hit testing; independent of Input System callback order.</summary>
    public sealed class PointerUiBlocker : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.GraphicRaycaster raycaster;
        [SerializeField] private EventSystem eventSystem;
        private PointerEventData pointer;
        private readonly List<RaycastResult> hits = new List<RaycastResult>(16);

        public bool Contains(Vector2 position)
        {
            if (raycaster == null || eventSystem == null) return true; // Fail closed.
            pointer ??= new PointerEventData(eventSystem);
            pointer.Reset(); pointer.position = position;
            hits.Clear(); raycaster.Raycast(pointer, hits);
            return hits.Count != 0;
        }
    }
}
