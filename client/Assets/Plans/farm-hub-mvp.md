# Shear & Grow — Farm Hub MVP (the farm becomes the game's home screen)

# Project Overview
- **Game Title:** Shear & Grow
- **High-Level Concept:** Cozy mobile game about running a small sheep farm. The player taps the buildings and sheep of their farm to shear, wash, dry and sell wool — the existing satisfying mini-games now live inside a small, living farm world.
- **Players:** Single player
- **Inspiration / Reference Games:** Clash of Clans (fixed-angle orthographic camera, free drag-pan + pinch-zoom anywhere, tap buildings → contextual panel), Hay Day (cozy farm readability)
- **Tone / Art Direction:** Stylized family 3D, soft rounded shapes, warm palette, soft lighting, clear silhouettes, matching the existing puff-wool sheep and barn materials. A small, tidy, cute farm with sheep — not a realistic village sim.
- **Target Platform:** Android + iOS (iPhone 14 Pro Max is a test device)
- **Screen Orientation / Resolution:** Portrait, reference 1080×1920, safe-area aware (verified at 1290×2796)
- **Render Pipeline:** URP (Unity 6000.6.8f1, IL2CPP, New Input System), Mobile_RPAsset on device

## Audit summary (what exists, what is reused)
| Existing | Finding | Use in Farm Hub |
|---|---|---|
| Scenes | Build order: `Hub` (temporary menu) → `Shearing_Feature_Test` → `WoolWash` → `WoolDry`. All mini-games return via `SceneNavigator.Go(SceneNavigator.Hub)` | New `FarmHub` scene becomes build index 0 and the single return target |
| `SceneNavigator` (Core) | Scene names + async single-scene load, double-tap guard | Becomes the farm-aware navigator: home = FarmHub, plus a one-shot **launch context** (which sheep to shear, which farm panel to reopen on return) |
| `WoolEconomy` / `EconomyStore` | Coins + Dirty/Wet/Clean wool, atomic saves, events | Farm HUD and building panels bind to `WoolEconomy.Changed` (no polling) |
| `SheepWoolStateStore` + `ShearingSessionController` | Per-sheep JSON saves. `creditedCutArea` = highest committed unique cut fraction, `completionFlags` = finished. Shearing scene has **one** `SheepRigBuilder` with a hard-coded `sheep_001` | Farm reads each sheep's state cheaply (small JSON, no mask decode) to show remaining wool. Shearing gets a runtime sheep-id override → a second sheep `sheep_002` works with its own save and payment ledger |
| `SellPanel`, `UiKit`, `ProceduralIcons`, `ProcessingHapticsDriver`, `ProcessingAudioLibrary` (Processing asm) | Working sell flow with coin-fly, sound and haptics; uGUI-from-code palette | Reused directly by the trader stall (no duplicate sell code) |
| `WoolPuffMeshBuilder`, `SoftCharacter` shader, barn materials | Cartoon puffs, soft lit shader with `_RimColor/_RimStrength` | Farm sheep wool, buildings and the tap highlight (rim boost via MaterialPropertyBlock — no post effects) |
| `ProceduralMeshFactory` | Ellipsoid / cylinder / box / combine | Procedural placeholder buildings, fences, trees (static-batched) |
| Layers | Only built-in layers | New layer **`FarmInteractable`** for tap hit-areas |
| Old `Hub` scene | Temporary menu | Kept in the project as a dev tool, **removed from the build** (user decision) |

**User decisions:** orthographic camera; Clash-of-Clans-style camera (free drag-pan anywhere + pinch/wheel zoom toward the fingers, clamped to the farm bounds, angle never changes); two sheep (`sheep_001`, `sheep_002`); old menu kept as a dev tool.

