using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Measures a bounded cross-thread handoff: the producer rents, the consumer returns, and a
/// shallow channel keeps only a few objects in flight, so the pool is never empty and every
/// operation measures the shared tier's slot search rather than creation churn. This is the
/// request/response shape (rent on the caller, return on the completion thread) where the renting
/// thread's home slot is always empty and the returned objects sit at the returning thread's home.
/// </summary>
[MemoryDiagnoser(displayGenColumns: false)]
public class ObjectPoolSparseHandoffBenchmarks
{
    private const int OperationsPerInvocation = 262_144;
    private const int RetainedObjects = 8;

    private BenchmarkWorkerGroup? _workers;
    private long _workerAllocatedBytes;

    // 32 uses the cache-line slot array; 4096 uses the striped store.
    [Params(32, 4096)]
    public int PoolCapacity { get; set; }

    // Objects in flight are bounded by the channel depth plus one being returned.
    [Params(1, 4)]
    public int ChannelDepth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(
            default,
            PoolCapacity,
            threadLocalFastPath: false);

        var retained = new Payload[RetainedObjects];
        for (int i = 0; i < retained.Length; i++)
        {
            retained[i] = pool.Rent();
        }

        foreach (Payload payload in retained)
        {
            pool.Return(payload);
        }

        var channel = new HandoffChannel(ChannelDepth);
        _workers = new BenchmarkWorkerGroup(
            2,
            workerIndex =>
            {
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                if (workerIndex == 0)
                {
                    for (int i = 0; i < OperationsPerInvocation; i++)
                    {
                        channel.Push(pool.Rent());
                    }
                }
                else
                {
                    for (int i = 0; i < OperationsPerInvocation; i++)
                    {
                        pool.Return(channel.Pop());
                    }
                }

                Interlocked.Add(
                    ref _workerAllocatedBytes,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine(
            "// Worker-thread allocated bytes in final invocation: "
            + Volatile.Read(ref _workerAllocatedBytes));
        _workers?.Dispose();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentOnProducerReturnOnConsumer()
    {
        Volatile.Write(ref _workerAllocatedBytes, 0);
        _workers!.Run();
    }

    // A minimal bounded single-producer single-consumer ring; only the pool behavior differs
    // between the compared library builds.
    private sealed class HandoffChannel
    {
        private readonly Payload?[] _slots;
        private readonly int _indexMask;
        private int _head;
        private int _tail;

        internal HandoffChannel(int capacity)
        {
            _slots = new Payload?[capacity];
            _indexMask = capacity - 1;
        }

        internal void Push(Payload item)
        {
            int tail = _tail;
            while (tail - Volatile.Read(ref _head) == _slots.Length)
            {
                Thread.SpinWait(1);
            }

            _slots[tail & _indexMask] = item;
            Volatile.Write(ref _tail, tail + 1);
        }

        internal Payload Pop()
        {
            int head = _head;
            while (Volatile.Read(ref _tail) == head)
            {
                Thread.SpinWait(1);
            }

            Payload item = _slots[head & _indexMask]!;
            _slots[head & _indexMask] = null;
            Volatile.Write(ref _head, head + 1);
            return item;
        }
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();

        public bool TryReset(Payload obj) => true;
    }
}
