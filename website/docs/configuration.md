---
title: Configuration
description: Configure capacity and lifecycle behavior.
---

# Configuration

Reservoir uses constructor arguments and policy implementations for pool behavior. Ownership diagnostics use one process-wide opt-in captured by each new pool.

## Retained object count

Every core pool retains at most `maxCapacity` idle objects. The default is:

```csharp
Math.Max(32, 2 * Environment.ProcessorCount)
```

Size this for peak simultaneous holders, not operations per second. If callers retain objects across asynchronous work, use peak in-flight operations rather than processor count. `maxCapacity` must be positive.

Large capacities automatically use dense striped storage. Empty-pool misses and burst transfers inspect at most the stripe count rather than scanning every retained slot, allowing async workloads to retain thousands of objects without capacity-linear hot-path work.

The bound does not throttle rentals. When all retained objects are busy, `Rent()` creates another object; on return, excess objects are destroyed.

## Retained backing capacity

Collection pools have a separate `maxRetainedCapacity`. It prevents one unusually large request from pinning a large backing array indefinitely.

- Lists, dictionaries, sets, queues, and stacks default to 1,024.
- `StringBuilderPool` defaults to 4,096.
- A negative value is rejected; zero means retain only instances with zero backing capacity.

Returned instances are cleared and inspected. Runtimes with capacity inspection discard oversized instances rather than trimming. On older runtimes, hash sets, queues, and stacks are trimmed before retention; dictionaries are discarded.

## Ownership diagnostics

Enable diagnostics before constructing pools:

```csharp
ObjectPoolDiagnostics.Enabled = true;
```

Each pool captures the setting when constructed. Existing pools are unaffected by later changes. Enabled pools detect wrong-pool and duplicate returns, and report rentals that become unreachable without being returned.

For development-only diagnostics, call this during application startup:

```csharp
ObjectPoolDiagnostics.EnableForDebugBuilds();
```

The method has `[Conditional("DEBUG")]`, so the consumer compiler omits the call from builds without `DEBUG`. Disabled pools allocate nothing for tracking but retain a small predictable branch on rent and return. Enabled diagnostics allocate tracking state and capture rent-site stack traces; do not use them for throughput measurements.

## Lifecycle choices

- Use `Clear()` to release retained resources while keeping a core pool open.
- Use `Dispose()` when a dedicated core pool's lifetime ends.
- Return `false` from `TryReset()` to reject a specific object.
- Implement `IPooledObjectDestroyPolicy<T>` when cleanup is not `IDisposable.Dispose()`.

Shared collection pools are intended to live for the process lifetime. The shared cancellation-token-source pool treats disposal as a clear operation so one caller cannot close it globally.
