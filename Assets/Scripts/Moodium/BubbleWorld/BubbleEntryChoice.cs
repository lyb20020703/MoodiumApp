using System;
using Moodium.Flow;
using Moodium.MoodCheck;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// 泡泡世界 2.0 (P0) rules. Each family has two worlds and they never mix:
    /// Candy = 棉花糖 (CandyFolder) / 薄荷巧克力 (NestedCandyFolder); Nature = 绿色森林 (NatureFolder) / 雪原树林 (NestedNatureFolder).
    /// In the room the user picks one of the two (the mood's pick is bigger, closer and ringed, without any voice);
    /// that world is the first layer and the other one is its "next layer". Pure helpers for EditMode tests.
    /// </summary>
    public static class BubbleChoiceRules
    {
        public const string SpriteFolder = "MoodiumBubbleWorld/Choice/";
        /// <summary>
        /// The other world's bubble sits this far to the right of the real one and a little higher (centres, metres).
        /// The real bubble already hovers left of the sensory bag (CandyWorldManager.BubbleHover), so the right keeps both
        /// near the middle of the view; the lift keeps it above Moodi, who floats low beside the real bubble.
        /// </summary>
        public const float PartnerSide = .42f, PartnerLift = .1f;
        /// <summary>The recommended bubble is this much closer than the other one, and the other one is this much smaller.</summary>
        public const float PickCloser = .1f, OtherScale = .87f;
        public const float LabelScale = .7f, LabelGap = .05f;
        /// <summary>First layer: home (left) and next layer (right), 30 cm apart, following the viewer as one pair.</summary>
        public const float PairHalf = .15f, PortalRadius = .11f, HomeRadius = .08f;
        /// <summary>Home / next-layer tags show for this long after the pair appears, and whenever a fingertip is this close.</summary>
        public const float PairTagSeconds = 4f, TagHandDistance = .25f, TagFade = .3f;
        /// <summary>A check-in older than this is not used for the recommendation.</summary>
        public const double FreshMoodMinutes = 60;

        /// <summary>The other world of the same family, or null.</summary>
        public static string Partner(string folder) => folder switch
        {
            BubbleWorldData.CandyFolder => BubbleWorldData.NestedCandyFolder,
            BubbleWorldData.NestedCandyFolder => BubbleWorldData.CandyFolder,
            BubbleWorldData.NatureFolder => BubbleWorldData.NestedNatureFolder,
            BubbleWorldData.NestedNatureFolder => BubbleWorldData.NatureFolder,
            // Only used by the old nested layers (m_NextLayer, off): the door switches through all three nature worlds.
            BubbleWorldData.MapleNatureFolder => BubbleWorldData.NatureFolder,
            _ => null,
        };

        /// <summary>Sprite key of a world: candy (棉花糖), mint (薄荷巧克力), forest (绿色森林), maple (枫林), snow (雪原树林).</summary>
        public static string Key(string folder) => folder switch
        {
            BubbleWorldData.CandyFolder => "candy",
            BubbleWorldData.NestedCandyFolder => "mint",
            BubbleWorldData.NatureFolder => "forest",
            BubbleWorldData.NestedNatureFolder => "snow",
            BubbleWorldData.MapleNatureFolder => "maple",
            _ => null,
        };

        /// <summary>
        /// The mood's pick within a family (DESIGN.md §6), or null without a check-in.
        /// Candy: tense / calm → 薄荷巧克力 (cool mint, slow breathing); low / okay / happy → 棉花糖.
        /// Nature: tense / calm → 雪原树林 (quiet, the world slows down); low → 枫林 (warm colours, leaves you can catch and
        /// let go); neutral / happy → 绿色森林.
        /// </summary>
        public static string Recommend(bool candy, MoodKind? mood)
        {
            if (!mood.HasValue) return null;
            var still = mood.Value == MoodKind.Tense || mood.Value == MoodKind.Calm;
            if (candy) return still ? BubbleWorldData.NestedCandyFolder : BubbleWorldData.CandyFolder;
            // Maple temporarily disabled: low mood uses the forest.
            return still ? BubbleWorldData.NestedNatureFolder : BubbleWorldData.NatureFolder;
        }

        /// <summary>The pre check-in's mood when it was made within the last hour (the sample object outlives visits).</summary>
        public static MoodKind? FreshMood(MoodCheckSample sample, DateTime utcNow)
        {
            if (sample == null || sample.phase != MoodCheckPhase.Pre || string.IsNullOrEmpty(sample.timestampUtc)) return null;
            if (!DateTime.TryParse(sample.timestampUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var at)) return null;
            var age = (utcNow - at).TotalMinutes;
            return age >= -1 && age <= FreshMoodMinutes ? sample.mood : (MoodKind?)null;
        }

        /// <summary>Flat forward of the viewer (yaw only).</summary>
        public static Vector3 Flat(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
        }

        /// <summary>Where the other world's bubble hovers: upper right of the real one, closer when it is the pick, further when the real one is.</summary>
        public static Vector3 PartnerPosition(Vector3 hover, Vector3 viewerForward, bool partnerPicked, bool realPicked)
        {
            var flat = Flat(viewerForward);
            var right = Quaternion.Euler(0f, 90f, 0f) * flat;
            return hover + right * PartnerSide + Vector3.up * PartnerLift - flat * (partnerPicked ? PickCloser : 0f) + flat * (realPicked ? PickCloser : 0f);
        }

        /// <summary>Bubble radius: full for the pick (or when nothing is picked), slightly smaller for the other one.</summary>
        public static float EntryRadius(float baseRadius, string pick, string folder) =>
            pick == null || pick == folder ? baseRadius : baseRadius * OtherScale;

        /// <summary>First layer: home on the left, next layer on the right of <paramref name="centre"/>.</summary>
        public static (Vector3 home, Vector3 next) Pair(Vector3 centre, Vector3 viewerForward)
        {
            var right = Quaternion.Euler(0f, 90f, 0f) * Flat(viewerForward);
            return (centre - right * PairHalf, centre + right * PairHalf);
        }

        public static bool TagVisible(float sinceShown, float tipDistance) =>
            sinceShown < PairTagSeconds || tipDistance < TagHandDistance;

        /// <summary>The world's name only (「棉花糖世界」) under its bubble in the room.</summary>
        public static string WorldLabel(string folder, bool english) =>
            Key(folder) is { } key ? SpriteFolder + "choice_world_" + key + "_" + (english ? "en" : "zh") : null;
        public static string NextLabel(string folder, bool english) =>
            Key(folder) is { } key ? SpriteFolder + "choice_next_" + key + "_" + (english ? "en" : "zh") : null;
        public static string HomeLabel(bool english) => SpriteFolder + "choice_home_" + (english ? "en" : "zh");
        public const string RingSprite = SpriteFolder + "choice_ring";
    }

    /// <summary>A label sprite that faces the viewer, fades, and is switched off when invisible (visionOS still draws alpha-0 quads).</summary>
    public sealed class BubbleTag
    {
        public readonly SpriteRenderer Renderer;
        float m_Alpha;
        public BubbleTag(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Renderer = go.AddComponent<SpriteRenderer>();
            Renderer.color = new Color(1f, 1f, 1f, 0f);
            go.SetActive(false);
        }
        public bool SetSprite(string id)
        {
            var sprite = id != null ? Moodium.UI.UIRefreshVisuals.Load(id) : null;
            Renderer.sprite = sprite;
            return sprite != null;
        }
        public float Height => Renderer.sprite != null ? Renderer.sprite.bounds.size.y * BubbleChoiceRules.LabelScale : 0f;
        /// <summary>Places the tag's top edge at <paramref name="top"/>, facing <paramref name="eye"/>, and moves its alpha toward <paramref name="target"/>.</summary>
        public void Place(Vector3 top, Vector3 eye, float target, float fadeSeconds, float dt)
        {
            var t = Renderer.transform;
            var parentScale = t.parent != null ? t.parent.lossyScale.x : 1f;
            t.localScale = Vector3.one * BubbleChoiceRules.LabelScale / Mathf.Max(1e-4f, parentScale);
            var centre = top - Vector3.up * (Height * .5f);
            var look = centre - eye;
            t.SetPositionAndRotation(centre, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity);
            m_Alpha = fadeSeconds <= 0f ? target : Mathf.MoveTowards(m_Alpha, target, dt / fadeSeconds);
            var on = m_Alpha > .01f && Renderer.sprite != null;
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            Renderer.color = new Color(1f, 1f, 1f, m_Alpha);
        }
        public void Hide() { m_Alpha = 0f; Renderer.gameObject.SetActive(false); }
        public float Alpha => m_Alpha;
    }

    /// <summary>
    /// The room entry offers both worlds of the family. <see cref="BubbleWorldController"/> keeps its real bubble (the
    /// first-layer world, already built) and this component adds, while it floats:
    /// - the other world as a light bubble (that world's panorama inside a film; its points load only if it is chosen);
    /// - a glass tag under each with just the world's name (「棉花糖世界」 / 「薄荷巧克力世界」);
    /// - a warm breathing ring around the mood's pick (no words, no voice).
    /// Touching the other bubble sets <see cref="PartnerTouched"/>; the controller then rebuilds with that world first.
    /// </summary>
    public sealed class BubbleEntryChoice : MonoBehaviour
    {
        public const float RiseSeconds = .8f, SinkSeconds = .6f, BobAmplitude = .015f, BobPeriod = 3.4f, SpinDegrees = 6f;

        Camera m_Viewer;
        Func<Vector3> m_RealCentre;
        Func<float> m_RealRadius;
        Func<Vector3, float, bool> m_Touching;
        string m_Real, m_Partner, m_Pick;
        bool m_English;
        GameObject m_PartnerRoot;
        Transform m_Film, m_Inner;
        Material m_FilmMaterial, m_InnerMaterial;
        Mesh m_RuntimeSphere;
        BubbleTag m_RealLabel, m_PartnerLabel;
        SpriteRenderer m_Ring;
        Vector3 m_From, m_Hover;
        float m_Radius, m_TargetRadius, m_ShownAt, m_LeaveAt = -1f, m_FilmAlpha = .85f;
        bool m_Active, m_Leaving, m_Armed, m_Loading, m_RealDecor;

        public bool PartnerTouched { get; private set; }
        public string PartnerFolder => m_Partner;
        public string Pick => m_Pick;
        public Vector3 PartnerCentre => m_PartnerRoot != null ? m_PartnerRoot.transform.position : m_Hover;
        public float PartnerRadius => m_TargetRadius;
        public bool Visible => m_Active;
        public SpriteRenderer Ring => m_Ring;
        public BubbleTag RealLabel => m_RealLabel;
        public BubbleTag PartnerLabel => m_PartnerLabel;
        public GameObject PartnerBubble => m_PartnerRoot;

        public static BubbleEntryChoice Create()
        {
            var go = new GameObject("Bubble Entry Choice");
            return go.AddComponent<BubbleEntryChoice>();
        }

        /// <summary>
        /// Shows the other world beside the real bubble. <paramref name="realCentre"/> / <paramref name="realRadius"/> follow the
        /// real bubble; <paramref name="touching"/> is the controller's fingertip test; <paramref name="pick"/> may be null.
        /// </summary>
        public void Show(Camera viewer, string realFolder, string pick, Vector3 from, Vector3 realHover, float baseRadius,
            Func<Vector3> realCentre, Func<float> realRadius, Func<Vector3, float, bool> touching, Color filmTint, float filmAlpha)
        {
            Hide();
            m_Viewer = viewer; m_Real = realFolder; m_Partner = BubbleChoiceRules.Partner(realFolder); m_Pick = pick;
            m_RealCentre = realCentre; m_RealRadius = realRadius; m_Touching = touching; m_FilmAlpha = filmAlpha;
            m_English = Moodium.Characters.MoodiVoice.LanguageCode == "en";
            if (m_Partner == null || viewer == null) return;
            m_From = from;
            m_Hover = BubbleChoiceRules.PartnerPosition(realHover, viewer.transform.forward, pick == m_Partner, pick == m_Real);
            m_TargetRadius = BubbleChoiceRules.EntryRadius(baseRadius, pick, m_Partner);
            BuildPartner(filmTint);
            m_RealLabel ??= new BubbleTag(transform, "Real Label");
            m_PartnerLabel ??= new BubbleTag(transform, "Partner Label");
            m_RealLabel.SetSprite(BubbleChoiceRules.WorldLabel(m_Real, m_English));
            m_PartnerLabel.SetSprite(BubbleChoiceRules.WorldLabel(m_Partner, m_English));
            if (m_Ring == null)
            {
                var ring = new GameObject("Pick Ring");
                ring.transform.SetParent(transform, false);
                m_Ring = ring.AddComponent<SpriteRenderer>();
                m_Ring.sprite = Moodium.UI.UIRefreshVisuals.Load(BubbleChoiceRules.RingSprite);
            }
            m_Ring.gameObject.SetActive(false);
            m_Active = true; m_Leaving = false; m_Armed = false; m_Loading = false; m_RealDecor = true; PartnerTouched = false;
            m_ShownAt = -1f; m_LeaveAt = -1f; m_Radius = .001f;   // both start on the next Step
            Debug.Log($"[Bubble Choice] {BubbleChoiceRules.Key(m_Real)} (real) / {BubbleChoiceRules.Key(m_Partner)} (panorama), pick={BubbleChoiceRules.Key(pick) ?? "none"}");
        }

        void BuildPartner(Color tint)
        {
            if (m_PartnerRoot != null) return;
            m_PartnerRoot = new GameObject("Choice Bubble (" + BubbleChoiceRules.Key(m_Partner) + ")");
            m_PartnerRoot.transform.SetParent(transform, false);
            var inner = new GameObject("Panorama");
            inner.transform.SetParent(m_PartnerRoot.transform, false);
            var mesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(m_Partner, BubbleWorldData.PanoramaMeshName));
            if (mesh == null) mesh = m_RuntimeSphere = BubbleWorldData.BuildPanoramaSphere();
            inner.AddComponent<MeshFilter>().sharedMesh = mesh;
            var innerRenderer = inner.AddComponent<MeshRenderer>();
            // Back-face culling on the inward sphere: from outside you see its far inner wall, i.e. look into the world.
            m_InnerMaterial = BubbleWorldData.CreateUnlit("Bubble Choice Panorama", Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(m_Partner)), false, CullMode.Back, Color.white);
            innerRenderer.sharedMaterial = m_InnerMaterial;
            innerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Inner = inner.transform;
            var film = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            film.name = "Film";
            film.transform.SetParent(m_PartnerRoot.transform, false);
            tint.a = m_FilmAlpha;
            m_FilmMaterial = BubbleWorldData.CreateUnlit("Bubble Choice Film", Resources.Load<Texture2D>(BubbleWorldData.FilmResource), true, CullMode.Off, tint);
            m_FilmMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var filmRenderer = film.GetComponent<MeshRenderer>();
            filmRenderer.sharedMaterial = m_FilmMaterial;
            filmRenderer.shadowCastingMode = ShadowCastingMode.Off;
            film.AddComponent<MoodiumSpatialButton>().Configure(null, null, ChoosePartner);
            m_Film = film.transform;
            SetPartnerRadius(.001f);
        }

        void SetPartnerRadius(float radius)
        {
            m_Radius = Mathf.Max(.001f, radius);
            if (m_Film != null) m_Film.localScale = Vector3.one * (m_Radius * 2f);
            if (m_Inner != null) m_Inner.localScale = Vector3.one * (m_Radius * .97f);
        }

        /// <summary>Look + pinch on the other bubble (or the editor mouse): choose its world.</summary>
        public void ChoosePartner() { if (m_Active && !m_Leaving && !m_Loading) PartnerTouched = true; }

        /// <summary>The real bubble was touched: the other one sinks back to where it rose from, tags fade.</summary>
        public void Dismiss()
        {
            if (!m_Active || m_Leaving) return;
            m_Leaving = true; m_LeaveAt = -1f; m_RealDecor = false;
        }

        /// <summary>The other world was chosen: it stays, gently pulsing, while its points load. The real bubble's tags fade.</summary>
        public void BeginLoading() { m_Loading = true; m_RealDecor = false; }

        public void Hide()
        {
            m_Active = m_Leaving = m_Loading = false; PartnerTouched = false;
            if (m_PartnerRoot != null) { BubblePlayVisuals.Release(m_PartnerRoot); m_PartnerRoot = null; m_Film = m_Inner = null; }
            if (m_FilmMaterial != null) { BubblePlayVisuals.Release(m_FilmMaterial); m_FilmMaterial = null; }
            if (m_InnerMaterial != null) { BubblePlayVisuals.Release(m_InnerMaterial); m_InnerMaterial = null; }
            if (m_RuntimeSphere != null) { BubblePlayVisuals.Release(m_RuntimeSphere); m_RuntimeSphere = null; }
            m_RealLabel?.Hide(); m_PartnerLabel?.Hide();
            if (m_Ring != null) m_Ring.gameObject.SetActive(false);
        }

        // Children go with this object; only the assets it made need releasing.
        void OnDestroy()
        {
            if (m_FilmMaterial != null) BubblePlayVisuals.Release(m_FilmMaterial);
            if (m_InnerMaterial != null) BubblePlayVisuals.Release(m_InnerMaterial);
            if (m_RuntimeSphere != null) BubblePlayVisuals.Release(m_RuntimeSphere);
        }

        void Update() => Step(Time.time, Time.deltaTime);

        /// <summary>One frame. Public so EditMode tests can drive it.</summary>
        public void Step(float now, float dt)
        {
            if (!m_Active || m_PartnerRoot == null || m_Viewer == null) return;
            if (m_ShownAt < 0f) m_ShownAt = now;
            if (m_Leaving && m_LeaveAt < 0f) m_LeaveAt = now;
            var eye = m_Viewer.transform.position;

            // Partner bubble: rise like the real one, bob, slow spin; sink when dismissed; pulse while loading.
            var since = now - m_ShownAt;
            Vector3 position;
            float radius;
            if (m_Leaving)
            {
                var k = BubbleWorldController.Ease((now - m_LeaveAt) / SinkSeconds);
                position = Vector3.Lerp(m_Hover, m_From, k);
                radius = Mathf.Lerp(m_TargetRadius, .001f, k);
                if (k >= 1f) { Hide(); return; }
            }
            else if (since < RiseSeconds)
            {
                position = BubbleWorldController.SummonPosition(m_From, m_Hover, since / RiseSeconds);
                radius = m_TargetRadius * BubbleWorldController.Ease(since / RiseSeconds);
            }
            else
            {
                position = m_Hover + Vector3.up * (Mathf.Sin((since - RiseSeconds) / BobPeriod * Mathf.PI * 2f + 1.3f) * BobAmplitude);
                radius = m_TargetRadius * (m_Loading ? 1f + Mathf.Sin(now * Mathf.PI * 2f / 1.2f) * .03f : 1f);
            }
            m_PartnerRoot.transform.position = position;
            SetPartnerRadius(radius);
            if (m_Inner != null) m_Inner.localRotation = Quaternion.Euler(0f, BubbleWorldController.GazeYaw(m_Viewer.transform.forward) + since * SpinDegrees, 0f);

            // Fingertip touch, only once the bubble has risen and the finger has been away from it.
            if (!m_Leaving && !m_Loading && since >= RiseSeconds && m_Touching != null)
            {
                var touching = m_Touching(position, m_Radius);
                if (!m_Armed) m_Armed = !touching;
                else if (touching) PartnerTouched = true;
            }

            // Name tags under each bubble, fading in after the rise.
            var show = !m_Leaving && since >= RiseSeconds * .6f;
            var partnerTop = position - Vector3.up * (m_Radius + BubbleChoiceRules.LabelGap);
            m_PartnerLabel.Place(partnerTop, eye, show ? 1f : 0f, BubbleChoiceRules.TagFade, dt);
            var realShow = show && m_RealDecor && m_RealCentre != null;
            if (m_RealCentre != null)
            {
                var realCentre = m_RealCentre();
                var realRadius = m_RealRadius != null ? m_RealRadius() : m_TargetRadius;
                var realTop = realCentre - Vector3.up * (realRadius + BubbleChoiceRules.LabelGap);
                m_RealLabel.Place(realTop, eye, realShow ? 1f : 0f, BubbleChoiceRules.TagFade, dt);
                PlaceRing(m_Pick == m_Real ? realCentre : position, m_Pick == m_Real ? realRadius : m_Radius, eye, now,
                    m_Pick != null && (m_Pick == m_Real ? realShow : show && !m_Loading));
            }
        }

        void PlaceRing(Vector3 centre, float radius, Vector3 eye, float now, bool on)
        {
            if (m_Ring == null || m_Ring.sprite == null) return;
            if (m_Ring.gameObject.activeSelf != on) m_Ring.gameObject.SetActive(on);
            if (!on) return;
            var t = m_Ring.transform;
            // The ring sprite is 15 cm with the ring at 13.5 cm: scale it to sit a little outside the film.
            var size = radius * 2f * 1.18f / (m_Ring.sprite.bounds.size.x * .9f);
            t.localScale = Vector3.one * size;
            var look = centre - eye;
            // A hair behind the bubble's centre plane so the film draws over its inner edge.
            t.SetPositionAndRotation(centre + look.normalized * .01f, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity);
            var breath = .5f - .5f * Mathf.Cos(now * Mathf.PI * 2f / 2.4f);
            m_Ring.color = new Color(1f, 1f, 1f, Mathf.Lerp(.55f, .95f, breath));
        }
    }
}
