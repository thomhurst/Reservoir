using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class ObjectPoolBenchmarks
{
    private readonly ObjectPool<Payload, PayloadPolicy> _pool = new(maxCapacity: 32);

    [GlobalSetup]
    public void WarmPool()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        using PooledLease<Payload, PayloadPolicy> lease = _pool.RentScoped();
    }

    [Benchmark(Baseline = true)]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);
        return payload;
    }

    [Benchmark]
    public Payload ScopedRentReturn()
    {
        using PooledLease<Payload, PayloadPolicy> lease = _pool.RentScoped();
        return lease.Value;
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
