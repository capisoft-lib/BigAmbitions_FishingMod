using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FishingMod
{
    internal sealed class FishingShoreResolver
    {
        private const float MaxShoreSearchDistance = 250f;
        private const float ShoreInset = 0.70f;
        private const float SampleRadius = 1.75f;
        private const int DirectionCount = 32;
        private const int MaxPathChecks = 160;
        private const float MaxFishingHeight = 80f;

        private static readonly float[] SearchRadii =
        {
            2f, 4f, 7f, 10f, 14f, 20f, 28f, 40f, 56f, 80f, 112f, 160f, 224f
        };

        private readonly List<Vector3> _candidates = new List<Vector3>(256);

        internal bool TryFindClosestReachable(
            NavMeshAgent agent,
            Vector3 start,
            Vector3 waterPoint,
            out Vector3 shorelinePoint,
            out float pathLength)
        {
            shorelinePoint = default;
            pathLength = 0f;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                return false;

            return TryFindClosestReachable(
                agent.agentTypeID,
                agent.areaMask,
                start,
                waterPoint,
                out shorelinePoint,
                out pathLength);
        }

        internal bool TryFindClosestReachable(
            int agentTypeId,
            int areaMask,
            Vector3 start,
            Vector3 waterPoint,
            out Vector3 shorelinePoint,
            out float pathLength)
        {
            shorelinePoint = default;
            pathLength = 0f;

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = agentTypeId,
                areaMask = areaMask
            };

            _candidates.Clear();
            // Start on the player's connected surface, including an elevated bridge. A
            // radius-1.75 query at sea level cannot see the usual quay 2.8 m above it.
            Vector3 playerLevelTarget = new Vector3(waterPoint.x, start.y, waterPoint.z);
            Vector3 reachableEdge = start;
            if (NavMesh.Raycast(start, playerLevelTarget, out NavMeshHit edgeHit, filter))
                reachableEdge = edgeHit.position;
            else if (NavMesh.SamplePosition(playerLevelTarget, out NavMeshHit targetHit, SampleRadius, filter))
                reachableEdge = targetHit.position;
            AddSample(reachableEdge, SampleRadius, filter, waterPoint);
            AddSample(playerLevelTarget, SampleRadius, filter, waterPoint);

            for (int radiusIndex = 0; radiusIndex < SearchRadii.Length; radiusIndex++)
            {
                float radius = SearchRadii[radiusIndex];

                float angularOffset = radiusIndex * 0.17320508f;
                for (int directionIndex = 0; directionIndex < DirectionCount; directionIndex++)
                {
                    float angle = angularOffset + directionIndex * Mathf.PI * 2f / DirectionCount;
                    Vector3 sample = waterPoint + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    sample.y = start.y;
                    AddSample(sample, SampleRadius, filter, waterPoint);
                    if (Mathf.Abs(start.y - waterPoint.y) > SampleRadius)
                    {
                        sample.y = waterPoint.y + 0.5f;
                        AddSample(sample, SampleRadius, filter, waterPoint);
                    }
                }
            }

            _candidates.Sort((left, right) => HorizontalDistanceSquared(left, waterPoint)
                .CompareTo(HorizontalDistanceSquared(right, waterPoint)));

            float bestScore = float.PositiveInfinity;
            NavMeshPath path = new NavMeshPath();
            // Keep a reachable fallback before nearer disconnected islands exhaust the
            // bounded path-check budget. Never shrink the search to the first island.
            EvaluateCandidate(reachableEdge, start, waterPoint, filter, path, ref bestScore, ref shorelinePoint, ref pathLength);
            int pathChecks = 0;
            for (int i = 0; i < _candidates.Count && pathChecks < MaxPathChecks; i++)
            {
                pathChecks++;
                EvaluateCandidate(_candidates[i], start, waterPoint, filter, path, ref bestScore, ref shorelinePoint, ref pathLength);
            }

            return !float.IsPositiveInfinity(bestScore);
        }

        private static void EvaluateCandidate(Vector3 rawCandidate, Vector3 start, Vector3 waterPoint,
            NavMeshQueryFilter filter, NavMeshPath path, ref float bestScore, ref Vector3 shorelinePoint, ref float pathLength)
        {
            float height = rawCandidate.y - waterPoint.y;
            if (height < -0.5f || height > MaxFishingHeight
                || HorizontalDistance(rawCandidate, waterPoint) > MaxShoreSearchDistance) return;
            Vector3 away = rawCandidate - waterPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) return;
            Vector3 wanted = rawCandidate + away.normalized * ShoreInset;
            if (!NavMesh.SamplePosition(wanted, out NavMeshHit insetHit, SampleRadius, filter)) return;
            Vector3 candidate = insetHit.position;
            if (Mathf.Abs(candidate.y - rawCandidate.y) > SampleRadius) return;
            if (!NavMesh.CalculatePath(start, candidate, filter, path) || path.status != NavMeshPathStatus.PathComplete) return;
            Vector3[] corners = path.corners;
            if (corners == null || corners.Length == 0 || (corners[corners.Length - 1] - candidate).sqrMagnitude > 2.25f) return;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            float score = FishingMath.ShoreScore(HorizontalDistance(candidate, waterPoint), length, height);
            if (score >= bestScore) return;
            bestScore = score;
            shorelinePoint = candidate;
            pathLength = length;
        }

        private void AddSample(Vector3 position, float maxDistance, NavMeshQueryFilter filter, Vector3 waterPoint)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, filter)) return;
            Vector3 candidate = hit.position;
            if (HorizontalDistance(candidate, waterPoint) > MaxShoreSearchDistance + 1f) return;

            for (int i = 0; i < _candidates.Count; i++)
            {
                if ((_candidates[i] - candidate).sqrMagnitude < 0.64f) return;
            }

            _candidates.Add(candidate);
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            return Mathf.Sqrt(HorizontalDistanceSquared(left, right));
        }

        private static float HorizontalDistanceSquared(Vector3 left, Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return x * x + z * z;
        }
    }
}
