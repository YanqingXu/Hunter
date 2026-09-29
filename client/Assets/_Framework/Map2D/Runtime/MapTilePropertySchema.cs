using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>Shared property definitions. Loading a schema never changes its defaults or adds definitions.</summary>
    [CreateAssetMenu(fileName = "TilePropertySchema", menuName = "BigWorld/2D Map/Property Schema")]
    public sealed class MapTilePropertySchema : ScriptableObject
    {
        [SerializeField] private List<MapTilePropertyDefinition> definitions = new List<MapTilePropertyDefinition>();

        public List<MapTilePropertyDefinition> Definitions
        {
            get { return definitions ?? (definitions = new List<MapTilePropertyDefinition>()); }
            set { definitions = value ?? new List<MapTilePropertyDefinition>(); }
        }

        /// <summary>Returns the first definition with an exact key. Display names do not participate in lookup.</summary>
        public bool TryGetDefinition(string key, out MapTilePropertyDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(key) || definitions == null) return false;
            foreach (MapTilePropertyDefinition candidate in definitions)
            {
                if (candidate != null && string.Equals(candidate.Key, key, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Explicit authoring helper. Adds missing built-ins as false Boolean properties;
        /// existing definitions, display names, and defaults are preserved.
        /// Editor callers must record Undo and mark the schema dirty themselves.
        /// </summary>
        public bool EnsureBuiltInDefinitions()
        {
            bool changed = EnsureBooleanDefinition(MapTilePropertyKeys.Destructible, "可被销毁");
            return EnsureBooleanDefinition(MapTilePropertyKeys.Flammable, "可被点燃") || changed;
        }

        private bool EnsureBooleanDefinition(string key, string displayName)
        {
            MapTilePropertyDefinition existing;
            if (TryGetDefinition(key, out existing)) return false;
            Definitions.Add(new MapTilePropertyDefinition
            {
                Key = key,
                Kind = MapTilePropertyKind.Boolean,
                DisplayName = displayName,
                BoolValue = false
            });
            return true;
        }
    }
}
