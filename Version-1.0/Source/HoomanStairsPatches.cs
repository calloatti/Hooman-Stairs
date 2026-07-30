using HarmonyLib;
using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.Common;
using Timberborn.Coordinates;
using Timberborn.EnterableSystem;
using Timberborn.Navigation;
using Timberborn.SlotSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  [HarmonyPatch]
  public static class HoomanStairsPatches
  {
    // Strongly-typed patch for NavMeshSource.BlockEdge
    [HarmonyPatch(typeof(NavMeshSource), nameof(NavMeshSource.BlockEdge))]
    [HarmonyPrefix]
    public static bool BlockEdge_Source_Prefix(int startNodeId, int endNodeId, int groupId)
    {
      return groupId != HoomanStairsManager.StairsGroupId;
    }

    // Strongly-typed patch for NavMeshSource.UnblockEdge
    [HarmonyPatch(typeof(NavMeshSource), nameof(NavMeshSource.UnblockEdge))]
    [HarmonyPrefix]
    public static bool UnblockEdge_Source_Prefix(int startNodeId, int endNodeId, int groupId)
    {
      return groupId != HoomanStairsManager.StairsGroupId;
    }

    // Strongly-typed patch for DistrictObstacleService.IsSetObstacle
    [HarmonyPatch(typeof(DistrictObstacleService), nameof(DistrictObstacleService.IsSetObstacle))]
    [HarmonyPostfix]
    public static void IsSetObstacle_Postfix(int nodeId, ref bool __result)
    {
      if (__result && HoomanStairsRegistry.StairNodeIds.ContainsKey(nodeId))
      {
        __result = false;
      }
    }

    [HarmonyPatch(typeof(FixedSlotManager), "OnEntererAdded")]
    [HarmonyPrefix]
    public static bool FixedSlotManager_OnEntererAdded_Prefix(FixedSlotManager __instance, object sender, EntererAddedEventArgs e, SlotManager ____slotManager)
    {
      ____slotManager.AddEnterer(e.Enterer);
      return false;
    }

    [HarmonyPatch(typeof(DistrictRandomDestinationPicker), nameof(DistrictRandomDestinationPicker.GetRandomDestination), new Type[] { typeof(District), typeof(Vector3) })]
    [HarmonyFinalizer]
    public static Exception GetRandomDestination_Finalizer(Exception __exception, ref Vector3 __result, Vector3 coordinates)
    {
      if (__exception is ArgumentOutOfRangeException)
      {
        __result = coordinates;
        return null;
      }
      return __exception;
    }

    [HarmonyPatch(typeof(Accessible), nameof(Accessible.SetAccesses))]
    [HarmonyPrefix]
    public static bool Accessible_SetAccesses_Prefix(Accessible __instance, ref IEnumerable<Vector3> accesses)
    {
      var buildingAccessible = __instance.GetComponent<BuildingAccessible>();
      if (buildingAccessible != null && HoomanStairsRegistry.TopBuildings.Contains(buildingAccessible.GetComponent<BlockObject>()))
      {
        var blockObject = buildingAccessible.GetComponent<BlockObject>();
        if (blockObject?.PositionedEntrance != null)
        {
          accesses = Enumerables.One(CoordinateSystem.GridToWorldCentered(blockObject.PositionedEntrance.DoorstepCoordinates));
        }
      }
      return true;
    }

    }
}