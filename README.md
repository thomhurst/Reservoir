# Reservoir

**Stop allocating the same thing twice**

Reservoir is thread-safe object pooling for .NET with **0 B warm paths** and bounded shared
retention. It ships as a small runtime library with public, library-friendly types and specialized
generic policies.

[![NuGet](https://img.shields.io/nuget/v/Reservoir.svg)](https://www.nuget.org/packages/Reservoir)
[![CI/CD](https://github.com/thomhurst/Reservoir/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/thomhurst/Reservoir/actions/workflows/ci-cd.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-54e1b6.svg)](LICENSE)

```shell
dotnet add package Reservoir
```

Requires a .NET Standard 2.0-compatible runtime or later.

## Why Reservoir?

- **Zero general-purpose pool allocations when warm.** `ObjectPool<T,TPolicy>` rent and return reuse fixed slots without allocating nodes. Legacy collection fallbacks may trim or replace backing storage.
- **Bounded shared retention.** You choose the shared tier's maximum idle-object count. Manual
  rentals use the bounded shared tier by default; pass `threadLocalFastPath: true` to opt into
  retaining one additional object per thread that rents.
- **Capacity-aware storage.** Cache-line-separated slots keep small pools fast; dense striped storage keeps large async working sets scalable.
- **Library-friendly delivery.** One public assembly identity flows normally through `PackageReference` dependency graphs.
- **Scoped ownership.** Stack-only leases return rentals automatically when synchronous work leaves scope.

## Rent. Work. Return.

Define lifecycle behavior as a struct policy so the JIT can specialize and inline it:

```csharp
using Reservoir;

var pool = new ObjectPool<Buffer, BufferPolicy>(maxCapacity: 64);

using var lease = pool.RentScoped(out Buffer buffer);
buffer.Write(payload);

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

`Create()` handles a miss. `TryReset()` prepares an object for reuse or returns `false` to discard it. Discarded `IDisposable` objects are disposed automatically; implement `IPooledObjectDestroyPolicy<T>` for custom cleanup. The scoped lease guarantees return when control leaves the synchronous scope. It uses a per-pool thread-local fast path and retains one object per participating thread in addition to the bounded shared tier.

For performance-critical synchronous code, prefer `RentScoped(out T)` on .NET 10; its thread-local
path is faster and avoids the ownership validation needed by repeated `lease.Value` access. On
.NET 8, manual `Rent()` and `Return()` remain faster. Manual rental is also required when work
crosses an `await`. Manual rentals use the bounded shared tier by default; opt into the thread-local
path with `threadLocalFastPath: true` only when its same-thread reuse wins justify its lookup and
retention costs. Measure on target hardware when nanoseconds matter.

For work that crosses an `await`, use `Rent()` and return the object in `finally`. See the [quick start](https://thomhurst.github.io/Reservoir/docs/quick-start) for both patterns.

## Pools included

Reservoir includes ready-to-use pools for:

`List<T>` · `Dictionary<TKey,TValue>` · `HashSet<T>` · `Queue<T>` · `Stack<T>` · `StringBuilder` · `CancellationTokenSource`

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

Collections return empty. Oversized backing stores are discarded instead of retained. Each pool exposes a shared instance and constructors for custom limits.

For synchronous scopes, `RentScoped` uses a per-pool thread-local fast path and returns the
collection automatically:

```csharp
using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> values);
values.Add(42);
Consume(values);
```

The lease is stack-only and cannot cross an `await`. Manual `Rent` and `Return` keep using the
bounded shared pool for asynchronous ownership.

`CancellationTokenSourcePool.RentScoped` uses the same per-pool thread-local strategy while
preserving source reset and disposal semantics. Use `Rent()` or `RentLinked()` when ownership
crosses an async boundary.

For synchronous, thread-affine hot paths, each specialized collection pool also exposes an
opt-in `ThreadLocalShared` facade:

```csharp
List<int> values = ListPool<int>.ThreadLocalShared.Rent();
try
{
    Consume(values);
}
finally
{
    ListPool<int>.ThreadLocalShared.Return(values);
}
```

It retains one item per participating thread, then falls back to the bounded `Shared` pool.
This improves same-thread reuse but can retain items on idle threads and is not globally bounded
by `Shared.MaximumRetained`.

## The ownership rule

> Returning an object transfers ownership to the pool. Do not touch it, return it twice, or return it to another pool.

Another thread may rent the same object immediately. Read the complete [ownership rules](https://thomhurst.github.io/Reservoir/docs/ownership-rules).

## Measured, not promised

<!-- BENCHMARK_RESULTS_START -->
BenchmarkDotNet 0.15.8 `MediumRun`, .NET 10.0.11, Windows 11, AMD EPYC 9V74:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 11.83 ns | 1.00 | 304 B |
| **Reservoir** | **10.36 ns** | **0.88** | **0 B** |
| `Microsoft.Extensions.ObjectPool` | 11.57 ns | 0.98 | 0 B |
| `ConcurrentBag<T>` pool | 25.19 ns | 2.13 | 0 B |
<!-- BENCHMARK_RESULTS_END -->

Every measured warm Reservoir path allocated **0 B**. Timings vary by machine; compare methods within the same run.

[See all benchmark results](https://thomhurst.github.io/Reservoir/docs/benchmarks) or reproduce them locally:

```shell
dotnet run -c Release -f net10.0 --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short --runtimes net8.0 net10.0 --apples
```

## When it fits

Choose Reservoir when you want bounded shared custom-object reuse, struct-policy specialization,
scoped leases, or capacity-aware storage.

Use `ArrayPool<T>` for raw arrays. Use `Microsoft.Extensions.ObjectPool` when integration with Microsoft Extensions abstractions matters more than Reservoir's specialized policies and leases.

## Go deeper

[Documentation](https://thomhurst.github.io/Reservoir/) · [Installation](https://thomhurst.github.io/Reservoir/docs/installation) · [API guide](https://thomhurst.github.io/Reservoir/docs/api/object-pools) · [Design notes](https://thomhurst.github.io/Reservoir/docs/design)

Reservoir is available under the [MIT license](LICENSE).
