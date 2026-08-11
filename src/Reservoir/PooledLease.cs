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
    private PooledLeaseState<T>[]? _states;
    private readonly int _stateIndex;
    private readonly long _token;

    internal PooledLease(
        ObjectPool<T, TPolicy> pool,
        PooledLeaseState<T>[] states,
        int stateIndex,
        long token)
    {
        _pool = pool;
        _states = states;
        _stateIndex = stateIndex;
        _token = token;
    }

    /// <summary>Gets the rented object while this lease owns it.</summary>
    public readonly T Value
    {
        get
        {
            PooledLeaseState<T>[] states = _states
                ?? throw new ObjectDisposedException(nameof(PooledLease<T, TPolicy>));
            return states[_stateIndex].GetValue(_token);
        }
    }

    /// <summary>Returns the rented object. Repeated calls on this lease are ignored.</summary>
    public void Dispose()
    {
        ObjectPool<T, TPolicy>? pool = _pool;
        PooledLeaseState<T>[]? states = _states;

        _pool = null;
        _states = null;

        if (pool is not null
            && states![_stateIndex].TryRelease(_token, out T? value))
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
    private PooledLease<T, ObjectPool<T>.PolicyAdapter> _lease;

    internal PooledLease(PooledLease<T, ObjectPool<T>.PolicyAdapter> lease)
    {
        _lease = lease;
    }

    /// <summary>Gets the rented object while this lease owns it.</summary>
    public readonly T Value => _lease.Value;

    /// <summary>Returns the rented object. Repeated calls on this lease are ignored.</summary>
    public void Dispose() => _lease.Dispose();
}

internal struct PooledLeaseState<T>
    where T : class
{
    // Thread-local slots use even versions when idle and odd versions to identify
    // active leases, so stale copies cannot release a later lease using the slot.
    private long _version;
    private T? _value;

    internal bool TryAcquire(T value, out long token)
    {
        if ((_version & 1) != 0)
        {
            token = 0;
            return false;
        }

        token = _version + 1;
        _value = value;
        _version = token;
        return true;
    }

    internal T GetValue(long token)
    {
        T? value = _value;
        return value is not null && _version == token
            ? value
            : throw new ObjectDisposedException(nameof(PooledLease<T>));
    }

    internal bool TryRelease(long token, out T? value)
    {
        if (_version != token)
        {
            value = null;
            return false;
        }

        value = _value;
        _value = null;
        _version = token + 1;
        return true;
    }
}
