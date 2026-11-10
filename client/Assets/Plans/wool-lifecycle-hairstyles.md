# Shear & Grow — Wool Lifecycle (regional regrowth), Preserve Hairstyle & Farm label fix

# Project Overview
- **Game Title:** Shear & Grow
- **High-Level Concept:** Cozy mobile farm game with tactile, satisfying manual sheep shearing. Every sheep has its own living fleece: cut areas regrow locally over time, and any cut shape can be preserved as a hairstyle.
- **Players:** Single player
- **Inspiration / Reference Games:** Hay Day (passive regrowth), Animal Crossing (expressive customisation), Clash of Clans (farm camera — already in place)
- **Tone / Art Direction:** Stylized family 3D, warm, soft "popcorn puff" fleece (existing look)
- **Target Platform:** Android + iOS (iPhone 14 Pro Max is a test device)
- **Screen Orientation / Resolution:** Portrait, reference 1080×1920, safe-area aware
- **Render Pipeline:** URP (Unity 6000.6.8f1), New Input System

## Audit summary
| System | Today | Problem for this task |
|---|---|---|
| `ShearMaskAuthority` | 512×256 byte mask; 255 = wool, 0 = sheared (binary); `CutFraction` = Σ weight of bare texels; RLE save | No partial length → no regrowth possible |
| `WoolShell` shader | Shell path: `mask − 0.5` threshold (binary). Puff path (default "ReferencePuffs" coat): whole puff visible if `mask > 0.5` | Cannot show 25/50/75% regrown wool |
| `ShearingSessionController.CommitState` | Pays via `WoolEconomy.CreditShearing(id, cycleId, cutFraction)` (monotonic "highest fraction" per cycle) | With regrowth the cut fraction goes **down** again → regrown wool could never be paid; instant Regrow (delete save → new cycle) = unlimited wool |
| Back / Done | Back = commit + farm. Done only at ≥ 95% and only once (`FinishSettled`), then completion panel; "Regrow" button (all builds) deletes the save | Finish requires 95%, no hairstyle concept |
| Farm sheep (`FarmSheepView`) | Simple puff cluster scaled by `1 − creditedCutArea`; tap = start shearing | Doesn't show the real shape, no regrowth, no preserve |
| `FarmWorldLabels` | Repositions only when the camera moves (or for sheep) | **Label bug root cause** (see Part 9) |
| Saves | `SheepWoolStateStore` schema 2 (per-sheep JSON), economy ledger `{sheepId, cycleId, creditedFraction, givenUnits}` | Need growth data, preserve flag, migration |

**User decisions:** instant Regrow becomes **dev-only**; farm sheep show the **real coat driven by the same authoritative mask**; growth **pauses only while that sheep is on the shearing table**.

# Game Mechanics
## Core Gameplay Loop
1. Tap a sheep on the farm → sheep card (wool %, growth status, hairstyle state) → *Shear*.
2. Shear any amount. Every texel stores its **current wool length** (0..255). The trimmer removes whatever length is there; payment = wool volume actually removed (a 20 %-regrown patch pays 20 % of a full patch).
3. Leave with **Back** (save + farm), **Finish shearing** (celebration + summary, no % requirement) or **Save hairstyle** (save, freeze growth, farm). All three pay through the same idempotent commit → the same area is never paid twice.
4. On the farm the sheep shows its exact coat (same mask). Unpreserved sheep regrow **per texel**: each cut patch grows independently from the moment it was cut (left side regrows, untouched back stays as it is).
5. Preserved sheep keep their shape forever (also after restart); "Unlock hairstyle" on the farm resumes growth from the saved lengths, without compensating the paused time. Preserved sheep can still be re-styled (the lock stays on).

