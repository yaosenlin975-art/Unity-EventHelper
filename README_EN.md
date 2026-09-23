[简体中文](README.md) | [English](README_EN.md)

# Lin Runtime Event Helper

An independent Unity runtime event package. It provides `EventHelper`, a global event bus keyed by struct type, and `EventMapper`, a component-local event dispatcher attached to a `GameObject`. Both APIs can be used independently and have no dependency on other Lin framework modules.

The package ID is `com.lin.runtime-event-helper`. It supports Unity 2021.3 and has no additional UPM dependencies.

## Quick start

### Main APIs

| API | Behavior |
| --- | --- |
| `EventHelper.Register<T>(handler)` | Registers a global handler. `T` must be a struct. |
| `EventHelper.Deregister<T>(handler)` | Removes a global handler. Pass the same method group to remove it. |
| `args.Dispatch()` | Dispatches a global event through `EventHelper`; main-thread calls run synchronously, while worker-thread calls are queued asynchronously to the main thread. |
| `EventMapper.Register<T>(handler)` | Registers a handler on this `EventMapper` component. |
| `EventMapper.Deregister<T>(handler)` | Removes a local handler from this component. |
| `EventMapper.Dispatch(args)` | Synchronously dispatches only to handlers on this component. |

Both systems distinguish events by their struct type. Global and component-local handlers are independent. Registrations and removals made during a callback take effect after the current dispatch. Handler invocation order is unspecified.

### Simple example: global event

```csharp
using Lin.Runtime.Helper;

public struct HealthChanged
{
    public int Previous;
    public int Current;
}

public static class HealthEventExample
{
    public static void Subscribe()
    {
        EventHelper.Register<HealthChanged>(OnHealthChanged);
    }

    public static void Unsubscribe()
    {
        EventHelper.Deregister<HealthChanged>(OnHealthChanged);
    }

    public static void ChangeHealth()
    {
        new HealthChanged { Previous = 100, Current = 80 }.Dispatch();
    }

    private static void OnHealthChanged(HealthChanged args)
    {
        UnityEngine.Debug.Log($"Health: {args.Previous} -> {args.Current}");
    }
}
```

Call `Subscribe()` during initialization and `Unsubscribe()` when the receiver no longer needs events. The struct type identifies the event; add fields as needed by the project.

### Component-local events

Add `EventMapper` to the target `GameObject`, then register, remove, and dispatch events through that component:

```csharp
using Lin.Runtime.Tool;
using UnityEngine;

public struct AlertEvent
{
    public int Code;
}

public sealed class LocalEventExample : MonoBehaviour
{
    private EventMapper eventMapper;

    private void Start()
    {
        eventMapper = GetComponent<EventMapper>();
        eventMapper.Register<AlertEvent>(OnAlert);
    }

    private void OnDestroy()
    {
        if (eventMapper is not null)
            eventMapper.Deregister<AlertEvent>(OnAlert);
    }

    public void RaiseAlert()
    {
        eventMapper.Dispatch(new AlertEvent { Code = 1 });
    }

    private void OnAlert(AlertEvent args)
    {
        Debug.Log($"Local event received: {args.Code}");
    }
}
```

`EventMapper` dispatches only to handlers registered on the same component instance. Instances on different `GameObject`s have separate handler lists.

## Handlers and performance

- `T` must be a value type (`struct`), and handlers use `Action<T>`.
- Registering the same handler more than once keeps only one entry; pass the same handler method group to remove it.
- `EventHelper` rents its active handler list from Unity's `ListPool` while subscribers exist and returns it when empty. Pending add/remove lists are returned after applying changes.
- `EventMapper` handler lists are returned to `ListPool` when the component is destroyed; dispatch snapshots are also borrowed temporarily from the pool.

## Threading

- `EventHelper.Register`, `Deregister`, and `Dispatch` can be called from worker threads. Requests are posted to Unity's main-thread context, so the event table and object pools are accessed only on the main thread.
- Calls made on the main thread remain synchronous. Worker-thread calls return immediately; registration and removal requests are processed on the main thread and applied at the next dispatch start or current dispatch end. Worker-thread dispatch callbacks also run on the main thread.
- Exceptions from direct main-thread dispatch propagate to the caller. Exceptions from worker-thread dispatch are logged to the Unity Console on the main thread.
- `EventMapper` is not thread-safe and should only be called on the main thread.
