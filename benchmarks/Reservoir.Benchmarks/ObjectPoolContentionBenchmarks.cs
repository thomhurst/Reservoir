using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.ObjectPool;

namespace Reservoir.Benchmarks;

[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;

    private BenchmarkWorkerGroup? _workers;

    [Params(1, 4, 8, 16, 32)]
    public int WorkerCount { get; set; }

    [Params(32, 256)]
    public int Capacity { get; set; }

    [GlobalSetup(Target = nameof(Reservoir))]
    public void SetupReservoir()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(maxCapacity: Capacity);
        WarmReservoirPool(pool, Capacity);
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunReservoir(pool, operationsPerWorker));
    }

    [GlobalSetup(Target = nameof(ReservoirThreadLocalFastPath))]
    public void SetupReservoirThreadLocalFastPath()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(
            default,
            Capacity,
            threadLocalFastPath: true);
        WarmReservoirPool(pool, Capacity);
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunReservoir(pool, operationsPerWorker));
    }

    [GlobalSetup(Target = nameof(MicrosoftExtensionsObjectPool))]
    public void SetupMicrosoftPool()
    {
        var pool = new DefaultObjectPool<Payload>(
            new DefaultPooledObjectPolicy<Payload>(),
            Capacity);
        WarmMicrosoftPool(pool, Capacity);
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunMicrosoftPool(pool, operationsPerWorker));
    }

    [GlobalSetup(Target = nameof(ConcurrentBag))]
    public void SetupConcurrentBag()
    {
        var bag = new ConcurrentBag<Payload>();
        for (int i = 0; i < Capacity; i++)
        {
            bag.Add(new Payload());
        }

        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunConcurrentBag(bag, operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void Reservoir() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ReservoirThreadLocalFastPath() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void MicrosoftExtensionsObjectPool() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ConcurrentBag() => _workers!.Run();

    private static void WarmReservoirPool(ObjectPool<Payload, PayloadPolicy> pool, int capacity)
    {
        var items = new Payload[capacity];

        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Rent();
        }

        foreach (Payload item in items)
        {
            pool.Return(item);
        }
    }

    private static void WarmMicrosoftPool(DefaultObjectPool<Payload> pool, int capacity)
    {
        var items = new Payload[capacity];

        for (int i = 0; i < items.Length; i++)
        {
            items[i] = pool.Get();
        }

        foreach (Payload item in items)
        {
            pool.Return(item);
        }
    }

    private static void RunReservoir(
        ObjectPool<Payload, PayloadPolicy> pool,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            Payload item = pool.Rent();
            pool.Return(item);
        }
    }

    private static void RunMicrosoftPool(
        DefaultObjectPool<Payload> pool,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            Payload item = pool.Get();
            pool.Return(item);
        }
    }

    private static void RunConcurrentBag(
        ConcurrentBag<Payload> bag,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            if (!bag.TryTake(out Payload? item))
            {
                item = new Payload();
            }

            bag.Add(item);
        }
    }

    public sealed class Payload
    {
        public byte[] Buffer { get; } = GC.AllocateUninitializedArray<byte>(256);
    }

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;

        public void Destroy(Payload obj)
        {
        }
    }
}
