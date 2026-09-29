// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public enum SkillProjectileStopReason { Manual, Lifetime, Wall, HitLimit, SkillEnded, Cancelled, Disabled, Error }

public sealed class SkillProjectileContext
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid CastId { get; }
    internal ISkillAudioService AudioService;
    public SkillClip SourceSkill { get; }
    public ISkillActor Caster { get; }
    public float AttackValue { get; }
    public string EventId => Attack.EventId;
    public PhysicsScene? QueryScene { get; internal set; }
    public PhysicsScene2D? QueryScene2D { get; internal set; }
    public SkillSpace Space { get; internal set; }
    internal readonly SkillHitLedger Hits = new SkillHitLedger();
    internal readonly SkillAttackDetectionEvent Attack;
    internal readonly Scene Scene;
    internal readonly float HitEffectLifetime;
    internal SkillProjectileContext(Guid cast, SkillClip skill, ISkillActor caster, float value, SkillProjectileEvent data, Scene scene)
    {
        CastId = cast; SourceSkill = skill; Caster = caster; AttackValue = value;
        Scene = scene; HitEffectLifetime = data.HitEffectLifetime;
        Attack = new SkillAttackDetectionEvent { EventId = data.EventId, FrameIndex = data.FrameIndex, AttackHitConfig = data.Hit, HitRules = data.HitRules };
    }
}

/// <summary>Independent, scene-owned projectile. The source SkillInstance may finish
/// or be replaced without losing damage snapshots, visuals or per-projectile hit records.</summary>
public sealed class SkillProjectile : MonoBehaviour
{
    private static readonly HashSet<SkillProjectile> active = new HashSet<SkillProjectile>();
    public static IEnumerable<SkillProjectile> Active => active;
    public SkillProjectileContext Context { get; private set; }
    public bool Alive { get; private set; }
    public SkillProjectileMotion Motion { get; private set; }
    public GameObject Visual { get; private set; }
    private SkillProjectileEvent data;
    private Transform target;
    private LayerMask targetLayers;
    private Action<ISkillHitTarget, SkillHitData> onHit;
    private Action<SkillProjectileContext, SkillProjectileStopReason, Vector3> onStop;
    private double engineTime;
    private int hitCount;
    private int rate, launchFrame;

    internal static SkillProjectile Spawn(SkillProjectileEvent configured, SkillInstance source, ISkillActor caster,
        float attackValue, Transform target, SkillAttackShape.Pose launch, LayerMask targetLayers, Scene scene)
    {
        var root = new GameObject("[技能投射物] " + (configured.Prefab == null ? "判定球" : configured.Prefab.name));
        root.SetActive(false);
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(root, scene);
        var projectile = root.AddComponent<SkillProjectile>();
        try
        {
            projectile.data = configured.Copy();
            projectile.rate = source.FrameRate; projectile.launchFrame = source.CurrentFrame;
            projectile.Context = new SkillProjectileContext(source.Id, source.Source, caster, attackValue, projectile.data, root.scene);
            projectile.Context.QueryScene = source.QueryScene;
            projectile.Context.QueryScene2D = source.QueryScene2D; projectile.Context.Space = source.Space;
            projectile.Context.AudioService = source.AudioService;
            projectile.target = target; projectile.targetLayers = targetLayers;
            projectile.onHit = source.Behaviour.OnProjectileHitTarget;
            projectile.onStop = source.Behaviour.OnProjectileStopped;
            projectile.Motion = new SkillProjectileMotion(projectile.data, source.FrameRate, launch);
            projectile.engineTime = Time.timeAsDouble; projectile.Alive = true;
            active.Add(projectile);
            // Register before prefab activation; Awake/OnEnable may interrupt the cast.
            source.ProjectileInstances.Add(projectile);
            root.transform.SetPositionAndRotation(launch.Position, launch.Rotation);
            if (configured.Prefab != null)
            {
                projectile.Visual = Instantiate(configured.Prefab, root.transform);
                PrepareVisual(projectile.Visual);
                projectile.Visual.transform.localPosition = Vector3.zero;
                projectile.Visual.transform.localRotation = Quaternion.identity;
                projectile.Visual.SetActive(true);
            }
            root.SetActive(true);
            if (!projectile.Alive) return projectile;
            if (projectile.Visual != null)
            { projectile.Visual.transform.localPosition = Vector3.zero; projectile.Visual.transform.localRotation = Quaternion.identity; }
            projectile.Sweep(launch, launch);
            return projectile;
        }
        catch { projectile.Stop(false, SkillProjectileStopReason.Error); throw; }
    }

