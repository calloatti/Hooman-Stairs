using Bindito.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;
using Timberborn.Navigation;
using Timberborn.SingletonSystem;
using Timberborn.UILayoutSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  public class StairConnection
  {
    public BlockObject TopBuilding;
    public BlockObject BottomBuilding;
    public List<Vector3Int> GridPath;
    public List<NavMeshEdge> InjectedEdges = new List<NavMeshEdge>();
  }

  public partial class HoomanStairsManager : IPostLoadableSingleton, IDisposable
  {
    public static int StairsGroupId { get; private set; } = -1;

    private readonly IBlockService _blockService;
    private readonly EventBus _eventBus;
    private readonly EntityComponentRegistry _entityComponentRegistry;
    private readonly INavMeshService _navMeshService;
    private readonly INavigationCachingService _navigationCachingService;
    private readonly NavMeshGroupService _navMeshGroupService;

    private readonly List<StairConnection> _activeConnections = new List<StairConnection>();

    [Inject]
    public HoomanStairsManager(
        IBlockService blockService,
        EventBus eventBus,
        EntityComponentRegistry entityComponentRegistry,
        INavMeshService navMeshService, INavigationCachingService navigationCachingService, NavMeshGroupService navMeshGroupService)
    {
      _blockService = blockService;
      _eventBus = eventBus;
      _entityComponentRegistry = entityComponentRegistry;
      _navMeshService = navMeshService;
      _navigationCachingService = navigationCachingService;
      _navMeshGroupService = navMeshGroupService;
    }

    public void PostLoad()
    {
      StairsGroupId = _navMeshGroupService.GetOrAddGroupId("HoomanStairs");

      _eventBus.Register(this);
    }

    public void Dispose()
    {
      _eventBus.Unregister(this);
      CleanupAllConnections();
    }

    [OnEvent]
    public void OnGameFullyLoaded(ShowPrimaryUIEvent @event) => RefreshAllBuildings();

    [OnEvent]
    public void OnBuildingFinished(EnteredFinishedStateEvent @event)
    {
      if (@event.BlockObject && IsTopValidBuilding(@event.BlockObject)) ScanTargetBuilding(@event.BlockObject);
    }

    [OnEvent]
    public void OnBuildingRemoved(ExitedFinishedStateEvent @event)
    {
      RemoveConnectionsForBuilding(@event.BlockObject);
    }

    public void RefreshAllBuildings()
    {
      var allBuildings = _entityComponentRegistry.GetEnabled<Timberborn.Buildings.Building>();
      foreach (var building in allBuildings)
      {
        BlockObject b = building.GetComponent<BlockObject>();
        if (b != null && b.IsFinished && IsTopValidBuilding(b)) ScanTargetBuilding(b);
      }
    }

    private int GetNodeId(Vector3Int coordinates)
    {
      Vector3Int size = _blockService.Size + new Vector3Int(2, 2, 2);
      Vector3Int c = coordinates + new Vector3Int(1, 1, 1);
      return c.x * size.y * size.z + c.y * size.z + c.z;
    }

    private bool IsInNavMesh(Vector3Int coordinates)
    {
      Vector3Int size = _blockService.Size;
      return coordinates.x >= -1 && coordinates.x <= size.x &&
             coordinates.y >= -1 && coordinates.y <= size.y &&
             coordinates.z >= -1 && coordinates.z <= size.z;
    }

    private void ScanTargetBuilding(BlockObject topBuilding)
    {
      if (!IsTopValidBuilding(topBuilding) || topBuilding.PositionedEntrance == null) return;
      if (_activeConnections.Any(c => c.TopBuilding == topBuilding)) return;

      var footprint = topBuilding.PositionedBlocks.GetOccupiedCoordinates().ToList();
      if (footprint.Count == 0) return;

      int lowestZ = footprint.Min(c => c.z);
      var candidates = new List<BlockObject>();

      // NEW: Track exactly which X,Y columns reached each specific bottom building safely
      var validDropColumnsByCandidate = new Dictionary<BlockObject, HashSet<Vector2Int>>();

      foreach (var coord in footprint.Where(c => c.z == lowestZ))
      {
        int checkZ = lowestZ - 1;
        while (checkZ >= 0)
        {
          var below = _blockService.GetBottomObjectAt(new Vector3Int(coord.x, coord.y, checkZ));

          if (below == null) break;
          if (below == topBuilding)
          {
            checkZ--;
            continue;
          }

          if (IsIntermediateRoof(below))
          {
            checkZ = below.Coordinates.z - 1;
            continue;
          }

          if (below.IsFinished && below.PositionedEntrance != null && IsBottomValidBuilding(below))
          {
            candidates.Add(below);

            // Record this specific column as a safe path to this bottom building
            if (!validDropColumnsByCandidate.ContainsKey(below))
            {
              validDropColumnsByCandidate[below] = new HashSet<Vector2Int>();
            }
            validDropColumnsByCandidate[below].Add(new Vector2Int(coord.x, coord.y));
          }

          break;
        }
      }

      if (candidates.Count == 0) return;

      float avgX = (float)candidates.Average(c => c.Coordinates.x);
      float avgY = (float)candidates.Average(c => c.Coordinates.y);
      BlockObject centralBelow = candidates.OrderBy(c => Mathf.Pow(c.Coordinates.x - avgX, 2) + Mathf.Pow(c.Coordinates.y - avgY, 2)).First();

      // SAFETY CHECK: Prevent merging two distinct, established districts (mimicking GateConflictDetector logic)
      var topDistrict = topBuilding.GetComponent<Timberborn.GameDistricts.DistrictBuilding>()?.District;
      var bottomDistrict = centralBelow.GetComponent<Timberborn.GameDistricts.DistrictBuilding>()?.District;
      if (topDistrict != null && bottomDistrict != null && topDistrict != bottomDistrict)
      {
        return;
      }

      var bottomFootprint = centralBelow.PositionedBlocks.GetOccupiedCoordinates().ToList();
      var validDropColumns = validDropColumnsByCandidate[centralBelow];

      Vector3Int topDoorstep = topBuilding.PositionedEntrance.DoorstepCoordinates;
      Vector3Int bottomDoorstep = centralBelow.PositionedEntrance.DoorstepCoordinates;
      Vector3Int bottomOutside = centralBelow.PositionedEntrance.Coordinates;

      HashSet<Vector2Int> top2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in footprint) top2DFootprint.Add(new Vector2Int(c.x, c.y));
      HashSet<Vector2Int> bottom2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in bottomFootprint) bottom2DFootprint.Add(new Vector2Int(c.x, c.y));

      if (!HoomanStairsPathfinder.TryGenerateInternalPath(
          topDoorstep,
          bottomDoorstep,
          bottomOutside,
          top2DFootprint, bottom2DFootprint, validDropColumns,
          out List<Vector3Int> gridPath, out var _))
      {
        return;
      }

      Vector3Int topOutside = topBuilding.PositionedEntrance.Coordinates;
      StairConnection conn = new StairConnection { TopBuilding = topBuilding, BottomBuilding = centralBelow, GridPath = gridPath };

      _activeConnections.Add(conn);

      foreach (var node in gridPath)
      {
        if (IsInNavMesh(node)) HoomanStairsRegistry.AddNode(GetNodeId(node));
      }
      if (IsInNavMesh(topOutside))
      {
        HoomanStairsRegistry.AddNode(GetNodeId(topOutside));
      }

      int group = StairsGroupId;
      for (int i = 0; i < gridPath.Count - 1; i++)
      {
        var eDown = NavMeshEdge.CreateGrouped(gridPath[i], gridPath[i + 1], group, true, 0.6f);
        var eUp = NavMeshEdge.CreateGrouped(gridPath[i + 1], gridPath[i], group, true, 0.8f);
        _navMeshService.AddEdge(eDown);
        _navMeshService.AddEdge(eUp);
        conn.InjectedEdges.Add(eDown);
        conn.InjectedEdges.Add(eUp);
      }
      // Bridge the top building's Coordinates to DoorstepCoordinates with our protected
      // group so the road network can reach the entrance through the stair path.
      // Only needed if the building wasn't already bridged as a bottom building by the
      // connection above (that path already ends at this building's Coordinates).
      if (!_activeConnections.Any(c => c.BottomBuilding == topBuilding))
      {
        var eDown = NavMeshEdge.CreateGrouped(topOutside, gridPath[0], group, true, 0.6f);
        var eUp = NavMeshEdge.CreateGrouped(gridPath[0], topOutside, group, true, 0.8f);
        _navMeshService.AddEdge(eDown);
        _navMeshService.AddEdge(eUp);
        conn.InjectedEdges.Add(eDown);
        conn.InjectedEdges.Add(eUp);
      }

      HoomanStairsRegistry.TopBuildings.Add(topBuilding);

      // Redirect accessible to DoorstepCoordinates so beavers path directly inside
      var buildingAccessible = topBuilding.GetComponent<Timberborn.Buildings.BuildingAccessible>();
      if (buildingAccessible != null)
      {
        var accessible = buildingAccessible.Accessible;
        if (accessible != null)
        {
          Vector3 doorstepWorld = CoordinateSystem.GridToWorldCentered(topBuilding.PositionedEntrance.DoorstepCoordinates);
          accessible.SetAccesses(Enumerables.One(doorstepWorld));
          var doorstepGrid = topBuilding.PositionedEntrance.DoorstepCoordinates;
          _navigationCachingService.StartCachingRoadFlowField(doorstepGrid);
          if (topBuilding.GetComponent<Timberborn.BuildingRange.BuildingWithTerrainRange>() != null)
          {
            _navigationCachingService.StartCachingTerrainFlowField(doorstepGrid);
          }
        }
      }

      // Track the top outside cell for cleanup
      gridPath.Add(topOutside);
    }

    private void RemoveConnectionsForBuilding(BlockObject blockObject)
    {
      for (int i = _activeConnections.Count - 1; i >= 0; i--)
      {
        var conn = _activeConnections[i];
        if (conn.TopBuilding == blockObject || conn.BottomBuilding == blockObject)
        {
          foreach (var edge in conn.InjectedEdges) _navMeshService.RemoveEdge(edge);
          foreach (var node in conn.GridPath)
          {
            if (IsInNavMesh(node)) HoomanStairsRegistry.RemoveNode(GetNodeId(node));
          }

          if (conn.TopBuilding == blockObject)
          {
            var entrance = conn.TopBuilding.PositionedEntrance;
            if (entrance != null)
            {
              _navigationCachingService.StopCachingRoadFlowField(entrance.DoorstepCoordinates);
              if (conn.TopBuilding.GetComponent<Timberborn.BuildingRange.BuildingWithTerrainRange>() != null)
              {
                _navigationCachingService.StopCachingTerrainFlowField(entrance.DoorstepCoordinates);
              }
            }
          }

          HoomanStairsRegistry.TopBuildings.Remove(conn.TopBuilding);
          _activeConnections.RemoveAt(i);
        }
      }
    }

    private void CleanupAllConnections()
    {
      foreach (var conn in _activeConnections)
      {
        foreach (var edge in conn.InjectedEdges) _navMeshService.RemoveEdge(edge);
        var entrance = conn.TopBuilding.PositionedEntrance;
        if (entrance != null)
        {
          _navigationCachingService.StopCachingRoadFlowField(entrance.DoorstepCoordinates);
          if (conn.TopBuilding.GetComponent<Timberborn.BuildingRange.BuildingWithTerrainRange>() != null)
          {
            _navigationCachingService.StopCachingTerrainFlowField(entrance.DoorstepCoordinates);
          }
        }
      }
      _activeConnections.Clear();
      HoomanStairsRegistry.StairNodeIds.Clear();
      HoomanStairsRegistry.TopBuildings.Clear();
    }

    private bool IsTopValidBuilding(BlockObject b) =>
            !b.HasComponent<Timberborn.GameDistricts.DistrictCenter>() &&
            !b.HasComponent<Timberborn.DistributionSystem.DistrictCrossing>() && (
            b.HasComponent<Timberborn.DwellingSystem.Dwelling>() ||
            b.HasComponent<Timberborn.WorkSystem.Workplace>() ||
            b.HasComponent<Timberborn.Stockpiles.Stockpile>() ||
            b.HasComponent<Timberborn.Attractions.Attraction>());

    private bool IsBottomValidBuilding(BlockObject b) =>
        !b.HasComponent<Timberborn.GameDistricts.DistrictCenter>() &&
        !b.HasComponent<Timberborn.DistributionSystem.DistrictCrossing>() && (
        b.HasComponent<Timberborn.DwellingSystem.Dwelling>() ||
        b.HasComponent<Timberborn.WorkSystem.Workplace>() ||
        b.HasComponent<Timberborn.Stockpiles.Stockpile>());

    private bool IsIntermediateRoof(BlockObject b)
    {
      if (b.TryGetComponent<Timberborn.TemplateSystem.TemplateSpec>(out var templateSpec))
      {
        return templateSpec.TemplateName == "Roof3x2.Folktails" || templateSpec.TemplateName == "Roof3x2.IronTeeth";
      }

      return b.Name.Contains("Roof3x2.Folktails") || b.Name.Contains("Roof3x2.IronTeeth");
    }
  }
}