using BigWorld.YouYou2D;
using SkillEditorKit;
using UnityEngine;

namespace BigWorld.Gameplay
{
    public enum PlayerMotionState { Idle, Run, Rise, Fall, Hurt, Dead }

    /// <summary>Physics locomotion for the playable level. Input can also come from a test or input adapter.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SkillActor2D))]
    [RequireComponent(typeof(SkillFacing2D))]
    [AddComponentMenu("BigWorld/Game/Player Controller 2D")]
    public sealed partial class PlayerController2D : MonoBehaviour
    {
        public WorldSession2D World;
        public SkillClip Shoot, Melee;
        public bool ReadKeyboard = true;
        private readonly HunterAim aim = new HunterAim();
        // Mirroring through the character's facing also preserves existing input adapters that set SkillFacing2D directly.
        public Vector2 AimDirection
        {
            get
            {
                float x = Mathf.Abs(aim.X) * (facing ? facing.Direction : aim.Facing), y = aim.Y;
                if (IsProne) HunterPostureRules.ProjectProneAim(x, y, facing ? facing.Direction : aim.Facing, out x, out y);
                return new Vector2(x, y);
            }
        }
        public float PostureHeightMultiplier => IsProne ? HunterPostureRules.ProneHeightMultiplier : 1;
        public Vector2 AimOrigin => (Vector2)transform.position + Vector2.up * (1.1f * PostureHeightMultiplier);
        public bool IsProne { get; private set; }
        public string PostureStatus { get; private set; }
        [Header("枪械与弹药")]
        [Min(1)] public int WeaponWeightLimit = 3;
        public HunterGunDefinition[] Guns = {
            new HunterGunDefinition { DefaultRound = HunterRoundDefinition.Rifle() },
            new HunterGunDefinition { Name = "手枪", Weight = 1, MagazineCapacity = 8, ReloadSeconds = 1.2f, FireInterval = .35f, Damage = 15, MeleeDamage = 15,
                DefaultRound = HunterRoundDefinition.Legacy(15) }
        };
        public HunterAmmoDefinition[] Ammunition = { new HunterAmmoDefinition() };
        public HunterWeapons Weapons { get; private set; }
        public string WeaponStatus { get; private set; }
        public bool CanInteract => CanAct() && !IsOnLadder && !IsHurt && !IsUsingItem && !IsSceneInteracting && Weapons != null && !Weapons.IsReloading && (!actor.Player || !actor.Player.IsPlaying);
        public SceneInteraction2D SceneInteraction { get; private set; }
        public bool IsSceneInteracting => SceneInteraction;
        public SceneInteraction2D NearbySceneInteraction => LevelSession2D.Instance && LevelSession2D.Instance.SceneInteractions
            ? LevelSession2D.Instance.SceneInteractions.FindNearest(this) : null;
        private Vector2 directionalInput;
        private System.Collections.Generic.List<HunterItemStack> items = new System.Collections.Generic.List<HunterItemStack>();
        public System.Collections.Generic.List<HunterItemStack> Items => HunterItems.Copy(items);
        public int ItemIndex { get; private set; }
        public bool IsUsingItem => pendingItem != null;
        public float ItemUseProgress => pendingItem == null ? 0 : Mathf.Clamp01(itemElapsed / pendingItem.Delay);
        public string ItemStatus { get; private set; }
        public float ItemMoveMultiplier => IsUsingItem ? HunterItems.UseMoveMultiplier : 1;
        public float ThrowRangeMultiplier { get; private set; } = 1;
        private HunterItemDefinition pendingItem;
        private Vector2 pendingItemDirection;
        private float pendingItemHeight = 1;
        private float itemElapsed;

