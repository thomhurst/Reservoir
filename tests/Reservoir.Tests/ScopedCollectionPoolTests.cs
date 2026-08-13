using System.Collections.Concurrent;
using System.Text;

namespace Reservoir.Tests;

public class ScopedCollectionPoolTests
{
    [Test]
    public async Task ScopedPoolsResetAndReuseEverySpecializedType()
    {
        var listPool = new ListPool<int>();
        var queuePool = new QueuePool<int>();
        var stackPool = new StackPool<int>();
        var dictionaryPool = new DictionaryPool<string, int>(StringComparer.OrdinalIgnoreCase);
        var hashSetPool = new HashSetPool<string>(StringComparer.OrdinalIgnoreCase);
        var builderPool = new StringBuilderPool();

        List<int> list;
        Queue<int> queue;
        Stack<int> stack;
        Dictionary<string, int> dictionary;
        HashSet<string> set;
        StringBuilder builder;

        {
            using ListPool<int>.Lease lease = listPool.RentScoped(out list);
            list.Add(1);
        }

        {
            using QueuePool<int>.Lease lease = queuePool.RentScoped(out queue);
            queue.Enqueue(1);
        }

        {
            using StackPool<int>.Lease lease = stackPool.RentScoped(out stack);
            stack.Push(1);
        }

        {
            using DictionaryPool<string, int>.Lease lease
                = dictionaryPool.RentScoped(out dictionary);
            dictionary["key"] = 1;
        }

        {
            using HashSetPool<string>.Lease lease = hashSetPool.RentScoped(out set);
            set.Add("value");
        }

        {
            using StringBuilderPool.Lease lease = builderPool.RentScoped(out builder);
            builder.Append('x');
        }

        bool listReused;
        bool queueReused;
        bool stackReused;
        bool dictionaryReused;
        bool setReused;
        bool builderReused;

        {
            using ListPool<int>.Lease lease = listPool.RentScoped();
            listReused = ReferenceEquals(lease.Value, list) && lease.Value.Count == 0;
        }

        {
            using QueuePool<int>.Lease lease = queuePool.RentScoped();
            queueReused = ReferenceEquals(lease.Value, queue) && lease.Value.Count == 0;
        }

        {
            using StackPool<int>.Lease lease = stackPool.RentScoped();
            stackReused = ReferenceEquals(lease.Value, stack) && lease.Value.Count == 0;
        }

        {
            using DictionaryPool<string, int>.Lease lease = dictionaryPool.RentScoped();
            dictionaryReused = ReferenceEquals(lease.Value, dictionary)
                && lease.Value.Count == 0
                && ReferenceEquals(lease.Value.Comparer, StringComparer.OrdinalIgnoreCase);
        }

        {
            using HashSetPool<string>.Lease lease = hashSetPool.RentScoped();
            setReused = ReferenceEquals(lease.Value, set)
                && lease.Value.Count == 0
                && ReferenceEquals(lease.Value.Comparer, StringComparer.OrdinalIgnoreCase);
        }

        {
            using StringBuilderPool.Lease lease = builderPool.RentScoped();
            builderReused = ReferenceEquals(lease.Value, builder) && lease.Value.Length == 0;
        }

        await Assert.That(listReused).IsTrue();
        await Assert.That(queueReused).IsTrue();
        await Assert.That(stackReused).IsTrue();
        await Assert.That(dictionaryReused).IsTrue();
        await Assert.That(setReused).IsTrue();
        await Assert.That(builderReused).IsTrue();
    }

    [Test]
    public async Task ScopedPoolsKeepCustomInstancesIsolated()
    {
        var firstPool = new ListPool<IsolationMarker>();
        var secondPool = new ListPool<IsolationMarker>();
        List<IsolationMarker> first;
        List<IsolationMarker> second;

        {
            using ListPool<IsolationMarker>.Lease lease = firstPool.RentScoped(out first);
        }

        {
            using ListPool<IsolationMarker>.Lease lease = secondPool.RentScoped(out second);
        }

        bool firstReused;
        bool secondReused;

        {
            using ListPool<IsolationMarker>.Lease lease = firstPool.RentScoped();
            firstReused = ReferenceEquals(lease.Value, first);
        }

        {
            using ListPool<IsolationMarker>.Lease lease = secondPool.RentScoped();
            secondReused = ReferenceEquals(lease.Value, second);
        }

        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(firstReused).IsTrue();
        await Assert.That(secondReused).IsTrue();
    }

