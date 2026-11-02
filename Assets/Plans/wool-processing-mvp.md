# Shear & Grow — Wool Processing MVP (Shear → Wash → Dry → Sell)

# Project Overview
- **Game Title:** Shear & Grow
- **High-Level Concept:** Cozy mobile 3D game built around tactile, satisfying hand interactions. The player shears a fluffy cartoon sheep, then washes and dries the wool by hand, and sells it at any stage for Coins.
- **Players:** Single player
- **Inspiration / Reference Games:** Satisfying/ASMR mobile games (power-wash, cleaning, grooming); Brawl Stars–level haptic responsiveness (from the Haptics 2.0 brief)
- **Tone / Art Direction:** Stylized family 3D animation; soft rounded forms, warm palette, cozy barn lighting, "popcorn puff" cartoon wool matching the existing sheep (Documentation/References)
- **Target Platform:** Android + iOS (iPhone 14 Pro Max is a test device)
- **Screen Orientation / Resolution:** Portrait, reference 1080×1920, safe-area aware (tested at 1290×2796)
- **Render Pipeline:** URP (Unity 6000.6.8f1, IL2CPP, New Input System)

## Audit summary (what exists and will be reused)
| Existing system | Finding | Reuse |
|---|---|---|
| `ShearingSessionController` + `ShearMaskAuthority` | Authoritative unique cut fraction; `CommitState()` credits only the new delta (`creditedCutArea`) on Back / Finish / Pause / Quit. No resource/economy exists (only an unused `creditedYield`). | Hook Dirty Wool crediting into `CommitState()` |
| `SheepWoolStateStore` | Atomic JSON (tmp + replace), Editor/Player save separation, legacy migration, `SaveEpoch` | Same pattern for the economy save |
| Haptics 2.0 (`HapticsManager`, `IHapticsPlatform`, `IOSHaptics` Core Haptics `.mm`, `AndroidHaptics` `.java`, `NullHaptics`) | Platform-agnostic manager: contact / continuous texture / accent / completion, rate limits, pause/focus stop, enable setting in PlayerPrefs | Reused as-is with **new Wash and Dry `HapticsConfig` + profiles** (no competing system) |
| `ShearingAudioDirector`, `ProceduralAudioLibrary` | Pooled AudioSources, procedural placeholder clips, `ShearAndGrow.SoundEnabled` pref | Same pattern; new procedural water/air clips |
| `WoolPuffMeshBuilder`, `WoolShell` puff shading | Reference-style cartoon puffs | Heap builder + new shader follow the same look |
| `ShearingHUD` (uGUI built in code, legacy `Text`, safe area, warm palette) | Project UI convention | New screens follow the same uGUI-from-code pattern |
| `GraphicsQualityConfig` | Low/Medium/High presets, mobile preset | Heap density per tier |
| Scenes | Only `Shearing_Feature_Test` (+ unused SampleScene) | Shearing scene kept; 3 new scenes |
| Assemblies | `ShearAndGrow.Shearing` (+ Editor) | New `ShearAndGrow.Core` and `ShearAndGrow.Processing` |

**User decisions:** separate scenes; 100 Dirty Wool per fully sheared sheep (proportional, credited once); English UI (the shearing HUD is translated too for consistency).

# Game Mechanics
## Core Gameplay Loop
1. **Hub** → *Shear Sheep* → existing manual shearing. On Back / Finish / app pause, newly sheared, not-yet-paid wool is credited as **Dirty Wool** (100 × unique cut fraction, per sheep and per wool cycle, exactly once).
2. **Hub** → *Wash Wool* (needs Dirty Wool) → the whole Dirty batch lies as a big dirty heap on a wooden tub with a grate. The player sprays it with a hose nozzle; dirt, dust and straw wash away locally, and the wool turns darker, glossy and clumps together. At 90% unique cleaned area the batch converts **Dirty → Wet**.
3. **Hub** → *Dry Wool* (needs Wet Wool) → the wet heap lies on a drying rack. The player "paints" fluffiness with a hair dryer: the wool lightens, loses its gloss, puffs separate and gain cauliflower volume, droplets fly off, a little fluff floats up. At 90% the batch converts **Wet → Clean**.
4. **Hub** → *Sell Wool* → sell any category (Dirty ×1.0, Wet ×1.2, Clean ×1.8 of the base price) for Coins, with a coin-fly animation, a clink sound and a haptic confirmation.

