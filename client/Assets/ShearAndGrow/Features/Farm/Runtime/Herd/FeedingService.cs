using System.Collections.Generic;
using ShearAndGrow.Economy;

namespace ShearAndGrow.Farm
{
    public enum FeedResult { Ok = 0, NoStation, Preserved, FullFleece, AlreadyBoosted, NotEnoughCoins, UnknownSheep, Busy, SaveFailed }

    /// <summary>
    /// Feeding Station: a paid, temporary regrowth multiplier for one sheep. Optional (sheep never need food).
    /// The boost is stored in the sheep's own save and applied by <c>SheepWoolLifecycle.CatchUp</c>; preserved
    /// hairstyles are refused before any coins move. Payment uses the economy receipt flow
    /// (spend → apply + save sheep → confirm; reconcile on start: applied ⇔ confirmed, otherwise refunded).
    /// </summary>
    public sealed class FeedingService
    {
        readonly BuildingService _svc;
        readonly FarmSheepStates _sheep;
        readonly FarmProgressionConfig _cfg;
        bool _busy;

        public FeedingService(BuildingService svc, FarmSheepStates sheep, FarmProgressionConfig cfg) { _svc = svc; _sheep = sheep; _cfg = cfg; }

        public int Price => _cfg != null ? _cfg.FeedPrice : 20;
        public float Multiplier => _cfg != null ? _cfg.FeedBoostMultiplier : 2f;
        public float Seconds => _cfg != null ? _cfg.FeedBoostSeconds : 180f;

        public FeedResult CanFeed(string sheepId)
        {
            if (!_svc.IsFunctional(BuildingInteraction.FeedingStation)) return FeedResult.NoStation;
            var e = _sheep.Find(sheepId);
            if (e == null) return FeedResult.UnknownSheep;
            if (e.State == null || e.Mask == null || e.Mask.WoolAmount >= 0.995f) return FeedResult.FullFleece;
            if (e.State.hairstylePreserved) return FeedResult.Preserved;
            if (_sheep.BoostSecondsLeft(sheepId) > 0.0) return FeedResult.AlreadyBoosted;
            if (_svc.Economy != null && !_svc.Economy.CanAfford(Price)) return FeedResult.NotEnoughCoins;
            return FeedResult.Ok;
        }

        public FeedResult TryFeed(string sheepId, string purchaseId)
        {
            if (string.IsNullOrEmpty(purchaseId) || _busy) return FeedResult.Busy;
            if (_sheep.HasFeedPurchase(sheepId, purchaseId)) { _svc.Economy?.ConfirmPurchase(purchaseId); return FeedResult.Ok; }
            var r = CanFeed(sheepId);
            if (r != FeedResult.Ok) return r;
            var econ = _svc.Economy;
            _busy = true;
            try
            {
                if (econ != null && Price > 0 && !econ.TrySpend(purchaseId, Price, BuildingService.FeedItemPrefix + sheepId)) return FeedResult.NotEnoughCoins;
                if (!_sheep.ApplyFeedBoost(sheepId, Multiplier, Seconds, purchaseId))
                {
                    if (econ != null && Price > 0) econ.RefundPurchase(purchaseId);
                    return FeedResult.SaveFailed;
                }
                if (econ != null && Price > 0) econ.ConfirmPurchase(purchaseId);
            }
            finally { _busy = false; }
            return FeedResult.Ok;
        }

        /// <summary>Closes feeding receipts left open by a crash: confirmed if the sheep holds the boost, else refunded.</summary>
        public void Reconcile(WoolEconomy econ)
        {
            if (econ == null) return;
            foreach (var p in new List<PendingPurchase>(econ.PendingPurchases))
            {
                if (p.itemId == null || !p.itemId.StartsWith(BuildingService.FeedItemPrefix)) continue;
                string sheepId = p.itemId.Substring(BuildingService.FeedItemPrefix.Length);
                if (_sheep.HasFeedPurchase(sheepId, p.purchaseId)) econ.ConfirmPurchase(p.purchaseId);
                else econ.RefundPurchase(p.purchaseId);
            }
        }
    }
}