# Game Mechanics
## Core Gameplay Loop
1. The game starts on the farm. The player sees the farmer house, the sheep pen with two grazing sheep (their wool visibly fuller or thinner depending on their saved shearing state), the wool-processing shed and the trader stall.
2. **Tap a sheep** (or the barn → pick a sheep) → fade → the existing shearing scene loads with that sheep → Back / Done → fade → back on the farm with a "+N Dirty Wool" toast, and that sheep now looks sheared.
3. **Tap the processing shed** → panel: *Wash Wool* (needs Dirty) / *Dry Wool* (needs Wet), with amounts and reasons when disabled → existing mini-game → back on the farm.
4. **Tap the trader** → the existing sell panel (Dirty/Wet/Clean, prices, Sell Selected / Sell All, coin-fly into the HUD coin counter).
5. **Tap the farmer house** → info card (name, level 1, "the heart of your farm — upgrades coming soon").
6. Repeat; regrow a finished sheep from inside the shearing scene (existing feature).

Engagement (MDA aesthetics: fantasy + expression): the world reflects the player's progress (sheared sheep, stocks shown on the HUD, the trader's coins flying home), reinforcing "my little farm" ownership (SDT autonomy/competence). Bartle: caters to Achievers/Explorers with a pannable world that can grow.

## Controls and Input Methods
Clash-of-Clans-style touch controls (New Input System; mouse + wheel in the Editor):
- **1-finger drag** = pan (the camera moves on the ground plane under the finger, 1:1 at any zoom), with light inertia after release, clamped to the farm bounds (soft edge spring-back).
- **2-finger pinch** = zoom toward the midpoint of the fingers (the point under the fingers stays put); **mouse wheel** zooms toward the cursor. Clamped min/max orthographic size; smooth.
- **Tap** (press + release under 12 px movement and 0.35 s) = select the object under the finger (raycast against the `FarmInteractable` layer only). A tap on empty ground closes open panels.
- No rotation and no tilt, ever — only position and orthographic size change.
- Touches starting on UI never move the camera or select objects. A second finger cancels a pending tap.

# UI
```
┌──────────────────────────────────┐
│ 🪙 1 240   [🟤 120][💧 40][⚪ 60] ⚙│  top HUD: coins + three wool chips + settings
│                                  │
│        (orthographic farm)       │
│   🏠 house      🏚 barn+pen 🐑🐑  │  world labels float above buildings:
│                                  │   "Barn", "Wool Shed  ●2" (badge when an
│   🛁 wool shed     ⛺ trader      │    action is available), "Trader"
│                                  │
│      (free grass for future)     │
│                    toast: +35 Dirty Wool
│ ┌──────────────────────────────┐ │  bottom sheet (only while a building
│ │ Wool Shed                  ✕ │ │   is selected; slides up, max ~40% of
│ │ [💧 Wash Wool   120 dirty ▸] │ │   the screen, the selected building
│ │ [🌬 Dry Wool    Wash first  ] │ │   is auto-framed above it)
│ └──────────────────────────────┘ │
└──────────────────────────────────┘
```
- **Top HUD:** compact bar inside the safe area. Chips pulse when their value changes. ⚙ opens a small settings popover (Sound, Haptics; in Editor/Development builds also DEV +100 Dirty/Wet and DEV Reset Save). No action buttons — all actions go through the world.
- **World labels:** small name tags (pooled uGUI, world-to-screen, updated only when the camera moves), with a badge dot when something can be done there (Dirty/Wet available at the shed, wool to sell at the trader, a sheep with wool in the pen). They hide when zoomed far out.
- **Bottom sheet panels (one at a time):**
  - *Barn:* list of sheep: name, wool bar (remaining %), status ("Ready to shear" / "Partly sheared 35%" / "Sheared — regrow inside"), button *Shear ▸*.
  - *Wool Shed:* *Wash Wool* / *Dry Wool* with amounts; disabled buttons show the reason ("No dirty wool — shear a sheep first").
  - *Trader:* the existing `SellPanel` opened as an overlay.
  - *Farmer House:* title, "Level 1", short placeholder text, a greyed "Upgrade (coming soon)".
- **Selection feedback:** a squash-and-stretch pulse on tap, a warm rim-light boost on the building's materials (MaterialPropertyBlock), a soft ground ring decal under the selection, a tap sound and a light haptic tick.
- **Transitions:** a full-screen fade (0.25 s out / 0.35 s in) shared by all scenes; the farm restores the camera position, zoom and reopens the relevant panel when returning (e.g. Dry "Sell ▸" lands on the farm with the trader panel open).

