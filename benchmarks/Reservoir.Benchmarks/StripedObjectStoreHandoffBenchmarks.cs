using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage", "Contention")]
[MemoryDiagnoser(displayGenColumns: false)]
[MinColumn]
[MaxColumn]
[MedianColumn]
public class StripedObjectStoreHandoffBenchmarks
{
    private const int OperationsPerInvocation = 327_680;

    private BenchmarkWorkerGroup? _workers;

    [Params(65, 4_096, 65_536)]
    public int Capacity { get; set; }

    [ParamsSource(nameof(PairCounts))]
    public int PairCount { get; set; }

    // A spinning handoff needs both threads of every pair on a core, so pair counts are capped at
    // half the processors; beyond that the benchmark measures the scheduler, not the store.
    public IEnumerable<int> PairCounts => new[]
    {
        1,
        4,
        8,
        Math.Max(1, Environment.ProcessorCount / 2),
    }.Where(pairCount => pairCount <= Math.Max(1, Environment.ProcessorCount / 2)).Distinct();

    [GlobalSetup]
    public void Setup()
    {
        var store = new StripedObjectStore<Payload>(Capacity);
        var handoffs = new Handoff[PairCount];

        for (int i = 0; i < PairCount; i++)
        {
            handoffs[i] = new Handoff();

            if (!store.TryPush(new Payload()))
            {
                throw new InvalidOperationException("Failed to populate striped store.");
            }
        }

        _workers = new BenchmarkWorkerGroup(
            PairCount * 2,
            workerIndex =>
            {
                int pairIndex = workerIndex % PairCount;
                int operationCount = GetOperationCount(pairIndex);

                if (workerIndex < PairCount)
                {
                    Rent(store, handoffs[pairIndex], operationCount);
                }
                else
                {
                    Return(store, handoffs[pairIndex], operationCount);
                }
            });
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void CrossThreadHandoff() => _workers!.Run();

    private static void Rent(
        StripedObjectStore<Payload> store,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 0)
            {
                Thread.SpinWait(1);
            }

            Payload? item;
            while (!store.TryPop(out item))
            {
                Thread.SpinWait(1);
            }

            handoff.Item = item;
            Volatile.Write(ref handoff.State, 1);
        }
    }

    private static void Return(
        StripedObjectStore<Payload> store,
        Handoff handoff,
        int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            while (Volatile.Read(ref handoff.State) != 1)
            {
                Thread.SpinWait(1);
            }

            Payload item = handoff.Item!;
            handoff.Item = null;

            while (!store.TryPush(item))
            {
                Thread.SpinWait(1);
            }

            Volatile.Write(ref handoff.State, 0);
        }
    }

    private int GetOperationCount(int pairIndex)
    {
        int baseCount = OperationsPerInvocation / PairCount;
        return baseCount + (pairIndex < OperationsPerInvocation % PairCount ? 1 : 0);
    }

    public sealed class Payload;

    // Size alone is ignored for classes holding references, so explicit offsets keep each pair's
    // handoff cell on its own cache lines.
    [StructLayout(LayoutKind.Explicit)]
    private sealed class Handoff
    {
        [FieldOffset(64)]
        internal Payload? Item;

        [FieldOffset(72)]
        internal int State;

#pragma warning disable CS0169 // The field is only there to occupy space.
        [FieldOffset(136)]
        private readonly long _trailingPad;
#pragma warning restore CS0169
    }
}
