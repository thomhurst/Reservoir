using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser]
public class SharedClearBenchmarks
{
    private ObjectPool<Payload, Policy> _pool = null!;
    private Payload[] _items = null!;

    [Params(1, 8, 64, 65)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkAsset.Verify();
        _pool = new ObjectPool<Payload, Policy>(default, Capacity);
        _items = new Payload[Capacity];
        for (int i = 0; i < _items.Length; i++)
        {
            _items[i] = new Payload();
        }

        RefillAndClear();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark]
    public void ClearEmpty() => _pool.Clear();

    [Benchmark]
    public void ReturnOneAndClear()
    {
        _pool.Return(_items[0]);
        _pool.Clear();
    }

    [Benchmark]
    public void RefillAndClear()
    {
        // Includes return costs. Preallocated payloads and no-op cleanup isolate storage work.
        foreach (Payload item in _items)
        {
            _pool.Return(item);
        }

        _pool.Clear();
    }

    public sealed class Payload;

    public readonly struct Policy : IPooledObjectDestroyPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload item) => true;
        public void Destroy(Payload item) { }
    }
}
