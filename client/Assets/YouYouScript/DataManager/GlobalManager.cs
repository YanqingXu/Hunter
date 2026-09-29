using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YouYou;

public class GlobalManager
{
    public Transform HeadBarContainer;

    public GlobalManager()
    {
        headBarViewQueue = new Queue<UIGlobalHeadBarView>();
        hudTextQueue = new Queue<HUDText>();
    }

    #region 血条
    private Queue<UIGlobalHeadBarView> headBarViewQueue;

    /// <summary>
    /// 创建血条
    /// </summary>
    /// <param name="onComplete"></param>
    public void CreateHeadBarView(Action<UIGlobalHeadBarView> onComplete)
    {
        if (headBarViewQueue.Count > 0)
        {
            UIGlobalHeadBarView headBarView = headBarViewQueue.Dequeue();

            headBarView.transform.SetParent(GameEntry.Data.GlobalManager.HeadBarContainer);
            headBarView.transform.localScale = Vector3.one;
            headBarView.transform.localPosition = new Vector3(0, 9999, 0);
            headBarView.gameObject.SetActive(true);

            onComplete?.Invoke(headBarView);
        }
        else
        {
            //加载 角色控制器
            GameEntry.Pool.GameObjectSpawn(SysPrefabId.HeadBar, (Transform trans, bool isNewInstance) =>
            {
                UIGlobalHeadBarView headBarView = trans.GetComponent<UIGlobalHeadBarView>();

                headBarView.transform.SetParent(GameEntry.Data.GlobalManager.HeadBarContainer);
                headBarView.transform.localScale = Vector3.one;
                headBarView.transform.localPosition = new Vector3(0, 9999, 0);
                onComplete?.Invoke(headBarView);
            });
        }
    }

    /// <summary>
    /// 回收血条
    /// </summary>
    /// <param name="headBarView"></param>
    public void ReleaseHeadBarView(UIGlobalHeadBarView headBarView)
    {
        headBarView.ClearAllBuff();
        headBarView.gameObject.SetActive(false);
        headBarViewQueue.Enqueue(headBarView);
    }
    #endregion

    #region HUD
    private Queue<HUDText> hudTextQueue;

    /// <summary>
    /// 创建血条
    /// </summary>
    /// <param name="onComplete"></param>
    public void CreateHudText(Action<HUDText> onComplete)
    {
        if (hudTextQueue.Count > 0)
        {
            HUDText hudText = hudTextQueue.Dequeue();

            hudText.transform.SetParent(GameEntry.Data.GlobalManager.HeadBarContainer);
            hudText.transform.localScale = Vector3.one;
            hudText.transform.localPosition = new Vector3(0, 9999, 0);
            hudText.gameObject.SetActive(true);

            onComplete?.Invoke(hudText);
        }
        else
        {
            //加载 角色控制器
            GameEntry.Pool.GameObjectSpawn(SysPrefabId.HUDText, (Transform trans, bool isNewInstance) =>
            {
                HUDText hudText = trans.GetComponent<HUDText>();

                hudText.transform.SetParent(GameEntry.Data.GlobalManager.HeadBarContainer);
                hudText.transform.localScale = Vector3.one;
                hudText.transform.localPosition = new Vector3(0, 9999, 0);
                onComplete?.Invoke(hudText);
            });
        }
    }

    /// <summary>
    /// 回收
    /// </summary>
    /// <param name="headBarView"></param>
    public void ReleaseHudText(HUDText hudText)
    {
        hudText.transform.localPosition = new Vector3(0, 9999, 0);
        hudTextQueue.Enqueue(hudText);
    }
    #endregion
}
