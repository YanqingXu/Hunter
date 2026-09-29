using UnityEngine;
using YouYou;

/// <summary>
/// 角色状态 攻击
/// </summary>
public class RoleFSMAttack : RoleFSMBase
{
    //动画长度
    float m_AnimLen = 0;
    float m_EnterTime = 0;

    public void SetAnimLen(float animLen)
    {
        m_AnimLen = animLen;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        GameEntry.Log(LogCategory.Normal, "RoleFSMAttack OnEnter");
        //获取动画的长度
        m_EnterTime = Time.time;
        m_AnimLen = 0;
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        if (m_AnimLen > 0 && Time.time > m_EnterTime + m_AnimLen)
        {
            //动画播放完毕进入待机
            CurrFsm.Owner.ChangeState(MyCommonEnum.RoleFSMState.Idle);
        }
    }

    public override void OnLeave()
    {
        base.OnLeave();
        GameEntry.Log(LogCategory.Normal, "RoleFSMAttack OnLeave");
    }
}
