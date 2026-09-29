using SuperScrollView;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YouYou;
using YouYou.Proto;
using DG.Tweening;
using TMPro;
using UnityEngine.EventSystems;

public class UIChatForm : UIFormBase
{
    public LoopListView2 simpleLoopListView;

    public LoopListView2 loopListView;
    public Button btnSys;
    public Button btnWorld;
    public Button btnFriend;
    public Button btnGand;
    public Button btnLeague;
    public Button btnTeam;

    /// <summary>
    /// 是否移动中
    /// </summary>
    private bool m_IsMoveing = false;
    public Button btnShowLeft;
    public Button btnClose;
    public RectTransform LeftChatRoot;

    /// <summary>
    /// 当前的频道
    /// </summary>
    private ChatChannel m_CurrChatChannel;

    private Color m_SelectColor = new Color(124 / 255f / 255 / 255f, 178 / 255f, 1);

    //===========================
    public RectTransform InputRect; //输入框区域
    public RectTransform EmptyRect; //不能输入时候的区域

    public TMP_InputField inputContent;
    public Button btnSelectFace; //选择表情按钮
    public Button btnSend; //发送按钮
    public RectTransform FaceRoot; //表情选择区域

    public Button btnFaceRootClose; //关闭表情区域
    public Button btnEquip; //显示装备区域
    public Button btnFace; //显示表情区域
    public RectTransform SelectFaceRoot;
    public RectTransform SelectEquipRoot;

    //假数据
    public Button btnFace1;
    public Button btnFace2;

    public Button btnEquip1;
    public Button btnEquip2;

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        simpleLoopListView.InitListView(0, OnGetSimpleItemByIndex);
        loopListView.InitListView(0, OnGetItemByIndex);

        btnSys.onClick.AddListener(() => { ChangeChannel(ChatChannel.Sys); });
        btnWorld.onClick.AddListener(() => { ChangeChannel(ChatChannel.World); });
        btnFriend.onClick.AddListener(() => { ChangeChannel(ChatChannel.Friend); });
        btnGand.onClick.AddListener(() => { ChangeChannel(ChatChannel.Gand); });
        btnLeague.onClick.AddListener(() => { ChangeChannel(ChatChannel.League); });
        btnTeam.onClick.AddListener(() => { ChangeChannel(ChatChannel.Team); });

        btnShowLeft.onClick.AddListener(() =>
        {
            if (m_IsMoveing) return;
            m_IsMoveing = true;
            LeftChatRoot.DOAnchorPos(new Vector2(500, 0), 0.5f).OnComplete(() => { m_IsMoveing = false; });
        });

        btnClose.onClick.AddListener(() =>
        {
            if (m_IsMoveing) return;
            m_IsMoveing = true;
            LeftChatRoot.DOAnchorPos(new Vector2(-100, 0), 0.5f).OnComplete(() => { m_IsMoveing = false; });
        });

        btnSelectFace.onClick.AddListener(() =>
        {
            FaceRoot.gameObject.SetActive(true);
        });

        btnFaceRootClose.onClick.AddListener(() =>
        {
            FaceRoot.gameObject.SetActive(false);
        });

        btnEquip.onClick.AddListener(() =>
        {
            SelectEquipRoot.gameObject.SetActive(true);
            SelectFaceRoot.gameObject.SetActive(false);
        });

        btnFace.onClick.AddListener(() =>
        {
            SelectEquipRoot.gameObject.SetActive(false);
            SelectFaceRoot.gameObject.SetActive(true);
        });

        btnFace1.onClick.AddListener(() =>
        {
            inputContent.text = inputContent.text + "<sprite=\"Face1\" index=0 anim=\"0,5,10\">";
        });
        btnFace2.onClick.AddListener(() =>
        {
            inputContent.text = inputContent.text + "<sprite=\"Face2\" index=0 anim=\"0,5,10\">";
        });

        btnEquip1.onClick.AddListener(() =>
        {
            inputContent.text = inputContent.text + "<link=\"Equip_01\"><u><i><#FF7000>[极品护腕]</u></i></color></link>";
        });
        btnEquip2.onClick.AddListener(() =>
        {
            inputContent.text = inputContent.text + "<link=\"Equip_02\"><u><i><#FF7000>[疾风衣]</u></i></color></link>";
        });

