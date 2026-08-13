using System.Collections.Concurrent;
using System.Text;

namespace Reservoir.Tests;

public class ThreadLocalPoolTests
{
    [Test]
    public async Task ThreadLocalListPoolResetsAndReusesOnSameThread()
    {
        ListPool<ListMarker>.ThreadLocalPool pool = ListPool<ListMarker>.ThreadLocalShared;
        List<ListMarker> expected = pool.Rent();
        expected.Add(new ListMarker());

        pool.Return(expected);
        List<ListMarker> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
        pool.Return(actual);
    }

    [Test]
    public async Task ThreadLocalPoolFallsBackWhenThreadSlotIsOccupied()
    {
        ListPool<FallbackMarker>.ThreadLocalPool pool
            = ListPool<FallbackMarker>.ThreadLocalShared;
        List<FallbackMarker> first = pool.Rent();
        List<FallbackMarker> second = pool.Rent();

        pool.Return(first);
        pool.Return(second);

        List<FallbackMarker> firstRental = pool.Rent();
        List<FallbackMarker> secondRental = pool.Rent();

        await Assert.That(firstRental).IsSameReferenceAs(first);
        await Assert.That(secondRental).IsSameReferenceAs(second);
        pool.Return(firstRental);
        pool.Return(secondRental);
    }

    [Test]
    public async Task ThreadLocalPoolFollowsReturningThread()
    {
        ListPool<CrossThreadMarker>.ThreadLocalPool pool
            = ListPool<CrossThreadMarker>.ThreadLocalShared;
        List<CrossThreadMarker> expected = pool.Rent();
        var result = new BlockingCollection<List<CrossThreadMarker>>();

        var thread = new Thread(() =>
        {
            pool.Return(expected);
            result.Add(pool.Rent());
        });

        thread.Start();
        thread.Join();

        await Assert.That(result.Take()).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task ThreadLocalPoolsResetEverySpecializedType()
    {
        Queue<int> queue = QueuePool<int>.ThreadLocalShared.Rent();
        queue.Enqueue(1);
        QueuePool<int>.ThreadLocalShared.Return(queue);

        Stack<int> stack = StackPool<int>.ThreadLocalShared.Rent();
        stack.Push(1);
        StackPool<int>.ThreadLocalShared.Return(stack);

        Dictionary<int, int> dictionary = DictionaryPool<int, int>.ThreadLocalShared.Rent();
        dictionary[1] = 1;
        DictionaryPool<int, int>.ThreadLocalShared.Return(dictionary);

        HashSet<int> set = HashSetPool<int>.ThreadLocalShared.Rent();
        set.Add(1);
        HashSetPool<int>.ThreadLocalShared.Return(set);

        StringBuilder builder = StringBuilderPool.ThreadLocalShared.Rent();
        builder.Append('x');
        StringBuilderPool.ThreadLocalShared.Return(builder);

        Queue<int> rentedQueue = QueuePool<int>.ThreadLocalShared.Rent();
        Stack<int> rentedStack = StackPool<int>.ThreadLocalShared.Rent();
        Dictionary<int, int> rentedDictionary
            = DictionaryPool<int, int>.ThreadLocalShared.Rent();
        HashSet<int> rentedSet = HashSetPool<int>.ThreadLocalShared.Rent();
        StringBuilder rentedBuilder = StringBuilderPool.ThreadLocalShared.Rent();

        await Assert.That(rentedQueue).IsEmpty();
        await Assert.That(rentedStack).IsEmpty();
        await Assert.That(rentedDictionary).IsEmpty();
        await Assert.That(rentedSet).IsEmpty();
        await Assert.That(rentedBuilder.Length).IsEqualTo(0);

        QueuePool<int>.ThreadLocalShared.Return(rentedQueue);
        StackPool<int>.ThreadLocalShared.Return(rentedStack);
        DictionaryPool<int, int>.ThreadLocalShared.Return(rentedDictionary);
        HashSetPool<int>.ThreadLocalShared.Return(rentedSet);
        StringBuilderPool.ThreadLocalShared.Return(rentedBuilder);
    }

    [Test]
    public async Task ThreadLocalDefaultComparerPoolsRejectIncompatibleComparers()
    {
        var dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DictionaryPool<string, int>.ThreadLocalShared.Return(dictionary);
        HashSetPool<string>.ThreadLocalShared.Return(set);

        Dictionary<string, int> rentedDictionary
            = DictionaryPool<string, int>.ThreadLocalShared.Rent();
        HashSet<string> rentedSet = HashSetPool<string>.ThreadLocalShared.Rent();

        await Assert.That(rentedDictionary).IsNotSameReferenceAs(dictionary);
        await Assert.That(rentedSet).IsNotSameReferenceAs(set);
    }

    [Test]
    public async Task ThreadLocalPoolRejectsOversizedItemsWithoutClearingThem()
    {
        List<object> occupied = ListPool<object>.ThreadLocalShared.Rent();
        ListPool<object>.ThreadLocalShared.Return(occupied);

        var oversized = new List<object>(ListPool<object>.DefaultMaximumRetainedCapacity + 1)
        {
            new(),
        };

        ListPool<object>.ThreadLocalShared.Return(oversized);
        List<object> rented = ListPool<object>.ThreadLocalShared.Rent();

        await Assert.That(rented).IsSameReferenceAs(occupied);
        await Assert.That(oversized).Count().IsEqualTo(1);
        ListPool<object>.ThreadLocalShared.Return(rented);
    }

    [Test]
    public async Task ThreadLocalPoolDestroysRejectedDisposableCollection()
    {
        var list = new DisposableList(
            ListPool<int>.DefaultMaximumRetainedCapacity + 1);

        ListPool<int>.ThreadLocalShared.Return(list);

        await Assert.That(list.IsDisposed).IsTrue();
    }

    [Test]
    public async Task WarmThreadLocalPoolAllocatesNothing()
    {
        ListPool<AllocationMarker>.ThreadLocalPool pool
            = ListPool<AllocationMarker>.ThreadLocalShared;
        pool.Return(pool.Rent());

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            List<AllocationMarker> list = pool.Rent();
            pool.Return(list);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
    }

    private sealed class ListMarker;
    private sealed class FallbackMarker;
    private sealed class CrossThreadMarker;
    private sealed class AllocationMarker;

    private sealed class DisposableList(int capacity) : List<int>(capacity), IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