# Key Asset & Context
### Assemblies (no cycles): `Core` ← `Shearing` ← `Processing` ← `Farm`
**Core (`Assets/ShearAndGrow/Core/Runtime`)**
- `SceneNavigator.cs` (modify): `Home = "FarmHub"`; `Hub` constant becomes an alias of `Home` (every existing "return to hub" call now lands on the farm with no further edits); old menu name kept as `DevMenu = "Hub"`.
- `LaunchContext.cs` (new): static one-shot handoff that survives a single scene load (not persisted):
  ```csharp
  public static class LaunchContext {
      public static string SheepId;            // shearing: which sheep (null = scene default)
      public static string ReturnPanel;        // farm: building id to reopen on return (e.g. "trader")
      public static Vector3? FarmCameraPos; public static float FarmCameraZoom; // restore view
      public static string ConsumeSheepId(); public static string ConsumeReturnPanel();
  }
  ```

**Shearing (minimal changes)**
- `SheepRigBuilder.cs`: `public void OverrideIdentity(string sheepId)` (only before `Build`).
- `ShearingSessionController.cs`: in `Awake`, before `_sheep.Build()`: `var id = LaunchContext.ConsumeSheepId(); if (!string.IsNullOrEmpty(id)) _sheep.OverrideIdentity(id);`. Everything else (per-sheep save, payment ledger, regrow) already keys on `SheepId`.
- `DefaultGameState.asset`: `StartingSheepIds = { "sheep_001", "sheep_002" }`.

**Processing (minimal changes)**
- `WoolProcessingSession.OnContinue`: Dry "Sell ▸" sets `LaunchContext.ReturnPanel = "trader"` before going home.
- `SellPanel`: unchanged (reused).

**Farm (new assembly `Assets/ShearAndGrow/Features/Farm/Runtime/ShearAndGrow.Farm.asmdef`, refs Core, Shearing, Processing, InputSystem, UGUI)**
*Data (no scene/visual dependencies — what a future building/grid system extends):*
- `FarmBuildingDefinition.cs` (SO): `Id`, `DisplayName`, `Description`, `Kind` (House, Barn, ProcessingShed, Trader, Decoration), `Footprint` (Vector2Int, grid cells), `StartLevel`, `MaxLevel`, `Icon`.
- `FarmLayout.cs` (SO): grid cell size (1 m), farm bounds (in cells), list of `PlacedBuilding { FarmBuildingDefinition def; Vector2Int cell; int rotationSteps; string viewKey; }` and sheep spawns `{ sheepId; Vector2Int penCell; }`. Positions are logical (grid), not scene transforms.
- `FarmState.cs` + `FarmStateStore.cs`: versioned atomic `farm.json` (Editor/Player folders like the other saves): placed buildings + levels (seeded from `FarmLayout` for a new player), last camera view. Ready for construction/upgrades later.
- `FarmSheepInfo` (struct) via `FarmSheepRoster.cs`: for each sheep id → `remainingWool = 1 − creditedCutArea`, `finished` flag, `pendingUnpaid` (none at rest); read from `SheepWoolStateStore` (small JSON, no mask decode) once on scene start / return.

*Interaction (decoupled from models):*
- `FarmInteractable.cs` (abstract MonoBehaviour): `BuildingId`, `HitArea` (BoxCollider on the `FarmInteractable` layer, separate child object), `ViewRoot` (any visual: 3D today, sprite tomorrow), `OnSelected(FarmContext)`, `OnDeselected()`, `HasAttention` (badge). The hit area and the view are independent → swapping the 3D model for a 2.5D sprite keeps the interaction.
- `HouseInteraction.cs`, `BarnInteraction.cs`, `SheepInteraction.cs`, `ProcessingShedInteraction.cs`, `TraderInteraction.cs`: open their panel / start their flow through `FarmHubController`.
- `FarmSelectionFeedback.cs`: pulse (scale on the `ViewRoot`), rim boost via `MaterialPropertyBlock` (`_RimStrength`, `_RimColor`) on the view's renderers, a ground ring (one pooled quad), tap sound + haptic tick.

