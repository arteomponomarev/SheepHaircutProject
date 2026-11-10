using System;
using System.Collections.Generic;

namespace ShearAndGrow.Economy
{
    /// <summary>Payment ledger for one sheep's current wool cycle (cycle changes when the wool regrows).</summary>
    [Serializable]
    public sealed class SheepWoolLedger
    {
        public string sheepId;
        public string cycleId;
        /// <summary>Highest unique cut fraction (0..1) already paid out for this cycle.</summary>
        public float creditedFraction;
        /// <summary>Dirty Wool units already given for this cycle.</summary>
        public int givenUnits;
    }

    /// <summary>
    /// Coins already deducted for a purchase whose result (e.g. a farm building) is not yet confirmed as saved.
    /// Reconciled on startup: confirmed if the purchased thing exists, otherwise refunded.
    /// </summary>
    [Serializable]
    public sealed class ToolLevelEntry
    {
        public string toolId;
        public int level = 1;
    }

    [Serializable]
    public sealed class PendingPurchase
    {
        public string purchaseId;
        public string itemId;
        public long price;
        public string utc;
    }

    /// <summary>
    /// PLAYER SAVE STATE of the economy. One file,written atomically after every operation, so every resource
    /// transfer (credit, processing, sale) is all-or-nothing.
    /// </summary>
    [Serializable]
    public sealed class PlayerEconomyState
    {
        public int schemaVersion;
        public int saveEpoch;
        public long coins;
        public int dirtyWool;
        public int wetWool;
        public int cleanWool;
        public List<SheepWoolLedger> sheepLedger = new List<SheepWoolLedger>();
        /// <summary>Ids of the most recently completed processing batches (idempotency across reloads).</summary>
        public List<string> recentBatchIds = new List<string>();
        /// <summary>Purchases paid but not yet confirmed (two-file atomic purchase, see WoolEconomy.TrySpend).</summary>
        public List<PendingPurchase> pendingPurchases = new List<PendingPurchase>();
        /// <summary>Recently confirmed / refunded purchase ids (idempotency: a known id is never charged again).</summary>
        public List<string> recentPurchaseIds = new List<string>();
        /// <summary>Owned tool upgrade levels (Stage 5). Missing entry = level 1.</summary>
        public List<ToolLevelEntry> toolLevels = new List<ToolLevelEntry>();
        public long totalCoinsEarned;
        public long totalCoinsSpent;
        public int operationCount;
        public string createdUtc;
        public string lastSaveUtc;
        public string lastAppVersion;
        public string platform;

        public int Get(WoolType t) => t == WoolType.Dirty ? dirtyWool : t == WoolType.Wet ? wetWool : cleanWool;

        public void Set(WoolType t, int value)
        {
            if (value < 0) value = 0;
            if (t == WoolType.Dirty) dirtyWool = value;
            else if (t == WoolType.Wet) wetWool = value;
            else cleanWool = value;
        }
    }

    /// <summary>
    /// A reserved amount of wool being processed by a mini-game. Nothing changes in the inventory until
    /// <see cref="WoolEconomy.CompleteBatch"/>; leaving cancels it and the wool stays with the player.
    /// </summary>
    public sealed class WoolProcessingBatch
    {
        public readonly string Id = Guid.NewGuid().ToString("N");
        public WoolType Input;
        public WoolType Output;
        public int Amount;
        public bool Completed { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool IsOpen => !Completed && !Cancelled;
    }

    /// <summary>Which categories to sell.</summary>
    public struct SellSelection
    {
        public bool Dirty, Wet, Clean;
        public static SellSelection All => new SellSelection { Dirty = true, Wet = true, Clean = true };
        public bool Includes(WoolType t) => t == WoolType.Dirty ? Dirty : t == WoolType.Wet ? Wet : Clean;
    }
}
