---
title: Collection and text pools
description: Reuse lists, dictionaries, sets, queues, stacks, and string builders.
---

# Collection and text pools

Specialized pools remove policy boilerplate for common mutable types. Each type exposes a process-wide `Shared` instance plus constructors for dedicated limits.

| Pool | Shared instance | Default largest retained capacity |
| --- | --- | ---: |
| `ListPool<T>` | `ListPool<T>.Shared` | 1,024 elements |
| `DictionaryPool<TKey,TValue>` | `DictionaryPool<TKey,TValue>.Shared` | 1,024 entries |
| `HashSetPool<T>` | `HashSetPool<T>.Shared` | 1,024 elements |
| `QueuePool<T>` | `QueuePool<T>.Shared` | 1,024 elements |
| `StackPool<T>` | `StackPool<T>.Shared` | 1,024 elements |
| `StringBuilderPool` | `StringBuilderPool.Shared` | 4,096 characters |

All rentals arrive empty. When the runtime exposes collection capacity, `Return` clears an instance, then retains it only when its backing capacity is at or below `MaximumRetainedCapacity`. Oversized instances are discarded without trimming.

Some .NET Standard 2.0-era runtimes do not expose collection capacity. On those runtimes, hash sets, queues, and stacks are trimmed after clearing so their backing stores remain bounded. Dictionaries are discarded on return because neither capacity inspection nor trimming is available. Lists and string builders expose capacity and retain normal behavior.

## Lists, queues, and stacks

```csharp
var listPool = new ListPool<int>(
    maxRetainedCapacity: 256,
    maxCapacity: 32);

List<int> list = listPool.Rent();
try
{
    list.Add(42);
}
finally
{
    listPool.Return(list);
}
```

`QueuePool<T>` and `StackPool<T>` use the same constructor shape.

## Dictionaries and hash sets

Dictionary and set pools preserve a comparer identity:

```csharp
var pool = new DictionaryPool<string, int>(
    comparer: StringComparer.OrdinalIgnoreCase,
    maxRetainedCapacity: 512,
    maxCapacity: 16);
```

`Comparer` exposes the configured comparer. An object with a different comparer is discarded instead of retained. `TKey` on `DictionaryPool<TKey,TValue>` must be `notnull`.

## StringBuilder

```csharp
StringBuilder builder = StringBuilderPool.Shared.Rent();
try
{
    builder.Append("request-").Append(requestId);
    return builder.ToString();
}
finally
{
    StringBuilderPool.Shared.Return(builder);
}
```

A builder is retained only when `Capacity <= MaximumRetainedCapacity` and `MaxCapacity == int.MaxValue`.

## Choose limits

`maxCapacity` controls how many empty instances stay cached. `maxRetainedCapacity` controls the largest backing store worth caching. Tune the first for peak simultaneous renters and the second for the common collection size, not an exceptional spike.

Specialized collection pools do not own external resources and do not expose `Clear` or `Dispose`. Use a custom `ObjectPool<T,TPolicy>` when lifecycle control is required.
