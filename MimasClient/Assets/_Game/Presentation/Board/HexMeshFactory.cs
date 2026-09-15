using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Builds pointy-top hexagonal prism meshes for board tiles. Flat-shaded by construction: the top
    /// face is a 6-triangle fan with a single up normal, and each of the six side quads carries its own
    /// four vertices with one outward normal, so no lighting seam bleeds between faces. There is no
    /// bottom face — tiles are never seen from below.
    /// The factory deliberately caches nothing: distinct prism heights are few, so the caller
    /// (<see cref="BoardView"/>) owns the cache and the mesh lifetime.
    /// </summary>
    public static class HexMeshFactory
    {
        /// <summary>Sides of a hexagon. Named so index arithmetic below reads as geometry, not as a magic 6.</summary>
        public const int SideCount = 6;

        private const float MinRadius = 1e-4f;
        private const float MinHeight = 1e-3f;

        private const int TopVertexCount = SideCount + 1;
        private const int SideVertexCount = SideCount * 4;
        private const int TriangleIndexCount = (SideCount * 3) + (SideCount * 2 * 3);

        /// <summary>
        /// Creates a prism whose top face sits at y = <paramref name="height"/> and whose base sits at y = 0,
        /// centred on the local origin. <paramref name="radius"/> is the circumradius (centre to corner),
        /// matching the tile size used by <see cref="HexLayout"/>.
        /// The returned mesh is owned by the caller — destroy it when the board is torn down.
        /// </summary>
        public static Mesh CreatePrism(float radius, float height, string meshName)
        {
            radius = Mathf.Max(radius, MinRadius);
            height = Mathf.Max(height, MinHeight);

            Vector3[] corners = new Vector3[SideCount];
            for (int i = 0; i < SideCount; i++) corners[i] = HexLayout.Corner(i, radius);

            var vertices = new List<Vector3>(TopVertexCount + SideVertexCount);
            var normals = new List<Vector3>(TopVertexCount + SideVertexCount);
            var uvs = new List<Vector2>(TopVertexCount + SideVertexCount);
            var triangles = new List<int>(TriangleIndexCount);

            AppendTopFace(vertices, normals, uvs, triangles, corners, radius, height);
            AppendSideBand(vertices, normals, uvs, triangles, corners, height);

            var mesh = new Mesh();
            mesh.name = string.IsNullOrEmpty(meshName) ? "HexPrism" : meshName;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Overload with a generated mesh name.</summary>
        public static Mesh CreatePrism(float radius, float height) => CreatePrism(radius, height, null);

        private static void AppendTopFace(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            Vector3[] corners, float radius, float height)
        {
            int centre = vertices.Count;
            vertices.Add(new Vector3(0f, height, 0f));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            float inverseDiameter = 1f / (2f * radius);
            for (int i = 0; i < SideCount; i++)
            {
                Vector3 c = corners[i];
                vertices.Add(new Vector3(c.x, height, c.z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + c.x * inverseDiameter, 0.5f + c.z * inverseDiameter));
            }

            // Corners advance counter-clockwise when read from +Y, so the fan must be wound
            // centre -> next -> current for the face normal to come out as +Y under Unity's
            // clockwise-is-front convention.
            for (int i = 0; i < SideCount; i++)
            {
                int current = centre + 1 + i;
                int next = centre + 1 + ((i + 1) % SideCount);
                triangles.Add(centre);
                triangles.Add(next);
                triangles.Add(current);
            }
        }

        private static void AppendSideBand(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            Vector3[] corners, float height)
        {
            for (int i = 0; i < SideCount; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % SideCount];
                Vector3 outward = new Vector3(a.x + b.x, 0f, a.z + b.z).normalized;

                int quad = vertices.Count;
                vertices.Add(new Vector3(a.x, 0f, a.z));
                vertices.Add(new Vector3(a.x, height, a.z));
                vertices.Add(new Vector3(b.x, height, b.z));
                vertices.Add(new Vector3(b.x, 0f, b.z));

                for (int n = 0; n < 4; n++) normals.Add(outward);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));

                triangles.Add(quad);
                triangles.Add(quad + 1);
                triangles.Add(quad + 2);

                triangles.Add(quad);
                triangles.Add(quad + 2);
                triangles.Add(quad + 3);
            }
        }
    }
}
