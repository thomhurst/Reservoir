using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>Models recursive callers holding one rental at each level of a synchronous traversal.</summary>
[MemoryDiagnoser]
public class NestedScopedPoolBenchmarks
{
    private readonly ObjectPool<Payload, Policy> _pool = new(maxCapacity: 64);
    private readonly ListPool<int> _lists = new(maxRetainedCapacity: 16, maxCapacity: 64);

    [Params(1, 8, 32)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Warm()
    {
        _ = ObjectScopes();
        _ = ListScopes();
    }

    [Benchmark]
    public int ObjectScopes() => RentObjects(Depth);

    [Benchmark]
    public int ListScopes() => RentLists(Depth);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int RentObjects(int depth)
    {
        using PooledLease<Payload, Policy> lease = _pool.RentScoped(out Payload item);
        item.Value = depth;
        return depth == 1 ? item.Value : RentObjects(depth - 1) + item.Value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int RentLists(int depth)
    {
        using ListPool<int>.Lease lease = _lists.RentScoped(out List<int> item);
        item.Add(depth);
        return depth == 1 ? item[0] : RentLists(depth - 1) + item[0];
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    public sealed class Payload
    {
        public int Value;
    }

    public readonly struct Policy : IPooledObjectPolicy<Payload>, INonThrowingResetPolicy
    {
        public Payload Create() => new();
        public bool TryReset(Payload obj)
        {
            obj.Value = 0;
            return true;
        }
    }
}
