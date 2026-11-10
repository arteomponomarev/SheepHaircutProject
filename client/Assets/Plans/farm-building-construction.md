# Shear & Grow — Stage 4: Farm Building, Grid Placement, Construction, Builder NPC, Decorations

# Project Overview
- **Game Title:** Shear & Grow
- **High-Level Concept:** Cozy mobile sheep-farm game. The player inherits a tiny farm, shears sheep, sells wool, and grows the farm by buying, placing and watching buildings get built. Decorating the farm comes early.
- **Players:** Single player
- **Inspiration / Reference Games:** Hay Day (placement ghost, construction site, decor), Clash of Clans (fixed ortho camera, a builder at work, timers), Township (staged construction visuals)
- **Tone / Art Direction:** Stylized family 3D, soft rounded low-poly, warm palette (existing procedural farm look). Placeholder art is procedural; it will be swapped for final models or 2.5D sprites later.
- **Target Platform:** Android + iOS (iPhone 14 Pro Max is a test device)
- **Screen Orientation / Resolution:** Portrait, reference 1080×1920, safe-area aware
- **Render Pipeline:** URP (Unity 6000.6.8f1), New Input System, IL2CPP

## Stage A — Audit (done during planning)
| Area | What the code does today | Consequence for this stage |
|---|---|---|
| Farm scene | `FarmSceneBuilder` (editor) bakes the house, barn + pen, **woolshed** and trader as fixed scene objects under `FarmWorld`. Static decor (trees, flowers, well, hay) is scattered randomly inside the lawn | Buildings must become **runtime-spawned visual prefabs** driven by save data. The shed must not exist for new players. Decor inside the buildable lawn must move out or count as reserved cells |
| Data | `FarmBuildingDefinition` SO (Id, DisplayName, Kind, Footprint, Start/MaxLevel). `FarmLayout` SO (CellSize 1, 16×24 cells, `CellToWorld`, PlacedBuilding by **type id**). `FarmState` schema 1 = list keyed by building **type id** (no instance ids, no construction) | Extend the definition (keep its class and GUIDs). Add instance-based FarmState schema 2 |
| Economy | `WoolEconomy`: atomic single-file commits, no "spend coins" API. `GameBalanceData.StartingCoins = 0` | Add a receipt-based `TrySpend / Confirm / Refund` for atomic purchases across two save files. StartingCoins becomes 100 |
| Interaction | `FarmInteractable` (logic) is separate from `ViewRoot` (visual) and a hit collider on layer `FarmInteractable`. `ProcessingShedInteraction` always opens Wash/Dry | Good base. Add `InstanceId` and gate on construction state |
| Input | `FarmInputRouter`: tap / 1-finger drag = camera pan / pinch / wheel, UI-blocking | Add a **drag interceptor**: a drag that starts on the ghost moves the ghost. Drags anywhere else still pan the camera |
| Camera | `FarmCameraController`: fixed pitch/yaw ortho, zoom, `ScreenToGround`, `FocusOn` | Reused unchanged (plus an edge-pan call during ghost drag) |
| Sheep | `FarmSheepView.SetPen(Rect, seed)` exists. The pen is a child of the barn view | The barn footprint includes the pen. Moving the barn calls `SetPen` with the new rect |
| Regional regrowth / hairstyles | Implemented (`SheepWoolLifecycle`, `FarmSheepStates`, `WoolGrowthClock`, `CreditRemovedWool`, sheep card with Unlock) | Must not regress. No changes planned there |
| Saves | economy.json (epoch), per-sheep JSON (epoch), farm.json (schema 1), clock.json. `SaveDataBuildValidator` | **User decision: old progress may be wiped** → bump the epochs and the farm schema for a clean start (see Save section) |
| Tests | No test assemblies in the project | Add an EditMode test assembly for the pure-C# building core |

**User decisions:** procedural cartoon builder NPC (code-animated, no Animator or NavMesh). Placement drag: the ghost moves only when the drag starts on it, otherwise the camera pans, and the camera edge-pans while the ghost is dragged near the screen edge. Old saves may be reset. Decorations use the same 1 m grid.

