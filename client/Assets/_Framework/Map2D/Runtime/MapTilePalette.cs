using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D
{
    [CreateAssetMenu(fileName = "TilePalette", menuName = "BigWorld/2D Map/Palette")]
    public sealed class MapTilePalette : ScriptableObject
    {
        [SerializeField] private List<MapTileType> types = new List<MapTileType>();
        public List<MapTileType> Types { get { return types ?? (types = new List<MapTileType>()); } set { types = value ?? new List<MapTileType>(); } }
    }
}
