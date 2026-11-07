using System;
using System.Collections;
using Moodium.Calibration;
using Moodium.Interaction;
using Moodium.Opening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Moodium.Reality
{
    /// <summary>
    /// Presents a non-looping squeeze video whose timeline follows the existing
    /// CandyEnergyController normalized progress. The controller only owns video
    /// presentation; it never changes squeeze energy or completion gameplay.
    ///
    /// Jelly dome feedback: the transparent dome over the video squashes while the bound
    /// CandySoft/NatureSoft is pressed and springs back with a small overshoot when released
    /// (a damped spring on a squash value, 0 = rest, 1 = pressed). At 100% the dome swells and
    /// "pops" before hiding, and Popped reports where Moodi should jump out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SqueezeProgressVideoController : MonoBehaviour, ISqueezeProgressDisplay
    {
        const string SequenceConfigPath = "MoodiumOpening/OpeningVideoSequenceConfig";
        const float TargetTimeTolerance = 0.015f;
        const float PrepareTimeout = 15f;
        const int DiscSegments = 64;
        const int DomeRings = 10;
        const int DomeSegments = 48;
        public const int ForegroundRenderQueue = (int)RenderQueue.Overlay + 100;

        [Header("Tracked Soft presentation")]
        [SerializeField, Min(.01f)] float m_VideoDiameter = .075f;
        [SerializeField, Range(-.05f, .05f)] float m_SurfaceOffset = -.005f;
        [SerializeField, Min(.01f)] float m_DomeRadius = .042f;
        [SerializeField, Range(0f, .5f)] float m_DomeOpacity = .10f;

        [Header("Jelly dome squash")]
        [Tooltip("How much the dome loses height when fully pressed (0.30 = 70% height).")]
        [SerializeField, Range(0f, .6f)] float m_SquashHeight = .30f;
        [Tooltip("How much the dome widens when fully pressed.")]
        [SerializeField, Range(0f, .4f)] float m_SquashWidth = .14f;
        [Tooltip("How much the flat video disc spreads when pressed (it never stretches vertically).")]
        [SerializeField, Range(0f, .3f)] float m_DiscSpread = .08f;
        [SerializeField, Range(0f, .01f)] float m_DiscSink = .003f;
        [SerializeField, Min(1f)] float m_PressStiffness = 2500f;
        [SerializeField, Range(.05f, 2f)] float m_PressDamping = .85f;
        [SerializeField, Min(1f)] float m_ReleaseStiffness = 220f;
        [SerializeField, Range(.05f, 2f)] float m_ReleaseDamping = .32f;
        [Tooltip("Extra downward kick on every accepted squeeze, so repeated presses keep the jelly wobbling.")]
        [SerializeField, Range(0f, 6f)] float m_PressImpulse = 2.5f;
        [Tooltip("Gentle idle breathing of the dome while untouched (fraction of its height).")]
        [SerializeField, Range(0f, .05f)] float m_BreathAmount = .015f;
        [SerializeField, Range(.05f, .5f)] float m_PopSeconds = .12f;
        [SerializeField, Range(1f, 2f)] float m_PopScale = 1.25f;
        [SerializeField] Color m_CandyDomeColor = new(1f, .38f, .62f, 1f);
        [SerializeField] Color m_NatureDomeColor = new(.55f, .86f, .53f, 1f);

        Camera m_Camera;
        VideoClip m_CandyClip;
        VideoClip m_NatureClip;
        OpeningVideoSequenceConfig m_SequenceConfig;
        GameObject m_Display;
        VideoPlayer m_Player;
        RenderTexture m_RenderTexture;
        Material m_Material;
        Material m_DomeMaterial;
        Mesh m_DiscMesh;
        Mesh m_DomeMesh;
        Renderer m_DiscRenderer;
        Renderer m_DomeRenderer;
        Coroutine m_PrepareRoutine;
        Transform m_SurfaceAnchor;
        Vector3 m_AnchorLocalPosition;
        Quaternion m_AnchorLocalRotation = Quaternion.identity;
        Vector3 m_AnchorLocalNormal = Vector3.up;
        Vector3 m_AnchorLocalForward = Vector3.forward;
        double m_TargetTime;
        float m_NormalizedProgress;
        bool m_Advancing;
        bool m_Prepared;
        bool m_FirstFrameReady;
        bool m_Completed;
        bool m_BoundToSoft;
        bool m_NatureWorld;
        Transform m_Dome;
        ChocolateCapsuleInteraction m_BoundCapsule;
        float m_Squash;
        float m_SquashVelocity;
        bool m_PressedByEvent;
        Coroutine m_PopRoutine;

        /// <summary>Raised once when the finished video pops; the position is the centre of the dome.</summary>
        public event Action<Vector3> Popped;
        /// <summary>Current squash amount (0 rest, 1 pressed, negative while springing back taller).</summary>
        public float Squash => m_Squash;

        public bool IsVisible => m_DiscRenderer != null && m_DiscRenderer.enabled;
        public VideoClip ActiveClip => m_Player != null ? m_Player.clip : null;

        public bool TryGetBurstExclusion(out Vector3 center, out float radius)
        {
            center = default;
            radius = m_VideoDiameter * .5f + .015f;
            if (!IsVisible || m_Display == null || !m_Display.activeInHierarchy) return false;
            // Resolve the tracked pose now so projectile updates cannot use last frame's pose.
            UpdateDisplayPose();
            center = m_Display.transform.position;
            return true;
        }

        public bool OverlapsBurstVideo(Bounds bounds)
        {
            if (!TryGetBurstExclusion(out var center, out var radius)) return false;
            // A thin region around the actual video plane, rather than a sphere
            // swallowing the emitter and its entire upward flight path.
            var normal = m_Display.transform.up;
            var relative = bounds.center - center;
            var normalExtent = Vector3.Dot(bounds.extents,
                new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)));
            if (Mathf.Abs(Vector3.Dot(relative, normal)) > .012f + normalExtent) return false;
            var projected = Vector3.ProjectOnPlane(relative, normal);
            return projected.magnitude <= radius + bounds.extents.magnitude;
        }

        /// <summary>Centre and up direction of the dome, once the video is bound to the soft model.</summary>
        public bool TryGetDomeCenter(out Vector3 center, out Vector3 up)
        {
            center = default;
            up = Vector3.up;
            if (!m_BoundToSoft || m_Display == null) return false;
            UpdateDisplayPose();
            center = m_Display.transform.position;
            up = m_Display.transform.up;
            return true;
        }

        /// <summary>Squash the dome by <paramref name="amount"/> (1 = fully pressed, more = harder) and let it spring back.</summary>
        public void Splash(float amount)
        {
            m_Squash = amount;
            m_SquashVelocity = 0f;
            m_PressedByEvent = false;
        }

        /// <summary>One step of a damped spring (semi-implicit Euler, sub-stepped at 240 Hz for stability).</summary>
        public static float StepSpring(float value, ref float velocity, float target, float stiffness, float dampingRatio, float deltaTime)
        {
            if (deltaTime <= 0f) return value;
            var steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime * 240f));
            var h = deltaTime / steps;
            var damping = 2f * dampingRatio * Mathf.Sqrt(Mathf.Max(0f, stiffness));
            for (var i = 0; i < steps; i++)
            {
                velocity += (stiffness * (target - value) - damping * velocity) * h;
                value += velocity * h;
            }
            return value;
        }

        /// <summary>Dome scale for a squash amount: shorter and wider when pressed, taller and narrower when overshooting back.</summary>
        public static Vector3 DomeScale(float squash, float heightLoss, float widthGain)
        {
            var width = Mathf.Max(.5f, 1f + widthGain * squash);
            return new Vector3(width, Mathf.Max(.2f, 1f - heightLoss * squash), width);
        }

        public void Configure(Camera camera, VideoClip candyClip, VideoClip natureClip)
        {
            m_Camera = camera;
            m_CandyClip = candyClip;
            m_NatureClip = natureClip;
            m_SequenceConfig = Resources.Load<OpeningVideoSequenceConfig>(SequenceConfigPath);
        }

        public void Begin(bool natureWorld)
        {
            var clip = natureWorld ? m_NatureClip : m_CandyClip;
            ResetAndHide();
            m_NatureWorld = natureWorld;
            if (clip == null)
            {
                Debug.LogWarning($"[Moodium Squeeze Video] Missing {(natureWorld ? "Nature" : "Candy")} squeeze video clip.");
                return;
            }

            CreateDisplay(clip);
            m_PrepareRoutine = StartCoroutine(PrepareFirstFrame());
        }

        /// <summary>
        /// Binds the already-preparing video to the accepted tracked CandySoft/NatureSoft.
        /// The display remains scene-rooted so non-uniform calibration scale cannot turn
        /// the circular video into an ellipse.
        /// </summary>
        public void BindToSoft(ChocolateCapsuleInteraction capsule)
        {
            if (capsule == null)
                return;

            var presentation = capsule.GetComponentInParent<CalibratedSensoryBagPresentation>();
            var follower = capsule.GetComponent<TrackedObjectPoseFollower>();
            m_SurfaceAnchor = presentation != null
                ? presentation.transform
                : follower != null && follower.Anchor != null ? follower.Anchor : capsule.transform;

            var trackingCenter = presentation != null
                ? presentation.CenterWorldPosition
                : follower != null && follower.Anchor != null ? follower.Anchor.position : capsule.transform.position;
            var surfaceNormal = presentation != null
                ? presentation.AnchorOrientation * Vector3.up
                : follower != null && follower.Anchor != null ? follower.Anchor.up : capsule.transform.up;
            if (Vector3.Dot(surfaceNormal, Vector3.up) < 0f)
                surfaceNormal = -surfaceNormal;

            var renderers = capsule.GetComponentsInChildren<Renderer>(true);
            var softTopY = trackingCenter.y;
            foreach (var renderer in renderers)
                if (renderer.enabled)
                    softTopY = Mathf.Max(softTopY, renderer.bounds.max.y);

            var worldPosition = BuildSurfacePosition(trackingCenter, softTopY, surfaceNormal, m_SurfaceOffset);
            m_AnchorLocalPosition = m_SurfaceAnchor.InverseTransformPoint(worldPosition);
            // Preserve the authored CandySoft/NatureSoft capsule orientation.
            // The previous camera-facing reconstruction added an unintended Y flip.
            m_AnchorLocalRotation = Quaternion.Inverse(m_SurfaceAnchor.rotation) * capsule.transform.rotation;
            m_AnchorLocalNormal = m_SurfaceAnchor.InverseTransformDirection(surfaceNormal).normalized;
            m_BoundCapsule = capsule;
            var surfaceOrientation=presentation!=null ? presentation.AnchorOrientation :
                follower!=null&&follower.Anchor!=null ? follower.Anchor.rotation : capsule.transform.rotation;
            m_AnchorLocalForward=m_SurfaceAnchor.InverseTransformDirection(surfaceOrientation*Vector3.forward);
            m_BoundToSoft = true;
            UpdateDisplayPose();
            UpdateDisplayVisibility();
        }

        public void SetProgress(float normalizedProgress)
        {
            m_NormalizedProgress = Mathf.Clamp01(normalizedProgress);
            if (m_Completed || !m_Prepared || m_Player == null || m_Player.clip == null)
                return;

            m_TargetTime = ProgressToVideoTime(m_NormalizedProgress, m_Player.length);
            var currentTime = m_Player.time;
            if (m_TargetTime >= m_Player.length - TargetTimeTolerance &&
                currentTime + TargetTimeTolerance >= m_Player.length)
            {
                CompleteAndHide();
                return;
            }
            if (m_TargetTime <= currentTime + TargetTimeTolerance)
            {
                m_Player.Pause();
                m_Advancing = false;
                return;
            }

            m_Advancing = true;
            m_Player.Play();
        }

        public void ResetAndHide()
        {
            m_BubbleSuspended = false;
            if (m_PrepareRoutine != null)
            {
                StopCoroutine(m_PrepareRoutine);
                m_PrepareRoutine = null;
            }
            if (m_PopRoutine != null)
            {
                StopCoroutine(m_PopRoutine);
                m_PopRoutine = null;
            }
            m_Dome = null;
            m_BoundCapsule = null;
            m_Squash = m_SquashVelocity = 0f;
            m_PressedByEvent = false;
            m_Advancing = false;
            m_Prepared = false;
            m_FirstFrameReady = false;
            m_Completed = false;
            m_BoundToSoft = false;
            m_SurfaceAnchor = null;
            m_AnchorLocalRotation = Quaternion.identity;
            m_NormalizedProgress = 0f;
            if (m_Player != null)
            {
                m_Player.Stop();
                m_Player.targetTexture = null;
            }
            if (m_Display != null)
                Destroy(m_Display);
            m_Display = null;
            m_Player = null;
            m_DiscRenderer = null;
            m_DomeRenderer = null;
            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
                m_RenderTexture = null;
            }
            if (m_Material != null)
            {
                Destroy(m_Material);
                m_Material = null;
            }
            if (m_DomeMaterial != null)
            {
                Destroy(m_DomeMaterial);
                m_DomeMaterial = null;
            }
            if (m_DiscMesh != null)
            {
                Destroy(m_DiscMesh);
                m_DiscMesh = null;
            }
            if (m_DomeMesh != null)
            {
                Destroy(m_DomeMesh);
                m_DomeMesh = null;
            }
        }

        public static double ProgressToVideoTime(float normalizedProgress, double duration)
        {
            return Math.Max(0d, duration) * Mathf.Clamp01(normalizedProgress);
        }

        public static Camera SelectPlacementCamera(Camera liveMainCamera, Camera cachedCamera) =>
            liveMainCamera != null ? liveMainCamera : cachedCamera;

        public static bool HasReachedTarget(double currentTime, double targetTime, double tolerance)
        {
            return currentTime + Math.Max(0d, tolerance) >= targetTime;
        }

        /// <summary>
        /// Maps a point from the unit video disc (-1..1) into a centered square crop of
        /// the source video. Wide clips crop their left/right edges instead of stretching.
        /// </summary>
        public static Vector2 MapDiscPointToVideoUv(Vector2 unitDiscPoint, uint videoWidth, uint videoHeight)
        {
            var width = Mathf.Max(1f, videoWidth);
            var height = Mathf.Max(1f, videoHeight);
            var aspect = width / height;
            var horizontalSpan = aspect >= 1f ? 1f / aspect : 1f;
            var verticalSpan = aspect >= 1f ? 1f : aspect;
            return new Vector2(
                .5f + unitDiscPoint.x * .5f * horizontalSpan,
                .5f + unitDiscPoint.y * .5f * verticalSpan);
        }

        public static Quaternion BuildSurfaceRotation(Vector3 surfaceNormal)
        {
            if (surfaceNormal.sqrMagnitude < .000001f)
                surfaceNormal = Vector3.up;
            return Quaternion.FromToRotation(Vector3.up, surfaceNormal.normalized);
        }

        public static Quaternion BuildSurfaceRotation(Vector3 surfaceNormal,Vector3 surfaceForward)
        {
            var normal=surfaceNormal.sqrMagnitude<.000001f?Vector3.up:surfaceNormal.normalized;
            var forward=Vector3.ProjectOnPlane(surfaceForward,normal);
            return forward.sqrMagnitude<.000001f ? BuildSurfaceRotation(normal) : Quaternion.LookRotation(forward.normalized,normal);
        }

        public static Vector3 BuildSurfacePosition(Vector3 trackingCenter, float softTopY,
            Vector3 surfaceNormal, float surfaceOffset)
        {
            var normal = surfaceNormal.sqrMagnitude < .000001f ? Vector3.up : surfaceNormal.normalized;
            var surfaceCenter = new Vector3(trackingCenter.x, softTopY, trackingCenter.z);
            return surfaceCenter + normal * surfaceOffset;
        }

        void CreateDisplay(VideoClip clip)
        {
            m_Display = new GameObject("Moodium Squeeze Progress Video Display");
            m_Display.name = "Moodium Squeeze Progress Video Display";
            m_DiscMesh = CreateCircularDiscMesh(m_VideoDiameter * .5f, DiscSegments, clip.width, clip.height);
            m_Display.AddComponent<MeshFilter>().sharedMesh = m_DiscMesh;

            m_RenderTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "Moodium Squeeze Progress Video Render Texture"
            };
            m_RenderTexture.Create();
            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            if (m_Material.HasProperty("_BaseMap"))
                m_Material.SetTexture("_BaseMap", m_RenderTexture);
            if (m_Material.HasProperty("_MainTex"))
                m_Material.SetTexture("_MainTex", m_RenderTexture);
            OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(m_Material);
            m_Material.renderQueue = ForegroundRenderQueue;
            if (m_Material.HasProperty("_ZWrite")) m_Material.SetFloat("_ZWrite", 0f);
            if (m_Material.HasProperty("_ZTest")) m_Material.SetInt("_ZTest", (int)CompareFunction.Always);
            m_DiscRenderer = m_Display.AddComponent<MeshRenderer>();
            m_DiscRenderer.sortingOrder = short.MaxValue;
            m_DiscRenderer.sharedMaterial = m_Material;
            m_Display.AddComponent<PolySpatialVideoRenderTextureUpdater>().Configure(m_RenderTexture);
            CreateDomeVisual();

            m_Player = m_Display.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.isLooping = false;
            m_Player.source = VideoSource.VideoClip;
            m_Player.clip = clip;
            // Squeeze feedback has its own audio; these progress videos are visual-only.
            m_Player.audioOutputMode = VideoAudioOutputMode.None;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_RenderTexture;
            m_Player.loopPointReached += OnLoopPointReached;
            UpdateDisplayVisibility();
        }

        static Mesh CreateCircularDiscMesh(float radius, int segments, uint videoWidth, uint videoHeight)
        {
            segments = Mathf.Max(12, segments);
            radius = Mathf.Max(.001f, radius);
            var vertices = new Vector3[segments + 1];
            var normals = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = MapDiscPointToVideoUv(Vector2.zero, videoWidth, videoHeight);
            for (var index = 0; index < segments; index++)
            {
                var angle = index * Mathf.PI * 2f / segments;
                var unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices[index + 1] = new Vector3(unit.x * radius, 0f, unit.y * radius);
                normals[index + 1] = Vector3.up;
                uvs[index + 1] = MapDiscPointToVideoUv(unit, videoWidth, videoHeight);
                var triangle = index * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = (index + 1) % segments + 1;
                triangles[triangle + 2] = index + 1;
            }

            var mesh = new Mesh { name = "Moodium Circular Squeeze Video Disc" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        void CreateDomeVisual()
        {
            var dome = new GameObject("Squeeze Video Transparent Dome");
            dome.transform.SetParent(m_Display.transform, false);
            m_Dome = dome.transform;
            m_DomeMesh = CreateHemisphereMesh(m_DomeRadius, DomeRings, DomeSegments);
            dome.AddComponent<MeshFilter>().sharedMesh = m_DomeMesh;
            m_DomeRenderer = dome.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m_DomeMaterial = new Material(shader) { name = "Moodium Squeeze Video Dome Material" };
            var tint = m_NatureWorld ? m_NatureDomeColor : m_CandyDomeColor;
            tint.a = m_DomeOpacity;
            if (m_DomeMaterial.HasProperty("_BaseColor")) m_DomeMaterial.SetColor("_BaseColor", tint);
            if (m_DomeMaterial.HasProperty("_Color")) m_DomeMaterial.SetColor("_Color", tint);
            OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(m_DomeMaterial);
            m_DomeMaterial.renderQueue = (int)RenderQueue.Transparent + 25;
            if (m_DomeMaterial.HasProperty("_ZWrite")) m_DomeMaterial.SetFloat("_ZWrite", 0f);
            if (m_DomeMaterial.HasProperty("_Cull")) m_DomeMaterial.SetFloat("_Cull", (float)CullMode.Off);
            m_DomeRenderer.sortingOrder = short.MaxValue - 1;
            m_DomeRenderer.sharedMaterial = m_DomeMaterial;
        }

        static Mesh CreateHemisphereMesh(float radius, int rings, int segments)
        {
            radius = Mathf.Max(.001f, radius);
            rings = Mathf.Max(2, rings);
            segments = Mathf.Max(12, segments);
            var vertices = new Vector3[(rings + 1) * (segments + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (var ring = 0; ring <= rings; ring++)
            {
                var latitude = ring / (float)rings;
                var theta = latitude * Mathf.PI * .5f;
                var ringRadius = Mathf.Sin(theta) * radius;
                var height = Mathf.Cos(theta) * radius;
                for (var segment = 0; segment <= segments; segment++)
                {
                    var longitude = segment / (float)segments;
                    var phi = longitude * Mathf.PI * 2f;
                    var index = ring * (segments + 1) + segment;
                    var vertex = new Vector3(Mathf.Cos(phi) * ringRadius, height,
                        Mathf.Sin(phi) * ringRadius);
                    vertices[index] = vertex;
                    normals[index] = vertex.normalized;
                    uvs[index] = new Vector2(longitude, latitude);
                }
            }

            var triangles = new int[rings * segments * 6];
            var triangleIndex = 0;
            for (var ring = 0; ring < rings; ring++)
            for (var segment = 0; segment < segments; segment++)
            {
                var current = ring * (segments + 1) + segment;
                var next = current + segments + 1;
                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = next + 1;
            }

            var mesh = new Mesh { name = "Moodium Squeeze Video Hemisphere" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        void UpdateDisplayPose()
        {
            if (!m_BoundToSoft || m_SurfaceAnchor == null || m_Display == null)
                return;
            var worldPosition = m_SurfaceAnchor.TransformPoint(m_AnchorLocalPosition);
            var worldRotation = m_SurfaceAnchor.rotation * m_AnchorLocalRotation;
            m_Display.transform.SetPositionAndRotation(worldPosition, worldRotation);
        }

        bool m_BubbleSuspended;
        public void SetBubbleSuspended(bool suspended)
        {
            m_BubbleSuspended = suspended;
            UpdateDisplayVisibility();
        }

        void UpdateDisplayVisibility()
        {
            // VideoPlayer.isPrepared only means decoding is ready; the target
            // RenderTexture may still contain an uninitialized frame for one or
            // more render submissions. Keep the mesh hidden until that first
            // frame has had a frame boundary to reach PolySpatial.
            var visible = !m_BubbleSuspended && m_BoundToSoft && m_Prepared && m_FirstFrameReady && !m_Completed;
            if (m_DiscRenderer != null) m_DiscRenderer.enabled = visible;
            if (m_DomeRenderer != null) m_DomeRenderer.enabled = visible;
        }

        IEnumerator PrepareFirstFrame()
        {
            m_Player.Prepare();
            var deadline = Time.realtimeSinceStartup + PrepareTimeout;
            while (m_Player != null && !m_Player.isPrepared && Time.realtimeSinceStartup < deadline)
                yield return null;

            m_PrepareRoutine = null;
            if (m_Player == null || !m_Player.isPrepared)
            {
                Debug.LogWarning("[Moodium Squeeze Video] Video preparation timed out; hiding the squeeze video.");
                ResetAndHide();
                yield break;
            }

            m_Player.time = 0d;
            m_TargetTime = 0d;
            m_Prepared = true;
            m_Completed = false;
            m_Player.Pause();
            // Do not expose the disc on the same frame that preparation completes.
            // This prevents a transient missing-texture/magenta frame during scene
            // transitions and video source changes.
            yield return null;
            if (m_Player == null || !m_Player.isPrepared)
                yield break;
            m_FirstFrameReady = true;
            UpdateDisplayVisibility();
            SetProgress(m_NormalizedProgress);
            Debug.Log($"[Moodium Squeeze Video] Ready: {m_Player.clip.name}, duration={m_Player.length:0.00}s.");
        }

        void Update()
        {
            if (!m_Advancing || m_Player == null || !m_Prepared)
                return;
            if (!HasReachedTarget(m_Player.time, m_TargetTime, TargetTimeTolerance))
                return;

            if (m_TargetTime >= m_Player.length - TargetTimeTolerance)
            {
                CompleteAndHide();
                return;
            }

            m_Player.time = Math.Min(m_TargetTime, m_Player.length);
            m_Player.Pause();
            m_Advancing = false;
        }

        void OnEnable()
        {
            ChocolateCapsuleInteraction.AnyCapsulePinched += OnAnyCapsulePinched;
            ChocolateCapsuleInteraction.AnyCapsuleHeld += OnAnyCapsuleHeld;
            ChocolateCapsuleInteraction.AnyCapsuleReleased += OnAnyCapsuleReleased;
            ChocolateCapsuleInteraction.AnyCapsuleTrackingLost += OnAnyCapsuleReleased;
        }

        void OnDisable()
        {
            ChocolateCapsuleInteraction.AnyCapsulePinched -= OnAnyCapsulePinched;
            ChocolateCapsuleInteraction.AnyCapsuleHeld -= OnAnyCapsuleHeld;
            ChocolateCapsuleInteraction.AnyCapsuleReleased -= OnAnyCapsuleReleased;
            ChocolateCapsuleInteraction.AnyCapsuleTrackingLost -= OnAnyCapsuleReleased;
        }

        bool AcceptsCapsule(ChocolateCapsuleInteraction capsule) =>
            capsule != null && (m_BoundCapsule == null || capsule == m_BoundCapsule);

        void OnAnyCapsulePinched(ChocolateCapsuleInteraction capsule, Vector3 position)
        {
            if (!AcceptsCapsule(capsule)) return;
            m_PressedByEvent = true;
            m_SquashVelocity += m_PressImpulse;
        }

        void OnAnyCapsuleHeld(ChocolateCapsuleInteraction capsule, Vector3 position)
        {
            if (AcceptsCapsule(capsule)) m_PressedByEvent = true;
        }

        void OnAnyCapsuleReleased(ChocolateCapsuleInteraction capsule)
        {
            if (AcceptsCapsule(capsule)) m_PressedByEvent = false;
        }

        void LateUpdate()
        {
            UpdateDisplayPose();
            UpdateSquash(Time.deltaTime);
        }

        void UpdateSquash(float deltaTime)
        {
            if (m_Display == null || m_PopRoutine != null) return;
            // The bound soft model knows whether it is held right now (hand contact, palm press or pointer);
            // events only cover the moment before the first press binds it.
            var pressed = m_BoundCapsule != null ? m_BoundCapsule.IsPinched : m_PressedByEvent;
            m_Squash = StepSpring(m_Squash, ref m_SquashVelocity, pressed ? 1f : 0f,
                pressed ? m_PressStiffness : m_ReleaseStiffness, pressed ? m_PressDamping : m_ReleaseDamping, deltaTime);
            var spread = 1f + m_DiscSpread * m_Squash;
            m_Display.transform.localScale = new Vector3(spread, 1f, spread);
            m_Display.transform.position -= m_Display.transform.up * (m_DiscSink * Mathf.Max(0f, m_Squash));
            if (m_Dome == null) return;
            var dome = DomeScale(m_Squash, m_SquashHeight, m_SquashWidth);
            var breath = !pressed && Mathf.Abs(m_Squash) < .05f ? Mathf.Sin(Time.time * Mathf.PI) * m_BreathAmount : 0f;
            m_Dome.localScale = new Vector3(dome.x / spread, dome.y + breath, dome.z / spread);
        }

        void OnLoopPointReached(VideoPlayer player)
        {
            if (player == null)
                return;
            CompleteAndHide();
        }

        void CompleteAndHide()
        {
            var firstCompletion = !m_Completed;
            var wasVisible = IsVisible;
            m_Completed = true;
            m_Advancing = false;
            if (m_Player != null)
            {
                m_Player.isLooping = false;
                m_Player.Pause();
            }
            if (firstCompletion && wasVisible && m_Display != null && Application.isPlaying)
            {
                // Keep the last frame on screen while the dome swells and pops, then hide.
                Popped?.Invoke(m_Display.transform.position);
                if (m_PopRoutine != null) StopCoroutine(m_PopRoutine);
                m_PopRoutine = StartCoroutine(PopThenHide());
                return;
            }
            if (m_Player != null) m_Player.Stop();
            if (m_Display != null)
                UpdateDisplayVisibility();
        }

        IEnumerator PopThenHide()
        {
            var startScale = m_Dome != null ? m_Dome.localScale : Vector3.one;
            var domeTint = m_NatureWorld ? m_NatureDomeColor : m_CandyDomeColor;
            for (var t = 0f; t < m_PopSeconds; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / m_PopSeconds);
                var eased = 1f - (1f - k) * (1f - k);
                if (m_Dome != null) m_Dome.localScale = startScale * Mathf.Lerp(1f, m_PopScale, eased);
                SetMaterialAlpha(m_DomeMaterial, domeTint, m_DomeOpacity * (1f - k));
                SetMaterialAlpha(m_Material, Color.white, 1f - k);
                yield return null;
            }
            m_PopRoutine = null;
            if (m_Player != null) m_Player.Stop();
            UpdateDisplayVisibility();
        }

        static void SetMaterialAlpha(Material material, Color color, float alpha)
        {
            if (material == null) return;
            color.a = Mathf.Clamp01(alpha);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        void OnDestroy()
        {
            if (m_Player != null)
                m_Player.loopPointReached -= OnLoopPointReached;
            ResetAndHide();
        }
    }
}
