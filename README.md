# Reservoir

**Stop allocating the same thing twice.**

Reservoir is bounded, thread-safe object pooling for .NET with a **0 B warm rent/return path**. It ships as C# source, so the optimized code compiles into your assembly—no runtime dependency, version conflict, or extra DLL.

[![NuGet](https://img.shields.io/nuget/v/Reservoir.svg)](https://www.nuget.org/packages/Reservoir)
[![CI/CD](https://github.com/thomhurst/Reservoir/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/thomhurst/Reservoir/actions/workflows/ci-cd.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-54e1b6.svg)](LICENSE)

```shell
dotnet add package Reservoir
```

Requires .NET 10 and C# 12 or later.

## Why Reservoir?

- **Zero allocations when warm.** Rent and return reuse fixed slots without allocating nodes.
- **Bounded retention.** You choose the maximum number of idle objects; the pool cannot grow without limit.
- **Source-only delivery.** Reservoir stays private to your project and adds no runtime package or assembly.
- **Ownership guardrails.** Debug builds detect invalid returns and report leaked rentals.

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

`Create()` handles a miss. `TryReset()` prepares an object for reuse or returns `false` to discard it. The scoped lease guarantees return when control leaves the synchronous scope.

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

## The ownership rule

> Returning an object transfers ownership to the pool. Do not touch it, return it twice, or return it to another pool.

Another thread may rent the same object immediately. Debug diagnostics make violations visible and report rentals that become unreachable without being returned. Read the complete [ownership rules](https://thomhurst.github.io/Reservoir/docs/ownership-rules).

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
dotnet run -c Release --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short
```

## When it fits

Choose Reservoir when you want bounded custom-object reuse, source ownership, struct-policy specialization, scoped leases, or debug ownership diagnostics.

Use `ArrayPool<T>` for raw arrays. Use `Microsoft.Extensions.ObjectPool` when ecosystem integration matters more than source-only delivery.

## Go deeper

[Documentation](https://thomhurst.github.io/Reservoir/) · [Installation](https://thomhurst.github.io/Reservoir/docs/installation) · [API guide](https://thomhurst.github.io/Reservoir/docs/api/object-pools) · [Design notes](https://thomhurst.github.io/Reservoir/docs/design)

Reservoir is available under the [MIT license](LICENSE).
