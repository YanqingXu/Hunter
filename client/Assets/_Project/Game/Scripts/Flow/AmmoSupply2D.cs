using System.Collections.Generic;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>A scene-owned, one-use supply. Full ammunition does not consume the box.</summary>
    [DisallowMultipleComponent, AddComponentMenu("BigWorld/Game/Ammo Supply 2D")]
    public sealed class AmmoSupply2D : MonoBehaviour
    {
        [Min(.1f)] public float InteractionDistance = 2;
        public LayerMask GroundLayers = 1;
        public SpriteRenderer Visual;
        public bool Used { get; private set; }
        private static readonly List<AmmoSupply2D> supplies = new List<AmmoSupply2D>();
        private readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { supplies.Clear(); }
        private void Awake() { if (!Visual) Visual = GetComponent<SpriteRenderer>(); UpdateVisual(); }
        private void OnEnable() { if (!supplies.Contains(this)) supplies.Add(this); }
        private void OnDisable() { supplies.Remove(this); }

        public static AmmoSupply2D FindNearest(PlayerController2D player)
        {
            if (!player) return null;
            AmmoSupply2D best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var supply in supplies)
            {
                if (!supply || !supply.isActiveAndEnabled || supply.gameObject.scene != player.gameObject.scene) continue;
                float distance = ((Vector2)(supply.transform.position - player.transform.position)).sqrMagnitude;
                if (distance >= bestDistance || !supply.InReach(player)) continue;
                best = supply; bestDistance = distance;
            }
            return best;
        }

        private bool InReach(PlayerController2D player)
        {
            float range = Mathf.Max(.1f, InteractionDistance);
            if (((Vector2)(transform.position - player.transform.position)).sqrMagnitude > range * range) return false;
            int count = Physics2D.LinecastNonAlloc(player.transform.position + Vector3.up * .7f, transform.position, hits, GroundLayers);
            for (int i = 0; i < count; i++)
                if (hits[i].collider && !hits[i].collider.isTrigger && !hits[i].collider.transform.IsChildOf(player.transform) &&
                    !hits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        public bool TryUse(PlayerController2D player)
        {
            if (!isActiveAndEnabled || Used || !player || gameObject.scene != player.gameObject.scene || !player.CanInteract || !InReach(player)) return false;
            if (player.Weapons.Refill() <= 0) return false;
            Used = true; UpdateVisual(); return true;
        }

        public void ResetSupply() { Used = false; UpdateVisual(); }
        private void UpdateVisual() { if (Visual) Visual.color = Used ? new Color(.3f, .35f, .4f) : new Color(.95f, .75f, .25f); }
        private void OnDrawGizmosSelected()
        { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, Mathf.Max(.1f, InteractionDistance)); }
    }
}
