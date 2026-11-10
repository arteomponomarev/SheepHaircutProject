using UnityEngine;
using UnityEngine.Rendering;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Visual build grid shown only while placing / editing: ONE ground quad (one draw call) whose small texture
    /// (8 px per cell) holds the cell lines and the occupancy tint. The texture is rebuilt only when the grid's
    /// <see cref="FarmGrid.Version"/> changes — never per frame, no allocations after the first build.
    /// Also owns the footprint highlight quad under the ghost (green / red via MaterialPropertyBlock).
    /// </summary>
    public sealed class GridOverlay : MonoBehaviour
    {
        const int Px = 8;
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Material _gridMaterial;
        [SerializeField] private Material _footprintMaterial;
        [SerializeField] private Color _line = new Color(1f, 1f, 1f, 0.32f);
        [SerializeField] private Color _free = new Color(1f, 1f, 1f, 0.05f);
        [SerializeField] private Color _occupied = new Color(0.95f, 0.35f, 0.25f, 0.30f);
        [SerializeField] private Color _reserved = new Color(0.55f, 0.45f, 0.35f, 0.34f);

        FarmGrid _grid;
        FarmLayout _layout;
        Texture2D _tex;
        Color32[] _px;
        byte[] _occ;
        int _builtVersion = -1;
        MeshRenderer _gridR, _footR;
        MaterialPropertyBlock _mpb;

        public bool Visible => _gridR != null && _gridR.enabled;

        public void Configure(Material grid, Material footprint) { _gridMaterial = grid; _footprintMaterial = footprint; }

        public void Initialise(FarmGrid grid, FarmLayout layout)
        {
            _grid = grid; _layout = layout;
            if (_gridR == null)
            {
                _gridR = CreateQuad("GridQuad", _gridMaterial, 0.03f);
                _footR = CreateQuad("FootprintQuad", _footprintMaterial, 0.05f);
                _mpb = new MaterialPropertyBlock();
            }
            int w = grid.Width * Px, h = grid.Height * Px;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "GridOverlay" };
                _px = new Color32[w * h];
                _occ = new byte[grid.Width * grid.Height];
            }
            var t = _gridR.transform;
            t.position = new Vector3(0f, 0.03f, 0f);
            t.localScale = new Vector3(grid.Width * layout.CellSize, grid.Height * layout.CellSize, 1f);
            _gridR.GetPropertyBlock(_mpb);
            _mpb.SetTexture(BaseMapId, _tex);
            _mpb.SetColor(BaseColorId, Color.white);
            _gridR.SetPropertyBlock(_mpb);
            _builtVersion = -1;
            SetVisible(false);
            ShowFootprint(false, default, Vector2Int.one, Color.clear);
        }

        MeshRenderer CreateQuad(string n, Material m, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = n;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.position = new Vector3(0f, y, 0f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public void SetVisible(bool on)
        {
            if (_gridR == null) return;
            _gridR.enabled = on;
            if (on) Refresh();
            if (!on) _footR.enabled = false;
        }

        /// <summary>Rebuilds the texture only if the grid changed since the last build.</summary>
        public void Refresh()
        {
            if (_grid == null || _builtVersion == _grid.Version) return;
            _builtVersion = _grid.Version;
            _grid.WriteOccupancy(_occ);
            int w = _grid.Width * Px;
            Color32 line = _line, free = _free, occ = _occupied, res = _reserved;
            for (int cy = 0; cy < _grid.Height; cy++)
                for (int cx = 0; cx < _grid.Width; cx++)
                {
                    byte o = _occ[cx + cy * _grid.Width];
                    Color32 fill = o == 2 ? occ : o == 1 ? res : free;
                    for (int py = 0; py < Px; py++)
                    {
                        int row = (cy * Px + py) * w + cx * Px;
                        for (int px = 0; px < Px; px++)
                            _px[row + px] = (px == 0 || py == 0) ? line : fill;
                    }
                }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        public void ShowFootprint(bool on, Vector3 center, Vector2Int footprint, Color color)
        {
            if (_footR == null) return;
            _footR.enabled = on;
            if (!on) return;
            var t = _footR.transform;
            t.position = new Vector3(center.x, 0.05f, center.z);
            float cs = _layout != null ? _layout.CellSize : 1f;
            t.localScale = new Vector3(footprint.x * cs, footprint.y * cs, 1f);
            _footR.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _footR.SetPropertyBlock(_mpb);
        }

        void LateUpdate() { if (Visible) Refresh(); }

        void OnDestroy() { if (_tex != null) Destroy(_tex); }
    }
}