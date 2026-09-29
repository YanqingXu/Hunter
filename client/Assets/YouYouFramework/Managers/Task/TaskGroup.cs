using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 任务组
    /// </summary>
    public class TaskGroup : IDisposable
    {
        /// <summary>
        /// 任务列表
        /// </summary>
        private LinkedList<TaskRoutine> m_TaskRoutineList;

        /// <summary>
        /// 任务组完成
        /// </summary>
        public BaseAction OnComplete;

        /// <summary>
        /// 是否并发执行
        /// </summary>
        private bool m_IsConcurrency = false;
        private TaskManager m_Owner;
        private bool m_Running;
        private bool m_Disposed;

        public TaskGroup()
        {
            m_TaskRoutineList = new LinkedList<TaskRoutine>();
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Running = false;
            OnComplete = null;
            try { CreateAllTask(); }
            finally
            {
                var owner = m_Owner;
                m_Owner = null;
                owner?.ReleaseGroup(this);
            }
        }

        internal void Prepare(TaskManager owner)
        {
            m_TaskRoutineList.Clear();
            OnComplete = null;
            m_Owner = owner;
            m_Running = false;
            m_Disposed = false;
            m_IsConcurrency = false;
            m_TotalCount = m_CurrCount = 0;
        }

        public void AddTask(TaskRoutine routine)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(TaskGroup));
            if (m_Running) throw new InvalidOperationException("运行中的任务组不能追加执行器。");
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            if (routine.Group != null) throw new InvalidOperationException("任务执行器已经属于一个任务组。");
            m_Owner?.TrackRoutine(routine);
            routine.Group = this;
            m_TaskRoutineList.AddLast(routine);
        }

        /// <summary>
        /// 清空所有任务
        /// </summary>
        public void CreateAllTask()
        {
            m_Running = false;
            m_TotalCount = m_CurrCount = 0;
            var errors = new List<Exception>();
            while (m_TaskRoutineList.First != null)
            {
                var routine = m_TaskRoutineList.First.Value;
                m_TaskRoutineList.RemoveFirst();
                routine.Group = null;
                try { routine.Cancel(); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) throw new AggregateException("取消任务组时发生异常。", errors);
        }

        public void OnUpdate()
        {
            if (!m_Running || m_Disposed) return;
            LinkedListNode<TaskRoutine> routine = m_TaskRoutineList.First;
            while (routine != null)
            {
                var next = routine.Next;
                try
                {
                    if (routine.List == m_TaskRoutineList) routine.Value.OnUpdate();
                }
                catch
                {
                    Dispose();
                    throw;
                }
                if (m_Disposed || !m_Running) return;
                routine = next;
            }
        }

        /// <summary>
        /// 执行任务
        /// </summary>
        public void Run(bool isConcurrency = false)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(TaskGroup));
            if (m_Running) return;
            m_Running = true;
            m_IsConcurrency = isConcurrency;
            m_TotalCount = m_TaskRoutineList.Count;
            m_CurrCount = 0;
            try
            {
                if (m_IsConcurrency) ConcurrencyTask();
                else CheckTask();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 检查任务
        /// </summary>
        private void CheckTask()
        {
            LinkedListNode<TaskRoutine> curr = m_TaskRoutineList.First;
            if (curr != null)
            {
                curr.Value.OnComplete = () =>
                {
                    if (m_Disposed || curr.List != m_TaskRoutineList) return;
                    m_TaskRoutineList.Remove(curr);
                    curr.Value.Group = null;
                    CheckTask();
                };
                curr.Value.Enter();
            }
            else
            {
                Finish();
            }
        }

        private int m_TotalCount = 0;
        private int m_CurrCount = 0;

        /// <summary>
        /// 并发执行任务
        /// </summary>
        private void ConcurrencyTask()
        {
            m_TotalCount = m_TaskRoutineList.Count;
            m_CurrCount = 0;
            if (m_TotalCount == 0) { Finish(); return; }

            LinkedListNode<TaskRoutine> routine = m_TaskRoutineList.First;
            while (routine != null)
            {
                LinkedListNode<TaskRoutine> next = routine.Next;
                var routine1 = routine;
                routine.Value.OnComplete = () =>
                {
                    if (m_Disposed || routine1.List != m_TaskRoutineList) return;
                    m_TaskRoutineList.Remove(routine1);
                    routine1.Value.Group = null;
                    CheckConcurrencyTaskComplete();
                };
                routine.Value.Enter();
                if (m_Disposed || !m_Running) return;
                routine = next;
            }
        }

        private void CheckConcurrencyTaskComplete()
        {
            m_CurrCount++;
            //Debug.LogError("m_CurrCount=" + m_CurrCount);
            //Debug.LogError("m_TotalCount=" + m_TotalCount);
            if (m_CurrCount == m_TotalCount)
            {
                Finish();
            }
        }

        private void Finish()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Running = false;
            var owner = m_Owner;
            var completed = OnComplete;
            OnComplete = null;
            owner?.RemoveTaskGroup(this);
            try { completed?.Invoke(); }
            finally
            {
                m_Owner = null;
                if (owner != null) owner.ReleaseGroup(this);
                else GameEntry.Pool?.EnqueueClassObject(this);
            }
        }
    }
}
