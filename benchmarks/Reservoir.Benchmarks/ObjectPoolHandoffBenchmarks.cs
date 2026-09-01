using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Measures the cross-thread handoff pattern: producer threads rent, consumer threads return.
/// This is the adversarial workload for the thread-local fast path, whose wins come from
/// same-thread rent/return cycles.
/// </summary>
[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolHandoffBenchmarks
{
    private const int OperationsPerInvocation = 262_144;
    private const int PoolCapacity = 32;

    private BenchmarkWorkerGroup? _workers;
    // MemoryDiagnoser only observes the BenchmarkDotNet thread, so worker-side allocations are
    // aggregated here per invocation and the steady-state value is printed on cleanup.
    private long _workerAllocatedBytes;

    [Params(1, 4)]
    public int PairCount { get; set; }

    [GlobalSetup(Target = nameof(Reservoir))]
    public void SetupReservoir()
        => SetupWorkers(new ObjectPool<Payload, PayloadPolicy>(maxCapacity: PoolCapacity));

    [GlobalSetup(Target = nameof(ReservoirThreadLocalFastPath))]
    public void SetupReservoirThreadLocalFastPath()
        => SetupWorkers(new ObjectPool<Payload, PayloadPolicy>(
            default,
            PoolCapacity,
            threadLocalFastPath: true));

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            "// Worker-thread allocated bytes in final invocation: "
            + Volatile.Read(ref _workerAllocatedBytes));
        _workers?.Dispose();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void Reservoir()
    {
        Volatile.Write(ref _workerAllocatedBytes, 0);
        _workers!.Run();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void ReservoirThreadLocalFastPath()
    {
        Volatile.Write(ref _workerAllocatedBytes, 0);
        _workers!.Run();
    }

    private void SetupWorkers(ObjectPool<Payload, PayloadPolicy> pool)
    {
        for (int i = 0; i < PoolCapacity; i++)
        {
            pool.Return(pool.Rent());
        }

        var channels = new HandoffChannel[PairCount];
        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = new HandoffChannel();
        }

        int handoffsPerPair = OperationsPerInvocation / PairCount;
        _workers = new BenchmarkWorkerGroup(
            PairCount * 2,
            workerIndex =>
            {
                HandoffChannel channel = channels[workerIndex / 2];
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                if ((workerIndex & 1) == 0)
                {
                    RunProducer(pool, channel, handoffsPerPair);
                }
                else
                {
                    RunConsumer(pool, channel, handoffsPerPair);
                }

                Interlocked.Add(
                    ref _workerAllocatedBytes,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            });
    }

    private static void RunProducer(
        ObjectPool<Payload, PayloadPolicy> pool,
        HandoffChannel channel,
        int handoffCount)
    {
        for (int i = 0; i < handoffCount; i++)
        {
            channel.Push(pool.Rent());
        }
    }

    private static void RunConsumer(
        ObjectPool<Payload, PayloadPolicy> pool,
        HandoffChannel channel,
        int handoffCount)
    {
        for (int i = 0; i < handoffCount; i++)
        {
            pool.Return(channel.Pop());
        }
    }

    // A minimal bounded single-producer single-consumer ring so the handoff cost is identical for
    // every benchmarked pool; only the pool behavior differs between methods.
    private sealed class HandoffChannel
    {
        private const int Capacity = 64;
        private const int IndexMask = Capacity - 1;

        private readonly Payload?[] _slots = new Payload?[Capacity];
        private int _head;
        private int _tail;

        internal void Push(Payload item)
        {
            int tail = _tail;
            while (tail - Volatile.Read(ref _head) == Capacity)
            {
                Thread.SpinWait(1);
            }

            _slots[tail & IndexMask] = item;
            Volatile.Write(ref _tail, tail + 1);
        }

        internal Payload Pop()
        {
            int head = _head;
            while (Volatile.Read(ref _tail) == head)
            {
                Thread.SpinWait(1);
            }

            Payload item = _slots[head & IndexMask]!;
            _slots[head & IndexMask] = null;
            Volatile.Write(ref _head, head + 1);
            return item;
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
    }
}
