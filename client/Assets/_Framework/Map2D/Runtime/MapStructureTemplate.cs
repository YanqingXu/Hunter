using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>A reusable two-layer grid layout, normally stored as a subasset of this template.</summary>
    [CreateAssetMenu(fileName = "StructureTemplate", menuName = "BigWorld/2D Map/Structure Template")]
    public sealed class MapStructureTemplate : ScriptableObject
    {
        [SerializeField] private string displayName = "房屋模板";
        [SerializeField, TextArea] private string description;
        [SerializeField] private GridMapAsset layout;

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(displayName) ? name : displayName; }
            set { displayName = value ?? string.Empty; }
        }

        public string Description
        {
            get { return description ?? string.Empty; }
            set { description = value ?? string.Empty; }
        }

        /// <summary>Authored tile references. Assigning a layout does not create, copy, or save assets.</summary>
        public GridMapAsset Layout { get { return layout; } set { layout = value; } }
        public int Width { get { return layout == null ? 0 : layout.Width; } }
        public int Height { get { return layout == null ? 0 : layout.Height; } }
    }
}
