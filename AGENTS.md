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
|---|---|---|
| `ModStarter.cs` | Entry point — Harmony.PatchAll() |
| `ModConfigurator.cs` | DI configurator `[Context("Game")]` — binds `HoomanStairsManager` as singleton |
| `HoomanStairsManager.cs` | Core singleton (IPostLoadableSingleton, IDisposable). Scans buildings, injects/removes nav mesh Edges, manages StairConnections. |
| `HoomanStairsPathfinder.cs` | Static BFS pathfinder — generates 2D path within building footprints + vertical drop |
| `HoomanStairsPatches.cs` | All Harmony patches |
| `HoomanStairsRegistry.cs` | Static ref-counted registry for stair nodes and top building set |

## The Four Pillars (Architecture)
1. **Inside-to-inside BFS path** — Pathfinder walks top `DoorstepCoordinates` → 2D BFS within footprint → vertical drop → bottom `DoorstepCoordinates` → bottom `Coordinates`. All path nodes are *inside* building footprints only. The path ends at the bottom building's `Coordinates` (outside, ground-level road-connected), creating a continuous route from road → bottom entrance → stair path → top interior.
2. **Central Registry** — `HoomanStairsRegistry` uses ref-counted `Dictionary<int, int>` for nodes so destroying one connection doesn't break another that shares nodes.
3. **AutoWall bypass** — Prefix patch on `NavMeshSource.BlockEdge`/`UnblockEdge` returns false for `StairsGroupId`, preventing the game from walling off stair paths.
4. **District bypass** — Postfix patch on `DistrictObstacleService.IsSetObstacle` returns false for stair nodes, letting the orange path line flow through.

## Key Data Flow
```
Building finished → OnEnteredFinishedStateEvent → ScanTargetBuilding()
  → checks stacked buildings below, skipping Roof3x2
  → HoomanStairsPathfinder.TryGenerateInternalPath(topDoorstep, bottomDoorstep, bottomOutside, ...)
  → registers nodes in HoomanStairsRegistry (ref-counted)
  → injects NavMeshEdge pairs (grouped = StairsGroupId, down cost 0.6, up cost 0.8)
  → if top building was never a bottom: injects Coordinates↔DoorstepCoordinates bridge edge (StairsGroupId)
  → marks top building in HoomanStairsRegistry.TopBuildings
  → redirects Accessible.Accesses (default = center of the Coordinates door tile) to DoorstepCoordinates so beavers path directly inside (no mid-air)
  → caches a road flow field at DoorstepCoordinates (useless but prevents GetFlowFieldAtNode throw)
  → caches a terrain flow field at DoorstepCoordinates if the building has BuildingWithTerrainRange (prevents FindTerrainPathCached throw)
  → nav mesh updates automatically (AddEdge/RemoveEdge → NavMeshUpdater.EnqueueRegularChange; no manual refresh)
```

## Building Entrance & Access (Ground Truth)
Verified against `timberborn-decompiled-1.0.13.1`. Three concepts name layers of the same door; don't confuse them.

### `Accessible.Accesses` (`Timberborn.Navigation.cs:1678-1754`)
- `ReadOnlyList<Vector3>` of WORLD-space points beavers path to (backed by `List<Vector3>`). Structure supports N; most subsystems require exactly 1 (`HasSingleAccess`, `.Single()` calls).
- For every qualifying building: exactly ONE, set by `BuildingAccessible.EnableAccesses` (`Timberborn.Buildings.cs:421-431`):
  - `ForceOneFinalAccess=false` (ALL Dwelling/Workplace/Stockpile/Attraction blueprints): `Accesses = Enumerables.One(GridToWorldCentered(PositionedEntrance.Coordinates))` = CENTER of the door tile, plus a `finalAccess`.
  - `ForceOneFinalAccess=true` (only DistrictCenter, EarthRecultivator, Zipline/Tubeway stations): no `finalAccess`.
- `finalAccess` (2nd arg of `SetAccesses`, `Navigation.cs:1747`) = `GridToWorld(TransformCoordinates(WorldToGrid(LocalAccess)))` (`Buildings.cs:451-455`) — the INTERIOR interaction point from `BuildingAccessibleSpec.LocalAccess`. `FindPathUnlimitedRange` appends an interior leg door→finalAccess (`Navigation.cs:1943-1969`); `FindExitPath` reverses it. This is where the beaver ends up inside — NOT DoorstepCoordinates.

