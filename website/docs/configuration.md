---
title: Configuration
description: Configure capacity and lifecycle behavior.
---

# Configuration

Reservoir uses constructor arguments and policy implementations for pool behavior.

## Retained object count

Every core pool retains at most `maxCapacity` idle objects. The default is:

```csharp
int defaultCapacity = Math.Max(32, 2 * Environment.ProcessorCount);
```

Size this for peak simultaneous holders, not operations per second. If callers retain objects across asynchronous work, use peak in-flight operations rather than processor count. `maxCapacity` must be positive.

Large capacities automatically use dense striped storage. Empty-pool misses and burst transfers inspect at most the stripe count rather than scanning every retained slot, allowing async workloads to retain thousands of objects without capacity-linear hot-path work.

The bound does not throttle rentals. When all retained objects are busy, `Rent()` creates another object; on return, excess objects are destroyed.

## Retained backing capacity

Collection pools have a separate `maxRetainedCapacity`. It prevents one unusually large request from pinning a large backing array indefinitely.

- Lists, dictionaries, sets, queues, and stacks default to 1,024.
- `StringBuilderPool` defaults to 4,096.
- A negative value is rejected; zero means retain only instances with zero backing capacity.

Returned instances are inspected before clearing. Runtimes with capacity inspection discard oversized instances without clearing or trimming. On older runtimes without capacity inspection, hash sets, queues, and stacks are cleared and trimmed before retention; dictionaries are discarded without clearing.

## Lifecycle choices

- Use `Clear()` to release retained resources while keeping a core pool open.
- Use `Dispose()` when a dedicated core pool's lifetime ends.
- Return `false` from `TryReset()` to reject a specific object.
- Implement `IPooledObjectDestroyPolicy<T>` when cleanup is not `IDisposable.Dispose()`.

Shared collection pools are intended to live for the process lifetime. The shared cancellation-token-source pool treats disposal as a clear operation so one caller cannot close it globally.
