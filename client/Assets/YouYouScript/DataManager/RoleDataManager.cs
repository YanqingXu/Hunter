using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;
using YouYou.DataTable;
using YouYou.Proto;

/// <summary>
/// 角色数据管理器
/// </summary>
public class RoleDataManager : IDisposable
{
    private LinkedList<RoleCtrl> m_RoleList;

    /// <summary>
    /// 当前PVP场景中的角色字典
    /// </summary>
    private Dictionary<long, RoleCtrl> m_CurrPVPSceneRoleDic;

    public RoleDataManager()
    {
        m_RoleList = new LinkedList<RoleCtrl>();
        m_CurrPVPSceneRoleDic = new Dictionary<long, RoleCtrl>();
        CurrPlayerMoveHelper = new GameObject("CurrPlayerMoveHelper");
    }
    /// <summary>
    /// 通过摇杆控制玩家移动时的一个辅助gameobject
    /// </summary>
    public GameObject CurrPlayerMoveHelper { get; }

    /// <summary>
    /// 当前玩家
    /// </summary>
    public RoleCtrl CurrPlayer;

    /// <summary>
    /// 根据职业编号创建角色
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="onComplete"></param>
    public void CreatePlayerByJobId(int jobId, BaseAction<RoleCtrl> onComplete = null)
    {
        //角色编号
        int baseRoleId = GameEntry.DataTable.JobList.GetEntityValue(jobId).BaseRoleId;

        //加载 角色控制器
        GameEntry.Pool.GameObjectSpawn(SysPrefabId.RoleCtrl, (Transform trans, bool isNewInstance) =>
        {
            RoleCtrl roleCtr = trans.GetComponent<RoleCtrl>();
            roleCtr.InitPlayerData(baseRoleId);

            if (!isNewInstance)
            {
                //如果不是新实例 在这里执行OnOpen方法
                roleCtr.OnOpen();
            }

            m_RoleList.AddLast(roleCtr);
            onComplete?.Invoke(roleCtr);
        });
    }

    /// <summary>
    /// 创建怪
    /// </summary>
    /// <param name="spriteId"></param>
    /// <param name="onComplete"></param>
    public void CreateSprite(int spriteId, BaseAction<RoleCtrl> onComplete = null)
    {
        //加载 角色控制器
        GameEntry.Pool.GameObjectSpawn(SysPrefabId.RoleCtrl, (Transform trans, bool isNewInstance) =>
        {
            RoleCtrl roleCtr = trans.GetComponent<RoleCtrl>();
            roleCtr.CurrRoleType = MyCommonEnum.RoleType.Monster;
            roleCtr.InitSpriteData(spriteId);

            if (!isNewInstance)
            {
                //如果不是新实例 在这里执行OnOpen方法
                roleCtr.OnOpen();
            }

            m_RoleList.AddLast(roleCtr);
            onComplete?.Invoke(roleCtr);
        });
    }

    /// <summary>
    /// 角色回池
    /// </summary>
    /// <param name="roleCtrl"></param>
    public void DespawnRole(RoleCtrl roleCtrl)
    {
        //先执行角色关闭方法
        roleCtrl.OnClose();
        //然后回池角色
        GameEntry.Pool.GameObjectDespawn(roleCtrl.transform);
        m_RoleList.Remove(roleCtrl);
    }

    /// <summary>
    /// 回池所有角色
    /// </summary>
    public void DespawnAllRole()
    {
        for (LinkedListNode<RoleCtrl> curr = m_RoleList.First; curr != null;)
        {
            LinkedListNode<RoleCtrl> next = curr.Next;
            DespawnRole(curr.Value);
            curr = next;
        }
    }

    /// <summary>
    /// 检查卸载角色动画
    /// </summary>
    public void CheckUnloadRoleAnimation()
    {
        for (LinkedListNode<RoleCtrl> curr = m_RoleList.First; curr != null;)
        {
            LinkedListNode<RoleCtrl> next = curr.Next;
            curr.Value.CheckUnloadRoleAnimation();
            curr = next;
        }
    }

    /// <summary>
    /// 服务器返回进入游戏消息
    /// </summary>
    public void OnEnterGameComplete()
    {
        EnterSceneApply(GameEntry.Data.UserDataManager.CurrSceneId);
    }

