using HarmonyLib;
using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.EnterableSystem;
using Timberborn.Navigation;
using Timberborn.PathSystem; // Added so we can access ConnectionService directly
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
      // If it belongs to our custom group, ignore the request entirely.
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

    // FIX: Vanilla Edge-Case Crash
    // If a building's visual slots are disabled (e.g., by clipping into adjacent stacked walls), 
    // FixedSlotManager natively crashes. We bypass the crash and let SlotManager use its native unassigned pool.
    // (Leaving "OnEntererAdded" as a string just in case it is an explicit/private interface implementation)
    [HarmonyPatch(typeof(FixedSlotManager), "OnEntererAdded")]
    [HarmonyPrefix]
    public static bool FixedSlotManager_OnEntererAdded_Prefix(FixedSlotManager __instance, object sender, EntererAddedEventArgs e, SlotManager ____slotManager)
    {
      // ____slotManager allows us to safely access the private _slotManager field
      ____slotManager.AddEnterer(e.Enterer);
      return false; // Skip the original method so it never throws the exception
    }

    // Strongly-typed patch for ConnectionService.IsEntranceInDirectionAt
    [HarmonyPatch(typeof(ConnectionService), nameof(ConnectionService.IsEntranceInDirectionAt))]
    [HarmonyPostfix]
    public static void IsEntranceInDirectionAt_Postfix(Vector3Int entranceCoordinates, Vector3Int doorstepCoordinates, ref bool __result)
    {
      if (__result) return;

      if (HoomanStairsRegistry.FakePathEdges.Contains(new EdgeKey(entranceCoordinates, doorstepCoordinates)))
      {
        __result = true;
      }
    }

    // --- NEW: Idle Wander Crash Fix ---
    // Strongly-typed patch to intercept the out-of-bounds crash when an idle beaver is trapped inside the stairs.
    [HarmonyPatch(typeof(DistrictRandomDestinationPicker), nameof(DistrictRandomDestinationPicker.GetRandomDestination), new Type[] { typeof(District), typeof(Vector3) })]
    [HarmonyFinalizer]
    public static Exception GetRandomDestination_Finalizer(Exception __exception, ref Vector3 __result, Vector3 coordinates)
    {
      // If the vanilla game crashes because the list of valid destinations was completely empty...
      if (__exception is ArgumentOutOfRangeException)
      {
        // We safely swallow the exception and tell the beaver to just stand still
        // at their current coordinates until a new task is assigned.
        __result = coordinates;

        // Returning null suppresses the exception so the game doesn't crash!
        return null;
      }

      // If it was some other unexpected error, let it crash normally so we don't hide real bugs.
      return __exception;
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