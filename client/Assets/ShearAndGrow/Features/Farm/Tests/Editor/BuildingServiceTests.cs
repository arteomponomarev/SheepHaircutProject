using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Farm.Tests
{
    public sealed class BuildingServiceTests
    {
        string _root;
        FarmLayout _layout;
        BuildingCatalog _catalog;
        ConstructionConfig _config;
        GameBalanceData _balance;
        FarmBuildingDefinition _house, _barn, _trader, _shed, _tree, _fence, _workshop;
        ManualBuildingClock _clock;
        bool _failFarmSave;

        static FarmBuildingDefinition Def(string id, BuildingCategory cat, Vector2Int fp, int price, float secs, BuildingInteraction it, int max = 1, bool movable = true, bool storable = false, bool purchasable = true)
        {
            var d = ScriptableObject.CreateInstance<FarmBuildingDefinition>();
            d.Id = id; d.Category = cat; d.Footprint = fp; d.Price = price; d.ConstructionSeconds = secs; d.Interaction = it;
            d.MaxInstances = max; d.Movable = movable; d.Storable = storable; d.Purchasable = purchasable;
            return d;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "sag_farm_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            EconomyStore.RootOverride = _root;
            FarmStateStore.RootOverride = _root;
            WoolEconomy.ResetInstanceForTests();
            _balance = ScriptableObject.CreateInstance<GameBalanceData>();
            _balance.StartingCoins = 100;
            WoolEconomy.Initialise(_balance);

            _house = Def("farmhouse", BuildingCategory.Building, new Vector2Int(4, 4), 0, 0, BuildingInteraction.FarmerHouse, purchasable: false);
            _barn = Def("barn", BuildingCategory.Building, new Vector2Int(5, 8), 0, 0, BuildingInteraction.Barn, purchasable: false);
            _trader = Def("trader", BuildingCategory.Building, new Vector2Int(3, 2), 0, 0, BuildingInteraction.Trader, movable: false, purchasable: false);
            _shed = Def("woolshed", BuildingCategory.Building, new Vector2Int(4, 3), 80, 8f, BuildingInteraction.WoolProcessing);
            _workshop = Def("workshop", BuildingCategory.Building, new Vector2Int(2, 2), 10, 20f, BuildingInteraction.None);
            _tree = Def("decor_tree", BuildingCategory.Decoration, Vector2Int.one, 5, 0, BuildingInteraction.None, max: 0, storable: true);
            _fence = Def("decor_fence", BuildingCategory.Decoration, Vector2Int.one, 2, 0, BuildingInteraction.None, max: 0, storable: true);

            _catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            _catalog.Items = new[] { _house, _barn, _trader, _shed, _workshop, _tree, _fence };
            _layout = ScriptableObject.CreateInstance<FarmLayout>();
            _layout.SizeCells = new Vector2Int(16, 24);
            _layout.Definitions = _catalog.Items;
            _layout.StartingBuildings = new List<StartingBuilding>
            {
                new StartingBuilding { InstanceId = "start_house", TypeId = "farmhouse", Cell = new Vector2Int(1, 17) },
                new StartingBuilding { InstanceId = "start_barn", TypeId = "barn", Cell = new Vector2Int(10, 13) },
                new StartingBuilding { InstanceId = "start_trader", TypeId = "trader", Cell = new Vector2Int(10, 4) },
            };
            _layout.ReservedRects = new List<RectInt> { new RectInt(7, 0, 2, 24) }; // road
            _config = ScriptableObject.CreateInstance<ConstructionConfig>();
            _clock = new ManualBuildingClock(new System.DateTime(2026, 7, 11, 12, 0, 0, System.DateTimeKind.Utc).Ticks);
            _failFarmSave = false;
        }

        [TearDown]
        public void TearDown()
        {
            WoolEconomy.ResetInstanceForTests();
            EconomyStore.RootOverride = null;
            FarmStateStore.RootOverride = null;
            WoolEconomy.QA_FailNextSave = false;
            try { Directory.Delete(_root, true); } catch { }
        }

        BuildingService NewService(FarmState loaded = null)
        {
            var s = new BuildingService(_layout, _catalog, _config, WoolEconomy.Instance, _clock,
                st => { if (_failFarmSave) return false; FarmStateStore.Save(st); return true; });
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

        // ---------------------------------------------------------------- new player & grid

        [Test]
        public void NewPlayer_HasHouseBarnTrader_NoShed()
        {
            var s = NewService();
            Assert.AreEqual(3, s.State.instances.Count);
            Assert.AreEqual(0, s.CountOf("woolshed"));
            Assert.IsFalse(s.IsFunctional(BuildingInteraction.WoolProcessing));
            Assert.AreEqual(100, Econ.Coins);
        }

        [Test]
        public void Grid_RejectsOutOfBounds_Reserved_Overlap()
        {
            var s = NewService();
            Assert.AreEqual(PlacementResult.OutOfBounds, s.CheckPurchase(_shed, new Vector2Int(14, 0)));
            Assert.AreEqual(PlacementResult.Reserved, s.CheckPurchase(_shed, new Vector2Int(6, 5)));
            Assert.AreEqual(PlacementResult.Occupied, s.CheckPurchase(_shed, new Vector2Int(2, 18)));
            Assert.AreEqual(PlacementResult.Ok, s.CheckPurchase(_shed, new Vector2Int(1, 8)));
        }

        [Test]
        public void Grid_MoveIgnoresOwnCells()
        {
            var s = NewService();
            var house = s.State.Find("start_house");
            Assert.AreEqual(PlacementResult.Ok, s.CheckMove(house, house.cell + new Vector2Int(1, -1)));
        }

        // ---------------------------------------------------------------- purchase

        [Test]
        public void Purchase_ChargesOnce_EvenWhenRepeated()
        {
            var s = NewService();
            Assert.IsTrue(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out var a, out _));
            Assert.IsTrue(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out var b, out _));
            Assert.AreSame(a, b);
            Assert.AreEqual(20, Econ.Coins);
            Assert.AreEqual(1, s.CountOf("woolshed"));
            Assert.AreEqual(0, Econ.PendingPurchases.Count);
            Assert.AreEqual(BuildingStatus.UnderConstruction, a.status);
        }

        [Test]
        public void Purchase_MaxInstances_And_NotEnoughCoins_And_Busy()
        {
            var s = NewService();
            Assert.IsTrue(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out _));
            Assert.IsFalse(s.TryPurchase(_shed, new Vector2Int(1, 2), "p2", out _, out var r));
            Assert.AreEqual(PlacementResult.MaxInstances, r);
            Assert.IsFalse(s.TryPurchase(_workshop, new Vector2Int(1, 2), "p3", out _, out r));
            Assert.AreEqual(PlacementResult.ConstructionBusy, r);
            // decor is never blocked by the construction
            Assert.IsTrue(s.TryPurchase(_tree, new Vector2Int(0, 0), "p4", out var tree, out _));
            Assert.AreEqual(BuildingStatus.Completed, tree.status);
            Assert.AreEqual(15, Econ.Coins);
        }

        [Test]
        public void Purchase_NotEnoughCoins_ChangesNothing()
        {
            var s = NewService();
            Assert.IsTrue(s.TryPurchase(_tree, new Vector2Int(0, 0), "t1", out _, out _)); // 95
            Assert.IsTrue(s.TryPurchase(_tree, new Vector2Int(1, 0), "t2", out _, out _)); // 90
            Assert.IsTrue(s.TryPurchase(_tree, new Vector2Int(2, 0), "t3", out _, out _)); // 85
            Assert.IsTrue(s.TryPurchase(_tree, new Vector2Int(3, 0), "t4", out _, out _)); // 80
            Assert.IsTrue(s.TryPurchase(_fence, new Vector2Int(4, 0), "f1", out _, out _)); // 78
            Assert.IsFalse(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out var r));
            Assert.AreEqual(PlacementResult.NotEnoughCoins, r);
            Assert.AreEqual(78, Econ.Coins);
            Assert.AreEqual(0, s.CountOf("woolshed"));
        }

        [Test]
        public void Purchase_FarmSaveFails_Refunds()
        {
            var s = NewService();
            _failFarmSave = true;
            Assert.IsFalse(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out _));
            Assert.AreEqual(100, Econ.Coins);
            Assert.AreEqual(0, s.CountOf("woolshed"));
            Assert.AreEqual(0, Econ.PendingPurchases.Count);
            Assert.AreEqual(PlacementResult.Ok, s.CheckPurchase(_shed, new Vector2Int(1, 8)), "cells released");
        }

        [Test]
        public void Purchase_EconomySaveFails_NothingBuilt()
        {
            var s = NewService();
            WoolEconomy.QA_FailNextSave = true;
            Assert.IsFalse(s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out _));
            Assert.AreEqual(100, Econ.Coins);
            Assert.AreEqual(0, s.CountOf("woolshed"));
        }

        [Test]
        public void Reconcile_PendingWithoutBuilding_Refunds()
        {
            NewService();
            Assert.IsTrue(Econ.TrySpend("crash1", 80, "woolshed")); // crash before the farm save
            var s = Reload();
            Assert.AreEqual(100, Econ.Coins);
            Assert.AreEqual(0, Econ.PendingPurchases.Count);
            Assert.AreEqual(0, s.CountOf("woolshed"));
        }

        [Test]
        public void Reconcile_PendingWithBuilding_Confirms()
        {
            var s = NewService();
            Assert.IsTrue(Econ.TrySpend("crash2", 80, "woolshed"));
            s.State.instances.Add(new BuildingInstanceState { instanceId = "x", typeId = "woolshed", cell = new Vector2Int(1, 8), purchaseId = "crash2", status = BuildingStatus.UnderConstruction, constructionSeconds = 8, constructionStartUtcTicks = _clock.Ticks, constructionEndUtcTicks = _clock.Ticks + 8 * System.TimeSpan.TicksPerSecond });
            s.Save(); // crash after the farm save, before confirming
            s = Reload();
            Assert.AreEqual(20, Econ.Coins);
            Assert.AreEqual(0, Econ.PendingPurchases.Count);
            Assert.AreEqual(1, s.CountOf("woolshed"));
            for (int i = 0; i < 5; i++) s = Reload();
            Assert.AreEqual(20, Econ.Coins);
            Assert.AreEqual(1, s.CountOf("woolshed"));
        }

        // ---------------------------------------------------------------- timers

        [Test]
        public void Construction_ProgressAndCompletesOnce()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out var b, out _);
            int events = 0; s.StatusChanged += _ => events++;
            Assert.AreEqual(0f, s.Progress(b), 1e-3f);
            _clock.Advance(4);
            Assert.AreEqual(0.5f, s.Progress(b), 1e-3f);
            Assert.AreEqual(0, s.Tick());
            Assert.IsFalse(s.IsFunctional(BuildingInteraction.WoolProcessing));
            _clock.Advance(4.01);
            Assert.AreEqual(1, s.Tick());
            Assert.AreEqual(0, s.Tick());
            Assert.AreEqual(1, events);
            Assert.IsTrue(s.IsFunctional(BuildingInteraction.WoolProcessing));
        }

        [Test]
        public void Construction_OfflineCompletesOnLoad()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out _);
            _clock.Advance(3600);
            s = Reload();
            var shed = s.State.FirstOfType("woolshed");
            Assert.AreEqual(BuildingStatus.Completed, shed.status);
            Assert.AreEqual(1, s.CompletedWhileAway.Count);
            Assert.AreEqual(new Vector2Int(1, 8), shed.cell);
            Assert.AreEqual(20, Econ.Coins);
        }

        [Test]
        public void Construction_StillRunningAfterReload_ShowsRemaining()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out _, out _);
            _clock.Advance(3);
            s = Reload();
            var shed = s.State.FirstOfType("woolshed");
            Assert.AreEqual(BuildingStatus.UnderConstruction, shed.status);
            Assert.AreEqual(5.0, s.RemainingSeconds(shed), 0.01);
        }

        [Test]
        public void Construction_ClockRollback_NeverExceedsDuration()
        {
            var s = NewService();
            s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out var b, out _);
            _clock.Advance(-86400);
            Assert.LessOrEqual(s.RemainingSeconds(b), 8.0);
            Assert.GreaterOrEqual(s.RemainingSeconds(b), 0.0);
            Assert.GreaterOrEqual(s.Progress(b), 0f);
        }

        [Test]
        public void StageOf_UsesThresholds()
        {
            Assert.AreEqual(0, _config.StageOf(0.1f, out _));
            Assert.AreEqual(1, _config.StageOf(0.3f, out _));
            Assert.AreEqual(2, _config.StageOf(0.7f, out _));
            Assert.AreEqual(3, _config.StageOf(0.95f, out _));
            Assert.AreEqual(4, _config.StageOf(1f, out _));
        }

        // ---------------------------------------------------------------- move / store

        [Test]
        public void Move_SavesAndDoesNotDuplicate()
        {
            var s = NewService();
            var house = s.State.Find("start_house");
            var target = new Vector2Int(1, 8);
            Assert.IsTrue(s.TryMove("start_house", target, out _));
            s = Reload();
            Assert.AreEqual(target, s.State.Find("start_house").cell);
            Assert.AreEqual(1, s.CountOf("farmhouse"));
            Assert.AreEqual(100, Econ.Coins, "moving is free");
            Assert.AreEqual(PlacementResult.Ok, s.CheckPurchase(_shed, new Vector2Int(1, 17)), "old cells freed");
        }

        [Test]
        public void Move_BlockedCases()
        {
            var s = NewService();
            Assert.IsFalse(s.TryMove("start_trader", new Vector2Int(1, 8), out var r));
            Assert.AreEqual(PlacementResult.NotMovable, r);
            Assert.IsFalse(s.TryMove("start_house", new Vector2Int(10, 13), out r));
            Assert.AreEqual(PlacementResult.Occupied, r);
            s.TryPurchase(_shed, new Vector2Int(1, 8), "p1", out var shed, out _);
            Assert.IsFalse(s.TryMove(shed.instanceId, new Vector2Int(1, 2), out r));
            Assert.AreEqual(PlacementResult.UnderConstruction, r);
        }

        [Test]
        public void Store_And_PlaceFromInventory_RoundTrip()
        {
            var s = NewService();
            s.TryPurchase(_tree, new Vector2Int(0, 0), "t1", out var t, out _);
            Assert.IsTrue(s.TryStore(t.instanceId));
            Assert.AreEqual(1, s.StoredCount("decor_tree"));
            Assert.AreEqual(PlacementResult.Ok, s.CheckPurchase(_tree, new Vector2Int(0, 0)));
            Assert.IsTrue(s.TryPlaceFromInventory("decor_tree", new Vector2Int(3, 3), out var placed, out _));
            Assert.AreEqual(t.instanceId, placed.instanceId);
            s = Reload();
            Assert.AreEqual(1, s.CountOf("decor_tree"));
            Assert.AreEqual(new Vector2Int(3, 3), s.State.FirstOfType("decor_tree").cell);
            Assert.AreEqual(95, Econ.Coins);
        }

        [Test]
        public void Fence_NeighbourMask()
        {
            var s = NewService();
            s.TryPurchase(_fence, new Vector2Int(0, 0), "f1", out _, out _);
            s.TryPurchase(_fence, new Vector2Int(1, 0), "f2", out _, out _);
            s.TryPurchase(_fence, new Vector2Int(1, 1), "f3", out _, out _);
            Assert.AreEqual(2, s.Grid.NeighbourMask(new Vector2Int(0, 0), "decor_fence"));
            Assert.AreEqual(1 | 8, s.Grid.NeighbourMask(new Vector2Int(1, 0), "decor_fence"));
        }

        // ---------------------------------------------------------------- store

        [Test]
        public void Store_OldSchemaDiscarded()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(FarmStateStore.FilePath, "{\"schemaVersion\":1,\"layoutVersion\":1,\"buildings\":[{\"buildingId\":\"woolshed\",\"level\":1}]}");
            Assert.IsFalse(FarmStateStore.TryLoad(0, out _));
            var s = NewService(null);
            Assert.AreEqual(0, s.CountOf("woolshed"));
        }

        [Test]
        public void Store_RoundTripAndBackup()
        {
            var s = NewService();
            s.TryPurchase(_tree, new Vector2Int(0, 0), "t1", out _, out _);
            File.WriteAllText(FarmStateStore.FilePath, "{corrupt");
            Assert.IsTrue(FarmStateStore.TryLoad(0, out var st), "backup used");
            Assert.GreaterOrEqual(st.instances.Count, 3);
        }
    }
}