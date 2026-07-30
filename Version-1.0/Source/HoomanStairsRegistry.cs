using System.Collections.Generic;
using Timberborn.BlockSystem;

namespace Calloatti.HoomanStairs
{

  public static class HoomanStairsRegistry
  {
    public static readonly Dictionary<int, int> StairNodeIds = new Dictionary<int, int>();

    public static readonly HashSet<BlockObject> TopBuildings = new HashSet<BlockObject>();

    public static void AddNode(int id)
    {
      if (!StairNodeIds.ContainsKey(id)) StairNodeIds[id] = 0;
      StairNodeIds[id]++;
    }

    public static void RemoveNode(int id)
    {
      if (StairNodeIds.ContainsKey(id))
      {
        StairNodeIds[id]--;
        if (StairNodeIds[id] <= 0) StairNodeIds.Remove(id);
      }
    }
  }
}