## Regrowth model (per texel, cheap, deterministic)
- `Occupancy[i]` (byte) = wool length: 255 = full fleece, 0 = bare. Growth = +1 step for every texel below 255 each `FullRegrowthDuration / 255` seconds (≈1.9 s at 8 min). Applied in integer steps with a carried remainder (`growthCarrySeconds`) → applying 10 × 1 min or 1 × 10 min gives the identical result.
- Time source: `WoolGrowthClock` (Core) — **Game Time** (default): persistent counter of played seconds (all scenes, saved every 15 s + on pause/quit/scene change); **Real Time** (config switch, prepared): UTC seconds. Each sheep save stores `growthClockStamp`; on load/tick: `delta = clock − stamp`, apply growth (unless preserved or growth disabled), set `stamp = clock`.
- Growth is driven by the sheep lifecycle service, not by a scene: the farm ticks visible sheep every `RegrowthUpdateInterval` (default 2 s); any scene that opens a sheep catches up the accumulated time on load. During its own shearing session the sheep's stamp is reset on commit (paused, user decision).
- Cost: one pass over ≤ 131 k bytes per sheep per interval (~0.1 ms); no per-frame work, no GPU readback, no allocations (preallocated arrays, `NativeArray` copy into the existing R8 texture).

## Payment model (anti-duplication)
- Each stamp returns the removed wool **volume** `Σ weight × length/255` (normalized to a full fleece). Bare texels return 0 (no feedback, no progress, no pay).
- Sheep save keeps a monotonic `lifetimeRemovedWool` (+= removed volume, saved in `CommitState` before paying).
- Economy ledger pays `units = floor(lifetimeRemovedWool × WoolPerFullSheep) − givenUnits` (`CreditRemovedWool`, idempotent; replaces the "highest fraction" rule). Re-commit, Back + Finish, re-entry, restart, preserve toggles → 0 extra. A crash between the two saves pays the difference on the next commit.
- Instant regrow is dev-only; dev regrow/reset deletes the save → new `cycleId` → ledger restarts for that cycle (dev only).

## Controls and Input Methods
Unchanged shearing controls (finger/mouse trimmer, orbit, pinch, belly roll). Farm: tap sheep → card; tap buttons. New compact "Done ▾" popover in shearing.

# UI
**Shearing HUD (bottom)**
```
[← Back]                               [Done ▾]
                    ┌──────────────────────────┐
  (popover on Done) │ ✓ Finish shearing         │  summary of wool earned
                    │   Show results & go home  │
                    │ ✂ Save hairstyle  🔒      │  freeze growth, keep shape
                    │   Keep this look on farm  │
                    └──────────────────────────┘
```
- Progress label: "Wool left 64 %" (current fleece amount incl. regrowth); a 🔒 "Hairstyle saved" chip in the top row when the sheep is preserved.
- The old 95 % auto-gate disappears: Done is always available. Reaching 95 % only triggers a sparkle (no forced exit), so the player can still save a hairstyle.
- Finish summary panel: "+N Dirty Wool this session", wool left %, buttons *Back to farm* / *Keep shearing* (+ *DEV Regrow* in development builds).
- Save hairstyle → toast "Hairstyle saved" → farm.

**Farm — sheep card (bottom sheet, replaces direct tap-to-shear)**
```
Clover                          ✕
Wool 64 %  ▓▓▓▓▓▓░░░   Regrowing · full in ~3 min
🔒 Hairstyle saved (growth paused)
[ ✂ Shear ▸ ]   [ 🔓 Unlock hairstyle ]   (lock button only when preserved)
```
- World label: "Clover 64 %" + 🔒 when preserved. Barn list rows show the same status.

# Key Asset & Context
### State & time (Core / Shearing)
- `Core/Runtime/WoolGrowthClock.cs` (new): `static double Now(WoolGrowthTimeMode)`, game-time accumulator (DontDestroyOnLoad driver created by `EconomyBootstrap`), atomic `clock.json` (Editor/Player folders), save every 15 s + pause/quit.
- `Shearing/Runtime/Config/WoolGrowthConfig.cs` (new SO) + asset `Config/Growth/WoolGrowthConfig.asset`: `RegrowthEnabled = true`, `FullRegrowthDuration = 480 s` (test value, 5–10 min), `TimeMode = GameTime`, `RegrowthUpdateInterval = 2 s`, `DebugRegrowthMultiplier = 1` (Editor/dev only), `MaxOfflineCatchUp` (for Real Time).
- `SheepWoolState` (schema 3): + `hairstylePreserved`, `growthClockStamp` (double), `growthCarrySeconds`, `lifetimeRemovedWool`, `maskEncoding` (1 = binary legacy, 2 = length bytes), `preservedUtc`.
- `ShearMaskAuthority`: `Stamp` returns removed volume and sets length 0; `WoolAmount` (Σ w·len/255), `CutFraction => 1 − WoolAmount` (existing callers keep working); `ApplyGrowth(int steps)` (only texels < 255, dirty rows tracked, returns whether anything changed); `HasWool` unchanged (len > 0); RLE encode/decode unchanged format (bytes), legacy masks are already 0/255 → compatible.
- `SheepWoolLifecycle.cs` (new, Shearing runtime, static): `CatchUp(state, mask, config, clockNow)`, `SetPreserved(state, bool, clockNow)`, steps calculation with carry, honoring Game/Real time + debug multiplier; pure C# → unit testable.