Engagement (MDA, aesthetics = sensation + submission): every finger movement shows an immediate local transformation (clean/dark-wet, wet/fluffy-white), synchronised with sound and haptics. The value multipliers reward finishing the chain (SDT competence) without forcing it (autonomy: sell early).

## Controls and Input Methods
- **Shearing:** unchanged (drag on the sheep = cut, drag elsewhere = orbit, pinch zoom, belly roll).
- **Wash / Dry:** fixed 3/4 top-down camera. Touch/mouse down = tool on (water / air); drag = move the stream; release = immediate stop of stream, sound and haptics. Multi-touch and touches over UI are ignored. The tool follows the finger with smoothing and an offset toward the camera, so the worked spot stays visible; processing happens at the real finger hit (responsive). Stamps are interpolated between frames (no gaps on fast swipes). No processing when the stream misses the wool.
- **Hub / Sell:** large buttons with icons, amounts, and disabled states.

# UI
**Hub (portrait)**
```
┌──────────────────────────────┐
│  🪙 1 240        [🔊] [📳]    │  coins + sound/vibration toggles
│ ┌──────┐ ┌──────┐ ┌──────┐   │
│ │dirty │ │ wet  │ │clean │   │  resource chips (icon + amount)
│ │ 120  │ │  0   │ │ 300  │   │
│ └──────┘ └──────┘ └──────┘   │
│   (static 3D showcase: three │
│    small wool piles on table)│
│ ┌──────────────────────────┐ │
│ │ ✂  Shear Sheep           │ │
│ ├──────────────────────────┤ │
│ │ 💧 Wash Wool      120 ▸  │ │  disabled + greyed when 0
│ ├──────────────────────────┤ │
│ │ 🌬 Dry Wool         0 ▸  │ │
│ ├──────────────────────────┤ │
│ │ 🪙 Sell Wool             │ │
│ └──────────────────────────┘ │
│  [DEV +100 Dirty] [DEV Reset]│  development builds only
└──────────────────────────────┘
```
**Sell panel (overlay in Hub)**
```
┌──────────────────────────────┐
│ Sell Wool                 ✕  │
│ [✓] Dirty  120 × 1.0 → 120 🪙│
│ [ ] Wet      0 × 1.2 →   0 🪙│
│ [✓] Clean  300 × 1.8 → 540 🪙│
│      Total: 660 🪙           │
│ [ Sell Selected ] [Sell All] │
└──────────────────────────────┘
```
**Wash / Dry HUD:** top-left `← Back`; small top-centre progress pill `Washed 63%` with a 90% tick; first-use hint (finger icon + "Hold & drag to wash"); result card on completion: `+120 Wet Wool` → `[Dry now] [Menu]`. Leaving early opens a confirm dialog: "Leave? Progress resets — your wool stays." `[Stay] [Leave]`.
**Shearing:** existing HUD, English strings. Back now saves, credits and returns to the Hub, with a toast `+37 Dirty Wool`. The completion panel shows the wool earned plus `[Regrow & shear again] [Return to sheep] [Menu]`.
Icons are generated procedurally at runtime (scissors, droplet, air swirl, coin) — placeholders.

# Key Asset & Context
### Assemblies (no cycles): `Core` ← `Shearing` ← `Processing`
- **`Assets/ShearAndGrow/Core/ShearAndGrow.Core.asmdef`** (namespace `ShearAndGrow.Economy`, no dependencies)
  - `WoolType.cs` — `enum WoolType { Dirty, Wet, Clean }`
  - `GameBalanceData.cs` (SO) — `WoolPerFullSheep=100`, `BaseWoolPrice=1`, `PriceMultiplier[3]={1.0,1.2,1.8}`, `StartingCoins=0`, `EconomySaveEpoch=0`, `int SellValue(WoolType t, int amount)` (floor).
  - `PlayerEconomyState.cs` (serializable) — `schemaVersion, saveEpoch, coins (long), dirty, wet, clean (int), List<SheepWoolLedger>{sheepId, cycleId, creditedFraction, givenUnits}, lastOperationId, createdUtc, lastAppVersion, platform`.
  - `EconomyStore.cs` — atomic JSON at `persistentDataPath/ShearAndGrow/{Saves|EditorSaves}/economy.json`; keeps `.bak`, falls back to it if the main file is unreadable; epoch filter; `DeleteAll()` (dev).
  - `WoolEconomy.cs` — runtime service: `Instance`, `Initialise(GameBalanceData)`, `event Changed`, `Get(WoolType)`, `Coins`,
    - `int CreditShearing(string sheepId, string cycleId, float uniqueCutFraction)` → `units = floor(fraction*WoolPerFullSheep) - givenUnits`, never negative, saved once.
    - `bool TryBeginBatch(WoolType input, out WoolProcessingBatch batch)` (reserves the whole available amount; future: amount parameter).
    - `bool CompleteBatch(WoolProcessingBatch b)` — idempotent (`b.Completed`), atomic `-input +output`, one save.
    - `void CancelBatch(b)` — releases the reservation, no change.
    - `int Sell(SellSelection sel)` → coins; guarded against re-entry; one save.
  - `WoolProcessingBatch.cs` — `Id (Guid), Input, Output, Amount, Completed, Cancelled`.
  - `SceneNavigator.cs` — scene names (`Hub`, `Shearing_Feature_Test`, `WoolWash`, `WoolDry`), `Go(string)` async load, `IsInBuild(name)`.
  - `EconomyBootstrap.cs` (MonoBehaviour, execution order -200) — present in every scene, initialises `WoolEconomy` once with its `GameBalanceData` reference.