        [Header("移动")]
        [Min(0)] public float MoveSpeed = 6;
        [Min(0)] public float WalkSpeed = 3;
        [Header("猎人体力与分段回血")]
        [Min(1)] public float MaxStamina = 100;
        [Min(0)] public float StaminaRecoveryDelay = 2;
        [Min(0)] public float StaminaRecoveryRate = 20;
        [Min(0)] public float MeleeStaminaCost = 25;
        public float EffectiveMeleeStaminaCost => MeleeStaminaCost * meleeStaminaMultiplier;
        private float meleeStaminaMultiplier = 1;
        [Min(0)] public float HealthRecoveryDelay = 6;
        [Min(0)] public float HealthRecoveryRate = 5;
        public float[] HealthSegments = { 25, 25, 25, 25 };
        public float Stamina => stamina != null ? stamina.Current : MaxStamina;
        public bool IsSprinting => !IsProne && sprintHeld && Stamina > 0 && Mathf.Abs(movement) > .001f && CanAct() && !IsHurt;
        public float HealthRecoveryCeiling => actor && HunterHealthSegments.IsValid(actor.MaxHealth, HealthSegments)
            ? HunterHealthSegments.RecoveryCeiling(actor.Health, actor.MaxHealth, HealthSegments) : (actor ? actor.Health : 0);
        [Min(0)] public float GroundAcceleration = 60;
        [Min(0)] public float GroundDeceleration = 75;
        [Min(0)] public float AirAcceleration = 30;
        [Min(0)] public float AirDeceleration = 20;

        [Header("跳跃")]
        [Min(0)] public float JumpSpeed = 10;
        [Min(0)] public float CoyoteTime = .12f;
        [Min(0)] public float JumpBufferTime = .12f;
        [Range(.05f, 1)] public float JumpReleaseMultiplier = .45f;
        public LayerMask GroundLayers = 1;
        [Min(.005f)] public float GroundProbeDistance = .08f;

        [Header("受伤")]
        [Min(0)] public float HurtDuration = .18f;
        [Min(0)] public float KnockbackSpeed = 5;
        [Min(0)] public float KnockbackLift = 2.5f;

        public bool ControlEnabled { get; private set; } = true;
        public bool IsGrounded { get; private set; }
        public bool IsHurt => actor && actor.IsAlive && Time.time < hurtUntil;
        public PlayerMotionState State { get; private set; } = PlayerMotionState.Idle;

        private Rigidbody2D body;
        private BoxCollider2D shape;
        private Vector2 standingSize, standingOffset;
        private bool postureCached;
        private SkillActor2D actor;
        private SkillFacing2D facing;
        private HunterStamina stamina;
        private bool sprintHeld, meleeActive;
        private float lastCombatTime = float.NegativeInfinity;
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[16];
        private float movement;
        private bool jumpHeld;
        private bool canCutJump;
        private float jumpBufferedUntil = float.NegativeInfinity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float hurtUntil = float.NegativeInfinity;

        private void Awake() => CacheComponents();

        private void OnEnable()
        {
            CacheComponents();
            actor.Damaged += OnDamaged;
            actor.Died += OnDied;
        }

        private void OnDisable()
        {
            DetachLadder();
            CancelSceneInteraction("角色停用，交互已取消。");
            if (actor)
            {
                actor.Damaged -= OnDamaged;
                actor.Died -= OnDied;
            }
            ClearInput();
            Weapons?.CancelReload();
            CancelItemUse();
            aim.Reset(facing ? facing.Direction : 1);
        }

        private void CacheComponents()
        {
            if (!body) body = GetComponent<Rigidbody2D>();
            if (!shape) shape = GetComponent<BoxCollider2D>();
            if (!postureCached && shape) { standingSize = shape.size; standingOffset = shape.offset; postureCached = true; }
            if (!actor) actor = GetComponent<SkillActor2D>();
            if (!facing) facing = GetComponent<SkillFacing2D>();
            if (stamina == null) stamina = new HunterStamina(MaxStamina, StaminaRecoveryDelay, StaminaRecoveryRate);
            if (Weapons == null) Weapons = new HunterWeapons(Guns, Ammunition, WeaponWeightLimit);
        }

        private bool CanAct() => isActiveAndEnabled && ControlEnabled && World && World.IsReady &&
            (!LevelSession2D.Instance || !LevelSession2D.Instance.Hunt || !LevelSession2D.Instance.Hunt.IsSpecialView) &&
            body && body.simulated && actor && actor.IsAlive && Time.timeScale > 0 &&
            (!GameServices2D.Instance || !GameServices2D.Instance.IsPaused);

