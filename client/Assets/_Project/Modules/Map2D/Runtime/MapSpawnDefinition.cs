using System;
using UnityEngine;

namespace BigWorld.Map2D
{
    public enum MapSpawnKind { Npc, Monster, Interactable }

    /// <summary>Authoring data only. Instances are created by the runtime entity streamer.</summary>
    [Serializable]
    public sealed class MapSpawnDefinition
    {
        [SerializeField] private string id = Guid.NewGuid().ToString("N");
        [SerializeField] private string displayName = "新生成点";
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector2Int cell;
        [SerializeField] private int count = 1;
        [SerializeField] private float spacing = 1f;
        [SerializeField] private MapSpawnKind kind = MapSpawnKind.Monster;
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float respawnSeconds;
        [SerializeField] private bool enabled = true;

        public string Id { get { return id; } set { id = value; } }
        public string DisplayName { get { return displayName; } set { displayName = value; } }
        public GameObject Prefab { get { return prefab; } set { prefab = value; } }
        public Vector2Int Cell { get { return cell; } set { cell = value; } }
        public int Count { get { return count; } set { count = Mathf.Clamp(value, 1, 128); } }
        public float Spacing { get { return spacing; } set { spacing = Finite(value) ? Mathf.Clamp(value, 0f, 128f) : 1f; } }
        public MapSpawnKind Kind { get { return kind; } set { kind = value; } }
        public float MaxHealth { get { return maxHealth; } set { maxHealth = Finite(value) ? Mathf.Max(.01f, value) : 100f; } }
        public float RespawnSeconds { get { return respawnSeconds; } set { respawnSeconds = Finite(value) ? Mathf.Max(0f, value) : 0f; } }
        public bool Enabled { get { return enabled; } set { enabled = value; } }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
            Count = count; Spacing = spacing; MaxHealth = maxHealth; RespawnSeconds = respawnSeconds;
            if (!Enum.IsDefined(typeof(MapSpawnKind), kind)) kind = MapSpawnKind.Monster;
        }

        public MapSpawnDefinition Clone(bool newIdentity = true)
        {
            var copy = (MapSpawnDefinition)MemberwiseClone();
            if (newIdentity) copy.id = Guid.NewGuid().ToString("N");
            return copy;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