    private static void PrepareVisual(GameObject visual)
    {
        // The prefab is a visual skin; this controller is the single movement/collision authority.
        foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) if (script != null) script.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (var body in visual.GetComponentsInChildren<Rigidbody2D>(true)) body.simulated = false;
        foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) { animator.applyRootMotion = false; animator.fireEvents = false; }
        foreach (var particle in visual.GetComponentsInChildren<ParticleSystem>(true))
        { var main = particle.main; main.stopAction = ParticleSystemStopAction.None; }
    }

    private void LateUpdate()
    {
        double now = Time.timeAsDouble;
        AdvanceBy(Math.Max(0, now - engineTime));
        engineTime = now;
    }

    public void AdvanceBy(double seconds)
    {
        if (!Alive || Motion == null) return;
        engineTime = Time.timeAsDouble;
        try
        {
            Motion.Advance(seconds, target == null ? (Vector3?)null : target.position, Sweep);
            if (!Alive) return;
            transform.SetPositionAndRotation(Motion.Pose.Position, Motion.Pose.Rotation);
            if (Visual != null)
            {
                // An Animator must not displace the visual root away from its collision pose.
                Visual.transform.localPosition = Vector3.zero; Visual.transform.localRotation = Quaternion.identity;
            }
            if (Motion.Finished) Stop(true, SkillProjectileStopReason.Lifetime);
        }
        catch (Exception ex) { Stop(false, SkillProjectileStopReason.Error); Debug.LogException(ex); }
    }

    internal void SkillFinished(SkillInstanceState state, int lastFrame)
    {
        if (!Alive) return;
        if (state == SkillInstanceState.Completed && data.OnSkillEnd == SkillProjectileEndPolicy.Destroy)
        {
            AdvanceBy(Math.Max(0, (lastFrame - launchFrame) / (double)rate - Motion.Age));
            Stop(false, SkillProjectileStopReason.SkillEnded);
        }
        else if (state == SkillInstanceState.Cancelled && data.OnSkillCancel == SkillProjectileEndPolicy.Destroy) Stop(false, SkillProjectileStopReason.Cancelled);
    }

    private struct Contact { public Component Collider; public float Distance; public Vector3 Point; }
    private bool Sweep(SkillAttackShape.Pose from, SkillAttackShape.Pose to)
    {
        if (!Alive) return false;
        var delta = to.Position - from.Position; float length = delta.magnitude;
        var contacts = new Dictionary<Component, Contact>();
        int mask = targetLayers.value | (data.StopAtWalls ? data.WallLayers.value : 0);
        if (Context.Space == SkillSpace.TwoD)
        {
            foreach (var collider in SkillPhysicsQuery2D.Circle(from.Position, data.Radius, mask, Context.QueryScene2D))
                contacts[collider] = new Contact { Collider=collider, Point=SkillCollider.ClosestPoint(collider,from.Position), Distance=0 };
            if(length > .000001f)
                foreach(var hit in SkillPhysicsQuery2D.CircleCast(from.Position,data.Radius,delta/length,length,mask,Context.QueryScene2D))
                    if(hit.collider != null && !contacts.ContainsKey(hit.collider))
                        contacts[hit.collider] = new Contact { Collider=hit.collider, Point=new Vector3(hit.point.x,hit.point.y,from.Position.z), Distance=hit.distance };
        }
        else
        {
        foreach (var collider in SkillPhysicsQuery.Sphere(from.Position, data.Radius, mask, Context.QueryScene, QueryTriggerInteraction.Collide))
            contacts[collider] = new Contact { Collider = collider, Point = collider.ClosestPoint(from.Position), Distance = 0 };
        if (length > .000001f)
            foreach (var hit in SkillPhysicsQuery.SphereCast(from.Position, data.Radius, delta / length, length, mask, Context.QueryScene, QueryTriggerInteraction.Collide))
                if (hit.collider != null && !contacts.ContainsKey(hit.collider))
                    contacts[hit.collider] = new Contact { Collider = hit.collider, Point = hit.point, Distance = hit.distance };
        }
        var sorted = new List<Contact>(contacts.Values);
        sorted.Sort((a, b) => { int c = a.Distance.CompareTo(b.Distance); return c != 0 ? c : a.Collider.GetInstanceID().CompareTo(b.Collider.GetInstanceID()); });
        foreach (var contact in sorted)
        {
            if (!Alive) return false;
            if (contact.Collider == null) continue;
            var hitTarget = contact.Collider.GetComponentInParent<ISkillHitTarget>();
            if (data.HitRules.ExcludeSelf && ReferenceEquals(hitTarget, Context.Caster)) continue;
            bool wall = data.StopAtWalls && !SkillCollider.IsTrigger(contact.Collider) &&
                (data.WallLayers.value & (1 << contact.Collider.gameObject.layer)) != 0 && hitTarget == null;
            if (wall)
            {
                transform.position = from.Position + (length > 0 ? delta / length * contact.Distance : Vector3.zero);
                Stop(true, SkillProjectileStopReason.Wall); return false;
            }
            if ((targetLayers.value & (1 << contact.Collider.gameObject.layer)) == 0 ||
                !SkillHitRules.Accepts(data.HitRules, Context.Caster, hitTarget, from.Position, contact.Point, Context.QueryScene, Context.Space, Context.QueryScene2D) ||
                !Context.Hits.Register(hitTarget, data.HitRules, Motion.Age)) continue;
            var attack = new SkillHitData { Projectile = Context, detectionEvent = Context.Attack, soure = Context.Caster,
                attackValue = Context.AttackValue, hitPoint = contact.Point, DetectionOrigin = from.Position, HitLedger = Context.Hits };
            hitCount++;
            onHit?.Invoke(hitTarget, attack);
            if (!Alive) return false;
            if (hitCount > data.PierceCount || (data.HitRules.MaxTargets > 0 && Context.Hits.TargetCount >= data.HitRules.MaxTargets))
            {
                transform.position = contact.Point; Stop(true, SkillProjectileStopReason.HitLimit); return false;
            }
        }
        return Alive;
    }

    public void Stop(bool playEndEffect = false, SkillProjectileStopReason reason = SkillProjectileStopReason.Manual)
    {
        if (!Alive) return;
        Alive = false; active.Remove(this); onHit = null;
        var stopped = onStop; onStop = null;
        try
        {
            // Observers cannot prevent cleanup or cosmetic feedback.
            try { stopped?.Invoke(Context, reason, transform.position); }
            catch (Exception ex) { Debug.LogException(ex); }
            if (playEndEffect && data?.EndEffectPrefab != null)
                Feedback(data.EndEffectPrefab, transform.position, transform.rotation, data.EndEffectLifetime, gameObject.scene);
        }
        finally
        {
            gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
    }

    internal static void HitFeedback(SkillHitData attack)
    {
        var hit = attack.detectionEvent?.AttackHitConfig;
        if (hit == null || attack.Projectile == null) return;
        try
        {
            if (hit.HitAudioClip != null) (attack.Projectile.AudioService ?? SkillUnityAudio.Instance).PlayClip(hit.HitAudioClip, attack.hitPoint, 1, 0, hit.HitAudioClip.length, attack.Projectile.Scene);
            Feedback(hit.HitEffectPrefab, attack.hitPoint, Quaternion.identity, attack.Projectile.HitEffectLifetime, attack.Projectile.Scene);
        }
        catch (Exception ex) { Debug.LogException(ex); } // Cosmetic failure must not swallow damage.
    }

    private static void Feedback(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime, Scene scene)
    {
        if (prefab == null || !scene.IsValid() || !scene.isLoaded) return;
        var effectRoot = new GameObject("[投射物反馈特效]"); effectRoot.SetActive(false);
        try
        {
            SceneManager.MoveGameObjectToScene(effectRoot, scene);
            effectRoot.transform.SetPositionAndRotation(position, rotation);
            var visual = Instantiate(prefab, effectRoot.transform); PrepareVisual(visual); visual.SetActive(true);
            effectRoot.SetActive(true);
            if (Application.isPlaying) Destroy(effectRoot, lifetime); else DestroyImmediate(effectRoot);
        }
        catch
        {
            if (Application.isPlaying) Destroy(effectRoot); else DestroyImmediate(effectRoot);
            throw;
        }
    }
    private void OnDisable() { if (Alive) Stop(false, SkillProjectileStopReason.Disabled); }
    private void OnDestroy() { Alive = false; active.Remove(this); onHit = null; onStop = null; }
}

}
