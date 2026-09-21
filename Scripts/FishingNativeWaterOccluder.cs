using UnityEngine;

namespace FishingMod
{
    internal sealed class FishingNativeWaterOccluder
    {
        private readonly string[] _path;
        private readonly int _layer;
        private readonly bool _box;
        private readonly bool _disabledRenderer;
        private readonly Vector3 _center;
        private readonly Vector3 _size;

        internal FishingNativeWaterOccluder(string path, int layer, bool box, bool disabledRenderer,
            Vector3 center, Vector3 size)
        {
            _path = path.Split('/');
            _layer = layer;
            _box = box;
            _disabledRenderer = disabledRenderer;
            _center = center;
            _size = size;
        }

        internal bool Matches(Collider collider)
        {
            if (collider.gameObject.layer != _layer || (_box ? !(collider is BoxCollider) : !(collider is MeshCollider)))
                return false;
            Transform owner = collider.transform;
            for (int i = _path.Length - 1; i >= 0; i--)
            {
                if (owner == null || owner.name != _path[i]) return false;
                owner = owner.parent;
            }
            if (owner != null) return false;
            Renderer renderer = collider.GetComponent<Renderer>();
            if (_disabledRenderer ? renderer == null || renderer.enabled : renderer != null) return false;
            Bounds bounds = collider.bounds;
            return (bounds.center - _center).sqrMagnitude < .04f
                && (bounds.size - _size).sqrMagnitude < .04f;
        }

        internal static bool IsVerifiedSupport(Collider collider)
        {
            foreach (var item in FishingNativeWaterOccluders.All)
                if (item.Matches(collider)) return true;
            return false;
        }
    }
}
