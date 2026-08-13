using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class RuntimePolicyObjectPoolBenchmarks
{
    private readonly ObjectPool<Payload> _pool = new(new PayloadPolicy(), maxCapacity: 32);

    [GlobalSetup]
    public void WarmPool()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        using PooledLease<Payload> lease = _pool.RentScoped();
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
        using PooledLease<Payload> lease = _pool.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped(out Payload payload);
        return payload;
    }

    public sealed class Payload;

    private sealed class PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class FactoryObjectPoolBenchmarks
{
    private readonly ObjectPool<Payload> _pool = new(() => new Payload(), maxCapacity: 32);

    [GlobalSetup]
    public void WarmPool()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);

        using PooledLease<Payload> lease = _pool.RentScoped();
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
        using PooledLease<Payload> lease = _pool.RentScoped();
        return lease.Value;
    }

    [Benchmark]
    public Payload ScopedOutRentReturn()
    {
        using PooledLease<Payload> lease = _pool.RentScoped(out Payload payload);
        return payload;
    }

    public sealed class Payload;
}
