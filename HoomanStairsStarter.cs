using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  public class HoomanStairsStarter : IModStarter
  {
    public static readonly string ModId = "calloatti.hoomanstairs";

    public void StartMod(IModEnvironment modEnvironment)
    {
      Debug.Log($"[{ModId}] Starting mod and applying Harmony patches...");

      // Instanciamos Harmony con el ID de nuestro mod y aplicamos los parches
      Harmony harmony = new Harmony(ModId);
      harmony.PatchAll();

      Debug.Log($"[{ModId}] Harmony patches applied.");
    }
  }
}