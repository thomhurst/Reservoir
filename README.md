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

Requires a .NET Standard 2.0-compatible runtime or later

## Why Reservoir?

- **Zero general-purpose pool allocations when warm.** `ObjectPool<T,TPolicy>` rent and return reuse fixed slots without allocating nodes. Legacy collection fallbacks may trim or replace backing storage.
- **Bounded retention.** You choose the maximum idle-object count. Every rental mode returns to
  the same bounded store, so idle objects never accumulate per thread.
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

    public void Destroy(Buffer buffer) { } // No resources to release.
}
```

`Create()` handles a miss. `TryReset()` prepares an object for reuse or returns `false` to discard it. `Destroy()` releases discarded objects. Every netstandard2.0 policy must implement it; modern targets provide default `IDisposable` cleanup when it is omitted. The scoped lease guarantees return when control leaves the synchronous scope and returns to the same bounded store as manual rentals.

**Breaking policy migration:** `Destroy(T)` now belongs to `IPooledObjectPolicy<T>` on every target. Rebuild existing netstandard2.0 consumers and change explicit `IPooledObjectDestroyPolicy<T>.Destroy` implementations to `IPooledObjectPolicy<T>.Destroy`. See the [migration guide](https://thomhurst.github.io/Reservoir/docs/api/object-pools#migrate-the-destruction-contract).

For performance-critical synchronous code, prefer `RentScoped(out T)` over repeated
`lease.Value` access, which validates ownership on every read. Manual rental is required when work
crosses an `await`. Measure on target hardware when nanoseconds matter.

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

For synchronous scopes, `RentScoped` returns the collection automatically:

```csharp
using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> values);
values.Add(42);
Consume(values);
```

The lease is stack-only and cannot cross an `await`. Manual `Rent` and `Return` keep using the
bounded shared pool for asynchronous ownership.

`CancellationTokenSourcePool.RentScoped` offers the same scoped ownership while preserving
source reset and disposal semantics. Use `Rent()` or `RentLinked()` when ownership crosses an
async boundary.

## The ownership rule

> Returning an object transfers ownership to the pool. Do not touch it, return it twice, or return it to another pool.

Another thread may rent the same object immediately. Read the complete [ownership rules](https://thomhurst.github.io/Reservoir/docs/ownership-rules).

## Measured, not promised

<!-- BENCHMARK_RESULTS_START -->
BenchmarkDotNet 0.15.8 `.NET 10.0`, .NET 10.0.13, Linux Ubuntu 24.04.5 LTS, INTEL XEON PLATINUM 8573C:

| Method | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: |
| `new` | 20.25 ns | 1.00 | 304 B |
| **Reservoir** | **18.65 ns** | **0.92** | **0 B** |
| `Microsoft.Extensions.ObjectPool` | 18.77 ns | 0.93 | 0 B |
| `ConcurrentBag<T>` pool | 56.62 ns | 2.80 | 0 B |
<!-- BENCHMARK_RESULTS_END -->

Allocation measurements apply to the published workloads and their recorded runtime and job. Timings vary by machine; compare methods within the same run.

[See all benchmark results and reproduction instructions](https://thomhurst.github.io/Reservoir/docs/benchmarks#reproduce).
Run the full suite on a GitHub Actions `ubuntu-latest` runner:

```shell
gh workflow run benchmarks.yml --ref main -f 'filter=*' -f job=short
```

For a bounded setup check followed by a short measurement, select one warm rent/return
benchmark on .NET 8 and .NET 10:

```shell
gh workflow run benchmarks.yml --ref main -f 'filter=*.ObjectPoolBenchmarks.RentReturn' -f job=short
```

The workflow first runs `--job Dry` to check compilation and execution; Dry results are
not performance measurements. It then runs `--job Short`. Do not add `--apples`:
BenchmarkDotNet 0.15.8 combines it with `OperationsPerInvoke` in a way that makes batched
contention cases effectively unbounded. Use [Benchmark comparison](.github/workflows/benchmark-compare.yml)
with explicit baseline and candidate SHAs for performance acceptance; local timings are diagnostic only.


## When it fits

Choose Reservoir when you want bounded shared custom-object reuse, struct-policy specialization,
scoped leases, or capacity-aware storage.

Use `ArrayPool<T>` for raw arrays. Use `Microsoft.Extensions.ObjectPool` when integration with Microsoft Extensions abstractions matters more than Reservoir's specialized policies and leases.

## Go deeper

[Documentation](https://thomhurst.github.io/Reservoir/) · [Installation](https://thomhurst.github.io/Reservoir/docs/installation) · [API guide](https://thomhurst.github.io/Reservoir/docs/api/object-pools) · [Design notes](https://thomhurst.github.io/Reservoir/docs/design)

Reservoir is available under the [MIT license](LICENSE).
