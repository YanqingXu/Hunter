using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigWorld.Map2D
{
    /// <summary>
    /// Per-renderer runtime tile view. Entity spawning belongs to GridMapEntityStreamer,
    /// so loading terrain must not also instantiate the source tile's prefab.
    /// </summary>
    public sealed class MapRuntimeTile : TileBase
    {
        [SerializeField] private MapTileType source;
        public MapTileType Source { get { return source; } }

        internal void Initialize(MapTileType value)
        {
            source = value;
            name = value == null ? "Map Runtime Tile" : value.name + " (Runtime)";
            hideFlags = HideFlags.HideAndDontSave;
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData = default;
            if (source != null) source.GetTileData(position, tilemap, ref tileData);
            tileData.gameObject = null;
        }
    }
}
