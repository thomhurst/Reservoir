---
title: Value task sources
description: Reuse manual async completion sources without warm-path allocations.
---

# Value task source pools

`ValueTaskSourcePool<T>` and `ValueTaskSourcePool` reuse manual async completion sources. They are
for I/O callbacks, signals, queues, and other hand-written operations that need to return
`ValueTask<T>` or `ValueTask` without allocating a `Task` or a fresh `IValueTaskSource` each time.

```csharp
PooledValueTaskSource<int> source = ValueTaskSourcePool<int>.Shared.Rent();
ValueTask<int> operation = source.CreateValueTask();

BeginOperation(
    onSuccess: value => source.SetResult(value),
    onError: error => source.SetException(error));

int result = await operation;
```

`CreateValueTask()` is equivalent to `new ValueTask<int>(source, source.Version)`. The version token
is part of the correctness contract: after consumption resets and returns a source, stale value
tasks throw instead of reading a later rental's result.

## Consumption rules

Each rental has one producer and one consumer:

- create one value task and await it exactly once;
- do not await copies concurrently;
- do not read `.Result` before `IsCompleted` is true;
- call either `SetResult` or `SetException` exactly once;
- after completion, the producer must not access the source again;
- never return the source manually—the consumer returns it inside `GetResult`.

These are `ValueTask` correctness rules, not debug-only checks. Reservoir keeps token validation and
an atomic consumption guard in Release builds.

## Continuations

The shared pools run continuations inline. A dedicated pool can schedule them asynchronously when
producer callbacks must not run consumer code:

```csharp
using var pool = new ValueTaskSourcePool<int>(
    maxCapacity: 64,
    runContinuationsAsynchronously: true);
```

Inline continuations avoid scheduling overhead. Asynchronous continuations reduce reentrancy and
stack-growth risks at the cost of thread-pool scheduling.

## Pool lifetime

`Clear()` drops retained sources but leaves a pool usable. `Dispose()` closes a dedicated pool;
outstanding operations can still be completed and consumed, but their sources are discarded on
return. Disposing `Shared` clears it without closing it.

## Scope

Use this pool for manual `IValueTaskSource` implementations. For allocation-sensitive `async`
methods themselves, .NET already provides `PoolingAsyncValueTaskMethodBuilder<T>` through
`AsyncMethodBuilderAttribute`; that is a separate mechanism and does not need this pool.