    [Test]
    public async Task NestedScopedLeasesPreserveBothRentals()
    {
        var pool = new ListPool<NestedMarker>(maxRetainedCapacity: 16, maxCapacity: 2);
        ListPool<NestedMarker>.Lease firstLease = pool.RentScoped();
        ListPool<NestedMarker>.Lease secondLease = pool.RentScoped();
        List<NestedMarker> first = firstLease.Value;
        List<NestedMarker> second = secondLease.Value;

        secondLease.Dispose();
        firstLease.Dispose();

        ListPool<NestedMarker>.Lease firstRentalLease = pool.RentScoped();
        ListPool<NestedMarker>.Lease secondRentalLease = pool.RentScoped();
        List<NestedMarker> firstRental = firstRentalLease.Value;
        List<NestedMarker> secondRental = secondRentalLease.Value;
        firstRentalLease.Dispose();
        secondRentalLease.Dispose();

        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(new[] { firstRental, secondRental }).Contains(first);
        await Assert.That(new[] { firstRental, secondRental }).Contains(second);
    }

    [Test]
    public async Task ScopedLeaseCopiesReturnValueOnce()
    {
        var pool = new ListPool<CopyMarker>(maxRetainedCapacity: 16, maxCapacity: 2);
        ListPool<CopyMarker>.Lease lease = pool.RentScoped();
        ListPool<CopyMarker>.Lease stale = lease;
        List<CopyMarker> value = lease.Value;

        lease.Dispose();
        ListPool<CopyMarker>.Lease nextLease = pool.RentScoped();
        stale.Dispose();
        List<CopyMarker> concurrent = pool.Rent();
        bool nextReusedValue = ReferenceEquals(nextLease.Value, value);
        bool ownershipRemainedExclusive = !ReferenceEquals(nextLease.Value, concurrent);
        nextLease.Dispose();
        pool.Return(concurrent);

        await Assert.That(nextReusedValue).IsTrue();
        await Assert.That(ownershipRemainedExclusive).IsTrue();
    }

    [Test]
    public async Task ScopedLeaseValueThrowsAfterDispose()
    {
        var pool = new ListPool<ValueMarker>();
        ListPool<ValueMarker>.Lease lease = pool.RentScoped();
        lease.Dispose();

        bool threw = false;
        try
        {
            _ = lease.Value;
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task ScopedPoolRejectsOversizedReturnedCollection()
    {
        var pool = new ListPool<object>(maxRetainedCapacity: 4, maxCapacity: 1);
        ListPool<object>.Lease lease = pool.RentScoped();
        List<object> oversized = lease.Value;
        oversized.EnsureCapacity(5);
        oversized.Add(new object());
        lease.Dispose();

        ListPool<object>.Lease replacementLease = pool.RentScoped();
        bool wasReplaced = !ReferenceEquals(replacementLease.Value, oversized);
        replacementLease.Dispose();

        await Assert.That(wasReplaced).IsTrue();
        await Assert.That(oversized).Count().IsEqualTo(1);
    }

    [Test]
    public async Task WarmScopedPoolAllocatesNothing()
    {
        var pool = new ListPool<AllocationMarker>();

        {
            using ListPool<AllocationMarker>.Lease lease = pool.RentScoped();
            _ = lease.Value.Count;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;

        for (int i = 0; i < 1_000; i++)
        {
            using ListPool<AllocationMarker>.Lease lease = pool.RentScoped();
            total += lease.Value.Count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(total).IsEqualTo(0);
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentFirstScopedRentKeepsOwnershipExclusive()
    {
        const int threadCount = 8;
        var pool = new ListPool<ConcurrentMarker>();
        var start = new ManualResetEventSlim();
        var allRented = new CountdownEvent(threadCount);
        var release = new ManualResetEventSlim();
        var active = new ConcurrentDictionary<List<ConcurrentMarker>, byte>();
        var failures = new ConcurrentQueue<Exception>();
        var threads = new Thread[threadCount];

        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                bool signaled = false;
                try
                {
                    start.Wait();
                    using ListPool<ConcurrentMarker>.Lease lease = pool.RentScoped();
                    if (!active.TryAdd(lease.Value, 0))
                    {
                        throw new InvalidOperationException("A scoped value had multiple owners.");
                    }

                    allRented.Signal();
                    signaled = true;
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Timed out waiting to release scoped values.");
                    }

                    _ = active.TryRemove(lease.Value, out _);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
                finally
                {
                    if (!signaled)
                    {
                        allRented.Signal();
                    }
                }
            });
            threads[i].Start();
        }

        start.Set();
        bool allThreadsRented = allRented.Wait(TimeSpan.FromSeconds(10));
        release.Set();
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        await Assert.That(allThreadsRented).IsTrue();
        await Assert.That(failures).IsEmpty();
    }

    private sealed class IsolationMarker;
    private sealed class NestedMarker;
    private sealed class CopyMarker;
    private sealed class ValueMarker;
    private sealed class AllocationMarker;
    private sealed class ConcurrentMarker;
}