### `PositionedEntrance` (`Timberborn.BlockSystem.cs:3294-3320`)
- `Coordinates` = the DOOR TILE (grid, world) = the game's "**entrance**". For standard buildings: blueprint-local `Y=-1` → exactly 1 tile OUTSIDE the footprint, at finished-floor height Z. Must be above-ground + nav-connected or the game flags the building unreachable (`BuildingsReachability.IsBlockedByTerrain`/`IsBlockedByNavMesh`, `Timberborn.BuildingsReachability.cs:348-357`).
- `DoorstepCoordinates` = `Coordinates - Direction2D.ToOffset()` (line 3302). Entrance direction = `placement.Orientation.Transform(OnlyAllowedEntranceDirection)` = local `Down` (verified: `DrivewayModel.GetLocalDirection` returns `Direction2D.Down`, `PathSystem.cs:460-466`). So Doorstep = 1 tile INWARD from the door = the FIRST INTERIOR floor tile = the game's "**inside**".
- Game code names them literally entrance/inside: path-mesh renderer `HasValidEntrance(entranceOwner, entrance, inside)` requires `Coordinates == entrance && DoorstepCoordinates == inside` (`Timberborn.BuildingsNavigation.cs:1506-1513`).
- The door NAV gap = `NavMeshEdge.CreateBlocking(DoorstepCoordinates, Coordinates, defaultGroupId)` ("entrance edge", `Timberborn.BlockSystemNavigation.cs:667-675`). Auto-wall adds it to the blocked set then EXEMPTS it (`_blockedEdges.Remove`, lines 603-610) so the door stays walkable. `BuildingBlockedAccessible.IsBlocked` = `!AreConnected(Coordinates, DoorstepCoordinates)` (`Buildings.cs:474-492`).
- DoorstepCoordinates also = driveway-mat position (`DrivewayModel.GetPositionedCoordinates`, `PathSystem.cs:469-476`) and character-control "walk to this building" destination (`PickDestination`, `CharacterControlSystemUI.cs:208-211`).

### Blueprint reality check (121 qualifying variants, `Blueprints/Buildings`, 1.0.13.1)
- All 120 have `Entrance` at blueprint-local `Y=-1` → door tile OUTSIDE footprint. The mod's outside/inside assumption holds.
- ONLY **Carousel** (Attraction, 5x5x3): `Entrance (2,0,1)` is INSIDE footprint bounds — an occupied block (`Occupations: "Top"`, walkable platform edge). Its default Accesses sits on the footprint perimeter. Harmless to the mod (the redirect to DoorstepCoordinates is unconditional).
- Only 6 blueprints total have `ForceOneFinalAccess: true`; none are Dwelling/Workplace/Stockpile/Attraction.

### Full enter flow (beaver)
1. Path = current pos → `Accesses` (door-tile center) on nav mesh → interior leg → `finalAccess`, then on to the assigned slot.
2. Walk to door tile `Coordinates` (OUTSIDE, road/terrain connected).
3. Cross the door-gap edge `Coordinates→Doorstep` — the only unblocked wall opening ("enter animation"; model disappears behind the wall).
4. Continue interior → `finalAccess` (LocalAccess) → slot (bed/workstation/attraction spot).
- **"Beaver teleports to DoorstepCoordinates" is WRONG** — Doorstep is just the 1st interior nav tile on the in/out route.

### What this means for the mod
- Default Accesses = center of the door tile = a MID-AIR floating tile for a stacked top building (its Coordinates tile hangs over nothing). Redirect → `DoorstepCoordinates` (interior tile) moves the nav target INSIDE the footprint — specifically the first interior tile, which is also the path's start node. Two reasons for the move: (1) beavers path to Accesses to enter, so the redirect keeps them from walking outside through mid-air just to then enter the building; (2) THE MAIN REASON — beavers idle/hang out at their building's `Accessible.Accesses` between tasks, so keeping it mid-air leaves beavers standing in empty space doing nothing. Moving it to the interior doorstep puts idle beavers on solid floor.
- `topOutside` (=Coordinates) stays the road-facing anchor; the mod's StairsGroupId bridge edge replaces the vanilla group-0 door gap, which auto-wall kills on stacking.
- Doorstep is an interior tile → its road flow field can never fill (DistrictMap.HasNode fails, see pitfall below) → the cache there is a throw-prevention stub only.

