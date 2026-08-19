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

new Harmony("Calloatti.HoomanStairs").PatchAll();

    }
  }
}