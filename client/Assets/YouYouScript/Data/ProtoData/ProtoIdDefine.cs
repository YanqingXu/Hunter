/// Create By 悠游课堂 http://www.u3dol.com 
/// <summary>
/// 协议编号
/// </summary>
public class ProtoIdDefine
{
    /// <summary>
    /// 玩家向服务器发送点击移动消息
    /// </summary>
    public const ushort Proto_C2GS_ClickMove = 14001;

    /// <summary>
    /// 玩家向服务器发送摇杆移动消息
    /// </summary>
    public const ushort Proto_C2GS_JoystickMove = 14002;

    /// <summary>
    /// 玩家向服务器发送抬起摇杆消息
    /// </summary>
    public const ushort Proto_C2GS_JoystickStop = 14003;

    /// <summary>
    /// 玩家向服务器发送进入AOI区域消息
    /// </summary>
    public const ushort Proto_C2GS_Enter_AOIArea = 14004;

    /// <summary>
    /// 玩家向服务器发送播放技能消息
    /// </summary>
    public const ushort Proto_C2GS_PlaySkill = 14005;

    /// <summary>
    /// 玩家向网关服务器注册客户端
    /// </summary>
    public const ushort Proto_C2GWS_RegClient = 10001;

    /// <summary>
    /// 玩家向网关服务器发送进入场景申请消息
    /// </summary>
    public const ushort Proto_C2GWS_EnterScene_Apply = 10002;

    /// <summary>
    /// 玩家向网关服务器发送进入场景消息
    /// </summary>
    public const ushort Proto_C2GWS_EnterScene = 10003;

    /// <summary>
    /// 玩家向网关服务器发送心跳消息
    /// </summary>
    public const ushort Proto_C2GWS_Heartbeat = 10004;

    /// <summary>
    /// 玩家向服务器发送创建角色消息
    /// </summary>
    public const ushort Proto_C2WS_CreateRole = 12001;

    /// <summary>
    /// 玩家向服务器发送查询已有角色消息
    /// </summary>
    public const ushort Proto_C2WS_GetRoleList = 12002;

    /// <summary>
    /// 玩家向服务器发送进入游戏消息
    /// </summary>
    public const ushort Proto_C2WS_EnterGame = 12003;

    /// <summary>
    /// 聊天消息体
    /// </summary>
    public const ushort Proto_C2WS_Chat_Data = 12004;

    /// <summary>
    /// 玩家向服务器发送聊天游戏消息
    /// </summary>
    public const ushort Proto_C2WS_SendChatMsg = 12005;

    /// <summary>
    /// Vector3
    /// </summary>
    public const ushort Proto_Vector3 = 1;

    /// <summary>
    /// 服务器返回进入场景申请
    /// </summary>
    public const ushort Proto_GS2C_ReturnEnterScene_Apply = 15001;

    /// <summary>
    /// 服务器返回场景线中角色列表消息
    /// </summary>
    public const ushort Proto_WS2C_SceneLineRole_DATA = 13001;

    /// <summary>
    /// 服务器返回场景线中角色列表消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnSceneLineRoleList = 15002;

    /// <summary>
    /// 服务器返回角色进入场景线消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleEnterSceneLine = 15003;

    /// <summary>
    /// 服务器返回角色离开场景线消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleLeaveSceneLine = 15004;

    /// <summary>
    /// 服务器返回角色状态修改消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleChangeState = 15005;

    /// <summary>
    /// buff数据
    /// </summary>
    public const ushort Proto_GS2C_Buff_Data = 15006;

    /// <summary>
    /// 服务器返回角色战斗数据修改消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleBattleDataChange = 15007;

    /// <summary>
    /// 服务器返回角色buff到期消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleBuffExpires = 15008;

    /// <summary>
    /// 服务器返回角色buff持续掉血消息
    /// </summary>
    public const ushort Proto_GS2C_ReturnRoleBuffContinueHurt = 15009;

    /// <summary>
    /// 游戏服务器向网关服务器返回心跳消息
    /// </summary>
    public const ushort Proto_GS2GWS_Heartbeat = 21001;

    /// <summary>
    /// 获取路径点
    /// </summary>
    public const ushort Proto_GS2NS_Vector3 = 20001;