    /// <summary>
    /// 进入场景申请
    /// </summary>
    /// <param name="sceneId">场景编号</param>
    public void EnterSceneApply(int sceneId)
    {
        C2GWS_EnterScene_Apply proto = new C2GWS_EnterScene_Apply();
        proto.SceneId = sceneId;
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 服务器返回进入场景申请消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnEnterSceneApply(GS2C_ReturnEnterScene_Apply proto)
    {
        if (proto.Result)
        {
            GameEntry.Data.UserDataManager.CurrSceneId = proto.SceneId;
            GameEntry.Data.UserDataManager.CurrPos = new UnityEngine.Vector3(proto.CurrPos.X, proto.CurrPos.Y, proto.CurrPos.Z);
            GameEntry.Procedure.ChangeState(ProcedureState.WorldMap);
        }
        else
        {
            //TODO 弹出提示
        }
    }

    /// <summary>
    /// 进入场景
    /// </summary>
    /// <param name="sceneId"></param>
    public void EnterScene(int sceneId)
    {
        C2GWS_EnterScene proto = new C2GWS_EnterScene();
        proto.SceneId = sceneId;
        GameEntry.Socket.SendMainMsg(proto);

        m_CurrPVPSceneRoleDic[GameEntry.Data.RoleDataManager.CurrPlayer.ServerRoleId] = GameEntry.Data.RoleDataManager.CurrPlayer;
    }

    /// <summary>
    /// 服务器返回场景中已有角色消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnSceneLineRoleList(GS2C_ReturnSceneLineRoleList proto)
    {
        int len = proto.RoleList.Count;
        for (int i = 0; i < len; i++)
        {
            WS2C_SceneLineRole_DATA data = proto.RoleList[i];
            LoadSceneLineRole(data);
        }
    }

    /// <summary>
    /// 服务器返回角色离开场景线
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleLeaveSceneLine(GS2C_ReturnRoleLeaveSceneLine proto)
    {
        if (m_CurrPVPSceneRoleDic.TryGetValue(proto.RoleId, out var roleCtrl))
        {
            //卸载角色
            roleCtrl.OnClose();
            m_CurrPVPSceneRoleDic.Remove(proto.RoleId);

            if (GameEntry.Data.CacheDataManager.CurrLockRole != null && GameEntry.Data.CacheDataManager.CurrLockRole.ServerRoleId== proto.RoleId)
            {
                GameEntry.Data.CacheDataManager.CurrLockRole = null;
            }
        }
    }

    /// <summary>
    /// 服务器返回角色进入场景线消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleEnterSceneLine(GS2C_ReturnRoleEnterSceneLine proto)
    {
        int len = proto.RoleList.Count;
        for (int i = 0; i < len; i++)
        {
            WS2C_SceneLineRole_DATA data = proto.RoleList[i];
            LoadSceneLineRole(data);
        }
    }

    /// <summary>
    /// 加载场景线中的角色
    /// </summary>
    /// <param name="data"></param>
    private void LoadSceneLineRole(WS2C_SceneLineRole_DATA data)
    {
        if (data.RoleType == RoleType.Player)
        {
            CreatePlayerByJobId(data.BaseRoleId, (RoleCtrl roleCtrl) =>
             {
                 roleCtrl.ServerRoleId = data.RoleId;
                 roleCtrl.CurrRoleType = MyCommonEnum.RoleType.OtherPlayer;
                 roleCtrl.transform.position = new UnityEngine.Vector3(data.CurrPos.X, data.CurrPos.Y, data.CurrPos.Z);
                 roleCtrl.transform.rotation = Quaternion.Euler(0, data.RotationY, 0);
                 roleCtrl.RoleInfo.InitRoleInfo(data, roleCtrl);
                 roleCtrl.OpenAgent();
                 roleCtrl.RefreshHeadBar();

                 //如果这个角色正在跑 继续让他跑
                 if (data.Status == (int)MyCommonEnum.RoleFSMState.Run)
                 {
                     roleCtrl.ClickMove(new UnityEngine.Vector3() { x = data.TargetPos.X, y = data.TargetPos.Y, z = data.TargetPos.Z });
                 }
                 else
                 {
                     roleCtrl.ChangeState((MyCommonEnum.RoleFSMState)data.Status);
                 }

                 m_CurrPVPSceneRoleDic[roleCtrl.ServerRoleId] = roleCtrl;
             });
        }
        else
        {
            CreateSprite(data.BaseRoleId, (RoleCtrl roleCtrl) =>
            {
                roleCtrl.ServerRoleId = data.RoleId;
                roleCtrl.transform.position = new UnityEngine.Vector3(data.CurrPos.X, data.CurrPos.Y, data.CurrPos.Z);
                roleCtrl.transform.rotation = Quaternion.Euler(0, data.RotationY, 0);

                DTSprite dTSprite = GameEntry.DataTable.SpriteList.GetEntityValue(data.BaseRoleId);
                data.NickName = dTSprite.Name;

                roleCtrl.RoleInfo.InitRoleInfo(data, roleCtrl);
                roleCtrl.OpenAgent();
                roleCtrl.RefreshHeadBar();

                //如果这个角色正在跑 继续让他跑
                if (data.Status == (int)MyCommonEnum.RoleFSMState.Run)
                {
                    roleCtrl.ClickMove(new UnityEngine.Vector3() { x = data.TargetPos.X, y = data.TargetPos.Y, z = data.TargetPos.Z });
                }
                else
                {
                    roleCtrl.ChangeState((MyCommonEnum.RoleFSMState)data.Status);
                }

                m_CurrPVPSceneRoleDic[roleCtrl.ServerRoleId] = roleCtrl;
            });
        }
    }

    /// <summary>
    /// 玩家进入AOI区域
    /// </summary>
    /// <param name="areaId"></param>
    public void PlayerEnterAOIArea(int areaId)
    {
        C2GS_Enter_AOIArea proto = new C2GS_Enter_AOIArea();
        proto.AreaId = areaId;
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 点击移动
    /// </summary>
    /// <param name="currPos">当前位置</param>
    /// <param name="targetPos">移动目标点</param>
    public void ClickMove(UnityEngine.Vector3 currPos, UnityEngine.Vector3 targetPos)
    {
        C2GS_ClickMove proto = new C2GS_ClickMove();
        proto.CurrPos = new YouYou.Proto.Vector3() { X = currPos.x, Y = currPos.y, Z = currPos.z };
        proto.TargetPos = new YouYou.Proto.Vector3() { X = targetPos.x, Y = targetPos.y, Z = targetPos.z };
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 上一次摇杆移动的方向
    /// </summary>
    private UnityEngine.Vector3 m_PrevJoystickMoveDir;

    /// <summary>
    /// 上一次摇杆移动的发消息的时间
    /// </summary>
    private float m_PrevJoystickMoveTime = 0;

    /// <summary>
    /// 摇杆移动
    /// </summary>
    /// <param name="currPos">当前位置</param>
    /// <param name="moveDir">移动方向</param>
    public void JoystickMove(UnityEngine.Vector3 currPos, UnityEngine.Vector3 moveDir)
    {
        if (m_PrevJoystickMoveDir == moveDir)
        {
            if (Time.time > m_PrevJoystickMoveTime + 0.062f)
            {
                m_PrevJoystickMoveTime = Time.time;
            }
            else
            {
                return;
            }
        }
        m_PrevJoystickMoveDir = moveDir;

        C2GS_JoystickMove proto = new C2GS_JoystickMove();
        proto.CurrPos = new YouYou.Proto.Vector3() { X = currPos.x, Y = currPos.y, Z = currPos.z };
        proto.MoveDir = new YouYou.Proto.Vector3() { X = moveDir.x, Y = moveDir.y, Z = moveDir.z };
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 摇杆抬起
    /// </summary>
    /// <param name="currPos">当前位置</param>
    /// <param name="rotationY">角色旋转</param>
    public void JoystickStop(UnityEngine.Vector3 currPos, float rotationY)
    {
        C2GS_JoystickStop proto = new C2GS_JoystickStop();
        proto.CurrPos = new YouYou.Proto.Vector3() { X = currPos.x, Y = currPos.y, Z = currPos.z };
        proto.RotationY = rotationY;
        GameEntry.Socket.SendMainMsg(proto);
    }

    public void RoleChangeState(GS2C_ReturnRoleChangeState proto)
    {
        //找到角色
        if (m_CurrPVPSceneRoleDic.TryGetValue(proto.RoleId, out RoleCtrl roleCtrl))
        {
            if (proto.Status == (int)MyCommonEnum.RoleFSMState.Idle)
            {
                if (this.CurrPlayer.ServerRoleId == proto.RoleId)
                {
                    return;
                }

                if (proto.ActionType == PlayerActionType.JoystickStop)
                {
                    roleCtrl.ServerJoystickStop(new UnityEngine.Vector3() { x = proto.CurrPos.X, y = proto.CurrPos.Y, z = proto.CurrPos.Z }, proto.RotationY);
                }
            }
            else if (proto.Status == (int)MyCommonEnum.RoleFSMState.Run)
            {
                if (proto.ActionType == PlayerActionType.ClickMove)
                {
                    if (this.CurrPlayer.ServerRoleId == proto.RoleId)
                    {
                        return;
                    }
                    roleCtrl.ServerRun(proto.RunSpeed, new UnityEngine.Vector3() { x = proto.TargetPos.X, y = proto.TargetPos.Y, z = proto.TargetPos.Z });
                }
                else if (proto.ActionType == PlayerActionType.ServerMove)
                {
                    roleCtrl.ServerRun(proto.RunSpeed, new UnityEngine.Vector3() { x = proto.TargetPos.X, y = proto.TargetPos.Y, z = proto.TargetPos.Z });
                }
                else
                {
                    if (this.CurrPlayer.ServerRoleId == proto.RoleId)
                    {
                        return;
                    }
                    roleCtrl.ServerJoystickMove(proto.RunSpeed, new UnityEngine.Vector3() { x = proto.TargetPos.X, y = proto.TargetPos.Y, z = proto.TargetPos.Z });
                }
            }
            else if (proto.Status == (int)MyCommonEnum.RoleFSMState.Attack)
            {
                //有角色发动了攻击，这个角色也可能是当前玩家
                roleCtrl.ChangeState(MyCommonEnum.RoleFSMState.Attack);

                //朝向被攻击者
                roleCtrl.transform.eulerAngles = new UnityEngine.Vector3(roleCtrl.transform.eulerAngles.x, proto.RotationY,
                    roleCtrl.transform.eulerAngles.z);

                DTSkillLevel dTSkillLevel = GameEntry.DataTable.SkillLevelList.GetEntityValue(proto.SkillLevel);

                GameEntry.Pool.GameObjectSpawn(dTSkillLevel.PrefabId, onComplete: (Transform trans, bool isNewInstance) =>
                {
                    TimelineCtrl timelineCtrl = trans.gameObject.GetComponent<TimelineCtrl>();
                    timelineCtrl.CurrRole = roleCtrl;

                    roleCtrl.SetAttackAnimLen(timelineCtrl.AttackEndTime);

                    timelineCtrl.OnStopped = () =>
                    {
                        GameEntry.Pool.GameObjectDespawn(trans);
                    };
                });
            }
        }
    }

    public void PlaySkill(int skillId)
    {
        if (GameEntry.Data.CacheDataManager.CurrLockRole == null)
        {
            return;
        }
        C2GS_PlaySkill proto = new C2GS_PlaySkill();
        proto.TargetRoleType = (int)GameEntry.Data.CacheDataManager.CurrLockRole.CurrRoleType;
        proto.TargetRoleId = GameEntry.Data.CacheDataManager.CurrLockRole.ServerRoleId;
        proto.SkillId = skillId;
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 服务器返回角色数据修改消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleBattleDataChange(GS2C_ReturnRoleBattleDataChange proto)
    {
        if (m_CurrPVPSceneRoleDic.TryGetValue(proto.RoleId, out RoleCtrl role))
        {
            role.RoleInfo.ChangeRoleBattleData(proto);
        }
    }

    /// <summary>
    /// 服务器返回角色因为buff持续掉血消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleBuffContinueHurt(GS2C_ReturnRoleBuffContinueHurt proto)
    {
        if (m_CurrPVPSceneRoleDic.TryGetValue(proto.RoleId, out RoleCtrl role))
        {
            role.BuffContinueHurt(proto.HurtValue);
        }
    }

    /// <summary>
    /// 服务器返回buff过期消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleBuffExpires(GS2C_ReturnRoleBuffExpires proto)
    {
        if (m_CurrPVPSceneRoleDic.TryGetValue(proto.RoleId, out RoleCtrl role))
        {
            role.RoleInfo.CurrRole.RemoveBuff(proto.BuffId);
        }
    }

    public void CheckHurt(long attackRoleId)
    {
        foreach (var item in m_CurrPVPSceneRoleDic)
        {
            item.Value.CheckHurt(attackRoleId);
        }
    }

    public void Dispose()
    {

    }
}