*Camera & input:*
- `FarmInputRouter.cs`: one place that turns touch/mouse into **tap / drag / pinch / wheel** (tap thresholds, UI-blocking, second-finger cancel, focus/pause release).
- `FarmCameraController.cs`: orthographic, fixed rotation (default 35° pitch, 45° yaw — classic isometric; serialized, never changed at runtime). `orthographicSize` zoom between min/max (fit-whole-farm at max, building-close-up at min, aspect-aware for portrait); zoom toward the screen point; pan by ground-plane deltas (1:1 under the finger), inertia with exponential decay, soft clamping of the camera's ground focus to the farm bounds (expanded by the visible area so edges are reachable), `FocusOn(Bounds)` (smooth auto-frame of a selected building above the bottom sheet), Save/Restore view. Pure transform math, no physics.

*Presentation:*
- `FarmHubController.cs`: composition root — initialises the economy wiring, FarmState, roster, camera, input, interactables, HUD; routes taps to interactables; starts scene transitions (`Go(scene)` with fade + saving the camera view + `LaunchContext`), reopens `ReturnPanel`, shows "+N Dirty Wool" (`WoolEconomy.ConsumeUnseenShearedWool`).
- `FarmHUD.cs`: top bar (coins + three chips + settings popover + dev tools), toast, fade overlay.
- `FarmPanels.cs`: bottom-sheet framework + `BarnPanel`, `ShedPanel`, `HousePanel` builders (UiKit), trader → `SellPanel`.
- `FarmWorldLabels.cs`: pooled name tags + attention badges, updated on camera move only.
- `FarmSheepView.cs`: light farm sheep (≈1.5k verts: soft body, head, legs + a low-puff wool cluster from `WoolPuffMeshBuilder` scaled by remaining wool; a bald-pink variant when sheared). Idle: breathing bob, occasional head turn, slow wander inside the pen rectangle (simple lerp to random points, no NavMesh/physics).
- `FarmAmbience.cs`: soft ambience loop (procedural birds/breeze placeholder), tap/open sounds; shares the sound setting.

*Editor:*
- `Features/Farm/Editor/FarmSceneBuilder.cs` — menu **Shear & Grow ▸ Farm ▸ Build Farm Hub Scene**: creates the definitions, `FarmLayout`, materials, the procedural farm (ground with a path, fenced pen, house, barn, wool shed with tub and drying rack, trader stall with awning and crates, trees, bushes, flowers, hay bales; static-batched), hit areas, one directional light (single soft shadow cascade) + trilight ambient, the existing volume profile, adds the `FarmInteractable` layer, saves `Assets/ShearAndGrow/Features/Farm/Scenes/FarmHub.unity`, and sets the build order **FarmHub, Shearing, WoolWash, WoolDry** (old `Hub` removed from the build, file kept).

### Assets created
`Farm/Config/FarmLayout.asset`, `Farm/Config/Buildings/{FarmerHouse, Barn, WoolShed, Trader}.asset`, `Farm/Materials/*` (grass, path, roof red/blue, wood, plaster, canvas, leaves, flowers, fence), `Farm/Scenes/FarmHub.unity`. All art is procedural placeholder (listed in the report).

