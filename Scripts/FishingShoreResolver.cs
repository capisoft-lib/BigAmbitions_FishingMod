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
            AddSample(waterPoint, MaxShoreSearchDistance, filter, waterPoint);

            float maximumUsefulRadius = MaxShoreSearchDistance;
            if (_candidates.Count > 0)
            {
                float directDistance = HorizontalDistance(_candidates[0], waterPoint);
                maximumUsefulRadius = Mathf.Min(MaxShoreSearchDistance, Mathf.Max(28f, directDistance + 28f));
            }

            for (int radiusIndex = 0; radiusIndex < SearchRadii.Length; radiusIndex++)
            {
                float radius = SearchRadii[radiusIndex];
                if (radius > maximumUsefulRadius) break;

                float angularOffset = radiusIndex * 0.17320508f;
                for (int directionIndex = 0; directionIndex < DirectionCount; directionIndex++)
                {
                    float angle = angularOffset + directionIndex * Mathf.PI * 2f / DirectionCount;
                    Vector3 sample = waterPoint + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    AddSample(sample, SampleRadius, filter, waterPoint);
                }
            }

            _candidates.Sort((left, right) => HorizontalDistanceSquared(left, waterPoint)
                .CompareTo(HorizontalDistanceSquared(right, waterPoint)));

            float bestScore = float.PositiveInfinity;
            int pathChecks = 0;
            for (int i = 0; i < _candidates.Count && pathChecks < MaxPathChecks; i++)
            {
                Vector3 rawCandidate = _candidates[i];
                if (Mathf.Abs(rawCandidate.y - waterPoint.y) > 12f) continue;

                Vector3 away = rawCandidate - waterPoint;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) continue;

                Vector3 wanted = rawCandidate + away.normalized * ShoreInset;
                if (!NavMesh.SamplePosition(wanted, out NavMeshHit insetHit, 2f, filter)) continue;
                Vector3 candidate = insetHit.position;

                NavMeshPath path = new NavMeshPath();
                pathChecks++;
                if (!NavMesh.CalculatePath(start, candidate, filter, path)
                    || path.status != NavMeshPathStatus.PathComplete
                    || path.corners == null || path.corners.Length == 0)
                    continue;

                Vector3 finalCorner = path.corners[path.corners.Length - 1];
                if ((finalCorner - candidate).sqrMagnitude > 2.25f) continue;

                float candidatePathLength = GetPathLength(path);
                float waterDistance = HorizontalDistance(candidate, waterPoint);
                float score = FishingMath.ShoreScore(
                    waterDistance,
                    candidatePathLength,
                    Mathf.Abs(candidate.y - waterPoint.y));
                if (score >= bestScore) continue;

                bestScore = score;
                shorelinePoint = candidate;
                pathLength = candidatePathLength;
            }

            return !float.IsPositiveInfinity(bestScore);
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

        private static float GetPathLength(NavMeshPath path)
        {
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return length;
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