# Game Mechanics
## Core Gameplay Loop
1. A new player starts with 100 coins, Farmer House (Lv 1), Barn + pen with Daisy & Clover, and the Trader by the road. There is no processing building.
2. Shear → Dirty Wool → sell at the Trader (always possible, so there is no economic dead end).
3. **Build ▸ Buildings ▸ Wool Processing (80 coins, 4×3 cells)** → a ghost appears → drag it onto free green cells → ✓ Confirm. Coins are charged once and a construction site appears.
4. The builder walks in and hammers. The building goes through Foundation → Walls/scaffold → Roof & details → Completed (8 s for the first building). A sparkle, a sound, a haptic tick and the toast "Wool Processing is ready!" mark the finish.
5. Tap the building → Open → the existing Wash / Dry panel → the existing mini-games → sell Clean Wool for 1.8× the price.
6. Decorate: buy trees, flower beds, stones, fences, paths and a sheep statue. They appear instantly. Move them any time, or store them in the inventory and place them again for free.
7. Edit mode: move the house, barn (with pen and sheep) or decor for free. The Trader stays pinned by the road.

MDA: *expression* (layout, decor) + *fantasy* (a living farm that grows) + *challenge-lite* (saving up for a building). SDT: autonomy (free placement), competence (first build unlocks better prices), relatedness to "my farm". Bartle: Achievers (unlock processing), Explorers/Socialisers-to-be (decorating).

## Controls and Input Methods
- **Tap** a building: selects it and opens its action sheet. **Tap** empty ground: closes the sheet. Touches starting on UI are ignored (existing `OverUI` rule).
- **Placement / Move mode:**
  - A drag that **starts on the ghost** (footprint rect + 0.5 cell margin, tested on the ground plane) moves the ghost. The ghost follows the ground point under the finger + the grab offset, lifted **~70 px (ref) upward on touch only**, so the finger does not cover it.
  - **Snap with hysteresis:** the snapped cell changes only after the continuous position crosses a cell border by ≥ 0.3 cell. The visual moves to the snapped cell with SmoothDamp (~0.06 s), so it never jitters.
  - **Edge pan:** while dragging the ghost within 8 % of a screen edge, the camera pans in that direction (speed scales with depth into the margin).
  - A drag elsewhere pans the camera. Pinch / wheel zooms. Tapping a free cell jumps the ghost there (convenience).
  - Bottom bar: ✕ Cancel / price / ✓ Confirm. Confirm is disabled with a reason when the spot is invalid. Cancel never costs coins.
- Camera angle never changes. Only the existing pan/zoom is used.

# UI
```
┌──────────────────────────────────┐
│ 🪙 100   [🟤 0][💧 0][⚪ 0]     ⚙ │  existing top HUD
│ 🏗 Wool Processing  ▓▓▓▓░░  0:05 │  construction chip (only while building; tap = focus)
│                                  │
│            (farm, ortho)         │
│                                  │
│                                  │
│ [✥ Edit]                [🔨 Build]│  bottom-corner round buttons (hidden while a sheet is open)
└──────────────────────────────────┘

Build catalog (bottom sheet, ~55 % height)
┌──────────────────────────────────┐
│ Build                          ✕ │
│ [ Buildings ]  [ Decorations ]   │  tabs
│ ┌──────┐ ┌──────┐ ┌──────┐       │  horizontal scroll cards
│ │ icon │ │ icon │ │ icon │       │
│ │Wool  │ │Tree  │ │Fence │       │
│ │Proc. │ │ 1×1  │ │ 1×1  │       │
│ │ 3×3  │ │🪙 5  │ │🪙 2  │       │
│ │🪙 80 │ │      │ │ ×3 in│       │  "In inventory ×3 · free"
│ │[Buy] │ │[Buy] │ │ inv. │       │
│ └──────┘ └──────┘ └──────┘       │
│ status lines: "Built" · "Building… 0:05" · "Need 20 more 🪙" ·
│ "Another construction is in progress" · "Max 1 reached"
└──────────────────────────────────┘

Placement bar (during placement / move)
┌──────────────────────────────────┐
│   "Drag to move · fits here ✓"  │  hint / reason ("Cells occupied", "Outside the farm", "Road")
│  [ ✕ Cancel ]  🪙 80  [ ✓ Place ]│
└──────────────────────────────────┘

Building action sheet (existing bottom-sheet framework)
 Completed:   Wool Processing · Lv 1        [Open ▸] [✥ Move] [ⓘ Info]
 Building:    Wool Processing · Building…  ▓▓▓▓▓░░ 62 %  · 3 s left
              "Can't move while under construction"
 Decoration:  Flower Bed                     [✥ Move] [📦 Store]
 Trader:      [Sell ▸] [ⓘ]   (Move hidden; Info says "The trader stays by the road")
```
- Grid overlay is visible only in placement / edit mode: one ground quad with a cell-line texture plus a per-cell occupancy texture (red tint = occupied/reserved). One draw call, refreshed only when the occupancy changes.
- Footprint highlight under the ghost: one quad, green (valid) / red (invalid). The ghost's renderers use one shared transparent material with a MaterialPropertyBlock tint.
- World labels gain a "🏗 62 %" suffix for sites and a badge when a building completes (cleared on first tap).
- Completion feedback: one pooled sparkle burst + dust puff, `ProceduralAudioLibrary.Finish()`, `ProcessingHapticsDriver.Completed()` (existing haptics stack), toast. No modal popups.

