using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShearAndGrow.Economy;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm.Tests
{
    public sealed class ProgressionTests
    {
        string _root;
        FarmLayout _layout;
        BuildingCatalog _catalog;
        ConstructionConfig _config;
        FarmProgressionConfig _prog;
        GameBalanceData _balance;
        WoolGrowthConfig _growth;
        FarmBuildingDefinition _house, _barn, _trader, _shed, _workshop, _station;
        ManualBuildingClock _clock;
        bool _failFarmSave;

        static FarmBuildingDefinition Def(string id, Vector2Int fp, int price, float secs, BuildingInteraction it, int houseLevel = 1, bool purchasable = true)
        {
            var d = ScriptableObject.CreateInstance<FarmBuildingDefinition>();
            d.Id = id; d.Category = BuildingCategory.Building; d.Footprint = fp; d.Price = price; d.ConstructionSeconds = secs; d.Interaction = it;
            d.MaxInstances = 1; d.Purchasable = purchasable; d.RequiredFarmerHouseLevel = houseLevel;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sag_prog_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            EconomyStore.RootOverride = _root;
            FarmStateStore.RootOverride = _root;
            SheepWoolStateStore.DirectoryOverride = Path.Combine(_root, "Sheep");
            WoolEconomy.ResetInstanceForTests();
            _balance = ScriptableObject.CreateInstance<GameBalanceData>();
            _balance.StartingCoins = 2000;
            WoolEconomy.Initialise(_balance);

            _house = Def("farmhouse", new Vector2Int(4, 4), 0, 0, BuildingInteraction.FarmerHouse, purchasable: false);
            _house.MaxLevel = 8;
            _barn = Def("barn", new Vector2Int(5, 8), 0, 0, BuildingInteraction.Barn, purchasable: false);
            _barn.MaxLevel = 2;
            _barn.UpgradeLevels = new[] { new BuildingLevel { Level = 2, RequiredFarmerHouseLevel = 2, Cost = 250, Seconds = 15f, Benefit = "4 sheep" } };
            _trader = Def("trader", new Vector2Int(3, 2), 0, 0, BuildingInteraction.Trader, purchasable: false);
            _shed = Def("woolshed", new Vector2Int(4, 3), 80, 8f, BuildingInteraction.WoolProcessing);
            _workshop = Def("trimmer_workshop", new Vector2Int(3, 3), 180, 12f, BuildingInteraction.TrimmerWorkshop, houseLevel: 2);
            _station = Def("feeding_station", new Vector2Int(3, 2), 120, 10f, BuildingInteraction.FeedingStation, houseLevel: 2);
            _catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            _catalog.Items = new[] { _house, _barn, _trader, _shed, _workshop, _station };
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
            _clock = new ManualBuildingClock(new System.DateTime(2026, 7, 11, 12, 0, 0, System.DateTimeKind.Utc).Ticks);
            _failFarmSave = false;
        }

        [TearDown]
        public void TearDown()
        {
            WoolEconomy.ResetInstanceForTests();
            EconomyStore.RootOverride = null;
            FarmStateStore.RootOverride = null;
            SheepWoolStateStore.DirectoryOverride = null;
            try { Directory.Delete(_root, true); } catch { }
        }

        BuildingService NewService(FarmState loaded = null)
        {
            var s = new BuildingService(_layout, _catalog, _config, WoolEconomy.Instance, _clock,
                st => { if (_failFarmSave) return false; FarmStateStore.Save(st); return true; }, 0, _prog);
            s.Load(loaded);
            return s;
        }

        BuildingService Reload()
        {
            WoolEconomy.ResetInstanceForTests();
            WoolEconomy.Initialise(_balance);
            FarmStateStore.TryLoad(0, out var st);
            return NewService(st);
        }

        static WoolEconomy Econ => WoolEconomy.Instance;

        BuildingService WithShed()
        {
            var s = NewService();
            Assert.IsTrue(s.TryPurchase(_shed, new Vector2Int(1, 8), "shed", out _, out _));
            _clock.Advance(9); s.Tick();
            return s;
        }

        BuildingService HouseLv2()
        {
            var s = WithShed();
            Assert.IsTrue(s.TryStartUpgrade("start_house", "house2", out var r), r.ToString());
            _clock.Advance(21); s.Tick();
            Assert.AreEqual(2, s.FarmerHouseLevel);
            return s;
        }

        // ---------------------------------------------------------------- house progression

        [Test]
        public void House_RequiresProcessingBuilding()
        {
            var s = NewService();
            var info = s.GetUpgrade(s.State.Find("start_house"));
            Assert.AreEqual(PlacementResult.RequirementsNotMet, info.Result);
            Assert.AreEqual("woolshed", info.MissingBuildings[0].Id);
            Assert.AreEqual(300, info.Cost);
        }

        [Test]
        public void House_ShedUnderConstruction_StillMissing()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "shed", out _, out _);
            Assert.AreEqual(PlacementResult.RequirementsNotMet, s.CheckUpgrade(s.State.Find("start_house")));
        }

        [Test]
        public void House_NotEnoughCoins()
        {
            _balance.StartingCoins = 380; WoolEconomy.ResetInstanceForTests(); WoolEconomy.Initialise(_balance);
            var s = WithShed(); // 380 - 80 = 300 → exactly enough
            Assert.AreEqual(PlacementResult.Ok, s.CheckUpgrade(s.State.Find("start_house")));
            Econ.TrySpend("x", 1, "test"); Econ.ConfirmPurchase("x");
            Assert.AreEqual(PlacementResult.NotEnoughCoins, s.CheckUpgrade(s.State.Find("start_house")));
        }

        [Test]
        public void House_Upgrade_ChargesOnce_TimerThenLevel()
        {
            var s = WithShed();
            long before = Econ.Coins;
            int levelEvents = 0; s.LevelChanged += (b, l) => levelEvents++;
            Assert.IsTrue(s.TryStartUpgrade("start_house", "p", out _));
            Assert.IsTrue(s.TryStartUpgrade("start_house", "p", out _)); // double tap
            Assert.IsFalse(s.TryStartUpgrade("start_house", "p2", out var r2));
            Assert.AreEqual(PlacementResult.UnderConstruction, r2);
            Assert.AreEqual(before - 300, Econ.Coins);
            var h = s.State.Find("start_house");
            Assert.AreEqual(BuildingStatus.Upgrading, h.status);
            Assert.AreEqual(1, s.FarmerHouseLevel);
_clock.Advance(10); Assert.AreEqual(0, s.Tick());
            _clock.Advance(11); Assert.AreEqual(1, s.Tick()); Assert.AreEqual(0, s.Tick());
            Assert.AreEqual(2, s.FarmerHouseLevel);
            Assert.AreEqual(1, levelEvents);
            Assert.AreEqual(PlacementResult.Ok, s.CheckPurchaseRules(_workshop), "workshop unlocked at Lv 2");
        }

        [Test]
        public void House_UpgradeBlocksOtherConstruction()
        {
            var s = HouseLv2();
            Assert.IsTrue(s.TryPurchase(_workshop, new Vector2Int(1, 1), "w", out _, out _));
            Assert.AreEqual(PlacementResult.ConstructionBusy, s.CheckUpgrade(s.State.Find("start_barn")));
        }

        [Test]
        public void Upgrade_PersistsAcrossRestart_AndOfflineCompletes()
        {
            var s = WithShed();
            s.TryStartUpgrade("start_house", "p", out _);
            long coins = Econ.Coins;
            s = Reload();
            Assert.AreEqual(BuildingStatus.Upgrading, s.State.Find("start_house").status);
            Assert.AreEqual(coins, Econ.Coins);
            _clock.Advance(3600);
            s = Reload();
            Assert.AreEqual(2, s.FarmerHouseLevel);
            Assert.AreEqual(coins, Econ.Coins, "no refund, no second charge");
            s = Reload();
            Assert.AreEqual(2, s.FarmerHouseLevel);
        }

        [Test]
        public void Upgrade_SaveFails_Refunds()
        {
            var s = WithShed();
            long coins = Econ.Coins;
            _failFarmSave = true;
            Assert.IsFalse(s.TryStartUpgrade("start_house", "p", out _));
            Assert.AreEqual(coins, Econ.Coins);
            Assert.AreEqual(BuildingStatus.Completed, s.State.Find("start_house").status);
        }

        [Test]
        public void Upgrade_CrashBeforeFarmSave_RefundedOnLoad()
        {
            var s = WithShed();
            long coins = Econ.Coins;
            Assert.IsTrue(Econ.TrySpend("crash", 300, BuildingService.UpgradeItemPrefix + "start_house"));
            s = Reload();
            Assert.AreEqual(coins, Econ.Coins);
            Assert.AreEqual(1, s.FarmerHouseLevel);
        }

        [Test]
        public void Barn_UpgradeNeedsHouse2_FunctionalWhileUpgrading()
        {
            var s = WithShed();
            Assert.AreEqual(PlacementResult.Locked, s.CheckUpgrade(s.State.Find("start_barn")));
            _clock.Advance(1);
            s.TryStartUpgrade("start_house", "h", out _); _clock.Advance(21); s.Tick();
            Assert.IsTrue(s.TryStartUpgrade("start_barn", "b", out _));
            Assert.IsTrue(s.IsFunctional(BuildingInteraction.Barn), "barn keeps working (shearing access) while upgrading");
            Assert.AreEqual(PlacementResult.UnderConstruction, s.CheckMove(s.State.Find("start_barn"), new Vector2Int(0, 0)));
            _clock.Advance(16); s.Tick();
            Assert.AreEqual(2, s.State.Find("start_barn").level);
            Assert.AreEqual(PlacementResult.MaxLevel, s.CheckUpgrade(s.State.Find("start_barn")));
            Assert.AreEqual(new Vector2Int(10, 13), s.State.Find("start_barn").cell, "upgrade never moves the building");
        }

        // ---------------------------------------------------------------- herd

        [Test]
        public void Herd_StartsWithTwo_CapacityByBarnLevel()
        {
            var s = NewService();
            var herd = new HerdService(s, _prog);
            Assert.AreEqual(2, herd.Count);
            Assert.AreEqual(2, herd.Capacity);
            Assert.AreEqual(HerdResult.BarnFull, herd.CanBuy());
            Assert.AreEqual(HerdResult.BarnFull, herd.TryBuySheep("x", out _));
        }

        [Test]
        public void Herd_BuyAfterBarn2_UniqueIds_Persist()
        {
            var s = HouseLv2();
            s.TryStartUpgrade("start_barn", "b", out _); _clock.Advance(16); s.Tick();
            var herd = new HerdService(s, _prog);
            Assert.AreEqual(4, herd.Capacity);
            long coins = Econ.Coins;
            Assert.AreEqual(HerdResult.Ok, herd.TryBuySheep("s1", out var a));
            Assert.AreEqual(HerdResult.Ok, herd.TryBuySheep("s1", out var a2)); // double tap
            Assert.AreSame(a, a2);
            Assert.AreEqual(coins - 200, Econ.Coins);
            Assert.AreEqual("sheep_003", a.sheepId);
            // a stale wool file for sheep_004 (e.g. from an old install) is never reused
            Directory.CreateDirectory(SheepWoolStateStore.Directory);
            File.WriteAllText(Path.Combine(SheepWoolStateStore.Directory, "sheep_004.json"), "{}");
            Assert.AreEqual(HerdResult.Ok, herd.TryBuySheep("s2", out var b));
            Assert.AreEqual("sheep_005", b.sheepId);
            Assert.AreEqual(HerdResult.BarnFull, herd.TryBuySheep("s3", out _));
            Assert.AreEqual(coins - 400, Econ.Coins);
            Assert.IsFalse(SheepWoolStateStore.Exists("sheep_003"), "new sheep has no cloned wool save → full fleece");
            s = Reload();
            herd = new HerdService(s, _prog);
            Assert.AreEqual(4, herd.Count);
            Assert.AreEqual(6, s.State.nextSheepNumber);
        }

        [Test]
        public void Herd_CrashBeforeFarmSave_Refunded()
        {
            var s = HouseLv2();
            long coins = Econ.Coins;
            Assert.IsTrue(Econ.TrySpend("crash", 200, BuildingService.SheepItemPrefix + "sheep_003"));
            s = Reload();
            Assert.AreEqual(coins, Econ.Coins);
            Assert.AreEqual(2, s.State.herd.Count);
        }

        [Test]
        public void Migration_Schema2_SeedsHerd_KeepsBuildings()
        {
            var s = WithShed();
            var st = s.State;
            st.herd.Clear(); st.nextSheepNumber = 0;
            st.schemaVersion = 2;
            File.WriteAllText(FarmStateStore.FilePath, JsonUtility.ToJson(st).Replace("\"schemaVersion\":3", "\"schemaVersion\":2"));
            Assert.IsTrue(FarmStateStore.TryLoad(0, out var loaded));
            Assert.AreEqual(3, loaded.schemaVersion);
            var s2 = NewService(loaded);
            Assert.AreEqual(2, s2.State.herd.Count);
            Assert.AreEqual("sheep_001", s2.State.herd[0].sheepId);
            Assert.AreEqual(3, s2.State.nextSheepNumber);
            Assert.AreEqual(1, s2.CountOf("woolshed"));
            Assert.AreEqual(BuildingStatus.Completed, s2.State.FirstOfType("woolshed").status);
        }

        // ---------------------------------------------------------------- feeding (lifecycle)

        static SheepWoolState PartlyShorn(out ShearMaskAuthority mask)
        {
            mask = new ShearMaskAuthority(new SheepBodySurface(), 64, 32);
            for (int i = 0; i < mask.Occupancy.Length / 2; i++) mask.Occupancy[i] = 0;
            mask.RecomputeCutWeight();
            return new SheepWoolState { sheepId = "sheep_001", growthClockStamp = 100.0, feedBoostMultiplier = 1f, maskEncoding = 2, saveSchemaVersion = 3 };
        }

        [Test]
        public void Boost_DoublesGrowth_AndIsChunkInvariant()
        {
            var a = PartlyShorn(out var ma);
            var b = PartlyShorn(out var mb);
            var c = PartlyShorn(out var mc);
            SheepWoolLifecycle.ApplyFeedBoost(b, mb, _growth, 100.0, 2f, 30f, "f");
            SheepWoolLifecycle.ApplyFeedBoost(c, mc, _growth, 100.0, 2f, 30f, "f");
            SheepWoolLifecycle.CatchUp(a, ma, _growth, 120.0);            // 20 steps
            SheepWoolLifecycle.CatchUp(b, mb, _growth, 120.0);            // 40 steps
            for (int t = 101; t <= 120; t++) SheepWoolLifecycle.CatchUp(c, mc, _growth, t);
            Assert.AreEqual(20, ma.Occupancy[0]);
            Assert.AreEqual(40, mb.Occupancy[0]);
            CollectionAssert.AreEqual(mb.Occupancy, mc.Occupancy, "chunked == single");
            SheepWoolLifecycle.CatchUp(b, mb, _growth, 150.0);            // 10 boosted (×2) + 20 normal = 40 more
            Assert.AreEqual(80, mb.Occupancy[0]);
        }

        [Test]
        public void Boost_RefusedForPreserved_NoChange()
        {
            var s = PartlyShorn(out var m);
            s.hairstylePreserved = true;
            var before = (byte[])m.Occupancy.Clone();
            Assert.IsFalse(SheepWoolLifecycle.ApplyFeedBoost(s, m, _growth, 100.0, 2f, 30f, "f"));
            SheepWoolLifecycle.CatchUp(s, m, _growth, 200.0);
            CollectionAssert.AreEqual(before, m.Occupancy);
            Assert.AreEqual(1f, s.feedBoostMultiplier);
        }

        [Test]
        public void Feeding_Service_ChargesOnce_RefusesPreservedWithoutCharge()
        {
            var s = HouseLv2();
            Assert.IsTrue(s.TryPurchase(_station, new Vector2Int(1, 1), "st", out _, out _));
            _clock.Advance(11); s.Tick();
            // two partly shorn sheep saves
            foreach (var id in new[] { "sheep_001", "sheep_002" })
            {
                var st = PartlyShorn(out var m); st.sheepId = id; st.cutMask = m.Encode();
                st.woolSurfaceVersion = 1; st.maskWidth = FarmSheepView.MaskW; st.maskHeight = FarmSheepView.MaskH;
                var mm = new ShearMaskAuthority(new SheepBodySurface(), FarmSheepView.MaskW, FarmSheepView.MaskH);
                for (int i = 0; i < mm.Occupancy.Length / 2; i++) mm.Occupancy[i] = 0;
                mm.RecomputeCutWeight();
                st.cutMask = mm.Encode(); st.growthClockStamp = ShearAndGrow.Economy.WoolGrowthClock.Now(_growth.TimeMode);
                if (id == "sheep_002") st.hairstylePreserved = true;
                SheepWoolStateStore.Save(st);
            }
            var sheep = new FarmSheepStates(_growth, 0, 1);
            sheep.Add("sheep_001", "Daisy"); sheep.Add("sheep_002", "Clover");
            var feed = new FeedingService(s, sheep, _prog);
            long coins = Econ.Coins;
            Assert.AreEqual(FeedResult.Preserved, feed.TryFeed("sheep_002", "f0"));
            Assert.AreEqual(coins, Econ.Coins, "no charge for a preserved sheep");
            Assert.AreEqual(FeedResult.Ok, feed.TryFeed("sheep_001", "f1"));
            Assert.AreEqual(FeedResult.Ok, feed.TryFeed("sheep_001", "f1")); // double tap
            Assert.AreEqual(coins - 20, Econ.Coins);
            Assert.AreEqual(FeedResult.AlreadyBoosted, feed.CanFeed("sheep_001"));
            Assert.IsTrue(SheepWoolStateStore.TryLoad("sheep_001", 0, out var saved) && saved.feedBoostMultiplier == 2f && saved.feedPurchaseId == "f1");
        }

        // ---------------------------------------------------------------- trimmer

        [Test]
        public void Trimmer_UpgradeOnce_RadiusScaled()
        {
            var tools = ScriptableObject.CreateInstance<ToolUpgradeConfig>();
            long coins = Econ.Coins;
            Assert.AreEqual(1, Econ.ToolLevel("trimmer"));
            Assert.IsTrue(Econ.TryUpgradeTool("trimmer", 1, 2, 150));
            Assert.IsFalse(Econ.TryUpgradeTool("trimmer", 1, 2, 150), "double tap");
            Assert.AreEqual(coins - 150, Econ.Coins);
            Assert.AreEqual(0.085f * 1.22f, tools.EffectiveRadius(0.085f, Econ.ToolLevel("trimmer")), 1e-5f);
            WoolEconomy.ResetInstanceForTests(); WoolEconomy.Initialise(_balance);
            Assert.AreEqual(2, Econ.ToolLevel("trimmer"), "persists");
            Assert.LessOrEqual(tools.EffectiveRadius(0.1f, 2), tools.MaxEffectiveRadius + 1e-6f, "clamped");
        }

        [Test]
        public void Trimmer_WiderBrush_PaysSameForSameWool()
        {
            // payment is per removed wool volume: stamping the whole mask with a small or a wide brush removes 100 %
            var surf = new SheepBodySurface();
            float RemoveAll(float radius)
            {
                var m = new ShearMaskAuthority(surf, 64, 32);
                float total = 0f;
                for (int v = 0; v < 32; v++)
                    for (int u = 0; u < 64; u++)
                        total += m.Stamp(surf.GetPoint((u + 0.5f) / 64f, (v + 0.5f) / 32f), radius, out _);
                return total;
            }
            Assert.AreEqual(RemoveAll(0.085f), RemoveAll(0.085f * 1.22f), 0.02f);
        }
    }
}