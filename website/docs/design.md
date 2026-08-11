---
title: Design notes
description: Why Reservoir ships as source and how its contention strategy works.
---

# Design notes

Reservoir optimizes for a narrow job: short, frequent ownership transfers of reusable reference objects inside one application.

## Why source-only

Object pooling often sits in hot code and exposes policy types in generic signatures. Shipping source gives the JIT the implementation and policy in the consuming assembly, while removing a runtime package from deployment.

The NuGet package is a development dependency. Its `contentFiles` compile into each referencing project, its types remain internal by default, and no Reservoir assembly crosses application or plugin boundaries.

Trade-offs are explicit: each project compiles its own copy, upgrades rebuild the consumer, and internal types from different assemblies are distinct. Define `RESERVOIR_PUBLIC` only when a library intentionally exposes its compiled copy.

## Storage and contention

The core pool stores retained references in a fixed-size array. Logical slots are spaced one 64-byte cache line apart on 64-bit runtimes to reduce false sharing between contending threads.

Each thread receives a stable stripe affinity. `Rent()` tries that stripe with an atomic exchange, then scans other logical slots on a miss. `Return()` atomically places the object into the preferred stripe and probes remaining slots only when needed. There is no global lock and no separately allocated node per return.

The fixed array provides the retention bound. It is not a semaphore: active rentals are outside the array, so the pool can create beyond the retained count under bursts.

## Why struct policies

`ObjectPool<T,TPolicy>` constrains `TPolicy` to `struct, IPooledObjectPolicy<T>`. That avoids storing a separate interface object and exposes a concrete policy type to generic specialization and inlining. It also makes thread safety visible: any mutable policy state is shared across concurrent calls inside the pool and must synchronize itself.

`ObjectPool<T>` remains available for factories and runtime-selected interface policies where convenience matters more than specialization.

## Lifecycle races

`Dispose()` marks the pool closed before draining retained slots. A concurrent renter that observes disposal destroys any item it removed and throws. A concurrent return either sees closure immediately or stores then rechecks and participates in clearing. Outstanding rentals are destroyed when eventually returned.

Debug ownership tracking uses weak keys so diagnostics do not keep leaked objects alive. Release builds omit tracking unless `RESERVOIR_DIAGNOSTICS` is defined.

## Compared with other pools

| | Reservoir | `Microsoft.Extensions.ObjectPool` | `ArrayPool<T>` |
| --- | --- | --- | --- |
| Pooled value | Any reference type | Any reference type | Arrays only |
| Distribution | Source compiled into consumer | Runtime assembly dependency | .NET runtime |
| Retention | Explicit bounded object count | Bounded retained object count | Implementation-managed buckets |
| Reset policy | Struct or interface policy; may reject | Policy return decision | Caller clears optionally |
| Scoped lease | Stack-only `PooledLease` | Not built in | Not built in |
| Debug ownership diagnostics | Included | Not built in | Not built in |

Choose `ArrayPool<T>` for raw arrays and established bucketed array reuse. Choose `Microsoft.Extensions.ObjectPool` when ecosystem integration and a conventional runtime dependency are preferred. Choose Reservoir when source ownership, struct-policy specialization, bounded custom-object retention, and its diagnostics match the application.
