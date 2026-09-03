using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingMod
{
    internal sealed class FishingWaterDetector
    {
        private const float MaxRayDistance = 1000f;
        private const float BoundsPadding = 0.75f;
        private const float HeightStep = 0.25f;
        private const float SpatialCellSize = 32f;
        private const int MaxCellsPerSurface = 512;
        private const int MaxRaycastHits = 128;
        private const int WaterAncestorDepth = 12;

        private readonly Dictionary<SurfaceCellKey, List<SurfaceTile>> _surfaceCells =
            new Dictionary<SurfaceCellKey, List<SurfaceTile>>();
        private readonly List<SurfaceTile> _largeOrUnboundedSurfaces = new List<SurfaceTile>();
        private readonly List<int> _surfaceHeightBuckets = new List<int>();
        private readonly HashSet<int> _surfaceHeightBucketIds = new HashSet<int>();
        private readonly HashSet<int> _waterIdentityIds = new HashSet<int>();
        private readonly HashSet<int> _hierarchyProbeIds = new HashSet<int>();
        private readonly HashSet<int> _indexedSurfaceIds = new HashSet<int>();
        private readonly HashSet<int> _queriedSurfaceIds = new HashSet<int>();
        private readonly RaycastHit[] _raycastHits = new RaycastHit[MaxRaycastHits];

        private int _cachedSceneHandle = int.MinValue;
        private bool _cacheBuilt;

        internal int SurfaceCount => _surfaceHeightBuckets.Count;
        internal int IndexedTileCount => _indexedSurfaceIds.Count;
        internal int CacheBuildCount { get; private set; }

        internal bool TryGetWaterPoint(Ray ray, Transform ignoredRoot, out Vector3 point)
        {
            point = default;
            EnsureCache();

            RaycastHit[] hits = _raycastHits;
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                hits,
                MaxRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            // Preserve correct blocker ordering in the exceptional case where the reusable
            // buffer is saturated. Ordinary water clicks allocate nothing.
            if (hitCount == hits.Length)
            {
                hits = Physics.RaycastAll(
                    ray,
                    MaxRayDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide);
                hitCount = hits.Length;
            }

            Array.Sort(hits, 0, hitCount, RaycastHitDistanceComparer.Instance);

            float firstBlockingDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.transform == null || IsIgnored(hit.transform, ignoredRoot)) continue;

                if (IsWaterHit(hit.transform))
                {
                    if (hit.distance <= firstBlockingDistance + 0.05f)
                    {
                        point = hit.point;
                        return true;
                    }

                    continue;
                }

                if (hit.collider != null && !hit.collider.isTrigger)
                    firstBlockingDistance = Mathf.Min(firstBlockingDistance, hit.distance);
            }

            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;

            float bestDistance = float.PositiveInfinity;
            Vector3 bestPoint = default;
            _queriedSurfaceIds.Clear();

            // Only the cells crossed by the click ray at each known water elevation are
            // queried. Individual tile bounds remain intact, so distant tiles can never
            // create a false rectangular water surface between them.
            for (int i = 0; i < _surfaceHeightBuckets.Count; i++)
            {
                int heightBucket = _surfaceHeightBuckets[i];
                float approximateHeight = heightBucket * HeightStep;
                float approximateDistance = (approximateHeight - ray.origin.y) / ray.direction.y;
                if (approximateDistance <= 0f || approximateDistance > MaxRayDistance) continue;

                Vector3 approximatePoint = ray.GetPoint(approximateDistance);
                int centreX = ToCell(approximatePoint.x);
                int centreZ = ToCell(approximatePoint.z);
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        SurfaceCellKey key = new SurfaceCellKey(
                            heightBucket,
                            centreX + offsetX,
                            centreZ + offsetZ);
                        if (!_surfaceCells.TryGetValue(key, out List<SurfaceTile> tiles)) continue;
                        for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
                            EvaluateSurface(ray, tiles[tileIndex], firstBlockingDistance, ref bestDistance, ref bestPoint);
                    }
                }
            }

            // Very large or authoritative unbounded surfaces stay rare and avoid filling
            // thousands of spatial cells. Their original bounds are still tested exactly.
            for (int i = 0; i < _largeOrUnboundedSurfaces.Count; i++)
                EvaluateSurface(
                    ray,
                    _largeOrUnboundedSurfaces[i],
                    firstBlockingDistance,
                    ref bestDistance,
                    ref bestPoint);

            if (float.IsPositiveInfinity(bestDistance)) return false;
            point = bestPoint;
            return true;
        }

        internal void ForceRefresh()
        {
            RebuildSurfaceCache(SceneManager.GetActiveScene().handle);
        }

        private void EnsureCache()
        {
            int activeSceneHandle = SceneManager.GetActiveScene().handle;
            if (_cacheBuilt && activeSceneHandle == _cachedSceneHandle) return;
            RebuildSurfaceCache(activeSceneHandle);
        }

        private void RebuildSurfaceCache(int activeSceneHandle)
        {
            _surfaceCells.Clear();
            _largeOrUnboundedSurfaces.Clear();
            _surfaceHeightBuckets.Clear();
            _surfaceHeightBucketIds.Clear();
            _waterIdentityIds.Clear();
            _hierarchyProbeIds.Clear();
            _indexedSurfaceIds.Clear();
            _queriedSurfaceIds.Clear();

            // Authoritative water components are rare. Discover them once per scene, mark
            // their hierarchy as water, then retain every renderer/collider as a local tile.
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.gameObject.scene.isLoaded) continue;
                string typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                if (!IsWaterComponentType(typeName) && !FishingMath.LooksLikeWater(behaviour.name)) continue;

                Transform owner = FindWaterOwner(behaviour.transform) ?? behaviour.transform;
                _waterIdentityIds.Add(owner.GetInstanceID());
                AddHierarchySurfaces(owner, allowUnbounded: IsWaterComponentType(typeName));
            }

            // Material-only water tiles are indexed once per scene. Sharing a material and
            // elevation only shares the height bucket; it never merges their spatial bounds.
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.gameObject.scene.isLoaded || !renderer.enabled) continue;

                Transform waterOwner = FindWaterOwner(renderer.transform);
                bool rendererIsWater = TryGetWaterMaterial(renderer, out _);
                if (waterOwner == null && !rendererIsWater) continue;

                if (waterOwner != null) _waterIdentityIds.Add(waterOwner.GetInstanceID());
                else _waterIdentityIds.Add(renderer.transform.GetInstanceID());
                AddBoundedSurface(renderer.transform, renderer.bounds);
            }

            _cachedSceneHandle = activeSceneHandle;
            _cacheBuilt = true;
            CacheBuildCount++;
        }

        private void AddHierarchySurfaces(Transform owner, bool allowUnbounded)
        {
            if (owner == null || !_hierarchyProbeIds.Add(owner.GetInstanceID())) return;

            bool foundBounds = false;
            Renderer[] renderers = owner.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer.enabled) continue;
                AddBoundedSurface(renderer.transform, renderer.bounds);
                foundBounds = true;
            }

            Collider[] colliders = owner.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (!collider.enabled) continue;
                AddBoundedSurface(collider.transform, collider.bounds);
                foundBounds = true;
            }

            if (!foundBounds && allowUnbounded)
                AddUnboundedSurface(owner, owner.position.y);
        }

        private void AddBoundedSurface(Transform owner, Bounds bounds)
        {
            if (owner == null || !_indexedSurfaceIds.Add(owner.GetInstanceID())) return;

            int heightBucket = ToHeightBucket(bounds.max.y);
            RegisterHeightBucket(heightBucket);
            SurfaceTile surface = new SurfaceTile(owner, heightBucket, bounds.max.y, bounds);

            int minX = ToCell(bounds.min.x - BoundsPadding);
            int maxX = ToCell(bounds.max.x + BoundsPadding);
            int minZ = ToCell(bounds.min.z - BoundsPadding);
            int maxZ = ToCell(bounds.max.z + BoundsPadding);
            long cellCount = (long)(maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount > MaxCellsPerSurface)
            {
                _largeOrUnboundedSurfaces.Add(surface);
                return;
            }

            for (int cellX = minX; cellX <= maxX; cellX++)
            {
                for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
                {
                    SurfaceCellKey key = new SurfaceCellKey(heightBucket, cellX, cellZ);
                    if (!_surfaceCells.TryGetValue(key, out List<SurfaceTile> tiles))
                    {
                        tiles = new List<SurfaceTile>();
                        _surfaceCells.Add(key, tiles);
                    }

                    tiles.Add(surface);
                }
            }
        }

        private void AddUnboundedSurface(Transform owner, float height)
        {
            if (owner == null || !_indexedSurfaceIds.Add(owner.GetInstanceID())) return;
            int heightBucket = ToHeightBucket(height);
            RegisterHeightBucket(heightBucket);
            _largeOrUnboundedSurfaces.Add(new SurfaceTile(owner, heightBucket, height));
        }

        private void RegisterHeightBucket(int heightBucket)
        {
            if (_surfaceHeightBucketIds.Add(heightBucket))
                _surfaceHeightBuckets.Add(heightBucket);
        }

        private void EvaluateSurface(
            Ray ray,
            SurfaceTile surface,
            float firstBlockingDistance,
            ref float bestDistance,
            ref Vector3 bestPoint)
        {
            if (!_queriedSurfaceIds.Add(surface.Id) || !surface.IsAlive) return;

            float distance = (surface.Height - ray.origin.y) / ray.direction.y;
            if (distance <= 0f || distance > MaxRayDistance || distance >= bestDistance) return;
            if (distance > firstBlockingDistance + 0.05f) return;

            Vector3 candidate = ray.GetPoint(distance);
            if (surface.HasBounds && !ContainsHorizontal(surface.Bounds, candidate, BoundsPadding)) return;

            bestDistance = distance;
            bestPoint = candidate;
        }

        private Transform FindWaterOwner(Transform transform)
        {
            Transform selected = null;
            int depth = 0;
            for (Transform current = transform; current != null && depth < WaterAncestorDepth;
                 current = current.parent, depth++)
            {
                if (_waterIdentityIds.Contains(current.GetInstanceID()) || HasWaterNameOrLayer(current))
                    selected = current;
            }

            return selected;
        }

        private bool IsWaterHit(Transform transform)
        {
            int depth = 0;
            for (Transform current = transform; current != null && depth < WaterAncestorDepth;
                 current = current.parent, depth++)
            {
                if (_waterIdentityIds.Contains(current.GetInstanceID()) || HasWaterNameOrLayer(current))
                    return true;
            }

            return false;
        }

        private static bool HasWaterNameOrLayer(Transform transform)
        {
            if (transform == null) return false;
            if (FishingMath.LooksLikeWater(transform.name)) return true;
            return FishingMath.LooksLikeWater(LayerMask.LayerToName(transform.gameObject.layer));
        }

        private static bool IsIgnored(Transform transform, Transform root)
        {
            return root != null && (transform == root || transform.IsChildOf(root));
        }

        private static bool IsWaterComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            return typeName.EndsWith(".WaterSurface", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("WaterSurface", StringComparison.OrdinalIgnoreCase)
                || typeName.EndsWith(".OceanRenderer", StringComparison.OrdinalIgnoreCase)
                || typeName.EndsWith(".WaterBody", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetWaterMaterial(Renderer renderer, out Material waterMaterial)
        {
            waterMaterial = null;
            if (renderer == null) return false;
            bool nameMatches = FishingMath.LooksLikeWater(renderer.name);

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                Shader shader = material.shader;
                if (!FishingMath.LooksLikeWater(material.name)
                    && (shader == null || !FishingMath.LooksLikeWater(shader.name)))
                    continue;

                waterMaterial = material;
                return true;
            }

            return nameMatches;
        }

        private static int ToHeightBucket(float height)
        {
            return Mathf.RoundToInt(height / HeightStep);
        }

        private static int ToCell(float value)
        {
            return Mathf.FloorToInt(value / SpatialCellSize);
        }

        private static bool ContainsHorizontal(Bounds bounds, Vector3 point, float padding)
        {
            return point.x >= bounds.min.x - padding && point.x <= bounds.max.x + padding
                && point.z >= bounds.min.z - padding && point.z <= bounds.max.z + padding;
        }

        private readonly struct SurfaceCellKey : IEquatable<SurfaceCellKey>
        {
            internal SurfaceCellKey(int heightBucket, int x, int z)
            {
                _heightBucket = heightBucket;
                _x = x;
                _z = z;
            }

            private readonly int _heightBucket;
            private readonly int _x;
            private readonly int _z;

            public bool Equals(SurfaceCellKey other)
            {
                return _heightBucket == other._heightBucket && _x == other._x && _z == other._z;
            }

            public override bool Equals(object obj)
            {
                return obj is SurfaceCellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _heightBucket;
                    hash = hash * 397 ^ _x;
                    return hash * 397 ^ _z;
                }
            }
        }

        private sealed class SurfaceTile
        {
            private readonly Transform _owner;

            internal SurfaceTile(Transform owner, int heightBucket, float height)
            {
                _owner = owner;
                Id = owner.GetInstanceID();
                HeightBucket = heightBucket;
                Height = height;
                Bounds = default;
                HasBounds = false;
            }

            internal SurfaceTile(Transform owner, int heightBucket, float height, Bounds bounds)
            {
                _owner = owner;
                Id = owner.GetInstanceID();
                HeightBucket = heightBucket;
                Height = height;
                Bounds = bounds;
                HasBounds = true;
            }

            internal int Id { get; }
            internal bool IsAlive => _owner != null && _owner.gameObject.activeInHierarchy;
            internal int HeightBucket { get; }
            internal float Height { get; }
            internal Bounds Bounds { get; }
            internal bool HasBounds { get; }
        }

        private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
        {
            internal static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();

            public int Compare(RaycastHit left, RaycastHit right)
            {
                return left.distance.CompareTo(right.distance);
            }
        }
    }
}
