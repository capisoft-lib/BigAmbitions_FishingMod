using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingMod
{
    internal sealed class FishingWaterDetector
    {
        internal const int StaticPolygonCount = StaticWater.GameWaterAtlas.PolygonCount;
        internal const float StaticSeaHeight = -2.8f;
        internal const float MaxFishingDistance = 10f;
        private readonly bool _useStaticAtlas;
        private readonly StaticWater.StaticWaterAtlas _staticAtlas;
        private static class StaticAtlasCache
        {
            internal static readonly StaticWater.StaticWaterAtlas Instance = StaticWater.GameWaterAtlas.Create();
        }

        // The legacy scene detector remains available only for isolated regression
        // fixtures. Runtime defaults to the embedded atlas and never scans the city.
        internal FishingWaterDetector(bool useStaticAtlas = true)
        {
            _useStaticAtlas = useStaticAtlas;
            if (useStaticAtlas)
            {
                _staticAtlas = StaticAtlasCache.Instance;
                _cacheBuilt = true;
                CacheBuildCount = 1;
            }
        }

        internal string LastMatchedZoneId { get; private set; }
        internal string LastFailureReason { get; private set; }
        internal Vector3 LastCandidatePoint { get; private set; }
        internal Collider LastBlockingCollider { get; private set; }
        private bool _hasProximityCache;
        private float _proximityX, _proximityZ;
        private double _proximityDistance;

        internal bool IsPlayerNearWater(Vector3 position, out float distance)
        {
            // Intentionally ignore Y: a pier or raised quay can be near water
            // even though its deck is higher than the sea surface.
            if (!_hasProximityCache || position.x != _proximityX || position.z != _proximityZ)
            {
                _proximityDistance = _staticAtlas != null
                    ? _staticAtlas.DistanceToWater(position.x, position.z) : double.PositiveInfinity;
                _proximityX = position.x;
                _proximityZ = position.z;
                _hasProximityCache = true;
            }
            double horizontal = _proximityDistance;
            distance = (float)horizontal;
            return horizontal <= MaxFishingDistance;
        }
        private const float MaxRayDistance = 1000f;
        private const float BoundsPadding = 0.75f;
        private const float HeightStep = 0.25f;
        private const float SpatialCellSize = 32f;
        private const int MaxCellsPerSurface = 512;
        private const int MaxRaycastHits = 128;
        private const float RecoveryRefreshSeconds = 10f;
        private const int ObjectsPerIndexStep = 128;
        private const double IndexStepMilliseconds = 1d;

        private Dictionary<SurfaceCellKey, List<SurfaceTile>> _surfaceCells =
            new Dictionary<SurfaceCellKey, List<SurfaceTile>>();
        private List<SurfaceTile> _largeOrUnboundedSurfaces = new List<SurfaceTile>();
        private List<int> _surfaceHeightBuckets = new List<int>();
        private HashSet<int> _surfaceHeightBucketIds = new HashSet<int>();
        private HashSet<int> _waterIdentityIds = new HashSet<int>();
        private HashSet<int> _indexedSurfaceIds = new HashSet<int>();
        private readonly HashSet<int> _queriedSurfaceIds = new HashSet<int>();
        private readonly RaycastHit[] _raycastHits = new RaycastHit[MaxRaycastHits];

        private int _cachedSceneHandle = int.MinValue;
        private bool _cacheBuilt;
        private int _cachedSceneSignature;
        private float _nextRecoveryRefreshAt;
        private FishingWaterDetector _buildingIndex;
        private IEnumerator<Transform> _scan;
        private int _scanSignature;
        private readonly List<Renderer> _scanRenderers = new List<Renderer>();
        private static readonly Type HdrpWaterType = Type.GetType(
            "UnityEngine.Rendering.HighDefinition.WaterSurface, Unity.RenderPipelines.HighDefinition.Runtime", false);

        internal int SurfaceCount => _useStaticAtlas ? 1 : _surfaceHeightBuckets.Count;
        internal int IndexedTileCount => _useStaticAtlas ? StaticPolygonCount : _indexedSurfaceIds.Count;
        internal int CacheBuildCount { get; private set; }
        internal bool IsIndexing => _scan != null;
        internal int LastIndexStepObjects { get; private set; }

        internal bool TryGetWaterPoint(Ray ray, Transform ignoredRoot, out Vector3 point)
        {
            if (_useStaticAtlas) return TryGetStaticWaterPoint(ray, ignoredRoot, out point);
            // Input only queries the published spatial index. Even a miss or a scene
            // change must never enumerate the city synchronously from a click.
            return TryGetCachedWaterPoint(ray, ignoredRoot, out point);
        }

        private bool TryGetStaticWaterPoint(Ray ray, Transform ignoredRoot, out Vector3 point)
        {
            point = default;
            LastMatchedZoneId = null;
            LastCandidatePoint = default;
            LastBlockingCollider = null;
            LastFailureReason = "parallel_ray";
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float distance = (StaticSeaHeight - ray.origin.y) / ray.direction.y;
            LastFailureReason = "sea_plane_out_of_range";
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f || distance > MaxRayDistance)
                return false;
            Vector3 candidate = ray.GetPoint(distance);
            LastCandidatePoint = candidate;
            LastFailureReason = "outside_static_polygons";
            if (!_staticAtlas.TryGetCandidate(candidate.x, candidate.z, out var zone)) return false;

            // A static X/Z hit is not permission to cast through a quay, bridge,
            // building, vehicle or other solid. Only the verified invisible marina
            // mouse-ground plane is exempt, after polygon membership is established.
            RaycastHit[] hits = _raycastHits;
            int count = Physics.RaycastNonAlloc(ray, hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            float closestBlockerDistance = float.PositiveInfinity;
            string closestBlockerReason = "solid_occlusion";
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.isTrigger || IsIgnored(hit.transform, ignoredRoot)) continue;
                bool support = IsHamptonsMarinaMousePlane(hit.collider) || IsHamptonsMarinaGroundVolume(hit.collider)
                    || IsHamptonsEastCoastOccluder(hit.collider)
                    || FishingNativeWaterOccluder.IsVerifiedSupport(hit.collider);
                // These planes cover land as well as water. A click on land must
                // not become a water click by projecting farther down the camera ray.
                if (support && _staticAtlas.TryGetCandidate(hit.point.x, hit.point.z, out _)) continue;
                if (hit.distance < distance - 0.05f && hit.distance < closestBlockerDistance)
                {
                    closestBlockerDistance = hit.distance;
                    LastBlockingCollider = hit.collider;
                    closestBlockerReason = support ? "land_before_water" : "solid_occlusion";
                }
            }
            if (LastBlockingCollider != null)
            {
                LastFailureReason = closestBlockerReason;
                return false;
            }
            point = candidate;
            LastMatchedZoneId = zone.Id;
            LastFailureReason = null;
            return true;
        }

        internal static bool IsHamptonsMarinaMousePlane(Collider collider)
        {
            // Game build 3675: this disabled-renderer mesh catches normal walking
            // clicks across the marina basin, at Y=.05 above the actual sea (-2.8).
            // Never ignore all Roads colliders or all invisible meshes: real docks
            // and barriers also have collision-only geometry.
            if (!(collider is MeshCollider) || collider.gameObject.layer != LayerMask.NameToLayer("Roads")) return false;
            Transform owner = collider.transform;
            if (owner.name != "RoadGroundPlane (4)" || owner.parent == null || owner.parent.name != "Roads"
                || owner.parent.parent == null || owner.parent.parent.name != "TheHamptons") return false;
            Renderer renderer = owner.GetComponent<Renderer>();
            if (renderer == null || renderer.enabled) return false;
            Bounds bounds = collider.bounds;
            return bounds.size.y < .05f && Mathf.Abs(bounds.center.y - .05f) < .1f
                && Mathf.Abs(bounds.center.x + 2548.2f) < 1f && Mathf.Abs(bounds.center.z + 1110.4f) < 1f
                && Mathf.Abs(bounds.size.x - 562f) < 1f && Mathf.Abs(bounds.size.z - 675.7f) < 1f;
        }

        internal static bool IsHamptonsMarinaGroundVolume(Collider collider)
        {
            // Build 3675: rendererless Ground (2) spans the same marina rectangle
            // as the mouse plane. Only filter this volume after atlas membership.
            if (!(collider is BoxCollider) || collider.gameObject.layer != LayerMask.NameToLayer("Ground")) return false;
            Transform owner = collider.transform;
            if (owner.name != "Ground (2)" || owner.parent == null || owner.parent.name != "HamptonsGroundPlanes"
                || owner.parent.parent == null || owner.parent.parent.name != "TheHamptons") return false;
            if (owner.GetComponent<Renderer>() != null) return false;
            Bounds bounds = collider.bounds;
            return Mathf.Abs(bounds.center.y + 1f) < .05f && Mathf.Abs(bounds.size.y - .5626125f) < .05f
                && Mathf.Abs(bounds.center.x + 2548.2f) < 1f && Mathf.Abs(bounds.center.z + 1110.4f) < 1f
                && Mathf.Abs(bounds.size.x - 562f) < 1f && Mathf.Abs(bounds.size.z - 675.7f) < 1f;
        }

        internal static bool IsHamptonsEastCoastOccluder(Collider collider)
        {
            // Build 3675, verified level16 objects. These broad invisible layers
            // overlap atlas water near (-2233,-875); physical colliders stay active.
            Transform owner = collider.transform;
            if (owner.parent == null || owner.parent.parent == null
                || owner.parent.parent.name != "TheHamptons") return false;
            Bounds b = collider.bounds;
            if (collider is MeshCollider && collider.gameObject.layer == LayerMask.NameToLayer("Roads")
                && owner.name == "RoadGroundPlane (2)" && owner.parent.name == "Roads")
            {
                Renderer renderer = owner.GetComponent<Renderer>();
                return renderer != null && !renderer.enabled && b.size.y < .05f
                    && Mathf.Abs(b.center.y - .05f) < .1f
                    && Mathf.Abs(b.center.x + 2333.6343f) < 1f && Mathf.Abs(b.center.z + 1316.2461f) < 1f
                    && Mathf.Abs(b.size.x - 474.8446f) < 1f && Mathf.Abs(b.size.z - 1087.4351f) < 1f;
            }
            if (collider is BoxCollider && collider.gameObject.layer == LayerMask.NameToLayer("Ground")
                && owner.name == "Ground" && owner.parent.name == "HamptonsGroundPlanes")
            {
                return owner.GetComponent<Renderer>() == null
                    && Mathf.Abs(b.center.y + 1f) < .05f && Mathf.Abs(b.size.y - .5626125f) < .05f
                    && Mathf.Abs(b.center.x + 2328.9805f) < 1f && Mathf.Abs(b.center.z + 1316.2461f) < 1f
                    && Mathf.Abs(b.size.x - 484.1732f) < 1f && Mathf.Abs(b.size.z - 1087.4351f) < 1f;
            }
            return false;
        }

        private bool TryGetCachedWaterPoint(Ray ray, Transform ignoredRoot, out Vector3 point)
        {
            point = default;

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

                if (IsWaterHit(hit.collider))
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
            if (_useStaticAtlas) return;
            CancelIndexing();
            RebuildSurfaceCache(SceneManager.GetActiveScene().handle);
            _nextRecoveryRefreshAt = Time.realtimeSinceStartup + RecoveryRefreshSeconds;
        }

        internal void RequestRefresh()
        {
            if (!_useStaticAtlas) _nextRecoveryRefreshAt = 0f;
        }

        // Called by Update, independently of input. Publish an entire new snapshot only
        // once ready, so fishing continues to use the previous index during the scan.
        internal void AdvanceIndexing()
        {
            LastIndexStepObjects = 0;
            if (_useStaticAtlas) return;
            int signature = SceneSignature();
            if (_scan != null && signature != _scanSignature) CancelIndexing();
            if (_scan == null)
            {
                if (_cacheBuilt && signature == _cachedSceneSignature &&
                    SceneManager.GetActiveScene().handle == _cachedSceneHandle &&
                    Time.realtimeSinceStartup < _nextRecoveryRefreshAt) return;
                _scanSignature = signature;
                _buildingIndex = new FishingWaterDetector(useStaticAtlas: false);
                _scan = SceneObjects().GetEnumerator();
            }
            long started = Stopwatch.GetTimestamp();
            do
            {
                if (!_scan.MoveNext())
                {
                    _surfaceCells = _buildingIndex._surfaceCells;
                    _largeOrUnboundedSurfaces = _buildingIndex._largeOrUnboundedSurfaces;
                    _surfaceHeightBuckets = _buildingIndex._surfaceHeightBuckets;
                    _surfaceHeightBucketIds = _buildingIndex._surfaceHeightBucketIds;
                    _waterIdentityIds = _buildingIndex._waterIdentityIds;
                    _indexedSurfaceIds = _buildingIndex._indexedSurfaceIds;
                    _queriedSurfaceIds.Clear();
                    _cachedSceneHandle = SceneManager.GetActiveScene().handle;
                    _cachedSceneSignature = _scanSignature;
                    _cacheBuilt = true; CacheBuildCount++;
                    _nextRecoveryRefreshAt = Time.realtimeSinceStartup + RecoveryRefreshSeconds;
                    CancelIndexing();
                    return;
                }
                Transform owner = _scan.Current;
                LastIndexStepObjects++;
                if (owner != null && owner.gameObject.activeInHierarchy && owner.gameObject.scene.isLoaded)
                {
                    var water = HdrpWaterType == null ? null : owner.GetComponent(HdrpWaterType) as MonoBehaviour;
                    if (water != null && water.isActiveAndEnabled) _buildingIndex.AddHdrpSurface(water);
                    owner.GetComponents(_scanRenderers);
                    foreach (var renderer in _scanRenderers)
                    {
                        if (renderer == null || !IsWaterRenderer(renderer)) continue;
                        _buildingIndex._waterIdentityIds.Add(owner.GetInstanceID());
                        _buildingIndex.AddBoundedSurface(owner, renderer.bounds,
                            mesh: owner.GetComponent<MeshFilter>()?.sharedMesh);
                    }
                }
            }
            while (LastIndexStepObjects < ObjectsPerIndexStep &&
                (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency < IndexStepMilliseconds);
        }

        internal void CancelIndexing()
        {
            _scan?.Dispose(); _scan = null; _buildingIndex = null; _scanRenderers.Clear();
        }

        private struct WalkCursor { internal Transform Owner; internal int Child; }
        private static IEnumerable<Transform> SceneObjects()
        {
            var roots = new List<GameObject>();
            var stack = new Stack<WalkCursor>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                roots.Clear(); scene.GetRootGameObjects(roots);
                foreach (var root in roots)
                {
                    if (root == null || !root.activeInHierarchy) continue;
                    stack.Push(new WalkCursor { Owner = root.transform, Child = -1 });
                    while (stack.Count > 0)
                    {
                        var cursor = stack.Pop();
                        if (cursor.Owner == null || !cursor.Owner.gameObject.activeInHierarchy) continue;
                        if (cursor.Child < 0)
                        {
                            cursor.Child = 0; stack.Push(cursor);
                            yield return cursor.Owner;
                        }
                        else if (cursor.Child < cursor.Owner.childCount)
                        {
                            var child = cursor.Owner.GetChild(cursor.Child++);
                            stack.Push(cursor);
                            stack.Push(new WalkCursor { Owner = child, Child = -1 });
                        }
                    }
                }
            }
        }

        private static int SceneSignature()
        {
            int signature = 17;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                unchecked { signature = signature * 31 + SceneManager.GetSceneAt(i).handle; }
            return signature;
        }

        private void RebuildSurfaceCache(int activeSceneHandle)
        {
            _surfaceCells.Clear();
            _largeOrUnboundedSurfaces.Clear();
            _surfaceHeightBuckets.Clear();
            _surfaceHeightBucketIds.Clear();
            _waterIdentityIds.Clear();
            _indexedSurfaceIds.Clear();
            _queriedSurfaceIds.Clear();

            // A water component describes its own surface, never the whole parent district.
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.isActiveAndEnabled || !behaviour.gameObject.scene.isLoaded) continue;
                string typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
                if (typeName == "UnityEngine.Rendering.HighDefinition.WaterSurface")
                    AddHdrpSurface(behaviour);
            }

            // Material-only water tiles are indexed once per scene. Sharing a material and
            // elevation only shares the height bucket; it never merges their spatial bounds.
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.gameObject.scene.isLoaded || !renderer.enabled) continue;

                if (!IsWaterRenderer(renderer)) continue;
                _waterIdentityIds.Add(renderer.transform.GetInstanceID());
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                AddBoundedSurface(renderer.transform, renderer.bounds, mesh: mesh);
            }

            _cachedSceneHandle = activeSceneHandle;
            _cachedSceneSignature = SceneSignature();
            _cacheBuilt = true;
            CacheBuildCount++;
        }

        private void AddHdrpSurface(MonoBehaviour surface)
        {
            Type type = surface.GetType();
            string geometry = type.GetField("geometryType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(surface)?.ToString();
            string surfaceType = type.GetField("surfaceType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(surface)?.ToString();
            Transform owner = surface.transform;
            if (geometry == "Infinite" && surfaceType == "OceanSeaLake")
            {
                AddUnboundedSurface(owner, owner.position.y);
                return;
            }
            // HDRP's underwater volume is NOT its water outline, and a custom mesh may
            // have no MeshRenderer. Unknown geometry must never become infinite water.
            Mesh mesh = type.GetField("mesh", BindingFlags.Instance | BindingFlags.Public)?.GetValue(surface) as Mesh;
            if (geometry == "CustomMesh" && mesh != null)
            {
                AddBoundedSurface(owner, TransformBounds(owner, mesh.bounds), owner.position.y, mesh, mesh.bounds);
                return;
            }
            if (geometry == null) return;
            Bounds quad = new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));
            AddBoundedSurface(owner, TransformBounds(owner, quad), owner.position.y, localBounds: quad);
        }

        private static Bounds TransformBounds(Transform owner, Bounds bounds)
        {
            Bounds result = new Bounds(owner.TransformPoint(bounds.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    result.Encapsulate(owner.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, 0f, z))));
            return result;
        }

        private void AddBoundedSurface(Transform owner, Bounds bounds, float? height = null, Mesh mesh = null, Bounds? localBounds = null)
        {
            if (owner == null || !_indexedSurfaceIds.Add(owner.GetInstanceID())) return;

            float surfaceHeight = height ?? bounds.center.y;
            int heightBucket = ToHeightBucket(surfaceHeight);
            RegisterHeightBucket(heightBucket);
            SurfaceTile surface = new SurfaceTile(owner, heightBucket, surfaceHeight, bounds, mesh, localBounds);

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
            if (!surface.ContainsFootprint(candidate)) return;

            bestDistance = distance;
            bestPoint = candidate;
        }

        private bool IsWaterHit(Collider collider)
        {
            if (collider == null) return false;
            Transform transform = collider.transform;
            // Do not inherit a broad "Ocean", "Harbor" or water-layer parent: its docks,
            // buildings and bridges are solid blockers, not fishing surfaces.
            if (_waterIdentityIds.Contains(transform.GetInstanceID())) return true;
            Renderer renderer = transform.GetComponent<Renderer>();
            if (renderer != null) return IsWaterRenderer(renderer);
            return !collider.isTrigger && HasWaterNameOrLayer(transform) && IsSurfaceShaped(collider.bounds);
        }

        private static bool IsSurfaceShaped(Bounds bounds) => bounds.size.x >= 1f && bounds.size.z >= 1f
            && bounds.size.y <= Mathf.Max(1f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.1f);

        private static bool IsWaterRenderer(Renderer renderer) => renderer.enabled
            && IsSurfaceShaped(renderer.bounds) && (TryGetWaterMaterial(renderer, out _) || HasWaterNameOrLayer(renderer.transform));

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
            private readonly Bounds? _localBounds;
            private readonly Vector3[] _vertices;
            private readonly int[] _triangles;

            internal SurfaceTile(Transform owner, int heightBucket, float height)
            {
                _owner = owner;
                Id = owner.GetInstanceID();
                HeightBucket = heightBucket;
                Height = height;
                Bounds = default;
                HasBounds = false;
            }

            internal SurfaceTile(Transform owner, int heightBucket, float height, Bounds bounds, Mesh mesh = null, Bounds? localBounds = null)
            {
                _owner = owner;
                Id = owner.GetInstanceID();
                HeightBucket = heightBucket;
                Height = height;
                Bounds = bounds;
                HasBounds = true;
                _localBounds = localBounds;
                if (mesh != null && mesh.isReadable)
                {
                    _vertices = mesh.vertices;
                    _triangles = mesh.triangles;
                }
            }

            internal bool ContainsFootprint(Vector3 point)
            {
                Vector3 local = _owner.InverseTransformPoint(point);
                if (_localBounds.HasValue && !ContainsHorizontal(_localBounds.Value, local, 0.001f)) return false;
                if (_vertices == null || _triangles == null) return true;
                for (int i = 0; i + 2 < _triangles.Length; i += 3)
                {
                    Vector3 a = _vertices[_triangles[i]], b = _vertices[_triangles[i + 1]], c = _vertices[_triangles[i + 2]];
                    float area = Cross(b - a, c - a);
                    if (Mathf.Abs(area) < 0.00001f) continue;
                    float u = Cross(b - local, c - local) / area;
                    float v = Cross(c - local, a - local) / area;
                    float w = 1f - u - v;
                    if (u >= -0.0001f && v >= -0.0001f && w >= -0.0001f) return true;
                }
                return false;
            }

            private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;

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
