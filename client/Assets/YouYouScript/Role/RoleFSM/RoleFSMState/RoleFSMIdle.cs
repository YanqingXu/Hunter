using UnityEngine;
using YouYou;
/// <summary>
/// 角色状态 待机
/// </summary>
public class RoleFSMIdle : RoleFSMBase
{
    public override void OnEnter()
    {
        base.OnEnter();
        GameEntry.Log(LogCategory.Normal, "RoleFSMIdle OnEnter");

        CurrFsm.Owner.CurrRoleCtrl.PlayAnimByAnimCategory(MyCommonEnum.RoleAnimCategory.IdleNormal);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
    }

    public override void OnLeave()
    {
        base.OnLeave();
        GameEntry.Log(LogCategory.Normal, "RoleFSMIdle OnLeave");
    }

    protected override bool IsGrounded => true;
}