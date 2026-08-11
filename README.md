# Reservoir

Reservoir provides bounded, thread-safe object pooling with an allocation-free warm rent/return path.

```csharp
var pool = new ObjectPool<MyBuffer, MyBufferPolicy>(maxCapacity: 64);
MyBuffer buffer = pool.Rent();

try
{
    // Use buffer.
}
finally
{
    pool.Return(buffer);
}
```

Implement `IPooledObjectPolicy<T>` on a struct to let the JIT specialize and inline policy calls. `Create()` supplies an object when the pool is empty. `TryReset()` prepares a returned object for reuse; returning `false` discards it.

Types designed for pooling can implement `IResettable` and use the constrained built-in policy. This avoids runtime type checks on the rent/return path:

```csharp
var pool = new ObjectPool<MyResettableBuffer, ResettablePooledObjectPolicy<MyResettableBuffer>>();
```

Discarded objects are disposed when they implement `IDisposable`. `Clear()` drains retained objects and disposes them while leaving the pool usable. `Dispose()` drains and permanently closes the pool; later returns are disposed immediately and later rents throw `ObjectDisposedException`.

Returning an object transfers ownership to the pool. Do not access it afterward and never return it twice. Another thread may rent it immediately after `Return()` completes.

Debug builds detect objects returned twice or returned to the wrong pool and throw `InvalidOperationException`. They also report rentals that become unreachable without being returned, including the rent-site stack trace, through `Trace` and `ObjectPoolDiagnostics.LeakDetected`. Define `RESERVOIR_DIAGNOSTICS` to enable the same checks in a Release or staging build. Diagnostics are compiled out when neither `DEBUG` nor `RESERVOIR_DIAGNOSTICS` is defined, leaving no fields or calls on the Release hot path.

Pools retain at most `maxCapacity` objects. Default capacity is `Math.Max(32, 2 * Environment.ProcessorCount)`. Size capacity for peak simultaneous holders, not request rate. When callers hold objects across `await`, use peak in-flight operations rather than processor count.

Use a scoped lease when the rental does not cross an `await`. Disposing the stack-only lease returns its value automatically:

```csharp
using var lease = pool.RentScoped();
MyBuffer buffer = lease.Value;
// Use buffer only while lease is alive.
```

For a direct local without a separate `Value` access:

```csharp
using var lease = pool.RentScoped(out MyBuffer buffer);
```

## Cancellation token sources

`CancellationTokenSourcePool` reuses sources only when
`CancellationTokenSource.TryReset()` confirms cancellation never fired. Canceled sources are
disposed and discarded. Timers and callbacks from an unfired rental are removed before reuse.

```csharp
CancellationTokenSourcePool pool = CancellationTokenSourcePool.Shared;
using CancellationTokenSource source = pool.Rent();
source.CancelAfter(TimeSpan.FromSeconds(30));
// Use source.Token.
```

Each rented source returns to its originating pool when disposed. Dispose it only after becoming
its sole owner again: no outstanding token readers and no concurrent `Cancel`, `CancelAfter`,
registration, or disposal operation may remain. Disposal races unsafely with those operations
because `TryReset()` is not thread-safe with concurrent use. Disposal transfers ownership to the
pool; dispose each rental exactly once, and do not use or dispose another alias afterward. Linked
sources created by `CancellationTokenSource.CreateLinkedTokenSource` are ordinary sources; dispose
them normally.

Dedicated pools own their retained sources. Call `Clear()` to release them while keeping the pool
usable, or dispose the pool when its lifetime ends. Disposing the process-wide shared pool only
clears its retained sources; it does not close the pool.

## Benchmarks

Run the full .NET 10 suite in Release mode:

```shell
dotnet run -c Release --project benchmarks/Reservoir.Benchmarks
```

Every warm pool path measured 0 B allocated per operation. Results below used
BenchmarkDotNet 0.15.8's `ShortRun` job on .NET 10.0.10, Windows 11, and an Intel
Core i7-12700K. Nanosecond results vary by machine; compare methods within a table.

### Core pool

The payload owns a 256-byte buffer. Lower ratio is better; `new` is the baseline.

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 12.67 ns | 1.00 | 304 B |
| Reservoir | 11.83 ns | 0.93 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 14.56 ns | 1.15 | 0 B |
| `ConcurrentBag<T>` pool | 39.48 ns | 3.12 | 0 B |

### Warm pool allocation guarantee

| Pool | Mean | Allocated |
| --- | ---: | ---: |
| `ObjectPool` | 11.52 ns | 0 B |
| `ListPool` | 13.13 ns | 0 B |
| `DictionaryPool` | 12.21 ns | 0 B |
| `HashSetPool` | 15.04 ns | 0 B |
| `QueuePool` | 13.62 ns | 0 B |
| `StackPool` | 13.86 ns | 0 B |
| `StringBuilderPool` | 14.96 ns | 0 B |

### Specialized workloads

| Workload | Baseline | Reservoir | Baseline allocated | Reservoir allocated |
| --- | ---: | ---: | ---: | ---: |
| `StringBuilder`, append 128 chars | 25.66 ns | 18.12 ns | 400 B | 0 B |
| `List<int>`, 8 items | 15.27 ns | 32.12 ns | 88 B | 0 B |
| `List<int>`, 128 items | 139.23 ns | 126.66 ns | 568 B | 0 B |
| `List<int>`, 2,048 items | 1,738.18 ns | 1,502.78 ns | 8,248 B | 0 B |

The single-thread TLS `StringBuilder` cache measured 5.52 ns and 0 B, as expected;
it trades away cross-thread reuse and bounded shared capacity. Scoped leases measured
12.67 ns versus 10.50 ns for manual rent/return, with 0 B allocated on both paths.

Raw Markdown, CSV, and HTML exports, including 1-32 worker contention results, are
checked in under [`benchmarks/results/20260811-200439`](benchmarks/results/20260811-200439).
