using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static MyCommonEnum;

namespace YouYou
{
    /// <summary>
    /// 精灵基类
    /// </summary>
    public class BaseSprite : MonoBehaviour
    {
        /// <summary>
        /// 服务器角色编号
        /// </summary>
        public long ServerRoleId;

        /// <summary>
        /// 当前角色类型
        /// </summary>
        public RoleType CurrRoleType;

        private void Awake()
        {
            OnAwake();
        }

        private void Start()
        {
            OnInit();
            OnOpen();
        }

        private void OnDestroy()
        {
            OnBeforDestroy();
        }

        public virtual void OnAwake() { }

        /// <summary>
        /// 自动初始化 注意和Init的区别
        /// </summary>
        protected void OnInit()
        {

        }

        /// <summary>
        /// 打开
        /// </summary>
        public virtual void OnOpen()
        {

        }

        /// <summary>
        /// 关闭
        /// </summary>
        public virtual void OnClose()
        {

        }

        /// <summary>
        /// 销毁
        /// </summary>
        protected virtual void OnBeforDestroy()
        {

        }
    }
}