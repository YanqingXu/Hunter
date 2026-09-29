namespace SkillEditorKit
{
using UnityEngine;
public enum SkillSpace { ThreeD = 0, TwoD = 1 }

/// <summary>Optional facing override. Without it, sprite flipX and transform scale are detected.</summary>
[DisallowMultipleComponent]
public sealed class SkillFacing2D : MonoBehaviour
{
    [Tooltip("自动读取精灵 flipX 和 Transform 镜像；关闭后使用 Direction。")]
    public bool FollowSprite = true;
    public SpriteRenderer Sprite;
    [Range(-1, 1)] public int Direction = 1;
    public void SetFacing(int direction)
    {
        Direction = direction < 0 ? -1 : 1; FollowSprite = false;
        if (Sprite != null) Sprite.flipX = Direction < 0;
    }
    public static Quaternion Rotation(Transform actor)
    {
        var facing = actor.GetComponentInParent<SkillFacing2D>();
        if (facing != null && !facing.FollowSprite)
            return Quaternion.Euler(0, 0, facing.Direction < 0 ? 180 : 0);
        var sprite = facing != null && facing.Sprite != null ? facing.Sprite : actor.GetComponentInChildren<SpriteRenderer>();
        var basis = sprite != null ? sprite.transform : actor;
        Vector2 direction = basis.TransformVector(Vector3.right);
        if (sprite != null && sprite.flipX) direction = -direction;
        return Look(direction);
    }
    public static Quaternion Look(Vector2 direction) => direction.sqrMagnitude < .000001f ? Quaternion.identity :
        Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    public static Matrix4x4 EffectAnchor(Transform actor, out Quaternion rotation)
    {
        rotation=Rotation(actor);
        var result=Matrix4x4.identity; var forward=rotation*Vector3.right;
        result.SetColumn(0,new Vector4(forward.x,forward.y,0,0));
        result.SetColumn(3,new Vector4(actor.position.x,actor.position.y,actor.position.z,1));
        return result;
    }
    public static Vector3 Forward(Quaternion rotation, SkillSpace space) => rotation * (space == SkillSpace.TwoD ? Vector3.right : Vector3.forward);
}
}
