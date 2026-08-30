#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Reservoir;

/// <summary>
/// Provides pools of reusable <see cref="CancellationTokenSource"/> instances.
/// </summary>
/// <remarks>
/// A renter must dispose each rental exactly once as the source's sole owner. Except for the
/// upstream callback owned by <see cref="RentLinked(CancellationToken)"/>, no token readers or
/// cancellation operations may remain in flight because
/// <c>CancellationTokenSource.TryReset()</c> is not thread-safe with concurrent use. Linked rentals
/// register an upstream token with a pooled source; they do not pool sources created by
/// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken)"/>.
/// Scoped rentals retain one reset source per participating thread in addition to the bounded
/// shared store. Dispose dedicated pools to release that thread-local retention.
/// </remarks>
public
sealed class CancellationTokenSourcePool : IDisposable
{
    private readonly ObjectPool<PooledCancellationTokenSource, Policy> _pool;
    private TrackedInstanceThreadLocalFrontTier<PooledCancellationTokenSource> _scopedTier;
    private int _isDisposed;

    /// <summary>Gets the shared pool.</summary>
    public static CancellationTokenSourcePool Shared { get; } = new();

    /// <summary>Initializes a pool with the default capacity.</summary>
    public CancellationTokenSourcePool()
        : this(ObjectPool<PooledCancellationTokenSource, Policy>.DefaultMaximumRetained)
    {
    }

    /// <summary>Initializes a pool with a custom capacity.</summary>
    public CancellationTokenSourcePool(int maxCapacity)
    {
        _pool = new ObjectPool<PooledCancellationTokenSource, Policy>(
            new Policy(this),
            maxCapacity);
    }

    /// <summary>Gets the maximum number of sources retained by this pool.</summary>
    public int MaximumRetained => _pool.MaximumRetained;

    /// <summary>Rents a source that returns to this pool when disposed.</summary>
    public CancellationTokenSource Rent() => _pool.Rent();

    /// <summary>
    /// Rents a source canceled by <paramref name="upstreamToken"/> that returns to this pool when
    /// disposed.
    /// </summary>
    /// <remarks>
    /// Disposal unregisters the upstream token and waits for any in-flight upstream callback before
    /// the source can be reused. A token that cannot be canceled uses the normal <see cref="Rent"/>
    /// path.
    /// </remarks>
    public CancellationTokenSource RentLinked(CancellationToken upstreamToken)
    {
        PooledCancellationTokenSource source = _pool.Rent();

        if (upstreamToken.CanBeCanceled)
        {
            source.RegisterUpstream(upstreamToken);
        }

        return source;
    }

    /// <summary>
    /// Rents a source owned by a stack-only lease using a per-pool thread-local fast path.
    /// </summary>
    public Lease RentScoped()
    {
        PooledCancellationTokenSource source = RentScopedValue();
        return new Lease(this, source);
    }

    /// <summary>
    /// Rents a source owned by a stack-only lease and also exposes the source directly.
    /// </summary>
    public Lease RentScoped(out CancellationTokenSource source)
    {
        PooledCancellationTokenSource pooledSource = RentScopedValue();
        source = pooledSource;
        return new Lease(this, pooledSource);
    }

    /// <summary>
    /// Disposes all retained sources while leaving the pool usable.
    /// </summary>
    public void Clear()
        => ClearRetained(disposePool: false);

    /// <summary>
    /// Disposes all retained sources and permanently closes the pool.
    /// </summary>
    /// <remarks>Disposing <see cref="Shared"/> clears it without closing it.</remarks>
    public void Dispose()
    {
        if (ReferenceEquals(this, Shared))
        {
            Clear();
            return;
        }

        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            ClearRetained(disposePool: true);
        }
    }

    private void ClearRetained(bool disposePool)
    {
        Exception? firstException = null;
        try
        {
            _scopedTier.Clear(_pool);
        }
        catch (Exception exception)
        {
            firstException = exception;
        }

        try
        {
            if (disposePool)
            {
                _pool.Dispose();
            }
            else
            {
                _pool.Clear();
            }
        }
        catch (Exception exception)
        {
            firstException ??= exception;
        }

        if (firstException is not null)
        {
            ExceptionDispatchInfo.Capture(firstException).Throw();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PooledCancellationTokenSource RentScopedValue()
    {
        ThrowIfDisposed();
        PooledCancellationTokenSource source = _scopedTier.Rent(_pool);
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            return source;
        }

        _pool.Destroy(source);
        return ThrowDisposed();
    }

    private void ReturnScoped(PooledCancellationTokenSource source)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            _pool.Destroy(source);
            return;
        }

        if (!TryReset(source))
        {
            return;
        }

        if (Volatile.Read(ref _isDisposed) != 0)
        {
            _pool.Destroy(source);
            return;
        }

        if (!_scopedTier.TryReturn(source))
        {
            _pool.ReturnWithoutResetWithLifecycle(source);
            return;
        }

        if (Volatile.Read(ref _isDisposed) != 0 && _scopedTier.TryRemove(source))
        {
            _pool.Destroy(source);
        }
    }

    private bool TryReset(PooledCancellationTokenSource source)
    {
        try
        {
            if (new Policy(this).TryReset(source))
            {
                return true;
            }
        }
        catch
        {
            _pool.Destroy(source);
            throw;
        }

        _pool.Destroy(source);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            ThrowDisposed();
        }
    }

