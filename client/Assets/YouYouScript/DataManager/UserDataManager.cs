//===================================================
//作    者：边涯  http://www.u3dol.com
//创建时间：
//备    注：
//===================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using YouYou;
using YouYou.Proto;

/// <summary>
/// 用户数据
/// </summary>
public class UserDataManager : IDisposable
{
    /// <summary>
    /// 共享的用户数据
    /// </summary>
    public ShareUserData ShareUserData;

    /// <summary>
    /// 服务器返回的任务列表
    /// </summary>
    public List<ServerTaskEntity> ServerTaskList
    {
        get;
        private set;
    }

    /// <summary>
    /// 性别
    /// </summary>
    public MyCommonEnum.Sex Sex
    {
        get;
        private set;
    }

    /// <summary>
    /// 昵称
    /// </summary>
    public string NickName
    {
        get;
        private set;
    }

    /// <summary>
    /// 等级
    /// </summary>
    public int Level
    {
        get;
        private set;
    }

    /// <summary>
    /// 当前场景编号
    /// </summary>
    public int CurrSceneId;

    /// <summary>
    /// 当前位置
    /// </summary>
    public UnityEngine.Vector3 CurrPos;

    public float RotationY;
    public int CurrHP;
    public int MaxHP;
    public int CurrMP;
    public int MaxMP;
    public int CurrFury;

    public UserDataManager()
    {
        ShareUserData = new ShareUserData();
        ServerTaskList = new List<ServerTaskEntity>();
    }

    /// <summary>
    /// 注册客户端
    /// </summary>
    public void RegClient()
    {
        C2GWS_RegClient proto = new C2GWS_RegClient();
        proto.AccountId = ShareUserData.AccountId;
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 查询角色列表
    /// </summary>
    public WS2C_ReturnRoleList ReturnRoleListData { get; private set; }

    public void OnReturnRoleList(WS2C_ReturnRoleList proto)
    {
        ReturnRoleListData = proto ?? throw new ArgumentNullException(nameof(proto));
        if (proto.RoleList.Count == 0) GameEntry.UI.OpenUIForm(UIFormId.UI_CreateRole, 0);
        else GameEntry.UI.OpenUIForm(UIFormId.UI_SelectRole);
    }

    public void GetRoleList()
    {
        C2WS_GetRoleList proto = new C2WS_GetRoleList();
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 服务器返回创建角色消息
    /// </summary>
    /// <param name="proto"></param>
    public void OnCreateRole(WS2C_ReturnCreateRole proto)
    {
        if (proto.Result)
        {
            ShareUserData.CurrRoleId = proto.RoleId;
            GameEntry.Log(LogCategory.Normal, "创建角色成功");

            //进入进入游戏流程
            GameEntry.Procedure.ChangeState(ProcedureState.EnterGame);
        }
        else
        {
            //根据错误码 弹出提示
            GameEntry.LogError("创建角色失败");
        }
    }

    /// <summary>
    /// 进入游戏
    /// </summary>
    public void EnterGame()
    {
        C2WS_EnterGame proto = new C2WS_EnterGame();
        proto.RoleId = ShareUserData.CurrRoleId;
        GameEntry.Socket.SendMainMsg(proto);
    }

    /// <summary>
    /// 服务器返回角色信息
    /// </summary>
    /// <param name="proto"></param>
    public void OnReturnRoleInfo(WS2C_ReturnRoleInfo proto)
    {
        ShareUserData.CurrRoleId = proto.RoleId;
        ShareUserData.CurrJobId = proto.JobId;
        Sex = (MyCommonEnum.Sex)proto.Sex;
        NickName = proto.NickName;
        Level = proto.Level;
        CurrSceneId = proto.CurrSceneId;
        CurrPos = new UnityEngine.Vector3(proto.CurrPos.X, proto.CurrPos.Y, proto.CurrPos.Z);
        RotationY = proto.RotationY;
        CurrHP = proto.CurrHP;
        MaxHP = proto.MaxHP;
        CurrMP = proto.CurrMP;
        MaxMP = proto.MaxMP;
        CurrFury = proto.CurrFury;
    }

    public void Clear()
    {
        ShareUserData.Dispose();
        ReturnRoleListData = null;
        ServerTaskList.Clear();
    }

    public void Dispose()
    {
        Clear();
    }
}