- **`Assets/ShearAndGrow/Features/Processing/Runtime/ShearAndGrow.Processing.asmdef`** (references Core, Shearing, InputSystem, UGUI)
  - `ProcessingConfig.cs` (SO, one asset each for Wash/Dry) — stage, completion threshold 0.9, brush radius, dose per second, minimum sweep dose, accent step (+6% → accent), heap size, puffs per quality tier, colors (dirty / dirt-spot / wet / dry), shape params (wet shrink, clump pull, dry fluff), audio volumes, `HapticsConfig` reference.
  - `WoolHeapBuilder.cs` — procedural heap of overlapping lumpy puffs on a mound (2–3 layers, puff radius 3.5–6 cm, ~300–420 puffs by tier). Vertex data: `uv0` = footprint UV of the puff centre, `uv1` = (radius, seed), `uv2` = puff centre (OS), `uv3` = cluster centre (OS, k-means groups of ~6 → wet clumping), `color.r` = AO. Also outputs a 96×96 **heightfield** (top surface) used for raycasts and coverage weights.
  - `HeapStateMask.cs` — CPU-authoritative R8 mask (96×96, texel = footprint cell). `Stamp(segment, radius, dose)` with seam-free falloff; per-texel value 0→1 (cleanliness or dryness); `UniqueProgress` = Σ weight of texels ≥ completion level (each texel counted once, so re-passes over finished cells give 0); dirty-rect upload to a `Texture2D` (no readbacks, no allocations in `Stamp`).
  - `HeapRaycaster.cs` — analytic ray march against the heightfield (no physics colliders).
  - `ProcessWool.shader` (+ `ProcessWoolCommon.hlsl`) — URP ForwardLit + ShadowCaster + DepthOnly. **Vertex:** samples the mask at the puff centre (`SAMPLE_TEXTURE2D_LOD`) → per-puff shape: dirty (matted, medium lobes) → wet (shrink ×0.86, sag, pulled toward cluster centre = clumping, smooth) → dry (×1.1, strong cauliflower lobes from value noise, separated). Airflow push/wobble near `_ToolPos` while drying; gentle idle breathing for dry puffs. **Fragment:** samples the mask at the fragment footprint position → soft local edges; dirt spots/dust via OS noise thresholded by cleanliness; wet = darker, desaturated, glossy highlight + droplet glints; dry = bright cream-white, matte, fuzzy rim + fine cotton relief; same warm wrap lighting as the `WoolShell` puffs.
  - `WoolVisualStateController.cs` — owns the heap mesh/material/mask texture, uploads changes, drives tool uniforms, completion "settle" animation (remaining cells lerp to done over 0.6 s).
  - `ToolPointerInput.cs` — single-pointer press/drag/release (touch + mouse), UI and multi-touch rejection, pause/focus cancel.
  - `HoseToolView.cs` / `DryerToolView.cs` — procedural nozzle + hose and hair-dryer meshes (`ProceduralMeshFactory`), smooth follow with an offset, spin-up/down, water jet (`LineRenderer`, pooled) / subtle air cone.
  - `ProcessingVfx.cs` — one pooled `ParticleSystem` per effect, hard caps: spray/splash (≤80), drips under the grate (≤30), dryer droplets (≤40), fluff (≤20); no shadows, small alpha particles (low overdraw).
  - `WashDebris.cs` — ~24 straws + dust flecks (shared mesh/material) placed on the heightfield; when the mask under them is cleaned they slide downhill, tumble and shrink away (pre-allocated, never destroyed).
  - `ProcessingAudioLibrary.cs` — procedural placeholders: water stream loop, water-on-wool layer, splash/drip one-shots, soft squish; dryer loop (low-passed noise + warm hum, no whine), "air working" layer, air puffs; completion chime. `ProcessingAudioDirector.cs` — pooled sources, ramps, voice limits, shares the `ShearAndGrow.SoundEnabled` pref.
  - `ProcessingHapticsDriver.cs` — owns a `HapticsManager` with the Wash or Dry `HapticsConfig`: press/first hit → `PlayContact`; real processing rate → `SetCutting(density)`; every +6% → `PlayChunk`; finished cells produce nothing (texture stops after `StopTimeout`); completion → `PlayCompletion`; stops on release / pause / focus loss / disable.
  - `WoolProcessingSession.cs` — scene controller for Wash and Dry (stage from config): begin batch (or "nothing to process" state), tool → raycast → stamps → progress → feedback; completion → `CompleteBatch` → result card; Back with confirm → `CancelBatch`. Exposes QA hooks (`QA_StrokeScreen`, `QA_State`).
  - `ProcessingHUD.cs` — Back, progress pill, first-use hint, confirm dialog, result card.
  - `HubController.cs` + `HubUI.cs` + `SellPanel.cs` — resource display (event-driven, not per frame), main actions, sell flow with coin-fly (pooled images), clink sound, haptic confirm, dev tools.
  - `ProceduralIcons.cs` — runtime-generated icon sprites.
  - `HubShowcase.cs` — three small static heaps (dirty / wet / clean) using the same shader, scaled by amount.
