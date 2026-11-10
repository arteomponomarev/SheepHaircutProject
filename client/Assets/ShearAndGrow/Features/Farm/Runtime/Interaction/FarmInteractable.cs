using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// A tappable thing on the farm. Logic (what happens on tap, attention badge) is independent from the visual:
    /// <see cref="ViewRoot"/> can hold a 3D model today or a 2.5D sprite later, and the tap target is a separate
    /// collider on the FarmInteractable layer. Building ids link the scene view to <see cref="FarmLayout"/> data.
    /// </summary>
    public abstract class FarmInteractable : MonoBehaviour
    {
        public const string LayerName = "FarmInteractable";

        [SerializeField] private string _buildingId = "building";
        [SerializeField] private string _displayName = "Building";
        [Tooltip("Visual root (model or sprite). Pulsed and highlighted on selection.")]
        [SerializeField] private Transform _viewRoot;
        [Tooltip("Where the world label floats.")]
        [SerializeField] private Transform _labelAnchor;
        [SerializeField] private bool _showLabel = true;

        Renderer[] _renderers;
        Bounds _bounds;
        bool _boundsValid;

        public string BuildingId => _buildingId;
        public string DisplayName => _displayName;
        /// <summary>Building instance this interaction belongs to (null for sheep / non-building interactables).</summary>
        public string InstanceId { get; private set; }
        /// <summary>Label must follow this target every frame (moving things).</summary>
        public bool MovesEveryFrame => IsDynamic;

        /// <summary>Binds a spawned building view to its saved instance (positions come from the grid, not the scene).</summary>
        public void BindInstance(string instanceId, string typeId, string displayName)
        {
            InstanceId = instanceId;
            _buildingId = instanceId; // unique per instance → panels / labels / return-panel keys stay unambiguous
            TypeId = typeId;
            _displayName = displayName;
            RefreshRenderers();
        }

        public string TypeId { get; private set; }
        public Transform ViewRoot => _viewRoot != null ? _viewRoot : transform;
        public Vector3 LabelPosition => _labelAnchor != null ? _labelAnchor.position : ViewBounds.center + Vector3.up * ViewBounds.extents.y;
        public bool ShowLabel => _showLabel;

        public Renderer[] Renderers
        {
            get
            {
                if (_renderers == null || _renderers.Length == 0) _renderers = ViewRoot.GetComponentsInChildren<Renderer>(true);
                return _renderers;
            }
        }

        public Bounds ViewBounds
        {
            get
            {
                if (_boundsValid && !IsDynamic) return _bounds;
                var rs = Renderers;
                _bounds = new Bounds(transform.position, Vector3.one * 0.5f);
                bool first = true;
                foreach (var r in rs)
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                    if (first) { _bounds = r.bounds; first = false; } else _bounds.Encapsulate(r.bounds);
                }
                _boundsValid = true;
                return _bounds;
            }
        }

        /// <summary>True for things that move (sheep): bounds are recomputed on demand.</summary>
        protected virtual bool IsDynamic => false;

        public void RefreshRenderers() { _renderers = null; _boundsValid = false; }

        /// <summary>Something can be done here right now (shows a badge on the label).</summary>
        public virtual bool HasAttention(FarmHubController hub) => false;

        /// <summary>Label text (may include a short status).</summary>
        public virtual string LabelText(FarmHubController hub) => _displayName;

        /// <summary>Tap action: open a panel or start an activity.</summary>
        public abstract void OnTap(FarmHubController hub);

        public void Configure(string id, string displayName, Transform view, Transform labelAnchor)
        {
            _buildingId = id; _displayName = displayName; _viewRoot = view; _labelAnchor = labelAnchor;
        }
    }
}