## Known Pitfalls & Lessons Learned
- **`StairsGroupId` must start at -1** (not 0) to avoid collision with Group 0 on map load.
- **`Accessible.Accesses` redirect to `DoorstepCoordinates`** — `ScanTargetBuilding` redirects the top building's accessible position (default = `GridToWorldCentered(Coordinates)`, the center of the door tile; see "Building Entrance & Access (Ground Truth)") from `Coordinates` (outside, mid-air) to `DoorstepCoordinates` (inside) so beavers path directly inside without the "exit to mid-air then re-enter" visual. The redirect is done via a `[HarmonyPrefix]` on `Accessible.SetAccesses` that checks `HoomanStairsRegistry.TopBuildings.Contains(blockObject)` — so only already-connected buildings are redirected. Newly constructed buildings get the redirect in `ScanTargetBuilding` after the connection is established (the prefix fires again via the manual `SetAccesses` call, and the building is now in `TopBuildings`). During saved-game load, buildings aren't in `TopBuildings` yet, so `StartCaching` caches at `Coordinates`; the redirect happens later in `RefreshAllBuildings` → `ScanTargetBuilding`.
- **`BuildingCachingFlowField.StartCaching`** (`Timberborn.BuildingsNavigation.cs:844-851`) runs during `OnEnterFinishedState` for every building. It calls `_navigationCachingService.StartCachingRoadFlowField(_accessCoordinates)` where `_accessCoordinates = WorldToGridInt(Accessible.Accesses.Single())`. This creates an empty cache entry keyed by node ID in `RoadFlowFieldCache.FlowFieldCache` (a `Dictionary<int, CacheEntry>`). The cache is NOT filled during this call — it just reserves a slot with ref-counting. The actual flow field is filled lazily on first pathfinding query.
- **`FindRoadPathCached` throws on cache miss** (`Timberborn.Navigation.cs:6199-6206`) — `NavigationService.FindRoadPath` calls `PathfindingService.FindRoadPathCached(start, end, ...)` which calls `_roadFlowFieldCache.GetFlowFieldAtNode(WorldToId(start))`. If no cache entry exists at the start node's ID, it throws `InvalidOperationException`. This means every building's accessible position MUST have a cached flow field entry, or any pathfinding FROM that building crashes. The cache entry can exist but remain unfilled (harmless — `FindPathInFlowField` returns false on unfilled flow fields).
- **`FillFlowField` requires district road flow field** (`Timberborn.Navigation.cs:6942`) — `RoadFlowFieldGenerator.FillFlowField` checks `roadNavMeshGraph.IsOnNavMesh(startNodeId) && limitingFlowField.HasNode(startNodeId)`. The `limitingFlowField` comes from `DistrictMap.GetDistrictRoadFlowFieldByRoadNodeId` which returns the empty flow field for non-road nodes (interior tiles). Interior tiles like `DoorstepCoordinates` fail the second check, so flow fields at interior nodes can never be filled. This is why the cache at `DoorstepCoordinates` is useless but necessary (to prevent the throw).
- **Cache at DoorstepCoordinates must be manually created** — Since `StartCaching` runs before `TopBuildings` is populated (during load), it caches at `Coordinates` (the default accessible). After `ScanTargetBuilding` redirects to `DoorstepCoordinates`, a new cache entry must be manually created via `_navigationCachingService.StartCachingRoadFlowField(DoorstepCoordinates)` in `ScanTargetBuilding`. If the building has `BuildingWithTerrainRange`, a terrain flow field cache must also be created with `StartCachingTerrainFlowField` — otherwise `FindTerrainPathCached` throws on the missing terrain cache. `INavigationCachingService` is injected into `HoomanStairsManager` for this purpose.
- **`PositionedEntrance` design rationale** — `Coordinates` = OUTSIDE (beaver parks here, road/terrain-connected). `DoorstepCoordinates` = INSIDE (first interior floor tile behind the door). The beaver does NOT teleport to Doorstep — it walks the door-gap nav edge (`Coordinates→Doorstep`, the one wall opening), then on to `finalAccess`/`LocalAccess`; Doorstep is just the first interior nav tile on the in/out route. See "Building Entrance & Access (Ground Truth)".
- **Topmost building needs a `Coordinates→DoorstepCoordinates` bridge edge with `StairsGroupId`** — The game's default entrance edge uses Group 0, which gets wall-blocked. Without a protected-group bridge, the top building's entrance is unreachable from the stair path. `ScanTargetBuilding` adds this bridge edge for every top building that wasn't already a bottom in another connection.
- **Middle buildings get the bridge edge for free** — When building C is the bottom of connection D→C, D's path ends with `C.Doorstep → C.Coordinates` (the last edge pair). This IS the bridge edge, already using `StairsGroupId`. So middle buildings never need an explicit bridge injection.
- **`FixedSlotManager.OnEntererAdded` crash** — Patched with a prefix that calls `slotManager.AddEnterer` directly and returns false. The crash happens when visual slots are disabled by clipping.
- **`DistrictRandomDestinationPicker.GetRandomDestination` crash** — Patched with a Finalizer that catches `ArgumentOutOfRangeException` (empty destination list) and returns current coordinates + null exception.
- **`PositionedEntrance.Coordinates` = OUTSIDE** (door tile); **`.DoorstepCoordinates` = INSIDE** (one tile in from the door) — opposite of what you might expect. Game's own naming: "entrance"/"inside" (see Ground Truth section).
- **`CoordinateSystem.GridToWorldCentered`** (`Coordinates.cs:254-257`) — `GridToWorld(Vector3Int)` swaps axes: `new Vector3(x, z, y)` (grid→Unity). `CenterWorld` adds `(0.5, 0, 0.5)` to center the tile. So the building interaction point (`Accessible`) defaults to the center of `Coordinates`'s tile — the tile just outside the door.
- **Middle buildings handled automatically** — Buildings are built bottom-up. When B finishes above A, B scans DOWN to find A (B=top, A=bottom). When C finishes above B, C scans DOWN to find B (C=top, B=bottom). B ends up with two connections: top of B→A, bottom of C→B. The nav mesh edges overlap in B's interior, creating a continuous path C→B→A. No re-scan mechanism needed.
- **Scanning direction is correct** — The newly-finished building is always the topmost (bottom-up construction). It scans downward, finding eligible buildings below that are already finished. No need to re-scan buildings above the newly finished one.
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
