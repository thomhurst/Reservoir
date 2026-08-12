---
title: Cancellation token pooling
description: Choose and safely use pooled CancellationTokenSource instances.
---

# Cancellation token pooling

`CancellationTokenSourcePool` reuses sources that finish without being canceled. It removes their timers and registrations before reuse, eliminating the allocations normally made by `CancellationTokenSource`, `CancelAfter`, and `CancellationToken.Register`.

## Choose the fastest method

Use the method that matches the source's lifetime:

| Scenario | Recommended method | Why |
| --- | --- | --- |
| Synchronous scope | `CancellationTokenSourcePool.Shared.RentScoped()` | Fastest pooled method; stack-only lease guarantees return. |
| Scope crosses `await` | `CancellationTokenSourcePool.Shared.Rent()` | Source can cross the async boundary and returns itself when disposed. |
| Source usually cancels | `new CancellationTokenSource()` | A canceled source cannot be reused, so pooling adds overhead. |
| No timer or registration, allocation is acceptable | `new CancellationTokenSource()` | Lowest raw latency for a trivial source. |
| Repeated timers or registrations | Pool | Reuses their internal storage and avoids steady-state allocations. |

`RentScoped()` is the most performant **pooled** API. It is intended for synchronous scopes and cannot live across an `await`. `Rent()` is the correct pooled API for async work. Plain construction remains faster when measuring only creation and disposal, but allocates on every operation.

Representative BenchmarkDotNet `ShortRun` results on .NET 10.0.11 and an Intel Core i7-12700K:

| Workload | `new` | Pool | `new` allocation | Pool allocation |
| --- | ---: | ---: | ---: | ---: |
| Create/dispose | 2.85 ns | 16.53 ns | 48 B | 0 B |
| Create/dispose with scoped lease | 2.85 ns | 13.41 ns | 48 B | 0 B |
| Schedule unfired timer | 50.54 ns | 47.19 ns | 144 B | 0 B |
| Register callback | 27.53 ns | 28.74 ns | 192 B | 0 B |
| Cancel/dispose | 19.76 ns | 26.93 ns | 48 B | 56 B |

Nanosecond timings vary by machine and runtime. Compare rows by workload: pooling optimizes allocation pressure and reusable timer/registration state, not every isolated operation. Under contention, relative latency also varies with worker count; benchmark your production-shaped workload when latency is critical. See [Benchmarks](../benchmarks.md) to reproduce the suite.

## Async use

Rent the source directly when its lifetime crosses an `await`:

```csharp
using CancellationTokenSource source = CancellationTokenSourcePool.Shared.Rent();
source.CancelAfter(TimeSpan.FromSeconds(30));
await ProcessAsync(source.Token);
```

The rented source is a specialized subtype. Calling `Dispose()` offers it back to its originating pool; the pool retains it only when reset succeeds. Dispose each rental exactly once and only after all work using its token has completed.

## Synchronous use

Use a scoped lease when the source never crosses an async boundary:

```csharp
using var lease = CancellationTokenSourcePool.Shared.RentScoped(
    out CancellationTokenSource source);

source.CancelAfter(TimeSpan.FromSeconds(5));
RunSynchronousWork(source.Token);
```

The lease owns the source. Do not also dispose `source`. `RentScoped()` returns a stack-only `Lease`, so it also prevents the rental from escaping to the heap.

## What can be reused

On return, the pool calls `CancellationTokenSource.TryReset()`:

- `true`: cancellation has not occurred; timers are disarmed, registrations are removed, and the source can be retained;
- `false`: cancellation occurred or reset is unsafe; the source is permanently disposed and discarded.

If the runtime does not expose `TryReset()`, returned sources are permanently disposed instead. This can occur when a .NET Standard 2.0 consumer runs on an older runtime.

Pooling therefore works best for timeout or speculative-cancellation sources that usually complete before cancellation. It provides little benefit when cancellation is the normal outcome.

Linked sources created by `CancellationTokenSource.CreateLinkedTokenSource` are ordinary sources. They do not come from Reservoir and must be disposed normally.

## Ownership and concurrency

Before disposing a rental or its lease, ensure there are:

- no outstanding token users;
- no concurrent `Cancel` or `CancelAfter` calls;
- no concurrent registration or disposal operations.

`TryReset()` is not thread-safe with concurrent source use. Disposal transfers ownership to the pool: do not access the source, its token, a registration, or another source alias afterward. These rules apply even when the pool itself is shared safely between threads.

## Shared and dedicated pools

Use `CancellationTokenSourcePool.Shared` for most applications. Create a dedicated pool to isolate retention or set a workload-specific limit:

```csharp
using var pool = new CancellationTokenSourcePool(maxCapacity: 32);
```

`maxCapacity` limits idle retained sources, not simultaneous rentals. `Clear()` permanently disposes retained sources while leaving the pool usable. `Dispose()` drains and closes a dedicated pool; outstanding rentals are permanently disposed when returned.

Calling `CancellationTokenSourcePool.Shared.Dispose()` only clears retained sources. It deliberately does not close the process-wide shared pool.
