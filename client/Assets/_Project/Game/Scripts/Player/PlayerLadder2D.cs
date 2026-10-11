using UnityEngine;

namespace BigWorld.Gameplay
{
    public sealed partial class PlayerController2D
    {
        public HunterLadder2D Ladder { get; private set; }
        public bool IsOnLadder => Ladder;
        public string LadderStatus { get; private set; }
        public HunterLadder2D NearbyLadder
        {
            get
            {
                var level = LevelSession2D.Instance;
                var ladder = level && level.SceneInteractions ? level.SceneInteractions.Ladder : null;
                return ladder && ladder.TryGetMount(this, out _) ? ladder : null;
            }
        }
        private float ladderGravity;
        private Vector2 ladderInput;

        public bool TryAttachLadder(HunterLadder2D ladder)
        {
            if (!CanInteract || IsProne || !IsGrounded || !ladder || !ladder.TryGetMount(this, out var point) || !TryLadderMove(point)) return false;
            Ladder = ladder; ladderGravity = body.gravityScale; body.gravityScale = 0; body.velocity = Vector2.zero;
            ClearInput(); lastGroundedTime = float.NegativeInfinity; IsGrounded = false; LadderStatus = null;
            LevelSession2D.Instance.NotifySceneInteraction("已上梯：上下攀爬，左右安全离梯，其他动作不可用。");
            return true;
        }
        public void DetachLadder(string message = "已离开梯子。")
        {
            // ReferenceEquals also handles Unity's destroyed-object null sentinel.
            if (ReferenceEquals(Ladder, null)) return;
            Ladder = null;
            if (body) { body.gravityScale = ladderGravity; body.velocity = Vector2.zero; }
            ClearInput(); lastGroundedTime = float.NegativeInfinity; LadderStatus = null;
            var level = LevelSession2D.Instance;
            if (level && level.IsGameplayActive) level.NotifySceneInteraction(message);
        }
        private void ValidateLadder()
        {
            if (ReferenceEquals(Ladder, null)) return;
            var level = LevelSession2D.Instance;
            if (!Ladder || !Ladder.Available || !level || (level.Phase != LevelPhase.Playing && level.Phase != LevelPhase.Paused) ||
                Mathf.Abs(body.position.x - Ladder.Bottom.x) > .1f || body.position.y < Ladder.Bottom.y - .05f || body.position.y > Ladder.Top.y + .05f)
                DetachLadder("攀爬条件改变，已离梯。");
        }
        private void TickLadder()
        {
            ValidateLadder(); if (!Ladder) return;
            body.velocity = Vector2.zero; IsGrounded = false;
            if (ladderInput == Vector2.zero) return;
            LadderStatus = null;
            if (Mathf.Abs(ladderInput.x) > .5f && Mathf.Abs(ladderInput.x) > Mathf.Abs(ladderInput.y))
            {
                Vector2 exit = body.position + Vector2.right * Mathf.Sign(ladderInput.x) * 1.1f;
                if (Ladder.TerrainReady(exit) && TryLadderMove(exit)) DetachLadder();
                else LadderStatus = "离梯方向受阻";
                return;
            }
            if (!Ladder.Motion.TryStep(Mathf.Clamp(body.position.y, Ladder.Bottom.y, Ladder.Top.y), ladderInput.y,
                Time.fixedDeltaTime, false, out float y, out var boundary)) return;
            if (!TryLadderMove(new Vector2(Ladder.Bottom.x, y))) { LadderStatus = "攀爬方向受阻"; return; }
            if (boundary == HunterLadderBoundary.Bottom) DetachLadder();
            else if (boundary == HunterLadderBoundary.Top)
            {
                if (TryLadderMove(Ladder.TopExit)) DetachLadder("已登上高台。");
                else LadderStatus = "上方出口受阻，可向下返回";
            }
        }
        private bool TryLadderMove(Vector2 target)
        {
            if (float.IsNaN(target.x) || float.IsNaN(target.y) || float.IsInfinity(target.x) || float.IsInfinity(target.y)) return false;
            Physics2D.SyncTransforms();
            var size = Vector2.Scale(shape.size, new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
            size = new Vector2(Mathf.Max(.001f, size.x - .02f), Mathf.Max(.001f, size.y - .02f));
            Vector2 offset = (Vector2)transform.TransformPoint(shape.offset) - (Vector2)transform.position;
            int layers = Physics2D.GetLayerCollisionMask(gameObject.layer);
            foreach (var collider in Physics2D.OverlapBoxAll(target + offset, size, transform.eulerAngles.z, layers))
                if (LadderObstacle(collider)) return false;
            Vector2 delta = target - body.position;
            if (delta.sqrMagnitude > .000001f)
                foreach (var hit in Physics2D.BoxCastAll(body.position + offset, size, transform.eulerAngles.z, delta.normalized, delta.magnitude, layers))
                    if (LadderObstacle(hit.collider) && hit.distance < delta.magnitude - .001f && Vector2.Dot(hit.normal, delta.normalized) < -.001f) return false;
            body.position = target; transform.position = new Vector3(target.x, target.y, transform.position.z);
            Physics2D.SyncTransforms(); return true;
        }
        private bool LadderObstacle(Collider2D collider) => collider && !collider.isTrigger && collider.attachedRigidbody != body &&
            !collider.transform.IsChildOf(transform) && !Physics2D.GetIgnoreCollision(shape, collider);
    }
}