# Key Asset & Context
### Architecture (4 separated layers, all in `ShearAndGrow.Farm` unless noted)
```
Data (SO, read-only)      State (save, truth)         Logic (pure C#)                 Presentation (MonoBehaviours)
FarmBuildingDefinition ─▶ BuildingInstanceState ◀── BuildingService ──events──▶ FarmWorldPresenter → IBuildingView (3D prefab | future sprite)
BuildingCatalog           FarmState (schema 2)       FarmGrid (occupancy)            PlacementController / GridOverlay / Ghost
FarmLayout (grid, start)                             ConstructionClock (IClock)      BuilderNpcController, ConstructionSiteView
ConstructionConfig                                    PurchaseReconciler              FarmInteractable (Interaction layer)
```
Positions come **only** from `cell + footprint` via `FarmGrid.FootprintCenterWorld`. Transforms are derived and never saved.

### Data — `Farm/Runtime/Building/Data/`
- `FarmBuildingDefinition.cs` (**extend**, keep the type and asset GUIDs):
  ```csharp
  public enum BuildingCategory { Building, Decoration }
  public enum BuildingInteraction { None, FarmerHouse, Barn, WoolProcessing, Trader }
  public enum ConnectMode { None, Fence, Path }        // simple neighbour auto-connect
  // existing: Id, DisplayName, Description, Kind, Footprint, StartLevel, MaxLevel
  public BuildingCategory Category; public BuildingInteraction Interaction;
  public int Price; public float ConstructionSeconds;      // 0 = instant (decor)
  public GameObject VisualPrefab; public Sprite Icon;
  public int RequiredFarmerHouseLevel = 1; public int MaxInstances = 1;  // 0 = unlimited
  public bool Movable = true, Storable, Purchasable = true, ShowInCatalog = true;
  public ConnectMode Connect; public bool BlocksCells = true;
  public int[] UpgradeCosts; public float[] UpgradeSeconds; // prepared, unused this stage
  public bool IsUnlocked(int houseLevel) => houseLevel >= RequiredFarmerHouseLevel;
  ```
- `BuildingCatalog.cs` (new SO): `FarmBuildingDefinition[] Items` (display order). `Find(id)`, `ByCategory(cat)`.
- `ConstructionConfig.cs` (new SO): `MaxConcurrentConstructions = 1`, stage thresholds `{0.18, 0.62, 0.9}`, `BuilderWalkSpeed = 1.6`, `BuilderRepositionSeconds = (3, 6)`, `TickInterval = 0.25 s`, `CompletionToastSeconds`.
- `FarmLayout.cs` (**extend**): `ReservedRects` (RectInt list: road, trader lane, front border), `StartingBuildings` (`{instanceId, typeId, cell}` with fixed ids `start_house`, `start_barn`, `start_trader`), `GateCell` (builder spawn), `FootprintCenterWorld(cell, footprint)`, `WorldToCell`.
- `GameBalanceData.asset`: `StartingCoins = 100`. Building prices and times live in each definition, which is the balance config for buildings.

