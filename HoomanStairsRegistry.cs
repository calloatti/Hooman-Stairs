using System.Collections.Generic;
using Timberborn.BlockSystem;
using UnityEngine;

namespace Calloatti.HoomanStairs
{
  // Estructura libre de basura (GC-free) para identificar una arista sin importar su dirección
  public struct EdgeKey
  {
    public Vector3Int A;
    public Vector3Int B;
    public EdgeKey(Vector3Int a, Vector3Int b) { A = a; B = b; }
    public override int GetHashCode() => A.GetHashCode() ^ B.GetHashCode();
    public override bool Equals(object obj)
    {
      if (!(obj is EdgeKey)) return false;
      EdgeKey o = (EdgeKey)obj;
      return (A == o.A && B == o.B) || (A == o.B && B == o.A);
    }
  }

  public static class HoomanStairsRegistry
  {
    // Usamos diccionarios para contar cuántas escaleras usan el mismo nodo/arista
    public static readonly Dictionary<int, int> StairNodeIds = new Dictionary<int, int>();
    public static readonly Dictionary<EdgeKey, int> TunnelEdges = new Dictionary<EdgeKey, int>();

    // Registramos los edificios superiores para cambiarles la puerta hacia adentro
    public static readonly HashSet<BlockObject> TopBuildings = new HashSet<BlockObject>();

    // NEW: Save the exact pair of coordinates for the visual path connection
    public static readonly HashSet<EdgeKey> FakePathEdges = new HashSet<EdgeKey>();

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

    public static void AddEdge(EdgeKey key)
    {
      if (!TunnelEdges.ContainsKey(key)) TunnelEdges[key] = 0;
      TunnelEdges[key]++;
    }

    public static void RemoveEdge(EdgeKey key)
    {
      if (TunnelEdges.ContainsKey(key))
      {
        TunnelEdges[key]--;
        if (TunnelEdges[key] <= 0) TunnelEdges.Remove(key);
      }
    }
  }
}