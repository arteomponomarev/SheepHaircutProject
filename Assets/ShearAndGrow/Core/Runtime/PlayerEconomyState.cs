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
    /// PLAYER SAVE STATE of the economy. One file, written atomically after every operation, so every resource
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
        public long totalCoinsEarned;
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
