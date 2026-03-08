using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.Navigation;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  [HarmonyPatch]
  public static class HoomanStairsPatches
  {
    [HarmonyPatch(typeof(NavMeshObject), nameof(NavMeshObject.BlockEdge))]
    [HarmonyPrefix]
    public static bool BlockEdge_Prefix(NavMeshEdge navMeshEdge)
    {
      EdgeKey key = new EdgeKey(navMeshEdge.Start, navMeshEdge.End);
      if (HoomanStairsRegistry.TunnelEdges.ContainsKey(key))
      {
        return false;
      }
      return true;
    }

    [HarmonyPatch("Timberborn.Navigation.DistrictObstacleService", "IsSetObstacle")]
    [HarmonyPostfix]
    public static void IsSetObstacle_Postfix(int nodeId, ref bool __result)
    {
      if (__result && HoomanStairsRegistry.StairNodeIds.ContainsKey(nodeId))
      {
        __result = false;
      }
    }

    [HarmonyPatch(typeof(Accessible), nameof(Accessible.SetAccesses))]
    [HarmonyPrefix]
    public static void SetAccesses_Prefix(Accessible __instance, ref IEnumerable<Vector3> accesses)
    {
      var blockObject = __instance.GetComponent<BlockObject>();

      if (blockObject != null && HoomanStairsRegistry.TopBuildings.Contains(blockObject))
      {
        // SOLUCIÓN AL CRASHEO: Comprobamos que el Accessible que se está modificando 
        // sea EXCLUSIVAMENTE el de la entrada principal (BuildingAccessible), ignorando los secundarios.
        var buildingAccessible = __instance.GetComponent<Timberborn.Buildings.BuildingAccessible>();
        if (buildingAccessible != null && buildingAccessible.Accessible == __instance)
        {
          if (blockObject.PositionedEntrance != null)
          {
            Vector3 insideCoords = NavigationCoordinateSystem.GridToWorld(blockObject.PositionedEntrance.Coordinates);
            accesses = new List<Vector3> { insideCoords };
          }
        }
      }
    }
  }
}