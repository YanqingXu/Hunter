using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YouYou;
using YouYou.Proto;

public class ChatItem : MonoBehaviour, IPointerClickHandler
{
    public Image imgHead;
    public Text txtNickName;
    public Text txtLevel;

    public RectTransform msgroot;
    public RectTransform rectContentBG;
    public TMP_Text txtContent;

    public void OnPointerClick(PointerEventData eventData)
    {
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(txtContent, Input.mousePosition, GameEntry.CameraCtrl.UICamera);
        if (linkIndex != -1)
        {
            TMP_LinkInfo linkInfo = txtContent.textInfo.linkInfo[linkIndex];

            string linkId = linkInfo.GetLinkID();
            string[] arr = linkId.Split('_');
            string type = arr[0];
            string id = arr[1];

            Debug.LogFormat("type {0}", type);
            Debug.LogFormat("id {0}", id);
            //Debug.Log("Link ID: \"" + linkInfo.GetLinkID() + "\"   Link Text: \"" + linkInfo.GetLinkText() + "\"");
        }
    }

    public void SetItemData(C2WS_Chat_Data msg, bool isRight)
    {
        if (msg.Channel == ChatChannel.Sys)
        {
            string str = string.Format("<#40a0ff>[{0}]</color>{1}", msg.NickName, msg.Content);
            txtContent.SetText(str);
            txtContent.alignment = TextAlignmentOptions.TopLeft;
        }
        else
        {
            txtNickName.text = msg.NickName;
            txtLevel.text = msg.Level.ToString();
            txtContent.SetText(msg.Content);
            txtContent.alignment = TextAlignmentOptions.TopLeft;
        }

        txtContent.ForceMeshUpdate(); //刷新网格 这个方法非常重要 否则下面的 bounds 都是 0

        //文字背景区域
        Vector2 size = rectContentBG.sizeDelta;

        size.x = 295;


        if (isRight)
        {
            if (txtContent.textInfo.lineCount > 1)
            {
                txtContent.alignment = TextAlignmentOptions.TopLeft;
            }
            else
            {
                txtContent.alignment = TextAlignmentOptions.TopRight;
                size.x = txtContent.textBounds.size.x + 10;
            }
            txtContent.ForceMeshUpdate(); //刷新网格 这个方法非常重要 否则下面的 bounds 都是 0
        }
        else
        {
            if (txtContent.textInfo.lineCount == 1)
            {
                size.x = txtContent.textBounds.size.x + 10;
            }
        }

        size.y = txtContent.textBounds.size.y + 10;

        //让文字背景区域的高度和宽度相符
        rectContentBG.sizeDelta = size;

        Vector2 msgrootSize = msgroot.sizeDelta;

        msgrootSize.y = size.y;

        //设置msgroot的高度
        msgroot.sizeDelta = msgrootSize;

        RectTransform tf = gameObject.GetComponent<RectTransform>();
        float y = msgrootSize.y;

        if (msg.Channel == ChatChannel.Sys)
        {
            y += 16;
            if (y < 45)
            {
                y = 45;
            }
        }
        else
        {
            y += 50;
            if (y < 120)
            {
                y = 120;
            }
        }

        tf.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y);
    }
}
