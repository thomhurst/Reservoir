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

Pools retain at most `maxCapacity` objects. Default capacity is `Math.Max(32, 2 * Environment.ProcessorCount)`. Size capacity for peak simultaneous holders, not request rate. When callers hold objects across `await`, use peak in-flight operations rather than processor count.
