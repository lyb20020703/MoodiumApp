using System;
using System.Collections;
using System.Collections.Generic;
using Moodium.Flow;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.Guide
{
    public enum GuideWorld { Candy, Nature }

    public enum GuideCard { Press, Free, Rhythm, Breath }

    /// <summary>
    /// The "how to play" cards shown when a world starts, and the "explore freely" card shown once energy reaches 100%.
    ///
    /// Every card face is a pre-rendered sprite (text included, one per world and language) from
    /// Resources/MoodiumWorldGuide/. Only the buttons are separate sprites, so they can carry a collider,
    /// a MoodiumSpatialButton and the visionOS hover highlight.
    ///
    /// The panel is world-locked in front of the viewer and re-centres lazily if the viewer turns away.
    ///
    /// Docked (on demand): <see cref="ShowDocked"/> stands a smaller (30 cm) card on the table behind the "?" button
    /// next to the mode buttons. It does not follow the gaze, has a close × instead of Skip and "Got it" on the last page,
    /// and can start with the "explore freely" page once energy reached 100%. Nothing opens by itself any more; see
    /// GuideHelpController.
    /// Card switching: the Next button, or a horizontal pinch-drag on the card (needs the small
    /// MoodiumSpatialInteractionController patch described in the README).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldGuideCards : MonoBehaviour
    {
        public const string ResourceFolder = "MoodiumWorldGuide/";
        /// <summary>The design is drawn in CSS pixels; a 520 px wide card is 0.42 m in the room.</summary>
        public const float MetersPerDesignPixel = 0.42f / 520f;
        /// <summary>Sprites are exported at 1.5x, so this is the Pixels Per Unit set by the importer.</summary>
        public const float PixelsPerUnit = 1.5f / MetersPerDesignPixel;

        // Layout in design pixels, measured from each card's top-left corner (same numbers as Preview/guide-preview.html).
        public static readonly Vector2 CardSize = new(520f, 660f);
        public static readonly Vector2 ExploreSize = new(640f, 560f);
        const float CardButtonRight = 488f, CardSkipCenterY = 43f, CardNextCenterY = 605f;
        const float ExploreButtonRight = 610f, ExploreButtonCenterY = 505f;
        const float ButtonLift = .003f; // buttons sit 3 mm in front of the card face

        // Docked card on the table (design: "玩法教学 · 规格").
        public const float DockedWidth = .30f;
        public static float DockedScale => DockedWidth / (CardSize.x * MetersPerDesignPixel);
        public const float DockedTiltDegrees = 12f;
        public const float DockedLift = .02f;

        static readonly GuideCard[] Cards = { GuideCard.Press, GuideCard.Free, GuideCard.Rhythm, GuideCard.Breath };

        [Header("Placement")]
        [SerializeField, Range(.5f, 1.5f)] float m_Distance = .9f;
        [Tooltip("How far below eye height the card centre sits.")]
        [SerializeField, Range(0f, .4f)] float m_DropBelowEyes = .12f;
        [Tooltip("Re-centre in front of the viewer when they look more than this many degrees away.")]
        [SerializeField, Range(20f, 90f)] float m_RecenterAngle = 50f;

        [Header("Behaviour")]
        [Tooltip("Off: the tutorial is shown only the first time each world is entered on this device.")]
        [SerializeField] bool m_ShowTutorialEveryTime = true;
        [SerializeField, Min(0f)] float m_ExploreDelay = 2.5f;
        [Tooltip("Horizontal pinch-drag distance (metres) that turns the page.")]
        [SerializeField, Range(.02f, .15f)] float m_SwipeThreshold = .05f;
        [SerializeField] int m_SortingOrder = 40;

        /// <summary>Raised when the tutorial closes. True if the user pressed Skip.</summary>
        public event Action<bool> TutorialFinished;
        /// <summary>Raised when the explore card closes.</summary>
        public event Action ExploreClosed;
        /// <summary>Raised when the docked help card closes (×, Got it, or CloseDocked).</summary>
        public event Action DockedClosed;

        enum Mode { None, Tutorial, Explore, Docked }

        struct Page
        {
            public bool Explore;
            public GuideCard Card;
        }
        readonly List<Page> m_Pages = new();
        bool m_Docked;

        readonly Dictionary<string, Sprite> m_Loaded = new();
        sealed class SharedSprite
        {
            public Sprite Sprite;
            public int Owners;
        }
        static readonly Dictionary<string,SharedSprite> SharedSprites=new();
        Mode m_Mode;
        GuideWorld m_World;
        bool m_English;
        int m_Index;
        Transform m_Viewer;
        Transform m_Root;
        Transform m_Pivot;
        SpriteRenderer m_Face;
        SpriteRenderer m_SkipButton;
        SpriteRenderer m_ActionButton;
        BoxCollider m_SwipeCollider;
        VisionOSSortingGroup m_Sorting;
        Coroutine m_Transition;
        Coroutine m_PendingExplore;
        bool m_Recentering;
        bool m_Swiping;
        Vector3 m_SwipeStart;
        float m_SwipeOffset;
        float m_Alpha = 1f;
        bool m_Closing;

        internal bool CanInteract => (m_Mode==Mode.Tutorial || m_Mode==Mode.Docked) && m_Root!=null && !m_Closing &&
            !m_Swiping && m_Transition==null && m_Alpha>.6f;

        public bool IsShowing => m_Root != null;
        public bool IsTutorialShowing => m_Mode == Mode.Tutorial && m_Root != null;
        public bool IsDockedOpen => m_Mode == Mode.Docked && m_Root != null && !m_Closing;
        public int CardIndex => m_Index;
        public int PageCount => m_Pages.Count;

        // ------------------------------------------------------------------ public API

        public void ShowTutorial(bool natureWorld, bool english, Transform viewer)
        {
            var world = natureWorld ? GuideWorld.Nature : GuideWorld.Candy;
            if (!m_ShowTutorialEveryTime && PlayerPrefs.GetInt(SeenKey(world), 0) == 1)
                return;
            CancelPendingExplore();
            Open(Mode.Tutorial, world, english, viewer);
            PlayerPrefs.SetInt(SeenKey(world), 1);
        }

        public void ShowExplore(bool natureWorld, bool english, Transform viewer, float delay = -1f)
        {
            CancelPendingExplore();
            m_PendingExplore = StartCoroutine(ShowExploreAfter(delay < 0f ? m_ExploreDelay : delay,
                natureWorld ? GuideWorld.Nature : GuideWorld.Candy, english, viewer));
        }

        /// <summary>
        /// Opens the help card standing on the table at <paramref name="dock"/> (the dock's +Z faces the viewer).
        /// With <paramref name="includeExplore"/> the "explore freely" page comes first.
        /// </summary>
        public void ShowDocked(Transform dock, bool natureWorld, bool english, bool includeExplore, int startPage = 0)
        {
            if (dock == null) return;
            CancelPendingExplore();
            Open(Mode.Docked, natureWorld ? GuideWorld.Nature : GuideWorld.Candy, english, null, dock, includeExplore, startPage);
        }

        public void CloseDocked()
        {
            if (m_Mode != Mode.Docked || m_Root == null) return;
            BeginClose();
            DockedClosed?.Invoke();
        }

        /// <summary>Turns the open docked card to <paramref name="card"/>'s page (used when a mode button is pressed).</summary>
        public void JumpTo(GuideCard card)
        {
            if (!IsDockedOpen || m_Swiping) return;
            var index = m_Pages.FindIndex(p => !p.Explore && p.Card == card);
            if (index < 0 || index == m_Index) return;
            GoTo(index, index > m_Index ? 1 : -1);
        }

        public static int DockedPageCount(bool includeExplore) => Cards.Length + (includeExplore ? 1 : 0);
        public static int DockedPageIndex(GuideCard card, bool includeExplore) =>
            Array.IndexOf(Cards, card) + (includeExplore ? 1 : 0);

        /// <summary>Card centre in dock space: bottom edge <see cref="DockedLift"/> above the table, leaning back.</summary>
        public static Vector3 DockedLocalPosition()
        {
            var half = CardSize.y * MetersPerDesignPixel * DockedScale * .5f;
            var tilt = DockedTiltDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, DockedLift + half * Mathf.Cos(tilt), -half * Mathf.Sin(tilt));
        }

        /// <summary>The card's +Z points away from the viewer (project convention); +X tilt leans its top away.</summary>
        public static Quaternion DockedLocalRotation() => Quaternion.Euler(DockedTiltDegrees, 180f, 0f);

        public void Next()
        {
            if (!CanInteract) return;
            if (m_Index >= m_Pages.Count - 1) { Finish(false); return; }
            GoTo(m_Index + 1, 1);
        }

        public void Previous()
        {
            if (!CanInteract || m_Index == 0) return;
            GoTo(m_Index - 1, -1);
        }

        public void Skip() => Finish(true);

        public void Hide(bool immediate = false)
        {
            CancelPendingExplore();
            if (immediate || !isActiveAndEnabled) { DestroyPanel();m_Mode=Mode.None;return; }
            BeginClose();
        }

        void BeginClose()
        {
            if(m_Root==null||m_Closing)return;
            m_Closing=true;m_Swiping=false;
            m_Mode = Mode.None;
            SetInput(false);
            Animate(FadeOutAndDestroy());
        }

        // ------------------------------------------------------------------ helpers (public for tests)

        public static bool IsEnglish() => PlayerPrefs.GetString("Moodium.Language", "Chinese") == "English";

        static string W(GuideWorld world) => world == GuideWorld.Nature ? "nature" : "candy";
        static string L(bool english) => english ? "en" : "zh";

        public static string CardSpriteName(GuideWorld world, GuideCard card, bool english) =>
            $"guide_card_{W(world)}_{card.ToString().ToLowerInvariant()}_{L(english)}";

        public static string ExploreSpriteName(GuideWorld world, bool english) => $"guide_explore_{W(world)}_{L(english)}";

        /// <summary>kind: "next", "start" or "explore".</summary>
        public static string ActionButtonSpriteName(string kind, GuideWorld world, bool english) =>
            $"guide_btn_{kind}_{W(world)}_{L(english)}";

        public static string SkipSpriteName(bool english) => $"guide_btn_skip_{L(english)}";

        public const string CloseSpriteName = "guide_btn_close";

        /// <summary>+1 = next card (dragged left), -1 = previous card (dragged right), 0 = snap back.</summary>
        public static int SwipeStep(float dragMeters, float threshold) =>
            dragMeters <= -threshold ? 1 : dragMeters >= threshold ? -1 : 0;

        /// <summary>Local position (metres, card centre = origin) of a sprite whose right edge and vertical centre are given in design pixels.</summary>
        public static Vector3 PlaceByRightEdge(Vector2 cardSize, float rightPx, float centerYPx, float spriteWidthMeters) =>
            new((rightPx - cardSize.x * .5f) * MetersPerDesignPixel - spriteWidthMeters * .5f,
                (cardSize.y * .5f - centerYPx) * MetersPerDesignPixel, -ButtonLift);

        static string SeenKey(GuideWorld world) => "Moodium.WorldGuide.Seen." + W(world);

        // ------------------------------------------------------------------ building

        IEnumerator ShowExploreAfter(float delay, GuideWorld world, bool english, Transform viewer)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            while(IsTutorialShowing)yield return null;
            m_PendingExplore = null;
            Open(Mode.Explore, world, english, viewer);
        }

        void Open(Mode mode, GuideWorld world, bool english, Transform viewer, Transform dock = null,
            bool includeExplore = false, int startPage = 0)
        {
            if (m_Root != null) DestroyPanel();
            m_Mode = mode;
            m_Closing=false;
            m_World = world;
            m_English = english;
            m_Docked = mode == Mode.Docked;
            m_Viewer = viewer != null ? viewer : Camera.main != null ? Camera.main.transform : null;
            m_Pages.Clear();
            if (mode == Mode.Explore || (mode == Mode.Docked && includeExplore)) m_Pages.Add(new Page { Explore = true });
            if (mode != Mode.Explore) foreach (var card in Cards) m_Pages.Add(new Page { Card = card });
            m_Index = Mathf.Clamp(startPage, 0, m_Pages.Count - 1);

            m_Root = new GameObject(mode == Mode.Tutorial ? "World Guide · Tutorial" : mode == Mode.Docked ? "World Guide · Help (docked)" : "World Guide · Explore").transform;
            if (m_Docked)
            {
                m_Root.SetParent(dock, false);
                m_Root.localPosition = DockedLocalPosition();
                m_Root.localRotation = DockedLocalRotation();
                m_Root.localScale = Vector3.one * DockedScale;
            }
            m_Sorting = m_Root.gameObject.AddComponent<VisionOSSortingGroup>();
            m_Pivot = new GameObject("Card").transform;
            m_Pivot.SetParent(m_Root, false);

            m_Face = CreateSprite("Face", m_Pivot, m_SortingOrder);
            if (mode == Mode.Tutorial || mode == Mode.Docked)
            {
                m_SkipButton = CreateButton(m_Docked ? "Close" : "Skip", m_Docked ? CloseSpriteName : SkipSpriteName(english), Skip);
                m_ActionButton = CreateButton("Next", null, Next);
                var swipe = new GameObject("Swipe Surface", typeof(BoxCollider), typeof(WorldGuideSwipeHandle));
                swipe.transform.SetParent(m_Pivot, false);
                swipe.transform.localPosition = new Vector3(0f, 0f, .004f);
                m_SwipeCollider = swipe.GetComponent<BoxCollider>();
                m_SwipeCollider.size = new Vector3(CardSize.x * MetersPerDesignPixel, CardSize.y * MetersPerDesignPixel, .002f);
                swipe.GetComponent<WorldGuideSwipeHandle>().Configure(this);
            }
            else
            {
                m_ActionButton = CreateButton("Explore", ActionButtonSpriteName("explore", world, english), CloseExplore);
            }
            ApplyPage();
            if (!m_Docked) PlaceInFront(true);
            Animate(FadeIn());
        }

        SpriteRenderer CreateSprite(string objectName, Transform parent, int order)
        {
            var go = new GameObject(objectName, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            m_Sorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting { Renderer = go, Order = order, ApplyToDescendants = false });
            return renderer;
        }

        SpriteRenderer CreateButton(string objectName, string spriteName, Action onPressed)
        {
            var renderer = CreateSprite(objectName + " Button", m_Pivot, m_SortingOrder + 1);
            if (spriteName != null) renderer.sprite = Load(spriteName);
            renderer.gameObject.AddComponent<BoxCollider>();
            // The card owns transitions/input gating; don't queue delayed button callbacks across pages.
            renderer.gameObject.AddComponent<MoodiumSpatialButton>().Configure(null, null, () =>
            {
                if(m_Root!=null&&!m_Closing&&m_Transition==null&&!m_Swiping&&m_Alpha>.6f)onPressed();
            });
            return renderer;
        }

        /// <summary>Puts the right sprites on the face and buttons for the current mode and page.</summary>
        // The explore page is the pre-rendered card only. The light-ring hint line that used to be drawn on top of it
        // is gone: the ring step no longer exists in the experience.
        void ApplyPage()
        {
            if (m_Mode == Mode.Tutorial || m_Mode == Mode.Docked)
            {
                var page = m_Pages[m_Index];
                var last = m_Index == m_Pages.Count - 1;
                var size = page.Explore ? ExploreSize : CardSize;
                m_Face.sprite = Load(page.Explore ? ExploreSpriteName(m_World, m_English) : CardSpriteName(m_World, page.Card, m_English));
                SetButtonSprite(m_ActionButton, ActionButtonSpriteName(last ? (m_Docked ? "gotit" : "start") : "next", m_World, m_English));
                LayoutButton(m_SkipButton, size, page.Explore ? ExploreButtonRight : CardButtonRight, CardSkipCenterY);
                if (page.Explore) LayoutButton(m_ActionButton, ExploreSize, ExploreButtonRight, ExploreButtonCenterY);
                else LayoutButton(m_ActionButton, CardSize, CardButtonRight, CardNextCenterY);
                if (m_SwipeCollider != null)
                    m_SwipeCollider.size = new Vector3(size.x * MetersPerDesignPixel, size.y * MetersPerDesignPixel, .002f);
            }
            else
            {
                m_Face.sprite = Load(ExploreSpriteName(m_World, m_English));
                LayoutButton(m_ActionButton, ExploreSize, ExploreButtonRight, ExploreButtonCenterY);
            }
            SetAlpha(m_Alpha);
        }

        void SetButtonSprite(SpriteRenderer button, string spriteName) => button.sprite = Load(spriteName);

        static void LayoutButton(SpriteRenderer button, Vector2 cardSize, float rightPx, float centerYPx)
        {
            if (button == null || button.sprite == null) return;
            var size = button.sprite.bounds.size;
            button.transform.localPosition = PlaceByRightEdge(cardSize, rightPx, centerYPx, size.x);
            var collider = button.GetComponent<BoxCollider>();
            collider.size = new Vector3(size.x + .01f, size.y + .01f, .012f);
        }

        Sprite Load(string spriteName)
        {
            if (m_Loaded.TryGetValue(spriteName, out var sprite) && sprite != null) return sprite;
            if(!SharedSprites.TryGetValue(spriteName,out var shared)||shared.Sprite==null)
            {
                shared=new SharedSprite{Sprite=Resources.Load<Sprite>(ResourceFolder+spriteName)};
                SharedSprites[spriteName]=shared;
            }
            sprite=shared.Sprite;
            if(sprite!=null)shared.Owners++;
            if (sprite == null) Debug.LogWarning("[World Guide] Missing sprite: " + spriteName);
            m_Loaded[spriteName] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------------ paging and swipe

        void GoTo(int index, int direction)
        {
            index = Mathf.Clamp(index, 0, m_Pages.Count - 1);
            if (index == m_Index) { Animate(SnapBack()); return; }
            Animate(TurnPage(index, direction));
        }

        void Finish(bool skipped)
        {
            if (m_Mode == Mode.Docked) { CloseDocked(); return; }
            if (m_Mode != Mode.Tutorial) return;
            BeginClose();
            TutorialFinished?.Invoke(skipped);
        }

        void CloseExplore()
        {
            if (m_Mode != Mode.Explore) return;
            BeginClose();
            ExploreClosed?.Invoke();
        }

        internal void BeginSwipe(Vector3 worldPosition)
        {
            if (!CanInteract) return;
            StopTransition();
            m_Swiping = true;
            m_SwipeStart = worldPosition;
            m_SwipeOffset = 0f;
        }

        internal void MoveSwipe(Vector3 worldPosition)
        {
            if (!m_Swiping || m_Root == null) return;
            var drag = Vector3.Dot(worldPosition - m_SwipeStart, m_Root.right);
            // Rubber-band at the first and last card.
            if ((m_Index == 0 && drag > 0f) || (m_Index == m_Pages.Count - 1 && drag < 0f)) drag *= .35f;
            m_SwipeOffset = Mathf.Clamp(drag, -.14f, .14f);
            m_Pivot.localPosition = new Vector3(m_SwipeOffset, 0f, 0f);
            SetAlpha(1f - Mathf.Abs(m_SwipeOffset) / .3f);
        }

        internal void EndSwipe()
        {
            if (!m_Swiping) return;
            m_Swiping = false;
            var step = SwipeStep(m_SwipeOffset, m_SwipeThreshold);
            var target = m_Index + step;
            if (step == 0 || target < 0 || target >= m_Pages.Count) Animate(SnapBack());
            else GoTo(target, step);
        }

        internal void CancelSwipe()
        {
            if(!m_Swiping)return;
            m_Swiping=false;
            if(m_Root!=null&&!m_Closing)Animate(SnapBack());
        }

        // ------------------------------------------------------------------ animation

        void Animate(IEnumerator routine)
        {
            StopTransition();
            m_Transition = StartCoroutine(routine);
        }

        void StopTransition()
        {
            if (m_Transition != null) StopCoroutine(m_Transition);
            m_Transition = null;
        }

        static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        IEnumerator FadeIn()
        {
            for (var t = 0f; t < 1f; t += Time.deltaTime / .35f)
            {
                var e = Ease(t);
                m_Pivot.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, e);
                m_Pivot.localPosition = new Vector3(0f, Mathf.Lerp(-.02f, 0f, e), 0f);
                SetAlpha(e);
                yield return null;
            }
            m_Pivot.localScale = Vector3.one;
            m_Pivot.localPosition = Vector3.zero;
            SetAlpha(1f);
            m_Transition = null;
        }

        IEnumerator TurnPage(int index, int direction)
        {
            var from = m_Pivot.localPosition.x;
            var fromAlpha = m_Alpha;
            for (var t = 0f; t < 1f; t += Time.deltaTime / .14f)
            {
                m_Pivot.localPosition = new Vector3(Mathf.Lerp(from, -.07f * direction, t), 0f, 0f);
                SetAlpha(Mathf.Lerp(fromAlpha, 0f, t));
                yield return null;
            }
            m_Index = index;
            ApplyPage();
            for (var t = 0f; t < 1f; t += Time.deltaTime / .22f)
            {
                var e = Ease(t);
                m_Pivot.localPosition = new Vector3(Mathf.Lerp(.07f * direction, 0f, e), 0f, 0f);
                SetAlpha(e);
                yield return null;
            }
            m_Pivot.localPosition = Vector3.zero;
            SetAlpha(1f);
            m_Transition = null;
        }

        IEnumerator SnapBack()
        {
            var from = m_Pivot.localPosition.x;
            var fromAlpha = m_Alpha;
            for (var t = 0f; t < 1f; t += Time.deltaTime / .2f)
            {
                var e = Ease(t);
                m_Pivot.localPosition = new Vector3(Mathf.Lerp(from, 0f, e), 0f, 0f);
                SetAlpha(Mathf.Lerp(fromAlpha, 1f, e));
                yield return null;
            }
            m_Pivot.localPosition = Vector3.zero;
            SetAlpha(1f);
            m_Transition = null;
        }

        IEnumerator FadeOutAndDestroy()
        {
            var fromAlpha = m_Alpha;
            for (var t = 0f; t < 1f; t += Time.deltaTime / .25f)
            {
                SetAlpha(Mathf.Lerp(fromAlpha, 0f, t));
                m_Pivot.localScale = Vector3.one * Mathf.Lerp(1f, .96f, t);
                yield return null;
            }
            m_Transition = null;
            DestroyPanel();
        }

        void SetAlpha(float alpha)
        {
            m_Alpha = Mathf.Clamp01(alpha);
            var color = new Color(1f, 1f, 1f, m_Alpha);
            if (m_Face != null) m_Face.color = color;
            if (m_SkipButton != null) m_SkipButton.color = color;
            if (m_ActionButton != null) m_ActionButton.color = color;
            // Buttons only react once the card is clearly visible.
            SetInput(!m_Closing && m_Alpha>.6f);
        }

        void SetInput(bool interactive)
        {
            if (m_SkipButton != null) m_SkipButton.GetComponent<BoxCollider>().enabled = interactive;
            if (m_ActionButton != null) m_ActionButton.GetComponent<BoxCollider>().enabled = interactive;
            if(m_SwipeCollider!=null)m_SwipeCollider.enabled=interactive;
        }

        // ------------------------------------------------------------------ placement

        void LateUpdate()
        {
            if (m_Root == null || m_Viewer == null || m_Swiping || m_Docked) return;   // the docked card stays on the table
            var toCard = m_Root.position - m_Viewer.position;
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up);
            var flat = Vector3.ProjectOnPlane(toCard, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f || flat.sqrMagnitude < 1e-4f) return;
            if (!m_Recentering && Vector3.Angle(forward, flat) > m_RecenterAngle) m_Recentering = true;
            if (m_Recentering) PlaceInFront(false);
        }

        void PlaceInFront(bool snap)
        {
            if (m_Viewer == null) return;
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            var target = m_Viewer.position + forward * m_Distance + Vector3.down * m_DropBelowEyes;
            var k = snap ? 1f : 1f - Mathf.Exp(-4f * Time.deltaTime);
            m_Root.position = Vector3.Lerp(m_Root.position, target, k);
            // Project convention: the panel's +Z points away from the viewer.
            var away = Vector3.ProjectOnPlane(m_Root.position - m_Viewer.position, Vector3.up);
            if (away.sqrMagnitude > 1e-4f) m_Root.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            if (!snap && (m_Root.position - target).sqrMagnitude < .0004f) m_Recentering = false;
        }

        // ------------------------------------------------------------------ cleanup

        void CancelPendingExplore()
        {
            if (m_PendingExplore != null) StopCoroutine(m_PendingExplore);
            m_PendingExplore = null;
        }

        void DestroyPanel()
        {
            StopTransition();
            m_Swiping = false;
            m_Recentering = false;
            if (m_Root != null)
            {
                m_Root.gameObject.SetActive(false);
                foreach(var renderer in m_Root.GetComponentsInChildren<SpriteRenderer>(true))renderer.sprite=null;
                if(Application.isPlaying)Destroy(m_Root.gameObject);else DestroyImmediate(m_Root.gameObject);
            }
            m_Root = null;
            m_Docked = false;
            m_Pivot = null;
            m_Face = m_SkipButton = m_ActionButton = null;
            m_SwipeCollider = null;
            m_Sorting = null;
            // Renderers are detached above, so the last owner can safely unload the imported assets.
            foreach(var pair in m_Loaded)
            {
                if(!SharedSprites.TryGetValue(pair.Key,out var shared))continue;
                if(--shared.Owners>0)continue;
                SharedSprites.Remove(pair.Key);
                if(shared.Sprite==null)continue;
                var texture=shared.Sprite.texture;
                Resources.UnloadAsset(shared.Sprite);
                if(texture!=null)Resources.UnloadAsset(texture);
            }
            m_Loaded.Clear();
        }

        void OnDisable() => Hide(true);
        void OnDestroy() => Hide(true);
    }
}
