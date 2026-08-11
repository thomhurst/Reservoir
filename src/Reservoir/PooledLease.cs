namespace Reservoir;

/// <summary>
/// Owns an object rented from an <see cref="ObjectPool{T,TPolicy}"/> and returns it on disposal.
/// </summary>
/// <typeparam name="T">The reference type stored by the pool.</typeparam>
/// <typeparam name="TPolicy">The policy used to create and reset objects.</typeparam>
public ref struct PooledLease<T, TPolicy>
    where T : class
    where TPolicy : struct, IPooledObjectPolicy<T>
{
    private ObjectPool<T, TPolicy>? _pool;
    private T? _value;

    internal PooledLease(ObjectPool<T, TPolicy> pool, T value)
    {
        _pool = pool;
        _value = value;
    }

    /// <summary>Gets the rented object while this lease owns it.</summary>
    public readonly T Value => _value
        ?? throw new ObjectDisposedException(nameof(PooledLease<T, TPolicy>));

    /// <summary>Returns the rented object. Repeated calls on this lease are ignored.</summary>
    public void Dispose()
    {
        ObjectPool<T, TPolicy>? pool = _pool;
        T? value = _value;

        _pool = null;
        _value = null;

        if (pool is not null)
        {
            pool.Return(value!);
        }
    }
}

/// <summary>
/// Owns an object rented from an <see cref="ObjectPool{T}"/> and returns it on disposal.
/// </summary>
/// <typeparam name="T">The reference type stored by the pool.</typeparam>
public ref struct PooledLease<T>
    where T : class
{
    private ObjectPool<T>? _pool;
    private T? _value;

    internal PooledLease(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        _value = value;
    }

    /// <summary>Gets the rented object while this lease owns it.</summary>
    public readonly T Value => _value
        ?? throw new ObjectDisposedException(nameof(PooledLease<T>));

    /// <summary>Returns the rented object. Repeated calls on this lease are ignored.</summary>
    public void Dispose()
    {
        ObjectPool<T>? pool = _pool;
        T? value = _value;

        _pool = null;
        _value = null;

        if (pool is not null)
        {
            pool.Return(value!);
        }
    }
}
