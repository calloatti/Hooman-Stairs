using Bindito.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BlockSystem;
using Timberborn.BuildingsNavigation;
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
    private readonly IBlockService _blockService;
    private readonly EventBus _eventBus;
    private readonly EntityComponentRegistry _entityComponentRegistry;
    private readonly INavMeshService _navMeshService;
    private readonly NavMeshGroupService _navMeshGroupService;

    private readonly List<StairConnection> _activeConnections = new List<StairConnection>();
    private GameObject _rendererHolder;
    private PathRenderer _renderer;

    private bool _debugNodes = true;
    private bool _debugLines = true;
    private bool _debugCarving = false;

    [Inject]
    public HoomanStairsManager(
        IBlockService blockService, EventBus eventBus,
        EntityComponentRegistry entityComponentRegistry,
        INavMeshService navMeshService, NavMeshGroupService navMeshGroupService)
    {
      _blockService = blockService;
      _eventBus = eventBus;
      _entityComponentRegistry = entityComponentRegistry;
      _navMeshService = navMeshService;
      _navMeshGroupService = navMeshGroupService;
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
      if (@event.BlockObject && IsValidBuilding(@event.BlockObject)) ScanTargetBuilding(@event.BlockObject);
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
        if (b != null && b.IsFinished && IsValidBuilding(b)) ScanTargetBuilding(b);
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
      if (topBuilding.PositionedEntrance == null) return;

      if (_activeConnections.Any(c => c.TopBuilding == topBuilding)) return;

      Vector3Int topOutside = topBuilding.PositionedEntrance.DoorstepCoordinates;
      Vector3Int topInside = topBuilding.PositionedEntrance.Coordinates;

      var footprint = topBuilding.PositionedBlocks.GetOccupiedCoordinates().ToList();
      if (footprint.Count == 0) return;

      int lowestZ = footprint.Min(c => c.z);

      var candidates = footprint
          .Where(c => c.z == lowestZ)
          .Select(coord => _blockService.GetBottomObjectAt(new Vector3Int(coord.x, coord.y, lowestZ - 1)))
          .Where(below => below != null && below != topBuilding && below.IsFinished && below.PositionedEntrance != null)
          .ToList();

      if (candidates.Count == 0) return;

      float avgX = (float)candidates.Average(c => c.Coordinates.x);
      float avgY = (float)candidates.Average(c => c.Coordinates.y);
      BlockObject centralBelow = candidates.OrderBy(c => Mathf.Pow(c.Coordinates.x - avgX, 2) + Mathf.Pow(c.Coordinates.y - avgY, 2)).First();

      Vector3Int bottomOutside = centralBelow.PositionedEntrance.DoorstepCoordinates;
      Vector3Int bottomInside = centralBelow.PositionedEntrance.Coordinates;

      var bottomFootprint = centralBelow.PositionedBlocks.GetOccupiedCoordinates().ToList();
      var intersection = footprint.Select(c => new Vector2Int(c.x, c.y)).Intersect(bottomFootprint.Select(c => new Vector2Int(c.x, c.y))).ToList();
      if (intersection.Count == 0) return;

      Vector2Int bridgeCol = intersection.OrderBy(c => Vector2Int.Distance(c, new Vector2Int(topInside.x, topInside.y))).First();

      // Convert 3D footprints to strict 2D sets for the pathfinder
      HashSet<Vector2Int> top2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in footprint) top2DFootprint.Add(new Vector2Int(c.x, c.y));

      HashSet<Vector2Int> bottom2DFootprint = new HashSet<Vector2Int>();
      foreach (var c in bottomFootprint) bottom2DFootprint.Add(new Vector2Int(c.x, c.y));

      // Pass the footprints to the isolated pathfinder
      if (!HoomanStairsPathfinder.TryGenerateInternalPath(
          topOutside, topInside, bottomInside, bottomOutside, bridgeCol,
          top2DFootprint, bottom2DFootprint,
          out List<Vector3Int> gridPath, out List<Vector3> path))
      {
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

      var buildingAccessible = topBuilding.GetComponent<Timberborn.Buildings.BuildingAccessible>();
      if (buildingAccessible != null && buildingAccessible.Accessible != null)
      {
        Vector3 inside = NavigationCoordinateSystem.GridToWorld(topInside);
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
          foreach (var edge in conn.InjectedEdges) _navMeshService.RemoveEdge(edge);

          foreach (var node in conn.GridPath)
          {
            if (IsInNavMesh(node)) HoomanStairsRegistry.RemoveNode(GetNodeId(node));
          }
          for (int j = 0; j < conn.GridPath.Count - 1; j++)
          {
            HoomanStairsRegistry.RemoveEdge(new EdgeKey(conn.GridPath[j], conn.GridPath[j + 1]));
          }

          HoomanStairsRegistry.TopBuildings.Remove(conn.TopBuilding);

          if (conn.TopBuilding != null && conn.TopBuilding.IsFinished)
          {
            var buildingAcc = conn.TopBuilding.GetComponent<Timberborn.Buildings.BuildingAccessible>();
            if (buildingAcc != null && buildingAcc.Accessible != null && conn.TopBuilding.PositionedEntrance != null)
            {
              Vector3 outside = NavigationCoordinateSystem.GridToWorld(conn.TopBuilding.PositionedEntrance.DoorstepCoordinates);
              buildingAcc.Accessible.SetAccesses(new List<Vector3> { outside });
            }
          }

          BlockObject survivor = (conn.TopBuilding == blockObject) ? conn.BottomBuilding : conn.TopBuilding;
          if (survivor != null && survivor.IsFinished) RefreshBuildingNavMesh(survivor);

          _activeConnections.RemoveAt(i);
        }
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

    private bool IsValidBuilding(BlockObject b) =>
        b.HasComponent<Timberborn.DwellingSystem.Dwelling>() ||
        b.HasComponent<Timberborn.WorkSystem.Workplace>() ||
        b.HasComponent<Timberborn.Stockpiles.Stockpile>();
  }
}