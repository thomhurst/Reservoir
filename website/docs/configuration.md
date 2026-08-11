---
title: Configuration
description: Configure capacity, visibility, diagnostics, and lifecycle behavior.
---

# Configuration

Reservoir uses constructor arguments and compilation symbols—no global runtime configuration.

## Retained object count

Every core pool retains at most `maxCapacity` idle objects. The default is:

```csharp
Math.Max(32, 2 * Environment.ProcessorCount)
```

Size this for peak simultaneous holders, not operations per second. If callers retain objects across asynchronous work, use peak in-flight operations rather than processor count. `maxCapacity` must be positive.

The bound does not throttle rentals. When all retained objects are busy, `Rent()` creates another object; on return, excess objects are destroyed.

## Retained backing capacity

Collection pools have a separate `maxRetainedCapacity`. It prevents one unusually large request from pinning a large backing array indefinitely.

- Lists, dictionaries, sets, queues, and stacks default to 1,024.
- `StringBuilderPool` defaults to 4,096.
- A negative value is rejected; zero means retain only instances with zero backing capacity.

Returned instances are cleared and inspected. Oversized instances are discarded rather than trimmed.

## Compilation symbols

| Symbol | Effect |
| --- | --- |
| `RESERVOIR_PUBLIC` | Makes Reservoir's top-level consumer API public instead of internal. |
| `RESERVOIR_DIAGNOSTICS` | Includes wrong-pool, double-return, and leak diagnostics outside Debug builds. |
| `DEBUG` | Includes the same diagnostics automatically. |

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);RESERVOIR_PUBLIC</DefineConstants>
</PropertyGroup>
```

When neither `DEBUG` nor `RESERVOIR_DIAGNOSTICS` is defined, tracking fields and calls are removed by conditional compilation; they do not remain on the Release hot path.

## Lifecycle choices

- Use `Clear()` to release retained resources while keeping a core pool open.
- Use `Dispose()` when a dedicated core pool's lifetime ends.
- Return `false` from `TryReset()` to reject a specific object.
- Override `Destroy()` when cleanup is not `IDisposable.Dispose()`.

Shared collection pools are intended to live for the process lifetime. The shared cancellation-token-source pool treats disposal as a clear operation so one caller cannot close it globally.