**Definitions (placeholder values, tunable):**
| Id | Name | Cat | Footprint | Price | Build s | Interaction | Movable | Notes |
|---|---|---|---|---|---|---|---|---|
| farmhouse | Farmer House | Building | 4×4 | – | – | FarmerHouse | ✓ | starting, not purchasable |
| barn | Barn & Sheep Pen | Building | 5×8 (barn + pen) | – | – | Barn | ✓ | starting. Pen rect follows the instance |
| trader | Trader Stall | Building | 3×2 | – | – | Trader | ✗ (pinned by the road) | starting |
| woolshed | Wool Processing | Building | 4×3 | 80 | 8 | WoolProcessing | ✓ | max 1, house level 1 |
| decor_tree | Small Tree | Deco | 1×1 | 5 | 0 | – | ✓ | storable |
| decor_flowerbed | Flower Bed | Deco | 1×1 | 8 | 0 | – | ✓ | storable |
| decor_stone | Decorative Stone | Deco | 1×1 | 4 | 0 | – | ✓ | storable |
| decor_fence | Wooden Fence | Deco | 1×1 | 2 | 0 | – | ✓ | Connect = Fence, chain placement |
| decor_path | Path Tile | Deco | 1×1 | 1 | 0 | – | ✓ | Connect = Path, chain placement |
| decor_sheepstatue | Sheep Statue | Deco | 2×2 | 25 | 0 | – | ✓ | storable |

### State — `Farm/Runtime/Building/State/`
```csharp
public enum BuildingStatus { UnderConstruction = 0, Completed = 1, Stored = 2 }
[Serializable] public sealed class BuildingInstanceState {
  public string instanceId;  public string typeId;  public int level = 1;
  public Vector2Int cell;    public int rotationSteps;   // rotation prepared, always 0 now
  public BuildingStatus status;
  public long constructionStartUtcTicks, constructionEndUtcTicks; public float constructionSeconds;
  public string purchaseId;  public bool completionSeen;  // false → show "ready" feedback once
}
[Serializable] public sealed class FarmState {   // schema 2, epoch
  public int schemaVersion, saveEpoch, layoutVersion;
  public List<BuildingInstanceState> instances;   public long lastSeenUtcTicks;
  public string createdUtc, lastSaveUtc;
}
```
- `FarmStateStore` (**modify**): schema 2 + `saveEpoch`. It is still an atomic tmp → replace + `.bak` save. A loaded file with schema < 2 or a lower epoch is ignored → new-player farm (user decision). A `Migrate(int fromSchema)` hook is kept for future versions.

### Logic — `Farm/Runtime/Building/Logic/` (pure C#, unit-testable, no UnityEngine.Object access)
- `IClock { long UtcNowTicks { get; } }` + `SystemClock`. Tests use `FakeClock`.
- `FarmGrid`: `int[] _cells` (index of the occupying instance + 1, 0 = free), `bool[] _reserved` (layout reserved rects + out-of-lawn cells). `Rebuild(state, catalog)` runs only on load and after mutations. `PlacementResult Check(def, cell, ignoreInstanceId)` returns `Ok | OutOfBounds | Reserved | Occupied | Locked | MaxInstances | ConstructionBusy | NotEnoughCoins` with the first blocking cell. `Occupy/Release(instance)`. `NeighbourMask(cell, typeId)` for connections. There is no per-frame work: the ghost only calls `Check` when its snapped cell changes.
- `BuildingService` (owns `FarmState`, `FarmGrid`, refs `WoolEconomy`, `IClock`, catalog, config):
  - `PlacementResult CanPlace(def, cell)` / `CanMove(inst, cell)`.
  - `bool TryPurchase(def, cell, string purchaseId, out BuildingInstanceState inst, out PlacementResult why)`, an **atomic** operation (below).
  - `bool TryMove(instanceId, cell)` — completed buildings and decor only. Free. Saves.
  - `bool TryStore(instanceId)` / `TryPlaceFromInventory(typeId, cell)` — decor only.
  - `int Tick()` completes due constructions (idempotent: only `UnderConstruction` → `Completed`) and returns the count.
  - `float Progress(inst)` / `double RemainingSeconds(inst)` (clamped, see the timer section).
  - `ActiveConstruction`, `CountOf(typeId)`, `IsFunctional(BuildingInteraction)` (e.g. processing available ⇔ a Completed woolshed exists).
  - events `InstanceAdded/Moved/Removed/StatusChanged(inst)`.
