using Bindito.Core;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  [Context("Game")]
  public class ModConfigurator : Configurator
  {

    protected override void Configure()
    {
      Debug.Log("[HoomanStairs] Configurator.Configure");

      // Bind the partial HoomanStairsManager as the Game Singleton
      Bind<HoomanStairsManager>().AsSingleton();
    }
  }
}