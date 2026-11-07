using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moodium.PointClouds;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes prefabs into surface point clouds for <see cref="PointCloudModel"/>:
/// Moodium ▸ Point Cloud Models ▸ Bake Selected Prefabs, or ▸ Bake Project Models (the trees, candy houses and honey jar
/// the worlds spawn today). Output: Assets/Resources/MoodiumPointClouds/&lt;prefab name&gt;.asset.
///
/// Sampling: LOD0 renderers only (MeshRenderer, and SkinnedMeshRenderer in its rest pose), points spread by surface
/// area, colour from each material's base texture × tint, cut-out leaf texels rejected, normals turned outward,
/// sorted bottom → top and split into height-slab chunks of <see cref="ChunkSize"/> points. Each point gets two soft
/// round 4×4 colour blocks (own / glow) in "&lt;prefab&gt; Point Colours.png" next to the asset.
/// </summary>
public static class PointCloudModelBaker
{
    const string OutputFolder = "Assets/Resources/" + PointCloudModelAsset.ResourceFolder;
    public const int ChunkSize = 4000;
    public const int DefaultPoints = 80000;
    public const int FoliagePoints = 140000;
    public const float PointSizeFactor = 1.4f;   // quad edge = spacing × factor; the round soft blob covers less than
                                                 // the square, so the quads overlap more to keep the surface closed
    const int MaxTextureRead = 1024;

    static readonly string[] ProjectModels =
    {
        "Assets/Animated Trees Package/Prefabs/Animated/Birches/Birch 1.prefab",
        "Assets/Animated Trees Package/Prefabs/Animated/Oaks/Oak 1.prefab",
        "Assets/prefab/CandyHouse.prefab",
        "Assets/prefab/CandyLittleHouse.prefab",
        "Assets/prefab/Honey Jar.prefab",
    };

