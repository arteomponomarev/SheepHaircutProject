# Shear & Grow — Stage 5: Farmer House Progression, Building Upgrades, Trimmer Workshop, Feeding Station, Barn Expansion

# Project Overview
- **Game Title:** Shear & Grow
- **High-Level Concept:** A cozy mobile sheep farm. You shear, wash, dry and sell wool, then reinvest the coins to grow the farm: upgrade the Farmer House, build new facilities, improve your tools, feed sheep and grow the flock.
- **Players:** Single player
- **Inspiration / Reference Games:** Hay Day (house/barn levels unlock buildings), Township (upgrade timers on the building itself), Clash of Clans (town hall gate: "build everything of your level first")
- **Tone / Art Direction:** Stylized family 3D with procedural placeholder art. Level variants make each upgrade visible. Final models or 2.5D sprites replace the prefabs later.
- **Target Platform:** Android + iOS
- **Screen Orientation / Resolution:** Portrait, reference 1080×1920, safe area aware
- **Render Pipeline:** URP (Unity 6000.6.8f1), New Input System

## Phase A — Audit (done during planning, based on the actual code)
| System | What the code actually does | Reuse / gap |
|---|---|---|
| Building (Stage 4) | `BuildingService` handles atomic purchase (economy receipts `TrySpend/Confirm/Refund` + reconcile on start), `FarmGrid`, one construction queue (`MaxConcurrentConstructions`), UTC timers, offline completion, move/store. `FarmBuildingDirector` handles ticking (only while something is building), the construction site and the builder NPC. 21 EditMode tests pass. | **Reuse completely.** Gap: no upgrade state. `level` is stored but never changes. `FarmBuildingDefinition.UpgradeCosts/UpgradeSeconds` exist but nothing uses them. |
| House level | `BuildingService.FarmerHouseLevel` reads the house instance level. `CheckPurchaseRules` already returns `Locked` via `RequiredFarmerHouseLevel`. The catalog shows "Needs Farmer House Lv N". | The gate already works. Only levels, upgrade logic and new definitions are missing. |
| Farm save | `farm.json` schema 2 (instances, epoch). `Migrate()` hook exists. | Add schema 3 (herd + upgrade fields) with a **real 2→3 migration** (no reset this time). |
| Sheep / herd | Fixed list `FarmLayout.Sheep` (Daisy, Clover). Sheep views are **baked into the scene** by `FarmSceneBuilder`. Each sheep's wool lives in its own file `SheepWoolStateStore` (`sheep_<id>.json`), keyed by id. Shearing gets its id from `LaunchContext.SheepId` → `SheepRigBuilder.OverrideIdentity`. | New sheep only need a new id and a new entry in the persistent herd list. Shearing, regrowth and hairstyle lock work per id already. Sheep views must be **spawned at runtime** from the herd. |
| Regrowth | `SheepWoolLifecycle.CatchUp(state, mask, cfg, clockNow)`: deterministic steps = `delta × multiplier / SecondsPerStep` with a carry. Preserved → no growth. `FarmSheepStates` ticks every 2 s. | Feeding plugs in **inside `CatchUp`** as a time-windowed multiplier. The mask is never edited directly. |
| Brush | `ShearingFeelConfig.BrushRadius = 0.085` is read directly in 6 places (`ShearingSessionController` stamp / visual ring / clumps / graph, `ShearingSurfaceMapper` stamp spacing). Payment = removed wool **volume** (`CreditRemovedWool`), not stamp count. | A per-session effective radius changes the real cut width. Payment stays volume-based, so a wider brush only makes shearing faster and never pays more for the same wool. |
| Economy | One atomic file per operation. `GameBalanceData`: 100 wool per full sheep, prices 1 / 1.2 / 1.8, start 100 coins. | Add tool levels (single-file atomic). Reuse the receipt pattern for upgrades, sheep purchases and feeding. |

**Income check (real numbers):** a fully sheared sheep gives 100 Dirty Wool = 100 coins dirty, or 180 coins washed and dried. Full regrowth takes 480 s of game time (`WoolGrowthConfig`). With 2 sheep that is ≈ 200–360 coins per ~8-minute cycle. The suggested prices (house 300, workshop 180, station 120, trimmer 150, barn 250, sheep 200, feed 20) total ≈ 1,220 coins ≈ 4–6 cycles (≈ 35–45 minutes). That is a reasonable test pace, so these values are used as defaults. A feed (×2 for 180 s) gives ≈ +37 wool ≈ 37–67 coins for 20 coins: worth it, but never required.

