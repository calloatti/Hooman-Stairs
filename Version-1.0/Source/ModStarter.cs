using HarmonyLib;
using Timberborn.ModManagerScene;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  public class ModStarter : IModStarter
  {

    public void StartMod(IModEnvironment modEnvironment)
    {
      Debug.Log("[HoomanStairs] IModStarter.StartMod");

      Harmony harmony = new Harmony("calloatti.hoomanstairs");
      harmony.PatchAll();

    }
  }
}