using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 任务执行器
    /// </summary>
    public class TaskRoutine
    {
        /// <summary>
        /// 编号
        /// </summary>
        public int TaskRoutineId;
        
        /// <summary>
        /// 具体的任务
        /// </summary>
        public BaseAction CurrTask;

        /// <summary>
        /// 任务完成
        /// </summary>
        public BaseAction OnComplete;

        /// <summary>
        /// 停止任务
        /// </summary>
        public BaseAction StopTask;

        /// <summary>
        /// 是否完成
        /// </summary>
        public bool Complete { get; private set; }

        /// <summary>
        /// 任务数据
        /// </summary>
        public object TaskData;

        private TaskManager m_Owner;
        private bool m_Entered;
        private bool m_Returned;
        internal TaskGroup Group { get; set; }

        internal void Prepare(TaskManager owner)
        {
            Clear();
            m_Owner = owner;
            m_Returned = false;
        }

        internal void Attach(TaskManager owner)
        {
            if (m_Returned) throw new System.InvalidOperationException("任务执行器已经结束，请重新创建。");
            if (m_Owner != null && m_Owner != owner)
                throw new System.InvalidOperationException("任务执行器不能跨管理器共享。");
            m_Owner = owner;
        }

        /// <summary>
        /// 进入任务
        /// </summary>
        public void Enter()
        {
            if (m_Returned) throw new System.InvalidOperationException("任务执行器已经结束，请重新创建。");
            if (m_Entered) return;
            m_Entered = true;
            Complete = false;

            if (CurrTask != null)
            {
                CurrTask.Invoke();
            }
            else
            {
                Leave();
            }
        }
        
        public void OnUpdate()
        {
            if (m_Entered && !m_Returned && Complete)
            {
                var owner = m_Owner;
                var completed = OnComplete;
                m_Returned = true;
                m_Entered = false;
                Complete = false;
                OnComplete = null;
                StopTask = null;
                owner?.DetachRoutine(this);
                try { completed?.Invoke(); }
                finally
                {
                    Clear();
                    if (owner != null) owner.RecycleRoutine(this);
                    else GameEntry.Pool?.EnqueueClassObject(this);
                }
            }
        }

        /// <summary>
        /// 离开任务
        /// </summary>
        public void Leave()
        {
            if (m_Entered && !m_Returned) Complete = true;
        }

        internal void Cancel()
        {
            if (m_Returned) return;
            var owner = m_Owner;
            var stop = StopTask;
            m_Returned = true;
            m_Entered = false;
            Complete = false;
            OnComplete = null;
            StopTask = null;
            owner?.DetachRoutine(this);
            try { stop?.Invoke(); }
            finally { Clear(); }
            // Leave() 没有租约代数。取消后退役执行器，避免旧异步回调结束新的池租约。
        }

        private void Clear()
        {
            CurrTask = null;
            OnComplete = null;
            StopTask = null;
            TaskData = null;
            TaskRoutineId = 0;
            Complete = false;
            m_Entered = false;
            Group = null;
            m_Owner = null;
        }
    }
}
