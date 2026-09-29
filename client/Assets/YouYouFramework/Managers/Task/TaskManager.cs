using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>
    /// 任务管理器
    /// </summary>
    public class TaskManager: ManagerBase, IDisposable
    {
        /// <summary>
        /// 任务组列表
        /// </summary>
        private LinkedList<TaskGroup> m_TaskGroupList;
        private readonly HashSet<TaskGroup> m_LeasedGroups = new HashSet<TaskGroup>();
        private readonly HashSet<TaskRoutine> m_LeasedRoutines = new HashSet<TaskRoutine>();
        private PoolManager m_Pool;
        private bool m_Disposed;

        public TaskManager()
        {
            m_TaskGroupList = new LinkedList<TaskGroup>();
        }
        
        public override void Init()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(TaskManager));
        }

        public void OnUpdate()
        {
            if (m_Disposed) return;
            LinkedListNode<TaskGroup> taskGroup = m_TaskGroupList.First;
            while (taskGroup != null)
            {
                var next = taskGroup.Next;
                if (taskGroup.List == m_TaskGroupList) taskGroup.Value.OnUpdate();
                if (m_Disposed) return;
                taskGroup = next;
            }
        }

        /// <summary>
        /// 创建一个任务组
        /// </summary>
        /// <returns></returns>
        public TaskGroup CreateTaskGroup()
        {
            EnsureAvailable();
            TaskGroup taskGroup = m_Pool.DequeueClassObject<TaskGroup>();
            taskGroup.Prepare(this);
            m_LeasedGroups.Add(taskGroup);
            m_TaskGroupList.AddLast(taskGroup);
            return taskGroup;
        }

        /// <summary>
        /// 移除任务组
        /// </summary>
        /// <param name="taskGroup"></param>
        public void RemoveTaskGroup(TaskGroup taskGroup)
        {
            m_TaskGroupList.Remove(taskGroup);
        }

        /// <summary>
        /// 创建任务执行器
        /// </summary>
        /// <returns></returns>
        public TaskRoutine CreateTaskRoutine()
        {
            EnsureAvailable();
            var routine = m_Pool.DequeueClassObject<TaskRoutine>();
            routine.Prepare(this);
            m_LeasedRoutines.Add(routine);
            return routine;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            var errors = new List<Exception>();
            foreach (var group in new List<TaskGroup>(m_LeasedGroups))
                try { group.Dispose(); } catch (Exception error) { errors.Add(error); }
            // 同时清理已创建但尚未加入任务组的执行器。
            foreach (var routine in new List<TaskRoutine>(m_LeasedRoutines))
                try { routine.Cancel(); } catch (Exception error) { errors.Add(error); }
            m_TaskGroupList.Clear();
            m_LeasedGroups.Clear();
            m_LeasedRoutines.Clear();
            m_Pool = null;
            if (errors.Count > 0) throw new AggregateException("取消原框架任务时发生异常。", errors);
        }

        private void EnsureAvailable()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (m_Pool == null) m_Pool = GameEntry.Pool;
            if (m_Pool == null) throw new InvalidOperationException("TaskManager 需要已初始化的原 PoolManager。");
        }

        internal void TrackRoutine(TaskRoutine routine)
        {
            EnsureAvailable();
            routine.Attach(this);
            m_LeasedRoutines.Add(routine);
        }

        internal void DetachRoutine(TaskRoutine routine) { m_LeasedRoutines.Remove(routine); }

        internal void RecycleRoutine(TaskRoutine routine)
        {
            if (!m_Disposed) m_Pool?.EnqueueClassObject(routine);
        }

        internal void ReleaseGroup(TaskGroup group)
        {
            m_TaskGroupList.Remove(group);
            if (m_LeasedGroups.Remove(group) && !m_Disposed) m_Pool?.EnqueueClassObject(group);
        }
    }
}
