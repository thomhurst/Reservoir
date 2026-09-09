using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

/// <summary>
/// Measures bounded request/completion handoffs with repeatable home or distant return affinity.
/// Unlike naturally assigned thread ordinals, these placements remain comparable between builds.
/// </summary>
[BenchmarkCategory("Storage", "Contention")]
[MemoryDiagnoser]
public class SmallPoolRemoteHandoffBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private BenchmarkWorkerGroup? _workers;
    private ObjectPool<Payload, PayloadPolicy>? _pool;
    private long _workerAllocatedBytes;
    private long _maximumWorkerAllocatedBytes;

    [Params(32, 64)]
    public int Capacity { get; set; }

    [Params(1)]
    public int PairCount { get; set; }

    [Params(ReturnAffinity.Home, ReturnAffinity.Distant)]
    public ReturnAffinity ReturnLocation { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var pool = new ObjectPool<Payload, PayloadPolicy>(Capacity);
        _pool = pool;
        int returnIndex = ReturnLocation == ReturnAffinity.Home ? 0 : Capacity - 1;
        uint returnOrdinal = 0;
        while (pool.GetAffinityIndex(returnOrdinal) != returnIndex)
        {
            returnOrdinal++;
        }
        var retained = new Payload[PairCount * 8];
        for (int i = 0; i < retained.Length; i++)
        {
            retained[i] = pool.Rent();
        }

        foreach (Payload item in retained)
        {
            pool.Return(item);
        }

        var channels = new HandoffCell[PairCount];
        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = new HandoffCell();
        }

        FieldInfo ordinal = pool.GetType()
            .GetField("_threadStripe", BindingFlags.NonPublic | BindingFlags.Static)!;
        var initialized = new bool[PairCount * 2];
        _workers = new BenchmarkWorkerGroup(PairCount * 2, workerIndex =>
        {
            int pair = workerIndex % PairCount;
            bool renter = workerIndex < PairCount;
            if (!initialized[workerIndex])
            {
                // Set affinity once on each dedicated worker, outside the measured calls.
                ordinal.SetValue(null, renter ? 1 : checked((int)returnOrdinal + 1));
                initialized[workerIndex] = true;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            HandoffCell channel = channels[pair];
            int operationCount = OperationsPerInvocation / PairCount;
            if (renter)
            {
                for (int i = 0; i < operationCount; i++)
                {
                    Payload item = pool.Rent();
                    while (Volatile.Read(ref channel.Item) is not null)
                    {
                        Thread.SpinWait(1);
                    }

                    Volatile.Write(ref channel.Item, item);
                }
            }
            else
            {
                for (int i = 0; i < operationCount; i++)
                {
                    Payload? item;
                    while ((item = Volatile.Read(ref channel.Item)) is null)
                    {
                        Thread.SpinWait(1);
                    }

                    Volatile.Write(ref channel.Item, null);
                    pool.Return(item);
                }
            }

            Interlocked.Add(ref _workerAllocatedBytes, GC.GetAllocatedBytesForCurrentThread() - before);
        });

        for (int i = 0; i < 4; i++)
        {
            _workers.Run();
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void Handoff()
    {
        _workerAllocatedBytes = 0;
        _workers!.Run();
        _maximumWorkerAllocatedBytes = Math.Max(_maximumWorkerAllocatedBytes, _workerAllocatedBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"// Worker allocated bytes per invocation: final={_workerAllocatedBytes}, max={_maximumWorkerAllocatedBytes}");
        _workers?.Dispose();
        _pool?.Dispose();
    }

    public enum ReturnAffinity
    {
        Home,
        Distant,
    }

    public sealed class Payload;

    public readonly struct PayloadPolicy : IPooledObjectPolicy<Payload>
    {
        public Payload Create() => new();
        public bool TryReset(Payload obj) => true;
    }

    [StructLayout(LayoutKind.Explicit)]
    private sealed class HandoffCell
    {
        [FieldOffset(64)]
        internal Payload? Item;

#pragma warning disable CS0169 // Keep neighbouring channels off the hot item's cache line.
        [FieldOffset(136)]
        private readonly long _trailingPad;
#pragma warning restore CS0169
    }
}
