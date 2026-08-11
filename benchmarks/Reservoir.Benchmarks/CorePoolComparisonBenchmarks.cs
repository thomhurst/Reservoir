using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.ObjectPool;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class CorePoolComparisonBenchmarks
{
    private const int PoolCapacity = 32;

    private readonly Consumer _consumer = new();
    private readonly ConcurrentBag<Payload> _concurrentBag = [];
    private readonly DefaultObjectPool<Payload> _microsoftPool = new(
        new DefaultPooledObjectPolicy<Payload>(),
        PoolCapacity);
    private readonly ObjectPool<Payload, PayloadPolicy> _reservoirPool = new(PoolCapacity);

    [GlobalSetup]
    public void WarmPools()
    {
        Payload reservoirPayload = _reservoirPool.Rent();
        _reservoirPool.Return(reservoirPayload);

        Payload microsoftPayload = _microsoftPool.Get();
        _microsoftPool.Return(microsoftPayload);

        _concurrentBag.Add(new Payload());
    }

    [Benchmark(Baseline = true)]
    public void New()
        => _consumer.Consume(new Payload());

    [Benchmark]
    public void Reservoir()
    {
        Payload payload = _reservoirPool.Rent();
        _consumer.Consume(payload);
        _reservoirPool.Return(payload);
    }

    [Benchmark]
    public void MicrosoftExtensionsObjectPool()
    {
        Payload payload = _microsoftPool.Get();
        _consumer.Consume(payload);
        _microsoftPool.Return(payload);
    }

    [Benchmark]
    public void ConcurrentBag()
    {
        if (!_concurrentBag.TryTake(out Payload? payload))
        {
            payload = new Payload();
        }

        _consumer.Consume(payload);
        _concurrentBag.Add(payload);
    }

    public sealed class Payload
    {
        public byte[] Buffer { get; } = GC.AllocateUninitializedArray<byte>(256);
    }

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
