/// Create By 悠游课堂 http://www.u3dol.com www.u3dol.com
using YouYou;
using YouYou.Proto;

/// <summary>
/// 网关服务器返回注册客户端结果
/// </summary>
public class GWS2C_ReturnRegClientHandler
{
    public static void OnHandler(byte[] buffer)
    {
        GWS2C_ReturnRegClient proto = GWS2C_ReturnRegClient.Parser.ParseFrom(buffer);

#if DEBUG_LOG_PROTO && DEBUG_MODEL
        GameEntry.Log(LogCategory.Proto, "<color=#00eaff>接收消息:</color><color=#00ff9c>" + proto.ProtoEnName + " " + proto.ProtoId + "</color>");
        GameEntry.Log(LogCategory.Proto, "<color=#c5e1dc>==>>" + proto.ToString() + "</color>");
#endif

        //派发注册客户端成功消息
        VarBool varBool = GameEntry.Pool.DequeueVarObject<VarBool>();
        varBool.Value = proto.Result;
        GameEntry.Event.CommonEvent.Dispatch(CommonEventId.OnRegClientComplete, varBool);
        GameEntry.Pool.EnqueueVarObject(varBool);
    }
}