    /// <summary>
    /// 获取路径点
    /// </summary>
    public const ushort Proto_GS2NS_GetNavPath = 20002;

    /// <summary>
    /// 游戏服务器向寻路服务器发送心跳消息
    /// </summary>
    public const ushort Proto_GS2NS_Heartbeat = 20003;

    /// <summary>
    /// 游戏服务器注册到中心服务器
    /// </summary>
    public const ushort Proto_GS2WS_RegGameServer = 16001;

    /// <summary>
    /// 网关服务器返回注册客户端结果
    /// </summary>
    public const ushort Proto_GWS2C_ReturnRegClient = 11001;

    /// <summary>
    /// 网关服务器返回心跳消息
    /// </summary>
    public const ushort Proto_GWS2C_Heartbeat = 11002;

    /// <summary>
    /// 服务器返回角色下线消息
    /// </summary>
    public const ushort Proto_GWS2C_ReturnOffline = 11003;

    /// <summary>
    /// 网关服务器注册到游戏服务器
    /// </summary>
    public const ushort Proto_GWS2GS_RegGatewayServer = 20004;

    /// <summary>
    /// 网关服务器发送心跳到游戏服
    /// </summary>
    public const ushort Proto_GWS2GS_Heartbeat = 20005;

    /// <summary>
    /// 网关服务器向游戏服务器发送角色离开场景消息
    /// </summary>
    public const ushort Proto_GWS2GS_LeaveScene = 20006;

    /// <summary>
    /// 网关服务器向游戏服务器发送角色下线消息
    /// </summary>
    public const ushort Proto_GWS2GS_Offline = 20007;

    /// <summary>
    /// 网关服务器向游戏服务器发送角色进入场景申请消息
    /// </summary>
    public const ushort Proto_GWS2GS_EnterScene_Apply = 20008;

    /// <summary>
    /// 网关服务器向游戏服务器发送角色进入场景消息
    /// </summary>
    public const ushort Proto_GWS2GS_EnterScene = 20009;

    /// <summary>
    /// 网关服务器注册到中心服务器
    /// </summary>
    public const ushort Proto_GWS2WS_RegGatewayServer = 18001;

    /// <summary>
    /// 网关服务器通知中心服务器注册到游戏服完毕
    /// </summary>
    public const ushort Proto_GWS2WS_RegGameServerSuccess = 18002;

    /// <summary>
    /// 网关服务器向中心服务器发送角色下线消息
    /// </summary>
    public const ushort Proto_GWS2WS_Offline = 18003;

    /// <summary>
    /// 获取路径点
    /// </summary>
    public const ushort Proto_NS2GS_Vector3 = 21002;

    /// <summary>
    /// 获取路径点
    /// </summary>
    public const ushort Proto_NS2GS_ReturnNavPath = 21003;

    /// <summary>
    /// 寻路服务器返回心跳消息
    /// </summary>
    public const ushort Proto_NS2GS_Heartbeat = 21004;

    /// <summary>
    /// 服务器返回创建角色消息
    /// </summary>
    public const ushort Proto_WS2C_ReturnCreateRole = 13002;

    /// <summary>
    /// 服务器返回已有角色消息
    /// </summary>
    public const ushort Proto_WS2C_ReturnRoleList = 13003;

    /// <summary>
    /// 
    /// </summary>
    public const ushort Proto_WS2C_ReturnRoleList_Item = 13004;

    /// <summary>
    /// 服务器返回角色信息
    /// </summary>
    public const ushort Proto_WS2C_ReturnRoleInfo = 13005;

    /// <summary>
    /// 服务器返回进入游戏完毕消息
    /// </summary>
    public const ushort Proto_WS2C_ReturnEnterGameComplete = 13006;

    /// <summary>
    /// 服务器返回聊天消息
    /// </summary>
    public const ushort Proto_WS2C_ReturnChatMsg = 13007;

    /// <summary>
    /// 服务器推送聊天消息
    /// </summary>
    public const ushort Proto_WS2C_PushChatMsg = 13008;

    /// <summary>
    /// 中心服务器通知网关服务器注册到游戏服
    /// </summary>
    public const ushort Proto_WS2GWS_ToRegGameServer = 19001;
}