- **Atomic purchase** (two files: economy.json + farm.json), receipt-based:
  1. Validate the definition, unlock, max instances, the single active construction (buildings only), the grid and the coins.
  2. `WoolEconomy.TrySpend(purchaseId, price, typeId)` deducts coins and adds a `PendingPurchase{id, typeId, price, utc}` to economy.json in **one commit**. It is idempotent: a known id returns the earlier result without charging again.
  3. Add the instance (`purchaseId`, `UnderConstruction`, start/end ticks, or `Completed` for decor), occupy the grid and run `FarmStateStore.Save`. On exception: remove the instance, `WoolEconomy.RefundPurchase(id)`, return false.
  4. `WoolEconomy.ConfirmPurchase(id)` moves the receipt to `recentPurchaseIds` (cap 64).
  - `PurchaseReconciler.Run()` on farm start: each pending receipt with a matching farm instance → Confirm. Without one → Refund. So a crash between the steps can never leave the player without coins and without the building, and never gives a free building.
  - Double-tap safety: one `purchaseId` per placement session, the Confirm button is disabled after the first press, and the service has a re-entrancy guard.
- `Core/Runtime/WoolEconomy.cs` + `PlayerEconomyState.cs` (**modify**): `List<PendingPurchase> pendingPurchases`, `List<string> recentPurchaseIds`, `bool CanAfford(long)`, `TrySpend`, `ConfirmPurchase`, `RefundPurchase`, `PendingPurchases`. The economy schema keeps its JSON-compatible additions.

### Timers & offline progress
- Times are stored as **UTC ticks** (`DateTime.UtcNow.Ticks`, `long` → JsonUtility-safe). End = start + duration × 10⁷.
- `Remaining = clamp(end − now, 0, duration)` and `Progress = 1 − Remaining/duration`.
  - Clock moved back → remaining is capped at the full duration (never negative, never longer than the definition).
  - Clock moved forward → finishes early. This is a **documented MVP limitation** (device clock is trusted. A future server time or monotonic uptime check can replace `SystemClock`).
  - `lastSeenUtcTicks` is saved on pause/quit. A rollback over 5 min is logged for diagnostics.
- Ticks: `FarmBuildingDirector` calls `service.Tick()` every `TickInterval` **only while a construction is active**, plus on Start and `OnApplicationPause(false)`. A construction finished offline becomes Completed immediately on load, shows the completed model directly and one toast ("…finished while you were away"). No animation replay.

### Presentation — `Farm/Runtime/Building/View/`
- `IBuildingView` (interface) + `BuildingView` (MonoBehaviour on every visual prefab). This is the seam for swapping 3D for 2.5D:
  ```csharp
  public interface IBuildingView {
    void Bind(BuildingInstanceState s, FarmBuildingDefinition d);
    void SetWorldPosition(Vector3 footprintCenter);
    void SetConstruction(int stage, float stageT);     // 0 foundation,1 walls,2 roof,3 done
    void SetGhost(Material ghostMat, bool on);  void SetConnections(int neighbourMask);
    Transform ViewRoot { get; }  Transform LabelAnchor { get; }  Collider HitArea { get; }
  }
  ```
  `BuildingView` holds optional stage groups `Stage_Foundation / Stage_Walls / Stage_Roof / Stage_Details`:
  - foundation shown from 0
  - walls grow in Y (0→1) during stage 1
  - roof drops in with an ease-out bounce during stage 2
  - details pop in at 0.9
  - Completed = all groups at full size with a squash-stretch "pop"

  Only Transform scale/position changes happen, with no instantiation during the build.
