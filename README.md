# Reservoir

Reservoir provides bounded, thread-safe object pooling with an allocation-free warm rent/return path.

```csharp
var pool = new ObjectPool<MyBuffer, MyBufferPolicy>(maxCapacity: 64);
MyBuffer buffer = pool.Rent();

try
{
    // Use buffer.
}
finally
{
    pool.Return(buffer);
}
```

Implement `IPooledObjectPolicy<T>` on a struct to let the JIT specialize and inline policy calls. `Create()` supplies an object when the pool is empty. `TryReset()` prepares a returned object for reuse; returning `false` discards it.

Types designed for pooling can implement `IResettable` and use the constrained built-in policy. This avoids runtime type checks on the rent/return path:

```csharp
var pool = new ObjectPool<MyResettableBuffer, ResettablePooledObjectPolicy<MyResettableBuffer>>();
```

Discarded objects are disposed when they implement `IDisposable`. `Clear()` drains retained objects and disposes them while leaving the pool usable. `Dispose()` drains and permanently closes the pool; later returns are disposed immediately and later rents throw `ObjectDisposedException`.

Returning an object transfers ownership to the pool. Do not access it afterward and never return it twice. Another thread may rent it immediately after `Return()` completes.

Debug builds detect objects returned twice or returned to the wrong pool and throw `InvalidOperationException`. They also report rentals that become unreachable without being returned, including the rent-site stack trace, through `Trace` and `ObjectPoolDiagnostics.LeakDetected`. Define `RESERVOIR_DIAGNOSTICS` to enable the same checks in a Release or staging build. Diagnostics are compiled out when neither `DEBUG` nor `RESERVOIR_DIAGNOSTICS` is defined, leaving no fields or calls on the Release hot path.

Pools retain at most `maxCapacity` objects. Default capacity is `Math.Max(32, 2 * Environment.ProcessorCount)`. Size capacity for peak simultaneous holders, not request rate. When callers hold objects across `await`, use peak in-flight operations rather than processor count.

Use a scoped lease when the rental does not cross an `await`. Disposing the stack-only lease returns its value automatically:

```csharp
using var lease = pool.RentScoped();
MyBuffer buffer = lease.Value;
// Use buffer only while lease is alive.
```

For a direct local without a separate `Value` access:

```csharp
using var lease = pool.RentScoped(out MyBuffer buffer);
```
