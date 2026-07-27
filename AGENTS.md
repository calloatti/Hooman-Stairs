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

## Build & Deploy
- Build via `dotnet build` in `Version-1.0/` or Visual Studio `.slnx`.
- Pre/post build scripts (`prebuild.ps1`/`postbuild.ps1`) handle assembly copying.
- `CommonModSettings.props` defines Timberborn game DLL references, publicizer configuration, and output paths.
- Game assemblies path: `C:\Program Files (x86)\Steam\steamapps\common\timberborn_main\Timberborn_Data\Managed`
- Harmony DLL path: Steam workshop content folder.