### Economy (Core)
- `WoolEconomy.CreditRemovedWool(sheepId, cycleId, lifetimeRemoved)` — no 0..1 clamp, idempotent via `givenUnits`; ledger field `creditedFraction` reused as "credited lifetime removal" (same JSON field → old saves stay valid). `CreditShearing` kept as a thin wrapper for compatibility.

### Shearing (modify, no rewrite of the feel)
- `ShearingSessionController`: load → `CatchUp` (growth during absence) → decode; `CommitState` writes `lifetimeRemovedWool`, mask, stamp = clock, then pays; `OnFinishPressed` without threshold / `FinishSettled` gate; new `OnSaveHairstylePressed`; Back keeps an existing lock; regrow → dev-only; `SessionWoolEarned` shown in the summary; progress shows wool left.
- `ShearingHUD`: Done ▾ popover (Finish / Save hairstyle), preserved chip, summary panel texts, dev regrow only in debug builds, "Wool left" label.
- `WoolVisualRenderer.UploadMask` unchanged path (bytes now carry length).
- `WoolShellCommon.hlsl` / `WoolShell.shader`: shells — visibility `len > ~0.03`, shell height × len (short stubble → full bulk, soft cut edge kept); puffs — vertex stage samples the mask at the puff's uv0 and shrinks/sinks the puff toward its root (`lerp(root, centre, len)`, radius × (0.35 + 0.65·len)), puffs with len < 0.04 are clipped; fresh regrowth slightly lighter/creamier tint so regrowing patches read clearly.
- `WoolPuffMeshBuilder`: writes `uv2 = (puff centre, 1)` and `uv3 = root point` for baked puffs (curl styles without these keep the old binary behaviour via the w-flag); `BuildCoat(..., lod)` with fewer segments/puffs for the farm.

### Farm (modify)
- `FarmSheepView`: real body (`SheepBodySurface` undercoat, low-res) + LOD puff coat with a per-sheep `M_WoolShell` instance and R8 mask texture filled from the authoritative save (same encoding); head/legs/tail as now, scaled to the body; idle/wander unchanged.
- `FarmSheepRoster` → `FarmSheepState` cache: loads each sheep save once, keeps decoded mask bytes in memory, ticks growth every interval via `SheepWoolLifecycle` (no disk I/O per tick; persisted on leave/pause and when toggling the lock).
- `SheepInteraction`: tap → sheep card. New `FarmPanels.ShowSheep(info)` with Shear / Unlock hairstyle; Barn rows show wool %, growth ETA, 🔒.
- `FarmWorldLabels`: **root-cause fix** (Part 9).

### Part 9 — label bug: root cause & fix
Labels are positioned in the first `LateUpdate`, before the `CanvasScaler` / `SafeAreaFitter` have produced the final canvas size and scale; afterwards building labels only reposition when the **camera** moves (sheep labels always move, so they looked fine). The first touch triggers a camera move (focus/inertia), which finally repositions them. Fix: track the inputs the projection depends on — camera pose & ortho size, `Screen.width/height`, `Screen.safeArea`, root canvas rect size and scale factor — and reposition whenever any of them changes (`OnRectTransformDimensionsChange` on the root as an additional trigger), and compute the position purely from viewport → canvas space. No fake input, no delays. Verified by measuring label positions on frame 1, 2, 10 and after a camera move, at two aspect ratios.

