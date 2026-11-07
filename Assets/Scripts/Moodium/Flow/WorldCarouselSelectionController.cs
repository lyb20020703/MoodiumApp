using System;
using System.Collections.Generic;
using Moodium.MoodCheck;
using Moodium.Opening;
using Moodium.UI;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Flow
{
    [DisallowMultipleComponent]
    public sealed class WorldCarouselSelectionController : MonoBehaviour
    {
        [Header("World-locked placement")]
        [SerializeField] float m_Distance = .75f;
        [SerializeField] float m_DropBelowEyes = .08f;
        [SerializeField] float m_RecenterAngle = 50;
        [Header("Carousel")]
        [SerializeField] float m_ItemSpacing = .58f;
        [SerializeField] float m_SideScale = .7f;
        [SerializeField] float m_SideAlpha = .55f;
        [SerializeField] float m_SideYaw = 20;
        [SerializeField] float m_SwipeThreshold = .06f;
        [SerializeField] float m_SwitchSeconds = .35f;
        [SerializeField] float m_ModelSize = .44f;

        readonly GameObject[] prefabs = new GameObject[2];
        readonly MoodiumWorldDefinition[] worlds = new MoodiumWorldDefinition[2];
        readonly Transform[] items = new Transform[2];
        readonly GlassSlideToEnter[] sliders = new GlassSlideToEnter[2];
        readonly TextMeshPro[] tags = new TextMeshPro[2];
        readonly List<GameObject>[] mascots = { new(), new() };
        readonly List<Material>[] materials = { new(), new() };
        readonly List<(SpriteRenderer renderer, Color color)>[] sprites = { new(), new() };
        Camera viewer;
        TMP_FontAsset chinese, englishFont;
        Material glass;
        Transform root;
        MoodEntryChip moodChip;
        MoodCheckSample summary;
        string recommended;
        bool english, suspended, committed, swiping, recentering;
        float displaySelection, transitionStart, transitionFrom, dragOffset;
        Vector3 swipeStart;
        Pose recenterTarget;

        public event Action<StickerWorldEntry> WorldEntrySelected;
        public event Action MoodCheckRequested;
        public event Action OwnershipSwitchRequested;
        public Transform SettingsAnchor { get; private set; }
        public bool IsSelectionActive => root != null;
        // The carousel is exclusively the no-sensory-bag fallback. Keep this guard
        // here as well as in the app-flow branch so a stale/re-entrant Begin call
        // can never leave a swipe surface active while sticker selection is required.
        public bool CanInteract => !SensoryBagOwnership.HasBag && IsSelectionActive && !suspended && !committed && !IsTransitioning;
        public bool IsTransitioning { get; private set; }
        public int SelectedIndex { get; private set; }
        public Transform Root => root;
        public bool IsSwiping => swiping;
        public GlassSlideToEnter GetSlider(int index) => sliders[index];
        public bool HasRecommendationTag(int index) => tags[index] != null && tags[index].gameObject.activeSelf;

        public void Configure(GameObject candyPrefab, GameObject naturePrefab,
            MoodiumWorldDefinition candyWorld, MoodiumWorldDefinition natureWorld, Camera camera)
        { prefabs[0] = candyPrefab; prefabs[1] = naturePrefab; worlds[0] = candyWorld; worlds[1] = natureWorld; viewer = camera; }
        public void ConfigureFonts(TMP_FontAsset cn, TMP_FontAsset en) { chinese = cn; englishFont = en; }
        public void ConfigureEntryGlassMaterial(Material material) => glass = material;

        public void Begin(OpeningVideoSession session)
        {
            End();
            if (SensoryBagOwnership.HasBag)
            {
                Debug.Log("[Sensory Bag Branch] mode=has step=carousel-blocked image-tracking=true");
                return;
            }
            english = session != null ? session.English : PlayerPrefs.GetString("Moodium.Language", "Chinese") == "English";
            session?.Player?.Pause(); session?.SetVisible(false);
            SelectedIndex = recommended == "nature" ? 1 : 0;
            displaySelection = SelectedIndex; committed = suspended = swiping = recentering = false;
            root = new GameObject("World Carousel Selection").transform;
            root.SetParent(transform, false);
            var camera = viewer != null ? viewer : Camera.main;
            if (camera != null) root.SetPositionAndRotation(Placement(camera).position, Placement(camera).rotation);
            var font = english ? englishFont : chinese;
            for (var i = 0; i < 2; i++)
            {
                var item = new GameObject(i == 0 ? "Carousel Candy" : "Carousel Nature");
                item.transform.SetParent(root, false); items[i] = item.transform;
                var target = new GameObject("Model Touch Target");
                target.transform.SetParent(item.transform, false);
                target.AddComponent<BoxCollider>().size = new Vector3(.48f, .46f, .18f);
                target.AddComponent<WorldCarouselSwipeHandle>().Configure(this, i);
                if (prefabs[i] != null)
                {
                    var model = Instantiate(prefabs[i], target.transform);
                    model.name = prefabs[i].name;
                    foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    var bounds = new Bounds(); var hasBounds = false;
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    { if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds); }
                    if (hasBounds)
                    {
                        var size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                        var localCenter = model.transform.InverseTransformPoint(bounds.center);
                        if (size > .0001f) model.transform.localScale *= m_ModelSize / size;
                        model.transform.localPosition -= model.transform.TransformVector(localCenter);
                    }
                    MoodiSelectionMascotController.AttachTo(model);
                    foreach (var moodi in model.GetComponentsInChildren<Moodium.Characters.MoodiCompanionAnimator>(true))
                        mascots[i].Add(moodi.gameObject);
                    if (mascots[i].Count == 0)
                    {
                        var legacy = MoodiSelectionMascotController.FindMascotAnimator(model);
                        if (legacy != null) mascots[i].Add(legacy.gameObject);
                    }
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is SpriteRenderer sprite) { sprites[i].Add((sprite, sprite.color)); continue; }
                        // Moodi stays opaque: switching its skinned mesh to transparent (ZWrite off) makes its
                        // eyes and inner faces show through under RealityKit.
                        if (renderer.GetComponentInParent<Moodium.Characters.MoodiCompanionAnimator>(true) != null) continue;
                        var copies = renderer.sharedMaterials;
                        for (var j = 0; j < copies.Length; j++)
                        {
                            if (copies[j] == null) continue;
                            copies[j] = new Material(copies[j]);
                            if (copies[j].HasProperty("_Surface")) OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(copies[j]);
                            materials[i].Add(copies[j]);
                        }
                        renderer.sharedMaterials = copies;
                    }
                }
                // Controls are siblings of the drag target, never its descendants.
                var control = new GameObject("Enter World Slider");
                control.transform.SetParent(item.transform, false);
                control.transform.localPosition = new Vector3(0, .31f, -.08f);
                sliders[i] = control.AddComponent<GlassSlideToEnter>();
                sliders[i].Configure(SensoryBagBranchUI.EnterLabel(worlds[i], english), i == 0 ? GlassSlideToEnter.Theme.Candy : GlassSlideToEnter.Theme.Nature,
                    font, glass, english ? "Entering…" : "正在进入…");
                var index = i;
                sliders[i].Completed += () => SelectEntry(index);
                tags[i] = SensoryBagBranchUI.Text(item.transform, "Moodi Recommendation Tag", "★ " + MoodCheckCopy.Get("tag.pick", english),
                    new Vector3(0, .40f, -.08f), font, .018f, .38f);
            }
            var drag = new GameObject("Carousel Drag Area");
            drag.transform.SetParent(root, false); drag.transform.localPosition = new Vector3(0, 0, .12f);
            drag.AddComponent<BoxCollider>().size = new Vector3(1.35f, .48f, .02f);
            drag.AddComponent<WorldCarouselSwipeHandle>().Configure(this);
            SensoryBagBranchUI.Button(root, "Previous World", "‹", new Vector3(-.70f, 0, -.04f), font, () => SelectIndex(0), .07f);
            SensoryBagBranchUI.Button(root, "Next World", "›", new Vector3(.70f, 0, -.04f), font, () => SelectIndex(1), .07f);
            var mood = new GameObject("Mood Check Entry"); mood.transform.SetParent(root, false);
            mood.transform.localPosition = new Vector3(0, -.27f, -.08f);
            moodChip = mood.AddComponent<MoodEntryChip>();
            moodChip.Initialize(null, font, () => { if (CanInteract) MoodCheckRequested?.Invoke(); });
            moodChip.AllowIdleHint = false;
            moodChip.SetContent(english, summary);
            // Beside the mood chip (chip spans x ±0.16), not below it: at y -0.39 the gear sat ~37° below the
            // eye line, outside the comfortable gaze range, so it could neither be seen nor tapped.
            SettingsAnchor = Moodium.UI.UIRefreshVisuals.Gear(root, new Vector3(.215f, -.27f, -.08f),
                () => { if (CanInteract) OwnershipSwitchRequested?.Invoke(); }).transform.parent;
            root.gameObject.AddComponent<SensoryBagDirectTouchInput>();
            ApplyLayout();
            Debug.Log("[Sensory Bag Branch] mode=none step=carousel-begin image-tracking=false");
        }

        public void SetMoodCheckSummary(MoodCheckSample sample)
        {
            summary = sample;
            if (moodChip != null) moodChip.SetContent(english, sample);
            SetRecommendedWorld(sample?.recommendedWorldId);
        }
        public void SetRecommendedWorld(string worldId)
        {
            recommended = worldId;
            if (IsSelectionActive && (worldId == "candy" || worldId == "nature")) SelectIndex(worldId == "nature" ? 1 : 0, true);
            ApplyLayout();
        }
        public void SetMoodCheckSuspended(bool value)
        {
            suspended = value; CancelSwipe();
            if (root != null) root.gameObject.SetActive(!value);
        }
        public void SelectIndex(int index, bool force = false)
        {
            if (index < 0 || index >= 2 || !IsSelectionActive || !force && !CanInteract) return;
            SelectedIndex = index; transitionFrom = displaySelection; transitionStart = Time.unscaledTime;
            IsTransitioning = Application.isPlaying && Mathf.Abs(displaySelection - index) > .0001f;
            if (!IsTransitioning) displaySelection = index;
            foreach (var slider in sliders) if (slider != null) slider.ResetSlider();
            ApplyLayout();
        }
        public void SelectEntry(int index)
        {
            if (!CanInteract || swiping || index != SelectedIndex || worlds[index] == null) return;
            committed = true;
            Debug.Log($"[Sensory Bag Branch] mode=none step=world-selected world={worlds[index].WorldId}");
            WorldEntrySelected?.Invoke(new StickerWorldEntry(TrackableId.invalidId, string.Empty, worlds[index], null, null));
        }
        public bool BeginSwipe(Vector3 world)
        {
            if (!CanInteract || swiping) return false;
            swiping = true; swipeStart = world; dragOffset = 0; return true;
        }
        public void MoveSwipe(Vector3 world)
        {
            if (!swiping || root == null) return;
            dragOffset = Vector3.Dot(world - swipeStart, root.right);
            displaySelection = Mathf.Clamp(SelectedIndex - dragOffset / m_ItemSpacing, -.15f, 1.15f);
            ApplyLayout();
        }
        public void EndSwipe(int tapIndex = -1)
        {
            if (!swiping) return;
            swiping = false;
            var index = WorldCarouselLayout.ResolveSwipe(SelectedIndex, dragOffset, m_SwipeThreshold);
            if (Mathf.Abs(dragOffset) < m_SwipeThreshold && tapIndex >= 0) index = tapIndex;
            SelectIndex(index, true);
        }
        public void CancelSwipe()
        {
            swiping = false; dragOffset = 0;
            displaySelection = SelectedIndex; IsTransitioning = false; ApplyLayout();
        }

        Pose Placement(Camera camera)
        {
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            forward.Normalize();
            return new Pose(camera.transform.position + forward * m_Distance - Vector3.up * m_DropBelowEyes, Quaternion.LookRotation(forward, Vector3.up));
        }
        void Update()
        {
            if (!IsSelectionActive || suspended) return;
            if (IsTransitioning)
            {
                var t = (Time.unscaledTime - transitionStart) / Mathf.Max(.01f, m_SwitchSeconds);
                displaySelection = Mathf.Lerp(transitionFrom, SelectedIndex, WorldCarouselLayout.Ease(t));
                if (t >= 1) { displaySelection = SelectedIndex; IsTransitioning = false; }
                ApplyLayout();
            }
            var camera = viewer != null ? viewer : Camera.main;
            if (camera == null || swiping) return;
            var direction = Vector3.ProjectOnPlane(root.position - camera.transform.position, Vector3.up);
            var gaze = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (!recentering && Vector3.Angle(direction, gaze) > m_RecenterAngle)
            { recentering = true; recenterTarget = Placement(camera); }
            if (!recentering) return;
            var factor = 1f - Mathf.Exp(-6 * Time.unscaledDeltaTime);
            root.position = Vector3.Lerp(root.position, recenterTarget.position, factor);
            root.rotation = Quaternion.Slerp(root.rotation, recenterTarget.rotation, factor);
            if (Vector3.Distance(root.position, recenterTarget.position) < .003f && Quaternion.Angle(root.rotation, recenterTarget.rotation) < .5f)
            { root.SetPositionAndRotation(recenterTarget.position, recenterTarget.rotation); recentering = false; }
        }
        void ApplyLayout()
        {
            if (root == null) return;
            for (var i = 0; i < 2; i++)
            {
                if (items[i] == null) continue;
                items[i].localPosition = WorldCarouselLayout.Position(i, displaySelection, m_ItemSpacing);
                items[i].localRotation = Quaternion.Euler(0, -(i - displaySelection) * m_SideYaw, 0);
                items[i].localScale = Vector3.one * WorldCarouselLayout.Scale(i, displaySelection, m_SideScale);
                var alpha = WorldCarouselLayout.Alpha(i, displaySelection, m_SideAlpha);
                foreach (var material in materials[i])
                {
                    var property = material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;
                    if (property != null) { var color = material.GetColor(property); color.a = alpha; material.SetColor(property, color); }
                }
                foreach (var pair in sprites[i]) if (pair.renderer != null) { var color = pair.color; color.a *= alpha; pair.renderer.color = color; }
                foreach (var mascot in mascots[i]) if (mascot != null) mascot.SetActive(i == SelectedIndex);
                if (sliders[i] != null) sliders[i].gameObject.SetActive(i == SelectedIndex && !swiping && !IsTransitioning);
                if (tags[i] != null) tags[i].gameObject.SetActive(worlds[i] != null && worlds[i].WorldId == recommended);
            }
        }
        public void End()
        {
            committed = true; swiping = false; IsTransitioning = false;
            var old = root; root = null;
            if (old != null) SensoryBagBranchUI.Dispose(old.gameObject);
            for (var i = 0; i < 2; i++)
            {
                foreach (var material in materials[i]) if (material != null)
                { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
                materials[i].Clear(); sprites[i].Clear(); mascots[i].Clear();
                items[i] = null; sliders[i] = null; tags[i] = null;
            }
            moodChip = null;
        }
        void OnDisable() => End();
        void OnDestroy() => End();
    }
}
