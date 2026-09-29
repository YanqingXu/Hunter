// Standalone derivative. Source project files remain unchanged.
namespace SkillEditorKit
{
/// <summary>
/// 动画节点基类
/// </summary>
public abstract class AnimationNodeBase
{
    public int InputPort;

    public abstract void SetSpeed(float speed);
    public virtual void PushPool()
    {
        // Nodes are execution-owned; no project object pool is required.
    }
}
}
