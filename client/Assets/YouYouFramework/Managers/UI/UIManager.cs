using System.Collections.Generic;
using System.Text;
using UnityEngine;
using YouYou.DataTable;

namespace YouYou
{
    public class UIManager
    {
        private bool m_Disposed;
        /// <summary>
        /// 已经打开的UI链表
        /// </summary>
        private LinkedList<UIFormBase> m_OpenUIFormList;

        /// <summary>
        /// 反切UI栈
        /// </summary>
        private Stack<UIFormBase> m_ReverseChangeUIStack;

        /// <summary>
        /// 正在加载中的UI窗口
        /// </summary>
        private LinkedList<int> m_LoadingUIFormList;

        public UIManager()
        {
            m_OpenUIFormList = new LinkedList<UIFormBase>();
            m_ReverseChangeUIStack = new Stack<UIFormBase>();
            m_LoadingUIFormList = new LinkedList<int>();
        }

        #region OpenUIForm 打开UI窗体
        /// <summary>
        /// 打开UI窗体
        /// </summary>
        /// <param name="uiFormId">formId</param>
        /// <param name="userData"></param>
        internal void OpenUIForm(int uiFormId, object userData = null, BaseAction<UIFormBase> onOpen = null)
        {
            if (m_Disposed) throw new System.ObjectDisposedException(nameof(UIManager));
            //1.读表

            DTSys_UIForm? entity = GameEntry.DataTable.Sys_UIFormList.GetEntity(uiFormId);
            if (entity == null)
            {
                return;
            }

            DTSys_UIForm sys_UIForm = entity.Value;

            //判断UI多实例
            if (!sys_UIForm.CanMulit && IsExists(uiFormId))
            {
                return;
            }

            #region 从UI池中获得UI
            UIFormBase formBase = GameEntry.UI.Dequeue(uiFormId);
            if (formBase == null)
            {
                if (IsLoading(uiFormId)) return;
                m_LoadingUIFormList.AddLast(uiFormId);

                string assetPath = string.Empty;
                switch (GameEntry.CurrLanguage)
                {
                    case YouYouLanguage.Chinese:
                        assetPath = sys_UIForm.AssetPathChinese;
                        break;
                    case YouYouLanguage.English:
                        assetPath = sys_UIForm.AssetPathEnglish;
                        break;
                }

                LoadUIAsset(assetPath, (ResourceEntity resourceEntity) =>
                {
                    if (m_Disposed) return;
                    GameObject uiObj = Object.Instantiate((Object)resourceEntity.Target) as GameObject;

                    //把克隆出来的资源 加入实例资源池
                    GameEntry.Pool.RegisterInstanceResource(uiObj.GetInstanceID(), resourceEntity);

                    uiObj.transform.SetParent(GameEntry.UI.GetUIGroup(sys_UIForm.UIGroupId).Group);
                    uiObj.transform.localPosition = Vector3.zero;
                    uiObj.transform.localScale = Vector3.one;

                    RectTransform rectTransform = uiObj.GetComponent<RectTransform>();
                    rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, 0);
                    rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0, 0);
                    rectTransform.anchorMin = Vector2.zero;
                    rectTransform.anchorMax = Vector2.one;

                    formBase = uiObj.GetComponent<UIFormBase>();
                    formBase.Init(uiFormId, sys_UIForm, sys_UIForm.UIGroupId, sys_UIForm.DisableUILayer == 1, sys_UIForm.IsLock == 1, userData, () =>
                    {
                        OpenUI(sys_UIForm, formBase, onOpen);
                    });
                    m_OpenUIFormList.AddLast(formBase);
                    m_LoadingUIFormList.Remove(uiFormId);
                });
            }
            else
            {
                formBase.Open(userData);
                m_OpenUIFormList.AddLast(formBase);
                GameEntry.UI.ShowUI(formBase);
                OpenUI(sys_UIForm, formBase, onOpen);
            }
            #endregion
        }

        private void OpenUI(DTSys_UIForm sys_UIForm, UIFormBase formBase, BaseAction<UIFormBase> onOpen)
        {
            //判断反切UI
            UIFormShowMode uIFormShowMode = (UIFormShowMode)sys_UIForm.ShowMode;
            if (uIFormShowMode == UIFormShowMode.ReverseChange)
            {
                //如果之前栈里边有UI
                if (m_ReverseChangeUIStack.Count > 0)
                {
                    //从栈顶拿到UI
                    UIFormBase topUIForm = m_ReverseChangeUIStack.Peek();

                    //禁用 冻结
                    GameEntry.UI.HideUI(topUIForm);
                }

                //把自己加入栈
                m_ReverseChangeUIStack.Push(formBase);
            }

            onOpen?.Invoke(formBase);
        }
        #endregion

        #region LoadUIAsset 加载UI资源
        /// <summary>
        /// 加载UI资源
        /// </summary>
        /// <param name="assetPath"></param>
        /// <returns></returns>
        private void LoadUIAsset(string assetPath, BaseAction<ResourceEntity> onComplete)
        {
            string str = assetPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(str) || !str.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) ||
                !str.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder sbr = StringHelper.PoolNew();
                str = sbr.AppendFormatNoGC("Assets/Download/UI/UIPrefab/{0}.prefab", assetPath).ToString();
                StringHelper.PoolDel(ref sbr);
            }
            GameEntry.Resource.ResourceLoaderManager.LoadMainAsset(AssetCategory.UIPrefab, str, (ResourceEntity resourceEntity) =>
            {
                onComplete?.Invoke(resourceEntity);
            });
        }
        #endregion

        #region IsExists 检查UI是否已经打开
        /// <summary>
        /// 检查UI是否已经打开
        /// </summary>
        /// <param name="uiformId"></param>
        /// <returns></returns>
        public bool IsExists(int uiformId)
        {
            for (LinkedListNode<UIFormBase> curr = m_OpenUIFormList.First; curr != null; curr = curr.Next)
            {
                if (curr.Value.UIFormId == uiformId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查UI正在加载中
        /// </summary>
        private bool IsLoading(int uiFormId)
        {
            for (LinkedListNode<int> curr = m_LoadingUIFormList.First; curr != null; curr = curr.Next)
            {
                if (curr.Value == uiFormId)
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region CloseUIForm 关闭UI
        /// <summary>
        /// 根据UIFormId关闭UI
        /// </summary>
        /// <param name="uiformId"></param>
        internal void CloseUIForm(int uiformId)
        {
            for (LinkedListNode<UIFormBase> curr = m_OpenUIFormList.First; curr != null; curr = curr.Next)
            {
                if (curr.Value.UIFormId == uiformId)
                {
                    CloseUIForm(curr.Value);
                    break;
                }
            }
        }

        /// <summary>
        /// 根据InstanceID关闭UI
        /// </summary>
        /// <param name="uiformId"></param>
        internal void CloseUIFormByInstanceID(int instanceID)
        {
            for (LinkedListNode<UIFormBase> curr = m_OpenUIFormList.First; curr != null; curr = curr.Next)
            {
                if (curr.Value.gameObject.GetInstanceID() == instanceID)
                {
                    CloseUIForm(curr.Value);
                    break;
                }
            }
        }

        internal void CloseUIForm(UIFormBase formBase)
        {
            if (m_Disposed || !formBase) return;
            m_OpenUIFormList.Remove(formBase);
            formBase.ToClose();

            //判断反切UI
            UIFormShowMode uIFormShowMode = (UIFormShowMode)formBase.SysUIForm.ShowMode;
            if (uIFormShowMode == UIFormShowMode.ReverseChange)
            {
                m_ReverseChangeUIStack.Pop();

                if (m_ReverseChangeUIStack.Count > 0)
                {
                    UIFormBase topForms = m_ReverseChangeUIStack.Peek();
                    GameEntry.UI.ShowUI(topForms);
                }
            }
        }
        #endregion

        internal void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            while (m_OpenUIFormList.Count > 0)
            {
                var form = m_OpenUIFormList.Last.Value;
                m_OpenUIFormList.RemoveLast();
                if (!form) continue;
                try { form.DestroyForShutdown(true); }
                catch (System.Exception exception) { Debug.LogException(exception); }
            }
            m_ReverseChangeUIStack.Clear();
            m_LoadingUIFormList.Clear();
        }
    }
}
