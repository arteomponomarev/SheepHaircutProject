using System;
using System.Collections.Generic;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    public enum HerdResult { Ok = 0, BarnFull, NotEnoughCoins, NoBarn, Busy, SaveFailed }

    /// <summary>
    /// The player's flock. Membership (ids, names) lives in farm.json (<see cref="FarmState.herd"/>); each sheep's wool
    /// lives in its own sheep save (created on first shearing, so a new sheep starts from the default full fleece).
    /// Capacity is derived from the barn level (never stored). Purchases use the economy receipt flow.
    /// </summary>
    public sealed class HerdService
    {
        readonly BuildingService _svc;
        readonly FarmProgressionConfig _cfg;
        bool _busy;

        public event Action<HerdEntry> SheepAdded;

        public HerdService(BuildingService svc, FarmProgressionConfig cfg) { _svc = svc; _cfg = cfg; }

        public IReadOnlyList<HerdEntry> Sheep => _svc.State.herd;
        public int Count => _svc.State.herd.Count;

        public int BarnLevel
        {
            get { var b = _svc.FirstWithInteraction(BuildingInteraction.Barn); return b != null ? Mathf.Max(1, b.level) : 0; }
        }

        public int Capacity => BarnLevel > 0 ? (_cfg != null ? _cfg.BarnCapacity(BarnLevel) : 2) : 0;
        public int Price => _cfg != null ? _cfg.SheepPrice : 200;

        public HerdEntry Find(string sheepId) => _svc.State.herd.Find(h => h.sheepId == sheepId);

        public HerdResult CanBuy()
        {
            if (BarnLevel <= 0) return HerdResult.NoBarn;
            if (Count >= Capacity) return HerdResult.BarnFull;
            if (_svc.Economy != null && !_svc.Economy.CanAfford(Price)) return HerdResult.NotEnoughCoins;
            return HerdResult.Ok;
        }

        /// <summary>
        /// Unique, permanent id: "sheep_NNN" from a counter that only increases (saved with the herd entry), skipping
        /// ids already in the flock or with an existing wool save (stale files after a dev reset are never reused).
        /// </summary>
        string NextSheepId()
        {
            int n = Mathf.Max(1, _svc.State.nextSheepNumber);
            while (true)
            {
                string id = "sheep_" + n.ToString("000");
                if (Find(id) == null && !SheepWoolStateStore.Exists(id)) { _svc.State.nextSheepNumber = n + 1; return id; }
                n++;
            }
        }

        string NextName()
        {
            var names = _cfg != null && _cfg.SheepNames != null && _cfg.SheepNames.Length > 0 ? _cfg.SheepNames : new[] { "Sheep" };
            for (int i = 0; i < names.Length; i++)
            {
                string candidate = names[(Count - 2 + i + names.Length * 4) % names.Length];
                if (!_svc.State.herd.Exists(h => h.displayName == candidate)) return candidate;
            }
            return names[0] + " " + Count;
        }

        /// <summary>Atomic: spend (receipt) → herd entry + counter (farm save) → confirm. Repeated id = no-op.</summary>
        public HerdResult TryBuySheep(string purchaseId, out HerdEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(purchaseId)) return HerdResult.Busy;
            var existing = _svc.State.herd.Find(h => h.purchaseId == purchaseId);
            if (existing != null) { entry = existing; _svc.Economy?.ConfirmPurchase(purchaseId); return HerdResult.Ok; }
            var econ = _svc.Economy;
            if (econ != null && econ.IsPurchaseKnown(purchaseId) && !econ.IsPurchasePending(purchaseId)) return HerdResult.Busy;
            if (_busy) return HerdResult.Busy;
            var r = CanBuy();
            if (r != HerdResult.Ok) return r;
            _busy = true;
            try
            {
                int prevCounter = _svc.State.nextSheepNumber;
                string id = NextSheepId();
                if (econ != null && Price > 0 && !econ.TrySpend(purchaseId, Price, BuildingService.SheepItemPrefix + id)) { _svc.State.nextSheepNumber = prevCounter; return HerdResult.NotEnoughCoins; }
                entry = new HerdEntry { sheepId = id, displayName = NextName(), purchaseId = purchaseId, acquiredUtc = DateTime.UtcNow.ToString("o") };
                _svc.State.herd.Add(entry);
                if (!_svc.SaveState())
                {
                    _svc.State.herd.Remove(entry);
                    _svc.State.nextSheepNumber = prevCounter;
                    if (econ != null && Price > 0) econ.RefundPurchase(purchaseId);
                    entry = null;
                    return HerdResult.SaveFailed;
                }
                if (econ != null && Price > 0) econ.ConfirmPurchase(purchaseId);
            }
            finally { _busy = false; }
            SheepAdded?.Invoke(entry);
            _svc.RaiseChanged();
            return HerdResult.Ok;
        }
    }
}