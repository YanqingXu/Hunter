using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>Tracks generated objects without relying on mutable GameObject names.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class MapGeneratedPart : MonoBehaviour
    {
        [SerializeField, HideInInspector] private GridMapRenderer owner;
        [SerializeField, HideInInspector] private bool isRoot;
        [SerializeField, HideInInspector] private MapLayer layer;
        [SerializeField, HideInInspector] private MapTileCollisionMode collision;
        public GridMapRenderer Owner { get { return owner; } }
        public bool IsRoot { get { return isRoot; } }
        public MapLayer Layer { get { return layer; } }
        public MapTileCollisionMode Collision { get { return collision; } }

        internal void Configure(GridMapRenderer newOwner, bool root, MapLayer newLayer, MapTileCollisionMode newCollision)
        {
            owner = newOwner;
            isRoot = root;
            layer = newLayer;
            collision = newCollision;
            hideFlags = HideFlags.HideInInspector;
        }
    }
}
