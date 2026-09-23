[简体中文](README.md) | [English](README_EN.md)

# Lin Runtime Event Helper

这是一个独立的 Unity 运行时事件包，提供按结构体类型分发的全局事件总线 `EventHelper`，以及挂在 `GameObject` 上的局部事件器 `EventMapper`。两个 API 均可独立使用，不依赖 Lin 框架其他模块。

包名为 `com.lin.runtime-event-helper`，最低支持 Unity 2021.3，无额外 UPM 依赖。

## 快速上手

### 主要接口

| 接口 | 作用 |
| --- | --- |
| `EventHelper.Register<T>(handler)` | 注册全局事件处理器，`T` 必须是结构体。 |
| `EventHelper.Deregister<T>(handler)` | 注销全局事件处理器。传入同一个方法组以注销。 |
| `args.Dispatch()` | 通过 `EventHelper` 派发全局事件；主线程调用同步执行，后台线程调用异步排队到主线程。 |
| `EventMapper.Register<T>(handler)` | 在该 `EventMapper` 组件上注册局部事件处理器。 |
| `EventMapper.Deregister<T>(handler)` | 注销该组件上的局部处理器。 |
| `EventMapper.Dispatch(args)` | 只向该组件的处理器同步派发事件。 |

两个系统都以事件结构体类型区分消息。全局处理器和组件局部处理器互不影响。回调期间发生的注册或注销在本轮派发完成后生效；处理器调用顺序不作保证。

### 简单示例：全局事件

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
        UnityEngine.Debug.Log($"生命值：{args.Previous} -> {args.Current}");
    }
}
```

在初始化时调用 `Subscribe()`，对象不再接收事件时调用 `Unsubscribe()`。事件由结构体类型标识，字段可按项目需求扩展。

### 组件局部事件

将 `EventMapper` 添加到目标 `GameObject`，再从该组件注册、注销和派发事件：

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
        Debug.Log($"收到局部事件：{args.Code}");
    }
}
```

`EventMapper` 只派发给注册在同一组件实例上的处理器；不同 `GameObject` 上的实例各自维护列表。

## 处理器与性能

- `T` 必须是值类型（`struct`），处理器类型为 `Action<T>`。
- 同一个处理器重复注册只保留一份；注销时传入相同的处理器方法组。
- `EventHelper` 的活动处理器列表在有订阅者时从 Unity `ListPool` 借用，并在清空后归还；待添加和待移除列表在应用后归还。
- `EventMapper` 的处理器列表在组件销毁时归还 `ListPool`；派发快照也通过对象池临时借用。

## 线程语义

- `EventHelper` 的 `Register`、`Deregister`、`Dispatch` 可以从工作线程调用；请求会排入 Unity 主线程上下文执行，事件表和对象池只在主线程访问。
- 主线程调用仍同步执行；工作线程调用会立即返回，注册和注销请求由主线程处理，并在下一次派发开始或当前派发结束时应用；工作线程派发的回调也在主线程执行。
- 主线程直接派发时，回调异常会传回调用方；工作线程派发产生的异常会在主线程记录到 Unity Console。
- `EventMapper` 不提供多线程支持，应只在主线程调用。
