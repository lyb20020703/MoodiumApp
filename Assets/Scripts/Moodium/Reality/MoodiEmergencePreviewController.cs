using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>
    /// Editor/Game-view proof of the exact runtime emergence composition.
    /// It deliberately uses MoodiEmergenceController, so the authored animation
    /// graph cannot drift from the Vision build.  The preview intentionally does
    /// not start spatial-pointer, XR-hand, or collider components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiEmergencePreviewController : MonoBehaviour
    {
        // The production flow begins at zero and intentionally hides Moodi.
        // A proof scene must instead open on a readable authored pose; the slider
        // still allows direct comparison with the hidden 0% state.
        [SerializeField, Range(0f, 1f)] float m_Progress = .5f;
        [SerializeField] GameObject m_CandySoftPrefab;
        [SerializeField] GameObject m_NatureSoftPrefab;
        [SerializeField] GameObject m_MoodiPrefab;
        [SerializeField] AnimationClip m_EmergenceClip;
        [SerializeField, Min(.001f)] float m_MoodiScale = .01f;
        [Header("Preview Playback Only")]
        [SerializeField] bool m_AutoPlayPreview = true;
        [SerializeField, Min(1f)] float m_AutoPreviewDuration = 8f;

        bool m_Built;
        float m_LastProgress = -1f;
        float m_AutoPreviewStartTime;

        public void Configure(GameObject candySoft, GameObject natureSoft, GameObject moodiPrefab,
            AnimationClip emergenceClip, float moodiScale)
        {
            m_CandySoftPrefab = candySoft;
            m_NatureSoftPrefab = natureSoft;
            m_MoodiPrefab = moodiPrefab;
            m_EmergenceClip = emergenceClip;
            m_MoodiScale = moodiScale;
        }

        void Start()
        {
            m_AutoPreviewStartTime = Time.time;
            BuildIfNeeded();
            if (m_AutoPlayPreview)
                m_Progress = AutoPreviewProgress(0f, m_AutoPreviewDuration);
            ApplyProgress();
        }

        void Update()
        {
            BuildIfNeeded();
            if (Application.isPlaying && m_AutoPlayPreview)
                m_Progress = AutoPreviewProgress(Time.time - m_AutoPreviewStartTime, m_AutoPreviewDuration);
            ApplyProgress();
        }

        /// <summary>Preview-only loop: hidden at 0%, fully emerged near the end, then restarts.</summary>
        public static float AutoPreviewProgress(float elapsedSeconds, float durationSeconds)
        {
            if (durationSeconds <= 0f)
                return 0f;
            return Mathf.Repeat(Mathf.Max(0f, elapsedSeconds), durationSeconds) / durationSeconds;
        }

        void BuildIfNeeded()
        {
            if (m_Built || m_MoodiPrefab == null || m_EmergenceClip == null)
                return;

            CreateWorldPreview("Candy World — Moodi emergence", new Vector3(-.18f, 0f, 0f), m_CandySoftPrefab);
            CreateWorldPreview("Nature World — Moodi emergence", new Vector3(.18f, 0f, 0f), m_NatureSoftPrefab);
            m_Built = true;
            Debug.Log($"[Moodi Emergence Preview] Built with clip '{m_EmergenceClip.name}' ({m_EmergenceClip.length:F2}s).");
        }

        void CreateWorldPreview(string label, Vector3 position, GameObject softPrefab)
        {
            if (softPrefab == null)
                return;

            var root = new GameObject(label + " Runtime Root").transform;
            root.position = position;
            var visual = Instantiate(softPrefab, root);
            visual.name = softPrefab.name;
            // Preview must use the same authored pose as the Vision runtime. Do not
            // compensate it with a table-plane rotation: CandySoft/NatureSoft already
            // carry their approved orientation in the prefab asset.
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * .12f;

            // The Soft remains under the stable bag root. Moodi alone belongs to
            // this motion root, exactly like the Vision runtime's separate root.
            var emergenceRoot = CreateEmergenceMotionRoot(root);
            var emergence = emergenceRoot.gameObject.AddComponent<MoodiEmergenceController>();
            emergence.ConfigurePreview(emergenceRoot, root, m_MoodiPrefab, m_EmergenceClip, m_MoodiScale);
            CreateLabel(label, position + Vector3.back * .18f + Vector3.up * .03f);
        }

        public static Transform CreateEmergenceMotionRoot(Transform bagRoot)
        {
            var motionRoot = new GameObject("Moodi Emergence Motion Root").transform;
            motionRoot.SetParent(bagRoot, false);
            return motionRoot;
        }

        static void CreateLabel(string message, Vector3 position)
        {
            var label = new GameObject(message + " Label").AddComponent<TextMesh>();
            label.text = message;
            label.fontSize = 36;
            label.characterSize = .0025f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.transform.position = position;
            label.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        void ApplyProgress()
        {
            if (!m_Built || Mathf.Approximately(m_LastProgress, m_Progress))
                return;
            m_LastProgress = m_Progress;
            MoodiEmergenceController.SetAllProgress(m_Progress);
        }

        void OnGUI()
        {
            if (!Application.isPlaying)
                return;
            GUI.Box(new Rect(18f, 18f, 360f, 110f), "Moodi Emergence — Runtime Preview");
            GUI.Label(new Rect(32f, 43f, 300f, 20f), $"World progress: {m_Progress:P0}");
            m_AutoPlayPreview = GUI.Toggle(new Rect(32f, 67f, 300f, 20f), m_AutoPlayPreview,
                $"Auto-preview emergence ({m_AutoPreviewDuration:0.#} sec)");
            var next = GUI.HorizontalSlider(new Rect(32f, 99f, 300f, 20f), m_Progress, 0f, 1f);
            if (!Mathf.Approximately(next, m_Progress))
            {
                m_AutoPlayPreview = false;
                m_Progress = next;
            }
        }
    }
}
