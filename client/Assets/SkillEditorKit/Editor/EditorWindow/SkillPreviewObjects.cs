// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SkillPreviewObjects
{
    /// <summary>Never activate a preview effect with gameplay scripts still attached.</summary>
    public static GameObject CreateEffect(GameObject prefab, Transform parent, string seedKey)
        => Create(prefab, parent, seedKey, false);

    public static GameObject CreateCharacter(GameObject prefab)
        => Create(prefab, null, "character", true);

    private static GameObject Create(GameObject prefab, Transform parent, string seedKey, bool character)
    {
        var staging = new GameObject("[SkillEditor] Inactive Staging") { hideFlags = HideFlags.HideAndDontSave };
        staging.SetActive(false);
        GameObject instance = null;
        try
        {
            instance = Object.Instantiate(prefab, staging.transform);
            var scripts = instance.GetComponentsInChildren<MonoBehaviour>(true).Where(s => s != null &&
                (!character || (!(s is SkillPlayer) && !(s is SkillWeapon) && !(s is SkillFacing2D)))).ToList();
            while (scripts.Count > 0)
            {
                var removable = scripts.FirstOrDefault(candidate => !scripts.Any(other => other != candidate &&
                    other.gameObject == candidate.gameObject && Requires(other.GetType(), candidate.GetType())));
                if (removable == null) throw new InvalidOperationException("特效脚本存在循环依赖，已取消预览以避免运行游戏逻辑。");
                scripts.Remove(removable);
                Object.DestroyImmediate(removable);
            }
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true))
            { audio.playOnAwake = false; audio.enabled = false; }
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody2D>(true)) body.simulated = false;
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
            { animator.enabled = character; animator.fireEvents = false; }
            var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var main = particles[i].main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                if (particles[i].useAutoRandomSeed)
                {
                    particles[i].useAutoRandomSeed = false;
                    particles[i].randomSeed = Seed(seedKey + ":" + i);
                }
            }
            instance.transform.SetParent(parent, false);
            return instance;
        }
        catch
        {
            if (instance != null) Object.DestroyImmediate(instance);
            throw;
        }
        finally { Object.DestroyImmediate(staging); }
    }

    private static bool Requires(Type dependent, Type dependency)
    {
        foreach (RequireComponent requirement in dependent.GetCustomAttributes(typeof(RequireComponent), true))
            if ((requirement.m_Type0 != null && requirement.m_Type0.IsAssignableFrom(dependency)) ||
                (requirement.m_Type1 != null && requirement.m_Type1.IsAssignableFrom(dependency)) ||
                (requirement.m_Type2 != null && requirement.m_Type2.IsAssignableFrom(dependency))) return true;
        return false;
    }

    private static uint Seed(string value)
    {
        uint seed = 2166136261;
        foreach (char c in value) seed = unchecked((seed ^ c) * 16777619);
        return seed == 0 ? 1 : seed;
    }

    public static void Simulate(GameObject instance, float seconds)
    {
        if (instance == null) return;
        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        var transforms = new HashSet<Transform>(particles.Select(p => p.transform));
        foreach (var particle in particles)
        {
            bool nested = false;
            for (var parent = particle.transform.parent; parent != null && parent != instance.transform.parent; parent = parent.parent)
                if (transforms.Contains(parent)) { nested = true; break; }
            if (!nested) particle.Simulate(Mathf.Max(0, seconds), true, true, true);
        }
    }
}

}
