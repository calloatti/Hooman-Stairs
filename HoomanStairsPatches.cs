using HarmonyLib;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.EnterableSystem;
using Timberborn.Navigation;
using Timberborn.SlotSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  [HarmonyPatch]
  public static class HoomanStairsPatches
  {
    // FIX: Use string-based patching because NavMeshSource is 'internal'
    // This targets Timberborn.Navigation.NavMeshSource.BlockEdge(int, int, int)
    [HarmonyPatch("Timberborn.Navigation.NavMeshSource", "BlockEdge")]
    [HarmonyPrefix]
    public static bool BlockEdge_Source_Prefix(int startNodeId, int endNodeId, int groupId)
    {
      // If it belongs to our custom group, ignore the request entirely.
      return groupId != HoomanStairsManager.StairsGroupId;
    }

    // FIX: Mirror the block logic to prevent the "it wasn't blocked" exception
    // Targets Timberborn.Navigation.NavMeshSource.UnblockEdge(int, int, int)
    [HarmonyPatch("Timberborn.Navigation.NavMeshSource", "UnblockEdge")]
    [HarmonyPrefix]
    public static bool UnblockEdge_Source_Prefix(int startNodeId, int endNodeId, int groupId)
    {
      return groupId != HoomanStairsManager.StairsGroupId;
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

    // FIX: Vanilla Edge-Case Crash
    // If a building's visual slots are disabled (e.g., by clipping into adjacent stacked walls), 
    // FixedSlotManager natively crashes. We bypass the crash and let SlotManager use its native unassigned pool.
    [HarmonyPatch(typeof(FixedSlotManager), "OnEntererAdded")]
    [HarmonyPrefix]
    public static bool FixedSlotManager_OnEntererAdded_Prefix(FixedSlotManager __instance, object sender, EntererAddedEventArgs e, SlotManager ____slotManager)
    {
      // ____slotManager allows us to safely access the private _slotManager field
      ____slotManager.AddEnterer(e.Enterer);
      return false; // Skip the original method so it never throws the exception
    }

    [HarmonyPatch("Timberborn.PathSystem.ConnectionService", "IsEntranceInDirectionAt")]
    [HarmonyPostfix]
    public static void IsEntranceInDirectionAt_Postfix(Vector3Int entranceCoordinates, Vector3Int doorstepCoordinates, ref bool __result)
    {
      if (__result) return;

      if (HoomanStairsRegistry.FakePathEdges.Contains(new EdgeKey(entranceCoordinates, doorstepCoordinates)))
      {
        __result = true;
      }
    }

    /*
    // Commented out to prevent conflict with HoomanStairsManager caching the un-shifted road node
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
    */
  }
}