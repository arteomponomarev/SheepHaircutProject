using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Turns building data into views: spawns each placed instance's visual prefab at its grid position, keeps
    /// instanceId → view, reacts to <see cref="BuildingService"/> events, pools decoration views, updates segment
    /// connections around changed cells and drives the construction stage of the running site.
    /// </summary>
    public sealed class FarmWorldPresenter : MonoBehaviour
    {
        BuildingService _svc;
        Transform _root;
        readonly Dictionary<string, BuildingView> _views = new Dictionary<string, BuildingView>();
        readonly Dictionary<string, Stack<BuildingView>> _pool = new Dictionary<string, Stack<BuildingView>>();
        readonly List<string> _tmp = new List<string>();

        public event Action<BuildingView, FarmInteractable> ViewSpawned, ViewDespawned, ViewMoved;

        public IEnumerable<BuildingView> Views => _views.Values;

        public void Initialise(BuildingService svc, Transform root)
        {
            _svc = svc; _root = root;
            _svc.InstanceAdded += OnAdded;
            _svc.InstanceMoved += OnMoved;
            _svc.InstanceRemoved += OnRemoved;
            _svc.StatusChanged += OnStatus;
            _svc.LevelChanged += OnLevel;
            foreach (var b in _svc.State.instances) if (b.IsPlaced) Spawn(b, false);
            foreach (var b in _svc.State.instances) if (b.IsPlaced) RefreshConnections(b);
        }

        void OnDestroy()
        {
            if (_svc == null) return;
            _svc.InstanceAdded -= OnAdded; _svc.InstanceMoved -= OnMoved; _svc.InstanceRemoved -= OnRemoved; _svc.StatusChanged -= OnStatus;
        }

        public BuildingView ViewOf(string instanceId) => instanceId != null && _views.TryGetValue(instanceId, out var v) ? v : null;
        public FarmInteractable InteractableOf(string instanceId) { var v = ViewOf(instanceId); return v != null ? v.GetComponent<FarmInteractable>() : null; }

        /// <summary>Creates a view of a definition that is NOT bound to an instance (placement ghost).</summary>
        public BuildingView CreateDetached(FarmBuildingDefinition def)
        {
            if (def == null || def.VisualPrefab == null) return null;
            var go = Instantiate(def.VisualPrefab, _root);
            var v = go.GetComponent<BuildingView>();
            if (v == null) v = go.AddComponent<BuildingView>();
            var it = go.GetComponent<FarmInteractable>();
            if (it != null) it.enabled = false;
            v.Bind(null, def);
            v.SetConstruction(4, 1f);
            return v;
        }

        BuildingView Spawn(BuildingInstanceState b, bool pop)
        {
            var def = _svc.Definition(b.typeId);
            if (def == null || def.VisualPrefab == null) { Debug.LogWarning("[Farm] No visual for " + b.typeId); return null; }
            BuildingView v = null;
            if (_pool.TryGetValue(b.typeId, out var stack) && stack.Count > 0) { v = stack.Pop(); v.gameObject.SetActive(true); }
            if (v == null)
            {
                var go = Instantiate(def.VisualPrefab, _root);
                v = go.GetComponent<BuildingView>();
                if (v == null) v = go.AddComponent<BuildingView>();
            }
            v.Bind(b, def);
            v.SetWorldPosition(_svc.WorldCenter(b), false);
            v.SetLevel(b.level);
            ApplyStage(v, b);
            _views[b.instanceId] = v;
            var it = v.GetComponent<FarmInteractable>();
            if (it != null) { it.enabled = true; it.BindInstance(b.instanceId, def.Id, def.DisplayName); }
            if (pop) v.Pop();
            ViewSpawned?.Invoke(v, it);
            return v;
        }

        void Despawn(string instanceId)
        {
            if (!_views.TryGetValue(instanceId, out var v)) return;
            _views.Remove(instanceId);
            var it = v.GetComponent<FarmInteractable>();
            ViewDespawned?.Invoke(v, it);
            if (v.Definition != null && v.Definition.IsDecoration)
            {
                v.gameObject.SetActive(false);
                if (!_pool.TryGetValue(v.TypeId, out var stack)) _pool[v.TypeId] = stack = new Stack<BuildingView>();
                stack.Push(v);
            }
            else Destroy(v.gameObject);
        }

        void ApplyStage(BuildingView v, BuildingInstanceState b)
        {
            if (b.status == BuildingStatus.UnderConstruction)
            {
                int stage = _svc.Config.StageOf(_svc.Progress(b), out float t);
                v.SetConstruction(Mathf.Min(stage, 3), t);
            }
            else v.SetConstruction(4, 1f);
        }

        /// <summary>Advances the visual stage of running constructions (called at the construction tick rate).</summary>
        public void RefreshConstruction()
        {
            foreach (var b in _svc.State.instances)
                if (b.status == BuildingStatus.UnderConstruction && _views.TryGetValue(b.instanceId, out var v)) ApplyStage(v, b);
        }

        public void SetHidden(string instanceId, bool hidden)
        {
            var v = ViewOf(instanceId);
            if (v != null) v.gameObject.SetActive(!hidden);
        }

        void OnAdded(BuildingInstanceState b)
        {
            Spawn(b, true);
            RefreshConnectionsAround(b);
        }

        void OnMoved(BuildingInstanceState b)
        {
            var v = ViewOf(b.instanceId);
            if (v == null) return;
            v.gameObject.SetActive(true);
            v.SetWorldPosition(_svc.WorldCenter(b), false);
            v.Pop();
            var it = v.GetComponent<FarmInteractable>();
            if (it != null) it.RefreshRenderers();
            // neighbours of the old and the new position
            foreach (var other in _svc.State.instances) if (other.IsPlaced && other.typeId == b.typeId) RefreshConnections(other);
            ViewMoved?.Invoke(v, it);
        }

        void OnRemoved(BuildingInstanceState b)
        {
            Despawn(b.instanceId);
            foreach (var other in _svc.State.instances) if (other.IsPlaced && other.typeId == b.typeId) RefreshConnections(other);
        }

        /// <summary>Upgrade finished: swap to the new level's model in place.</summary>
        void OnLevel(BuildingInstanceState b, int level)
        {
            var v = ViewOf(b.instanceId);
            if (v == null) return;
            v.SetLevel(level);
            v.Pop();
            var it = v.GetComponent<FarmInteractable>();
            if (it != null) it.RefreshRenderers();
            LevelSwapped?.Invoke(v, it);
        }

        public event Action<BuildingView, FarmInteractable> LevelSwapped;

        void OnStatus(BuildingInstanceState b)
        {
            var v = ViewOf(b.instanceId);
            if (v == null) return;
            ApplyStage(v, b);
            v.Pop();
        }

        void RefreshConnectionsAround(BuildingInstanceState b)
        {
            var def = _svc.Definition(b.typeId);
            if (def == null || def.Connect == ConnectMode.None) return;
            RefreshConnections(b);
            var g = _svc.Grid;
            Vector2Int c = b.cell;
            _tmp.Clear();
            AddOccupant(g, c.x, c.y + 1); AddOccupant(g, c.x + 1, c.y); AddOccupant(g, c.x, c.y - 1); AddOccupant(g, c.x - 1, c.y);
            foreach (var id in _tmp) { var n = _svc.State.Find(id); if (n != null) RefreshConnections(n); }
        }

        void AddOccupant(FarmGrid g, int x, int y) { var id = g.OccupantAt(x, y); if (id != null) _tmp.Add(id); }

        void RefreshConnections(BuildingInstanceState b)
        {
            var def = _svc.Definition(b.typeId);
            if (def == null || def.Connect == ConnectMode.None) return;
            var v = ViewOf(b.instanceId);
            if (v != null) v.SetConnections(_svc.Grid.NeighbourMask(b.cell, b.typeId));
        }

        /// <summary>Connection mask a ghost would get at <paramref name="cell"/> (preview).</summary>
        public int PreviewMask(FarmBuildingDefinition def, Vector2Int cell) =>
            def != null && def.Connect != ConnectMode.None ? _svc.Grid.NeighbourMask(cell, def.Id) : 0;
    }
}