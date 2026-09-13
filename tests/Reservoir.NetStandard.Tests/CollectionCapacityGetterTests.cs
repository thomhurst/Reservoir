namespace Reservoir.Tests;

public class CollectionCapacityGetterTests
{
    [Test]
    public async Task PublicCapacityGetterAvoidsCallingEnsureCapacity()
    {
        var collection = new PublicCapacityCollection();

        await Assert.That(CollectionCapacity<PublicCapacityCollection>.IsAvailable).IsTrue();
        await Assert.That(CollectionCapacity<PublicCapacityCollection>.Get(collection)).IsEqualTo(17);
    }

    [Test]
    public async Task EnsureCapacityFallbackReceivesZero()
    {
        var collection = new EnsureOnlyCollection();

        await Assert.That(CollectionCapacity<EnsureOnlyCollection>.IsAvailable).IsTrue();
        await Assert.That(CollectionCapacity<EnsureOnlyCollection>.Get(collection)).IsEqualTo(19);
        await Assert.That(collection.RequestedCapacity).IsEqualTo(0);
    }

    [Test]
    public async Task KnownArrayLayoutRemainsReadableWithoutPublicCapacityApis()
    {
        var collection = new ArrayOnlyCollection();

        await Assert.That(CollectionCapacity<ArrayOnlyCollection>.IsAvailable).IsTrue();
        await Assert.That(CollectionCapacity<ArrayOnlyCollection>.Get(collection))
            .IsEqualTo(collection.ActualLength);
    }

    [Test]
    public async Task UnknownLayoutWithoutPublicCapacityApisRemainsUnavailable()
    {
        await Assert.That(CollectionCapacity<object>.IsAvailable).IsFalse();
    }

    private sealed class PublicCapacityCollection
    {
        public int Capacity => 17;

        public int EnsureCapacity(int capacity)
            => throw new InvalidOperationException("Public getter should avoid the growth API.");
    }

    private sealed class EnsureOnlyCollection
    {
        public int RequestedCapacity { get; private set; } = -1;

        public int EnsureCapacity(int capacity)
        {
            RequestedCapacity = capacity;
            return 19;
        }
    }

    private sealed class ArrayOnlyCollection
    {
        private readonly int[] _array = new int[23];

        public int ActualLength => _array.Length;
    }
}