# Game Mechanics
## Core Gameplay Loop
1. Shear → wash/dry → sell (unchanged).
2. **Farmer House Lv 1 → 2:** you need a *completed* Wool Processing building, 300 coins and a free builder. Then Upgrade → the builder works on the house (scaffold, timer, 20 s test value) → the house becomes Lv 2 with a new look.
3. Lv 2 unlocks in the catalog: **Trimmer Workshop** (180), **Feeding Station** (120) and **Barn Lv 2** (250, upgraded from the barn sheet).
4. **Workshop:** Trimmer Lv 1 → 2 (150, instant) → +22 % cutting width in the shearing scene.
5. **Feeding Station:** pick a sheep → Feed (20) → ×2 regrowth for 180 s of game time. Preserved hairstyles cannot be fed; the UI explains why and no coins are taken.
6. **Barn Lv 2:** capacity goes from 2 to 4 → Buy Sheep (200) → a new sheep with full wool appears in the pen and can be sheared.
7. The Workshop and Feeding Station become the mandatory buildings for a future Lv 2 → 3 (Lv 3 itself is not implemented).

Motivation (SDT): autonomy (free choice of what to upgrade, nothing mandatory beyond the "build everything of your level" gate), competence (a wider trimmer and more sheep make the core loop feel better). MDA: *challenge-lite* and *fantasy* (the farm visibly grows). Bartle: Achievers get clear goals; nobody is forced into tasks.

## Controls and Input Methods
Unchanged (tap, drag, pinch; placement through the Stage 4 catalog). The new actions are buttons in the existing bottom-sheet framework. There are no extra confirmation screens; the upgrade button shows its price and is the confirmation.

# UI
```
Farmer House (bottom sheet)                 Barn & Sheep Pen
┌──────────────────────────────┐           ┌──────────────────────────────┐
│ Farmer House · Lv 1        ✕ │           │ Barn · Lv 1 · Sheep 2/2    ✕ │
│ Next: Lv 2 · 300 🪙 · 20 s    │           │ [✂ Daisy  64% · regrowing ›] │
│ Unlocks: Trimmer Workshop,   │           │ [✂ Clover 100% · ready    ›] │
│ Feeding Station, Barn Lv 2   │           │ [+ Buy Sheep  200🪙] (Barn full – upgrade the barn)
│ ✓ Wool Processing built      │           │ [⬆ Upgrade Barn → Lv 2 · 250🪙 · 4 sheep]
│ ✗ 120 more coins needed      │           │   (Needs Farmer House Lv 2)
│ [⬆ Upgrade · 300]  (disabled)│           │ [✥ Move]
│ [✥ Move]                     │           └──────────────────────────────┘
└──────────────────────────────┘
Trimmer Workshop                             Feeding Station
┌──────────────────────────────┐           ┌──────────────────────────────┐
│ Trimmer Workshop           ✕ │           │ Feeding Station · Feed 20🪙 ✕ │
│ Trimmer · Lv 1               │           │ [🌾 Daisy 40% · Feed (20) ›]  │
│ Cutting width 100% → 122%    │           │ [🌾 Clover · ×2 active 2:14 ] │
│ [⬆ Upgrade Trimmer · 150]    │           │ [🌾 Bella · Hairstyle saved – │
│  (Max level reached / Not    │           │      unlock it to feed]      │
│   enough coins)              │           │ [🌾 Mia · Full fleece]        │
└──────────────────────────────┘           └──────────────────────────────┘
```
- An upgrading building shows the existing construction sheet ("Farmer House · Upgrading to Lv 2 · 62 % · 8 s left"). The barn stays usable while it upgrades (shearing is the income and must never be blocked). Only moving is disabled.
- The top construction chip and the world label (`Farmer House (upgrading)`, `Clover ×2`) reuse the Stage 4 components. Feed / buy / upgrade give a toast, a sparkle, a sound and a haptic tick through the existing `ProcessingHapticsDriver`.
- The shearing HUD shows no new UI; the trimmer is simply wider. A level badge "Trimmer Lv 2" is added to the existing hint line.

# Key Asset & Context
### Configuration (no hardcoded numbers in UI or logic)
- `Farm/Runtime/Building/Data/FarmProgressionConfig.cs` (new SO, asset `Farm/Config/FarmProgressionConfig.asset`):
  ```csharp
  [Serializable] public struct HouseLevel { public int Level; public int UpgradeCost; public float UpgradeSeconds; [TextArea] public string Unlocks; }
  public HouseLevel[] HouseLevels;           // only entry: Lv 2 (300, 20 s). Lv 3–8 are added later as data
  public int[] BarnCapacityByLevel = {2, 4};
  public int SheepPrice = 200;  public string[] SheepNames;   // cute names for bought sheep
  public float FeedBoostMultiplier = 2f, FeedBoostSeconds = 180f; public int FeedPrice = 20;
  public int MaxVisibleSheep = 6;
  ```
