/// Create By 悠游课堂 http://www.u3dol.com www.u3dol.com
using YouYou;
using YouYou.Proto;

/// <summary>
/// 服务器返回角色进入场景线消息
/// </summary>
public class GS2C_ReturnRoleEnterSceneLineHandler
{
    public static void OnHandler(byte[] buffer)
    {
        GS2C_ReturnRoleEnterSceneLine proto = GS2C_ReturnRoleEnterSceneLine.Parser.ParseFrom(buffer);

#if DEBUG_LOG_PROTO && DEBUG_MODEL
        GameEntry.Log(LogCategory.Proto, "<color=#00eaff>接收消息:</color><color=#00ff9c>" + proto.ProtoEnName + " " + proto.ProtoId + "</color>");
        GameEntry.Log(LogCategory.Proto, "<color=#c5e1dc>==>>" + proto.ToString() + "</color>");
#endif

        GameEntry.Data.RoleDataManager.OnReturnRoleEnterSceneLine(proto);
    }
}