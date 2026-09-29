/// Create By 悠游课堂 http://www.u3dol.com www.u3dol.com
using YouYou;
using YouYou.Proto;

/// <summary>
/// 服务器返回角色信息
/// </summary>
public class WS2C_ReturnRoleInfoHandler
{
    public static void OnHandler(byte[] buffer)
    {
        WS2C_ReturnRoleInfo proto = WS2C_ReturnRoleInfo.Parser.ParseFrom(buffer);

#if DEBUG_LOG_PROTO && DEBUG_MODEL
        GameEntry.Log(LogCategory.Proto, "<color=#00eaff>接收消息:</color><color=#00ff9c>" + proto.ProtoEnName + " " + proto.ProtoId + "</color>");
        GameEntry.Log(LogCategory.Proto, "<color=#c5e1dc>==>>" + proto.ToString() + "</color>");
#endif

        GameEntry.Data.UserDataManager.OnReturnRoleInfo(proto);
    }
}