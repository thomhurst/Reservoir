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
    }

    [Benchmark]
    public Payload RentReturn()
    {
        Payload payload = _pool.Rent();
        _pool.Return(payload);
        return payload;
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
