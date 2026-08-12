using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
[AllStatisticsColumn]
public class SmallPoolReturnBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private const int PoolCapacity = 32;

    private BenchmarkWorkerGroup? _workers;
    private int _checksum;

    [Params(SmallPoolPopulation.Empty, SmallPoolPopulation.HalfFull, SmallPoolPopulation.Full)]
    public SmallPoolPopulation InitialPopulation { get; set; }

    [Params(Workload.SameThreadReuse, Workload.CrossThreadHandoff)]
    public Workload Scenario { get; set; }

    [GlobalSetup(Target = nameof(Displacing))]
    public void SetupDisplacing() => Setup(ReturnStrategy.Displacing);

    [GlobalSetup(Target = nameof(NonDisplacing))]
    public void SetupNonDisplacing() => Setup(ReturnStrategy.NonDisplacing);

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public int Displacing()
    {
        _workers!.Run();
        return Volatile.Read(ref _checksum);
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int NonDisplacing()
    {
        _workers!.Run();
        return Volatile.Read(ref _checksum);
    }

    private void Setup(ReturnStrategy strategy)
    {
        int workerCount = Scenario == Workload.SameThreadReuse ? 1 : 2;
        int operationsPerWorker = OperationsPerInvocation / workerCount;
        var store = new SmallReturnStore(PoolCapacity, InitialPopulation);

        _workers = new BenchmarkWorkerGroup(
            workerCount,
            workerIndex =>
            {
                int returnIndex = store.GetStartIndex(workerIndex);
                int takeIndex = Scenario == Workload.SameThreadReuse
                    ? returnIndex
                    : store.GetStartIndex((workerIndex + 1) % workerCount);
                var item = new ReturnPayload();

                Volatile.Write(
                    ref _checksum,
                    store.Run(
                        strategy,
                        item,
                        returnIndex,
                        takeIndex,
                        operationsPerWorker));
            });
    }

    public enum Workload
    {
        SameThreadReuse,
        CrossThreadHandoff,
    }
}

[BenchmarkCategory("Storage")]
[MemoryDiagnoser(displayGenColumns: false)]
[AllStatisticsColumn]
public class SmallPoolReturnContentionBenchmarks
{
    private const int OperationsPerInvocation = 65_536;
    private const int PoolCapacity = 32;

    private BenchmarkWorkerGroup? _workers;
    private int _checksum;

    [Params(SmallPoolPopulation.Empty, SmallPoolPopulation.HalfFull, SmallPoolPopulation.Full)]
    public SmallPoolPopulation InitialPopulation { get; set; }

    [Params(1, 4, 8, 16, 32)]
    public int WorkerCount { get; set; }

    [GlobalSetup(Target = nameof(Displacing))]
    public void SetupDisplacing() => Setup(ReturnStrategy.Displacing);

    [GlobalSetup(Target = nameof(NonDisplacing))]
    public void SetupNonDisplacing() => Setup(ReturnStrategy.NonDisplacing);

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public int Displacing()
    {
        _workers!.Run();
        return Volatile.Read(ref _checksum);
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public int NonDisplacing()
    {
        _workers!.Run();
        return Volatile.Read(ref _checksum);
    }

    private void Setup(ReturnStrategy strategy)
    {
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        var store = new SmallReturnStore(PoolCapacity, InitialPopulation);

        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            workerIndex =>
            {
                int startIndex = store.GetStartIndex(workerIndex);
                var item = new ReturnPayload();

                Volatile.Write(
                    ref _checksum,
                    store.Run(
                        strategy,
                        item,
                        startIndex,
                        startIndex,
                        operationsPerWorker));
            });
    }
}

public enum SmallPoolPopulation
{
    Empty,
    HalfFull,
    Full,
}

internal enum ReturnStrategy
{
    Displacing,
    NonDisplacing,
}

internal sealed class ReturnPayload;

internal sealed class SmallReturnStore
{
    private const int CacheLineSlotStride = 8;
    private const uint StripeHashMultiplier = 2_654_435_769u;

    private readonly ReturnPayload?[] _items;
    private readonly int _capacity;
    private readonly int _indexMask;
    private readonly bool _isFull;

