/*
┌────────────────────────────┐
│　Description: GameObject事件器
│　Remark: 
└────────────────────────────┘
┌──────────────┐                                   
│　ClassName: EventMapper
└──────────────┘
*/
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Lin.Runtime.Tool
{
    public class EventMapper : MonoBehaviour
    {
        // ponytail: List 查找和增删为 O(n)；订阅变更成为热点时，再按委托维护索引。
        private Dictionary<Type, List<Delegate>> eventMap;

        private void Awake()
        {
            eventMap = new Dictionary<Type, List<Delegate>>();
        }

        private void OnDestroy()
        {
            foreach (var handlers in eventMap.Values)
            {
                ListPool<Delegate>.Release(handlers);
            }

            eventMap.Clear();
        }

        public void Register<T>(Action<T> action) where T : struct
        {
            var type = typeof(T);
            if (!eventMap.TryGetValue(type, out var handlers))
            {
                handlers = ListPool<Delegate>.Get();
                eventMap.Add(type, handlers);
            }

            if (!handlers.Contains(action))
                handlers.Add(action);
        }

        public void Deregister<T>(Action<T> action) where T : struct
        {
            var type = typeof(T);
            if (!eventMap.TryGetValue(type, out var handlers))
                return;

            handlers.Remove(action);
        }

        public void Dispatch<T>(T args) where T : struct
        {
            var type = args.GetType();
            if (!eventMap.TryGetValue(type, out var handlers))
                return;

            // 回调可能增删 handlers；使用快照保证本轮派发遍历不受影响
            using (ListPool<Delegate>.Get(out var snapshot))
            {
                snapshot.AddRange(handlers);
                foreach (var action in snapshot)
                    (action as Action<T>)(args);
            }
        }
    }
}
