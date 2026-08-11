using System.Runtime.CompilerServices;
using System.Threading;

namespace Reservoir;

/// <summary>
/// A bounded, thread-safe object pool specialized for a struct policy.
/// </summary>
/// <typeparam name="T">The reference type stored by the pool.</typeparam>
/// <typeparam name="TPolicy">The policy used to create and reset objects.</typeparam>
public sealed class ObjectPool<T, TPolicy>
    where T : class
    where TPolicy : struct, IPooledObjectPolicy<T>
{
    [ThreadStatic]
    private static PooledLeaseState<T>[]? _threadLeaseStates;

    private readonly ObjectWrapper[] _items;
    private TPolicy _policy;
    private T? _fastItem;

    /// <summary>Initializes a pool with the default policy value and capacity.</summary>
    public ObjectPool()
        : this(default, DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with the default policy value.</summary>
    public ObjectPool(int maxCapacity)
        : this(default, maxCapacity)
    {
    }

    /// <summary>Initializes a pool with the supplied policy and default capacity.</summary>
    public ObjectPool(TPolicy policy)
        : this(policy, DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with the supplied policy and capacity.</summary>
    public ObjectPool(TPolicy policy, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity);

        _policy = policy;
        MaximumRetained = maxCapacity;
        _items = new ObjectWrapper[maxCapacity - 1];
    }

    /// <summary>Gets the default maximum number of retained objects.</summary>
    public static int DefaultMaximumRetained => Math.Max(32, 2 * Environment.ProcessorCount);

    /// <summary>Gets the maximum number of objects retained by this pool.</summary>
    public int MaximumRetained { get; }

    /// <summary>Rents an object, creating one when no retained object is available.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Rent()
    {
        T? item = Interlocked.Exchange(ref _fastItem, null);
        if (item is not null)
        {
            return item;
        }

        return RentSlow();
    }

    /// <summary>Rents an object owned by a stack-only lease that returns it on disposal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledLease<T, TPolicy> RentScoped()
    {
        T value = Rent();
        PooledLeaseState<T>[] states = _threadLeaseStates
            ??= new PooledLeaseState<T>[1];

        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].TryAcquire(value, out long token))
            {
                return new PooledLease<T, TPolicy>(this, states, i, token);
            }
        }

        states = new PooledLeaseState<T>[checked(states.Length * 2)];
        _threadLeaseStates = states;
        _ = states[0].TryAcquire(value, out long expandedToken);
        return new PooledLease<T, TPolicy>(this, states, 0, expandedToken);
    }

    /// <summary>
    /// Rents an object owned by a stack-only lease and also exposes the object directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledLease<T, TPolicy> RentScoped(out T value)
    {
        PooledLease<T, TPolicy> lease = RentScoped();
        value = lease.Value;
        return lease;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T RentSlow()
    {
        for (int i = 0; i < _items.Length; i++)
        {
            T? item = Volatile.Read(ref _items[i].Element);
            if (item is not null
                && ReferenceEquals(
                    Interlocked.CompareExchange(ref _items[i].Element, null, item),
                    item))
            {
                return item;
            }
        }

        return _policy.Create()
            ?? throw new InvalidOperationException("The pool policy returned null from Create().");
    }

    /// <summary>Resets and returns an object. Objects exceeding capacity are discarded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (!_policy.TryReset(obj))
        {
            return;
        }

        T? displaced = Interlocked.Exchange(ref _fastItem, obj);
        if (displaced is null)
        {
            return;
        }

        ReturnSlow(obj, displaced);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReturnSlow(T returned, T displaced)
    {
        for (int i = 0; i < _items.Length; i++)
        {
            if (Interlocked.CompareExchange(ref _items[i].Element, displaced, null) is null)
            {
                return;
            }
        }

        // Preserve full-pool semantics: discard the newly returned object when
        // it has not already been rented or displaced by another thread.
        Interlocked.CompareExchange(ref _fastItem, displaced, returned);
    }

    private struct ObjectWrapper
    {
        internal T? Element;
    }
}
