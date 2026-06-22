using System.Collections.Generic;
using UnityEngine;
using Timberborn.BlockSystem;

namespace Calloatti.HoomanStairs
{
  public partial class HoomanStairsManager
  {
    private class PathRenderer : MonoBehaviour
    {
      private Material _mat;
      private Mesh _cubeWireMesh;
      private readonly Dictionary<BlockObject, List<Vector3>> _persistentPaths = new Dictionary<BlockObject, List<Vector3>>();

      private bool _drawNodes;
      private bool _drawLines;
      private bool _drawCarving;

      void Awake()
      {
        // 1. Shader básico para dibujar colores planos.
        _mat = new Material(Shader.Find("Hidden/Internal-Colored"));
        _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        // 2. CONSTRUCCIÓN MANUAL DEL CUBO (Solo 12 aristas, sin diagonales).
        _cubeWireMesh = new Mesh();
        _cubeWireMesh.vertices = new Vector3[] {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),  new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),  new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),   new Vector3(-0.5f, 0.5f, 0.5f)
        };

        // Definimos las conexiones de las líneas para formar el cubo.
        _cubeWireMesh.SetIndices(new int[] {
            0,1, 1,2, 2,3, 3,0, // Base inferior
            4,5, 5,6, 6,7, 7,4, // Base superior
            0,4, 1,5, 2,6, 3,7  // Conexiones verticales
        }, MeshTopology.Lines, 0);
      }

      public void UpdatePathData(BlockObject owner, List<Vector3> points, bool nodes, bool lines, bool carving)
      {
        _persistentPaths[owner] = new List<Vector3>(points);
        _drawNodes = nodes;
        _drawLines = lines;
        _drawCarving = carving;
      }

      public void ClearPathData(BlockObject owner) => _persistentPaths.Remove(owner);

      void OnRenderObject()
      {
        if (_persistentPaths.Count == 0) return;
        _mat.SetPass(0);

        foreach (var points in _persistentPaths.Values)
        {
          for (int i = 0; i < points.Count; i++)
          {
            // CARVING: Dibuja la jaula de tamaño bloque completo (1.0).
            if (_drawCarving)
            {
              Graphics.DrawMeshNow(_cubeWireMesh, Matrix4x4.TRS(points[i], Quaternion.identity, Vector3.one));
            }
            // NODOS: Solo si carving está apagado, dibuja un cubito pequeño.
            else if (_drawNodes)
            {
              Graphics.DrawMeshNow(_cubeWireMesh, Matrix4x4.TRS(points[i], Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f)));
            }

            // LÍNEAS: Ahora usamos GL directo para que sean hilos finos y limpios.
            if (_drawLines && i < points.Count - 1)
            {
              GL.Begin(GL.LINES);
              GL.Color(Color.white);
              GL.Vertex(points[i]);
              GL.Vertex(points[i + 1]);
              GL.End();
            }
          }
        }
      }

      void OnDestroy()
      {
        if (_cubeWireMesh != null) Destroy(_cubeWireMesh);
        if (_mat != null) Destroy(_mat);
      }
    }
  }
}