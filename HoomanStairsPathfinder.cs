using System.Collections.Generic;
using Timberborn.Navigation;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  public static class HoomanStairsPathfinder
  {
    public static bool TryGenerateInternalPath(
        Vector3Int topOutside,
        Vector3Int topInside,
        Vector3Int bottomInside,
        Vector3Int bottomOutside,
        Vector2Int bridgeCol,
        HashSet<Vector2Int> topFootprint,
        HashSet<Vector2Int> bottomFootprint,
        out List<Vector3Int> gridPath,
        out List<Vector3> path)
    {
      gridPath = new List<Vector3Int>();
      path = new List<Vector3>();

      // 1. Outside to Inside
      AddNode(topOutside, gridPath, path);
      Vector3Int current = topInside;
      AddNode(current, gridPath, path);

      // 2. BFS from Top Inside to Common Footprint (bridgeCol)
      var topPath2D = FindPath2D(new Vector2Int(current.x, current.y), bridgeCol, topFootprint);
      if (topPath2D == null) return false; // Fails safely instead of walking outside

      foreach (var step in topPath2D)
      {
        current = new Vector3Int(step.x, step.y, current.z);
        AddNode(current, gridPath, path);
      }

      // 3. Drop Z at the Common Footprint
      int safety = 0;
      while (current.z != bottomInside.z && safety++ < 100)
      {
        current.z += (bottomInside.z > current.z) ? 1 : -1;
        AddNode(current, gridPath, path);
      }

      // 4. BFS from Common Footprint to Bottom Inside
      var bottomPath2D = FindPath2D(new Vector2Int(current.x, current.y), new Vector2Int(bottomInside.x, bottomInside.y), bottomFootprint);
      if (bottomPath2D == null) return false;

      foreach (var step in bottomPath2D)
      {
        current = new Vector3Int(step.x, step.y, current.z);
        AddNode(current, gridPath, path);
      }

      // 5. Inside to Outside
      AddNode(bottomOutside, gridPath, path);

      return true;
    }

    // Pure BFS: Will never step outside the footprint.
    private static List<Vector2Int> FindPath2D(Vector2Int start, Vector2Int target, HashSet<Vector2Int> footprint)
    {
      List<Vector2Int> result = new List<Vector2Int>();
      if (start == target) return result;

      // Force the start and target tiles to be considered valid floor space
      // (Fixes the bug where the door coordinate isn't in the OccupiedBlocks list)
      footprint.Add(start);
      footprint.Add(target);

      Queue<Vector2Int> queue = new Queue<Vector2Int>();
      Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

      queue.Enqueue(start);
      cameFrom[start] = start;

      Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

      while (queue.Count > 0)
      {
        Vector2Int curr = queue.Dequeue();
        if (curr == target) break;

        foreach (var dir in dirs)
        {
          Vector2Int next = curr + dir;
          // STRICT CHECK: Must be inside the building footprint
          if (footprint.Contains(next) && !cameFrom.ContainsKey(next))
          {
            queue.Enqueue(next);
            cameFrom[next] = curr;
          }
        }
      }

      // If no path is found entirely inside the building, abort.
      if (!cameFrom.ContainsKey(target)) return null;

      // Trace the path backwards from target to start
      Vector2Int step = target;
      while (step != start)
      {
        result.Add(step);
        step = cameFrom[step];
      }
      result.Reverse();

      return result;
    }

    private static void AddNode(Vector3Int node, List<Vector3Int> gridPath, List<Vector3> path)
    {
      if (gridPath.Count > 0 && gridPath[gridPath.Count - 1] == node) return;

      gridPath.Add(node);
      path.Add(NavigationCoordinateSystem.GridToWorld(node));
    }
  }
}