using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using YouYou;
using static MyCommonEnum;

/// <summary>
/// 角色状态机管理器
/// </summary>
public class RoleFSMManager : ManagerBase
{
    /// <summary>
    /// 当前角色状态机
    /// </summary>
    public Fsm<RoleFSMManager> CurrFsm { get; private set; }

    /// <summary>
    /// 当前的角色状态
    /// </summary>
    public RoleFSMState CurrRoleFSMState
    {
        get
        {
            if (CurrFsm == null)
            {
                return RoleFSMState.Idle;
            }
            return (RoleFSMState)CurrFsm.CurrStateType;
        }
    }

    /// <summary>
    /// 当前的角色状态
    /// </summary>
    public FsmState<RoleFSMManager> CurrRoleFSM
    {
        get
        {
            return CurrFsm.GetState(CurrFsm.CurrStateType);
        }
    }

    /// <summary>
    /// 当前角色控制器
    /// </summary>
    public RoleCtrl CurrRoleCtrl
    {
        get;
        private set;
    }

    public RoleFSMManager(RoleCtrl roleCtrl)
    {
        CurrRoleCtrl = roleCtrl;
    }

    /// <summary>
    /// 跑状态
    /// </summary>
    private RoleFSMRun m_RoleFSMRun;

    /// <summary>
    /// 攻击状态
    /// </summary>
    public RoleFSMAttack RoleFSMAttack { get; private set; }

    public override void Init()
    {
        FsmState<RoleFSMManager>[] states = new FsmState<RoleFSMManager>[6];
        states[(sbyte)RoleFSMState.Idle] = new RoleFSMIdle();

        m_RoleFSMRun = new RoleFSMRun();
        states[(sbyte)RoleFSMState.Run] = m_RoleFSMRun;

        RoleFSMAttack = new RoleFSMAttack();
        states[(sbyte)RoleFSMState.Attack] = RoleFSMAttack;

        states[(sbyte)RoleFSMState.Hurt] = new RoleFSMHurt();

        CurrFsm = GameEntry.Fsm.Create(this, states);
    }

    /// <summary>
    /// 切换状态
    /// </summary>
    /// <param name="state"></param>
    public void ChangeState(RoleFSMState state)
    {
        CurrFsm.ChangeState((sbyte)state);
    }

    public void ClickMove(Vector3 targetPos)
    {
        ChangeState(RoleFSMState.Run);
        m_RoleFSMRun.ClickMove(targetPos);
    }

    /// <summary>
    /// 服务器移动
    /// </summary>
    /// <param name="runSpeed"></param>
    /// <param name="targetPos"></param>
    public void ServerRun(float runSpeed, Vector3 targetPos)
    {
        ChangeState(RoleFSMState.Run);
        m_RoleFSMRun.ServerRun(runSpeed, targetPos);
    }

    public void JoystickMove(float runSpeed, Vector3 dir, bool clientAction)
    {
        ChangeState(RoleFSMState.Run);
        m_RoleFSMRun.JoystickMove(runSpeed, dir, clientAction);
    }

    public void JoystickStop(bool clientAction, Vector3 currPos, float rotationY)
    {
        m_RoleFSMRun.JoystickStop(clientAction, currPos, rotationY);
    }

    public void OnUpdate()
    {
        if (CurrFsm == null) return;
        CurrFsm.OnUpate();
    }

    /// <summary>
    /// 设置参数值
    /// </summary>
    /// <typeparam name="TData">泛型类型</typeparam>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public void SetData<TData>(string key, TData value)
    {
        CurrFsm.SetData<TData>(key, value);
    }

    /// <summary>
    /// 获取参数值
    /// </summary>
    /// <typeparam name="TData"></typeparam>
    /// <param name="key"></param>
    /// <returns></returns>
    public TData GetData<TData>(string key)
    {
        return CurrFsm.GetData<TData>(key);
    }
}