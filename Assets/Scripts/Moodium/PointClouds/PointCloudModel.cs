using System;
using System.Collections.Generic;
using Moodium.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.PointClouds
{
    /// <summary>
    /// Shows a spawned prefab (tree, candy house, honey jar…) as its baked point cloud instead of its mesh:
    /// points fly in and settle bottom → top when it appears, and points near a fingertip burst out of the surface,
    /// glow, then spring back. Rendering is the stock URP/Unlit shader on static baked meshes (PolySpatial/RealityKit
    /// safe); only chunks that are currently moving get a dynamic copy whose vertices are rewritten, and they go back
    /// to the static mesh as soon as they settle.
    /// The original renderers are disabled (colliders, Animator and other components keep working).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PointCloudModel : MonoBehaviour
    {
        static readonly XRHandJointID[] Tips =
        {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        [SerializeField, Range(15f, 90f)] float m_UpdateRate = 45f;
        [SerializeField, Min(0f)] float m_TouchedCooldown = .25f;
        [SerializeField] bool m_TouchEnabled = true;
        [Header("Foliage sway (trees)")]
        [SerializeField, Range(0f, 4f)] float m_SwayDegrees = 1.2f;

        /// <summary>World position of a new touch (for sparkles / sound). Rate-limited by the cooldown.</summary>
        public event Action<Vector3> Touched;
        public event Action AppearFinished;

        public PointCloudModelAsset Asset => m_Asset;
        public bool Appearing => m_AppearTime >= 0f;

        PointCloudModelAsset m_Asset;
        Transform m_Points;
        Material m_Material;
        readonly List<Renderer> m_HiddenRenderers = new();
        readonly List<LODGroup> m_DisabledLodGroups = new();

        sealed class Chunk
        {
            public GameObject Go;
            public MeshFilter Filter;
            public Mesh Dynamic;
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public bool Touching;
            public bool Moving;
        }

        Chunk[] m_Chunks;
        Vector3[] m_Offset, m_Velocity, m_Work;
        float[] m_Size;
        float m_AppearTime = -1f;
        float m_NextStep, m_LastTouched = -10f, m_SwayPhase;

        XRHandSubsystem m_Hands;
        XROrigin m_Origin;
        readonly List<Vector3> m_TipsLocal = new(11);
        Vector3 m_DebugTip;
        int m_DebugTipFrame = -10;

        // ---------- Attach ----------

        /// <summary>
        /// Looks for Resources/MoodiumPointClouds/&lt;prefabName&gt; and, if it exists, switches <paramref name="instance"/>
        /// to its point cloud. Returns null (and leaves the instance untouched) when there is no baked asset.
        /// </summary>
        public static PointCloudModel TryAttach(GameObject instance, string prefabName, bool playAppear = true)
        {
            if (instance == null) return null;
            var asset = PointCloudModelAsset.Load(prefabName);
            if (asset == null || asset.Count == 0) return null; // not baked: keep the mesh (no log, this is the normal case)
            var model = instance.GetComponent<PointCloudModel>() ?? instance.AddComponent<PointCloudModel>();
            model.Build(asset);
            if (playAppear) model.PlayAppear();
            var baked = 0;
            foreach (var mesh in asset.Chunks) if (mesh != null) baked++;
            Debug.Log($"[Point Clouds] {prefabName}: showing {asset.Count} points in {asset.ChunkCount} chunks " +
                      $"({baked} baked meshes), hid {model.m_HiddenRenderers.Count} renderers, " +
                      $"shader {(model.m_Material != null ? model.m_Material.shader.name : "none")}, atlas {(asset.Atlas != null ? asset.Atlas.width : 0)}².");
            return model;
        }

        void Build(PointCloudModelAsset asset)
        {
            Clear();
            m_Asset = asset;
            // LOD selection must not turn the original tree meshes back on as the viewer moves.
            foreach (var lod in GetComponentsInChildren<LODGroup>(true))
                if (lod.enabled) { lod.enabled = false; m_DisabledLodGroups.Add(lod); }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; m_HiddenRenderers.Add(r); }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            m_Material = new Material(shader) { name = $"{asset.name} Points" };
            if (m_Material.HasProperty("_BaseMap")) m_Material.SetTexture("_BaseMap", asset.Atlas);
            if (m_Material.HasProperty("_MainTex")) m_Material.SetTexture("_MainTex", asset.Atlas);
            if (m_Material.HasProperty("_BaseColor")) m_Material.SetColor("_BaseColor", Color.white);
            if (m_Material.HasProperty("_Cull")) m_Material.SetFloat("_Cull", (float)CullMode.Off);
            if (m_Material.HasProperty("_Surface")) m_Material.SetFloat("_Surface", 0f);
            // Soft round points: alpha clipping with threshold 0 = alpha-to-coverage on visionOS (PolySpatial docs,
            // URP Unlit). No sorting, no custom shader. The Unity Editor has no alpha-to-coverage, so points look
            // harder there than on device.
            if (m_Material.HasProperty("_AlphaClip")) m_Material.SetFloat("_AlphaClip", 1f);
            if (m_Material.HasProperty("_Cutoff")) m_Material.SetFloat("_Cutoff", 0f);
            m_Material.EnableKeyword("_ALPHATEST_ON");
            m_Material.SetOverrideTag("RenderType", "TransparentCutout");
            m_Material.renderQueue = (int)RenderQueue.AlphaTest;

            m_Points = new GameObject("Point Cloud").transform;
            m_Points.SetParent(transform, false);
            m_Chunks = new Chunk[asset.ChunkCount];
            for (var c = 0; c < m_Chunks.Length; c++)
            {
                var go = new GameObject($"Points {c:00}");
                go.transform.SetParent(m_Points, false);
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = asset.Chunks[c] != null ? asset.Chunks[c] : asset.BuildChunkMesh(c, $"Points {c:00} (runtime)");
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_Material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                m_Chunks[c] = new Chunk { Go = go, Filter = filter };
            }
            var n = asset.Count;
            m_Offset = new Vector3[n];
            m_Velocity = new Vector3[n];
            m_Work = new Vector3[n];
            m_Size = new float[n];
            m_SwayPhase = UnityEngine.Random.value * 10f;
        }

        /// <summary>Back to the original mesh renderers (debug / fallback).</summary>
        public void Clear()
        {
            foreach (var r in m_HiddenRenderers) if (r != null) r.enabled = true;
            m_HiddenRenderers.Clear();
            foreach (var lod in m_DisabledLodGroups) if (lod != null) lod.enabled = true;
            m_DisabledLodGroups.Clear();
            if (m_Chunks != null)
                foreach (var c in m_Chunks) if (c?.Dynamic != null) Destroy(c.Dynamic);
            m_Chunks = null;
            if (m_Points != null) Destroy(m_Points.gameObject);
            m_Points = null;
            if (m_Material != null) Destroy(m_Material);
            m_Material = null;
            m_AppearTime = -1f;
        }

        void OnDestroy() => Clear();

        public void PlayAppear()
        {
            if (m_Chunks == null) return;
            m_AppearTime = 0f;
            foreach (var c in m_Chunks) c.Go.SetActive(false);
        }

        // ---------- Update ----------

        void Update()
        {
            if (m_Chunks == null) return;
            if (m_Asset.Foliage && m_SwayDegrees > 0f)
            {
                var t = Time.time + m_SwayPhase;
                m_Points.localRotation = Quaternion.Euler(Mathf.Sin(t * .9f) * m_SwayDegrees, 0f, Mathf.Sin(t * .7f + 1.3f) * m_SwayDegrees * .8f);
            }
            if (Time.unscaledTime < m_NextStep) return;
            var dt = Mathf.Min(.05f, Mathf.Max(1f / m_UpdateRate, Time.unscaledDeltaTime));
            m_NextStep = Time.unscaledTime + 1f / m_UpdateRate;

            if (m_AppearTime >= 0f) StepAppear(dt);
            else if (m_TouchEnabled) StepTouch(dt);
        }

        void StepAppear(float dt)
        {
            m_AppearTime += dt;
            var t = m_AppearTime;
            var allDone = true;
            for (var c = 0; c < m_Chunks.Length; c++)
            {
                m_Asset.ChunkRange(c, out var start, out var end);
                if (end <= start) continue;
                // A chunk is a height slab: its points start between the delays of its lowest and highest point.
                var first = PointCloudFx.AppearDelay(m_Asset.Height01[start], 0f);
                var last = PointCloudFx.AppearDelay(m_Asset.Height01[end - 1], 1f) + PointCloudFx.AppearEachSeconds;
                var chunk = m_Chunks[c];
                if (t < first) { allDone = false; continue; }
                if (t > last)
                {
                    if (chunk.Dynamic != null || !chunk.Go.activeSelf) Settle(c);
                    continue;
                }
                allDone = false;
                if (!chunk.Go.activeSelf) chunk.Go.SetActive(true);
                EnsureDynamic(c);
                for (var i = start; i < end; i++)
                {
                    PointCloudFx.Appear(i, m_Asset.Positions[i], m_Asset.Normals[i], m_Asset.Height01[i], t, out m_Work[i], out m_Size[i]);
                }
                m_Asset.WriteChunkVertices(c, chunk.Vertices, m_Work, m_Size);
                chunk.Dynamic.SetVertices(chunk.Vertices);
            }
            if (allDone)
            {
                Debug.Log($"[Point Clouds] {m_Asset.name}: appear finished after {m_AppearTime:0.00} s.");
                m_AppearTime = -1f;
                AppearFinished?.Invoke();
            }
        }

        void StepTouch(float dt)
        {
            CollectTips();
            var radius = m_Asset.Foliage ? PointCloudFx.FoliageTouchRadius : PointCloudFx.TouchRadius;
            var reach = radius + .02f;
            Vector3 strongestPoint = default;
            var strongest = 0f;

            for (var c = 0; c < m_Chunks.Length; c++)
            {
                var chunk = m_Chunks[c];
                var bounds = m_Asset.ChunkBounds[c];
                bounds.Expand(reach * 2f);
                chunk.Touching = false;
                foreach (var tip in m_TipsLocal)
                    if (bounds.Contains(tip)) { chunk.Touching = true; break; }
                if (!chunk.Touching && !chunk.Moving) continue;

                EnsureDynamic(c);
                m_Asset.ChunkRange(c, out var start, out var end);
                var anyMoving = false;
                var atlas = m_Asset.Atlas.width;
                for (var i = start; i < end; i++)
                {
                    var rest = m_Asset.Positions[i];
                    var target = Vector3.zero;
                    var weight = 0f;
                    if (chunk.Touching)
                        foreach (var tip in m_TipsLocal)
                        {
                            var w = PointCloudFx.TouchWeight(rest, tip, radius);
                            if (w <= weight) continue;
                            weight = w;
                            target = PointCloudFx.TouchOffset(i, rest, m_Asset.Normals[i], tip, w);
                        }
                    if (weight > strongest) { strongest = weight; strongestPoint = rest; }
                    var stiffness = target.sqrMagnitude >= m_Offset[i].sqrMagnitude ? PointCloudFx.PressStiffness : PointCloudFx.ReleaseStiffness;
                    m_Offset[i] = PointCloudFx.SpringStep(m_Offset[i], ref m_Velocity[i], target, stiffness, dt);
                    var energy = m_Offset[i].magnitude / PointCloudFx.TouchPush;
                    if (energy > .002f || m_Velocity[i].sqrMagnitude > 1e-6f) anyMoving = true;
                    m_Work[i] = rest + m_Offset[i];
                    m_Size[i] = PointCloudFx.TouchSize(energy * 1.6f);
                    PointCloudFx.WriteBlockUVs(chunk.Uvs, (i - start) * 4, i, energy * 1.6f > PointCloudFx.GlowAt, atlas, true, m_Asset.Count);
                }
                chunk.Moving = anyMoving || chunk.Touching;
                if (!chunk.Moving)
                {
                    for (var i = start; i < end; i++) { m_Offset[i] = Vector3.zero; m_Velocity[i] = Vector3.zero; }
                    Settle(c);
                    continue;
                }
                m_Asset.WriteChunkVertices(c, chunk.Vertices, m_Work, m_Size);
                chunk.Dynamic.SetVertices(chunk.Vertices);
                chunk.Dynamic.SetUVs(0, chunk.Uvs);
            }

            if (strongest > .2f && Time.unscaledTime - m_LastTouched > m_TouchedCooldown)
            {
                if (m_LastTouched < 0f) Debug.Log($"[Point Clouds] {m_Asset.name}: first touch ({m_TipsLocal.Count} fingertips tracked).");
                m_LastTouched = Time.unscaledTime;
                Touched?.Invoke(m_Points.TransformPoint(strongestPoint));
            }
        }

        /// <summary>Swap a chunk to a private dynamic copy so its vertices can move.</summary>
        void EnsureDynamic(int c)
        {
            var chunk = m_Chunks[c];
            if (chunk.Dynamic != null) return;
            var source = chunk.Filter.sharedMesh;
            chunk.Dynamic = Instantiate(source);
            chunk.Dynamic.name = source.name + " (moving)";
            chunk.Dynamic.MarkDynamic();
            chunk.Vertices = source.vertices;
            chunk.Uvs = source.uv;
            chunk.Filter.sharedMesh = chunk.Dynamic;
        }

        /// <summary>Back to the static baked mesh (rest pose, own colours).</summary>
        void Settle(int c)
        {
            var chunk = m_Chunks[c];
            chunk.Go.SetActive(true);
            chunk.Moving = false;
            chunk.Filter.sharedMesh = m_Asset.Chunks[c] != null ? m_Asset.Chunks[c] : chunk.Filter.sharedMesh;
            if (chunk.Dynamic != null && m_Asset.Chunks[c] != null)
            {
                Destroy(chunk.Dynamic);
                chunk.Dynamic = null;
            }
        }

        // ---------- Hands ----------

        void CollectTips()
        {
            m_TipsLocal.Clear();
            if (Time.frameCount - m_DebugTipFrame <= 2) m_TipsLocal.Add(m_DebugTip);
            if (m_Hands == null || !m_Hands.running)
            {
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
                if (m_Hands == null) return;
            }
            if (m_Origin == null) m_Origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_Origin != null ? m_Origin.Origin.transform : null;
            AddTips(m_Hands.leftHand, origin);
            AddTips(m_Hands.rightHand, origin);
        }

        void AddTips(XRHand hand, Transform origin)
        {
            if (!hand.isTracked) return;
            foreach (var id in Tips)
                if (hand.GetJoint(id).TryGetPose(out var pose))
                    m_TipsLocal.Add(m_Points.InverseTransformPoint(PalmPressGestureController.TransformTrackingPose(pose, origin).position));
        }

        /// <summary>Editor / debug: feed a fingertip without XR Hands (world space). Call every frame while touching.</summary>
        public void DebugTouch(Vector3 worldTip)
        {
            if (m_Chunks == null) return;
            m_DebugTip = m_Points.InverseTransformPoint(worldTip);
            m_DebugTipFrame = Time.frameCount;
        }
    }
}
