using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Reservoir;

/// <summary>
/// A bounded, thread-safe object pool specialized for a struct policy.
/// </summary>
/// <typeparam name="T">The reference type stored by the pool.</typeparam>
/// <typeparam name="TPolicy">The policy used to create and reset objects.</typeparam>
public sealed class ObjectPool<T, TPolicy> : IDisposable
    where T : class
    where TPolicy : struct, IPooledObjectPolicy<T>
{
    [ThreadStatic]
    private static PooledLeaseState<T, TPolicy>? _threadLeaseState;

    private readonly ObjectWrapper[] _items;
    private TPolicy _policy;
    private int _isDisposed;
    private T? _fastItem;

    private static readonly bool s_isStaticallyDisposable
        = typeof(IDisposable).IsAssignableFrom(typeof(T));

    private static readonly bool s_mayHaveDisposableImplementations = !typeof(T).IsSealed;

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
        ThrowIfDisposed();

        T? item = Interlocked.Exchange(ref _fastItem, null);
        T rented = item ?? RentSlow();

        if (Volatile.Read(ref _isDisposed) == 0)
        {
            return rented;
        }

        DisposeItem(rented);
        return ThrowDisposed();
    }

    /// <summary>Rents an object owned by a stack-only lease that returns it on disposal.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledLease<T, TPolicy> RentScoped()
    {
        T value = Rent();
        return CreateLease(value);
    }

    /// <summary>
    /// Rents an object owned by a stack-only lease and also exposes the object directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledLease<T, TPolicy> RentScoped(out T value)
    {
        value = Rent();
        return CreateLease(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PooledLease<T, TPolicy> CreateLease(T value)
    {
        PooledLeaseState<T, TPolicy>? state = _threadLeaseState;

        if (state is not null && state.TryAcquire(this, value, out long token))
        {
            return new PooledLease<T, TPolicy>(state, token);
        }

        return RentScopedSlow(value, state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private PooledLease<T, TPolicy> RentScopedSlow(
        T value,
        PooledLeaseState<T, TPolicy>? state)
    {
        if (state is null)
        {
            state = new PooledLeaseState<T, TPolicy>();
            _threadLeaseState = state;
        }
        else
        {
            while (state.Next is not null)
            {
                state = state.Next;
                if (state.TryAcquire(this, value, out long token))
                {
                    return new PooledLease<T, TPolicy>(state, token);
                }
            }

            state.Next = new PooledLeaseState<T, TPolicy>();
            state = state.Next;
        }

        _ = state.TryAcquire(this, value, out long firstToken);
        return new PooledLease<T, TPolicy>(state, firstToken);
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

        if (Volatile.Read(ref _isDisposed) != 0)
        {
            DisposeItem(obj);
            return;
        }

        if (!_policy.TryReset(obj))
        {
            DisposeItem(obj);
            return;
        }

        T? displaced = Interlocked.Exchange(ref _fastItem, obj);
        if (displaced is null)
        {
            ClearIfDisposed();
            return;
        }

        ReturnSlow(obj, displaced);
        ClearIfDisposed();
    }

    /// <summary>
    /// Removes all retained objects and disposes those that implement <see cref="IDisposable"/>.
    /// The pool remains usable.
    /// </summary>
    public void Clear()
    {
        Exception? firstException = null;

        DisposeRetained(Interlocked.Exchange(ref _fastItem, null), ref firstException);

        for (int i = 0; i < _items.Length; i++)
        {
            DisposeRetained(
                Interlocked.Exchange(ref _items[i].Element, null),
                ref firstException);
        }

        if (firstException is not null)
        {
            ExceptionDispatchInfo.Capture(firstException).Throw();
        }
    }

    /// <summary>
    /// Permanently closes the pool and disposes all retained disposable objects. Objects returned
    /// after disposal are disposed instead of retained.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            Clear();
        }
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
        T? observed = Interlocked.CompareExchange(ref _fastItem, displaced, returned);
        DisposeItem(ReferenceEquals(observed, returned) ? returned : displaced);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            Clear();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            ThrowDisposed();
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T ThrowDisposed()
        => throw new ObjectDisposedException(typeof(ObjectPool<T, TPolicy>).FullName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DisposeItem(T obj)
    {
        if (s_isStaticallyDisposable)
        {
            ((IDisposable)obj).Dispose();
            return;
        }

        if (s_mayHaveDisposableImplementations && obj is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static void DisposeRetained(T? obj, ref Exception? firstException)
    {
        if (obj is null)
        {
            return;
        }

        try
        {
            DisposeItem(obj);
        }
        catch (Exception exception)
        {
            firstException ??= exception;
        }
    }

    private struct ObjectWrapper
    {
        internal T? Element;
    }
}
