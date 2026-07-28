# Hooman Stairs — Mod-Specific Agent Instructions

## Identity
- **Mod ID:** `Calloatti.HoomanStairs`
- **Assembly:** `hoomanstairs`
- **Min Game Version:** 1.0.12.5 — uses `timberborn-decompiled-1.0.*` and `timberborn-ripped-1.0.*`
- **Framework:** .NET Standard 2.1, C# 10, Harmony
- **Entry point:** `IModStarter` with `Harmony.PatchAll()` — Harmony ID `"calloatti.hoomanstairs"`

## What This Mod Does
Automatically generates internal navigation paths (virtual stairs) between stacked qualifying buildings so beavers can walk entirely indoors between floors. No external stair scaffolding needed.

Qualifying top buildings: `Dwelling`, `Workplace`, `Stockpile`, `Attraction`.
Qualifying bottom buildings: `Dwelling`, `Workplace`, `Stockpile`.
Intermediate roofs: `Roof3x2.Folktails` / `Roof3x2.IronTeeth` (traversed through).

## Source Architecture (`Version-1.0/Source/`)

| File | Role |
|---|---|
| `ModStarter.cs` | Entry point — Harmony.PatchAll() |
| `ModConfigurator.cs` | DI configurator `[Context("Game")]` — binds `HoomanStairsManager` as singleton |
| `HoomanStairsManager.cs` | Core singleton (IPostLoadableSingleton, IDisposable). Scans buildings, injects/removes nav mesh Edges, manages StairConnections. |
| `HoomanStairsManager.Visualizer.cs` | Partial class with `PathRenderer` MonoBehaviour for debug wireframe rendering |
| `HoomanStairsPathfinder.cs` | Static BFS pathfinder — generates 2D path within building footprints + vertical drop |
| `HoomanStairsPatches.cs` | All Harmony patches |
| `HoomanStairsRegistry.cs` | Static ref-counted registry for stair nodes, top buildings, fake path edges + `EdgeKey` struct |

## The Six Pillars (Architecture)
1. **Manhattan/BFS path** — Pathfinder walks top outside → top inside → 2D BFS within footprint → vertical drop → bottom inside → bottom outside. All path nodes are *inside* building footprints only.
2. **Central Registry** — `HoomanStairsRegistry` uses ref-counted `Dictionary<int, int>` for nodes so destroying one connection doesn't break another that shares nodes.
3. **AutoWall bypass** — Prefix patch on `NavMeshSource.BlockEdge`/`UnblockEdge` returns false for `StairsGroupId`, preventing the game from walling off stair paths.
4. **District bypass** — Postfix patch on `DistrictObstacleService.IsSetObstacle` returns false for stair nodes, letting the orange path line flow through.
5. **Entrance shift** — Top building's entrance is moved inward (`PositionedEntrance.Coordinates - offset`). If the door hangs over air, building interaction point shifts inside.
6. **Live refresh** — `RefreshBuildingNavMesh` calls `BlockAndRemoveFromNavMesh` + `UnblockAndAddToNavMesh` to regenerate walls with Harmony intercepting.

## Key Data Flow
```
Building finished → OnEnteredFinishedStateEvent → ScanTargetBuilding()
  → checks stacked buildings below, skipping Roof3x2
  → shifts top entrance inward if no solid floor outside
  → HoomanStairsPathfinder.TryGenerateInternalPath()
  → registers nodes in HoomanStairsRegistry (ref-counted)
  → injects NavMeshEdge pairs (grouped = StairsGroupId, down cost 0.6, up cost 0.8)
  → Starts caching road+terrain flow field at top outside
  → Overrides BuildingAccessible accesses to the shifted-inside coordinate
  → Refreshes nav mesh on both buildings
```