    [MenuItem("Moodium/Point Cloud Models/Bake Project Models")]
    public static void BakeProjectModels()
    {
        foreach (var path in ProjectModels)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogWarning($"[Point Clouds] Missing {path}");
            else Bake(prefab);
        }
    }

    [MenuItem("Moodium/Point Cloud Models/Bake Selected Prefabs")]
    static void BakeSelected()
    {
        foreach (var prefab in Selection.gameObjects.Where(g => PrefabUtility.IsPartOfPrefabAsset(g)))
            Bake(prefab);
    }

    struct Tri
    {
        public Vector3 A, B, C;
        public Vector2 UA, UB, UC;
        public int Material;
        public float Area;
    }

    sealed class Surface
    {
        public Color[] Pixels;
        public int Width, Height;
        public Color Tint = Color.white;
        public bool AlphaTest;
        public float Cutoff = .5f;
        public bool Leaves;
    }

    public static PointCloudModelAsset Bake(GameObject prefab, int points = 0)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var tris = new List<Tri>();
            var surfaces = new List<Surface>();
            var textureCache = new Dictionary<Texture, (Color[], int, int)>();
            CollectTriangles(instance, tris, surfaces, textureCache);
            if (tris.Count == 0)
            {
                Debug.LogWarning($"[Point Clouds] {prefab.name}: no visible LOD0 meshes.");
                return null;
            }
            var foliage = surfaces.Any(s => s.Leaves);
            if (points <= 0) points = foliage ? FoliagePoints : DefaultPoints;

            // Area-weighted sampling with alpha rejection.
            var cumulative = new float[tris.Count];
            var total = 0f;
            for (var i = 0; i < tris.Count; i++) { total += tris[i].Area; cumulative[i] = total; }
            var oversample = surfaces.Any(s => s.AlphaTest) ? 60 : 1;
            var rng = new System.Random(12345);
            var pos = new List<Vector3>(points);
            var nrm = new List<Vector3>(points);
            var col = new List<Color32>(points);
            var tries = points * oversample;
            var attempts = 0;
            for (var k = 0; k < tries && pos.Count < points; k++)
            {
                attempts++;
                var r = (float)rng.NextDouble() * total;
                var t = System.Array.BinarySearch(cumulative, r);
                if (t < 0) t = ~t;
                t = Mathf.Min(t, tris.Count - 1);
                var tri = tris[t];
                var r1 = Mathf.Sqrt((float)rng.NextDouble());
                var r2 = (float)rng.NextDouble();
                float w0 = 1f - r1, w1 = r1 * (1f - r2), w2 = r1 * r2;
                var surface = surfaces[tri.Material];
                var c = surface.Tint;
                if (surface.Pixels != null)
                {
                    var uv = tri.UA * w0 + tri.UB * w1 + tri.UC * w2;
                    var x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * surface.Width), 0, surface.Width - 1);
                    var y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * surface.Height), 0, surface.Height - 1);
                    c *= surface.Pixels[y * surface.Width + x];
                }
                if (surface.AlphaTest && c.a < surface.Cutoff) continue;
                pos.Add(tri.A * w0 + tri.B * w1 + tri.C * w2);
                nrm.Add(Vector3.Cross(tri.B - tri.A, tri.C - tri.A).normalized);
                c.a = 1f;
                col.Add(c);
            }
            var n = pos.Count;
            var acceptance = n / (float)Mathf.Max(1, attempts);   // share of the surface that is not cut out

            // Sort bottom → top; orient normals outward.
            var order = Enumerable.Range(0, n).OrderBy(i => pos[i].y).ToArray();
            var minY = pos[order[0]].y;
            var maxY = pos[order[n - 1]].y;
            var height = Mathf.Max(1e-4f, maxY - minY);
            var medianY = pos[order[n / 2]].y;
            var asset = ScriptableObject.CreateInstance<PointCloudModelAsset>();
            asset.name = prefab.name;
            asset.SourcePrefab = AssetDatabase.GetAssetPath(prefab);
            asset.Foliage = foliage;
            asset.Height = height;
            asset.Positions = new Vector3[n];
            asset.Normals = new Vector3[n];
            asset.Height01 = new float[n];
            var colors = new Color32[n];
            for (var k = 0; k < n; k++)
            {
                var i = order[k];
                asset.Positions[k] = pos[i];
                asset.Normals[k] = PointCloudFx.OrientOutward(pos[i], nrm[i], medianY, height);
                asset.Height01[k] = (pos[i].y - minY) / height;
                colors[k] = col[i];
            }
            var visibleArea = total * acceptance;
            asset.PointSize = Mathf.Sqrt(visibleArea / n) * PointSizeFactor;

            // Atlas: two soft round 4×4 blocks per point (own colour, glow colour), bilinear, no mipmaps.
            var size = PointCloudFx.AtlasSizeFor(n, foliage);
            var atlas = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = prefab.name + " Point Colours", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var texels = new Color32[size * size];
            var perRow = size / 4;
            for (var k = 0; k < n; k++)
            {
                for (var glow = 0; glow < (foliage ? 1 : 2); glow++)
                {
                    var block = foliage ? k : k * 2 + glow;
                    var x0 = block % perRow * 4;
                    var y0 = block / perRow * 4;
                    var c = glow == 0 ? colors[k] : PointCloudFx.GlowColor(colors[k]);
                    for (var y = 0; y < 4; y++)
                    for (var x = 0; x < 4; x++)
                    {
                        c.a = (byte)Mathf.RoundToInt(PointCloudFx.BlobAlpha(x, y) * 255f);
                        texels[(y0 + y) * size + x0 + x] = c;
                    }
                }
            }
            if (foliage)
            {
                var block = n; var x0 = block % perRow * 4; var y0 = block / perRow * 4;
                for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
                { var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(PointCloudFx.BlobAlpha(x, y) * 255f)); texels[(y0+y)*size+x0+x] = c; }
            }
            atlas.SetPixels32(texels);
            atlas.Apply(false, false);
            // Saved as a PNG next to the asset (a 2048² RGBA texture inside a text-serialised .asset would add ~33 MB).
            Directory.CreateDirectory(OutputFolder);
            var atlasPath = OutputFolder + prefab.name + " Point Colours.png";
            File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;                       // blocks must never blend with their neighbours
            importer.filterMode = FilterMode.Bilinear;            // bilinear inside a block = the soft round blob
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            asset.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            // Height-slab chunks.
            var chunkCount = Mathf.CeilToInt(n / (float)ChunkSize);
            asset.ChunkStart = new int[chunkCount];
            asset.ChunkBounds = new Bounds[chunkCount];
            asset.Chunks = new Mesh[chunkCount];
            for (var c = 0; c < chunkCount; c++) asset.ChunkStart[c] = c * ChunkSize;
            for (var c = 0; c < chunkCount; c++)
            {
                asset.ChunkRange(c, out var s, out var e);
                var b = new Bounds(asset.Positions[s], Vector3.zero);
                for (var i = s + 1; i < e; i++) b.Encapsulate(asset.Positions[i]);
                asset.ChunkBounds[c] = b;
                asset.Chunks[c] = asset.BuildChunkMesh(c, $"{prefab.name} Points {c:00}");
            }

            // Save (replace) with the chunk meshes as sub-assets.
            var path = OutputFolder + prefab.name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            foreach (var mesh in asset.Chunks) AssetDatabase.AddObjectToAsset(mesh, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Point Clouds] acceptance {acceptance:P1}; {prefab.name}: {n} points (target {points}), trunk/leaf surfaces {(foliage ? "leaf" : "trunk")}, {chunkCount} chunks, world point size {asset.PointSize * 100f:0.0} cm (root-local), " +
                      $"{(foliage ? "foliage" : "solid")}, colour atlas {size}² → {path}");
            return asset;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static void CollectTriangles(GameObject root, List<Tri> tris, List<Surface> surfaces,
        Dictionary<Texture, (Color[], int, int)> textureCache)
    {
        var rootInverse = root.transform.worldToLocalMatrix;
        var excluded = new HashSet<Renderer>();
        foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = lod.GetLODs();
            for (var l = 1; l < lods.Length; l++)
                foreach (var r in lods[l].renderers) if (r != null) excluded.Add(r);
        }
        var materialIndex = new Dictionary<Material, int>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || excluded.Contains(renderer) || renderer is ParticleSystemRenderer) continue;
            Mesh mesh;
            Matrix4x4 toRoot;
            var temporary = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true);
                temporary = true;
                toRoot = rootInverse * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer)
            {
                mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                toRoot = rootInverse * renderer.transform.localToWorldMatrix;
            }
            else continue;
            if (mesh == null) continue;

            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var materials = renderer.sharedMaterials;
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = sub < materials.Length ? materials[sub] : null;
                if (material == null) continue;
                if (!materialIndex.TryGetValue(material, out var mi))
                {
                    mi = surfaces.Count;
                    materialIndex[material] = mi;
                    surfaces.Add(ReadSurface(material, textureCache));
                }
                var indices = mesh.GetTriangles(sub);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = toRoot.MultiplyPoint3x4(vertices[indices[i]]);
                    var b = toRoot.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    var c = toRoot.MultiplyPoint3x4(vertices[indices[i + 2]]);
                    var area = Vector3.Cross(b - a, c - a).magnitude * .5f;
                    if (area <= 1e-10f) continue;
                    tris.Add(new Tri
                    {
                        A = a, B = b, C = c, Area = area, Material = mi,
                        UA = uvs.Length > 0 ? uvs[indices[i]] : Vector2.zero,
                        UB = uvs.Length > 0 ? uvs[indices[i + 1]] : Vector2.zero,
                        UC = uvs.Length > 0 ? uvs[indices[i + 2]] : Vector2.zero,
                    });
                }
            }
            if (temporary) Object.DestroyImmediate(mesh);
        }
    }

    static Surface ReadSurface(Material material, Dictionary<Texture, (Color[], int, int)> cache)
    {
        var surface = new Surface();
        foreach (var name in new[] { "_BaseColor", "_Color" })
            if (material.HasProperty(name)) { surface.Tint = material.GetColor(name); break; }
        Texture texture = null;
        foreach (var name in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
            if (material.HasProperty(name) && (texture = material.GetTexture(name)) != null) break;
        surface.AlphaTest = material.IsKeywordEnabled("_ALPHATEST_ON") ||
                            (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f) ||
                            material.renderQueue == (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        if (material.HasProperty("_Cutoff")) surface.Cutoff = material.GetFloat("_Cutoff");
        var lower = (material.name + " " + (texture != null ? texture.name : "")).ToLowerInvariant();
        surface.Leaves = lower.Contains("leav") || lower.Contains("foliage");
        if (surface.Leaves) surface.AlphaTest = true;
        if (texture == null) return surface;
        if (!cache.TryGetValue(texture, out var read)) cache[texture] = read = ReadTexture(texture);
        (surface.Pixels, surface.Width, surface.Height) = read;
        return surface;
    }

    /// <summary>Reads any texture (readable or not) through a RenderTexture, at most 1024².</summary>
    static (Color[], int, int) ReadTexture(Texture texture)
    {
        var scale = Mathf.Min(1f, MaxTextureRead / (float)Mathf.Max(texture.width, texture.height));
        var w = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
        var h = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;
        var readable = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = readable.GetPixels();
        Object.DestroyImmediate(readable);
        return (pixels, w, h);
    }
}
