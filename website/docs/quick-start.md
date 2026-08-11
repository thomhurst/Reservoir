---
sidebar_position: 3
title: Quick start
description: Rent, use, and safely return objects with Reservoir.
---

# Quick start

An object pool needs a reference type and a struct policy. `Create()` handles a miss; `TryReset()` prepares an object before retention.

```csharp
using Reservoir;

var pool = new ObjectPool<Buffer, BufferPolicy>(maxCapacity: 64);
Buffer buffer = pool.Rent();

try
{
    buffer.Write(payload);
}
finally
{
    pool.Return(buffer);
}

sealed class Buffer
{
    public int Length { get; set; }

    public void Write(ReadOnlySpan<byte> value) => Length += value.Length;
}

readonly struct BufferPolicy : IPooledObjectPolicy<Buffer>
{
    public Buffer Create() => new();

    public bool TryReset(Buffer buffer)
    {
        buffer.Length = 0;
        return true;
    }
}
```

`Return` transfers ownership to the pool. The reset runs before the object becomes available to another renter.

## Scoped lease

When a rental does not cross an `await`, a stack-only lease can guarantee the return:

```csharp
using var lease = pool.RentScoped();
Buffer buffer = lease.Value;
buffer.Write(payload);
```

Or expose the value directly:

```csharp
using var lease = pool.RentScoped(out Buffer buffer);
buffer.Write(payload);
```

`PooledLease` is a `ref struct`; it cannot be captured, boxed, stored on the heap, or carried across an `await`. Use manual `Rent`/`Return` with `try`/`finally` for asynchronous ownership.

## Shared collection pool

Specialized pools return empty collections and discard collections whose backing storage grew beyond the configured limit:

```csharp
List<int> numbers = ListPool<int>.Shared.Rent();

try
{
    numbers.Add(1);
    numbers.Add(2);
    Consume(numbers);
}
finally
{
    ListPool<int>.Shared.Return(numbers);
}
```

Next: read the [ownership rules](ownership-rules.md), then choose a pool from the [API guide](api/object-pools.md).
