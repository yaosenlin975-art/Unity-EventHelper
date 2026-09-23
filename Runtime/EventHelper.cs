/*
┌────────────────────────────┐
│　Description: 事件调度 改
│　Remark: 
└────────────────────────────┘
┌──────────────┐                                   
│　ClassName: EventHelper
└──────────────┘
*/

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Pool;

namespace Lin.Runtime.Helper
{
    public static class EventHelper
    {
        private static volatile MainThreadContext mainThreadContext;
        private static readonly SendOrPostCallback PROCESS_EVENT_REQUEST = ProcessEventRequest;

        public static void Register<T>(Action<T> handler) where T : struct => EventMap<T>.Register(handler);

        public static void Deregister<T>(Action<T> handler) where T : struct => EventMap<T>.Deregister(handler);

        public static void Dispatch<T>(this T args) where T : struct => EventMap<T>.Dispatch(args);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRuntimeMainThreadContext() => CaptureMainThreadContext();

        internal static void CaptureMainThreadContext()
        {
            mainThreadContext = new MainThreadContext(SynchronizationContext.Current, Thread.CurrentThread.ManagedThreadId);
        }

        private static bool IsMainThread
        {
            get
            {
                var context = mainThreadContext;
                if (context is null)
                    throw new InvalidOperationException("EventHelper 主线程上下文尚未初始化。");

                return Thread.CurrentThread.ManagedThreadId == context.ThreadId;
            }
        }

        private static void PostEventRequest(IEventRequest request)
        {
            var context = mainThreadContext;
            if (context?.SynchronizationContext is null)
                throw new InvalidOperationException("EventHelper 未能获取 Unity 主线程 SynchronizationContext。");

            context.SynchronizationContext.Post(PROCESS_EVENT_REQUEST, request);
        }

        private static void ProcessEventRequest(object state)
        {
            try
            {
                ((IEventRequest)state).Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private interface IEventRequest
        {
            void Invoke();
        }

        private sealed class EventRequest<T> : IEventRequest where T : struct
        {
            private readonly EEventOperation operation;
            private readonly Action<T> handler;
            private readonly T args;

            public EventRequest(EEventOperation operation, Action<T> handler, T args)
            {
                this.operation = operation;
                this.handler = handler;
                this.args = args;
            }

            public void Invoke()
            {
                switch (operation)
                {
                    case EEventOperation.Register:
                        EventMap<T>.RegisterOnMainThread(handler);
                        break;
                    case EEventOperation.Deregister:
                        EventMap<T>.DeregisterOnMainThread(handler);
                        break;
                    case EEventOperation.Dispatch:
                        EventMap<T>.DispatchOnMainThread(args);
                        break;
                }
            }
        }

        private sealed class MainThreadContext
        {
            public readonly SynchronizationContext SynchronizationContext;
            public readonly int ThreadId;

            public MainThreadContext(SynchronizationContext synchronizationContext, int threadId)
            {
                SynchronizationContext = synchronizationContext;
                ThreadId = threadId;
            }
        }

        private enum EEventOperation
        {
            Register,
            Deregister,
            Dispatch
        }

        private static class EventMap<T> where T : struct
        {
            // ponytail: List 查找和增删为 O(n)；订阅变更成为热点时，再按委托维护索引。
            private static List<Action<T>> handlers;
            // 派发期间收集到的待处理增删,执行前后统一应用,避免 foreach 迭代中修改 handlers
            private static List<Action<T>> pendingAdds;
            private static List<Action<T>> pendingRemoves;

            public static void Register(Action<T> handler)
            {
                if (IsMainThread)
                {
                    RegisterOnMainThread(handler);
                    return;
                }

                PostEventRequest(new EventRequest<T>(EEventOperation.Register, handler, default));
            }

            public static void Deregister(Action<T> handler)
            {
                if (IsMainThread)
                {
                    DeregisterOnMainThread(handler);
                    return;
                }

                PostEventRequest(new EventRequest<T>(EEventOperation.Deregister, handler, default));
            }

            public static void Dispatch(T args)
            {
                if (IsMainThread)
                {
                    DispatchOnMainThread(args);
                    return;
                }

                PostEventRequest(new EventRequest<T>(EEventOperation.Dispatch, null, args));
            }

            public static void RegisterOnMainThread(Action<T> handler)
            {
                // 后调用的 Register 覆盖先调用的 Deregister(同一 handler)
                if (pendingRemoves is not null)
                {
                    pendingRemoves.Remove(handler);
                    if (pendingRemoves.Count == 0)
                    {
                        ListPool<Action<T>>.Release(pendingRemoves);
                        pendingRemoves = null;
                    }
                }

                if (handlers is not null && handlers.Contains(handler))
                    return;

                if (pendingAdds is null)
                    pendingAdds = ListPool<Action<T>>.Get();

                if (!pendingAdds.Contains(handler))
                    pendingAdds.Add(handler);
            }

            public static void DeregisterOnMainThread(Action<T> handler)
            {
                // 后调用的 Deregister 覆盖先调用的 Register(同一 handler)
                if (pendingAdds is not null)
                {
                    pendingAdds.Remove(handler);
                    if (pendingAdds.Count == 0)
                    {
                        ListPool<Action<T>>.Release(pendingAdds);
                        pendingAdds = null;
                    }
                }

                if (handlers is null || !handlers.Contains(handler))
                    return;

                if (pendingRemoves is null)
                    pendingRemoves = ListPool<Action<T>>.Get();

                if (!pendingRemoves.Contains(handler))
                    pendingRemoves.Add(handler);
            }

            public static void DispatchOnMainThread(T args)
            {
                // 执行前: 把上次派发累积的待处理增删先应用到 handlers
                ApplyPending();

                if (handlers is null)
                    return;

                // 执行: 此时 handlers 在整个迭代过程中不会被修改
                foreach (var handler in handlers)
                    handler(args);

                // 执行后: 把本次派发期间产生的 Register/Deregister 也应用,避免跨周期累积
                ApplyPending();
            }

            private static void ApplyPending()
            {
                if (pendingRemoves is not null)
                {
                    foreach (var h in pendingRemoves)
                        handlers.Remove(h);

                    ListPool<Action<T>>.Release(pendingRemoves);
                    pendingRemoves = null;
                }

                if (pendingAdds is not null)
                {
                    if (handlers is null)
                        handlers = ListPool<Action<T>>.Get();

                    foreach (var h in pendingAdds)
                    {
                        if (!handlers.Contains(h))
                            handlers.Add(h);
                    }

                    ListPool<Action<T>>.Release(pendingAdds);
                    pendingAdds = null;
                }

                if (handlers is not null && handlers.Count == 0)
                {
                    ListPool<Action<T>>.Release(handlers);
                    handlers = null;
                }
            }
        }
    }
}
