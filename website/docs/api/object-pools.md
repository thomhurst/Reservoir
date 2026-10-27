---
title: Object pools
description: Configure ObjectPool, write policies, use IResettable, and control disposal.
---

# Object pools

`ObjectPool<T,TPolicy>` is the primary API. It is thread-safe, specializes for a struct policy,
and bounds its shared retention tier.

```csharp
var pool = new ObjectPool<Buffer, BufferPolicy>(
    policy: new BufferPolicy(maxRetainedBytes: 64 * 1024),
    maxCapacity: 128);
```

`maxCapacity` is the maximum number of idle objects the pool retains, not a limit on
simultaneous rentals. Manual and scoped rentals share that bound. A miss always calls `Create()`,
so demand can exceed the retained count.

## Write a policy

```csharp
readonly struct BufferPolicy(int maxRetainedBytes) : IPooledObjectDestroyPolicy<Buffer>
{
    public Buffer Create() => new(initialCapacity: 4096);

    public bool TryReset(Buffer buffer)
    {
        buffer.Clear();
        return buffer.Capacity <= maxRetainedBytes;
    }

    public void Destroy(Buffer buffer) => buffer.ReleaseNativeMemory();
}
```

| Member | Called when | Contract |
| --- | --- | --- |
| `Create()` | No retained object is available | Return a non-null object. |
| `TryReset(T)` | An object is returned | Restore clean state; return `false` to discard. |
| `Destroy(T)` | An object is discarded, cleared, or returned after disposal | Permanently release resources. Required on netstandard2.0; modern targets provide default `IDisposable` cleanup. |

`IPooledObjectPolicy<T>` declares all three methods on every target framework. On netstandard2.0, implement `Destroy` even when cleanup is a no-op. On modern targets, omitting it uses the default implementation, which disposes objects implementing `IDisposable`. Implementing `Destroy` on a struct policy also avoids boxing when discarded objects need cleanup. `IPooledObjectDestroyPolicy<T>` remains an optional marker that inherits the same contract.

## Migrate the destruction contract

This is a breaking change for libraries compiled against the previous netstandard2.0 asset. Rebuild those libraries with the new package before consuming them from an application. Updating only the application's package reference does not migrate old policy binaries.

1. Add `Destroy(T)` to every netstandard2.0 policy. To preserve previous default cleanup, dispose the object when it implements `IDisposable`; use a no-op only when nothing needs releasing.
2. Change explicit implementations to name `IPooledObjectPolicy<T>`. A public `Destroy(T)` method already satisfies the new contract.
3. Rebuild and deploy the consuming libraries together with the application. Existing modern base-interface overrides and default cleanup retain their behavior.

```csharp
readonly struct ResourcePolicy : IPooledObjectDestroyPolicy<Resource>
{
    public Resource Create() => new();
    public bool TryReset(Resource resource) => false;

    // Previously: void IPooledObjectDestroyPolicy<Resource>.Destroy(...)
    void IPooledObjectPolicy<Resource>.Destroy(Resource resource)
        => resource.Dispose();
}
```

There is now one declaring interface for `Destroy` across netstandard2.0, net8.0, and net10.0. A library rebuilt against the netstandard2.0 asset can execute unchanged against either modern asset. Generic calls preserve mutable struct state by reference, including calls constrained to the marker interface. Pools use ordinary interface dispatch without reflection-based destruction discovery.

A `readonly struct` policy avoids an interface-object allocation and gives the JIT a concrete call target. Policy state should be immutable or explicitly thread-safe because pool operations may call it concurrently.

## Use `IResettable`

Types designed for pooling can own their reset logic:

```csharp
sealed class Buffer : IResettable
{
    public int Length { get; private set; }

    public bool TryReset()
    {
        Length = 0;
        return true;
    }
}

var pool = new ObjectPool<Buffer, ResettablePooledObjectPolicy<Buffer>>();
```

`ResettablePooledObjectPolicy<T>` requires `T : class, IResettable, new()` and uses the interface's return value to decide whether to retain the object.

## Factory and interface-policy overload

`ObjectPool<T>` is convenient when policy specialization is not needed:

```csharp
var factoryPool = new ObjectPool<Buffer>(() => new Buffer(), maxCapacity: 32);
var policyPool = new ObjectPool<Buffer>(new RuntimeBufferPolicy(), maxCapacity: 32);
```

The factory overload retains every returned object after no-op reset. It still disposes discarded `IDisposable` instances. The interface-policy overload delegates creation, reset, and destruction to `IPooledObjectPolicy<T>`.

Use this overload for class policies selected at runtime. Passing a struct policy to
`ObjectPool<T>` boxes it once and keeps `Create`, `TryReset`, and `Destroy` behind interface
dispatch. When the struct policy type is known at compile time, use `ObjectPool<T,TPolicy>` to
retain constrained calls, generic specialization, and inlining opportunities.

## Rent and return

- `Rent()` retrieves a retained object or creates one.
- `Return(T)` resets then retains or destroys the object.
- `RentScoped()` creates a stack-only `PooledLease` for synchronous scopes.
- `RentScoped(out T)` also exposes the value as a local.

Scoped and manual rentals use the same bounded store; nothing is parked per thread.

For performance-critical synchronous code, prefer `RentScoped(out T)`; the `out` overload avoids
repeated lease ownership validation. Manual rental is required when ownership crosses an `await`.
Nanosecond results vary, so benchmark representative workloads on target hardware.

## Scoped rental retention

Nested scopes and many threads can rent more than the capacity concurrently; only idle objects
are bounded. Excess returns use the policy's existing destruction behavior.

The bound does not include outstanding rentals, references kept by callers, or lease ownership
bookkeeping. Bookkeeping can allocate on a thread's first rental and when nesting reaches a
new depth; warmed scopes reuse it.

Default shared-tier retention is `Math.Max(32, 2 * Environment.ProcessorCount)`. Pass a positive
`maxCapacity` to every constructor to override it.

## Clear and dispose

`Clear()` drains retained objects and destroys them. The pool stays usable.

`Dispose()` drains retained objects and permanently closes the pool. Later `Rent()` calls throw `ObjectDisposedException`; objects returned after disposal are destroyed immediately. Outstanding renters remain their owners until they return.

If several destruction operations throw during a drain, Reservoir continues draining and rethrows the first exception afterward.
