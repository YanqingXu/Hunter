/// Create By 悠游课堂 http://www.u3dol.com www.u3dol.com
using System;
using UnityEngine;
using YouYou;
using YouYou.Proto;

/// <summary>
/// 网关服务器返回心跳消息
/// </summary>
public class GWS2C_HeartbeatHandler
{
    public static void OnHandler(byte[] buffer)
    {
        GWS2C_Heartbeat proto = GWS2C_Heartbeat.Parser.ParseFrom(buffer);

        GameEntry.Socket.PingValue = (int)((DateTime.UtcNow.Ticks - proto.Time) * 0.5f / 10000) + proto.ToGameServerPing;
        //GameEntry.Log(LogCategory.Proto, "PingValue={0}", GameEntry.Socket.PingValue);
        GameEntry.Socket.LastServerTime = proto.ServerTime;

#if DEBUG_LOG_PROTO && DEBUG_MODEL
        //GameEntry.Log(LogCategory.Proto, "<color=#00eaff>接收消息:</color><color=#00ff9c>" + proto.ProtoEnName + " " + proto.ProtoId + "</color>");
        //GameEntry.Log(LogCategory.Proto, "<color=#c5e1dc>==>>" + proto.ToString() + "</color>");
#endif
    }
}