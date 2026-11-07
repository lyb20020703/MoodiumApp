using UnityEngine;

namespace Moodium.PointClouds
{
    /// <summary>
    /// A prefab baked into surface points (Moodium ▸ Point Cloud Models ▸ Bake Selected Prefabs). Lives at
    /// Resources/MoodiumPointClouds/&lt;prefab name&gt;.asset with its chunk meshes and colour atlas as sub-assets.
    /// Positions are in the prefab root's local space, so the points follow the spawned instance's pose and scale.
    /// Points are sorted bottom → top, and each chunk is a contiguous height slab (appear order and touch locality).
    /// </summary>
    public sealed class PointCloudModelAsset : ScriptableObject
    {
        public string SourcePrefab;
        public bool Foliage;                 // trees: larger touch radius, gentle sway
        public float PointSize;              // quad edge length (root-local metres)
        public float Height;                 // root-local height of the baked points
        public Vector3[] Positions;
        public Vector3[] Normals;            // oriented outward
        public float[] Height01;             // 0 = lowest point, 1 = highest
        public int[] ChunkStart;             // ChunkStart[c] .. ChunkStart[c + 1] (last = Positions.Length)
        public Bounds[] ChunkBounds;         // rest bounds (root-local)
        public Mesh[] Chunks;                // static rest meshes: 4 vertices / 2 triangles per point
        public Texture2D Atlas;              // two soft 4×4 blocks per point: own colour, glow colour

        public int Count => Positions != null ? Positions.Length : 0;
        public int ChunkCount => Chunks != null ? Chunks.Length : 0;

        public void ChunkRange(int chunk, out int start, out int end)
        {
            start = ChunkStart[chunk];
            end = chunk + 1 < ChunkStart.Length ? ChunkStart[chunk + 1] : Count;
        }

        public const string ResourceFolder = "MoodiumPointClouds/";

        public static PointCloudModelAsset Load(string prefabName) =>
            string.IsNullOrEmpty(prefabName) ? null : Resources.Load<PointCloudModelAsset>(ResourceFolder + prefabName);

        /// <summary>Writes one chunk's 4 corners per point (rest pose unless offsets/sizes are given).</summary>
        public void WriteChunkVertices(int chunk, Vector3[] vertices, Vector3[] positions = null, float[] sizes = null)
        {
            ChunkRange(chunk, out var start, out var end);
            var half = PointSize * .5f;
            for (var i = start; i < end; i++)
            {
                var size = sizes != null ? sizes[i] : 1f;
                var p = positions != null ? positions[i] : Positions[i];
                PointCloudFx.QuadAxes(i, Normals[i], half * size, out var a, out var b);
                var v = (i - start) * 4;
                vertices[v] = p - a - b;
                vertices[v + 1] = p + a - b;
                vertices[v + 2] = p + a + b;
                vertices[v + 3] = p - a + b;
            }
        }

        /// <summary>Builds a chunk mesh (used by the baker; also the runtime fallback).</summary>
        public Mesh BuildChunkMesh(int chunk, string name)
        {
            ChunkRange(chunk, out var start, out var end);
            var n = end - start;
            var vertices = new Vector3[n * 4];
            var uvs = new Vector2[n * 4];
            var indices = new int[n * 6];
            WriteChunkVertices(chunk, vertices);
            var atlas = Atlas != null ? Atlas.width : PointCloudFx.AtlasSizeFor(Count);
            for (var k = 0; k < n; k++)
            {
                PointCloudFx.WriteBlockUVs(uvs, k * 4, start + k, false, atlas, true, Count);
                var v = k * 4;
                var q = k * 6;
                indices[q] = v; indices[q + 1] = v + 1; indices[q + 2] = v + 2;
                indices[q + 3] = v; indices[q + 4] = v + 2; indices[q + 5] = v + 3;
            }
            var mesh = new Mesh { name = name };
            if (vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            // Grow bounds so scattered points are never culled.
            var b = mesh.bounds;
            b.Expand(PointCloudFx.TouchPush * 2f + PointCloudFx.AppearSpread * 2f);
            mesh.bounds = b;
            return mesh;
        }
    }
}
