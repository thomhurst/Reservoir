---
title: Design notes
description: How Reservoir is distributed and how its contention strategy works.
---

# Design notes

Reservoir optimizes for a narrow job: short, frequent ownership transfers of reusable reference objects inside one application.

## Why a runtime library

Reservoir ships as a conventional runtime library. The JIT can inline library methods and specialize `ObjectPool<T,TPolicy>` for concrete struct policies across the assembly boundary, so source injection is not required for the optimized hot path.

Compiled package assets give every project the same public type identity, allow dependencies to flow transitively, and keep Reservoir source outside consumer compiler and analyzer settings. Consumers also do not inherit Reservoir's C# language-version requirement.

Reservoir targets .NET Standard 2.0, .NET 8, and .NET 10. Modern targets use framework-specific fast paths; older compatible frameworks use the portable .NET Standard implementation.

## Storage and contention

The core pool chooses between two fixed-size stores. Pools retaining up to 64 objects use an array whose logical slots are spaced one 64-byte cache line apart on 64-bit runtimes, starting one line past the array header so no slot shares a line with the length that every bounds check reads. Larger pools use dense striped stacks backed by preallocated node arrays. Version-stamped compare-and-swap heads prevent ABA while nodes move between each stripe's available and free lists. Each stripe, like each per-thread slot of the thread-local tiers, is padded on both sides so neighbouring objects never share its cache lines; the runtime ignores `StructLayout` sizes on classes that hold references, so the padding comes from an explicit-layout base class and a trailing pad field on the allocated subclass.

Each thread receives stable stripe affinity. Small-pool `Rent()` tries that slot with an atomic exchange, then scans other logical slots on a miss. Large-pool operations try the preferred stripe, then steal across at most 32 stripes. Their work is bounded by stripe count rather than retained capacity. There is no global lock and no separately allocated node per return.

Fixed storage provides the retention bound. It is not a semaphore: active rentals are outside the store, so the pool can create beyond the retained count under bursts.

## Why struct policies

`ObjectPool<T,TPolicy>` constrains `TPolicy` to `struct, IPooledObjectPolicy<T>`. That avoids storing a separate interface object and exposes a concrete policy type to generic specialization and inlining. It also makes thread safety visible: any mutable policy state is shared across concurrent calls inside the pool and must synchronize itself.

`ObjectPool<T>` remains available for factories and runtime-selected interface policies where convenience matters more than specialization.

## Lifecycle races

`Dispose()` marks the pool closed before draining retained slots. A concurrent renter that observes disposal destroys any item it removed and throws. A concurrent return either sees closure immediately or stores then rechecks and participates in clearing. Outstanding rentals are destroyed when eventually returned.

## Compared with other pools

| | Reservoir | `Microsoft.Extensions.ObjectPool` | `ArrayPool<T>` |
| --- | --- | --- | --- |
| Pooled value | Any reference type | Any reference type | Arrays only |
| Distribution | Runtime assembly dependency | Runtime assembly dependency | .NET runtime |
| Retention | Bounded shared tier; scoped TLS adds one per thread | Bounded retained object count | Implementation-managed buckets |
| Reset policy | Struct or interface policy; may reject | Policy return decision | Caller clears optionally |
| Scoped lease | Stack-only `PooledLease` | Not built in | Not built in |

Choose `ArrayPool<T>` for raw arrays and established bucketed array reuse. Choose
`Microsoft.Extensions.ObjectPool` for Microsoft Extensions integration. Choose Reservoir when
struct-policy specialization, bounded shared custom-object retention, capacity-aware storage, and
scoped leases match the application.
