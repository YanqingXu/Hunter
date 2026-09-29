using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 通用事件
    /// </summary>
    public class CommonEvent: IDisposable
    {
        public delegate void OnActionHandler(object userData);
        public Dictionary<ushort, LinkedList<OnActionHandler>> dic = new Dictionary<ushort, LinkedList<OnActionHandler>>();

        #region AddEventListener 添加监听
        /// <summary>
        /// 添加监听
        /// </summary>
        /// <param name="key"></param>
        /// <param name="handler"></param>
        public void AddEventListener(ushort key, OnActionHandler handler)
        {
            LinkedList<OnActionHandler> lstHandler = null;
            dic.TryGetValue(key, out lstHandler);
            if (lstHandler == null)
            {
                lstHandler = new LinkedList<OnActionHandler>();
                dic[key] = lstHandler;
            }

            lstHandler.AddLast(handler);
        }
        #endregion

        #region RemoveEventListener 移除监听
        /// <summary>
        /// 移除监听
        /// </summary>
        /// <param name="key"></param>
        /// <param name="handler"></param>
        public void RemoveEventListener(ushort key, OnActionHandler handler)
        {
            LinkedList<OnActionHandler> lstHandler = null;
            dic.TryGetValue(key, out lstHandler);

            if (lstHandler != null)
            {
                lstHandler.Remove(handler);
                if (lstHandler.Count == 0)
                {
                    dic.Remove(key);
                }
            }
        }
        #endregion

        #region Dispatch 派发
        /// <summary>
        /// 派发
        /// </summary>
        /// <param name="key"></param>
        /// <param name="p"></param>
        public void Dispatch(ushort key, object userData = null)
        {
            LinkedList<OnActionHandler> lstHandler = null;
            dic.TryGetValue(key, out lstHandler);

            if (lstHandler != null)
            {
                for (LinkedListNode<OnActionHandler> curr = lstHandler.First; curr != null;)
                {
                    LinkedListNode<OnActionHandler> next = curr.Next;
                    curr.Value?.Invoke(userData);
                    curr = next;
                }
            }
        }
        #endregion

        public void Dispose()
        {
            // Dispatch keeps the next node on its stack, and callers can retain dic's lists.
            foreach (LinkedList<OnActionHandler> handlers in dic.Values)
            {
                if (handlers == null) continue;
                for (LinkedListNode<OnActionHandler> node = handlers.First; node != null; node = node.Next)
                    node.Value = null;
                handlers.Clear();
            }
            dic.Clear();
        }
    }
}