# Implementation Steps
1. **Growth config & clock** — `WoolGrowthConfig` SO + asset, `WoolGrowthClock` (Game/Real time, persistence), driver started by `EconomyBootstrap`. *Dependency: none.*
2. **Mask authority** — length semantics, volume-returning `Stamp`, `WoolAmount`, `ApplyGrowth`, keep RLE format. *Dependency: none.*
3. **Lifecycle & save schema 3** — `SheepWoolState` fields, `SheepWoolLifecycle` (catch-up with carry, preserve/unpreserve without compensation), migration from schema ≤ 2 (binary mask → lengths 0/255 unchanged, `lifetimeRemovedWool = creditedCutArea`, `growthClockStamp = clock now` → no fake instant growth, not preserved), surface/mask-size compatibility check kept. *Dependency: 1, 2.*
4. **Economy** — `CreditRemovedWool`, ledger reuse, wrapper; EditMode tests for scenarios F + migration. *Dependency: 3.*
5. **Shearing integration** — load catch-up, commit pays removed volume, Back / Finish / Save hairstyle, no 95 % gate, dev-only regrow, HUD popover + chips + summary. *Dependency: 3, 4.*
6. **Shader & coat mesh** — length-aware shells and puffs, puff `uv2/uv3`, LOD coat; render the sheep at 0/25/50/75/100 % regrowth and with a hairstyle to verify the look. *Dependency: 2.*
7. **Farm integration** — real-coat `FarmSheepView`, `FarmSheepState` cache + growth ticks, sheep card with Shear / Unlock, labels with 🔒 and %, Barn rows; scene builder update + rebuild FarmHub. *Dependency: 3, 5, 6.*
8. **Label bug fix** — `FarmWorldLabels` dependency tracking + verification measurements. *Dependency: none (done with 7).*
9. **Verification** — EditMode + Play Mode scenarios A–J (below), screenshots; restore user saves/settings afterwards. *Dependency: 1–8.*
10. **Mobile check** — player script compile iOS/Android (release + dev), growth tick cost & GC, save validator. *Dependency: 9.*

# Verification & Testing
**EditMode (automated)**
- Growth math: 1 × 480 s ≡ 480 × 1 s (identical bytes); half duration → cut texels at 128 ±1, untouched texels unchanged (scenario B, regional); texels cut at different times keep independent lengths.
- Preserve (C/D): preserved + 10 min → bytes unchanged; unpreserve → stamp reset, next 60 s grows exactly 60 s worth (no pause compensation).
- Economy (F): cut 40 % → 40 units; re-stamp bare area → 0 removed, 0 units; regrow to 20 % → re-cut → +8 units (20 % of 40); Back + Finish + re-commit → +0; toggling preserve ×10 → +0; dev regrow (new cycle) only in debug builds.
- Two sheep (G): independent bytes, stamps, flags, ledger entries.
- Migration: a schema-2 save (binary mask, `creditedCutArea = 0.35`, ledger `givenUnits = 35`) loads with the same cut shape, `lifetimeRemovedWool = 0.35`, re-commit pays 0; incompatible surface version / mask size still ignored safely; clean install = full wool, no growth stamp from Editor saves (validator).
**Play Mode (automated, Editor)**
- A: farm → shear ~30 % → Back → farm shows the cut on that sheep's coat; re-enter after simulated growth (debug multiplier) → partially regrown shape, wool % consistent.
- C/E: Save hairstyle → farm shows 🔒, wait → unchanged; re-enter, cut more → Back → lock still on, new shape saved.
- D: Unlock on farm → growth resumes.
- H: reload saves from disk → identical masks/flags.
- I: cold start of FarmHub → label screen positions on frame 1/2/10 equal the positions after a camera move (< 1 px), also at a second aspect ratio and after returning from a mini-game.
- J: wash → dry → sell flow still works; shearing feedback counters (clumps, audio, haptics) unchanged; no errors.
- Screenshots: shearing sheep at 0/50/100 % regrowth, a hairstyle on the farm (portrait).
**Builds & performance:** player scripts compile for iOS + Android (release + development); growth tick < 0.5 ms for 2 sheep, 0 GC per tick; save validator passes.
**Manual on device (not possible here):** heat/FPS on farm with two real-coat sheep, regrowth look on device, haptics unchanged.

**Balancing knobs (report):** `FullRegrowthDuration`, `TimeMode` (+ `MaxOfflineCatchUp`), `RegrowthUpdateInterval`, `WoolPerFullSheep`, farm coat LOD (puff count/segments).