        btnSend.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(inputContent.text))
            {
                return;
            }
            C2WS_Chat_Data msg = new C2WS_Chat_Data();
            msg.Channel = m_CurrChatChannel;
            msg.RoleId = GameEntry.Data.UserDataManager.ShareUserData.CurrRoleId;
            msg.Level = GameEntry.Data.UserDataManager.Level;
            msg.NickName = GameEntry.Data.UserDataManager.NickName;
            msg.JobId = 1;
            msg.Head = "";
            msg.Content = inputContent.text;
            GameEntry.Data.ChatDataManager.SendChatMsg(msg);

            inputContent.text = "";
        });
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        GameEntry.Event.CommonEvent.AddEventListener(CommonEventId.OnPushChatMsg, OnPushChatMsg);

        SetSimpleLoopListViewData();

        ChangeChannel(ChatChannel.World);
    }

    private void SetButtonColor(Button btn, Color color)
    {
        btn.GetComponent<Image>().color = color;
    }

    private void ChangeChannel(ChatChannel chatChannel)
    {
        if (m_CurrChatChannel == chatChannel)
        {
            return;
        }
        m_CurrChatChannel = chatChannel;

        SetButtonColor(btnSys, Color.white);
        SetButtonColor(btnWorld, Color.white);
        SetButtonColor(btnFriend, Color.white);
        SetButtonColor(btnGand, Color.white);
        SetButtonColor(btnLeague, Color.white);
        SetButtonColor(btnTeam, Color.white);

        switch (chatChannel)
        {
            case ChatChannel.Sys:
                SetButtonColor(btnSys, m_SelectColor);

                InputRect.gameObject.SetActive(false);
                EmptyRect.gameObject.SetActive(true);
                break;
            case ChatChannel.World:
                SetButtonColor(btnWorld, m_SelectColor);
                InputRect.gameObject.SetActive(true);
                EmptyRect.gameObject.SetActive(false);
                break;
            case ChatChannel.Friend:
                SetButtonColor(btnFriend, m_SelectColor);
                InputRect.gameObject.SetActive(true);
                EmptyRect.gameObject.SetActive(false);
                break;
            case ChatChannel.Gand:
                SetButtonColor(btnGand, m_SelectColor);
                InputRect.gameObject.SetActive(true);
                EmptyRect.gameObject.SetActive(false);
                break;
            case ChatChannel.League:
                SetButtonColor(btnLeague, m_SelectColor);
                InputRect.gameObject.SetActive(true);
                EmptyRect.gameObject.SetActive(false);
                break;
            case ChatChannel.Team:
                SetButtonColor(btnTeam, m_SelectColor);
                InputRect.gameObject.SetActive(true);
                EmptyRect.gameObject.SetActive(false);
                break;
        }

        LoadCurrChannelMsg();
    }

    /// <summary>
    /// 加载当前频道的消息
    /// </summary>
    private void LoadCurrChannelMsg()
    {
        switch (m_CurrChatChannel)
        {
            case ChatChannel.Sys:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.SysChatMsg;
                break;
            case ChatChannel.World:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.WorldChatMsg;
                break;
            case ChatChannel.Friend:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.FriendChatMsg;
                break;
            case ChatChannel.Gand:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.GandChatMsg;
                break;
            case ChatChannel.League:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.LeagueChatMsg;
                break;
            case ChatChannel.Team:
                GameEntry.Data.ChatDataManager.CurrChannelChatMsg = GameEntry.Data.ChatDataManager.TeamChatMsg;
                break;
        }

        loopListView.SetListItemCount(GameEntry.Data.ChatDataManager.CurrChannelChatMsg.Count);

        //移动到最后一条
        loopListView.MovePanelToItemIndex(GameEntry.Data.ChatDataManager.CurrChannelChatMsg.Count - 1, 0);
    }

    private void OnPushChatMsg(object userData)
    {
        SetSimpleLoopListViewData();

        loopListView.SetListItemCount(GameEntry.Data.ChatDataManager.CurrChannelChatMsg.Count);
        //移动到最后一条
        loopListView.MovePanelToItemIndex(GameEntry.Data.ChatDataManager.CurrChannelChatMsg.Count - 1, 0);
    }

    private void SetSimpleLoopListViewData()
    {
        simpleLoopListView.SetListItemCount(GameEntry.Data.ChatDataManager.SynthesisChatMsg.Count);

        //移动到最后一条
        simpleLoopListView.MovePanelToItemIndex(GameEntry.Data.ChatDataManager.SynthesisChatMsg.Count - 1, 0);
    }

    /// <summary>
    /// 获取综合频道的聊天内容
    /// </summary>
    /// <param name="arg1"></param>
    /// <param name="arg2"></param>
    /// <returns></returns>
    private LoopListViewItem2 OnGetSimpleItemByIndex(LoopListView2 listView, int index)
    {
        LoopListViewItem2 item = listView.NewListViewItem("ItemPrefab1");

        ChatSimpleItem itemScript = item.GetComponent<ChatSimpleItem>();

        itemScript.SetItemData(index);
        return item;
    }

    private LoopListViewItem2 OnGetItemByIndex(LoopListView2 listView, int index)
    {
        C2WS_Chat_Data data = GameEntry.Data.ChatDataManager.CurrChannelChatMsg[index];
        if (data == null)
        {
            return null;
        }

        LoopListViewItem2 item;
        if (data.Channel == ChatChannel.Sys)
        {
            item = listView.NewListViewItem("ItemPrefab0");
        }
        else
        {
            if (data.RoleId == GameEntry.Data.UserDataManager.ShareUserData.CurrRoleId)
            {
                item = listView.NewListViewItem("ItemPrefab2");
            }
            else
            {
                item = listView.NewListViewItem("ItemPrefab1");
            }
        }

        ChatItem itemScript = item.GetComponent<ChatItem>();

        itemScript.SetItemData(data, data.RoleId == GameEntry.Data.UserDataManager.ShareUserData.CurrRoleId);
        return item;
    }

    protected override void OnClose()
    {
        base.OnClose();
        GameEntry.Event.CommonEvent.RemoveEventListener(CommonEventId.OnPushChatMsg, OnPushChatMsg);
    }

    protected override void OnBeforeDestroy()
    {
        base.OnBeforeDestroy();

        simpleLoopListView = null;
    }
}
