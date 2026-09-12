using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class TrackedPoolInitializationBenchmarks
{
    [Benchmark]
    public void ManualRentReturnDispose()
    {
        // Includes creation, first-thread registration, and disposal of the retained payload.
        using var pool = new ObjectPool<Payload, Policy>(default, 1, threadLocalFastPath: true);
        pool.Return(pool.Rent());
    }

    [Benchmark]
    public void ScopedRentReturnDispose()
    {
        using var pool = new ObjectPool<Payload, Policy>(default, 1);
        using var lease = pool.RentScoped();
    }

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectDestroyPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item) { }
    }
}
