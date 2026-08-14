using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[GenericTypeArguments(typeof(GenericThreadLocalTrivialPolicy))]
[GenericTypeArguments(typeof(GenericThreadLocalResetPolicy))]
[MemoryDiagnoser(displayGenColumns: false)]
public class GenericThreadLocalObjectPoolBenchmarks<TPolicy>
    where TPolicy : struct, IPooledObjectPolicy<GenericThreadLocalPayload>
{
    [ThreadStatic]
    private static GenericThreadLocalPayload? _threadStaticItem;

    private ObjectPool<GenericThreadLocalPayload, TPolicy>? _pool;
    private TPolicy _policy;

    [Params(1, 32, 64, 256)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _policy = default;
        _pool = new ObjectPool<GenericThreadLocalPayload, TPolicy>(Capacity);
        _pool.Return(_pool.Rent());
        _pool.ReturnThreadLocal(_pool.RentThreadLocal());
        _threadStaticItem = _policy.Create();
    }

    [Benchmark(Baseline = true)]
    public GenericThreadLocalPayload ThreadStaticCache()
    {
        GenericThreadLocalPayload item = _threadStaticItem ?? _policy.Create();
        _threadStaticItem = null;
        item.Value = 1;
        if (!_policy.TryReset(item))
        {
            throw new InvalidOperationException("Benchmark policy rejected an item.");
        }

        _threadStaticItem = item;
        return item;
    }

    [Benchmark]
    public GenericThreadLocalPayload Shared()
    {
        GenericThreadLocalPayload item = _pool!.Rent();
        item.Value = 1;
        _pool.Return(item);
        return item;
    }

    [Benchmark]
    public GenericThreadLocalPayload ThreadLocal()
    {
        GenericThreadLocalPayload item = _pool!.RentThreadLocal();
        item.Value = 1;
        _pool.ReturnThreadLocal(item);
        return item;
    }

    [Benchmark]
    public GenericThreadLocalPayload Scoped()
    {
        using PooledLease<GenericThreadLocalPayload, TPolicy> lease
            = _pool!.RentScoped(out GenericThreadLocalPayload item);
        item.Value = 1;
        return item;
    }
}

[MemoryDiagnoser(displayGenColumns: false)]
public class GenericThreadLocalObjectPoolHandoffBenchmarks
{
    private const int OperationsPerInvocation = 65_536;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 32, 64, 256)]
    public int Capacity { get; set; }

    [Params(1, 8)]
    public int PairCount { get; set; }

    [GlobalSetup(Target = nameof(Shared))]
    public void SetupShared()
    {
        var pool = CreatePool(out Handoff[] handoffs);
        _workers = new BenchmarkWorkerGroup(
            PairCount * 2,
            workerIndex =>
            {
                int pairIndex = workerIndex % PairCount;
                int operationCount = GetOperationCount(pairIndex);

                if (workerIndex < PairCount)
                {
                    RentShared(pool, handoffs[pairIndex], operationCount);
                }
                else
                {
                    ReturnShared(pool, handoffs[pairIndex], operationCount);
                }
            });
    }

    [GlobalSetup(Target = nameof(ThreadLocal))]
    public void SetupThreadLocal()
    {
        var pool = CreatePool(out Handoff[] handoffs);
        _workers = new BenchmarkWorkerGroup(
            PairCount * 2,
            workerIndex =>
            {
                int pairIndex = workerIndex % PairCount;
                int operationCount = GetOperationCount(pairIndex);

                if (workerIndex < PairCount)
                {
                    RentThreadLocal(pool, handoffs[pairIndex], operationCount);
                }
                else
                {
                    ReturnThreadLocal(pool, handoffs[pairIndex], operationCount);
                }
            });
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void Shared() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ThreadLocal() => _workers!.Run();

    private ObjectPool<GenericThreadLocalPayload, GenericThreadLocalTrivialPolicy> CreatePool(
        out Handoff[] handoffs)
    {
        var pool = new ObjectPool<
            GenericThreadLocalPayload,
            GenericThreadLocalTrivialPolicy>(Capacity);
        handoffs = new Handoff[PairCount];

        for (int i = 0; i < handoffs.Length; i++)
        {
            handoffs[i] = new Handoff();
        }

        return pool;
    }

    private static void RentShared(
        ObjectPool<GenericThreadLocalPayload, GenericThreadLocalTrivialPolicy> pool,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 0)
            {
                Thread.SpinWait(1);
            }

            handoff.Item = pool.Rent();
            Volatile.Write(ref handoff.State, 1);
        }
    }

    private static void ReturnShared(
        ObjectPool<GenericThreadLocalPayload, GenericThreadLocalTrivialPolicy> pool,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 1)
            {
                Thread.SpinWait(1);
            }

            GenericThreadLocalPayload item = handoff.Item!;
            handoff.Item = null;
            pool.Return(item);
            Volatile.Write(ref handoff.State, 0);
        }
    }

    private static void RentThreadLocal(
        ObjectPool<GenericThreadLocalPayload, GenericThreadLocalTrivialPolicy> pool,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 0)
            {
                Thread.SpinWait(1);
            }

            handoff.Item = pool.RentThreadLocal();
            Volatile.Write(ref handoff.State, 1);
        }
    }

    private static void ReturnThreadLocal(
        ObjectPool<GenericThreadLocalPayload, GenericThreadLocalTrivialPolicy> pool,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 1)
            {
                Thread.SpinWait(1);
            }

            GenericThreadLocalPayload item = handoff.Item!;
            handoff.Item = null;
            pool.ReturnThreadLocal(item);
            Volatile.Write(ref handoff.State, 0);
        }
    }

    private int GetOperationCount(int pairIndex)
    {
        int baseCount = OperationsPerInvocation / PairCount;
        return baseCount + (pairIndex < OperationsPerInvocation % PairCount ? 1 : 0);
    }

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private sealed class Handoff
    {
        internal GenericThreadLocalPayload? Item;
        internal int State;
    }
}

public sealed class GenericThreadLocalPayload
{
    internal int Value;
}

public readonly struct GenericThreadLocalTrivialPolicy
    : IPooledObjectPolicy<GenericThreadLocalPayload>
{
    public GenericThreadLocalPayload Create() => new();

    public bool TryReset(GenericThreadLocalPayload obj) => true;
}

public readonly struct GenericThreadLocalResetPolicy
    : IPooledObjectPolicy<GenericThreadLocalPayload>
{
    public GenericThreadLocalPayload Create() => new();

    public bool TryReset(GenericThreadLocalPayload obj)
    {
        obj.Value = 0;
        return true;
    }
}
