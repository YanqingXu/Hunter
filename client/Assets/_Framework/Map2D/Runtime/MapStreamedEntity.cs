using System;
using BigWorld.Pooling.Unity;
using UnityEngine;

namespace BigWorld.Map2D
{
    /// <summary>Arguments restored by the pool while the actor is still inactive.</summary>
    public readonly struct MapEntitySpawnArgs
    {
        public GridMapEntityStreamer Owner { get; }
        public MapEntityState State { get; }
        public Transform Parent { get; }
        public MapSpawnKind Kind { get; }
        public float MaxHealth { get; }
        public float RespawnSeconds { get; }
        public string EntityId => State == null ? null : State.Id;
        internal int BindingVersion { get; }

        internal MapEntitySpawnArgs(GridMapEntityStreamer owner, MapEntityState state, Transform parent,
            MapSpawnKind kind, float maxHealth, float respawnSeconds, int bindingVersion)
        {
            Owner = owner; State = state; Parent = parent; Kind = kind;
            MaxHealth = maxHealth; RespawnSeconds = respawnSeconds; BindingVersion = bindingVersion;
        }
    }

    /// <summary>
    /// Optional prefab-root component for a streamed actor. The pool adds it when absent.
    /// Gameplay scripts should use these state methods instead of destroying a pooled object.
    /// Awake must not overwrite the state restored before OnEnable.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BigWorld/Map Streamed Entity")]
    public sealed class MapStreamedEntity : MonoBehaviour, IPooledLifecycle, IPooledInitializer<MapEntitySpawnArgs>
    {
        [SerializeField, Min(.01f)] private float defaultMaxHealth = 100f;
        private GridMapEntityStreamer owner;
        private Transform mapTransform;
        private MapEntityState state;
        private int bindingVersion;
        private float maxHealth, respawnSeconds;

        public string EntityId => state == null ? null : state.Id;
        public MapEntityState State => state;
        public MapSpawnKind Kind { get; private set; }
        public float Health => state == null ? DefaultMaxHealth : state.Health;
        public float MaxHealth => state == null ? DefaultMaxHealth : maxHealth;
        public bool Opened => state != null && state.Opened;
        public bool Ignited => state != null && state.Ignited;
        public bool Destroyed => state != null && state.Destroyed;
        /// <summary>Set while combat or another interaction must retain this actor at any distance.</summary>
        public bool CombatPinned { get; set; }
        public float DefaultMaxHealth
        {
            get => Finite(defaultMaxHealth) ? Mathf.Max(.01f, defaultMaxHealth) : 100f;
            set => defaultMaxHealth = Finite(value) ? Mathf.Max(.01f, value) : 100f;
        }

        public void InitializeOnce() { }

        public void InitializeForUse(MapEntitySpawnArgs args)
        {
            ReleaseBinding(false);
            // Only the prefab root owns the stable actor identity. Child components are inert.
            if (transform.parent != args.Parent || args.Owner == null || args.State == null) return;
            owner = args.Owner; mapTransform = args.Parent; state = args.State;
            bindingVersion = args.BindingVersion; maxHealth = args.MaxHealth;
            respawnSeconds = args.RespawnSeconds; Kind = args.Kind; CombatPinned = false;
            owner.ActorPrepared(this, bindingVersion);
        }

        public void TakeDamage(float amount)
        {
            if (!IsCurrent || Destroyed || !Finite(amount) || amount <= 0f || !owner.CanDamageEntity(state.Id, bindingVersion, state)) return;
            state.Health = Mathf.Max(0f, state.Health - amount);
            if (state.Health <= 0f) Kill();
        }

        public void Kill()
        {
            if (!IsCurrent || Destroyed || !owner.CanDamageEntity(state.Id, bindingVersion, state)) return;
            CaptureState();
            state.Health = 0f; state.Destroyed = true;
            state.RespawnAt = respawnSeconds > 0f ? Time.timeAsDouble + respawnSeconds : 0d;
            CombatPinned = false;
            // Cell deletion synchronously returns this actor and clears this binding.
            owner.EntityKilled(state.Id, bindingVersion, state);
        }

        public void MarkOpened() { if (IsCurrent && !Destroyed) state.Opened = true; }
        public void Ignite() { if (IsCurrent && !Destroyed) owner.IgniteEntity(state.Id, bindingVersion, state); }

        /// <summary>Save position relative to the renderer; flags and health already share session state.</summary>
        public void CaptureState()
        {
            if (!IsCurrent || mapTransform == null) return;
            Vector3 position = mapTransform.InverseTransformPoint(transform.position);
            if (Finite(position.x) && Finite(position.y) && Finite(position.z)) state.LocalPosition = position;
        }

        public void OnRelease() { ReleaseBinding(true); }

        internal void ReleaseBinding(bool captureState)
        {
            if (captureState) CaptureState();
            owner = null; mapTransform = null; state = null;
            bindingVersion = 0; maxHealth = 0f; respawnSeconds = 0f; CombatPinned = false;
        }

        private bool IsCurrent => owner != null && state != null && owner.IsBindingCurrent(state.Id, bindingVersion, state);
        private void OnDisable() { CaptureState(); }
        private void OnDestroy() { if (IsCurrent) Kill(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