# Implementation Steps
1. **Core navigation & launch context** — `LaunchContext.cs`; `SceneNavigator` home/alias/dev-menu constants. *Dependency: none.*
2. **Shearing & processing hooks** — `SheepRigBuilder.OverrideIdentity`, `ShearingSessionController` reads `LaunchContext.SheepId`; Dry "Sell ▸" sets `ReturnPanel`; `DefaultGameState` starting sheep `sheep_001`, `sheep_002`. *Dependency: 1.*
3. **Farm data layer** — asmdef; `FarmBuildingDefinition`, `FarmLayout`, `FarmState`, `FarmStateStore`, `FarmSheepRoster`; EditMode tests (new player seeding, save/load, roster for unsheared / partial / finished sheep). *Dependency: 1.*
4. **Camera & input** — `FarmInputRouter`, `FarmCameraController`; deterministic EditMode simulation: pan 1:1, zoom-to-point invariance, bounds clamp, inertia decay, fit-whole-farm at max zoom for portrait, angle unchanged after any input. *Dependency: 3.*
5. **Farm world (scene builder)** — procedural buildings/props/sheep views, materials, lighting, hit areas, layer, `FarmHub.unity`, build order. Render top-down portrait screenshots and iterate on readability/coziness. *Dependency: 3, 4.*
6. **Interactions** — `FarmInteractable` + 5 interactions, `FarmSelectionFeedback`, `FarmWorldLabels`, `FarmSheepView` (state-driven wool, idle wander). *Dependency: 5.*
7. **HUD & panels** — `FarmHUD`, `FarmPanels` (Barn/Shed/House), trader → `SellPanel`, settings popover + dev tools, toasts, fade. *Dependency: 6.*
8. **Flow integration** — `FarmHubController` transitions with fade, camera view save/restore, `ReturnPanel` reopen, sheared-wool toast; every mini-game's exit lands on the farm. *Dependency: 2, 7.*
9. **Play Mode validation** (bootstrap runner; Editor saves backed up/restored, domain reload temporarily enabled): full loop from the farm (see Verification); portrait screenshots at 1290×2796. *Dependency: 8.*
10. **Mobile check** — compile player scripts iOS/Android (release + dev), scene stats (verts, batches, lights, shadow casters), GC per frame in farm idle/pan, save-data validator; device checklist. *Dependency: 9.*

# Verification & Testing
**EditMode (automated)**
- FarmState: new player seeded from FarmLayout (4 buildings, house level 1); save → load round trip; corrupted file → backup.
- Roster: no save → 100% wool; save with `creditedCutArea = 0.35` → 65%; finished flag → "sheared"; `sheep_002` independent from `sheep_001`.
- Camera simulation (portrait aspect): world point under a drag stays under the finger (error < 1 px); pinch zoom keeps the midpoint fixed; zoom clamped to min/max; focus never leaves the bounds; inertia stops < 1 s; rotation identical before/after 1000 random inputs; max zoom-out shows all four buildings.
- Input: tap vs drag thresholds, UI-blocked touches ignored, second finger cancels a tap.

**Play Mode (automated, Editor)**
- Game starts in FarmHub (build index 0); HUD shows the coins and wool from the save.
- Tap `sheep_002` → shearing loads with `SheepId == sheep_002` → shear ~30% → Back → farm: +30 Dirty, toast shown, sheep_002 wool visibly reduced, sheep_001 untouched; reload → same.
- Shed panel: Wash enabled only with Dirty, Dry only with Wet; Wash → complete → farm: Wet increased; Dry "Sell ▸" → farm with the trader panel open.
- Trader: Sell All → coins credited once (double tap), stocks 0; empty sell panel doesn't break.
- House: info card opens; tap on empty ground closes panels.
- Camera: scripted drag/pinch/wheel; angle constant; returning from a mini-game restores the camera view.
- No errors in the log; no duplicated wool or coins across 3 round trips.

**Builds & performance:** player scripts compile for iOS + Android (release + development); farm stats (target: < 60k verts, < 60 batches with static batching, 1 realtime light, 1 shadow cascade); 0 GC alloc per frame while idle/panning; save-data validator passes.

**Manual on device (not possible here, listed explicitly):** FPS and heat after 10 minutes on the farm + full loop on Android and iPhone 14 Pro Max; pan/zoom feel (inertia, pinch precision); tap accuracy on small sheep; label legibility; transitions; app kill → relaunch lands on the farm with the same resources and sheep states.

**Known limitations to report:** procedural placeholder art and audio (proper building models / 2.5D renders, sheep farm model, ambience recordings needed); farmer-house levels are display-only; no construction, upgrades, workers or trader logistics yet (by design).
