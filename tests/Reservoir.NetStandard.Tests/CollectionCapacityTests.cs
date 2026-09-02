namespace Reservoir.Tests;

// Compiled only against the netstandard2.0 build, which is where the capacity shims exist.
public class CollectionCapacityTests
{
    [Test]
    public async Task BackingArrayLengthTracksTheRuntimeCapacityOfEveryPooledCollectionType()
    {
        await AssertBackingArrayLength(
            new Dictionary<int, int>(),
            (dictionary, i) => dictionary[i] = i,
            dictionary => dictionary.EnsureCapacity(0));
        await AssertBackingArrayLength(
            new HashSet<int>(),
            (set, i) => set.Add(i),
            set => set.EnsureCapacity(0));
        await AssertBackingArrayLength(
            new Queue<int>(),
            (queue, i) => queue.Enqueue(i),
            queue => queue.EnsureCapacity(0));
        await AssertBackingArrayLength(
            new Stack<int>(),
            (stack, i) => stack.Push(i),
            stack => stack.EnsureCapacity(0));
    }

    [Test]
    public async Task BackingArrayLengthGetterIsAbsentForUnknownLayouts()
    {
        Func<List<int>, int>? getter = RuntimeCompatibility.CreateBackingArrayLengthGetter<List<int>>();

        await Assert.That(getter).IsNull();
    }

    [Test]
    public async Task CollectionCapacityMatchesEnsureCapacityWhereTheRuntimeHasIt()
    {
        var dictionary = new Dictionary<int, int>();
        for (int i = 0; i < 100; i++)
        {
            dictionary[i] = i;
        }

        await Assert.That(CollectionCapacity<Dictionary<int, int>>.IsAvailable).IsTrue();
        await Assert.That(CollectionCapacity<Dictionary<int, int>>.Get(dictionary))
            .IsEqualTo(dictionary.EnsureCapacity(0));
    }

    private static async Task AssertBackingArrayLength<TCollection>(
        TCollection collection,
        Action<TCollection, int> add,
        Func<TCollection, int> runtimeCapacity)
        where TCollection : class
    {
        Func<TCollection, int>? getter = RuntimeCompatibility.CreateBackingArrayLengthGetter<TCollection>();

        await Assert.That(getter).IsNotNull();
        await Assert.That(getter!(collection)).IsEqualTo(runtimeCapacity(collection));

        for (int i = 0; i < 100; i++)
        {
            add(collection, i);
        }

        await Assert.That(getter(collection)).IsGreaterThanOrEqualTo(100);
        await Assert.That(getter(collection)).IsEqualTo(runtimeCapacity(collection));
    }
}
