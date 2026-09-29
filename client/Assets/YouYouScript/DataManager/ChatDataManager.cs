using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;
using YouYou.Proto;

public class ChatDataManager : IDisposable
{
    /// <summary>
    /// 综合聊天
    /// </summary>
    public List<C2WS_Chat_Data> SynthesisChatMsg { get; private set; }

    public List<C2WS_Chat_Data> SysChatMsg { get; private set; }
    public List<C2WS_Chat_Data> WorldChatMsg { get; private set; }
    public List<C2WS_Chat_Data> FriendChatMsg { get; private set; }
    public List<C2WS_Chat_Data> GandChatMsg { get; private set; }
    public List<C2WS_Chat_Data> LeagueChatMsg { get; private set; }
    public List<C2WS_Chat_Data> TeamChatMsg { get; private set; }

    /// <summary>
    /// 当前频道的聊天内容
    /// </summary>
    public List<C2WS_Chat_Data> CurrChannelChatMsg;

    /// <summary>
    /// 频道颜色
    /// </summary>
    public Dictionary<ChatChannel, Color> ChatChannelColor;

    /// <summary>
    /// 频道文本
    /// </summary>
    public Dictionary<ChatChannel, string> ChatChannelText;

    public ChatDataManager()
    {
        SynthesisChatMsg = new List<C2WS_Chat_Data>();
        SysChatMsg = new List<C2WS_Chat_Data>();
        WorldChatMsg = new List<C2WS_Chat_Data>();
        FriendChatMsg = new List<C2WS_Chat_Data>();
        GandChatMsg = new List<C2WS_Chat_Data>();
        LeagueChatMsg = new List<C2WS_Chat_Data>();
        TeamChatMsg = new List<C2WS_Chat_Data>();

        ChatChannelColor = new Dictionary<ChatChannel, Color>();
        ChatChannelColor[ChatChannel.Sys] = new Color(191 / 255f, 42 / 255f, 5 / 255f, 1);
        ChatChannelColor[ChatChannel.World] = new Color(17 / 255f, 188 / 255f, 28 / 255f, 1);
        ChatChannelColor[ChatChannel.Friend] = new Color(173 / 255f, 36 / 255f, 207 / 255f, 1);
        ChatChannelColor[ChatChannel.Gand] = new Color(73 / 255f, 36 / 255f, 207 / 255f, 1);
        ChatChannelColor[ChatChannel.League] = new Color(36 / 255f, 123 / 255f, 207 / 255f, 1);
        ChatChannelColor[ChatChannel.Team] = new Color(36 / 255f, 207 / 255f, 206 / 255f, 1);

        ChatChannelText = new Dictionary<ChatChannel, string>();
        ChatChannelText[ChatChannel.Sys] = "系统";
        ChatChannelText[ChatChannel.World] = "世界";
        ChatChannelText[ChatChannel.Friend] = "好友";
        ChatChannelText[ChatChannel.Gand] = "帮会";
        ChatChannelText[ChatChannel.League] = "同盟";
        ChatChannelText[ChatChannel.Team] = "队伍";
    }

    /// <summary>
    /// 客户端发送聊天消息到服务器
    /// </summary>
    /// <param name="msg"></param>
    public void SendChatMsg(C2WS_Chat_Data msg)
    {
        C2WS_SendChatMsg proto = new C2WS_SendChatMsg();
        proto.ChatMsg = msg;
        GameEntry.Socket.SendMainMsg(proto);

        PushChatMsg(msg);
    }
    public void PushChatMsg(C2WS_Chat_Data msg)
    {
        PushSynthesisChatMsg(msg);

        switch (msg.Channel)
        {
            case ChatChannel.Sys:
                PushChannelChatMsg(msg, SysChatMsg);
                break;
            case ChatChannel.World:
                PushChannelChatMsg(msg, WorldChatMsg);
                break;
            case ChatChannel.Friend:
                PushChannelChatMsg(msg, FriendChatMsg);
                break;
            case ChatChannel.Gand:
                PushChannelChatMsg(msg, GandChatMsg);
                break;
            case ChatChannel.League:
                PushChannelChatMsg(msg, LeagueChatMsg);
                break;
            case ChatChannel.Team:
                PushChannelChatMsg(msg, TeamChatMsg);
                break;
        }

        //派发来了新消息事件
        GameEntry.Event.CommonEvent.Dispatch(CommonEventId.OnPushChatMsg);
    }

    private void PushChannelChatMsg(C2WS_Chat_Data msg, List<C2WS_Chat_Data> lst)
    {
        if (lst.Count > 100)
        {
            lst.RemoveAt(0);
        }

        lst.Add(msg);
    }

    /// <summary>
    /// 服务器推送综合频道消息
    /// </summary>
    /// <param name="msg"></param>
    private void PushSynthesisChatMsg(C2WS_Chat_Data msg)
    {
        if (SynthesisChatMsg.Count > 15)
        {
            SynthesisChatMsg.RemoveAt(0);
        }

        SynthesisChatMsg.Add(msg);
    }

    public void Dispose()
    {

    }
}
