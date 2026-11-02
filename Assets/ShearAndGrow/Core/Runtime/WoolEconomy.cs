using System;
using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// Single runtime authority over Coins and Dirty / Wet / Clean wool. Scene-independent (lives across scene loads)
    /// so farm buildings can call the same operations later. Every operation validates, mutates and saves the whole
    /// state once, so a transfer is either fully applied or not at all. All calls are main-thread and synchronous;
    /// re-entrant/double calls see the already-updated state and do nothing.
    /// </summary>
    public sealed class WoolEconomy
    {
        const int MaxRecentBatches = 32;

        static WoolEconomy _instance;
        public static WoolEconomy Instance => _instance;
        public static bool IsReady => _instance != null;

        readonly GameBalanceData _balance;
        PlayerEconomyState _state;
        WoolProcessingBatch _openBatch;

        public event Action Changed;

        public GameBalanceData Balance => _balance;
        public long Coins => _state.coins;
        public int Get(WoolType t) => _state.Get(t);
        /// <summary>Amount available for new operations (excludes wool reserved by an open batch).</summary>
        public int Available(WoolType t) => _state.Get(t) - (_openBatch != null && _openBatch.IsOpen && _openBatch.Input == t ? _openBatch.Amount : 0);
        public int TotalWool => _state.dirtyWool + _state.wetWool + _state.cleanWool;
        public WoolProcessingBatch OpenBatch => _openBatch != null && _openBatch.IsOpen ? _openBatch : null;
        public string LoadInfo { get; private set; }
        public PlayerEconomyState DebugState => _state;

        WoolEconomy(GameBalanceData balance)
        {
            _balance = balance;
            if (EconomyStore.TryLoad(balance.EconomySaveEpoch, out var loaded)) _state = loaded;
            else _state = CreateDefault(balance);
            LoadInfo = EconomyStore.LastLoadInfo;
        }

        /// <summary>Creates the service once (idempotent). Called by <see cref="EconomyBootstrap"/> in every scene.</summary>
        public static WoolEconomy Initialise(GameBalanceData balance)
        {
            if (_instance == null)
            {
                if (balance == null) throw new ArgumentNullException(nameof(balance));
                _instance = new WoolEconomy(balance);
            }
            return _instance;
        }

        /// <summary>Tests / dev reset: drops the in-memory instance so the next Initialise reloads from disk.</summary>
        public static void ResetInstanceForTests() => _instance = null;

        static PlayerEconomyState CreateDefault(GameBalanceData b) => new PlayerEconomyState
        {
            schemaVersion = EconomyStore.CurrentSchema,
            saveEpoch = b.EconomySaveEpoch,
            coins = b.StartingCoins,
            dirtyWool = b.StartingDirtyWool,
            wetWool = b.StartingWetWool,
            cleanWool = b.StartingCleanWool,
            createdUtc = DateTime.UtcNow.ToString("o"),
        };

        void Commit()
        {
            _state.operationCount++;
            try { EconomyStore.Save(_state); }
            catch (Exception e) { Debug.LogError("[Economy] Save failed: " + e.Message); }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ shearing

        /// <summary>
        /// Pays Dirty Wool for newly sheared, never-paid wool of a sheep's current cycle. The ledger stored in the
        /// economy file is the single source of truth: units = floor(fraction × WoolPerFullSheep) − already given.
        /// Returns the units added (0 if nothing new). Safe to call any number of times.
        /// </summary>
        public int CreditShearing(string sheepId, string cycleId, float uniqueCutFraction)
        {
            if (string.IsNullOrEmpty(sheepId) || string.IsNullOrEmpty(cycleId)) return 0;
            uniqueCutFraction = Mathf.Clamp01(uniqueCutFraction);
            var entry = _state.sheepLedger.Find(l => l.sheepId == sheepId);
            if (entry == null)
            {
                entry = new SheepWoolLedger { sheepId = sheepId, cycleId = cycleId };
                _state.sheepLedger.Add(entry);
            }
            else if (entry.cycleId != cycleId)
            {
                // wool regrew: a new cycle starts from zero (the previous cycle was paid when it was committed)
                entry.cycleId = cycleId;
                entry.creditedFraction = 0f;
                entry.givenUnits = 0;
            }
            if (uniqueCutFraction <= entry.creditedFraction) return 0;
            entry.creditedFraction = uniqueCutFraction;
            int totalUnits = Mathf.FloorToInt(uniqueCutFraction * _balance.WoolPerFullSheep + 1e-4f);
            int add = Mathf.Max(0, totalUnits - entry.givenUnits);
            entry.givenUnits += add;
            _state.dirtyWool += add;
            UnseenShearedWool += add;
            Commit();
            return add;
        }

        /// <summary>Dirty Wool earned since the Hub last showed it (UI notice only, not persisted).</summary>
        public int UnseenShearedWool { get; private set; }
        public int ConsumeUnseenShearedWool() { int v = UnseenShearedWool; UnseenShearedWool = 0; return v; }

        /// <summary>Units already paid for a sheep's current cycle (UI / tests).</summary>
        public int GivenUnits(string sheepId, string cycleId)
        {
            var e = _state.sheepLedger.Find(l => l.sheepId == sheepId);
            return e != null && e.cycleId == cycleId ? e.givenUnits : 0;
        }

        // ------------------------------------------------------------------ processing

        public static WoolType OutputOf(WoolType input) => input == WoolType.Dirty ? WoolType.Wet : WoolType.Clean;

        /// <summary>Reserves the whole available amount of <paramref name="input"/> (Dirty→Wet or Wet→Clean).</summary>
        public bool TryBeginBatch(WoolType input, out WoolProcessingBatch batch) => TryBeginBatch(input, int.MaxValue, out batch);

        /// <summary>Reserves up to <paramref name="maxAmount"/> units (future: partial batches).</summary>
        public bool TryBeginBatch(WoolType input, int maxAmount, out WoolProcessingBatch batch)
        {
            batch = null;
            if (input == WoolType.Clean) return false;
            if (OpenBatch != null) CancelBatch(OpenBatch); // only one mini-game runs at a time
            int amount = Mathf.Min(maxAmount, Available(input));
            if (amount <= 0) return false;
            batch = new WoolProcessingBatch { Input = input, Output = OutputOf(input), Amount = amount };
            _openBatch = batch;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Atomically converts the batch. Idempotent: returns false if it was already completed or cancelled.</summary>
        public bool CompleteBatch(WoolProcessingBatch batch)
        {
            if (batch == null || !batch.IsOpen || batch != _openBatch) return false;
            if (_state.recentBatchIds.Contains(batch.Id)) { batch.Completed = true; return false; }
            int amount = Mathf.Min(batch.Amount, _state.Get(batch.Input));
            if (amount <= 0) { CancelBatch(batch); return false; }
            _state.Set(batch.Input, _state.Get(batch.Input) - amount);
            _state.Set(batch.Output, _state.Get(batch.Output) + amount);
            _state.recentBatchIds.Add(batch.Id);
            if (_state.recentBatchIds.Count > MaxRecentBatches) _state.recentBatchIds.RemoveAt(0);
            batch.Amount = amount;
            batch.Completed = true;
            _openBatch = null;
            Commit();
            return true;
        }

        /// <summary>Releases a reservation; the wool stays unchanged.</summary>
        public void CancelBatch(WoolProcessingBatch batch)
        {
            if (batch == null || !batch.IsOpen) return;
            batch.Cancelled = true;
            if (_openBatch == batch) _openBatch = null;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ selling

        public long QuoteSale(SellSelection sel)
        {
            long total = 0;
            for (int i = 0; i < 3; i++)
            {
                var t = (WoolType)i;
                if (sel.Includes(t)) total += _balance.SellValue(t, Available(t));
            }
            return total;
        }

        /// <summary>Sells the selected categories completely; returns coins earned (0 if nothing to sell).</summary>
        public long Sell(SellSelection sel)
        {
            long earned = 0;
            bool any = false;
            for (int i = 0; i < 3; i++)
            {
                var t = (WoolType)i;
                if (!sel.Includes(t)) continue;
                int amount = Available(t);
                if (amount <= 0) continue;
                earned += _balance.SellValue(t, amount);
                _state.Set(t, _state.Get(t) - amount);
                any = true;
            }
            if (!any) return 0;
            _state.coins += earned;
            _state.totalCoinsEarned += earned;
            Commit();
            return earned;
        }

        // ------------------------------------------------------------------ development

        /// <summary>Development builds only: adds wool for testing.</summary>
        public void DevAdd(WoolType t, int amount)
        {
            if (!Debug.isDebugBuild || amount <= 0) return;
            _state.Set(t, _state.Get(t) + amount);
            Commit();
        }

        /// <summary>Development builds only: wipes the economy back to the default game state.</summary>
        public void DevReset()
        {
            if (!Debug.isDebugBuild) return;
            EconomyStore.DeleteAll();
            _openBatch = null;
            _state = CreateDefault(_balance);
            Changed?.Invoke();
        }
    }
}
