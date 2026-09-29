using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BigWorld.Map2D
{
    public enum MapLayer { Terrain, Objects }
    public enum MapTileCollisionMode { None, Solid, OneWay }

    /// <summary>A reusable tile definition. Game-specific behaviour belongs on the optional prefab.</summary>
    [CreateAssetMenu(fileName = "TileType", menuName = "BigWorld/2D Map/Tile Type")]
    public sealed class MapTileType : TileBase
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName = "新类型";
        [SerializeField] private MapLayer layer;
        [SerializeField] private Color tint = Color.white;
        [SerializeField] private Sprite sprite;
        [SerializeField] private GameObject prefab;
        [SerializeField] private MapTileCollisionMode collision;
        [SerializeField] private bool walkable = true;
        [SerializeField, Min(0.01f)] private float movementCost = 1f;
        [SerializeField, TextArea] private string description;
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeField] private MapTilePropertySchema propertySchema;
        [SerializeField] private List<MapTilePropertyValue> propertyOverrides = new List<MapTilePropertyValue>();

        private static Texture2D fallbackTexture;
        private static Sprite fallbackSprite;

        public string Id { get { return id; } set { id = value ?? string.Empty; } }
        public string DisplayName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } set { displayName = value ?? string.Empty; } }
        public MapLayer Layer { get { return layer; } set { layer = value; } }
        public Color Tint { get { return tint; } set { tint = value; } }
        public Sprite Sprite { get { return sprite; } set { sprite = value; } }
        public GameObject Prefab { get { return prefab; } set { prefab = value; } }
        public MapTileCollisionMode Collision { get { return collision; } set { collision = value; } }
        public bool Walkable { get { return walkable; } set { walkable = value; } }
        public bool BlocksMovement { get { return collision != MapTileCollisionMode.None; } }
        public float MovementCost { get { return movementCost; } set { movementCost = ValidCost(value); } }
        public string Description { get { return description; } set { description = value ?? string.Empty; } }
        public List<string> Tags { get { return tags ?? (tags = new List<string>()); } set { tags = value ?? new List<string>(); } }
        public MapTilePropertySchema PropertySchema { get { return propertySchema; } set { propertySchema = value; } }
        public List<MapTilePropertyValue> PropertyOverrides
        {
            get { return propertyOverrides ?? (propertyOverrides = new List<MapTilePropertyValue>()); }
            set { propertyOverrides = value ?? new List<MapTilePropertyValue>(); }
        }
        public bool CanBeDestroyed { get { return GetBool(MapTilePropertyKeys.Destructible); } }
        public bool CanBeIgnited { get { return GetBool(MapTilePropertyKeys.Flammable); } }

        /// <summary>
        /// Returns a shared reference to the matching override or schema default.
        /// Do not modify the returned value directly: Clone it and use SetPropertyOverride
        /// to edit this tile without changing shared schema defaults.
        /// A missing schema/definition has no effective value. Stale kinds are ignored.
        /// </summary>
        public bool TryGetProperty(string key, out MapTilePropertyValue value)
        {
            value = null;
            MapTilePropertyDefinition definition;
            if (propertySchema == null || !propertySchema.TryGetDefinition(key, out definition)) return false;
            if ((int)definition.Kind < 0 || (int)definition.Kind > (int)MapTilePropertyKind.Text) return false;
            if (propertyOverrides != null)
            {
                for (int i = propertyOverrides.Count - 1; i >= 0; i--)
                {
                    MapTilePropertyValue candidate = propertyOverrides[i];
                    if (candidate != null && candidate.Kind == definition.Kind && string.Equals(candidate.Key, key, StringComparison.Ordinal))
                    {
                        value = candidate;
                        return true;
                    }
                }
            }
            value = definition;
            return true;
        }

        /// <summary>Returns a shared effective value only when the schema declares the requested kind. Clone before editing.</summary>
        public bool TryGetProperty(string key, MapTilePropertyKind kind, out MapTilePropertyValue value)
        {
            if (TryGetProperty(key, out value) && value.Kind == kind) return true;
            value = null;
            return false;
        }

        public bool GetBool(string key, bool fallback = false)
        {
            MapTilePropertyValue value;
            return TryGetProperty(key, MapTilePropertyKind.Boolean, out value) ? value.BoolValue : fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            MapTilePropertyValue value;
            return TryGetProperty(key, MapTilePropertyKind.Integer, out value) ? value.IntValue : fallback;
        }

        public float GetNumber(string key, float fallback = 0f)
        {
            MapTilePropertyValue value;
            return TryGetProperty(key, MapTilePropertyKind.Number, out value) ? value.NumberValue : fallback;
        }

        public string GetText(string key, string fallback = "")
        {
            MapTilePropertyValue value;
            return TryGetProperty(key, MapTilePropertyKind.Text, out value) ? value.TextValue : fallback;
        }

        /// <summary>
        /// Copies and replaces all overrides for this exact key. Returns whether the list changed.
        /// Missing definitions or mismatched kinds are rejected. Does not modify the shared schema.
        /// Editor callers must record Undo and mark this tile dirty themselves.
        /// </summary>
        public bool SetPropertyOverride(MapTilePropertyValue value)
        {
            MapTilePropertyDefinition definition;
            if (value == null || propertySchema == null || !propertySchema.TryGetDefinition(value.Key, out definition) || value.Kind != definition.Kind)
                return false;
            if ((int)value.Kind < 0 || (int)value.Kind > (int)MapTilePropertyKind.Text) return false;
            int matchingCount = 0;
            MapTilePropertyValue existing = null;
            foreach (MapTilePropertyValue candidate in PropertyOverrides)
            {
                if (candidate != null && string.Equals(candidate.Key, value.Key, StringComparison.Ordinal))
                {
                    matchingCount++;
                    existing = candidate;
                }
            }
            if (matchingCount == 1 && value.SameTypedValue(existing)) return false;
            MapTilePropertyValue copy = value.Clone();
            RemovePropertyOverride(value.Key);
            propertyOverrides.Add(copy);
            return true;
        }

        /// <summary>Removes every override for an exact key, including overrides whose definition was deleted.</summary>
        public bool RemovePropertyOverride(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || propertyOverrides == null) return false;
            bool changed = false;
            for (int i = propertyOverrides.Count - 1; i >= 0; i--)
            {
                MapTilePropertyValue candidate = propertyOverrides[i];
                if (candidate == null || !string.Equals(candidate.Key, key, StringComparison.Ordinal)) continue;
                propertyOverrides.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        public bool HasTag(string tag)
        {
            return !string.IsNullOrEmpty(tag) && Tags.Contains(tag);
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = sprite != null ? sprite : GetFallbackSprite();
            tileData.color = tint;
            tileData.transform = Matrix4x4.identity;
            tileData.gameObject = prefab;
            // A scene can contain serialized editor-preview tiles before its renderer's
            // OnEnable runs. Suppress those prefabs too, not only the runtime proxy tiles.
            if (prefab != null && Application.isPlaying)
            {
                Tilemap component = tilemap == null ? null : tilemap.GetComponent<Tilemap>();
                MapGeneratedPart part = component == null ? null : component.GetComponent<MapGeneratedPart>();
                GridMapRenderer owner = part == null || part.IsRoot ? null : part.Owner;
                if (owner != null && owner.StreamingEnabled && component.transform.IsChildOf(owner.transform) &&
                    Application.IsPlaying(component.gameObject))
                    tileData.gameObject = null;
            }
            tileData.flags = TileFlags.LockAll | TileFlags.InstantiateGameObjectRuntimeOnly;
            tileData.colliderType = BlocksMovement ? Tile.ColliderType.Grid : Tile.ColliderType.None;
        }

        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
        {
            return true;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            movementCost = ValidCost(movementCost);
            if (tags == null) tags = new List<string>();
        }

        private static float ValidCost(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(0.01f, value);
        }

        private static Sprite GetFallbackSprite()
        {
            if (fallbackSprite != null) return fallbackSprite;
            if (fallbackTexture == null)
            {
                fallbackTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Map2D Shared White Texture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                fallbackTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                fallbackTexture.Apply(false, true);
            }
            fallbackSprite = UnityEngine.Sprite.Create(fallbackTexture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f, 0, SpriteMeshType.FullRect);
            fallbackSprite.name = "Map2D Shared White Sprite";
            fallbackSprite.hideFlags = HideFlags.HideAndDontSave;
            return fallbackSprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedResources()
        {
            ReleaseSharedResources();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void RegisterSharedResourceCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ReleaseSharedResources;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseSharedResources;
            EditorApplication.quitting -= ReleaseSharedResources;
            EditorApplication.quitting += ReleaseSharedResources;
        }
#endif

        private static void ReleaseSharedResources()
        {
            if (Application.isPlaying)
            {
                if (fallbackSprite != null) Destroy(fallbackSprite);
                if (fallbackTexture != null) Destroy(fallbackTexture);
            }
            else
            {
                if (fallbackSprite != null) DestroyImmediate(fallbackSprite);
                if (fallbackTexture != null) DestroyImmediate(fallbackTexture);
            }
            fallbackSprite = null;
            fallbackTexture = null;
        }
    }
}
