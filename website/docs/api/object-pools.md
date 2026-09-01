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

`maxCapacity` is the maximum number of idle objects retained by the bounded shared tier, not a
limit on simultaneous rentals. Scoped rentals additionally retain one object per participating
thread. A miss always calls `Create()`, so demand can exceed the retained count.

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
| `Destroy(T)` | An object is discarded, cleared, or returned after disposal | Permanently release resources when implementing `IPooledObjectDestroyPolicy<T>`. |

Implement `IPooledObjectPolicy<T>` when default destruction is sufficient. Reservoir automatically disposes discarded objects that implement `IDisposable`. Implement `IPooledObjectDestroyPolicy<T>` only when cleanup needs different behavior. This derived interface is the portable custom-destruction contract, including on .NET Standard 2.0; modern targets retain the base interface's default `Destroy` method for compatibility.

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

The factory overload retains every returned object after no-op reset. It still disposes discarded `IDisposable` instances. The interface-policy overload delegates creation and reset. It also delegates destruction when the policy implements `IPooledObjectDestroyPolicy<T>`.

Use this overload for class policies selected at runtime. Passing a struct policy to
`ObjectPool<T>` boxes it once and keeps `Create`, `TryReset`, and `Destroy` behind interface
dispatch. When the struct policy type is known at compile time, use `ObjectPool<T,TPolicy>` to
retain constrained calls, generic specialization, and inlining opportunities.

## Rent and return

- `Rent()` retrieves a retained object or creates one.
- `Return(T)` resets then retains or destroys the object.
- `RentScoped()` creates a stack-only `PooledLease` for synchronous scopes.
- `RentScoped(out T)` also exposes the value as a local.

`RentScoped` uses a per-pool thread-local fast path, then falls back to the bounded shared tier.
Manual `Rent()`/`Return()` use only the bounded shared tier by default. Construct the pool with
`threadLocalFastPath: true` to opt manual rentals into the same fast path when same-thread reuse
outweighs the lookup overhead. Thread-local items are additional retention — up to one object per
participating thread beyond `MaximumRetained` — and can remain attached to idle threads until
`Clear()` or `Dispose()` drains them.

For performance-critical synchronous code, prefer `RentScoped(out T)`; the `out` overload avoids
repeated lease ownership validation. Manual rental is required when ownership crosses an `await`.
Nanosecond results vary, so benchmark representative workloads on target hardware.

Default shared-tier retention is `Math.Max(32, 2 * Environment.ProcessorCount)`. Pass a positive
`maxCapacity` to every constructor to override it.

## Clear and dispose

`Clear()` drains thread-local and shared retained slots and destroys their objects. The pool stays
usable.

`Dispose()` drains retained objects and permanently closes the pool. Later `Rent()` calls throw `ObjectDisposedException`; objects returned after disposal are destroyed immediately. Outstanding renters remain their owners until they return.

If several destruction operations throw during a drain, Reservoir continues draining and rethrows the first exception afterward.