- `ConnectableDecorView : BuildingView`: fence posts + rails toward same-type neighbours, path tile edge pieces (4-bit mask → enable/disable child pieces). It is recomputed only for the changed cell and its 4 neighbours.
- `ConstructionSiteView` (one pooled instance, since only one construction is active): a dirt pad scaled to the footprint, scaffolding poles, a plank pile and one `ParticleSystem` for dust puffs (max 12 particles, no collision). It is hidden on completion.
- `FarmWorldPresenter`: spawns views from state on start, keeps `instanceId → view`, listens to service events (add/move/remove/status), adds/removes world labels, and reparents the pen + calls `FarmSheepView.SetPen(rect)` when the barn moves (sheep snap into the new pen). Decor views are pooled per type (store/move reuses them).
- Existing interactions stay on the prefabs (`HouseInteraction`, `BarnInteraction`, `ProcessingShedInteraction`, `TraderInteraction`). `FarmInteractable` gains `InstanceId` and `Configure(...)` from the presenter. New `DecorationInteraction` opens the Move/Store sheet.
- `ProcessingShedInteraction` (**modify**): `HasAttention` and `OnTap` respect `hub.Buildings.IsFunctional(...)`. While under construction, the tap opens the construction sheet. `FarmPanels.ShowShed` also guards (no Wash/Dry unless Completed).
- `PlacementController` (modes: `None, PlaceNew, MoveExisting`):
  - `BeginPlace(def | inventoryTypeId)` spawns the ghost (the def's visual prefab with the ghost material), finds the first valid cell near the screen centre (spiral search) and focuses the camera there.
  - `BeginMove(instanceId)`: the original view stays visible at 35 % alpha while the ghost moves. The grid check uses `ignoreInstanceId`.
  - Drag handling (snap + hysteresis + finger offset + edge pan) feeds the snapped cell to `service.Can…`, updates the tint and the reason text.
  - `Confirm()` → `TryPurchase` / `TryMove` / `TryPlaceFromInventory` → closes, or for fence/path **chain mode** keeps placing the next segment at the adjacent cell until Cancel. `Cancel()` restores everything with no economy calls.
  - It implements `IFarmDragInterceptor { bool TryBeginDrag(Vector2 screen); void Drag(Vector2 screen); void EndDrag(); bool HandleTap(Vector2 screen); }`.
- `FarmInputRouter` (**modify**): optional `IFarmDragInterceptor`. On Pending → Drag transition, ask the interceptor first. If it accepts, route the drag there instead of the camera. Tap in placement mode → interceptor `HandleTap`. Pinch/wheel unchanged.
- `GridOverlay`: one quad covering the lawn, URP unlit transparent material, `_MainTex` = cell-line texture tiled per cell, `_OccTex` = R8 texture W×H (point filter) uploaded on grid change only. It is enabled in placement/edit mode only.
- `BuilderNpcController` + `BuilderNpcFactory` (procedural cartoon builder, ~800 verts, ~10 rigid parts: boots, overalls body, head, yellow hard hat, arms, hammer. `SoftCharacter` shader materials. No shadows except a blob quad).
  - State machine: `Hidden → WalkIn → Working ⇄ Reposition → Celebrate → WalkOut → Hidden`.
  - Movement is straight segments via up to 3 waypoints (gate → road → nearest footprint corner → work point), going around the footprint rectangle corners. There is no NavMesh, physics or collision.
  - Work points: 4 sides of the footprint at 0.6 m outside. Every 3–6 s it moves to another point.
  - Code animation: walk bob + leg swing (sin), hammer swing curve with a squash at impact + a dust puff + a soft knock sound (procedural, throttled, only if the site is on screen).
  - **Purely visual:** the construction state never waits for it. If the route is blocked, it fades out and in at the work point. On load with an active site it spawns directly at a work point. On completion it plays a short cheer and walks out.
- `FarmBuildingDirector` (new composition piece used by `FarmHubController`): owns `BuildingService`, the presenter, placement, overlay, builder, site, ticking, completion feedback and save-on-pause.
- `FarmHubController` (**modify**):
  - creates the director before collecting interactables
  - routes taps on construction sites to the construction sheet
  - hides Build/Edit buttons during placement
  - camera core bounds from farm bounds
  - Dev tools: "DEV finish construction" and "DEV +100 coins"
  - DevReset also resets the building service
- UI — `Farm/Runtime/UI/`:
  - `BuildCatalogPanel.cs`: tabs, scroll cards, statuses from `service.CanPlace` / economy. Refreshed on `WoolEconomy.Changed` + service events only.
  - `PlacementBar.cs`
  - `ConstructionChip.cs`: text updated once per second, cached strings, no per-frame alloc
  - `FarmPanels` (**modify**): `ShowBuilding(inst, def, actions)`, `ShowConstruction(inst, def)` with a live progress bar, `ShowDecoration(...)`.
  - `FarmHUD` (**modify**): Build / Edit buttons, hooks for the construction chip.
  - Edit mode: banner "Edit mode · tap a building to move it · Done". Taps select a movable instance → `BeginMove`. Trader → toast.

### Editor — `Farm/Editor/`
- `FarmSceneBuilder.cs` (**modify**):
  - generates **visual prefabs** to `Farm/Prefabs/Buildings/*.prefab` (house, barn+pen, trader, woolshed) and `Farm/Prefabs/Decor/*.prefab` (6 decor). Every model part is tagged with a construction stage group.
  - generates `Farm/Prefabs/Builder.prefab`, `ConstructionSite.prefab` and the ghost / grid materials
  - renders **catalog icons** (one-off ortho render → `Farm/Art/Icons/*.png` sprites)
  - writes the definitions, `BuildingCatalog.asset`, `ConstructionConfig.asset` and the `FarmLayout` reserved rects + starting buildings
  - keeps environment decor (trees/flowers/hay/well) outside the buildable area or registers it as reserved
  - **removes the baked buildings from the scene** (spawned at runtime), then rebuilds `FarmHub.unity`
- `SaveDataBuildValidator` (**modify**): also checks that `farm.json` is never under Assets/StreamingAssets/Resources and that the definitions/catalog are consistent (unique ids, prefab present, footprint ≥ 1, starting instances not overlapping and inside bounds). This way no developer test farm ships as start data: the starting farm comes **only** from `FarmLayout.StartingBuildings`.

### Tests — `Farm/Tests/Editor/ShearAndGrow.Farm.Tests.asmdef` (EditMode, refs Farm + Core)
`FarmGridTests`, `BuildingServiceTests`, `PurchaseAtomicityTests`, `ConstructionTimerTests`, `FarmStateStoreTests` (temporary `RootOverride` folders for both stores).

# Implementation Steps
1. **Data layer** — extend `FarmBuildingDefinition`. Add `BuildingCatalog`, `ConstructionConfig`, the `FarmLayout` reserved rects/starting buildings/helpers. `StartingCoins = 100`. *Dependency: none.*
2. **State + store** — `BuildingInstanceState`, FarmState schema 2 + epoch, store changes (old schema → new farm). Bump `GameBalanceData.EconomySaveEpoch` 0→1 and `DefaultGameState.SaveEpoch` +1 → full clean start (user decision; sheep, coins and wool reset together, so nothing is half-migrated). *Dependency: 1.*
3. **Economy receipts** — `TrySpend / ConfirmPurchase / RefundPurchase`, pending receipts and recent ids in `PlayerEconomyState`. *Dependency: none.*
4. **Grid + BuildingService + clock + reconciler** (pure C#) + **EditMode tests**. *Dependency: 1–3.*
5. **Visual layer** — `IBuildingView`, `BuildingView` stages, `ConnectableDecorView`, `ConstructionSiteView`, `FarmWorldPresenter` (spawn from state, barn pen follow), `DecorationInteraction`, `FarmInteractable.InstanceId`. *Dependency: 4.*
6. **Scene builder** — prefabs (4 buildings with stage groups, 6 decor, builder, site), icons, materials, catalog/config assets, reserved rects, remove baked buildings, rebuild FarmHub. Portrait screenshots to check readability. *Dependency: 5.*
7. **Placement** — `PlacementController`, input interceptor in `FarmInputRouter`, ghost material + tint, `GridOverlay`, snap/hysteresis/finger offset/edge pan, chain mode. *Dependency: 5, 6.*
8. **Construction UI** — Build/Edit buttons, `BuildCatalogPanel`, `PlacementBar`, `ConstructionChip`, building/construction/decoration sheets in `FarmPanels`. *Dependency: 7.*
9. **Construction runtime** — `FarmBuildingDirector` ticking, stage animation, site, completion feedback (sparkle pool, sound, haptics, toast, label badge), offline completion on load, save on pause. *Dependency: 5, 8.*
10. **Builder NPC** — factory + controller state machine, work points, code animation, knock audio, fade fallback. *Dependency: 9.*
11. **Wool Processing integration** — shed gating (`IsFunctional`), construction sheet while building, Open → existing `ShowShed` → existing Wash/Dry scenes. Return to the farm keeps the instance (state from save). *Dependency: 9.*
12. **Edit mode** — move completed buildings/decor (free, grid-checked, cancelable, saved), trader pinned, under-construction move blocked with a message, decor Store/inventory placement. *Dependency: 7, 8.*
13. **Decorations** — 6 items incl. fence/path connections and chain placement. *Dependency: 7, 12.*
14. **Hub integration & dev tools** — `FarmHubController` wiring, DevReset of the building state, DEV finish construction, DEV +coins, validator update. *Dependency: 9–13.*
15. **Validation** — tests + Play Mode scripted runs (below), screenshots, player-script compile for Android/iOS, perf stats. Editor saves are backed up and restored afterwards. *Dependency: all.*

# Verification & Testing
**EditMode (automated)**
- Grid: footprint inside/outside bounds, reserved road cells rejected, overlap rejected, `ignoreInstanceId` lets a building move onto part of its own old cells, Rebuild after load equals the incremental state.
- Purchase: 100 coins → buy woolshed (80) → 20 coins, 1 instance, receipt confirmed. Same `purchaseId` twice → still 20 coins, 1 instance. Second woolshed → `MaxInstances`. 79 coins → `NotEnoughCoins`, no change. Second building during an active construction → `ConstructionBusy`. Decor allowed during construction.
- Atomicity: simulated farm-save failure → coins refunded, no instance. Crash after spend (pending receipt, no instance) → reconciler refunds. Crash after the farm save (pending receipt + instance) → reconciler confirms, no refund. Restart repeated 5× → no double charge, no duplicate.
- Timers (FakeClock): progress 0 → 0.5 → 1. `Tick` completes exactly once. Offline +1 h → Completed on load. Clock −1 day → remaining ≤ duration, never negative. Stage index from the thresholds.
- Move/store: move completed → cells updated and saved, no duplicate. Move under construction → rejected. Trader move → rejected. Store/place-from-inventory round trip.
- Store: schema-1 / lower-epoch farm.json → new farm (house, barn, trader; **no woolshed**). Save → load round trip identical. Corrupt file → `.bak`.

**Play Mode (scripted in Editor, QA hooks)**
1. Clean start: 100 coins, house/barn/trader, 2 sheep, no shed. Shearing works. Dirty Wool sells at the trader.
2. Build ▸ Wool Processing → ghost on a valid cell (green). Move onto the barn → red + "Cells occupied". Onto the road → "Reserved". Cancel → coins still 100.
3. Place again → Confirm (double invoke) → 20 coins once, construction site and builder appear, stages advance, the chip counts down, completes after ~8 s with toast/sound/haptic.
4. Tap the shed → Open → Wash enabled with Dirty → Wash scene → back → Dry → back → Sell Clean at 1.8×.
5. Start a second test build (dev building or DEV) while one runs → blocked message. Place decor during construction → instant.
6. Quit mid-construction (scene reload + simulated clock +60 s) → Completed on load, no animation replay, one toast.
7. Edit: move house and barn (sheep follow the pen), trader blocked, reload → positions kept, still functional, no duplicates.
8. Decor: place tree, flower bed, stone, statue, a 5-segment fence and a path chain (connections update), move one, store one, reload → all restored.
9. Regression: shearing + regional regrowth/hairstyle lock + wash + dry + sell + labels + camera angle unchanged + haptics calls unchanged. No errors in the console.

**Builds & performance:** player scripts compile for Android + iOS (release + dev). Farm idle and active construction: 0 GC alloc per frame (Profiler), grid work only on change, ≤ +15 draw calls during construction (site + builder + dust), no new realtime lights, no physics bodies added (only trigger/hit colliders), particle cap 12.

**Not possible here (explicitly not claimed):** physical Android/iOS tests (heat, FPS, touch feel of the ghost drag, edge-pan speed, haptic feel). Listed as a device checklist in the final report.

**Known limitations / to replace later:** procedural placeholder art (4 buildings with stage groups, 6 decor, builder, site, icons), procedural knock sound. Device-clock-trusted timers. No rotation of buildings (field prepared). No upgrades or Farmer House progression (fields prepared). One active construction only.
