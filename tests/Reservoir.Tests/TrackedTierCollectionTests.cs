using System.Runtime.CompilerServices;

namespace Reservoir.Tests;

public class TrackedTierCollectionTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DestroyedItemsAreCollectibleWhilePoolRemainsAlive(bool scoped, bool dispose)
    {
        var state = new State();
        var pool = new ObjectPool<Item, Policy>(new Policy(state), 1, threadLocalFastPath: !scoped);
        WeakReference<Item> item = Seed(pool, scoped);

        if (dispose)
        {
            pool.Dispose();
        }
        else
        {
            pool.Clear();
        }

        bool alive = CollectAndCheck(item);
        GC.KeepAlive(pool);
        await Assert.That(state.Destroyed).IsEqualTo(1);
        await Assert.That(alive).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DestroyedItemsAreCollectibleAfterCleanupThrows(bool scoped)
    {
        var state = new State { Failure = new InvalidOperationException("Cleanup failed.") };
        using var pool = new ObjectPool<Item, Policy>(new Policy(state), 1, threadLocalFastPath: !scoped);
        WeakReference<Item> item = Seed(pool, scoped);

        await Assert.That(() => pool.Clear()).Throws<InvalidOperationException>();

        bool alive = CollectAndCheck(item);
        GC.KeepAlive(pool);
        await Assert.That(state.Destroyed).IsEqualTo(1);
        await Assert.That(alive).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DestroyedScopedSourcesAreCollectibleWhilePoolRemainsAlive(bool dispose)
    {
        var pool = new CancellationTokenSourcePool(1);
        WeakReference<CancellationTokenSource> source = Seed(pool);
        if (dispose)
        {
            pool.Dispose();
        }
        else
        {
            pool.Clear();
        }

        bool alive = CollectAndCheck(source);
        GC.KeepAlive(pool);
        await Assert.That(alive).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentRentClearDisposeAndGcPreserveOwnership(bool scoped)
    {
        var state = new RaceState();
        using var pool = new ObjectPool<RaceItem, RacePolicy>(new RacePolicy(state), 4, threadLocalFastPath: !scoped);
        using var start = new Barrier(5);
        using var returned = new ManualResetEventSlim();
        IEnumerable<Action<CancellationToken>> workers = Enumerable.Range(0, 4)
            .Select<int, Action<CancellationToken>>(_ => token =>
            {
                start.SignalAndWait(token);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (scoped)
                        {
                            using PooledLease<RaceItem, RacePolicy> lease = pool.RentScoped();
                            Use(lease.Value, state);
                        }
                        else
                        {
                            RaceItem item = pool.Rent();
                            Use(item, state);
                            pool.Return(item);
                        }

                        returned.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            });

        await ConcurrentTestWorkers.RunAsync(workers.Append(token =>
        {
            start.SignalAndWait(token);
            for (int i = 0; i < 500; i++)
            {
                token.ThrowIfCancellationRequested();
                pool.Clear();
                if ((i & 31) == 0)
                {
                    GC.Collect();
                }
            }

            returned.Wait(token);
            pool.Dispose();
        }));

        await Assert.That(state.Failures).IsEqualTo(0);
        await Assert.That(state.Destroyed).IsEqualTo(state.Created);
    }

    [Test]
    public async Task ConcurrentScopedSourcesClearDisposeAndGcPreserveOwnership()
    {
        using var pool = new CancellationTokenSourcePool(4);
        using var start = new Barrier(5);
        using var returned = new ManualResetEventSlim();
        IEnumerable<Action<CancellationToken>> workers = Enumerable.Range(0, 4)
            .Select<int, Action<CancellationToken>>(_ => token =>
            {
                start.SignalAndWait(token);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    CancellationTokenSourcePool.Lease lease;
                    try
                    {
                        lease = pool.RentScoped();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    using (lease)
                    {
                        // Accessing Token throws if a concurrent clear destroyed this rental.
                        lease.Value.Token.ThrowIfCancellationRequested();
                        Thread.SpinWait(8);
                        lease.Value.Token.ThrowIfCancellationRequested();
                    }

                    returned.Set();
                }
            });

        await ConcurrentTestWorkers.RunAsync(workers.Append(token =>
        {
            start.SignalAndWait(token);
            for (int i = 0; i < 500; i++)
            {
                token.ThrowIfCancellationRequested();
                pool.Clear();
                if ((i & 31) == 0)
                {
                    GC.Collect();
                }
            }

            returned.Wait(token);
            pool.Dispose();
        }));
    }

    private static void Use(RaceItem item, RaceState state)
    {
        if (Interlocked.CompareExchange(ref item.State, 1, 0) != 0)
        {
            Interlocked.Increment(ref state.Failures);
        }

        Thread.SpinWait(8);
        if (Interlocked.Exchange(ref item.State, 0) != 1)
        {
            Interlocked.Increment(ref state.Failures);
        }
    }

    private sealed class RaceItem
    {
        // 0 = available, 1 = rented, 2 = destroyed.
        internal int State;
    }

    private sealed class RaceState
    {
        internal int Created;
        internal int Destroyed;
        internal int Failures;
    }

    private readonly struct RacePolicy(RaceState state) : IPooledObjectDestroyPolicy<RaceItem>
    {
        public RaceItem Create()
        {
            Interlocked.Increment(ref state.Created);
            return new RaceItem();
        }

        public bool TryReset(RaceItem item) => true;

        public void Destroy(RaceItem item)
        {
            if (Interlocked.Exchange(ref item.State, 2) != 0)
            {
                Interlocked.Increment(ref state.Failures);
            }

            Interlocked.Increment(ref state.Destroyed);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Item> Seed(ObjectPool<Item, Policy> pool, bool scoped)
    {
        if (scoped)
        {
            using PooledLease<Item, Policy> lease = pool.RentScoped();
            return new WeakReference<Item>(lease.Value);
        }

        Item item = pool.Rent();
        pool.Return(item);
        return new WeakReference<Item>(item);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<CancellationTokenSource> Seed(CancellationTokenSourcePool pool)
    {
        using CancellationTokenSourcePool.Lease lease = pool.RentScoped();
        return new WeakReference<CancellationTokenSource>(lease.Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CollectAndCheck<T>(WeakReference<T> reference) where T : class
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return reference.TryGetTarget(out _);
    }

    private sealed class Item
    {
        internal readonly byte[] Payload = new byte[1024 * 1024];
    }

    private sealed class State
    {
        internal int Destroyed;
        internal Exception? Failure;
    }

    private readonly struct Policy(State state) : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item) => true;
        public void Destroy(Item item)
        {
            Interlocked.Increment(ref state.Destroyed);
            if (state.Failure is { } failure)
            {
                throw failure;
            }
        }
    }
}