- `FarmBuildingDefinition` (**extend**, keep the asset GUIDs): replace the unused `UpgradeCosts/UpgradeSeconds` with
  ```csharp
  [Serializable] public struct BuildingLevel { public int Level; public int RequiredFarmerHouseLevel; public int Cost; public float Seconds; public string Benefit; }
  public BuildingLevel[] UpgradeLevels;   // target levels 2..Max (house levels come from FarmProgressionConfig)
  public bool MandatoryForProgression = true;   // functional buildings only; decorations never count
  ```
  Barn: `UpgradeLevels = [{2, house 2, 250, 15 s, "Room for 4 sheep"}]`.
- New definitions (the Stage 4 catalog, grid placement, preview, receipts, timers and builder are reused unchanged):
  - `trimmer_workshop`: 3×3, 180, 12 s, requires house Lv 2, `BuildingInteraction.TrimmerWorkshop`
  - `feeding_station`: 3×2, 120, 10 s, requires house Lv 2, `BuildingInteraction.FeedingStation`
- `Shearing/Runtime/Config/ToolUpgradeConfig.cs` (new SO in the Shearing assembly so the shearing scene can read it): `ToolId = "trimmer"`, levels `[{1, radius ×1.00, cost 0}, {2, radius ×1.22, cost 150}]`, `RequiresBuilding = "trimmer_workshop"`, `MaxEffectiveRadius` clamp (safety against "too wide").

### Save data (each authoritative value lives in exactly one place)
| Data | Where (authoritative) | Notes |
|---|---|---|
| House level, building levels, upgrade state/timer | `farm.json` → `BuildingInstanceState` | new fields `upgradeTargetLevel`, `upgradePurchaseId`, status `Upgrading` (reuses the start/end ticks) |
| Unlocks | **derived** from house level + definitions | never stored |
| Barn capacity | **derived** from barn level + `BarnCapacityByLevel` | never stored |
| Herd (sheep ids, names, purchase ids, id counter) | `farm.json` → `herd[]`, `nextSheepNumber` | schema 3 |
| Wool mask, regrowth, hairstyle lock, **feed boost** | `sheep_<id>.json` (`SheepWoolState`) | new `feedBoostUntil` (growth-clock seconds), `feedBoostMultiplier`, `feedPurchaseId` |
| Trimmer level, coins, receipts | `economy.json` → `toolLevels[]` | single-file atomic |

- **Migration (no reset):** a schema-2 farm loads, gets `herd` seeded from `FarmLayout.Sheep` (`sheep_001` Daisy, `sheep_002` Clover → their existing wool files keep working) and `nextSheepNumber = 3`, then saves as schema 3. Missing JSON fields in economy and sheep files take neutral defaults (trimmer Lv 1, no boost). No epochs are bumped.

### Logic (pure C#, extending Stage 4)
- `BuildingService` (**extend**):
  - `BuildingStatus.Upgrading = 3`. `ActiveConstructionCount` counts `UnderConstruction + Upgrading` → **one shared builder queue**, no second queue.
  - `PlacementResult` gains `RequirementsNotMet, MaxLevel`.
  - `UpgradeInfo GetUpgrade(inst)` → next level, cost, seconds, required house level, result plus the list of missing mandatory buildings. Source: `FarmProgressionConfig` for the house, `UpgradeLevels` for others.
  - `PlacementResult CheckUpgrade(inst)`, in this order:
    1. not already upgrading / under construction
    2. a next level exists
    3. required house level
    4. house only: every `MandatoryForProgression` functional definition with `RequiredFarmerHouseLevel ≤ current` has a **Completed** instance (→ "Build Wool Processing first")
    5. no active construction
    6. coins
  - `bool TryStartUpgrade(instanceId, purchaseId, out result)`: same receipt flow as purchases (spend → save the instance as `Upgrading` with the purchase id → confirm; refund on failure; a repeated id is a no-op).
  - `Tick()` completes upgrades exactly once (`level = upgradeTargetLevel`, status `Completed`, event `LevelChanged`).
  - `Reconcile()` also matches receipts by `upgradePurchaseId`.
  - `IsFunctional` treats `Upgrading` as functional at the old level.
  - Move is blocked while upgrading.
