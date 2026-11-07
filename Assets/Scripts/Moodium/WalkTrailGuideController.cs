using System.Collections.Generic;
using Moodium.CandyWorld;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>Optional post-reward walking journey. Only reconstructed, level ground can become a target.</summary>
    public sealed class WalkTrailGuideController : MonoBehaviour
    {
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { if (m_Root != null) foreach (Transform child in m_Root) if (child != m_Guide && !child.name.StartsWith("Walk Trail Voice")) visibility.Register(child.gameObject); }

        enum Phase { Waiting, Inviting, Walking, Sleeping, Complete }
        sealed class FloatingCard
        {
            public SpriteRenderer Renderer;
            public Vector3 Position;
            public float Born;
            public bool Collect;
        }
        readonly List<Vector3> m_Gardens = new();
        readonly List<SpriteRenderer> m_Dots = new();
        readonly List<FloatingCard> m_Cards = new();
        readonly AudioClip[] m_Notes = new AudioClip[8];
        readonly List<AudioSource> m_Voices = new();
        readonly List<GameObject> m_Decorations = new();
        readonly List<SpriteRenderer> m_GardenRings = new();
        NatureFootstepGrassController m_Trail;
        CandyRainRewardController m_Rain;
        Transform m_Viewer;
        Transform m_Wrist;
        WorldActionDock m_Dock;
        Transform m_Root;
        Transform m_Guide;
        Moodium.Characters.MoodiCompanionAnimator m_GuideMoodi;
        Moodium.Characters.MoodiVoice m_GuideVoice;
        Moodium.Characters.MoodiHandCompanion m_HandCompanion;
        // Companion polish: look back while leading, wait and wave when the viewer falls behind,
        // come closer when the viewer stands still.
        float m_NextLookBack, m_LookBackUntil, m_WaitSince = -1f, m_NextWaitWave, m_StillSince = -1f;
        int m_LookBacks;
        bool m_ToldGrowth, m_SaidStill;
        Vector3 m_StillAnchor;
        bool HandHasGuide => m_HandCompanion != null && m_HandCompanion.HasControl;
        // Set when the squeeze rescue handed Moodi over: it already thanked the user (K10).
        bool m_RescueShown;
        SpriteRenderer m_Glow;
        SpriteRenderer m_Ring;
        SpriteRenderer m_Invite;
        AudioClip m_Chime, m_Complete, m_Hop;
        Color m_Tint;
        Phase m_Phase;
        bool m_Candy, m_English, m_HasTarget, m_HasOrigin, m_HasCompletedEnergy, m_RainSeen;
        float m_FullAt, m_InvitedAt, m_LastStep = -100f, m_NextSearch, m_NextSafety, m_LastHop, m_NextDots;
        Material m_CandyPink, m_CandyCream;
        Texture2D m_GrowthGlowTexture;
        Sprite m_GrowthGlowSprite;
        int m_Note, m_Voice;
        Vector3 m_Start, m_Target, m_LastHead, m_InviteStart;
        bool m_HasGuidePosition;
        Vector3 m_RouteStart;
        float m_GuideDistance;
        bool m_TargetIsGarden;
        string m_LastWaitReason;
        string World => m_Candy ? "candy" : "nature";
        string Language => m_English ? "en" : "cn";
        public event System.Action WalkInvited;
        public event System.Action<Vector3> GardenDiscovered;
        public IEnumerable<Vector3> OccupiedGardenPositions => m_Gardens;
        public event System.Action<Vector3> StepRecorded;
        public bool HandHasMoodi => HandHasGuide;
        public void Say(string id) => m_GuideVoice?.Say(id);
        /// <summary>Say a line if Moodi is out and can speak; false if not (the caller can try again later).</summary>
        public bool TrySay(string id) => m_GuideVoice != null && m_GuideVoice.isActiveAndEnabled && m_GuideVoice.Say(id);
        Vector3 m_RingPoint;
        float m_RingPresentationUntil;
        bool m_RingOrbit;
        public void PresentRing(string id, Vector3 point)
        {
            Say(id);
            if (id != "F1" && id != "F1t") return;
            m_RingPoint = point; m_RingOrbit = id == "F1"; m_RingPresentationUntil = Time.time + 3f;
        }
        bool m_Suspended, m_Landed, m_InvitationNotified;
        float m_PausedAt;
        float m_ReleasedAt = -1f;
        public void SetSuspended(bool value)
        {
            if (m_Suspended == value) return;
            if (value) m_PausedAt = Time.time;
            else { var pause = Time.time - m_PausedAt; m_FullAt += pause; m_InvitedAt += pause; if (m_ReleasedAt >= 0f) m_ReleasedAt += pause; m_NextSearch += pause; m_NextSafety += pause; }
            m_Suspended = value;
            if (m_HandCompanion != null) m_HandCompanion.enabled = !value;
        }
        public int DiscoveredGardenCount => m_Gardens.Count;
        public static bool ShouldOfferWalk(bool rainSeen, bool rainPlaying, float secondsSinceFull) =>
            ShouldOfferWalk(false, false, -1f, secondsSinceFull);
        public static bool ShouldOfferWalk(bool handHasGuide, bool landedThenFree, float secondsSinceFree, float secondsSinceFull) =>
            !handHasGuide && ((landedThenFree && secondsSinceFree >= 2f) || secondsSinceFull >= 20f);
        public static int NextNoteIndex(int current) => current < 7 ? current + 1 : 3;
        public static float AdvanceGuideDistance(float current, float length, float distanceFromViewer, float deltaTime) =>
            distanceFromViewer > 1.2f ? current : Mathf.MoveTowards(current, length, Mathf.Max(0f, deltaTime) * .45f);
        public static Vector3 CompanionFollowPoint(Vector3 viewer, Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            return viewer + forward * .65f + Vector3.Cross(Vector3.up, forward) * .22f - Vector3.up * .45f;
        }
        public static Vector3 SmoothGuidePosition(Vector3 current, Vector3 desired, float deltaTime) =>
            Vector3.Lerp(current, desired, 1f - Mathf.Exp(-6f * Mathf.Max(0f, deltaTime)));
        public static Vector3 GuideHoverPoint(Vector3 groundPoint, Vector3 viewer, float bob)
        {
            // Ground navigation owns X/Z only. Keep the companion at the same
            // body-relative height whether leading, arriving, or following.
            groundPoint.y=CompanionFollowPoint(viewer,Vector3.forward).y+Mathf.Clamp(bob,-.015f,.015f);
            return groundPoint;
        }
        /// <summary>Where Moodi floats when the viewer has stood still for a while: offset to the right, looking up at them.</summary>
        public static Vector3 ApproachPoint(Vector3 viewer, Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            return viewer + forward * .5f + Vector3.Cross(Vector3.up, forward) * .22f - Vector3.up * .28f;
        }
        /// <summary>True when the guide is outside a cone around where the viewer looks.</summary>
        public static bool IsOutOfView(Vector3 viewerForward, Vector3 toGuide, float maxAngle = 70f)
        {
            viewerForward = Vector3.ProjectOnPlane(viewerForward, Vector3.up);
            toGuide = Vector3.ProjectOnPlane(toGuide, Vector3.up);
            if (viewerForward.sqrMagnitude < 1e-4f || toGuide.sqrMagnitude < 1e-4f) return false;
            return Vector3.Angle(viewerForward, toGuide) > maxAngle;
        }

        public void BeginSession(NatureFootstepGrassController trail, CandyRainRewardController rain,
            Transform viewer, Transform wrist, bool candy, GameObject[] decorations, GameObject guidePrefab = null)
        {
            StopAndClear();
            m_Trail = trail; m_Rain = rain; m_Viewer = viewer; m_Wrist = wrist; m_Candy = candy;
            m_Dock = wrist != null ? wrist.GetComponent<WorldActionDock>() : null;
            m_English = PlayerPrefs.GetString("Moodium.Language", "Chinese") == "English";
            m_Tint = candy ? new Color(1f, .55f, .78f) : new Color(.44f, .83f, .60f);
            m_Phase = Phase.Waiting;
            m_HasCompletedEnergy = m_HasTarget = m_HasOrigin = m_RainSeen = false;
            m_NextSearch = m_NextSafety = 0f; m_Note = 0; m_LastStep = -100f;
            m_LastWaitReason = null;
            m_HasGuidePosition = false;
            m_NextLookBack = m_LookBackUntil = m_NextWaitWave = 0f;
            m_WaitSince = m_StillSince = -1f;
            m_LookBacks = 0;
            m_ToldGrowth = m_SaidStill = false;
            m_InvitationNotified = false; m_RescueShown = false; m_Suspended = m_Landed = false; m_ReleasedAt = -1f;
            Moodium.Characters.MoodiVoice.ResetSession();
            m_Trail.SetGuidedMode(true);
            m_Trail.StepGrown += OnStep;
            if (decorations != null)
                foreach (var prefab in decorations)
                    if (prefab != null && (prefab.name.ToLowerInvariant().Contains("star") ||
                        prefab.name.ToLowerInvariant().Contains("lolli"))) m_Decorations.Add(prefab);
            m_Root = new GameObject("Walk Trail Journey").transform;
            for (var i = 0; i < 5; i++)
            {
                var sound = new GameObject("Walk Trail Voice " + i);
                sound.transform.SetParent(m_Root, false);
                var source = sound.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 1f;
                source.minDistance = .3f; source.maxDistance = 5f;
                m_Voices.Add(source);
            }
            for (var i = 0; i < 8; i++) m_Notes[i] = LoadAudio("walk_step_" + (i + 1).ToString("00"));
            m_Chime = LoadAudio("walk_spot_chime"); m_Complete = LoadAudio("walk_complete"); m_Hop = LoadAudio("walk_guide_hop");
            m_Ring = Sprite("walk_floor_ring", .6f, true);
            m_Invite = Sprite($"walk_floor_invite_{World}_{Language}", .6f, true, false);
            m_Glow = Sprite("walk_guide_glow", .14f, false);
            // Keep the legacy node for existing layout code, but never render a halo.
            m_Glow.enabled = false;
            m_Guide = new GameObject("Walking Light Guide").transform;
            m_Guide.SetParent(m_Root, false);
            if (guidePrefab != null)
            {
                var visual=Instantiate(guidePrefab,m_Guide);
                // MoodiBreathingFloat captured the prefab scale on Instantiate and writes it back every frame, which
                // fought PrepareGuide's 20 cm size and the hand companion's landing squash (Moodi popped smaller).
                // The companion already bobs Moodi on the palm, so the guide does without it. Off before scaling:
                // its OnDisable restores the captured scale.
                if (visual.TryGetComponent<Moodium.Flow.MoodiBreathingFloat>(out var breathing)) breathing.enabled = false;
                PrepareGuide(visual);
                m_GuideMoodi=visual.GetComponent<Moodium.Characters.MoodiCompanionAnimator>();
                if(m_GuideMoodi!=null)m_GuideMoodi.SetFacingViewer(m_Viewer);
                if (!visual.TryGetComponent(out m_GuideVoice))
                    m_GuideVoice = visual.AddComponent<Moodium.Characters.MoodiVoice>();
                m_HandCompanion = m_Guide.gameObject.AddComponent<Moodium.Characters.MoodiHandCompanion>();
                m_HandCompanion.Initialize(m_Guide, visual.transform, m_GuideMoodi, m_GuideVoice, viewer, m_Tint);
                m_HandCompanion.Landed += () => m_Landed = true;
            }
            m_Ring.gameObject.SetActive(false); m_Invite.gameObject.SetActive(false);
            m_Guide.gameObject.SetActive(false); m_Glow.gameObject.SetActive(false);
            for (var i = 0; i < 12; i++)
            {
                var dot = Sprite("walk_trail_dot", .08f, true, false);
                dot.gameObject.SetActive(false); m_Dots.Add(dot);
            }
            if (viewer != null) m_LastHead = viewer.position;
            enabled = true;
        }

        /// <summary>
        /// The squeeze rescue's Moodi jumped out and stopped at <paramref name="position"/>. Show the walk guide's
        /// Moodi right there (same prefab and size), let it follow the user immediately, and start the palm
        /// teaching (A2) after <paramref name="teachDelay"/> seconds so it follows the thank-you line.
        /// </summary>
        public void AppearFromRescue(Vector3 position, float teachDelay = 2.8f)
        {
            if (m_Guide == null) return;
            m_RescueShown = true;
            m_Guide.position = position;
            m_HasGuidePosition = true;
            m_Guide.gameObject.SetActive(true);
            if (m_Glow != null) m_Glow.gameObject.SetActive(true);
            if (m_GuideVoice != null) m_GuideVoice.SayAfter("A2", teachDelay);
            if (m_HandCompanion != null) m_HandCompanion.NotifyInvited();
        }

        public void SetProgress(float progress)
        {
            if (progress < .999f || m_HasCompletedEnergy) return;
            m_HasCompletedEnergy = true;
            m_FullAt = Time.time;
        }

        void Update()
        {
            if (m_Suspended) { if (m_Viewer != null && m_Guide != null) MoveGuideSmoothly(CompanionFollowPoint(m_Viewer.position, m_Viewer.forward)); return; }
            if (m_Landed && !HandHasGuide && m_ReleasedAt < 0f) m_ReleasedAt = Time.time;
            if (m_Root == null || m_Trail == null || m_Viewer == null) return;
            if (Time.time < m_RingPresentationUntil && !HandHasGuide && m_Guide != null)
            {
                var angle = (3f - (m_RingPresentationUntil - Time.time)) / 3f * Mathf.PI * 2f;
                var offset = m_RingOrbit ? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * .22f : Vector3.zero;
                m_Guide.gameObject.SetActive(true); MoveGuideSmoothly(m_RingPoint + offset + Vector3.up * .35f); return;
            }
            UpdateCards();
            foreach (var ring in m_GardenRings)
                if (ring != null) ring.color = new Color(m_Tint.r, m_Tint.g, m_Tint.b,
                    .45f + Mathf.Sin(Time.time * Mathf.PI * .5f) * .1f);
            if (!m_HasCompletedEnergy) return;
            if (m_Rain != null && m_Rain.IsPlaying) m_RainSeen = true;
            if (m_Phase == Phase.Waiting)
            {
                if (!ShouldOfferWalk(HandHasGuide, m_ReleasedAt >= 0f, Time.time - m_ReleasedAt, Time.time - m_FullAt))
                {
                    // A rescued Moodi is already out: let it come over to the user instead of waiting hidden.
                    if (m_RescueShown) UpdateCompanionFollow();
                    return;
                }
                if (!m_InvitationNotified) { m_InvitationNotified = true; WalkInvited?.Invoke(); }
                // Basic planting must not depend on finding a large, completely
                // reconstructed garden target. Each plant still validates its floor.
                m_Trail.SetWalkingEnabled(true);
                UpdateCompanionFollow();
                if (Time.time < m_NextSearch) return;
                m_NextSearch = Time.time + 2f;
                if (!m_Trail.TryGetFloor(out var floor))
                {
                    ReportGuideWait("No reconstructed floor below the viewer; look down and scan the floor.");
                    return;
                }
                if (!m_HasOrigin) { m_Start = floor; m_HasOrigin = true; }
                if (!FindTarget(true))
                {
                    ReportGuideWait("No scanned, unobstructed forward route yet; waiting for floor reconstruction.");
                    return;
                }
                Invite();
            }
            if (!m_HasOrigin) return;
            var movement = Vector3.ProjectOnPlane(m_Viewer.position - m_LastHead, Vector3.up).magnitude;
            if (m_Phase == Phase.Sleeping && movement >= .3f)
            {
                m_LastHead = m_Viewer.position;
                m_Phase = Phase.Walking; m_Trail.SetWalkingEnabled(true);
                m_NextSearch = 0f;
            }
            if (m_Phase == Phase.Inviting && movement >= .3f)
            {
                m_Phase = Phase.Walking;
                m_Invite.gameObject.SetActive(false);
                m_LastHead = m_Viewer.position;
                if (!m_ToldGrowth && m_GuideVoice != null) { m_ToldGrowth = true; m_GuideVoice.Say("D3"); }
            }
            if (m_Phase == Phase.Inviting && Time.time - m_InvitedAt >= 60f)
            {
                m_Phase = Phase.Sleeping;
                m_Trail.SetWalkingEnabled(false);
                m_Invite.gameObject.SetActive(false); m_Ring.gameObject.SetActive(false);
                HideDots(); m_LastHead = m_Viewer.position;
            }
            if (m_Phase == Phase.Sleeping || m_Phase == Phase.Complete)
            {
                UpdateCompanionFollow();
                return;
            }
            if ((m_Phase == Phase.Walking || m_Phase == Phase.Inviting) && !m_HasTarget && Time.time >= m_NextSearch)
            {
                m_NextSearch = Time.time + 2f;
                FindTarget(false);
            }
            if (m_HasTarget && Time.time >= m_NextSafety)
            {
                m_NextSafety = Time.time + 1f;
                if (!m_Trail.TryFindSafeGround(m_Target, m_Start.y, out _, m_TargetIsGarden) ||
                    !m_Trail.IsSafePath(m_Viewer.position, m_Target, m_Start.y) ||
                    (HorizontalDistance(m_Viewer.position, m_Target) > .65f &&
                     Vector3.Dot(Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized,
                         Vector3.ProjectOnPlane(m_Target - m_Viewer.position, Vector3.up).normalized) < .5f))
                {
                    m_HasTarget = false; m_Ring.gameObject.SetActive(false); HideDots();
                    if (!HandHasGuide) { m_Guide.gameObject.SetActive(false); m_Glow.gameObject.SetActive(false); }
                }
            }
            if (m_HasTarget && HorizontalDistance(m_Viewer.position, m_Target) <= .4f)
            {
                if (m_Phase == Phase.Walking && m_TargetIsGarden) DiscoverGarden();
                else if (!m_TargetIsGarden)
                {
                    m_HasTarget = false; m_Ring.gameObject.SetActive(false); HideDots();
                    m_NextSearch = Time.time + .2f;
                }
            }
            UpdateGuide();
        }

        bool FindTarget(bool invite)
        {
            if (!m_Trail.TryGetFloor(out var floor)) return false;
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            var angles = new[] { 0f, -30f, 30f, -60f, 60f };
            foreach (var distance in invite ? new[] { 1.3f } : new[] { 1.5f, 1.8f, 1.3f })
                foreach (var angle in angles)
                {
                    var desired = floor + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;
                    if (HorizontalDistance(desired, m_Start) > 2.5f) continue;
                    if (!NatureFootstepGrassController.HasMinimumHorizontalSeparation(desired, m_Gardens, 1.2f)) continue;
                    if (!m_Trail.TryFindSafeGround(desired, m_Start.y, out var candidate, true) ||
                        !m_Trail.IsSafePath(floor, candidate, m_Start.y)) continue;
                    SetRouteTarget(floor, candidate, true, forward);
                    return true;
                }
            // A large garden needs the original .8 m clearance disk. A short
            // guide waypoint only needs a scanned, body-width unobstructed route.
            // Never block all guidance merely because no large garden fits yet.
            foreach (var distance in new[] { 1.1f, .9f, .7f })
                foreach (var angle in angles)
                {
                    var desired = floor + Quaternion.AngleAxis(angle, Vector3.up) * forward * distance;
                    if (!m_Trail.TryFindSafeGround(desired, floor.y, out var candidate) ||
                        !m_Trail.IsSafePath(floor, candidate, floor.y)) continue;
                    SetRouteTarget(floor, candidate, false, forward);
                    return true;
                }
            return false;
        }

        void SetRouteTarget(Vector3 floor, Vector3 candidate, bool garden, Vector3 forward)
        {
            m_Target = candidate; m_HasTarget = true; m_TargetIsGarden = garden;
            m_RouteStart = floor;
            m_GuideDistance = Mathf.Min(.3f, HorizontalDistance(floor, candidate));
            m_NextDots = 0f;
            m_LastWaitReason = null;
            m_Ring.transform.position = candidate + Vector3.up * .018f;
            m_Ring.gameObject.SetActive(true);
            if (m_Invite != null && m_Invite.gameObject.activeSelf)
                m_Invite.transform.position = candidate + forward * .4f + Vector3.up * .012f;
            Debug.Log($"[Walk Trail] Route ready: {(garden ? "garden" : "short waypoint")}, length={HorizontalDistance(floor, candidate):0.00}m.");
        }

        void ReportGuideWait(string reason)
        {
            if (m_LastWaitReason == reason) return;
            m_LastWaitReason = reason;
            Debug.LogWarning("[Walk Trail] " + reason);
        }

        void Invite()
        {
            m_Phase = Phase.Inviting; m_InvitedAt = Time.time;
            m_LastHead = m_Viewer.position;
            m_InviteStart = m_HasGuidePosition ? m_Guide.position : CompanionFollowPoint(m_Viewer.position, m_Viewer.forward);
            m_Guide.position = m_InviteStart;
            m_HasGuidePosition = true;
            m_Guide.gameObject.SetActive(true); m_Glow.gameObject.SetActive(true);
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized;
            m_Invite.transform.position = m_Target + forward * .4f + Vector3.up * .012f;
            m_Invite.transform.rotation = Quaternion.LookRotation(Vector3.down, forward);
            m_Invite.gameObject.SetActive(true);
            m_Trail.SetWalkingEnabled(true);
            Play(m_Hop, m_Target, .25f);
            StartCoroutine(DemonstrateGrowth(m_Target));
            if(m_GuideMoodi!=null)m_GuideMoodi.PlayWave();
            // After the squeeze rescue Moodi has already thanked the user and started the palm teaching.
            if (m_GuideVoice != null && !m_RescueShown) { m_GuideVoice.Say("A1"); m_GuideVoice.Say("A2"); }
            if (m_HandCompanion != null) m_HandCompanion.NotifyInvited();
        }

        System.Collections.IEnumerator DemonstrateGrowth(Vector3 target)
        {
            yield return new WaitForSeconds(.8f);
            for (var i = 0; i < 3; i++)
            {
                var p = target + new Vector3(Mathf.Cos(i * 2.1f), 0f, Mathf.Sin(i * 2.1f)) * .16f;
                if (m_Suspended) yield break;
                if (m_Trail.TryFindSafeGround(p, m_Start.y, out var ground))
                { m_Trail.SpawnGrass(ground); Sparkle(ground); }
            }
        }

        void OnStep(Vector3 position)
        {
            if (m_Suspended) return;
            StepRecorded?.Invoke(position);
            if (Time.time - m_LastStep > 1.2f) m_Note = 0;
            Play(m_Notes[m_Note], position, .35f);
            m_Note = NextNoteIndex(m_Note);
            m_LastStep = Time.time;
            Sparkle(position);
        }

        void DiscoverGarden()
        {
            if (m_Suspended) return;
            GardenDiscovered?.Invoke(m_Viewer.position);
            m_Gardens.Add(m_Target);
            var garden = new GameObject("Secret Garden " + m_Gardens.Count).transform;
            garden.SetParent(m_Root, false);
            garden.position = m_Target;
            for (var i = 0; i < 9; i++)
            {
                var angle = i * Mathf.PI * 2f / 9f;
                var p = m_Target + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (.12f + (i % 3) * .065f);
                if (!m_Trail.TryFindSafeGround(p, m_Start.y, out var ground)) continue;
                var prefab = m_Candy && i % 3 == 0 && m_Decorations.Count > 0
                    ? m_Decorations[(i / 3) % m_Decorations.Count] : m_Trail.FlowerPrefab;
                var plant = m_Trail.SpawnGrass(ground, true, prefab);
                if (plant != null) plant.transform.SetParent(garden, true);
                Sparkle(ground);
            }
            var ring = Sprite("walk_floor_ring", .85f, true);
            ring.transform.position = m_Target + Vector3.up * .009f;
            m_GardenRings.Add(ring);
            if (m_Candy)
            {
                CreateLollipop(m_Target + new Vector3(-.15f, 0f, .08f), garden, 0);
                CreateLollipop(m_Target + new Vector3(.12f, 0f, -.06f), garden, 1);
            }
            Play(m_Chime, m_Target, .4f);
            if(m_GuideMoodi!=null)m_GuideMoodi.PlayHappy();
            if (m_GuideVoice != null)
            {
                if (m_Gardens.Count >= 3) m_GuideVoice.Say("D7");
                else m_GuideVoice.SayGardenFound(m_Candy);
            }
            ShowCard($"walk_counter_{World}_{m_Gardens.Count}_{Language}", m_Target + Vector3.up * .7f, .35f, true);
            m_HasTarget = false; m_Ring.gameObject.SetActive(false); HideDots();
            if (m_Gardens.Count >= 3)
            {
                m_Phase = Phase.Complete;
                m_Trail.WaveTrail();
                ShowCard($"walk_toast_done_{World}_{Language}", m_Viewer.position + m_Viewer.forward, .5f, false);
                Play(m_Complete, m_Viewer.position + m_Viewer.forward, .4f);
            }
            else m_NextSearch = Time.time + .25f;
        }

        void UpdateGuide()
        {
            if (!m_HasTarget)
            {
                HideDots();
                UpdateCompanionFollow();
                if (m_Phase != Phase.Complete)
                    ReportGuideWait("Forward route unavailable; companion follows the viewer while waiting for a scanned route.");
                return;
            }
            m_Guide.gameObject.SetActive(true); m_Glow.gameObject.SetActive(true);
            m_Glow.color = m_Tint;
            var viewerFloor = new Vector3(m_Viewer.position.x, m_Start.y, m_Viewer.position.z);
            var routeLength = HorizontalDistance(m_RouteStart, m_Target);
            var routeDirection = Vector3.ProjectOnPlane(m_Target - m_RouteStart, Vector3.up).normalized;
            var invite = Mathf.Clamp01((Time.time - m_InvitedAt) / .8f);
            var holding = UpdateLookBack() | UpdateWaitForViewer();
            if ((m_Phase != Phase.Inviting || invite >= 1f) && !holding)
                m_GuideDistance = AdvanceGuideDistance(m_GuideDistance, routeLength,
                    HorizontalDistance(m_Viewer.position, m_RouteStart + routeDirection * m_GuideDistance), Time.deltaTime);
            var guidePoint = m_RouteStart + routeDirection * m_GuideDistance;
            if (!m_Trail.TryFindSafeGround(guidePoint, m_Start.y, out guidePoint))
            {
                m_HasTarget = false;
                m_Ring.gameObject.SetActive(false);
                HideDots();
                UpdateCompanionFollow();
                ReportGuideWait("Ground under the next guide hop is no longer tracked; replanning.");
                return;
            }
            var hop = Mathf.Abs(Mathf.Sin(Time.time * 2.3f));
            var desired = GuideHoverPoint(guidePoint,m_Viewer.position,Mathf.Sin(Time.time*2f)*.015f);
            var destination = m_Phase == Phase.Inviting && invite < 1f
                ? Vector3.Lerp(m_InviteStart, desired, invite) + Vector3.up * Mathf.Sin(invite * Mathf.PI) * .2f
                : desired;
            MoveGuideSmoothly(destination);
            if (hop < .08f && Time.time - m_LastHop > 1f)
            { m_LastHop = Time.time; Play(m_Hop, m_Guide.position, .08f); }
            Face(m_Glow.transform, m_Guide.position);
            var start = Vector3.MoveTowards(viewerFloor, m_Target, .2f);
            var count = Mathf.Clamp(Mathf.CeilToInt(HorizontalDistance(start, m_Target) / .22f), 1, 12);
            if (Time.time >= m_NextDots)
            {
                m_NextDots = Time.time + .2f;
                for (var i = 0; i < m_Dots.Count; i++)
                {
                    var active = i < count;
                    var p = active ? Vector3.Lerp(start, m_Target, (i + 1f) / (count + 1f)) : Vector3.zero;
                    if (active) active = m_Trail.TryFindSafeGround(p, m_Start.y, out p);
                    m_Dots[i].gameObject.SetActive(active);
                    if (!active) continue;
                    m_Dots[i].transform.position = p + Vector3.up * .018f;
                    var pulse = .75f + .25f * Mathf.Sin(Time.time * 4f - i * .7f);
                    m_Dots[i].color = new Color(1f, 1f, 1f, pulse);
                }
            }
            if (m_Phase == Phase.Inviting)
            {
                var c = Color.white; c.a = 1f - Mathf.Clamp01((Time.time - m_InvitedAt - 58f) / 2f);
                m_Invite.color = c;
            }
        }

        void UpdateCompanionFollow()
        {
            m_Guide.gameObject.SetActive(true); m_Glow.gameObject.SetActive(true);
            var companion = IsViewerResting()
                ? ApproachPoint(m_Viewer.position, m_Viewer.forward)
                : CompanionFollowPoint(m_Viewer.position, m_Viewer.forward);
            companion.y += Mathf.Sin(Time.time * 2f) * .015f;
            MoveGuideSmoothly(companion);
            Face(m_Glow.transform, m_Guide.position);
            m_Glow.color = new Color(m_Tint.r, m_Tint.g, m_Tint.b, .3f + Mathf.Sin(Time.time) * .05f);
        }

        void MoveGuideSmoothly(Vector3 destination)
        {
            if (HandHasGuide) return; // Moodi is flying to, riding on or hopping off a palm
            m_Guide.position = m_HasGuidePosition
                ? SmoothGuidePosition(m_Guide.position, destination, Time.deltaTime) : destination;
            m_HasGuidePosition = true;
        }

        /// <summary>Every 4–6 s while leading, Moodi stops for a second and turns back to look at the viewer
        /// (MoodiCompanionAnimator faces the viewer whenever it stops). Every third time it says D4.</summary>
        bool UpdateLookBack()
        {
            if (Time.time < m_LookBackUntil) return true;
            if (m_NextLookBack <= 0f) m_NextLookBack = Time.time + Random.Range(4f, 6f);
            if (Time.time < m_NextLookBack || HandHasGuide) return false;
            m_LookBackUntil = Time.time + 1f;
            m_NextLookBack = m_LookBackUntil + Random.Range(4f, 6f);
            if (++m_LookBacks % 3 == 0 && m_GuideVoice != null) m_GuideVoice.SayOneOf("D4");
            return true;
        }

        /// <summary>When the viewer falls 1.2 m behind or looks away, Moodi holds still, waves and says D5.</summary>
        bool UpdateWaitForViewer()
        {
            var far = HorizontalDistance(m_Viewer.position, m_Guide.position) > 1.2f;
            var away = IsOutOfView(m_Viewer.forward, m_Guide.position - m_Viewer.position);
            if (!far && !away) { m_WaitSince = -1f; return false; }
            if (m_WaitSince < 0f) m_WaitSince = Time.time;
            var waited = Time.time - m_WaitSince;
            if (waited > (far ? 1.5f : 2f) && Time.time >= m_NextWaitWave && !HandHasGuide)
            {
                m_NextWaitWave = Time.time + 8f;
                if (m_GuideMoodi != null) m_GuideMoodi.PlayWave();
                if (m_GuideVoice != null) m_GuideVoice.Say("D5");
            }
            return away && waited > .5f;
        }

        /// <summary>True after the viewer has stood still for 8 s (after the walk was offered). Says E1 once per rest.</summary>
        bool IsViewerResting()
        {
            if (m_Phase == Phase.Waiting) return false;
            var head = m_Viewer.position;
            if (m_StillSince < 0f || HorizontalDistance(head, m_StillAnchor) > .3f)
            {
                m_StillAnchor = head; m_StillSince = Time.time; m_SaidStill = false;
                return false;
            }
            if (Time.time - m_StillSince < 8f) return false;
            if (!m_SaidStill && m_GuideVoice != null && m_Phase != Phase.Complete) { m_SaidStill = true; m_GuideVoice.Say("E1"); }
            return true;
        }

        void Sparkle(Vector3 position)
        {
            m_GrowthGlowSprite = Resources.Load<Sprite>("MoodiumWalkTrail/walk_growth_glow");
            if (m_GrowthGlowSprite == null)
            {
                const int size = 128;
                m_GrowthGlowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Walk Growth White Soft Glow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var pixels = new Color[size * size];
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var radius = new Vector2((x + .5f) / size * 2f - 1f, (y + .5f) / size * 2f - 1f).magnitude;
                        var alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, 1f, radius));
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha * .95f);
                    }
                m_GrowthGlowTexture.SetPixels(pixels);
                m_GrowthGlowTexture.Apply(false, true);
                m_GrowthGlowSprite = UnityEngine.Sprite.Create(m_GrowthGlowTexture,
                    new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
            }
            var root = new GameObject("New Growth White Highlight");
            root.transform.SetParent(m_Root, false);
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            root.transform.localScale = Vector3.one * .44f;
            var burst = root.AddComponent<SpriteRenderer>();
            burst.sprite = m_GrowthGlowSprite;
            burst.color = Color.white;
            burst.transform.position = position + Vector3.up * .025f;
            burst.sortingOrder = 1;
            StartCoroutine(FadeBurst(burst));
        }
        System.Collections.IEnumerator FadeBurst(SpriteRenderer burst)
        {
            var scale = burst.transform.localScale;
            const float duration = 1.2f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                if (burst == null) yield break;
                burst.transform.localScale = scale * Mathf.Lerp(.8f, 1.15f, Mathf.SmoothStep(0f, 1f, t / duration));
                var alpha = Mathf.SmoothStep(0f, 1f, t / .08f) *
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, duration, t)));
                burst.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }
            if (burst != null) Destroy(burst.gameObject);
        }

        void ShowCard(string resource, Vector3 position, float width, bool collect)
        {
            var sprite = Sprite(resource, width, false, false);
            Face(sprite.transform, position);
            m_Cards.Add(new FloatingCard { Renderer = sprite, Position = position, Born = Time.time, Collect = collect });
        }
        void UpdateCards()
        {
            for (var i = m_Cards.Count - 1; i >= 0; i--)
            {
                var card = m_Cards[i]; var age = Time.time - card.Born;
                var duration = card.Collect ? 1.6f : 2.5f;
                if (card.Renderer == null || age > duration)
                { if (card.Renderer != null) Destroy(card.Renderer.gameObject); m_Cards.RemoveAt(i); continue; }
                var position = card.Position;
                // Collected cards fly into the dock's Shower button (formerly: into the wrist band).
                if (card.Collect && age > 1f && m_Dock != null && m_Dock.isActiveAndEnabled && m_Dock.Visible)
                    position = Vector3.Lerp(position, m_Dock.ReleaseAnchor.position, Mathf.SmoothStep(0f, 1f, (age - 1f) / .6f));
                Face(card.Renderer.transform, position);
                card.Renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01((duration - age) / .5f));
                if (card.Collect && age > 1f) card.Renderer.transform.localScale *= Mathf.Exp(-Time.deltaTime * 3f);
            }
        }

        SpriteRenderer Sprite(string resource, float width, bool floor, bool tint = true)
        {
            var go = new GameObject(resource); go.transform.SetParent(m_Root, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Resources.Load<Sprite>("MoodiumWalkTrail/" + resource);
            if (renderer.sprite != null) go.transform.localScale = Vector3.one * width / renderer.sprite.bounds.size.x;
            renderer.color = tint ? m_Tint : Color.white;
            if (floor) { go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); renderer.sortingOrder = 1; }
            return renderer;
        }
        void Face(Transform visual, Vector3 position)
        { visual.position = position; visual.rotation = Quaternion.LookRotation(position - m_Viewer.position, Vector3.up); }
        void HideDots() { foreach (var dot in m_Dots) if (dot != null) dot.gameObject.SetActive(false); }
        void Play(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null || m_Voices.Count == 0) return;
            var source = m_Voices[m_Voice++ % m_Voices.Count];
            source.transform.position = position; source.PlayOneShot(clip, volume);
        }
        static AudioClip LoadAudio(string name) => Resources.Load<AudioClip>("MoodiumWalkTrail/" + name);
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
        static void PrepareGuide(GameObject visual)
        {
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            var bounds = new Bounds(visual.transform.position, Vector3.zero);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (size > .0001f) visual.transform.localScale *= .20f / size;
        }

        void CreateLollipop(Vector3 position, Transform garden, int index)
        {
            if (m_CandyPink == null)
            {
                m_CandyPink = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m_CandyPink.SetColor("_BaseColor", m_Tint);
                m_CandyPink.SetFloat("_Smoothness", .85f);
                m_CandyCream = new Material(m_CandyPink);
                m_CandyCream.SetColor("_BaseColor", new Color(1f, .94f, .8f));
            }
            var root = new GameObject("Secret Garden Lollipop");
            root.transform.SetParent(garden, false); root.transform.position = position;
            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.transform.SetParent(root.transform, false);
            stick.transform.localPosition = Vector3.up * .15f;
            stick.transform.localScale = new Vector3(.009f, .15f, .009f);
            stick.GetComponent<Renderer>().sharedMaterial = m_CandyCream;
            Destroy(stick.GetComponent<Collider>());
            var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = Vector3.up * .31f;
            top.transform.localScale = new Vector3(.12f, .12f, .045f);
            top.GetComponent<Renderer>().sharedMaterial = index == 0 ? m_CandyPink : m_CandyCream;
            Destroy(top.GetComponent<Collider>());
            root.AddComponent<WalkTrailGrowth>().Initialize(position);
        }

        public void StopAndClear()
        {
            if (m_Trail != null)
            {
                m_Trail.StepGrown -= OnStep;
                m_Trail.SetWalkingEnabled(false);
            }
            StopAllCoroutines();
            if (m_Root != null) Destroy(m_Root.gameObject);
            m_Root = null;
            m_HandCompanion = null; m_GuideVoice = null;
            m_Gardens.Clear(); m_Dots.Clear(); m_Cards.Clear(); m_Voices.Clear(); m_Decorations.Clear();
            m_GardenRings.Clear();
            if (m_CandyPink != null) Destroy(m_CandyPink);
            if (m_CandyCream != null) Destroy(m_CandyCream);
            m_CandyPink = m_CandyCream = null;
            if (m_GrowthGlowSprite != null) Destroy(m_GrowthGlowSprite);
            if (m_GrowthGlowTexture != null) Destroy(m_GrowthGlowTexture);
            m_GrowthGlowSprite = null;
            m_GrowthGlowTexture = null;
            enabled = false;
        }
        void OnDisable() { if (m_Root != null) StopAndClear(); }
    }
}
