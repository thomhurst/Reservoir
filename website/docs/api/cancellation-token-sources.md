---
title: Cancellation token sources
description: Safely reuse uncanceled CancellationTokenSource instances.
---

# Cancellation token source pool

`CancellationTokenSourcePool` reuses a source only when `CancellationTokenSource.TryReset()` confirms cancellation never fired. Canceled sources are permanently disposed and discarded. Timers and registrations from an unfired rental are removed before reuse.

`TryReset()` is available on .NET 6 and later. On older runtimes, returned sources are permanently disposed rather than reused.

```csharp
CancellationTokenSourcePool pool = CancellationTokenSourcePool.Shared;

using CancellationTokenSource source = pool.Rent();
source.CancelAfter(TimeSpan.FromSeconds(30));
await ProcessAsync(source.Token);
```

The rented source is a specialized subtype that returns itself to its originating pool when disposed. Dispose each rental exactly once, only after becoming its sole owner again.

## Concurrency rules

Before disposal, ensure there are:

- no outstanding token readers;
- no concurrent `Cancel` or `CancelAfter` calls;
- no concurrent registration or disposal operations.

`TryReset()` is not thread-safe with concurrent source use. Disposal transfers ownership to the pool, so do not access or dispose another alias afterward.

Linked sources created by `CancellationTokenSource.CreateLinkedTokenSource` are ordinary sources. They do not come from Reservoir and should be disposed normally.

## Scoped lease

For synchronous scopes, use `RentScoped()`:

```csharp
using var lease = pool.RentScoped(out CancellationTokenSource source);
source.CancelAfter(TimeSpan.FromSeconds(5));
RunSynchronousWork(source.Token);
```

Do not also dispose `source`; the lease owns the return.

## Pool lifetime

A dedicated `CancellationTokenSourcePool(maxCapacity)` owns retained sources. `Clear()` permanently disposes retained sources while leaving the pool usable. `Dispose()` closes a dedicated pool.

`CancellationTokenSourcePool.Shared.Dispose()` deliberately clears retained sources without closing the process-wide shared pool.