- **Editor:** `Processing/Editor/WoolProcessingSceneBuilder.cs` — menu *Shear & Grow ▸ Processing ▸ Build Hub / Wash / Dry Scenes*: builds `Hub.unity`, `WoolWash.unity`, `WoolDry.unity` (barn materials, wooden tub with grate / drying rack, 3 lights like the shearing scene, existing volume profile), creates config assets, and sets the build order **Hub, Shearing, WoolWash, WoolDry** (SampleScene moved to the end, not deleted).
- **Assets created:** `Core/Config/GameBalanceData.asset`; `Processing/Config/{WashConfig, DryConfig}.asset`; `Processing/Config/Haptics/{WashHapticsConfig, DryHapticsConfig}.asset` + 3 profiles each (Soft / Satisfying / Powerful; same profile index as the player's chosen profile); materials `M_ProcessWool`, `M_WaterDrop`, `M_AirStreak`, `M_Straw`, `M_TubWood`, `M_Grate`.

### Modified existing files (minimal)
- `ShearAndGrow.Shearing.asmdef` → add a reference to `ShearAndGrow.Core`.
- `SheepWoolState.cs` → add `cycleId` (a new GUID per wool cycle; legacy saves get one on load).
- `ShearingSessionController.cs` → `CommitState()` calls `WoolEconomy.CreditShearing(...)` (the ledger in the economy file is the single source of truth for payment, so a crash between mask and economy saves can neither lose wool nor double it); a session wool counter; Back → commit + `SceneNavigator.Go(Hub)` when the Hub is in the build (otherwise the old re-enter behaviour, so the scene still works standalone); `RegrowRoutine` commits before the reset; `DevResetSave` also resets the economy.
- `ShearingHUD.cs` → English strings, completion panel with the wool earned + Menu button.
- `ShearingFeelConfig.asset` hint → English.
- `DevSaveTools.cs`, `SaveDataBuildValidator.cs` → include `economy.json` (reset, and detection of economy data inside Assets).
- Shearing scene → add `EconomyBootstrap`.

# Implementation Steps
1. **Core economy** — create the `ShearAndGrow.Core` asmdef + `WoolType`, `GameBalanceData`, `PlayerEconomyState`, `EconomyStore`, `WoolProcessingBatch`, `WoolEconomy`, `SceneNavigator`, `EconomyBootstrap`; create `GameBalanceData.asset`. *Dependency: none.*
2. **EditMode verification of the economy** (RunCommand, real store under a temporary test root): credit once / partial sessions / regrow cycle / no negative; batch complete idempotent, cancel keeps wool; sell guards + prices; save → reload; corrupted main file → `.bak` recovery; epoch. *Dependency: 1.*
3. **Shearing integration** — asmdef reference, `cycleId`, `CommitState` crediting, Back → Hub, completion panel, regrow commit, English HUD, dev reset, validator update, `EconomyBootstrap` in the scene. *Dependency: 1.*
4. **Processing foundation** — asmdef, `ProcessingConfig`, `WoolHeapBuilder` (+ heightfield), `HeapStateMask`, `HeapRaycaster`, `ToolPointerInput`, `ProcessWool.shader`, `WoolVisualStateController`. Render the heap at stages (dirty / wet / dry, and half-processed masks) to images and iterate on the look until it reads as volumetric cartoon wool, not blobs. *Dependency: 1.*
5. **Wash mini-game** — `HoseToolView`, water jet, `ProcessingVfx` (spray, splash, drips), `WashDebris`, `ProcessingAudioLibrary`/`Director` (water), `ProcessingHapticsDriver` + Wash haptic assets, `WoolProcessingSession` (wash stage), `ProcessingHUD`. *Dependency: 3 (economy flow), 4.*
6. **Dry mini-game** — `DryerToolView`, air cone, dryer droplets + fluff VFX, dryer audio, Dry haptic assets, airflow wobble uniforms, dry completion. *Dependency: 5 (shared session/HUD).*
7. **Hub + Sell** — `HubController`, `HubUI`, `SellPanel`, `ProceduralIcons`, `HubShowcase`, coin-fly, sound/haptic confirm, dev tools (+100 Dirty / Reset, debug builds only). *Dependency: 1, 4 (showcase shader).*
8. **Scene builder + build settings** — `WoolProcessingSceneBuilder`; generate the 3 scenes and assets; set the build order. *Dependency: 3–7.*
9. **Play Mode tests** (bootstrap runner, Editor saves backed up/restored): see Verification. Tune wash/dry dose so a typical scripted swipe pattern finishes in 8–15 s. *Dependency: 8.*
10. **Mobile validation prep** — compile player scripts for iOS + Android (release & development), native plugin compile check (unchanged plugins), performance stats in the Editor (verts, batches, active particles, GC alloc in the hot loop), plus a device test checklist. *Dependency: 9.*

# Verification & Testing
**EditMode (automated):**
- Credit: 37% → 37 units; +0 on re-commit; 37%→60% across sessions → +23; regrow (new cycle) → new credit from 0; fractions never double-count after a reload.
- Batches: begin reserves 120; complete → dirty −120, wet +120 once; second complete → false; cancel → unchanged; begin with 0 → false.
- Sell: prices ×1.0 / 1.2 / 1.8 with floor; selling twice in a row → the second gives 0; coins persisted.
- Persistence: reload equals saved; corrupted `economy.json` → restored from `.bak`; Editor/Player separation; build validator passes.

**Play Mode (automated, Editor):**
- Shearing: shear ~40% → Back → Hub shows 40 Dirty; re-enter, cut the same area → still 40; finish → total 100; regrow → can earn again.
- Wash: scripted screen-space sweeps → progress only increases on new cells; re-sweeping cleaned cells adds 0; straws removed; haptic counters (NullHaptics) show contact / texture / accents / completion and no texture while over clean area; completion converts the batch exactly once, even if completion is triggered twice; Back mid-wash → wool unchanged.
- Dry: same checks for dryness; the puff-expansion uniform path is verified via screenshots.
- Sell: Sell Selected / Sell All amounts; coins credited once on a double click.
- Screenshots (portrait 1290×2796): wash at 0% / 50% / 100%, dry at 0% / 50% / 100%, Hub, Sell — reviewed for readability of dirty vs clean vs wet vs fluffy.
- Duration: the scripted human-like swipe pattern reaches 90% in 8–15 s for both games (tuned via config).
- Performance in the Editor: vertices, batches, particles ≤ caps, 0 GC allocations per frame in the wash/dry hot loop.

**Builds:** compile player scripts for Android + iOS (release + development); console clean.

**Manual on device (cannot be done here, will be listed explicitly):** FPS/heat over 10 minutes per mini-game on Android and iPhone 14 Pro Max, input latency, haptic feel of the Wash vs Dry profiles, audio levels, app switching mid-wash, clean install → full loop, app update keeps the economy.

**Known limitations to report:** all art and audio are procedural placeholders (tub/rack/hose/dryer models, straw meshes, water/air particle textures, recorded audio are needed for final quality); no real fluid or fibre simulation, by design.
