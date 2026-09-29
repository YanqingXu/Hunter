using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YouYou;
using YouYou.Proto;

public class ChatSimpleItem : MonoBehaviour
{
    public Image imgChannel;
    public Text txtChannel;

    public RectTransform msgroot;
    public TMP_Text txtContent;

    public void SetItemData(int index)
    {
        C2WS_Chat_Data msg = GameEntry.Data.ChatDataManager.SynthesisChatMsg[index];

        imgChannel.color = GameEntry.Data.ChatDataManager.ChatChannelColor[msg.Channel];
        txtChannel.text= GameEntry.Data.ChatDataManager.ChatChannelText[msg.Channel];

        string str = string.Format("<#40a0ff>[{0}]</color>{1}", msg.NickName, msg.Content);
        txtContent.SetText(str);
        txtContent.ForceMeshUpdate(); //刷新网格 这个方法非常重要 否则下面的 bounds 都是 0

        Vector2 msgrootSize = msgroot.sizeDelta;

        msgrootSize.y = txtContent.textBounds.size.y + 10;
        //设置msgroot的高度
        msgroot.sizeDelta = msgrootSize;

        RectTransform tf = gameObject.GetComponent<RectTransform>();
        float y = msgrootSize.y;
        if (y < 32)
        {
            y = 32;
        }

        tf.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y);
    }
}