- `HerdService` (new, `Farm/Runtime/Herd/`):
  - `Capacity` (from the barn level), `Count`, `CanBuy() → Ok | BarnFull | NotEnoughCoins | NoBarn`.
  - `TryBuySheep(purchaseId, out entry)` uses the receipt pattern: spend → add a herd entry with a new id → save farm → confirm. Startup reconciles receipts `sheep:` by `purchaseId`.
  - **Unique ids:** `sheep_{nextSheepNumber:000}`. The counter only increments and is saved in the same write as the herd entry. Ids already in the herd or with an existing `sheep_<id>.json` file are skipped (stale files after a dev reset are never reused). A new sheep has **no wool file** → full wool from the default game state, exactly like a never-sheared starting sheep. No mask is ever cloned.
- `FeedingService` (new, Farm):
  - `CanFeed(sheepId) → Ok | Preserved | FullFleece | AlreadyBoosted | NoStation | NotEnoughCoins`.
  - `TryFeed(sheepId, purchaseId)`: spend (receipt `feed:<sheep>`) → `FarmSheepStates.ApplyFeedBoost(sheepId, until, multiplier, purchaseId)` (first runs CatchUp to now, then sets the window and saves the sheep file) → confirm. Startup reconcile: the receipt is confirmed if that sheep file holds the same `feedPurchaseId`, otherwise refunded.
- `SheepWoolLifecycle.CatchUp` (**extend**, still deterministic): the delta interval `[stamp, now]` is split at `feedBoostUntil`. The boosted part grows × `feedBoostMultiplier`. Preserved → no growth and no boost (feeding is refused earlier anyway). `SecondsToFull` accounts for the remaining boost. EditMode test: 1 × 300 s equals 300 × 1 s with a boost window inside.
- `WoolEconomy` (**extend**): `int ToolLevel(string toolId)`; `bool TryUpgradeTool(toolId, fromLevel, toLevel, cost)` is atomic, idempotent (only if the current level == fromLevel) and needs no receipt because it is a single file.

### Shearing integration
- `ShearingSessionController`:
  - In `Awake`, compute `_brushRadius = feel.BrushRadius × toolConfig.Multiplier(economy.ToolLevel("trimmer"))`, clamped to `MaxEffectiveRadius`.
  - Replace the 6 direct `_feel.BrushRadius` reads with `_brushRadius`, so the stamp, visual cut ring, clumps, attachment graph and mapper spacing (via a new `ShearingSurfaceMapper.SetBrushRadius`) all use the same value.
  - Stamp spacing stays a fraction of the radius, so no gaps and no extra stamps per second.
  - Haptics read cut events (`CutDelta` area), so they scale naturally and nothing else changes.
- `TrimmerViewController.SetHeadWidth(float multiplier)`: widens the blade/head mesh horizontally to match the cut width, so what you see matches what is cut.

### Presentation
- `IBuildingView.SetLevel(int)` + `BuildingView` `_levelRoots[]` (one model root per level; Stage groups belong to the Lv 1 root for the first build).
  - Upgrade visual: the existing `ConstructionSiteView` (scaffold, dust) and the builder are assigned to the upgrading building.
  - On completion: swap the level root + pop + sparkle.
  - Footprint and position never change.
- `FarmBuildingDirector`: the site/builder sync uses `ActiveConstruction` (now also returns upgrading instances). New events `UpgradeStarted / LevelChanged`.
- Interactions (new): `TrimmerWorkshopInteraction`, `FeedingStationInteraction`. **Extended:** `HouseInteraction` (progression sheet), `BarnInteraction` (capacity, buy, upgrade).
- `FarmPanels`: `ShowHouseProgress`, `ShowBarnHerd`, `ShowWorkshop`, `ShowFeeding` built from the existing `ShowActions` / `ActionRow` helpers. Text updates on economy or building events only.
- **Herd views:** `FarmHerdPresenter` spawns a `FarmSheep.prefab` (FarmSheepView + SheepInteraction + hit area; generated by the scene builder) per herd entry, up to `MaxVisibleSheep`. Every sheep is always listed in the barn sheet. New sheep pop in with a sparkle. The pen rect follows the barn (Stage 4). The barn Lv 2 pen grows inside the same footprint (pen fence variant).
- **Feeding feedback:** the sheep does a short in-place eating animation (head down nibble, 2 s, its `FarmSheepView` idle code) plus a small hay tuft that fades, a green sparkle, a haptic tick and a "×2" suffix on its label while boosted. No pathfinding.
- Art (procedural placeholders, replaceable):
  - Farmer House Lv 2: bigger body, dormer window, chimney with smoke puff mesh, porch roof, flower boxes
  - Barn Lv 2: hayloft door, weathervane, extra hay, wider pen
  - Workshop: shed with a big trimmer sign, workbench, gear sign
  - Feeding Station: hay rack with roof and troughs
  - Catalog icons rendered as in Stage 4

