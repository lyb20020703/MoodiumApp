using System.Collections;
using System.Collections.Generic;
using Moodium.CandyWorld;
using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Rewards
{
    [DisallowMultipleComponent]
    public sealed class RewardSequenceController : MonoBehaviour
    {
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { foreach (var reward in m_SpawnedRewards) if (reward != null) visibility.Register(reward); }

        readonly HashSet<int> m_AwardedStages = new();
        readonly List<GameObject> m_SpawnedRewards = new();
        CandyEnergyController m_Energy;
        WorldRewardTable m_Table;
        RewardBracelet m_Bracelet;
        Camera m_Camera;
        Vector3 m_LastPinchPosition;
        Bounds m_BagBounds;
        bool m_NatureWorld;
        bool m_Running;
        CandyRainRewardController m_RainReward;
        bool m_FullRewardPlayed;
        bool m_TableFeedbackOnly;
        System.Action<int> m_TableFeedback;
        public void ConfigureTableFeedback(CandyEnergyController energy,System.Action<int> feedback)
        {
            StopAndClear();m_Energy=energy;m_TableFeedback=feedback;m_TableFeedbackOnly=true;
        }
        public int DeliveredStageCount => m_AwardedStages.Count;

        public void Configure(CandyEnergyController energy, WorldRewardTable table, RewardBracelet bracelet,
            Camera camera, bool natureWorld, CandyRainRewardController rainReward)
        {
            m_TableFeedbackOnly=false;m_TableFeedback=null;
            if (m_Energy != null)
                m_Energy.StageReached -= OnStageReached;
            m_Energy = energy;
            m_Table = table;
            m_Bracelet = bracelet;
            m_Camera = camera;
            m_NatureWorld = natureWorld;
            m_RainReward = rainReward;
        }

        public void BeginSession()
        {
            StopAndClear();
            m_Running = true;
            if (m_Energy != null)
                m_Energy.StageReached += OnStageReached;
        }

        public void RecordPinch(Vector3 visualCenter, Bounds bagBounds)
        {
            m_LastPinchPosition = visualCenter;
            m_BagBounds = bagBounds;
        }
        public void SetProgress(float normalized) => m_Bracelet?.SetProgress(normalized);

        public void StopAndClear()
        {
            if (m_Energy != null)
                m_Energy.StageReached -= OnStageReached;
            StopAllCoroutines();
            foreach (var reward in m_SpawnedRewards)
                if (reward != null)
                    if (Application.isPlaying) Destroy(reward); else DestroyImmediate(reward);
            m_SpawnedRewards.Clear();
            m_AwardedStages.Clear();
            m_FullRewardPlayed = false;
            m_Bracelet?.Clear();
            m_Running = false;
        }

        void OnStageReached(int stage)
        {
            if(m_TableFeedbackOnly)
            {
                if(m_Running&&stage<100&&m_AwardedStages.Add(stage))m_TableFeedback?.Invoke(stage);
                return;
            }
            if (!m_Running || !m_AwardedStages.Add(stage) || m_Table == null || !m_Table.TryGetEntry(stage, out var entry))
                return;
            StartCoroutine(Deliver(entry));
        }

        IEnumerator Deliver(RewardEntry entry)
        {
            var source = m_LastPinchPosition != Vector3.zero ? m_LastPinchPosition :
                (m_Camera != null ? m_Camera.transform.position + m_Camera.transform.forward * .4f : transform.position);
            var orb = RewardOrb.Create(entry, m_NatureWorld, source + Vector3.up * .12f);
            m_SpawnedRewards.Add(orb.gameObject);
            yield return new WaitForSeconds(1.3f);
            if (m_Bracelet != null)
            {
                m_Bracelet.TryCollect(orb, entry);
                if (m_Bracelet.CollectedCount == m_Bracelet.SlotCount && !m_FullRewardPlayed)
                {
                    m_FullRewardPlayed = true;
                    m_RainReward?.PlayReward();
                    StartCoroutine(StopRainAfterDelay());
                }
            }

            yield return new WaitForSeconds(1f);
            SpawnPhysicalReward(entry);
        }

        IEnumerator StopRainAfterDelay()
        {
            yield return new WaitForSeconds(8f);
            m_RainReward?.StopAndClear();
        }

        void SpawnPhysicalReward(RewardEntry entry)
        {
            if (entry.prefabs == null)
                return;
            var forward = m_Camera != null ? Vector3.ProjectOnPlane(m_Camera.transform.forward, Vector3.up).normalized : Vector3.forward;
            var origin = m_Camera != null ? m_Camera.transform.position : transform.position;
            var placement = new RewardPlacementSolver().Solve(new RewardPlacementRequest
            {
                Stage = entry.stage,
                SizeClass = entry.sizeClass,
                TargetHeight = entry.targetHeight,
                HeadPosition = origin,
                Forward = forward,
                BagBounds = m_BagBounds.size.sqrMagnitude > 0f ? m_BagBounds : new Bounds(m_LastPinchPosition, Vector3.one * .1f),
                VideoBounds = new Bounds(m_LastPinchPosition, Vector3.zero),
                Occupied = new List<RewardOccupiedSpace>()
            }, new RuntimePlacementQueries());
            if (!placement.IsValid)
            {
                Debug.LogWarning($"[Reward Sequence] No valid scanned placement for stage {entry.stage}; reward was not spawned.");
                return;
            }
            var position = placement.Position;
            foreach (var prefab in entry.prefabs)
            {
                if (prefab == null)
                    continue;
                var reward = Instantiate(prefab, position, Quaternion.Euler(0f, placement.YawDegrees, 0f));
                reward.name = prefab.name + " (World Reward)";
                m_SpawnedRewards.Add(reward);
                if (TryGetRenderedHeight(reward, out var sourceHeight))
                    reward.transform.localScale *= entry.targetHeight / sourceHeight;
                var grow = reward.AddComponent<RewardGrowIn>();
                grow.Begin(position.y, entry.targetHeight);
                var soft = reward.GetComponent<CompletionHouseSoftInteraction>();
                if (soft == null)
                    soft = reward.AddComponent<CompletionHouseSoftInteraction>();
                soft.Configure();
                break;
            }
        }

        static bool TryGetRenderedHeight(GameObject target, out float height)
        {
            height = 0f;
            var found = false;
            var bounds = default(Bounds);
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }
            height = found ? bounds.size.y : 0f;
            return height > .0001f;
        }

        sealed class RuntimePlacementQueries : IRewardPlacementQueries
        {
            public bool TryGetSurface(Vector3 candidate, RewardSurfaceKind kind, out RewardSurface surface)
            {
                foreach (var hit in Physics.RaycastAll(candidate + Vector3.up * 2.5f, Vector3.down, 4f,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (hit.collider is MeshCollider)
                    {
                        surface = new RewardSurface(hit.point, hit.normal);
                        return true;
                    }
                surface = default;
                return false;
            }

            public bool HasClearance(Vector3 position, float height, float radius) => !Physics.CheckCapsule(
                position + Vector3.up * .05f, position + Vector3.up * Mathf.Max(.05f, height), radius,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            public bool IsVisible(Vector3 headPosition, Vector3 samplePoint)
            {
                var delta = samplePoint - headPosition;
                var distance = delta.magnitude;
                return distance < .001f || !Physics.Raycast(headPosition, delta / distance, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
        }
    }
}
