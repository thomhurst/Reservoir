using System.Text;

namespace Reservoir.Tests;

public class CollectionPoolTests
{
    [Test]
    public async Task ListPoolClearsAndReusesList()
    {
        var pool = new ListPool<int>(maxRetainedCapacity: 16, maxCapacity: 1);
        List<int> expected = pool.Rent();
        expected.Add(42);

        pool.Return(expected);
        List<int> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
    }

    [Test]
    public async Task DictionaryPoolClearsAndPreservesComparer()
    {
        var pool = new DictionaryPool<string, int>(
            StringComparer.OrdinalIgnoreCase,
            maxRetainedCapacity: 16,
            maxCapacity: 1);
        Dictionary<string, int> expected = pool.Rent();
        expected["KEY"] = 42;

        pool.Return(expected);
        Dictionary<string, int> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
        await Assert.That(actual.Comparer).IsSameReferenceAs(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public async Task HashSetPoolClearsAndPreservesComparer()
    {
        var pool = new HashSetPool<string>(
            StringComparer.OrdinalIgnoreCase,
            maxRetainedCapacity: 16,
            maxCapacity: 1);
        HashSet<string> expected = pool.Rent();
        expected.Add("VALUE");

        pool.Return(expected);
        HashSet<string> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
        await Assert.That(actual.Comparer).IsSameReferenceAs(StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public async Task DictionaryPoolDiscardsDictionaryWithDifferentComparer()
    {
        var pool = new DictionaryPool<string, int>(StringComparer.OrdinalIgnoreCase);
        var incompatible = new Dictionary<string, int>(StringComparer.Ordinal);

#if DEBUG || RESERVOIR_DIAGNOSTICS
        await Assert.That(() => pool.Return(incompatible)).Throws<InvalidOperationException>();
#else
        pool.Return(incompatible);
        Dictionary<string, int> rented = pool.Rent();

        await Assert.That(rented).IsNotSameReferenceAs(incompatible);
        await Assert.That(rented.Comparer).IsSameReferenceAs(StringComparer.OrdinalIgnoreCase);
#endif
    }

    [Test]
    public async Task HashSetPoolDiscardsSetWithDifferentComparer()
    {
        var pool = new HashSetPool<string>(StringComparer.OrdinalIgnoreCase);
        var incompatible = new HashSet<string>(StringComparer.Ordinal);

#if DEBUG || RESERVOIR_DIAGNOSTICS
        await Assert.That(() => pool.Return(incompatible)).Throws<InvalidOperationException>();
#else
        pool.Return(incompatible);
        HashSet<string> rented = pool.Rent();

        await Assert.That(rented).IsNotSameReferenceAs(incompatible);
        await Assert.That(rented.Comparer).IsSameReferenceAs(StringComparer.OrdinalIgnoreCase);
#endif
    }

    [Test]
    public async Task StackPoolClearsAndReusesStack()
    {
        var pool = new StackPool<int>(maxRetainedCapacity: 16, maxCapacity: 1);
        Stack<int> expected = pool.Rent();
        expected.Push(42);

        pool.Return(expected);
        Stack<int> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
    }

    [Test]
    public async Task QueuePoolClearsAndReusesQueue()
    {
        var pool = new QueuePool<int>(maxRetainedCapacity: 16, maxCapacity: 1);
        Queue<int> expected = pool.Rent();
        expected.Enqueue(42);

        pool.Return(expected);
        Queue<int> actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual).IsEmpty();
    }

    [Test]
    public async Task StringBuilderPoolClearsAndReusesBuilder()
    {
        var pool = new StringBuilderPool(maxRetainedCapacity: 16, maxCapacity: 1);
        StringBuilder expected = pool.Rent();
        expected.Append("value");

        pool.Return(expected);
        StringBuilder actual = pool.Rent();

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(actual.Length).IsEqualTo(0);
    }

    [Test]
    public async Task StringBuilderPoolDiscardsBuilderWithRestrictedMaximumCapacity()
    {
        var pool = new StringBuilderPool(maxRetainedCapacity: 16, maxCapacity: 1);
        var incompatible = new StringBuilder(capacity: 1, maxCapacity: 1);

#if DEBUG || RESERVOIR_DIAGNOSTICS
        await Assert.That(() => pool.Return(incompatible)).Throws<InvalidOperationException>();
#else
        pool.Return(incompatible);
        StringBuilder rented = pool.Rent();

        await Assert.That(rented).IsNotSameReferenceAs(incompatible);
        await Assert.That(rented.MaxCapacity).IsEqualTo(int.MaxValue);
#endif
    }

    [Test]
    [Arguments("list")]
    [Arguments("dictionary")]
    [Arguments("hash-set")]
    [Arguments("stack")]
    [Arguments("queue")]
    [Arguments("string-builder")]
    public async Task OversizedInstancesAreDiscarded(string poolType)
    {
        object returned;
        object replacement;

        switch (poolType)
        {
            case "list":
                var listPool = new ListPool<int>(4, 1);
                List<int> list = listPool.Rent();
                list.Capacity = 8;
                listPool.Return(list);
                returned = list;
                replacement = listPool.Rent();
                break;
            case "dictionary":
                var dictionaryPool = new DictionaryPool<int, int>(4, 1);
                Dictionary<int, int> dictionary = dictionaryPool.Rent();
                dictionary.EnsureCapacity(8);
                dictionaryPool.Return(dictionary);
                returned = dictionary;
                replacement = dictionaryPool.Rent();
                break;
            case "hash-set":
                var setPool = new HashSetPool<int>(4, 1);
                HashSet<int> set = setPool.Rent();
                set.EnsureCapacity(8);
                setPool.Return(set);
                returned = set;
                replacement = setPool.Rent();
                break;
            case "stack":
                var stackPool = new StackPool<int>(4, 1);
                Stack<int> stack = stackPool.Rent();
                stack.EnsureCapacity(8);
                stackPool.Return(stack);
                returned = stack;
                replacement = stackPool.Rent();
                break;
            case "queue":
                var queuePool = new QueuePool<int>(4, 1);
                Queue<int> queue = queuePool.Rent();
                queue.EnsureCapacity(8);
                queuePool.Return(queue);
                returned = queue;
                replacement = queuePool.Rent();
                break;
            case "string-builder":
                var builderPool = new StringBuilderPool(4, 1);
                StringBuilder builder = builderPool.Rent();
                builder.Capacity = 8;
                builderPool.Return(builder);
                returned = builder;
                replacement = builderPool.Rent();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(poolType));
        }

        await Assert.That(replacement).IsNotSameReferenceAs(returned);
    }

#if !DEBUG && !RESERVOIR_DIAGNOSTICS
    [Test]
    public async Task WarmSharedListPoolRentAndReturnAllocatesNothing()
    {
        ListPool<int> pool = ListPool<int>.Shared;
        List<int> warm = pool.Rent();
        pool.Return(warm);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            List<int> list = pool.Rent();
            pool.Return(list);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }
#endif

    [Test]
    public async Task InvalidLimitsThrow()
    {
        await Assert.That(() => new ListPool<int>(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StringBuilderPool(16, 0)).Throws<ArgumentOutOfRangeException>();
    }
}
