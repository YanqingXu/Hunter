namespace SkillEditorKit
{
    /// <summary>Optional host-owned immutable settings, captured when a cast is accepted. Never stored on a SkillClip.</summary>
    public interface ISkillProjectileSettings
    {
        float Damage { get; }
        SkillProjectileEvent Configure(SkillProjectileEvent authored, int frameRate);
        ISkillProjectileTraversal CreateTraversal();
    }

    /// <summary>Separate state for each projectile. Returning false from TryPass stops it at that solid obstacle.</summary>
    public interface ISkillProjectileTraversal
    {
        float Damage { get; }
        bool TryPass(UnityEngine.Component obstacle);
    }
}