        private void Update()
        {
            var level = LevelSession2D.Instance;
            if (IsUsingItem && level && level.Phase != LevelPhase.Playing && level.Phase != LevelPhase.Paused) CancelItemUse();
            if (!CanAct() || IsHurt)
            {
                ClearInput();
                UpdateState();
                return;
            }

            if (ReadKeyboard)
            {
                float horizontal = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                                   (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                float vertical = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) -
                                 (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                SetDirectionalInput(new Vector2(horizontal, vertical), Input.GetKeyDown(KeyCode.Space), Input.GetKey(KeyCode.Space));
                SetSprint(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                if (Input.GetKeyDown(KeyCode.C)) TrySetProne(!IsProne);
                if (Input.GetKeyDown(KeyCode.Alpha1)) TrySwitchWeapon(0);
                if (Input.GetKeyDown(KeyCode.Alpha2)) TrySwitchWeapon(1);
                if (Input.GetKeyDown(KeyCode.R)) TryReload();
                if (Input.GetKeyDown(KeyCode.E)) TryInteract();
                if (Input.GetKey(KeyCode.J)) TryShoot();
                if (Input.GetKeyDown(KeyCode.K)) TryMelee();
                if (Input.GetKeyDown(KeyCode.Q)) TrySelectItem(ItemIndex + 1);
                if (Input.GetKeyDown(KeyCode.F)) TryUseItem();
                if (Input.GetKeyDown(KeyCode.G)) TryUseOnceSkill();
            }
            TickItemUse();
            TickResources();
            Weapons.Tick(Time.deltaTime);
            UpdateState();
        }

        public void SetSprint(bool held)
        {
            if (IsOnLadder) { sprintHeld = false; return; }
            if (held) InterruptSceneInteraction();
            sprintHeld = held && !IsProne && CanAct() && !IsHurt && Stamina > (sprintHeld ? 0 : 1);
        }
        public void CancelInput() { ClearInput(); }

        private void LateUpdate() { ValidateLadder(); if (SceneInteraction) SceneInteraction.Advance(this, Time.deltaTime); }
        private void OnApplicationFocus(bool focused) { if (!focused) { ClearInput(); CancelSceneInteraction("失去焦点，交互已取消。"); } }
        private void OnApplicationPause(bool paused) { if (paused) { ClearInput(); CancelSceneInteraction("应用挂起，交互已取消。"); } }
        public bool TryBeginSceneInteraction(SceneInteraction2D target)
        {
            if (!target || !CanInteract || directionalInput != Vector2.zero || jumpHeld || sprintHeld || !target.Begin(this)) return false;
            SceneInteraction = target; ClearInput(); body.velocity = new Vector2(0, body.velocity.y);
            LevelSession2D.Instance.NotifySceneInteraction(target.DelaySeconds > 0 ? "正在" + target.DisplayName + "，移动或其他操作会中断。" : target.DisplayName);
            target.Advance(this, 0); return true;
        }
        public void CancelSceneInteraction(string message = "操作中断，交互已取消。")
        {
            var target = SceneInteraction; SceneInteraction = null;
            if (!target) return;
            target.Cancel(this);
            var level = LevelSession2D.Instance;
            if (level && level.Phase == LevelPhase.Playing) level.NotifySceneInteraction(message);
        }
        private void InterruptSceneInteraction() { if (IsSceneInteracting && CanAct() && !IsHurt) CancelSceneInteraction(); }
        internal void CompleteSceneInteraction(SceneInteraction2D target)
        {
            if (SceneInteraction != target) return;
            SceneInteraction = null;
            LevelSession2D.Instance.NotifySceneInteraction("弹药柜已开启，可交互补充弹药。");
        }
        private static bool DirectionIntent(Vector2 input)
        {
            return !float.IsNaN(input.x) && !float.IsNaN(input.y) && !float.IsInfinity(input.x) && !float.IsInfinity(input.y) &&
                (double)input.x * input.x + (double)input.y * input.y > (double).15f * .15f;
        }

        public bool TrySetProne(bool prone)
        {
            InterruptSceneInteraction();
            if (!CanInteract || !IsGrounded) return false;
            if (prone == IsProne) return true;
            if (!prone && !HasStandingSpace()) { PostureStatus = "上方受阻，保持匍匐"; return false; }
            ApplyPosture(prone);
            return true;
        }

        private bool HasStandingSpace()
        {
            Physics2D.SyncTransforms();
            var scale = transform.lossyScale;
            var size = new Vector2(Mathf.Abs(standingSize.x * scale.x), Mathf.Abs(standingSize.y * scale.y));
            // A small inset excludes resting floor contact without ignoring solid ceilings or other actors.
            size = new Vector2(Mathf.Max(.001f, size.x - .02f), Mathf.Max(.001f, size.y - .02f));
            foreach (var overlap in Physics2D.OverlapBoxAll(transform.TransformPoint(standingOffset), size,
                transform.eulerAngles.z, Physics2D.GetLayerCollisionMask(gameObject.layer)))
            {
                if (!overlap || overlap.isTrigger || overlap.attachedRigidbody == body || overlap.transform.IsChildOf(transform) ||
                    Physics2D.GetIgnoreCollision(shape, overlap)) continue;
                return false;
            }
            return true;
        }

        private void ApplyPosture(bool prone)
        {
            IsProne = prone; PostureStatus = null; sprintHeld = false;
            jumpBufferedUntil = float.NegativeInfinity; canCutJump = false;
            shape.size = new Vector2(standingSize.x, standingSize.y * PostureHeightMultiplier);
            shape.offset = standingOffset - Vector2.up * ((standingSize.y - shape.size.y) * .5f);
            if (prone) { var direction = AimDirection; aim.TrySet(direction.x, direction.y); }
            Physics2D.SyncTransforms();
        }
        public void NotifyCombat() { lastCombatTime = Time.time; }

        private void TickResources()
        {
            if (!CanAct()) return;
            bool meleePlaying = meleeActive && actor.Player && actor.Player.IsPlaying;
            if (!meleePlaying) meleeActive = false;
            stamina.Tick(Time.deltaTime, IsSprinting || meleePlaying);
            if (Time.time - lastCombatTime < Mathf.Max(0, HealthRecoveryDelay)) return;
            float amount = Mathf.Min(HealthRecoveryCeiling - actor.Health, Mathf.Max(0, HealthRecoveryRate) * Time.deltaTime);
            actor.Heal(amount);
        }

        /// <summary>jumpPressed is an edge; jumpHeld is the current button state. Paused input is discarded.</summary>
        public void SetInput(float horizontal, bool jumpPressed, bool jumpHeld)
        { SetDirectionalInput(new Vector2(horizontal, 0), jumpPressed, jumpHeld); }

        /// <summary>One stick controls horizontal movement and two-dimensional aim; jump remains a separate button.</summary>
        public void SetDirectionalInput(Vector2 input, bool jumpPressed, bool jumpHeld)
        {
            if (!CanAct() || IsHurt) { ClearInput(); return; }
            if (IsOnLadder) { ladderInput = DirectionIntent(input) ? Vector2.ClampMagnitude(input, 1) : Vector2.zero; return; }
            if (jumpPressed || DirectionIntent(input)) InterruptSceneInteraction();
            movement = SetAimInput(input) ? Mathf.Clamp(input.x, -1, 1) : 0;
            directionalInput = DirectionIntent(input) ? input : Vector2.zero;
            this.jumpHeld = jumpHeld;
            if (jumpPressed && !IsUsingItem && (!IsProne || TrySetProne(false)))
                jumpBufferedUntil = Time.time + Mathf.Max(0, JumpBufferTime);
        }

        public bool SetAimInput(Vector2 input)
        {
            if (IsOnLadder) return false;
            if (DirectionIntent(input)) InterruptSceneInteraction();
            if (!CanAct() || IsHurt || !aim.TrySet(input.x, input.y)) return false;
            if (input.x != 0 && facing) facing.SetFacing(aim.Facing);
            if (IsProne) { var direction = AimDirection; aim.TrySet(direction.x, direction.y); }
            return true;
        }

        public bool TryShoot()
        {
            if (IsOnLadder) return false;
            InterruptSceneInteraction();
            if (!CanAct() || IsHurt || IsUsingItem || !Weapons.CanShoot ||
                !actor.TryPlay(Shoot, 0, Weapons.Damage, projectileDirection2D: AimDirection,
                    projectileHeightMultiplier2D: PostureHeightMultiplier, projectileSettings: new HunterShotSettings(Weapons.Round))) return false;
            Weapons.TryFire();
            WeaponStatus = null;
            NotifyCombat(); return true;
        }
        public bool TryMelee()
        {
            if (IsOnLadder) return false;
            InterruptSceneInteraction();
            if (!CanAct() || IsHurt || IsUsingItem || Weapons.IsReloading || !stamina.CanSpend(EffectiveMeleeStaminaCost) || !actor.TryPlay(Melee, .5f, Weapons.MeleeDamage)) return false;
            stamina.TrySpend(EffectiveMeleeStaminaCost);
            meleeActive = true;
            NotifyCombat(); return true;
        }

        public bool TryReload()
        {
            if (IsOnLadder) return false;
            InterruptSceneInteraction();
            if (!CanAct() || IsHurt || IsUsingItem || (actor.Player && actor.Player.IsPlaying) || !Weapons.TryReload()) return false;
            WeaponStatus = "正在换弹"; return true;
        }

        public bool TrySwitchWeapon(int index)
        {
            if (IsOnLadder) return false;
            InterruptSceneInteraction();
            if (!CanAct() || IsHurt || IsUsingItem || (actor.Player && actor.Player.IsPlaying) || !Weapons.TrySwitch(index)) return false;
            WeaponStatus = null; return true;
        }

        public bool TryUseNearbySupply()
        {
            InterruptSceneInteraction();
            if (!CanInteract) return false;
            var supply = AmmoSupply2D.FindNearest(this);
            return supply && supply.TryUse(this);
        }

        public bool TryInteract()
        {
            if (IsSceneInteracting)
            {
                if (!CanAct() || IsHurt) return false;
                CancelSceneInteraction(); return true;
            }
            if (!CanInteract) return false;
            var level = LevelSession2D.Instance;
            if (level && level.Hunt && level.Hunt.TryInteract()) return true;
            var nearby = FindNearbyLoot();
            if (nearby is HunterLadder2D ladder) return TryAttachLadder(ladder);
            if (nearby is SceneInteraction2D scene) return TryBeginSceneInteraction(scene);
            if (nearby is SkillPickup2D skill) return skill.TryCollect(this);
            if (nearby is ItemSupplyPoint2D item) return item.TryCollect(this);
            if (nearby is AmmoSupply2D ammo) return ammo.TryUse(this);
            return false;
        }

        public string NearbyLootHint()
        {
            var nearby = FindNearbyLoot(); var level = LevelSession2D.Instance;
            var hunter = level && level.Career != null ? level.Career.Selected : null;
            if (nearby is SkillPickup2D skill) return skill.Hint(hunter);
            if (nearby is ItemSupplyPoint2D item) return item.Hint(hunter);
            if (nearby is AmmoSupply2D ammo) return ammo.Used ? "弹药箱已用尽" : Weapons.CanRefill ? "E 补充弹药（本局一次）" : "弹药已满，补给箱保留";
            if (nearby is SceneInteraction2D scene) return scene.Hint;
            if (nearby is HunterLadder2D) return IsProne ? "请先起身再攀爬" : "E 攀爬梯子";
            return "";
        }
        private Component FindNearbyLoot()
        {
            var level = LevelSession2D.Instance;
            var skill = level && level.SkillLoot ? level.SkillLoot.FindNearest(this) : null;
            var item = level && level.ItemLoot ? level.ItemLoot.FindNearest(this) : null;
            var ammo = AmmoSupply2D.FindNearest(this);
            var scene = NearbySceneInteraction;
            Component nearest = null; float score = float.PositiveInfinity;
            ConsiderLoot(skill, skill && skill.Used, ref nearest, ref score);
            ConsiderLoot(item, item && item.Used, ref nearest, ref score);
            ConsiderLoot(ammo, ammo && ammo.Used, ref nearest, ref score);
            ConsiderLoot(scene, false, ref nearest, ref score);
            ConsiderLoot(NearbyLadder, false, ref nearest, ref score);
            return nearest;
        }
        private void ConsiderLoot(Component candidate, bool used, ref Component nearest, ref float score)
        {
            if (!candidate) return;
            float value = ((Vector2)(candidate.transform.position - transform.position)).sqrMagnitude + (used ? 10000 : 0);
            if (candidate is HunterLadder2D ladder && ladder.TryGetMount(this, out var mount))
                value = (mount - (Vector2)transform.position).sqrMagnitude;
            if (value < score) { nearest = candidate; score = value; }
        }

        public void ResetLoadout()
        {
            CacheComponents();
            Weapons = new HunterWeapons(Guns, Ammunition, WeaponWeightLimit);
            meleeStaminaMultiplier = 1; ThrowRangeMultiplier = 1; WeaponStatus = null;
            ApplyItems(new System.Collections.Generic.List<HunterItemStack>()); ItemStatus = null;
        }

        public void EquipHunter(HunterRecord hunter)
        {
            if (hunter == null || hunter.Weight == 0) throw new System.ArgumentException("请先为猎人装备武器。");
            if (Guns == null || Guns.Length < 2) throw new System.ArgumentException("缺少步枪或手枪配置。");
            CacheComponents();
            if (!HunterHealthLayout.IsAllowed(actor.MaxHealth, hunter.HealthSegments))
                throw new System.ArgumentException("角色固定生命上限与保存的血段配置不一致。");
            var selected = new System.Collections.Generic.List<HunterGunDefinition>();
            var effects = new HunterSkillEffects(hunter.Skills);
            if (hunter.Rifle) selected.Add(Guns[0]);
            if (hunter.Pistol) selected.Add(Guns[1]);
            if (!HunterTemplates.CanCarryWeapons(hunter.TemplateId, hunter.Weight) ||
                !HunterTemplates.CanCarrySkills(hunter.TemplateId, hunter.CarriedSkillValue))
                throw new System.ArgumentException("猎人模板或携带容量不合法。");
            Weapons = new HunterWeapons(selected.ToArray(), Ammunition, hunter.Template.WeaponWeightLimit)
                .WithTimingMultipliers(effects.FireIntervalMultiplier, effects.ReloadMultiplier);
            meleeStaminaMultiplier = effects.MeleeStaminaMultiplier;
            ThrowRangeMultiplier = effects.ThrowRangeMultiplier;
            WeaponStatus = null;
            ApplyItems(hunter.Items); ItemStatus = null;
            HealthSegments = (float[])hunter.HealthSegments.Clone();
        }

        internal void ApplyItems(System.Collections.Generic.List<HunterItemStack> value)
        { items = HunterItems.Copy(value); ItemIndex = Mathf.Clamp(ItemIndex, 0, Mathf.Max(0, items.Count - 1)); CancelItemUse(); }

        public bool TrySelectItem(int index)
        {
            InterruptSceneInteraction();
            if (!CanInteract || items.Count == 0 || index < 0) return false;
            ItemIndex = index % items.Count; ItemStatus = null; return true;
        }

        public bool TryUseItem()
        {
            InterruptSceneInteraction();
            var level = LevelSession2D.Instance;
            if (!CanInteract || !level || level.TrainingMode || !level.IsGameplayActive || ItemIndex >= items.Count) return false;
            var stack = items[ItemIndex]; var item = HunterItems.Find(stack.Id);
            if (stack.Uses <= 0) { ItemStatus = item.Name + "次数已用尽"; return false; }
            if (item.Kind == HunterItemKind.Medical && actor.Health >= actor.MaxHealth) { ItemStatus = "生命已满，道具保留"; return false; }
            if (item.Kind == HunterItemKind.Melee)
            {
                if (!stamina.CanSpend(item.StaminaCost) || !actor.TryPlay(Melee, .5f, item.Value, true)) return false;
                stamina.TrySpend(item.StaminaCost); meleeActive = true; NotifyCombat(); ItemStatus = "匕首攻击"; return true;
            }
            // Snapshot the base catalogue at preparation start; later skill changes cannot alter an in-flight item.
            pendingItem = item.WithThrowRangeMultiplier(ThrowRangeMultiplier); itemElapsed = 0; jumpBufferedUntil = float.NegativeInfinity;
            pendingItemDirection = AimDirection; pendingItemHeight = PostureHeightMultiplier;
            ItemStatus = "正在使用" + item.Name; return true;
        }

        private void TickItemUse()
        {
            if (pendingItem == null || !CanAct() || IsHurt) return;
            itemElapsed += Time.deltaTime;
            if (itemElapsed < pendingItem.Delay) return;
            var item = pendingItem; int index = ItemIndex;
            Vector2 direction = pendingItemDirection; float height = pendingItemHeight;
            CancelItemUse();
            if (item.Kind == HunterItemKind.Medical && actor.Health >= actor.MaxHealth) { ItemStatus = "生命已满，道具保留"; return; }
            // Prepare the projectile while inactive; publish its effect only after the consumed item is durable.
            HunterThrowable2D projectile = null;
            if (item.Kind == HunterItemKind.Throwable)
                projectile = HunterThrowable2D.Prepare(this, item, direction, height);
            if (!LevelSession2D.Instance.TryConsumeItem(index, item.Id))
            {
                if (projectile) Destroy(projectile.gameObject);
                ItemStatus = "道具保存失败，按 F 重试"; return;
            }
            if (item.Kind == HunterItemKind.Medical) actor.Heal(item.Value);
            else { projectile.gameObject.SetActive(true); NotifyCombat(); }
            ItemStatus = "已使用" + item.Name;
        }

        private void CancelItemUse()
        {
            if (pendingItem != null && ItemStatus == "正在使用" + pendingItem.Name)
                ItemStatus = "使用已取消，次数保留";
            pendingItem = null; itemElapsed = 0;
            pendingItemDirection = Vector2.zero; pendingItemHeight = 1;
        }

        public bool TryUseOnceSkill()
        {
            InterruptSceneInteraction();
            var level = LevelSession2D.Instance;
            return CanInteract && level && level.TryUseOnceSkill(HunterSkillId.EmergencyBandage);
        }

        internal HunterWeapons PrepareSkillUpdate(HunterSkillEffects effects)
        { return Weapons.WithTimingMultipliers(effects.FireIntervalMultiplier, effects.ReloadMultiplier); }

        internal void ApplySkillUpdate(HunterWeapons prepared, HunterSkillEffects effects)
        {
            Weapons = prepared; meleeStaminaMultiplier = effects.MeleeStaminaMultiplier; ThrowRangeMultiplier = effects.ThrowRangeMultiplier;
        }

        public void SetControlEnabled(bool enabled)
        {
            CacheComponents();
            if (!enabled && (!LevelSession2D.Instance || LevelSession2D.Instance.Phase != LevelPhase.Paused)) CancelSceneInteraction("行动结束，交互已取消。");
            if (!enabled && (!LevelSession2D.Instance || LevelSession2D.Instance.Phase != LevelPhase.Paused)) DetachLadder();
            ControlEnabled = enabled;
            ClearInput();
            if (!enabled && body) body.velocity = new Vector2(0, body.velocity.y);
            UpdateState();
        }

        /// <summary>The level owns health, simulation, and control unlock; this only resets locomotion and pose.</summary>
        public void ResetForRespawn(Vector2 position)
        { ResetPoseAt(position, true); }

        internal void ReviveAt(Vector2 position)
        { ResetPoseAt(position, false); }

        // Streaming can finish after ResetPoseAt. Recheck before the level resumes physics at the destination.
        internal void ResolveSpawnPosture()
        {
            CacheComponents();
            ApplyPosture(!HasStandingSpace());
            var visual = GetComponent<PlayerVisual2D>();
            if (visual) visual.ResetPose();
        }

        private void ResetPoseAt(Vector2 position, bool restoreStamina)
        {
            DetachLadder();
            CancelSceneInteraction("位置重置，交互已取消。");
            CacheComponents();
            ClearInput();
            lastGroundedTime = hurtUntil = float.NegativeInfinity;
            if (restoreStamina) stamina.Reset();
            Weapons.CancelReload();
            WeaponStatus = null;
            meleeActive = false;
            CancelItemUse();
            aim.Reset();
            lastCombatTime = Time.time;
            IsGrounded = false;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0;
            body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Physics2D.SyncTransforms();
            // Respawning in a low space must not force the standing collider into a ceiling.
            if (facing) facing.SetFacing(1);
            ResolveSpawnPosture();
            UpdateState();
        }

        private void FixedUpdate()
        {
            IsGrounded = ProbeGround();
            if (!CanAct())
            {
                ClearInput();
                lastGroundedTime = float.NegativeInfinity;
                body.velocity = actor && actor.IsAlive ? new Vector2(0, body.velocity.y) : Vector2.zero;
                UpdateState();
                return;
            }

            if (IsHurt)
            {
                // Preserve the hit impulse throughout hit stun instead of overwriting it with movement.
                ClearInput();
                lastGroundedTime = float.NegativeInfinity;
                UpdateState();
                return;
            }

            if (IsSceneInteracting)
            {
                body.velocity = new Vector2(0, body.velocity.y);
                UpdateState(); return;
            }

            if (IsOnLadder) { TickLadder(); UpdateState(); return; }

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                canCutJump = false;
            }

            float acceleration = Mathf.Abs(movement) > .001f
                ? (IsGrounded ? GroundAcceleration : AirAcceleration)
                : (IsGrounded ? GroundDeceleration : AirDeceleration);
            float speed = (IsSprinting ? MoveSpeed : WalkSpeed) * ItemMoveMultiplier * (IsProne ? HunterPostureRules.ProneMoveMultiplier : 1);
            body.velocity = new Vector2(Mathf.MoveTowards(body.velocity.x, movement * Mathf.Max(0, speed),
                Mathf.Max(0, acceleration) * Time.fixedDeltaTime), body.velocity.y);

            if (Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= Mathf.Max(0, CoyoteTime))
            {
                body.velocity = new Vector2(body.velocity.x, Mathf.Max(0, JumpSpeed));
                jumpBufferedUntil = lastGroundedTime = float.NegativeInfinity;
                IsGrounded = false;
                canCutJump = true;
                stamina.InterruptRecovery();
            }
            if (canCutJump && !jumpHeld && body.velocity.y > 0)
            {
                body.velocity = new Vector2(body.velocity.x, body.velocity.y * Mathf.Clamp01(JumpReleaseMultiplier));
                canCutJump = false;
            }
            if (body.velocity.y <= 0) canCutJump = false;
            UpdateState();
        }

        private bool ProbeGround()
        {
            if (!body || !body.simulated || !shape || !shape.enabled || body.velocity.y > .1f) return false;
            var filter = new ContactFilter2D();
            filter.SetLayerMask(GroundLayers);
            filter.useTriggers = false;
            int count = shape.Cast(Vector2.down, filter, groundHits, Mathf.Max(.005f, GroundProbeDistance));
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!hit.collider || hit.rigidbody == body || hit.normal.y < .65f) continue;
                // Enemy bodies, hurtboxes, and other players are not platforms, even on the terrain layer.
                if (hit.collider.GetComponentInParent<SkillActor2D>()) continue;
                return true;
            }
            return false;
        }