    internal SmallReturnStore(int capacity, SmallPoolPopulation population)
    {
        _capacity = capacity;
        _indexMask = (capacity & (capacity - 1)) == 0 ? capacity - 1 : -1;
        _items = new ReturnPayload[capacity * CacheLineSlotStride];

        int itemCount = population switch
        {
            SmallPoolPopulation.Empty => 0,
            SmallPoolPopulation.HalfFull => Math.Max(1, capacity / 2),
            SmallPoolPopulation.Full => capacity,
            _ => throw new ArgumentOutOfRangeException(nameof(population)),
        };

        _isFull = itemCount == capacity;
        for (int i = 0; i < itemCount; i++)
        {
            GetSlot(i) = new ReturnPayload();
        }
    }

    internal int GetStartIndex(int workerIndex)
    {
        uint mixedIndex = unchecked((uint)workerIndex) * StripeHashMultiplier;
        return _indexMask >= 0
            ? (int)(mixedIndex & (uint)_indexMask)
            : (int)(((ulong)mixedIndex * (uint)_capacity) >> 32);
    }

    internal int Run(
        ReturnStrategy strategy,
        ReturnPayload initialItem,
        int returnIndex,
        int takeIndex,
        int operationCount)
    {
        ReturnPayload item = initialItem;
        int sameItemCount = 0;

        for (int i = 0; i < operationCount; i++)
        {
            bool retained = strategy == ReturnStrategy.Displacing
                ? ReturnDisplacing(item, returnIndex)
                : ReturnNonDisplacing(item, returnIndex);

            if (_isFull)
            {
                if (retained)
                {
                    throw new InvalidOperationException(
                        "A full store retained the newly returned item.");
                }

                continue;
            }

            if (!retained)
            {
                continue;
            }

            ReturnPayload? rented;
            do
            {
                rented = Take(takeIndex);
            }
            while (rented is null);

            if (ReferenceEquals(rented, item))
            {
                sameItemCount++;
            }

            item = rented;
        }

        return sameItemCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReturnDisplacing(ReturnPayload returned, int startIndex)
    {
        ReturnPayload? displaced = Interlocked.Exchange(ref GetSlot(startIndex), returned);
        if (displaced is null)
        {
            return true;
        }

        return ReturnDisplacingSlow(returned, displaced, startIndex);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ReturnDisplacingSlow(
        ReturnPayload returned,
        ReturnPayload displaced,
        int startIndex)
    {
        for (int offset = 1; offset < _capacity; offset++)
        {
            int index = startIndex + offset;
            if (index >= _capacity)
            {
                index -= _capacity;
            }

            if (Interlocked.CompareExchange(ref GetSlot(index), displaced, null) is null)
            {
                return true;
            }
        }

        ReturnPayload? observed = Interlocked.CompareExchange(
            ref GetSlot(startIndex),
            displaced,
            returned);
        return !ReferenceEquals(observed, returned);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReturnNonDisplacing(ReturnPayload returned, int startIndex)
    {
        if (Interlocked.CompareExchange(ref GetSlot(startIndex), returned, null) is null)
        {
            return true;
        }

        return ReturnNonDisplacingSlow(returned, startIndex);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool ReturnNonDisplacingSlow(ReturnPayload returned, int startIndex)
    {
        for (int offset = 1; offset < _capacity; offset++)
        {
            int index = startIndex + offset;
            if (index >= _capacity)
            {
                index -= _capacity;
            }

            if (Interlocked.CompareExchange(ref GetSlot(index), returned, null) is null)
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReturnPayload? Take(int startIndex)
    {
        ref ReturnPayload? startSlot = ref GetSlot(startIndex);
        ReturnPayload? observed = Volatile.Read(ref startSlot);
        ReturnPayload? item = observed is not null
            && ReferenceEquals(
                Interlocked.CompareExchange(ref startSlot, null, observed),
                observed)
            ? observed
            : null;

        if (item is not null)
        {
            return item;
        }

        for (int offset = 1; offset < _capacity; offset++)
        {
            int index = startIndex + offset;
            if (index >= _capacity)
            {
                index -= _capacity;
            }

            ref ReturnPayload? slot = ref GetSlot(index);
            observed = Volatile.Read(ref slot);
            if (observed is not null
                && ReferenceEquals(
                    Interlocked.CompareExchange(ref slot, null, observed),
                    observed))
            {
                return observed;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ReturnPayload? GetSlot(int index)
        => ref _items[index * CacheLineSlotStride];
}
