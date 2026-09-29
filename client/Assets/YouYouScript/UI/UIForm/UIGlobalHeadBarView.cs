using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YouYou;
using YouYou.DataTable;

public class UIGlobalHeadBarView : MonoBehaviour
{
    public Image imgHP;
    public Text txtNickName;

    public Transform buffContainer;
    public YouYouImage imgBuffIcon;

    public void Init(string nickName, int maxHp, int currHp)
    {
        txtNickName.text = nickName;
        ChangeHp(maxHp, currHp);
    }

    public void ChangeHp(int maxHp, int currHp)
    {
        if (maxHp == 0)
        {
            imgHP.fillAmount = 0;
        }
        else
        {
            imgHP.fillAmount = (float)currHp / maxHp;
        }
    }

    public Dictionary<int, YouYouImage> buffDic = new Dictionary<int, YouYouImage>();

    public void ClearAllBuff()
    {
        foreach (var item in buffDic)
        {
            Destroy(item.Value.gameObject);
        }
        buffDic.Clear();
    }

    public void AddBuff(int buffId)
    {
        //如果buff不存在 才进行添加
        if (!buffDic.TryGetValue(buffId, out YouYouImage image))
        {
            DTBuff buffConfig = GameEntry.DataTable.BuffList.GetEntityValue(buffId);

            GameObject obj = Instantiate(imgBuffIcon.gameObject);
            obj.name = buffId.ToString();
            obj.transform.SetParent(buffContainer);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            obj.SetActive(true);

            image = obj.GetComponent<YouYouImage>();
            image.LoadImage(buffConfig.BuffIcon);

            buffDic.Add(buffId, image);
        }
    }

    public void RemoveBuff(int buffId)
    {
        if (buffDic.TryGetValue(buffId, out YouYouImage image))
        {
            buffDic.Remove(buffId);
            Destroy(image.gameObject);
        }
    }
}