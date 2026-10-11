using System;
using UnityEngine;

namespace BigWorld.Gameplay
{
    /// <summary>Explicit eligibility marker on the logical root of a solid prop, shared by all its colliders.</summary>
    [DisallowMultipleComponent]
    public sealed class HunterPenetrableProp2D : MonoBehaviour
    {
        [Min(1)] public int Kind = 1;
        public string LogicalId { get; private set; }
        private void Awake() { RenewIdentity(); }
        // Call on reuse for a different logical prop; unloading/reloading the same prop should retain its identity.
        public void RenewIdentity() { LogicalId = Guid.NewGuid().ToString("N"); }
        internal void Show(bool visible)
        {
            foreach (var shape in GetComponentsInChildren<Collider2D>(true)) shape.enabled = visible;
            foreach (var visual in GetComponentsInChildren<SpriteRenderer>(true)) visual.enabled = visible;
        }
    }
}