#if NET5_0_OR_GREATER
    [DoesNotReturn]
#endif
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PooledCancellationTokenSource ThrowDisposed()
        => throw new ObjectDisposedException(typeof(CancellationTokenSourcePool).FullName);

    private void Return(PooledCancellationTokenSource source) => _pool.Return(source);

    internal sealed class PooledCancellationTokenSource : CancellationTokenSource
    {
        private readonly CancellationTokenSourcePool _owner;
        private CancellationTokenRegistration _upstreamRegistration;

        internal PooledCancellationTokenSource(CancellationTokenSourcePool owner)
        {
            _owner = owner;
        }

        internal void RegisterUpstream(CancellationToken upstreamToken)
        {
#if NETCOREAPP3_0_OR_GREATER
            _upstreamRegistration = upstreamToken.UnsafeRegister(
                static state => ((CancellationTokenSource)state!).Cancel(),
                this);
#else
            _upstreamRegistration = upstreamToken.Register(
                static state => ((CancellationTokenSource)state!).Cancel(),
                this,
                useSynchronizationContext: false);
#endif
        }

        internal void DisposePermanently()
        {
            DisposeUpstreamRegistration();
            base.Dispose(true);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeUpstreamRegistration();
                _owner.Return(this);
            }
        }

        private void DisposeUpstreamRegistration()
        {
            CancellationTokenRegistration registration = _upstreamRegistration;
            _upstreamRegistration = default;
            registration.Dispose();
        }
    }

    /// <summary>Owns a rented source and returns it on disposal.</summary>
    public ref struct Lease
    {
        private readonly CancellationTokenSourcePool? _pool;
        private ScopedPoolLease<PooledCancellationTokenSource> _lease;

        internal Lease(
            CancellationTokenSourcePool pool,
            PooledCancellationTokenSource source)
        {
            _pool = pool;
            _lease = new ScopedPoolLease<PooledCancellationTokenSource>(source);
        }

        /// <summary>Gets the rented source while this lease owns it.</summary>
        public readonly CancellationTokenSource Value
            => _lease.Value;

        /// <summary>Returns the source. Repeated calls and stale copies are ignored.</summary>
        public void Dispose()
        {
            if (_lease.TryRelease(out PooledCancellationTokenSource source))
            {
                _pool!.ReturnScoped(source);
            }
        }
    }

    internal readonly struct Policy : IPooledObjectDestroyPolicy<PooledCancellationTokenSource>
    {
#if !NET6_0_OR_GREATER
        private static readonly Func<CancellationTokenSource, bool>? s_tryReset
            = RuntimeCompatibility.CreateParameterlessBooleanMethod<CancellationTokenSource>(
                "TryReset");
#endif

        private readonly CancellationTokenSourcePool _owner;

        internal Policy(CancellationTokenSourcePool owner)
        {
            _owner = owner;
        }

        public PooledCancellationTokenSource Create() => new(_owner);

        public bool TryReset(PooledCancellationTokenSource source)
        {
#if NET6_0_OR_GREATER
            // TryReset disarms the runtime timer and rejects reuse once its callback was queued.
            // ObjectPool permanently disposes the source whenever this returns false.
            return source.TryReset();
#else
            return s_tryReset?.Invoke(source) == true;
#endif
        }

        public void Destroy(PooledCancellationTokenSource source) => source.DisposePermanently();
    }
}
