using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShearAndGrow.Economy;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm.Tests
{
    /// <summary>Stage 5.5: offline regrowth timebase, game clock, developer operations (no duplicate payouts / buildings).</summary>
    public sealed class Stage55Tests
    {
        string _root;
        FarmLayout _layout;
        BuildingCatalog _catalog;
        ConstructionConfig _config;
        FarmProgressionConfig _prog;
        GameBalanceData _balance;
        WoolGrowthConfig _growth;
        FarmBuildingDefinition _house, _barn, _trader, _shed;
        ManualBuildingClock _clock;

        static FarmBuildingDefinition Def(string id, Vector2Int fp, int price, float secs, BuildingInteraction it, bool purchasable = true)
        {
            var d = ScriptableObject.CreateInstance<FarmBuildingDefinition>();
            d.Id = id; d.Category = BuildingCategory.Building; d.Footprint = fp; d.Price = price; d.ConstructionSeconds = secs; d.Interaction = it;
            d.MaxInstances = 1; d.Purchasable = purchasable; d.RequiredFarmerHouseLevel = 1;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sag_s55_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            EconomyStore.RootOverride = _root;
            FarmStateStore.RootOverride = _root;
            WoolGrowthClock.RootOverride = _root;
            SheepWoolStateStore.DirectoryOverride = Path.Combine(_root, "Sheep");
            WoolGrowthClock.ResetForTests();
            GameClock.ResetForTests();
            WoolEconomy.ResetInstanceForTests();
            _balance = ScriptableObject.CreateInstance<GameBalanceData>();
            _balance.StartingCoins = 2000;
            WoolEconomy.Initialise(_balance);

            _house = Def("farmhouse", new Vector2Int(4, 4), 0, 0, BuildingInteraction.FarmerHouse, false); _house.MaxLevel = 8;
            _barn = Def("barn", new Vector2Int(5, 8), 0, 0, BuildingInteraction.Barn, false);
            _trader = Def("trader", new Vector2Int(3, 2), 0, 0, BuildingInteraction.Trader, false);
            _shed = Def("woolshed", new Vector2Int(4, 3), 80, 8f, BuildingInteraction.WoolProcessing);
            _catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            _catalog.Items = new[] { _house, _barn, _trader, _shed };
            _layout = ScriptableObject.CreateInstance<FarmLayout>();
            _layout.SizeCells = new Vector2Int(16, 24);
            _layout.Definitions = _catalog.Items;
            _layout.StartingBuildings = new List<StartingBuilding>
            {
                new StartingBuilding { InstanceId = "start_house", TypeId = "farmhouse", Cell = new Vector2Int(1, 17) },
                new StartingBuilding { InstanceId = "start_barn", TypeId = "barn", Cell = new Vector2Int(10, 13) },
                new StartingBuilding { InstanceId = "start_trader", TypeId = "trader", Cell = new Vector2Int(10, 4) },
            };
            _layout.Sheep = new List<FarmSheepSpawn> { new FarmSheepSpawn { SheepId = "sheep_001", DisplayName = "Daisy" }, new FarmSheepSpawn { SheepId = "sheep_002", DisplayName = "Clover" } };
            _config = ScriptableObject.CreateInstance<ConstructionConfig>();
            _prog = ScriptableObject.CreateInstance<FarmProgressionConfig>();
            _growth = ScriptableObject.CreateInstance<WoolGrowthConfig>();
            _growth.FullRegrowthDuration = 255f; // 1 s per step
            _growth.DebugRegrowthMultiplier = 1f;
            _clock = new ManualBuildingClock(new DateTime(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc).Ticks);
        }

        [TearDown]
        public void TearDown()
        {
            WoolEconomy.ResetInstanceForTests();
            EconomyStore.RootOverride = null;
            FarmStateStore.RootOverride = null;
            WoolGrowthClock.RootOverride = null;
            SheepWoolStateStore.DirectoryOverride = null;
            WoolGrowthClock.ResetForTests();
            GameClock.SetSource(null);
            GameClock.ResetForTests();
            try { Directory.Delete(_root, true); } catch { }
        }

        BuildingService NewService(FarmState loaded = null)
        {
            var s = new BuildingService(_layout, _catalog, _config, WoolEconomy.Instance, _clock, st => { FarmStateStore.Save(st); return true; }, 0, _prog);
            s.Load(loaded);
            return s;
        }

        static ShearMaskAuthority HalfShorn()
        {
            var m = new ShearMaskAuthority(new SheepBodySurface(), FarmSheepView.MaskW, FarmSheepView.MaskH);
            for (int i = 0; i < m.Occupancy.Length / 2; i++) m.Occupancy[i] = 0;
            m.RecomputeCutWeight();
            return m;
        }

        // ---------------------------------------------------------------- offline regrowth timebase

        [Test]
        public void Timebase_GameTimeSave_ConvertedToRealTime_KeepsPendingGrowthAndBoost()
        {
            WoolGrowthClock.SetGameSecondsForTests(1000.0);
            var mask = HalfShorn();
            var s = new SheepWoolState { sheepId = "a", growthClockMode = 0, growthClockStamp = 900.0, feedBoostMultiplier = 2f, feedBoostUntil = 1050.0, maskEncoding = 2, saveSchemaVersion = 3 };
            _growth.TimeMode = WoolGrowthTimeMode.RealTime;
            double now = WoolGrowthClock.Now(WoolGrowthTimeMode.RealTime);
            int steps = SheepWoolLifecycle.CatchUp(s, mask, _growth, now);
            Assert.AreEqual((int)WoolGrowthTimeMode.RealTime, s.growthClockMode);
            Assert.AreEqual(200, steps, "100 s pending, fully boosted ×2 → 200 steps (no instant regrowth, nothing lost)");
            Assert.AreEqual(200, mask.Occupancy[0]);
            Assert.AreEqual(255, mask.Occupancy[mask.Occupancy.Length - 1], "untouched fleece unchanged");
            Assert.AreEqual(50.0, SheepWoolLifecycle.BoostSecondsLeft(s, now), 0.01, "boost window survives the conversion");
        }

        [Test]
        public void Timebase_LegacySave_NoFakeRegrowthFromEpochGap()
        {
            WoolGrowthClock.SetGameSecondsForTests(60.0);
            var mask = HalfShorn();
            var s = new SheepWoolState { sheepId = "a", growthClockStamp = 50.0, feedBoostMultiplier = 1f, maskEncoding = 2, saveSchemaVersion = 3 }; // legacy: mode missing = GameTime
            _growth.TimeMode = WoolGrowthTimeMode.RealTime;
            Assert.AreEqual(10, SheepWoolLifecycle.CatchUp(s, mask, _growth, WoolGrowthClock.Now(WoolGrowthTimeMode.RealTime)));
        }

        [Test]
        public void RealTime_GrowsWhileAway_AndIsChunkInvariant()
        {
            _growth.TimeMode = WoolGrowthTimeMode.RealTime;
            var a = HalfShorn(); var b = HalfShorn();
            var sa = new SheepWoolState { sheepId = "a", growthClockMode = 1, growthClockStamp = 1000.0, feedBoostMultiplier = 1f };
            var sb = new SheepWoolState { sheepId = "b", growthClockMode = 1, growthClockStamp = 1000.0, feedBoostMultiplier = 1f };
            SheepWoolLifecycle.CatchUp(sa, a, _growth, 1120.0);                              // app closed for 2 min
            for (int t = 1010; t <= 1120; t += 10) SheepWoolLifecycle.CatchUp(sb, b, _growth, t); // app open
            CollectionAssert.AreEqual(a.Occupancy, b.Occupancy);
            Assert.AreEqual(120, a.Occupancy[0]);
            Assert.AreEqual(0, SheepWoolLifecycle.CatchUp(sa, a, _growth, 1100.0), "clock moved back: no negative growth");
        }

        [Test]
        public void GameClock_NeverRunsBackwards()
        {
            var src = new TestSource { Now = DateTime.UtcNow.AddDays(10) };
            GameClock.SetSource(src);
            var t1 = GameClock.UtcNow;
            src.Now = src.Now.AddHours(-3);           // device clock turned back
            Assert.AreEqual(t1, GameClock.UtcNow, "time holds instead of running backwards");
            src.Now = src.Now.AddHours(4);
            Assert.Greater(GameClock.UtcNow, t1);
        }

        sealed class TestSource : ITimeSource
        {
            public DateTime Now;
            public string Name => "test";
            public bool IsTrusted => true;
            public DateTime UtcNow => Now;
        }

        // ---------------------------------------------------------------- developer building operations

        [Test]
        public void DevFinish_CompletesOnce_NoSecondCharge_NoDuplicate()
        {
            var s = NewService();
            Assert.IsTrue(s.TryPurchase(_shed, new Vector2Int(1, 8), "shed", out _, out _));
            long coins = WoolEconomy.Instance.Coins;
            Assert.AreEqual(1, s.DevFinishJobs(BuildingStatus.UnderConstruction));
            Assert.AreEqual(0, s.DevFinishJobs(BuildingStatus.UnderConstruction), "nothing left to finish");
            Assert.AreEqual(BuildingStatus.Completed, s.State.FirstOfType("woolshed").status);
            Assert.AreEqual(1, s.CountOf("woolshed"));
            Assert.AreEqual(0, s.ActiveConstructionCount);
            Assert.AreEqual(coins, WoolEconomy.Instance.Coins);
            // restart: same result, no refund, no re-charge
            WoolEconomy.ResetInstanceForTests(); WoolEconomy.Initialise(_balance);
            FarmStateStore.TryLoad(0, out var st);
            var s2 = NewService(st);
            Assert.AreEqual(1, s2.CountOf("woolshed"));
            Assert.AreEqual(BuildingStatus.Completed, s2.State.FirstOfType("woolshed").status);
            Assert.AreEqual(coins, WoolEconomy.Instance.Coins);
            Assert.AreEqual(0, WoolEconomy.Instance.PendingPurchases.Count);
        }

        [Test]
        public void DevAdvance_ShiftsTimer_ThenCompletesThroughTick()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "shed", out var inst, out _);
            s.DevAdvanceJobs(5);
            Assert.AreEqual(3.0, s.RemainingSeconds(inst), 0.01);
            Assert.AreEqual(BuildingStatus.UnderConstruction, inst.status);
            s.DevAdvanceJobs(5);
            Assert.AreEqual(BuildingStatus.Completed, inst.status);
            Assert.AreEqual(1, s.CountOf("woolshed"));
        }

        // ---------------------------------------------------------------- developer wool operations

        void SaveHalfShorn(string id, float lifetimeRemoved, string cycle, bool preserved = false)
        {
            var m = HalfShorn();
            var st = new SheepWoolState
            {
                sheepId = id, cycleId = cycle, woolSurfaceVersion = 1, maskWidth = FarmSheepView.MaskW, maskHeight = FarmSheepView.MaskH, maskEncoding = 2,
                saveSchemaVersion = 3, cutMask = m.Encode(), lifetimeRemovedWool = lifetimeRemoved, hairstylePreserved = preserved, feedBoostMultiplier = 1f,
                growthClockMode = (int)_growth.TimeMode, growthClockStamp = WoolGrowthClock.Now(_growth.TimeMode),
            };
            SheepWoolStateStore.Save(st);
        }

        [Test]
        public void DevRestore_PaysNothing_RegrownWoolPaidOnceWhenSheared()
        {
            SaveHalfShorn("sheep_001", 0.5f, "c1");
            var econ = WoolEconomy.Instance;
            Assert.AreEqual(50, econ.CreditRemovedWool("sheep_001", "c1", 0.5f)); // the real shearing payout
            var sheep = new FarmSheepStates(_growth, 0, 1);
            sheep.Add("sheep_001", "Daisy");
            Assert.IsTrue(sheep.DevRestoreFullWool("sheep_001", out string err), err);
            Assert.AreEqual(1f, sheep.Find("sheep_001").Mask.WoolAmount, 1e-4f);
            Assert.AreEqual(50, econ.Get(WoolType.Dirty), "restoring wool never credits Dirty Wool");
            Assert.IsTrue(SheepWoolStateStore.TryLoad("sheep_001", 0, out var saved));
            Assert.AreEqual(0.5f, saved.lifetimeRemovedWool, 1e-5f, "ledger basis untouched");
            Assert.AreEqual("c1", saved.cycleId, "same wool cycle");
            Assert.AreEqual(0, econ.CreditRemovedWool("sheep_001", "c1", saved.lifetimeRemovedWool), "re-commit without shearing pays 0");
            // the player now shears the whole (restored) fleece: exactly one full fleece more is paid
            Assert.AreEqual(100, econ.CreditRemovedWool("sheep_001", "c1", saved.lifetimeRemovedWool + 1f));
            Assert.AreEqual(150, econ.Get(WoolType.Dirty));
        }

        [Test]
        public void DevSetGrowth_ChangesOnlyCutRegions_RefusesPreserved()
        {
            SaveHalfShorn("sheep_001", 0.5f, "c1");
            SaveHalfShorn("sheep_002", 0.5f, "c2", preserved: true);
            var sheep = new FarmSheepStates(_growth, 0, 1);
            sheep.Add("sheep_001", "Daisy"); sheep.Add("sheep_002", "Clover");
            Assert.IsTrue(sheep.DevSetCutRegionGrowth("sheep_001", 0.25f, out string err), err);
            var o = sheep.Find("sheep_001").Mask.Occupancy;
            Assert.AreEqual(64, o[0]);
            Assert.AreEqual(255, o[o.Length - 1], "never-cut fleece untouched");
            // after a full restore, the remembered cut region can be set again
            Assert.IsTrue(sheep.DevRestoreFullWool("sheep_001", out err), err);
            Assert.IsTrue(sheep.DevSetCutRegionGrowth("sheep_001", 0f, out err), err);
            Assert.AreEqual(0, o[0]); Assert.AreEqual(255, o[o.Length - 1]);
            Assert.IsFalse(sheep.DevRestoreFullWool("sheep_002", out err), "preserved hairstyle is protected");
            Assert.IsFalse(sheep.DevSetCutRegionGrowth("sheep_002", 1f, out err));
        }

        // ---------------------------------------------------------------- developer economy operations

        [Test]
        public void DevEconomy_ClampsAndNeverNegative()
        {
            var e = WoolEconomy.Instance;
            Assert.IsTrue(e.DevSetWool(WoolType.Wet, -5)); Assert.AreEqual(0, e.Get(WoolType.Wet));
            Assert.IsTrue(e.DevAddWoolClamped(WoolType.Clean, int.MaxValue)); Assert.AreEqual(WoolEconomy.DevMaxWool, e.Get(WoolType.Clean));
            Assert.IsTrue(e.DevAddWoolClamped(WoolType.Clean, int.MaxValue)); Assert.AreEqual(WoolEconomy.DevMaxWool, e.Get(WoolType.Clean));
            Assert.IsTrue(e.DevSetCoins(long.MaxValue)); Assert.AreEqual(WoolEconomy.DevMaxCoins, e.Coins);
            Assert.IsTrue(e.DevAddCoinsClamped(long.MaxValue)); Assert.AreEqual(WoolEconomy.DevMaxCoins, e.Coins);
            Assert.IsTrue(e.DevResetCoins()); Assert.AreEqual(2000, e.Coins);
            // persisted atomically
            WoolEconomy.ResetInstanceForTests(); WoolEconomy.Initialise(_balance);
            Assert.AreEqual(2000, WoolEconomy.Instance.Coins);
            Assert.AreEqual(WoolEconomy.DevMaxWool, WoolEconomy.Instance.Get(WoolType.Clean));
        }
    }
}
