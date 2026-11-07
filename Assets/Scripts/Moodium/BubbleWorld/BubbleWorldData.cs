using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Point-cloud data for the Nature "bubble world" (converted offline from a Gaussian splat by
    /// Source/tools/splat_convert.py) and the meshes built from it.
    ///
    /// points.bytes (little endian):
    ///   char[4] "MSPC", int32 version (1), int32 count, float32 eyeHeight, float32 maxRadius,
    ///   count × { float32 x, y, z, half size, byte r, g, b, a }  — Unity frame, metres, sorted near → far.
    /// Point i takes its colour from texel (i % S, i / S) of point_colors.png (row 0 at the top), so the
    /// UV0 samples display colors; UV1 carries seed/sway, and float vertex colors encode a shared
    /// per-splat center for rigid GPU motion through a PolySpatial-compatible Shader Graph.
    /// </summary>
    public static class BubbleWorldData
    {
        // One folder per world under Resources/. Each holds points.bytes, point_colors.png, panorama.png and the
        // baked Meshes/ folder; the bubble film is shared.
        public const string NatureFolder = "MoodiumBubbleWorld/";
        public const string NestedNatureFolder = "MoodiumBubbleWorld/NestedNature/";
        /// <summary>
        /// 枫林世界 (2026-11-03): the third Nature world, an autumn hillside of red maples. Converted from a Hunyuan
        /// Gaussian PLY that came without its panorama, so up was found by levelling the far horizon and the panorama is
        /// rendered from the Gaussians (frame.json). +z, the view through the door, faces the big red maple.
        /// </summary>
        public const string MapleNatureFolder = "MoodiumBubbleWorld/MapleNature/";
        public const string CandyFolder = "MoodiumBubbleWorld/Candy/";
        public const string NestedCandyFolder = "MoodiumBubbleWorld/Candy/Nested/";
        public const string FilmResource = NatureFolder + "bubble_film";

        public static string FolderFor(bool candyWorld) => candyWorld ? CandyFolder : NatureFolder;
        public static string PointsResource(string folder) => folder + "points";
        public static string ColorsResource(string folder) => folder + "point_colors";
        public static string RoundColorsResource(string folder) => folder + "point_colors_round";
        public static string PanoramaResource(string folder) => folder + "panorama";
        /// <summary>64×32 heavily blurred copy of panorama.png: colour only, no photo detail (BubbleWorldController.SkyStyle.Soft).</summary>
        public static string SoftPanoramaResource(string folder) => folder + "panorama_soft";
        public static string MeshResource(string folder, string meshName) => folder + "Meshes/" + meshName;
        public const int LayerCount = 20;
        public const int RecordSize = 18;

        /// <summary>
        /// All exports are canonical. Nested frame.json records the offline
        /// rotation AND handedness conversion, which cannot be represented by a quaternion.
        /// </summary>
        public static Quaternion SourceFrameRotation(string folder)
        {
            return Quaternion.identity;
        }

        public sealed class Cloud
        {
            public int Count;
            public float EyeHeight;
            public float MaxRadius;
            public Vector3[] Positions;
            public float[] Sizes;
            public Color32[] Colors;
        }

        public static string LayerMeshName(int layer) => $"BubbleWorld_Layer_{layer:00}";
        public const string PanoramaMeshName = "BubbleWorld_PanoramaSphere";

        public static bool TryParse(byte[] bytes, out Cloud cloud)
        {
            cloud = null;
            if (bytes == null || bytes.Length < 20 || bytes[0] != 'M' || bytes[1] != 'S' || bytes[2] != 'P' || bytes[3] != 'C')
                return false;
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.ReadBytes(4);
            var version = reader.ReadInt32();
            var count = reader.ReadInt32();
            if (version != 1 || count <= 0 || bytes.Length < 20 + (long)count * RecordSize) return false;
            cloud = new Cloud
            {
                Count = count,
                EyeHeight = reader.ReadSingle(),
                MaxRadius = reader.ReadSingle(),
                Positions = new Vector3[count],
                Sizes = new float[count],
                Colors = new Color32[count]
            };
            for (var i = 0; i < count; i++)
            {
                cloud.Positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                cloud.Sizes[i] = Mathf.HalfToFloat(reader.ReadUInt16());
                cloud.Colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            }
            return true;
        }

        /// <summary>
        /// Reads only the header and the last point of each layer (points are sorted near → far), so the runtime can
        /// plan the reveal without parsing 200k records when the meshes are already baked.
        /// </summary>
        public static bool TryReadSummary(byte[] bytes, int layers, out int count, out float eyeHeight, out float[] layerOuterRadii)
        {
            count = 0; eyeHeight = 0f; layerOuterRadii = null;
            if (bytes == null || bytes.Length < 20 || bytes[0] != 'M' || bytes[1] != 'S' || bytes[2] != 'P' || bytes[3] != 'C')
                return false;
            if (BitConverter.ToInt32(bytes, 4) != 1) return false;
            count = BitConverter.ToInt32(bytes, 8);
            if (count <= 0 || bytes.Length < 20 + (long)count * RecordSize) return false;
            eyeHeight = BitConverter.ToSingle(bytes, 12);
            layerOuterRadii = new float[layers];
            for (var l = 0; l < layers; l++)
            {
                LayerRange(l, count, layers, out var s, out var e);
                if (e <= s) continue;
                var offset = 20 + (e - 1) * RecordSize;
                var p = new Vector3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));
                layerOuterRadii[l] = p.magnitude;
            }
            return true;
        }

        /// <summary>Point index range [start, end) of a layer. Points are sorted near → far, so layers are distance shells.</summary>
        public static void LayerRange(int layer, int count, int layers, out int start, out int end)
        {
            layers = Mathf.Max(1, layers);
            start = (int)((long)count * layer / layers);
            end = (int)((long)count * (layer + 1) / layers);
        }

        /// <summary>UV of the texel that holds point <paramref name="index"/>'s colour.</summary>
        public static Vector2 ColorUV(int index, int atlasSize)
        {
            var x = index % atlasSize;
            var y = index / atlasSize;
            return new Vector2((x + .5f) / atlasSize, 1f - (y + .5f) / atlasSize);
        }

        public static int AtlasSizeFor(int count)
        {
            var size = 1;
            while (size * size < count) size *= 2;
            return size;
        }

        public static int RoundAtlasSizeFor(int count)
        {
            var size = 4;
            while ((long)(size / 4) * (size / 4) < count) size *= 2;
            return size;
        }

        public static void WriteRoundUVs(Vector2[] uvs, int offset, int index, int atlasSize)
        {
            // Share the model atlas layout helper, using one block per scan point rather than own/glow pairs.
            Moodium.PointClouds.PointCloudFx.WriteBlockUVs(uvs, offset, index / 2, index % 2 != 0, atlasSize);
        }

        public static Texture2D BuildRoundAtlas(Cloud cloud)
        {
            var size = RoundAtlasSizeFor(cloud.Count);
            var pixels = new Color32[size * size];
            var perRow = size / 4;
            for (var i = 0; i < cloud.Count; i++)
            {
                var color = cloud.Colors[i];
                var x0 = i % perRow * 4;
                var y0 = i / perRow * 4;
                for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                {
                    color.a = (byte)Mathf.RoundToInt(Moodium.PointClouds.PointCloudFx.BlobAlpha(x, y) * 255f);
                    pixels[(y0 + y) * size + x0 + x] = color;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
                { name = "Bubble World Round Colours", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        public static void ConfigureRoundPoints(Material material)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        /// <summary>
        /// One soft round textured quad per point, facing the scan centre (where the viewer stands), turned by a
        /// per-point angle. Front faces point at the centre.
        /// </summary>
        public static bool IsCandy(string folder) => folder == CandyFolder || folder == NestedCandyFolder;
        public static bool IsNature(string folder) => folder == NatureFolder || folder == NestedNatureFolder || folder == MapleNatureFolder;

        public static Mesh BuildLayerMesh(Cloud cloud, int start, int end, string name, float sizeScale = 1f, bool candy = false)
        {
            var atlas = RoundAtlasSizeFor(cloud.Count);
            var n = Mathf.Max(0, end - start);
            var vertices = new Vector3[n * 4];
            var uvs = new Vector2[n * 4];
            var motionUVs = new Vector2[n * 4];
            var centers = new Color[n * 4];
            var indices = new int[n * 6];
            for (var k = 0; k < n; k++)
            {
                var i = start + k;
                var c = cloud.Positions[i];
                var toCentre = -c.normalized;
                var helper = Mathf.Abs(toCentre.y) > .95f ? Vector3.forward : Vector3.up;
                var t = Vector3.Cross(helper, toCentre).normalized;
                var b = Vector3.Cross(toCentre, t);
                var angle = Hash01(i) * Mathf.PI * .5f;
                var ca = Mathf.Cos(angle); var sa = Mathf.Sin(angle);
                var tr = (t * ca + b * sa) * (cloud.Sizes[i] * .5f * sizeScale);
                var br = (-t * sa + b * ca) * (cloud.Sizes[i] * .5f * sizeScale);
                var v = k * 4;
                vertices[v] = c - tr - br;
                vertices[v + 1] = c + tr - br;
                vertices[v + 2] = c + tr + br;
                vertices[v + 3] = c - tr + br;
                WriteRoundUVs(uvs, v, i, atlas);
                var motion = new Vector2(MotionSeed(i), BubbleSplatMotionMath.Sway(c, cloud.Colors[i], cloud.EyeHeight, candy));
                var center = BubbleSplatMotionMath.EncodeCenter(c);
                for (var corner = 0; corner < 4; corner++) { motionUVs[v + corner] = motion; centers[v + corner] = center; }
                var q = k * 6;
                indices[q] = v; indices[q + 1] = v + 1; indices[q + 2] = v + 2;
                indices[q + 3] = v; indices[q + 4] = v + 2; indices[q + 5] = v + 3;
            }
            var mesh = new Mesh { name = name };
            if (vertices.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.uv2 = motionUVs;
            mesh.colors = centers;
            mesh.triangles = indices;
            if (n > 0 && !FacesTowards(mesh, 0, -cloud.Positions[start])) FlipWinding(mesh);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(BubbleSplatMotionMath.MaxDisplacement * 2);
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>
        /// Unit sphere whose front faces point inwards, with u = 0.5 + atan2(x, z) / 2π and v = 0.5 + asin(y) / π —
        /// the same equirect mapping the converter used for panorama.png, so sky and points line up.
        /// </summary>
        public static Mesh BuildPanoramaSphere(int longitude = 96, int latitude = 48)
        {
            var vertices = new Vector3[(longitude + 1) * (latitude + 1)];
            var uvs = new Vector2[vertices.Length];
            for (var j = 0; j <= latitude; j++)
            {
                var lat = (j / (float)latitude - .5f) * Mathf.PI;
                for (var i = 0; i <= longitude; i++)
                {
                    var lon = (i / (float)longitude - .5f) * Mathf.PI * 2f;
                    var index = j * (longitude + 1) + i;
                    vertices[index] = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
                    uvs[index] = new Vector2(i / (float)longitude, j / (float)latitude);
                }
            }
            var triangles = new int[longitude * latitude * 6];
            var t = 0;
            for (var j = 0; j < latitude; j++)
            for (var i = 0; i < longitude; i++)
            {
                var a = j * (longitude + 1) + i;
                var b = a + longitude + 1;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
            var mesh = new Mesh { name = PanoramaMeshName, vertices = vertices, uv = uvs, triangles = triangles };
            // Pick a triangle away from the poles and make sure it faces the centre.
            var probe = (latitude / 2 * longitude + 1) * 6 / 3;
            if (!FacesTowards(mesh, probe, -mesh.vertices[mesh.triangles[probe * 3]])) FlipWinding(mesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Does triangle <paramref name="triangle"/>'s front face (Unity: Cross(b − a, c − a)) point along <paramref name="direction"/>?</summary>
        public static bool FacesTowards(Mesh mesh, int triangle, Vector3 direction)
        {
            var tris = mesh.triangles;
            var v = mesh.vertices;
            var a = v[tris[triangle * 3]]; var b = v[tris[triangle * 3 + 1]]; var c = v[tris[triangle * 3 + 2]];
            return Vector3.Dot(Vector3.Cross(b - a, c - a), direction) > 0f;
        }

        static void FlipWinding(Mesh mesh)
        {
            var tris = mesh.triangles;
            for (var i = 0; i < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
            mesh.triangles = tris;
        }

        public static float MotionSeed(int i) => Hash01(i) * (65535f / 65536f);

        static float Hash01(int i)
        {
            unchecked
            {
                var h = (uint)i * 2654435761u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>URP/Unlit material (the shader this project already relies on for panoramas on device).</summary>
        public static Material CreateUnlit(string name, Texture texture, bool transparent, CullMode cull, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)cull);
            if (transparent) Moodium.Opening.OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(material);
            else
            {
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            }
            return material;
        }

        public static void SetAlpha(Material material, float alpha)
        {
            if (material == null) return;
            var c = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            c.a = Mathf.Clamp01(alpha);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            if (material.HasProperty("_Color")) material.SetColor("_Color", c);
        }

        /// <summary>Largest distance from the centre in each layer (near → far).</summary>
        public static float[] LayerOuterRadii(Cloud cloud, int layers)
        {
            var radii = new float[layers];
            for (var l = 0; l < layers; l++)
            {
                LayerRange(l, cloud.Count, layers, out var s, out var e);
                radii[l] = e > s ? cloud.Positions[e - 1].magnitude : 0f;
            }
            return radii;
        }
    }
}
