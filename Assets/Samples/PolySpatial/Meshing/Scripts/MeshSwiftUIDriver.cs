using System;
using System.Collections.Generic;
using AOT;
using PolySpatial.Samples;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using Object = UnityEngine.Object;

#if UNITY_VISIONOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Samples.PolySpatial.SwiftUI.Scripts
{
    // This is a driver MonoBehaviour that connects to SwiftUISamplePlugin.swift via
    // C# DllImport. See MeshSamplePlugin.swift for more information.
    public class MeshSwiftUIDriver : MonoBehaviour
    {
        [FormerlySerializedAs("m_CustomPrefabs")]
        [FormerlySerializedAs("m_ObjectsToSpawn")]
        public GameObject[] customPrefabs;

        [SerializeField]
        float m_SpawnScale = 0.25f;

        [SerializeField]
        Transform m_SpawnPosition;

        [SerializeField]
        Transform m_SpawnReference;

        [SerializeField]
        float m_SpawnDistance = 1.2f;

        [SerializeField]
        float m_SpawnHeight = 0.75f;

        [SerializeField]
        float m_SpawnHorizontalJitter = 0.15f;

        [SerializeField]
        bool m_EnableFallbackCube = true;

        [SerializeField]
        bool m_UseDebugMaterialWhenModelHasNoMaterial = true;

        [SerializeField]
        Color m_DebugMaterialColor = new(1f, 0.72f, 0.08f, 1f);

        [SerializeField]
        AudioClip m_DefaultSpawnTouchSound;

        [SerializeField]
        ARMeshManager m_MeshManager;

        [SerializeField]
        Material m_OcclusionMaterial;

        [SerializeField]
        Material m_WireFrameMaterial;

        [SerializeField]
        Material m_TextureMaterial;

        [SerializeField]
        LoadLevelButton m_LoadLevelButton;

        [SerializeField]
        int m_MaxSpawnedObjects = 150;

        [SerializeField]
        SwiftFPSCounter m_FPSCounter;

        bool m_SpawningObjects;
        bool m_EmbeddedMoodiumMode;
        bool m_ShowMesh = true;
        List<GameObject> m_SpawnedObjects = new();
        float m_RealTimeAtSpawn;

        const float k_SpawnDelay = 0.25f;

        public bool IsSpawningObjects => m_SpawningObjects;
        public int SpawnedObjectCount
        {
            get
            {
                m_SpawnedObjects.RemoveAll(item => item == null);
                return m_SpawnedObjects.Count;
            }
        }

        void OnEnable()
        {
            if (!m_EmbeddedMoodiumMode)
            {
                OpenSwiftUIWindow("MeshSample");
                SetNativeCallback(CallbackFromNative);
            }
            if (m_FPSCounter != null)
                m_FPSCounter.enabled = true;
        }

        void OnDisable()
        {
            SetNativeCallback(null);
            ForceCloseWindow();
            m_SpawningObjects = false;
        }

        public void ForceCloseWindow()
        {
            CloseSwiftUIWindow("MeshSample");
            if (m_FPSCounter != null)
                m_FPSCounter.enabled = false;
        }

        public void SetEmbeddedMoodiumMode(bool embedded)
        {
            m_EmbeddedMoodiumMode = embedded;
            if (embedded && isActiveAndEnabled)
            {
                SetNativeCallback(null);
                ForceCloseWindow();
            }
        }

        public void SetSpawningObjects(bool enabled)
        {
            m_SpawningObjects = enabled;
            if (enabled)
                m_RealTimeAtSpawn = Time.realtimeSinceStartup - k_SpawnDelay;
            var prefabCount = customPrefabs == null ? 0 : customPrefabs.Length;
            Debug.Log($"MeshSwiftUIDriver: Moodium Spawn Objects = {enabled}. " +
                      $"customPrefabs length = {prefabCount}. valid custom prefab count = {GetValidPrefabCount()}");
        }

        public void ClearSpawnedObjects()
        {
            m_SpawningObjects = false;
            var removed = 0;
            foreach (var obj in m_SpawnedObjects)
            {
                if (obj == null)
                    continue;
                Destroy(obj);
                removed++;
            }
            m_SpawnedObjects.Clear();
            Debug.Log($"MeshSwiftUIDriver: Cleared {removed} registered Spawn Objects instances.");
        }

        void Update()
        {
            if (m_SpawningObjects)
            {
                if (Time.realtimeSinceStartup >= m_RealTimeAtSpawn + k_SpawnDelay)
                {
                    if(m_SpawnedObjects.Count >= m_MaxSpawnedObjects)
                    {
                        Debug.Log($"MeshSwiftUIDriver: Max spawned object count reached ({m_MaxSpawnedObjects}).");
                        return;
                    }

                    SpawnOneObject();
                    m_RealTimeAtSpawn = Time.realtimeSinceStartup;
                }
            }

        }

        void SpawnOneObject()
        {
            var prefabCount = customPrefabs == null ? 0 : customPrefabs.Length;
            var validPrefabCount = GetValidPrefabCount();
            var spawnPosition = GetSpawnPosition();
            Debug.Log($"MeshSwiftUIDriver: customPrefabs length = {prefabCount}");
            Debug.Log($"MeshSwiftUIDriver: valid custom prefab count = {validPrefabCount}");
            Debug.Log($"MeshSwiftUIDriver: Spawn position = {spawnPosition}");

            GameObject newObject;
            var isFallback = false;
            if (prefabCount == 0)
            {
                newObject = SpawnFallbackCube(spawnPosition, "customPrefabs array is empty.", out isFallback);
            }
            else
            {
                var prefab = SelectRandomValidPrefab();
                if (prefab == null)
                {
                    newObject = SpawnFallbackCube(spawnPosition, "customPrefabs has no valid entries.", out isFallback);
                }
                else
                {
                    Debug.Log($"MeshSwiftUIDriver: selected prefab = {prefab.name}");
                    newObject = InstantiateCustomPrefab(prefab, spawnPosition, out isFallback);
                }
            }

            if (newObject == null)
            {
                Debug.LogWarning("MeshSwiftUIDriver: Spawn returned null, object will not be added to spawned list.");
                return;
            }

            PrepareSpawnedObject(newObject, isFallback);
            newObject.transform.rotation = UnityEngine.Random.rotation;
            m_SpawnedObjects.Add(newObject);
            RegisterCreativeRuntimeObject(newObject);
        }

        int GetValidPrefabCount()
        {
            if (customPrefabs == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var prefab in customPrefabs)
            {
                if (prefab != null)
                {
                    count++;
                }
            }

            return count;
        }

        GameObject SelectRandomValidPrefab()
        {
            var validPrefabCount = GetValidPrefabCount();
            if (validPrefabCount == 0)
            {
                return null;
            }

            var selectedIndex = UnityEngine.Random.Range(0, validPrefabCount);
            foreach (var prefab in customPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                if (selectedIndex == 0)
                {
                    return prefab;
                }

                selectedIndex--;
            }

            return null;
        }

        GameObject InstantiateCustomPrefab(GameObject prefab, Vector3 spawnPosition, out bool isFallback)
        {
            isFallback = false;

            try
            {
                var instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
                if (instance == null)
                {
                    return SpawnFallbackCube(spawnPosition, $"Instantiate returned null for selected prefab {prefab.name}.", out isFallback);
                }

                Debug.Log($"MeshSwiftUIDriver: instantiated custom model successfully = {instance.name}");
                return instance;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return SpawnFallbackCube(spawnPosition, $"Instantiate failed for selected prefab {prefab.name}: {exception.Message}", out isFallback);
            }
        }

        Vector3 GetSpawnPosition()
        {
            var reference = GetSpawnReference();
            if (reference == null)
            {
                var fallbackPosition = m_SpawnPosition != null ? m_SpawnPosition.position : Vector3.up;
                Debug.LogWarning($"MeshSwiftUIDriver: No camera spawn reference found. Using fallback SpawnPosition = {fallbackPosition}");
                return fallbackPosition;
            }

            var forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = reference.forward.normalized;
            }

            var right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
            var jitter = right * UnityEngine.Random.Range(-m_SpawnHorizontalJitter, m_SpawnHorizontalJitter);
            return reference.position + forward * m_SpawnDistance + Vector3.up * m_SpawnHeight + jitter;
        }

        Transform GetSpawnReference()
        {
            if (m_SpawnReference != null)
            {
                return m_SpawnReference;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera.transform;
            }

            var taggedCamera = GameObject.FindWithTag("MainCamera");
            if (taggedCamera != null)
            {
                return taggedCamera.transform;
            }

            var camera = FindFirstObjectByType<Camera>();
            return camera != null ? camera.transform : null;
        }

        GameObject SpawnFallbackCube(Vector3 spawnPosition, string reason, out bool isFallback)
        {
            isFallback = true;
            Debug.LogWarning($"MeshSwiftUIDriver: fallback cube spawned because: {reason}");
            if (!m_EnableFallbackCube)
            {
                Debug.LogWarning("MeshSwiftUIDriver: Fallback cube is disabled; no object will be spawned for this failed spawn.");
                return null;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Spawn Fallback Cube";
            cube.transform.position = spawnPosition;
            cube.transform.localScale = Vector3.one * Mathf.Max(0.2f, m_SpawnScale);
            ApplyVisibleDebugMaterial(cube);
            return cube;
        }

        void PrepareSpawnedObject(GameObject newObject, bool isFallback)
        {
            if (!isFallback)
            {
                newObject.transform.localScale *= m_SpawnScale;
            }

            newObject.SetActive(true);

            if (!isFallback && m_UseDebugMaterialWhenModelHasNoMaterial && NeedsDebugMaterial(newObject))
            {
                ApplyVisibleDebugMaterial(newObject);
            }

            var rigidbody = newObject.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = newObject.AddComponent<Rigidbody>();
                Debug.Log($"MeshSwiftUIDriver: Rigidbody added to {newObject.name}");
            }

            rigidbody.useGravity = true;
            rigidbody.isKinematic = false;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var colliders = newObject.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0)
            {
                var collider = AddBoundsCollider(newObject);
                Debug.Log($"MeshSwiftUIDriver: Collider added to {newObject.name}: {collider.GetType().Name}");
            }
            else
            {
                foreach (var collider in colliders)
                {
                    collider.enabled = true;
                    if (collider is MeshCollider meshCollider && !meshCollider.convex)
                    {
                        meshCollider.convex = true;
                        Debug.Log($"MeshSwiftUIDriver: MeshCollider set to convex on {meshCollider.name}");
                    }
                }

                Debug.Log($"MeshSwiftUIDriver: Existing collider found on {newObject.name}. Collider count = {colliders.Length}");
            }

            AddPhysicsDestroyInteraction(newObject);
            EnsureTouchAudio(newObject);
        }

        void EnsureTouchAudio(GameObject newObject)
        {
            const string componentTypeName = "Moodium.Interaction.MoodiumTouchAudio, Assembly-CSharp";
            var componentType = Type.GetType(componentTypeName);
            if (componentType == null)
                return;
            var audio = newObject.GetComponent(componentType);
            if (audio == null)
                audio = newObject.AddComponent(componentType);
            var touchSound = componentType.GetProperty("TouchSound")?.GetValue(audio) as AudioClip;
            if (touchSound == null && m_DefaultSpawnTouchSound != null)
                componentType.GetMethod("Configure")?.Invoke(audio, new object[] { m_DefaultSpawnTouchSound });
        }

        static void RegisterCreativeRuntimeObject(GameObject instance)
        {
            var registryType = Type.GetType("Moodium.Flow.MoodiumRuntimeObjectRegistry, Assembly-CSharp");
            registryType?.GetMethod("RegisterCreative")?.Invoke(null, new object[] { instance });
        }

        static void AddPhysicsDestroyInteraction(GameObject newObject)
        {
            const string componentTypeName = "Moodium.Interaction.DestroyOnTouch, Assembly-CSharp";
            var componentType = Type.GetType(componentTypeName);
            if (componentType == null)
            {
                Debug.LogError($"MeshSwiftUIDriver: interaction component was not found: {componentTypeName}");
                return;
            }

            if (newObject.GetComponent(componentType) == null)
                newObject.AddComponent(componentType);
        }

        BoxCollider AddBoundsCollider(GameObject newObject)
        {
            var renderers = newObject.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(newObject.transform.position, Vector3.one * 0.1f);
            var hasBounds = false;

            foreach (var renderer in renderers)
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            var boxCollider = newObject.AddComponent<BoxCollider>();
            var localCenter = newObject.transform.InverseTransformPoint(bounds.center);
            var localSize = new Vector3(
                bounds.size.x / newObject.transform.lossyScale.x,
                bounds.size.y / newObject.transform.lossyScale.y,
                bounds.size.z / newObject.transform.lossyScale.z);

            boxCollider.center = localCenter;
            boxCollider.size = localSize;
            return boxCollider;
        }

        void ApplyVisibleDebugMaterial(GameObject newObject)
        {
            var renderers = newObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"MeshSwiftUIDriver: No renderer found on {newObject.name}; fallback cube may be needed if this model stays invisible.");
                return;
            }

            var material = CreateVisibleMaterial();
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = material;
            }
        }

        bool NeedsDebugMaterial(GameObject newObject)
        {
            var renderers = newObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"MeshSwiftUIDriver: No renderer found on custom model {newObject.name}; debug material cannot be applied.");
                return true;
            }

            foreach (var renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    renderer.enabled = true;
                }

                var materials = renderer.sharedMaterials;
                foreach (var material in materials)
                {
                    if (material != null && material.shader != null)
                    {
                        return false;
                    }
                }
            }

            Debug.LogWarning($"MeshSwiftUIDriver: Custom model {newObject.name} has renderers but no valid material/shader. Applying debug material.");
            return true;
        }

        Material CreateVisibleMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader);
            material.color = m_DebugMaterialColor;
            return material;
        }

        delegate void CallbackDelegate(string command);

        // This attribute is required for methods that are going to be called from native code
        // via a function pointer.
        [MonoPInvokeCallback(typeof(CallbackDelegate))]
        static void CallbackFromNative(string command)
        {
            // MonoPInvokeCallback methods will leak exceptions and cause crashes; always use a try/catch in these methods
            try
            {
                Debug.Log("Callback from native: " + command);

                // This could be stored in a static field or a singleton.
                // If you need to deal with multiple windows and need to distinguish between them,
                // you could add an ID to this callback and use that to distinguish windows.
                var self = Object.FindFirstObjectByType<MeshSwiftUIDriver>();

                switch (command)
                {
                    case "closed":
                        break;
                    case "showmesh":
                        self.ToggleMesh();
                        break;
                    case "occlusionMat":
                        self.SetMeshMaterial(self.m_OcclusionMaterial);
                        break;
                    case "wireframeMat":
                        self.SetMeshMaterial(self.m_WireFrameMaterial);
                        break;
                    case "textureMat":
                        self.SetMeshMaterial(self.m_TextureMaterial);
                        break;
                    case "spawnObjects":
                        self.SpawnObjects();
                        break;
                    case "deleteObjects":
                        self.DeleteObjects();
                        break;
                    case "returnToMenu":
                        self.LoadMenuScene();
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        void SpawnObjects()
        {
            SetSpawningObjects(!m_SpawningObjects);
        }

        void DeleteObjects()
        {
            ClearSpawnedObjects();
        }

        void ToggleMesh()
        {
            m_ShowMesh = !m_ShowMesh;
            if (m_ShowMesh)
            {
                m_MeshManager.enabled = true;
            }
            else
            {
                m_MeshManager.enabled = false;
                foreach(var mesh in m_MeshManager.meshes)
                {
                    mesh.gameObject.SetActive(false);
                }
            }
        }

        void SetMeshMaterial(Material mat)
        {
            foreach (var mesh in m_MeshManager.meshes)
            {
                mesh.GetComponent<MeshRenderer>().material = mat;
            }
        }

        void LoadMenuScene()
        {
            m_LoadLevelButton.Press();
        }

#if UNITY_VISIONOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void SetNativeCallback(CallbackDelegate callback);

        [DllImport("__Internal")]
        static extern void OpenSwiftUIWindow(string name);

        [DllImport("__Internal")]
        static extern void CloseSwiftUIWindow(string name);
#else
        static void SetNativeCallback(CallbackDelegate callback) {}
        static void OpenSwiftUIWindow(string name) {}
        static void CloseSwiftUIWindow(string name) {}
#endif
    }
}
