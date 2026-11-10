using System;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>Lets a gesture owner take over one-finger drags / taps before the camera gets them.</summary>
    public interface IFarmDragInterceptor
    {
        bool TryBeginDrag(Vector2 screen);
        void Drag(Vector2 screen);
        void EndDrag();
        /// <summary>Return true when the tap was consumed.</summary>
        bool HandleTap(Vector2 screen);
    }

    public enum PlacementMode { None = 0, PlaceNew = 1, PlaceFromInventory = 2, Move = 3 }

    /// <summary>
    /// Placement / move mode. Shows a translucent ghost of the definition's visual, snaps it to cells with
    /// hysteresis (no jitter between two cells), lifts it above the finger on touch, edge-pans the camera, and
    /// validates only when the snapped cell changes. Confirm calls the <see cref="BuildingService"/>; Cancel never
    /// touches the economy. Pure presentation + input: all rules live in the service.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour, IFarmDragInterceptor
    {
        [SerializeField] private Material _ghostMaterial;
        [Tooltip("Cell fraction the finger must cross beyond a border before the snapped cell changes.")]
        [SerializeField, Range(0f, 0.5f)] private float _hysteresis = 0.25f;
        [Tooltip("Ghost lifted above the finger on touch screens (px at 1080 reference height).")]
        [SerializeField] private float _touchLiftPx = 70f;
        [Tooltip("Screen fraction near the edges that pans the camera while dragging the ghost.")]
        [SerializeField, Range(0f, 0.25f)] private float _edgeMargin = 0.08f;
        [SerializeField] private float _edgePanSpeed = 9f;
        [SerializeField] private Color _validColor = new Color(0.45f, 1f, 0.55f, 0.55f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.38f, 0.32f, 0.55f);

        BuildingService _svc;
        FarmWorldPresenter _presenter;
        FarmCameraController _camera;
        GridOverlay _overlay;
        BuildingView _ghost;
        FarmBuildingDefinition _def;
        string _moveId, _purchaseId;
        Vector2Int _cell;
        Vector2 _grabOffsetCells;
        bool _dragging;
        Vector2 _dragScreen;
        bool _touchDrag;

        public PlacementMode Mode { get; private set; }
        public bool IsActive => Mode != PlacementMode.None;
        public FarmBuildingDefinition Definition => _def;
        public Vector2Int Cell => _cell;
        public PlacementResult Result { get; private set; }
        public string MovingInstanceId => _moveId;
        public bool IsDragging => _dragging;
        public BuildingView Ghost => _ghost;
        /// <summary>Cost of confirming (0 for moves and inventory placement).</summary>
        public int Price => Mode == PlacementMode.PlaceNew && _def != null ? _def.Price : 0;

        public event Action Changed;
        /// <summary>(confirmed, instance) — instance is null when cancelled.</summary>
        public event Action<bool, BuildingInstanceState> Finished;
        public event Action Started;

        public void Configure(Material ghost) { _ghostMaterial = ghost; }

        public void Initialise(BuildingService svc, FarmWorldPresenter presenter, FarmCameraController cam, GridOverlay overlay)
        {
            _svc = svc; _presenter = presenter; _camera = cam; _overlay = overlay;
        }

        // ------------------------------------------------------------------ begin / end

        public bool BeginPlace(FarmBuildingDefinition def, bool fromInventory)
        {
            if (def == null) return false;
            CancelSilently();
            _def = def;
            Mode = fromInventory ? PlacementMode.PlaceFromInventory : PlacementMode.PlaceNew;
            _purchaseId = Guid.NewGuid().ToString("N"); // one id per placement session → a double confirm never charges twice
            if (!CreateGhost()) { Mode = PlacementMode.None; return false; }
            _cell = FindStartCell(ScreenCenterCell(), null);
            ApplyCell(_cell, false);
            FocusGhost();
            Started?.Invoke();
            return true;
        }

        public bool BeginMove(string instanceId)
        {
            var b = _svc.State.Find(instanceId);
            if (b == null || !b.IsPlaced) return false;
            CancelSilently();
            _def = _svc.Definition(b.typeId);
            _moveId = instanceId;
            Mode = PlacementMode.Move;
            if (!CreateGhost()) { Mode = PlacementMode.None; _moveId = null; return false; }
            _presenter.SetHidden(instanceId, true);
            _cell = b.cell;
            ApplyCell(_cell, false);
            Started?.Invoke();
            return true;
        }

        bool CreateGhost()
        {
            _ghost = _presenter.CreateDetached(_def);
            if (_ghost == null) return false;
            _ghost.SetGhost(_ghostMaterial, true);
            foreach (var c in _ghost.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            _overlay.SetVisible(true);
            return true;
        }

        /// <summary>Confirms the current placement. Returns true when the action was applied.</summary>
        public bool Confirm()
        {
            if (!IsActive) return false;
            Revalidate();
            if (Result != PlacementResult.Ok) { Changed?.Invoke(); return false; }
            BuildingInstanceState inst = null;
            PlacementResult r;
            bool ok;
            switch (Mode)
            {
                case PlacementMode.PlaceNew: ok = _svc.TryPurchase(_def, _cell, _purchaseId, out inst, out r); break;
                case PlacementMode.PlaceFromInventory: ok = _svc.TryPlaceFromInventory(_def.Id, _cell, out inst, out r); break;
                case PlacementMode.Move:
                    ok = _svc.TryMove(_moveId, _cell, out r);
                    inst = _svc.State.Find(_moveId);
                    break;
                default: return false;
            }
            Result = r;
            if (!ok) { Changed?.Invoke(); return false; }

            // chain placement for segment decor (fence / path): keep going with the next segment
            bool chain = Mode != PlacementMode.Move && _def.Connect != ConnectMode.None && CanChainAnother();
            var def = _def; var mode = Mode; var lastCell = _cell;
            if (chain)
            {
                _purchaseId = Guid.NewGuid().ToString("N");
                if (mode == PlacementMode.PlaceFromInventory && _svc.StoredCount(def.Id) == 0) Mode = PlacementMode.PlaceNew;
                _cell = FindStartCell(lastCell, null, preferAdjacent: true);
                ApplyCell(_cell, false);
                Finished?.Invoke(true, inst);
                Changed?.Invoke();
                return true;
            }
            End(false);
            Finished?.Invoke(true, inst);
            return true;
        }

        bool CanChainAnother()
        {
            if (Mode == PlacementMode.PlaceFromInventory && _svc.StoredCount(_def.Id) > 0) return true;
            return _svc.CheckPurchaseRules(_def) == PlacementResult.Ok;
        }

        public void Cancel()
        {
            if (!IsActive) return;
            End(true);
            Finished?.Invoke(false, null);
        }

        void CancelSilently() { if (IsActive) End(true); }

        void End(bool restore)
        {
            if (Mode == PlacementMode.Move && _moveId != null) _presenter.SetHidden(_moveId, false);
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            _overlay.ShowFootprint(false, default, Vector2Int.one, Color.clear);
            _overlay.SetVisible(false);
            Mode = PlacementMode.None;
            _def = null; _moveId = null; _purchaseId = null; _dragging = false;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ validation & visuals

        PlacementResult Validate(Vector2Int cell)
        {
            switch (Mode)
            {
                case PlacementMode.PlaceNew: return _svc.CheckPurchase(_def, cell);
                case PlacementMode.PlaceFromInventory:
                    return _svc.StoredCount(_def.Id) > 0 ? _svc.Grid.CheckArea(FarmGrid.RectOf(cell, _def.SafeFootprint), null, out _) : PlacementResult.Unknown;
                case PlacementMode.Move: return _svc.CheckMove(_svc.State.Find(_moveId), cell);
            }
            return PlacementResult.Unknown;
        }

        /// <summary>Re-checks the current cell (economy or grid changed while placing).</summary>
        public void Revalidate()
        {
            if (!IsActive) return;
            Result = Validate(_cell);
            UpdateTint();
        }

        void ApplyCell(Vector2Int cell, bool smooth)
        {
            _cell = cell;
            Result = Validate(cell);
            var center = _svc.Layout.FootprintCenterWorld(cell, _def.SafeFootprint);
            _ghost.SetWorldPosition(center, smooth);
            _ghost.SetConnections(_presenter.PreviewMask(_def, cell));
            UpdateTint();
            Changed?.Invoke();
        }

        void UpdateTint()
        {
            if (_ghost == null) return;
            bool ok = Result == PlacementResult.Ok;
            var c = ok ? _validColor : _invalidColor;
            _ghost.SetGhostTint(new Color(Mathf.Lerp(c.r, 1f, 0.35f), Mathf.Lerp(c.g, 1f, 0.35f), Mathf.Lerp(c.b, 1f, 0.35f), 0.62f));
            _overlay.ShowFootprint(true, _svc.Layout.FootprintCenterWorld(_cell, _def.SafeFootprint), _def.SafeFootprint, c);
        }

        Vector2Int ScreenCenterCell()
        {
            var g = _camera.ScreenToGround(new Vector2(Screen.width * 0.5f, Screen.height * 0.55f));
            var c = _svc.Layout.WorldToCell(g);
            var fp = _def.SafeFootprint;
            return new Vector2Int(c.x - fp.x / 2, c.y - fp.y / 2);
        }

        /// <summary>Nearest valid cell (spiral search) — the ghost starts somewhere it fits when possible.</summary>
        Vector2Int FindStartCell(Vector2Int around, string ignore, bool preferAdjacent = false)
        {
            var fp = _def.SafeFootprint;
            Vector2Int Clamp(Vector2Int c) => new Vector2Int(Mathf.Clamp(c.x, 0, Mathf.Max(0, _svc.Grid.Width - fp.x)), Mathf.Clamp(c.y, 0, Mathf.Max(0, _svc.Grid.Height - fp.y)));
            if (preferAdjacent)
            {
                // continue a line: try the four neighbours of the last segment first
                var dirs = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
                foreach (var d in dirs) { var c = around + d; if (Validate(c) == PlacementResult.Ok) return c; }
            }
            around = Clamp(around);
            if (Validate(around) == PlacementResult.Ok) return around;
            int maxR = Mathf.Max(_svc.Grid.Width, _svc.Grid.Height);
            for (int r = 1; r < maxR; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                        var c = new Vector2Int(around.x + dx, around.y + dy);
                        if (c.x < 0 || c.y < 0 || c.x + fp.x > _svc.Grid.Width || c.y + fp.y > _svc.Grid.Height) continue;
                        if (Validate(c) == PlacementResult.Ok) return c;
                    }
            return around;
        }

        void FocusGhost()
        {
            if (_ghost == null) return;
            var center = _svc.Layout.FootprintCenterWorld(_cell, _def.SafeFootprint);
            var size = new Vector3(_def.SafeFootprint.x, 2f, _def.SafeFootprint.y);
            _camera.FocusOn(new Bounds(center + Vector3.up, size), 0.55f, false);
        }

        // ------------------------------------------------------------------ input (IFarmDragInterceptor)

        Vector3 GroundUnder(Vector2 screen, bool lift)
        {
            if (lift) screen.y += _touchLiftPx * (Screen.height / 1920f);
            return _camera.ScreenToGround(screen);
        }

        static bool IsTouch() => UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.isPressed;

        public bool TryBeginDrag(Vector2 screen)
        {
            if (!IsActive || _ghost == null) return false;
            var p = _svc.Layout.WorldToCellF(_camera.ScreenToGround(screen));
            var fp = _def.SafeFootprint;
            const float margin = 0.6f;
            bool onGhost = p.x >= _cell.x - margin && p.x <= _cell.x + fp.x + margin && p.y >= _cell.y - margin && p.y <= _cell.y + fp.y + margin;
            if (!onGhost) return false;
            _touchDrag = IsTouch();
            // grab offset keeps the ghost from jumping under the finger; touch adds the lift
            var lifted = _svc.Layout.WorldToCellF(GroundUnder(screen, _touchDrag));
            _grabOffsetCells = new Vector2(_cell.x, _cell.y) - lifted;
            _dragging = true;
            _dragScreen = screen;
            return true;
        }

        public void Drag(Vector2 screen)
        {
            if (!_dragging) return;
            _dragScreen = screen;
            UpdateFromScreen(screen);
        }

        void UpdateFromScreen(Vector2 screen)
        {
            var f = _svc.Layout.WorldToCellF(GroundUnder(screen, _touchDrag)) + _grabOffsetCells; // continuous min-corner
            var next = _cell;
            // hysteresis: switch only when clearly beyond the border of the current cell
            if (Mathf.Abs(f.x - _cell.x) > 0.5f + _hysteresis) next.x = Mathf.RoundToInt(f.x);
            if (Mathf.Abs(f.y - _cell.y) > 0.5f + _hysteresis) next.y = Mathf.RoundToInt(f.y);
            var fp = _def.SafeFootprint;
            next.x = Mathf.Clamp(next.x, -1, _svc.Grid.Width - fp.x + 1);
            next.y = Mathf.Clamp(next.y, -1, _svc.Grid.Height - fp.y + 1);
            if (next != _cell) ApplyCell(next, true);
        }

        public void EndDrag() { _dragging = false; }

        public bool HandleTap(Vector2 screen)
        {
            if (!IsActive) return false;
            var c = _svc.Layout.WorldToCell(_camera.ScreenToGround(screen));
            var fp = _def.SafeFootprint;
            var target = new Vector2Int(c.x - fp.x / 2, c.y - fp.y / 2);
            if (_svc.Grid.InBounds(c.x, c.y)) ApplyCell(target, true);
            return true; // taps never select buildings while placing
        }

        void Update()
        {
            if (!_dragging || _camera == null) return;
            // edge pan: move the camera when the finger is near a screen edge, the ghost follows the finger
            float mx = Screen.width * _edgeMargin, my = Screen.height * _edgeMargin;
            Vector2 dir = Vector2.zero;
            if (_dragScreen.x < mx) dir.x = -(1f - _dragScreen.x / mx);
            else if (_dragScreen.x > Screen.width - mx) dir.x = 1f - (Screen.width - _dragScreen.x) / mx;
            if (_dragScreen.y < my) dir.y = -(1f - _dragScreen.y / my);
            else if (_dragScreen.y > Screen.height - my) dir.y = 1f - (Screen.height - _dragScreen.y) / my;
            if (dir.sqrMagnitude < 1e-4f) return;
            _camera.GroundAxes(out var right, out var fwd);
            float speed = _edgePanSpeed * Mathf.Max(0.5f, _camera.Size / 8f) * Time.unscaledDeltaTime;
            _camera.PanBy((right * dir.x + fwd * dir.y) * speed);
            UpdateFromScreen(_dragScreen);
        }

        // ------------------------------------------------------------------ QA
        public void QA_SetCell(Vector2Int cell) { if (IsActive) ApplyCell(cell, false); }
        public string QA_PurchaseId => _purchaseId;
    }
}