using Bindito.Core;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockSystem;
using Timberborn.BuildingsNavigation;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;
using Timberborn.Navigation;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
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
    private readonly IBlockService _blockService;
    private readonly ITerrainService _terrainService;
    private readonly StackableBlockService _stackableBlockService;
    private readonly EventBus _eventBus;
    private readonly EntityComponentRegistry _entityComponentRegistry;
    private readonly INavMeshService _navMeshService;
    private readonly NavMeshGroupService _navMeshGroupService;
    private readonly INavigationCachingService _navigationCachingService;

    private readonly List<StairConnection> _activeConnections = new List<StairConnection>();
    private GameObject _rendererHolder;
    private PathRenderer _renderer;

    private bool _debugNodes = false;
    private bool _debugLines = false;
    private bool _debugCarving = false;

    [Inject]
    public HoomanStairsManager(
        IBlockService blockService,
        ITerrainService terrainService,
        StackableBlockService stackableBlockService,
        EventBus eventBus,
        EntityComponentRegistry entityComponentRegistry,
        INavMeshService navMeshService, NavMeshGroupService navMeshGroupService,
        INavigationCachingService navigationCachingService)
    {
      _blockService = blockService;
      _terrainService = terrainService;
      _stackableBlockService = stackableBlockService;
      _eventBus = eventBus;
      _entityComponentRegistry = entityComponentRegistry;
      _navMeshService = navMeshService;
      _navMeshGroupService = navMeshGroupService;
      _navigationCachingService = navigationCachingService;
    }

    public void PostLoad()
    {
      _rendererHolder = new GameObject("HoomanStairsRenderer");
      _renderer = _rendererHolder.AddComponent<PathRenderer>();
      _eventBus.Register(this);
    }

    public void Dispose()
    {
      _eventBus.Unregister(this);
      if (_rendererHolder != null) UnityEngine.Object.Destroy(_rendererHolder);
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
      if (_renderer != null) _renderer.ClearPathData(@event.BlockObject);
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
      // GUARD 1: Only buildings we care about, and only if they have an entrance
      if (!IsTopValidBuilding(topBuilding) || topBuilding.PositionedEntrance == null) return;
      if (_activeConnections.Any(c => c.TopBuilding == topBuilding)) return;

      // --- PRE-SCAN: Check for a building below BEFORE modifying anything ---
      var footprint = topBuilding.PositionedBlocks.GetOccupiedCoordinates().ToList();
      if (footprint.Count == 0) return;

      int lowestZ = footprint.Min(c => c.z);
      var candidates = footprint
          .Where(c => c.z == lowestZ)
          .Select(coord => _blockService.GetBottomObjectAt(new Vector3Int(coord.x, coord.y, lowestZ - 1)))
          .Where(below => below != null && below != topBuilding && below.IsFinished && below.PositionedEntrance != null && IsBottomValidBuilding(below))
          .ToList();

      // GUARD 2: If no valid building below, exit. Do not touch District Centers or Piles.
      if (candidates.Count == 0) return;

      float avgX = (float)candidates.Average(c => c.Coordinates.x);
      float avgY = (float)candidates.Average(c => c.Coordinates.y);
      BlockObject centralBelow = candidates.OrderBy(c => Mathf.Pow(c.Coordinates.x - avgX, 2) + Mathf.Pow(c.Coordinates.y - avgY, 2)).First();

      var bottomFootprint = centralBelow.PositionedBlocks.GetOccupiedCoordinates().ToList();
      var intersection = footprint.Select(c => new Vector2Int(c.x, c.y)).Intersect(bottomFootprint.Select(c => new Vector2Int(c.x, c.y))).ToList();

      // GUARD 3: Must have an intersection to form a "staircase"
      if (intersection.Count == 0) return;

      // --- RECORD PRE-OUTSIDE ---
      // Check if the tile directly beneath the original door coordinates is valid solid ground or a solid building
      Vector3Int topPreOutsideRaw = topBuilding.PositionedEntrance.Coordinates;
      Vector3Int belowPreOutside = topPreOutsideRaw + new Vector3Int(0, 0, -1);
      bool isWalkable = _terrainService.Underground(belowPreOutside) || _stackableBlockService.IsStackableBlockAt(belowPreOutside);
      Vector3Int? topPreOutside = isWalkable ? (Vector3Int?)topPreOutsideRaw : null;

      // --- THE SHIFT (Just-In-Time) ---
      // We only reach this point if we are 100% sure we are making a stair connection
      var removeMethod = AccessTools.Method(typeof(BlockObject), "RemoveFromService");
      var addMethod = AccessTools.Method(typeof(BlockObject), "AddToService");

      removeMethod?.Invoke(topBuilding, null);

      Vector3Int offset = topBuilding.PositionedEntrance.Direction2D.ToOffset();
      Vector3Int deepInside = topBuilding.PositionedEntrance.Coordinates - offset;

      var constructor = AccessTools.Constructor(typeof(PositionedEntrance), new Type[] { typeof(Vector3Int), typeof(Timberborn.Coordinates.Direction2D) });
      if (constructor != null)
      {
        PositionedEntrance fakedEntrance = (PositionedEntrance)constructor.Invoke(new object[] { deepInside, topBuilding.PositionedEntrance.Direction2D });
        var property = AccessTools.Property(typeof(BlockObject), nameof(BlockObject.PositionedEntrance));
        property?.SetValue(topBuilding, fakedEntrance, null);
      }

      addMethod?.Invoke(topBuilding, null);

      // --- CONTINUE WITH PATHFINDING ---
      Vector3Int topInside = topBuilding.PositionedEntrance.DoorstepCoordinates;
      Vector3Int topOutside = topBuilding.PositionedEntrance.Coordinates;

      HashSet<Vector2Int> top2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in footprint) top2DFootprint.Add(new Vector2Int(c.x, c.y));
      HashSet<Vector2Int> bottom2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in bottomFootprint) bottom2DFootprint.Add(new Vector2Int(c.x, c.y));

      if (!HoomanStairsPathfinder.TryGenerateInternalPath(
          topPreOutside, topOutside, topInside,
          centralBelow.PositionedEntrance.DoorstepCoordinates,
          centralBelow.PositionedEntrance.Coordinates,
          top2DFootprint, bottom2DFootprint,
          out List<Vector3Int> gridPath, out List<Vector3> path))
      {
        //RevertBuildingEntrance(topBuilding);
        return;
      }

      StairConnection conn = new StairConnection { TopBuilding = topBuilding, BottomBuilding = centralBelow, GridPath = gridPath };
      _activeConnections.Add(conn);

      foreach (var node in gridPath)
      {
        if (IsInNavMesh(node)) HoomanStairsRegistry.AddNode(GetNodeId(node));
      }
      for (int i = 0; i < gridPath.Count - 1; i++)
      {
        HoomanStairsRegistry.AddEdge(new EdgeKey(gridPath[i], gridPath[i + 1]));
      }

      int group = _navMeshGroupService.GetDefaultGroupId();
      for (int i = 0; i < gridPath.Count - 1; i++)
      {
        var eDown = NavMeshEdge.CreateGrouped(gridPath[i], gridPath[i + 1], group, true, 1.0f);
        var eUp = NavMeshEdge.CreateGrouped(gridPath[i + 1], gridPath[i], group, true, 1.0f);
        _navMeshService.AddEdge(eDown);
        _navMeshService.AddEdge(eUp);
        conn.InjectedEdges.Add(eDown);
        conn.InjectedEdges.Add(eUp);
      }

      HoomanStairsRegistry.TopBuildings.Add(topBuilding);
      _navigationCachingService.StartCachingRoadFlowField(topOutside);

      var buildingAccessible = topBuilding.GetComponent<Timberborn.Buildings.BuildingAccessible>();
      if (buildingAccessible != null && buildingAccessible.Accessible != null)
      {
        Vector3 inside = NavigationCoordinateSystem.GridToWorld(topOutside);
        buildingAccessible.Accessible.SetAccesses(new List<Vector3> { inside });
      }

      RefreshBuildingNavMesh(topBuilding);
      RefreshBuildingNavMesh(centralBelow);

      if (_renderer != null) _renderer.UpdatePathData(topBuilding, path, _debugNodes, _debugLines, _debugCarving);
    }

    private void RemoveConnectionsForBuilding(BlockObject blockObject)
    {
      for (int i = _activeConnections.Count - 1; i >= 0; i--)
      {
        var conn = _activeConnections[i];
        if (conn.TopBuilding == blockObject || conn.BottomBuilding == blockObject)
        {
          // 1. Clean up NavMesh edges and Registry nodes
          foreach (var edge in conn.InjectedEdges) _navMeshService.RemoveEdge(edge);
          foreach (var node in conn.GridPath)
          {
            if (IsInNavMesh(node)) HoomanStairsRegistry.RemoveNode(GetNodeId(node));
          }
          for (int j = 0; j < conn.GridPath.Count - 1; j++)
          {
            HoomanStairsRegistry.RemoveEdge(new EdgeKey(conn.GridPath[j], conn.GridPath[j + 1]));
          }

          // 2. Stop the AI from looking for the faked internal flow field
          if (conn.TopBuilding != null)
          {
            //_navigationCachingService.StopCachingRoadFlowField(conn.TopBuilding.PositionedEntrance.Coordinates);
          }

          // 3. Cleanup our Registry trackers
          HoomanStairsRegistry.TopBuildings.Remove(conn.TopBuilding);

          // 4. We DO NOT call RevertBuildingEntrance here. 
          // Since the top building is being deleted, we let the vanilla 
          // destruction process finish without interfering with the ComponentCache.

          _activeConnections.RemoveAt(i);
        }
      }
    }
    private void RevertBuildingEntrance(BlockObject topBuilding)
    {
      var removeMethod = AccessTools.Method(typeof(BlockObject), "RemoveFromService");
      var updateMethod = AccessTools.Method(typeof(BlockObject), "UpdateTransformedBlocks");
      var addMethod = AccessTools.Method(typeof(BlockObject), "AddToService");

      removeMethod?.Invoke(topBuilding, null);
      updateMethod?.Invoke(topBuilding, null);
      addMethod?.Invoke(topBuilding, null);

      var buildingAcc = topBuilding.GetComponent<Timberborn.Buildings.BuildingAccessible>();
      if (buildingAcc != null && buildingAcc.Accessible != null && topBuilding.PositionedEntrance != null)
      {
        Vector3 outside = NavigationCoordinateSystem.GridToWorld(topBuilding.PositionedEntrance.DoorstepCoordinates);
        buildingAcc.Accessible.SetAccesses(new List<Vector3> { outside });
      }
    }

    private void CleanupAllConnections()
    {
      foreach (var conn in _activeConnections)
      {
        foreach (var edge in conn.InjectedEdges) _navMeshService.RemoveEdge(edge);
      }
      _activeConnections.Clear();
      HoomanStairsRegistry.StairNodeIds.Clear();
      HoomanStairsRegistry.TunnelEdges.Clear();
      HoomanStairsRegistry.TopBuildings.Clear();
    }

    private void RefreshBuildingNavMesh(BlockObject building)
    {
      var navMesh = building.GetComponent<BuildingNavMesh>();
      if (navMesh != null)
      {
        navMesh.BlockAndRemoveFromNavMesh();
        navMesh.UnblockAndAddToNavMesh();
      }
    }

    private bool IsTopValidBuilding(BlockObject b) =>
        b.HasComponent<Timberborn.DwellingSystem.Dwelling>() ||
        b.HasComponent<Timberborn.WorkSystem.Workplace>() ||
        b.HasComponent<Timberborn.Stockpiles.Stockpile>() ||
        b.HasComponent<Timberborn.Attractions.Attraction>();

    private bool IsBottomValidBuilding(BlockObject b) =>
        b.HasComponent<Timberborn.DwellingSystem.Dwelling>() ||
        b.HasComponent<Timberborn.WorkSystem.Workplace>() ||
        b.HasComponent<Timberborn.Stockpiles.Stockpile>();
  }
}