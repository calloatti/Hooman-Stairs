using Bindito.Core;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  [Context("Game")]
  public class HoomanStairsConfigurator : Configurator
  {
    public static readonly string Prefix = "[HoomanStairs]";

    protected override void Configure()
    {
      Debug.Log($"{Prefix} Configurator: Initializing Systems...");

      // Bind the partial HoomanStairsManager as the Game Singleton
      Bind<HoomanStairsManager>().AsSingleton();
    }
  }
}