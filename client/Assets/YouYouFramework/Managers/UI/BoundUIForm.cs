using System;
using System.Collections.Generic;
using UnityEngine;

namespace YouYou
{
    /// <summary>保留预制体的命名组件绑定，供 C# 窗体直接访问。</summary>
    public abstract class BoundUIForm : UIFormBase
    {
        [Serializable] public class Binding { public string Name; public int Type; public UnityEngine.Object ComObj; }
        [Serializable] public class BindingGroup { public string Name; public Binding[] Components; }
        [SerializeField] private BindingGroup[] m_ComponentGroups;
        private readonly Dictionary<string, UnityEngine.Object> m_Components = new Dictionary<string, UnityEngine.Object>();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            m_Components.Clear();
            foreach (var group in m_ComponentGroups ?? Array.Empty<BindingGroup>())
                foreach (var item in group.Components ?? Array.Empty<Binding>())
                    m_Components.Add(item.Name, item.ComObj);
        }

        protected T Get<T>(string name) where T : UnityEngine.Object
        {
            if (m_Components.TryGetValue(name, out var obj) && obj is T typed) return typed;
            throw new InvalidOperationException($"{gameObject.name}: missing {typeof(T).Name} binding '{name}'.");
        }

        protected static void DispatchInt(ushort eventId, int value)
        {
            var arg = GameEntry.Pool.DequeueVarObject<VarInt>();
            arg.Value = value;
            try { GameEntry.Event.CommonEvent.Dispatch(eventId, arg); }
            finally { GameEntry.Pool.EnqueueVarObject(arg); }
        }

        protected override void OnBeforeDestroy()
        {
            m_Components.Clear();
            base.OnBeforeDestroy();
        }
    }
}
