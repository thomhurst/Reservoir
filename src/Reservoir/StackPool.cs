#nullable enable

using System;
using System.Collections.Generic;

namespace Reservoir;

/// <summary>Provides pools of reusable <see cref="Stack{T}"/> instances.</summary>
/// <typeparam name="T">The element type.</typeparam>
public
sealed class StackPool<T>
{
    private readonly ObjectPool<Stack<T>, Policy> _pool;
    private InstanceThreadLocalFrontTier<Stack<T>> _scopedTier;

    /// <summary>Gets the default largest stack capacity retained by a pool.</summary>
    public const int DefaultMaximumRetainedCapacity = 1024;

    /// <summary>Gets the shared pool.</summary>
    public static StackPool<T> Shared { get; } = new();

    /// <summary>
    /// Gets an opt-in pool that retains one stack per participating thread before using
    /// <see cref="Shared"/> as a bounded fallback.
    /// </summary>
    public static ThreadLocalPool ThreadLocalShared { get; } = new();

    /// <summary>Initializes a pool with default limits.</summary>
    public StackPool()
        : this(DefaultMaximumRetainedCapacity, ObjectPool<Stack<T>, Policy>.DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with a custom maximum retained stack capacity.</summary>
    public StackPool(int maxRetainedCapacity)
        : this(maxRetainedCapacity, ObjectPool<Stack<T>, Policy>.DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with custom item and stack capacity limits.</summary>
    public StackPool(int maxRetainedCapacity, int maxCapacity)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedCapacity);
#else
        if (maxRetainedCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetainedCapacity),
                maxRetainedCapacity,
                null);
        }
#endif
        MaximumRetainedCapacity = maxRetainedCapacity;
        _pool = new ObjectPool<Stack<T>, Policy>(new Policy(maxRetainedCapacity), maxCapacity);
    }

    /// <summary>Gets the maximum number of stacks retained by this pool.</summary>
    public int MaximumRetained => _pool.MaximumRetained;

    /// <summary>Gets the largest stack capacity retained by this pool.</summary>
    public int MaximumRetainedCapacity { get; }

    /// <summary>Rents an empty stack.</summary>
    public Stack<T> Rent() => _pool.RentWithoutLifecycle();

    /// <summary>Rents an empty stack owned by a stack-only thread-local lease.</summary>
    public Lease RentScoped()
        => new(this, _scopedTier.Rent(_pool));

    /// <summary>
    /// Rents an empty stack owned by a stack-only thread-local lease and exposes it directly.
    /// </summary>
    public Lease RentScoped(out Stack<T> stack)
    {
        stack = _scopedTier.Rent(_pool);
        return new Lease(this, stack);
    }

    /// <summary>Returns a stack, clearing it when retained and discarding it when too large.</summary>
    public void Return(Stack<T> stack)
    {
        ThrowIfNull(stack);

        if (!TryReset(stack))
        {
            return;
        }

        _pool.ReturnWithoutReset(stack);
    }

    private bool TryReset(Stack<T> stack)
    {
        if (Policy.Reset(stack, MaximumRetainedCapacity))
        {
            return true;
        }

        _pool.Destroy(stack);
        return false;
    }

    private void ReturnScoped(Stack<T> stack)
    {
        if (!TryReset(stack))
        {
            return;
        }

        if (!_scopedTier.TryReturn(stack))
        {
            _pool.ReturnWithoutReset(stack);
        }
    }

    private static void ThrowIfNull(Stack<T>? stack)
    {
        if (stack is null)
        {
            throw new ArgumentNullException("obj");
        }
    }

    private readonly struct Policy(int maxRetainedCapacity) : IPooledObjectPolicy<Stack<T>>
    {
#if NETSTANDARD2_0
        private static readonly Func<Stack<T>, int, int>? s_ensureCapacity
            = RuntimeCompatibility.CreateEnsureCapacity<Stack<T>>();
#endif

        public Stack<T> Create() => [];

        internal static bool Reset(Stack<T> obj, int maximumRetainedCapacity)
        {
#if NETSTANDARD2_0
            if (s_ensureCapacity is null)
            {
                obj.Clear();
                obj.TrimExcess();
                return true;
            }

            if (s_ensureCapacity(obj, 0) > maximumRetainedCapacity)
            {
                return false;
            }

            obj.Clear();
            return true;
#else
            if (obj.EnsureCapacity(0) > maximumRetainedCapacity)
            {
                return false;
            }

            obj.Clear();
            return true;
#endif
        }

        public bool TryReset(Stack<T> obj) => Reset(obj, maxRetainedCapacity);
    }

    /// <summary>Owns a thread-local stack rental and returns it when disposed.</summary>
    public ref struct Lease
    {
        private readonly StackPool<T>? _pool;
        private ScopedPoolLease<Stack<T>> _lease;

        internal Lease(StackPool<T> pool, Stack<T> stack)
        {
            _pool = pool;
            _lease = new ScopedPoolLease<Stack<T>>(stack);
        }

        /// <summary>Gets the rented stack while this lease owns it.</summary>
        public readonly Stack<T> Value => _lease.Value;

        /// <summary>Returns the stack. Repeated calls and stale copies are ignored.</summary>
        public void Dispose()
        {
            if (_lease.TryRelease(out Stack<T> stack))
            {
                _pool!.ReturnScoped(stack);
            }
        }
    }

    /// <summary>Provides thread-local-first access to <see cref="Shared"/>.</summary>
    public sealed class ThreadLocalPool
    {
        internal ThreadLocalPool()
        {
        }

        /// <summary>Rents an empty stack from the current thread or shared fallback.</summary>
        public Stack<T> Rent()
            => ThreadLocalFrontTier<Stack<T>, Policy>.Rent(Shared._pool);

        /// <summary>Returns a stack to the current thread or shared fallback.</summary>
        public void Return(Stack<T> stack)
        {
            ThrowIfNull(stack);
            if (!Shared.TryReset(stack))
            {
                return;
            }

            if (!ThreadLocalFrontTier<Stack<T>, Policy>.TryReturn(stack))
            {
                Shared._pool.ReturnWithoutReset(stack);
            }
        }
    }
}