        private void ClearInput()
        {
            ladderInput = Vector2.zero;
            directionalInput = Vector2.zero;
            movement = 0;
            sprintHeld = false;
            jumpHeld = false;
            canCutJump = false;
            jumpBufferedUntil = float.NegativeInfinity;
        }

        private void OnDamaged(SkillHitData hit)
        {
            DetachLadder("受伤离开梯子。");
            CancelSceneInteraction("受伤打断场景交互。");
            if (IsUsingItem) ItemStatus = "受伤打断道具使用，次数保留";
            CancelItemUse();
            Weapons.CancelReload();
            WeaponStatus = "受伤打断换弹";
            NotifyCombat();
            ClearInput();
            if (!actor.IsAlive) { OnDied(); return; }
            hurtUntil = Time.time + Mathf.Max(0, HurtDuration);
            lastGroundedTime = float.NegativeInfinity;
            IsGrounded = false;
            float sourceX = hit.soure != null && hit.soure.ModelTransform
                ? hit.soure.ModelTransform.position.x : hit.hitPoint.x;
            float direction = Mathf.Abs(transform.position.x - sourceX) > .01f
                ? Mathf.Sign(transform.position.x - sourceX) : -(facing ? facing.Direction : 1);
            body.velocity = new Vector2(direction * Mathf.Max(0, KnockbackSpeed), Mathf.Max(body.velocity.y, KnockbackLift));
            UpdateState();
        }

        private void OnDied()
        {
            DetachLadder();
            CancelSceneInteraction("死亡中断场景交互。");
            CancelItemUse();
            Weapons.CancelReload();
            ClearInput();
            ControlEnabled = false;
            hurtUntil = float.NegativeInfinity;
            body.velocity = Vector2.zero;
            State = PlayerMotionState.Dead;
        }

        private void UpdateState()
        {
            if (!actor || !actor.IsAlive) State = PlayerMotionState.Dead;
            else if (IsHurt) State = PlayerMotionState.Hurt;
            else if (IsOnLadder) State = PlayerMotionState.Idle;
            else if (!body || !body.simulated || !ControlEnabled || !World || !World.IsReady) State = PlayerMotionState.Idle;
            else if (!IsGrounded) State = body.velocity.y > .1f ? PlayerMotionState.Rise : PlayerMotionState.Fall;
            else State = Mathf.Abs(body.velocity.x) > .1f ? PlayerMotionState.Run : PlayerMotionState.Idle;
        }
    }
}