## Known Pitfalls & Lessons Learned
- **`StairsGroupId` must start at -1** (not 0) to avoid collision with Group 0 on map load.
- **EdgeKey is direction-agnostic** — `Equals` checks both `(A,B)` and `(B,A)`. This matters for `FakePathEdges` used in `ConnectionService.IsEntranceInDirectionAt`.
- **`FixedSlotManager.OnEntererAdded` crash** — Patched with a prefix that calls `slotManager.AddEnterer` directly and returns false. The crash happens when visual slots are disabled by clipping.
- **`DistrictRandomDestinationPicker.GetRandomDestination` crash** — Patched with a Finalizer that catches `ArgumentOutOfRangeException` (empty destination list) and returns current coordinates + null exception.
- **`PositionedEntrance.Coordinates` = OUTSIDE** the building; **`.DoorstepCoordinates` = INSIDE**. This is opposite of what you might expect.
- **Debug config** — `%USERPROFILE%/AppData/LocalLow/Mechanistry/Timberborn/HoomanStairs.txt` with `DebugNodes`, `DebugLines`, `DebugCarving` booleans.
- **Ref-counted nodes** — `AddNode`/`RemoveNode` use increment/decrement. A node is only removed from the registry when its count reaches 0. Never directly clear the registry without cleanup.
- **No localization** — This mod has no user-facing UI strings. No localization files needed.
- **Harmony patches target specific game classes** — patches are organized by target class in `HoomanStairsPatches.cs` (not split into separate files, following existing convention).
- **CitizenUnstucker roof bug (base-game)** — `CitizenUnstucker.TryUnstuckAndKeepDistrict` (`Timberborn.GameDistricts.cs:753-772`) is the game's built-in beaver rescue. When a beaver is "globally unreachable" (pathfinding fails or nav mesh timing race), this fires and checks `IsStuckInsideFinishedBuilding`. If true, it searches `Deltas.Neighbors26Vector3Int` (26 positions in a 3x3x3 cube) for the first neighbor where `DistrictIsGloballyReachable` returns true. The array is ordered Z=-1 (below) first, then Z=0, then Z=1. Neighbors below and on the same level are inside occupied/restricted blocks → unreachable. The first hit is often `(0,0,1)` — the roof surface. Once on the roof, `IsStuckInsideFinishedBuilding` returns false (roof is not "inside" a building), so subsequent unstuck attempts do nothing, and the beaver gets unassigned from its district.

  **Trigger conditions**: `UnassignDistrictIfCutOff()` runs on every nav mesh update AND every time a beaver's pathfinding task returns Failure. A beaver inside ANY building whose path task momentarily fails can trigger this — it is NOT mod-specific.

  **Proof**: Reproduced on a plain ground-level building with no mod intervention. Placing a decorative roof over the stuck beaver via dev mode instantly unstucks it — `IsStuckInsideFinishedBuilding` becomes true again, the unstucker finds a valid neighbor, and teleports the beaver. Removing the decorative roof leaves the beaver trapped on an open roof surface.

## Proposed Fix — CitizenUnstucker Patch (All Buildings)

### Problem
The vanilla `CitizenUnstucker` teleports beavers to the first globally-reachable 26-neighbor position, which is almost always the roof `(0,0,1)`. Once on the roof, the beaver is permanently stuck and gets unassigned from its district.

### Solution
A `[HarmonyPrefix]` on `CitizenUnstucker.TryUnstuckAndKeepDistrict` that runs before the original (returns false to skip it for our cases). The patch:
1. Checks if the beaver is globally unreachable
2. Finds the **nearest position that is part of the district's ROAD network** (the road network, not terrain — since roads are always walkable and connected to the district)
3. Teleports the beaver there

### Search Strategy
- Start from the beaver's grid position
- Spiral outward in expanding diamond rings at the beaver's Z-level
- For each position, check `_navigationCachingService` or `_districtService.DistrictIsGloballyReachable(district, worldPos)` using a road-specific check
- First reachable road position wins
- Fallback: expand Z-range if nothing found

### Implementation
New Harmony patch in `HoomanStairsPatches.cs`. Prefix returns false only when our logic handles it. Falls through to original (and other mods' patches like Beavers For Real) for unhandled cases.

### Compatibility
Vanilla `TryFindReachablePosition` searches 26 neighbors with `DistrictIsGloballyReachable` (uses `_globalReachabilityService.AreaReachable` → `InstantTerrainNavMeshGraph`). Our patch replaces this with a road-network-targeted spiral search. Other mods patching the same method will still run as postfixes and see our `__result`.

## Build & Deploy
- Build via `dotnet build` in `Version-1.0/` or Visual Studio `.slnx`.
- Pre/post build scripts (`prebuild.ps1`/`postbuild.ps1`) handle assembly copying.
- `CommonModSettings.props` defines Timberborn game DLL references, publicizer configuration, and output paths.
- Game assemblies path: `C:\Program Files (x86)\Steam\steamapps\common\timberborn_main\Timberborn_Data\Managed`
- Harmony DLL path: Steam workshop content folder.

## Game Source Access & Research

### Version-to-Path Mapping
Each mod's `Version-{X.Y}` folder targets game version `{X}.{Y}.x.x`. The suffix after the version number (e.g., `-b769e88-sw`) does not matter — match on the major.minor prefix using a wildcard.

| Version Folder | Game Version | Decompiled (glob) | Ripped (glob) | Docs (glob) |
|---|---|---|---|---|
| `Version-1.0` | `1.0.x.x` | `timberborn-decompiled-1.0.*` | `timberborn-ripped-1.0.*` | `timberborn-docs-1.0.*` |
| `Version-1.1` | `1.1.x.x` | `timberborn-decompiled-1.1.*` | `timberborn-ripped-1.1.*` | _(none yet)_ |

### Base Path
All game reference directories live under `C:\Users\calloatti\source\repos\`.

### Available Directory Types
| Prefix | Contents |
|---|---|
| `timberborn-decompiled-{version}*` | Decompiled C# game source |
| `timberborn-ripped-{version}*` | Ripped Unity assets (sprites, shaders, prefabs) |
| `timberborn-docs-{version}*` | Per-assembly documentation markdown |

### Decompiled Directory Structure
Inside each decompiled folder:
  * `EditorDll`
  * `EditorUI`
  * `Localizations`
  * `Shaders`
  * `UI`
  * `Blueprints`

### Version Checking
Target game versions can be confirmed via `_version.txt` at the root of each decompiled folder. Compare this to the `MinimumGameVersion` value in the mod's `manifest.json`.