# Implementation Steps
1. **Config & definitions:** `FarmProgressionConfig`, `BuildingLevel`/`UpgradeLevels`/`MandatoryForProgression`, `ToolUpgradeConfig`, new `BuildingInteraction` values. *Dependency: none.*
2. **Save schema 3 + migration:** herd, counter, instance upgrade fields; schema 2 → 3 (seed herd from the layout); new sheep / economy fields with defaults. *Dependency: 1.*
3. **Generic building upgrades:** `Upgrading` status, `GetUpgrade/CheckUpgrade/TryStartUpgrade`, completion in `Tick`, reconcile, shared queue, move block. **EditMode tests:** house gate (missing processing, under construction, not enough coins, busy builder), charge once, repeated id, restart mid-upgrade, offline completion, level kept after reload, barn functional while upgrading. *Dependency: 2.*
4. **Herd & sheep purchase:** `HerdService` (capacity, unique ids, receipts) + tests (full barn, coins, unique ids incl. stale files, reload, crash → refund). *Dependency: 2, 3.*
5. **Feeding:** lifecycle boost window, `FarmSheepStates.ApplyFeedBoost`, `FeedingService` + tests (determinism, preserved refused without charge, boost expiry, reconcile). *Dependency: 2.*
6. **Trimmer upgrade:** `WoolEconomy` tool levels, effective brush radius in the session + mapper, trimmer head width + tests (radius multiplier read, payment per removed volume unchanged for the same cut area, idempotent purchase). *Dependency: 1.*
7. **Views & scene builder:** level roots (house/barn Lv 2), workshop and station prefabs, `FarmSheep.prefab`, `FarmHerdPresenter`, definitions/catalog/config assets, icons, rebuild FarmHub (sheep no longer baked). *Dependency: 3–6.*
8. **UI:** house progression sheet, barn herd sheet (buy/upgrade/shear), workshop sheet, feeding sheet, feedback (toast, sparkle, haptic, sound), labels. *Dependency: 7.*
9. **Director/hub wiring:** upgrade site and builder, level swap, herd spawn after purchase, new sheep → `StartShearing(id)`, reconcile of all receipt types on start. *Dependency: 8.*
10. **Validation:** EditMode suite, Play Mode end-to-end run (below), validators (catalog consistency incl. levels, no save files in Assets), console clean, perf notes. Editor saves are backed up and restored, and the domain reload setting is restored. *Dependency: all.*

# Verification & Testing
**EditMode (automated):** everything listed in steps 3–6, plus the existing 21 Stage 4 tests (no regressions). Migration: a schema-2 `farm.json` with the processing building loads into schema 3 with the same instances, levels and positions, plus a herd of `sheep_001/002` and an unchanged economy.

**Play Mode (scripted, fresh save + one run on a migrated schema-2 save):**
1. House: Upgrade is disabled with "Build Wool Processing first". Build the processing building, then "Not enough coins". DEV coins → Upgrade (double tap) → charged once, builder + scaffold, catalog purchase blocked "builder busy", barn still opens and shearing can start, completion → Lv 2, visual swapped, catalog shows Workshop and Station unlocked.
2. Workshop: build via the catalog → sheet → Upgrade Trimmer (double tap) → Lv 2 once. Shearing scene: effective radius = 0.085 × 1.22, cut area per stamp ≈ +49 %, wool paid = removed volume (same total for a fully sheared sheep).
3. Feeding: build → feed a partly sheared sheep → coins −20, boost active, regrowth over 10 s ≈ 2 × the unfed rate. A preserved sheep is refused and no coins are taken. A full sheep is refused.
4. Barn: "Barn full 2/2", upgrade → 2/4 → buy a sheep (double tap) → one new `sheep_003` at full wool, visible in the pen, can be sheared → its own wool file. Hairstyle lock works for it. Buying a 5th → refused ("Barn full").
5. Reload the scene: levels, herd, boost, trimmer level and an upgrade timer that is still running all persist. No duplicates, no second charge.
6. Regressions: wash, dry, sell, regrowth, labels, camera angle, haptics calls; console has no errors.

**Device (not possible here, will be listed explicitly):** heat/FPS with 4 sheep and the new buildings, feel of the wider trimmer, haptics on Android/iOS.

**Known limitations (to report):** procedural placeholder art, Lv 3–8 content absent by design, the feed boost keeps counting while the sheep is on the shearing table (growth is paused there by design), the device clock is trusted for building timers, and only one trimmer upgrade level.
