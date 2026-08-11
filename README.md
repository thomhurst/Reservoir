# Reservoir

Reservoir is bounded, thread-safe object pooling for .NET with a **0 B warm rent/return path**. It ships as C# source, so the implementation compiles into your assembly: no runtime dependency, dependency conflict, or extra DLL.

```shell
dotnet add package Reservoir
```

Reservoir is a development dependency, so `PrivateAssets="all"` is automatic. Package source files join your project compilation, Reservoir types are `internal` by default, and no `Reservoir.dll` appears in build output. Requires .NET 10 and C# 12 or later.

[Full documentation](https://thomhurst.github.io/Reservoir/) · [Quick start](https://thomhurst.github.io/Reservoir/docs/quick-start) · [Design notes](https://thomhurst.github.io/Reservoir/docs/design) · [Benchmarks](https://thomhurst.github.io/Reservoir/docs/benchmarks)

## Quick start

Use a struct policy so the JIT can specialize and inline lifecycle calls:

```csharp
using Reservoir;

var pool = new ObjectPool<Buffer, BufferPolicy>(maxCapacity: 64);
Buffer buffer = pool.Rent();

try
{
    buffer.Write(payload);
}
finally
{
    pool.Return(buffer);
}

sealed class Buffer
{
    public int Length { get; set; }
    public void Write(ReadOnlySpan<byte> value) => Length += value.Length;
}

readonly struct BufferPolicy : IPooledObjectPolicy<Buffer>
{
    public Buffer Create() => new();

    public bool TryReset(Buffer buffer)
    {
        buffer.Length = 0;
        return true;
    }
}
```

`Create()` handles a miss. `TryReset()` prepares a return and may return `false` to discard it. `Destroy()` defaults to `IDisposable.Dispose()` and can be overridden for custom cleanup.

For synchronous scopes, a stack-only lease guarantees return:

```csharp
using var lease = pool.RentScoped(out Buffer buffer);
buffer.Write(payload);
```

Use manual `try`/`finally` when ownership crosses an `await`; `PooledLease` is a `ref struct` and cannot cross one.

## Built-in pools

```csharp
List<int> values = ListPool<int>.Shared.Rent();
try
{
    values.Add(42);
    Consume(values);
}
finally
{
    ListPool<int>.Shared.Return(values);
}
```

| Pool | Purpose | Default largest retained capacity |
| --- | --- | ---: |
| `ListPool<T>` | `List<T>` | 1,024 |
| `DictionaryPool<TKey,TValue>` | `Dictionary<TKey,TValue>` with optional comparer | 1,024 |
| `HashSetPool<T>` | `HashSet<T>` with optional comparer | 1,024 |
| `QueuePool<T>` | `Queue<T>` | 1,024 |
| `StackPool<T>` | `Stack<T>` | 1,024 |
| `StringBuilderPool` | `StringBuilder` | 4,096 |
| `CancellationTokenSourcePool` | Uncanceled timeout/registration sources | n/a |
| `ValueTaskSourcePool<T>` | Manual allocation-free async completions | n/a |

Collections arrive empty. Oversized backing stores are discarded rather than trimmed. Each pool has a `Shared` instance and constructors for custom retained-object and backing-capacity limits.

`CancellationTokenSourcePool` returns a rental to its originating pool when disposed:

```csharp
using CancellationTokenSource source = CancellationTokenSourcePool.Shared.Rent();
source.CancelAfter(TimeSpan.FromSeconds(30));
await ProcessAsync(source.Token);
```

It reuses a source only when `TryReset()` confirms cancellation never fired. Dispose it exactly once as sole owner, after all token reads and cancellation operations finish. See the [complete concurrency rules](https://thomhurst.github.io/Reservoir/docs/api/cancellation-token-sources).

For hand-written asynchronous operations, `ValueTaskSourcePool<T>` reuses
`IValueTaskSource<T>` implementations and returns each source automatically when its value task is
consumed:

```csharp
PooledValueTaskSource<int> source = ValueTaskSourcePool<int>.Shared.Rent();
ValueTask<int> operation = source.CreateValueTask();

BeginOperation(
    onSuccess: value => source.SetResult(value),
    onError: error => source.SetException(error));

int result = await operation;
```

Consume each value task exactly once. Never await copies concurrently, read `.Result` while the
operation is pending, or touch the source after completion. See the [complete value task source
rules](https://thomhurst.github.io/Reservoir/docs/api/value-task-sources).

## Core API

- `ObjectPool<T,TPolicy>`: generic struct-policy fast path.
- `ObjectPool<T>`: convenient `Func<T>` or interface-policy overload.
- `IResettable` + `ResettablePooledObjectPolicy<T>`: reset logic owned by the pooled type.
- `Rent`, `Return`, `RentScoped`: manual or lexical ownership.
- `Clear`: destroy retained objects while keeping a core pool usable.
- `Dispose`: drain and permanently close a core pool; later rents throw and later returns are destroyed.

Pools retain at most `maxCapacity` idle objects. Default retention is `Math.Max(32, 2 * Environment.ProcessorCount)`. This does not throttle concurrent rentals: size it for peak simultaneous holders. Specialized pools also accept `maxRetainedCapacity` to reject unusually large backing stores.

## Ownership rules

Returning an object transfers ownership to the pool. After `Return`:

- never touch the object;
- never return it twice;
- never return it to a different pool.

Another thread may rent it immediately. Debug builds detect double returns and wrong-pool returns. They also report rentals that become unreachable without return, including the rent-site stack trace, through `Trace` and `ObjectPoolDiagnostics.LeakDetected`.

Define `RESERVOIR_DIAGNOSTICS` to enable those checks in Release or staging builds. When neither it nor `DEBUG` is defined, diagnostic fields and calls are compiled out of the hot path.

Define `RESERVOIR_PUBLIC` when Reservoir types must appear in your assembly's public API:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);RESERVOIR_PUBLIC</DefineConstants>
</PropertyGroup>
```

## Benchmarks

BenchmarkDotNet 0.15.8 `ShortRun`, .NET 10.0.10, Windows 11, Intel Core i7-12700K:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 12.67 ns | 1.00 | 304 B |
| Reservoir | 11.83 ns | 0.93 | 0 B |
| `Microsoft.Extensions.ObjectPool` | 14.56 ns | 1.15 | 0 B |
| `ConcurrentBag<T>` pool | 39.48 ns | 3.12 | 0 B |

Every measured warm Reservoir path allocated 0 B. Run the suite:

```shell
dotnet run -c Release --project benchmarks/Reservoir.Benchmarks
```

Raw Markdown, CSV, and HTML exports—including 1–32 worker contention results—are under [`benchmarks/results/20260811-200439`](benchmarks/results/20260811-200439).

## Why Reservoir

The core uses a fixed, bounded slot array, per-thread stripe affinity, atomic exchange/compare-exchange operations, and cache-line-spaced logical slots. It has no global lock and allocates no nodes on a warm return. Struct policies expose concrete lifecycle calls to generic specialization.

Use `ArrayPool<T>` for raw arrays. Use `Microsoft.Extensions.ObjectPool` when ecosystem integration and a normal runtime dependency matter more. Use Reservoir when source ownership, bounded custom-object reuse, struct-policy specialization, scoped leases, and debug ownership diagnostics fit the application.

Reservoir is available under the [MIT license](LICENSE).
