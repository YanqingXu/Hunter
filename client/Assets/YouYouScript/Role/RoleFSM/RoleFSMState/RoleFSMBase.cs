using UnityEngine;
using YouYou;

/// <summary>
/// 角色状态基类
/// </summary>
public class RoleFSMBase : FsmState<RoleFSMManager>
{
    /// <summary>
    /// 让角色贴地面方向
    /// </summary>
    private Vector3 m_MoveGroundDir = new Vector3(0, -1000, 0);

    /// <summary>
    /// 角色是否贴地面
    /// </summary>
    /// <returns></returns>
    protected virtual bool IsGrounded
    {
        get { return false; }
    }

    public override void OnEnter()
    {

    }

    public override void OnUpdate()
    {
        //让角色贴地面
        //if (IsGrounded && !CurrFsm.Owner.CurrRoleCtrl.Agent.gr)
        //{
        //    CurrFsm.Owner.CurrRoleCtrl.CharacterController.Move(m_MoveGroundDir);
        //}
    }

    public override void OnLeave()
    {

    }

    public override void OnDestroy()
    {

    }
}