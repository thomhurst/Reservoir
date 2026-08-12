using BenchmarkDotNet.Attributes;

namespace Reservoir.Benchmarks;

[BenchmarkCategory("CancellationTokenSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourcePoolBenchmarks
{
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.Rent();
        source.Dispose();

        using CancellationTokenSourcePool.Lease lease = _pool.RentScoped();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public bool NewDispose()
    {
        using var source = new CancellationTokenSource();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentDispose()
    {
        CancellationTokenSource source = _pool.Rent();
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }

    [Benchmark]
    public bool ScopedRentDispose()
    {
        using CancellationTokenSourcePool.Lease lease = _pool.RentScoped();
        return lease.Value.IsCancellationRequested;
    }
}

[BenchmarkCategory("CancellationTokenSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourceTimerBenchmarks
{
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.Rent();
        source.CancelAfter(_timeout);
        source.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public bool NewScheduleDispose()
    {
        using var source = new CancellationTokenSource();
        source.CancelAfter(_timeout);
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentScheduleDispose()
    {
        CancellationTokenSource source = _pool.Rent();
        source.CancelAfter(_timeout);
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }
}

[BenchmarkCategory("CancellationTokenSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourceRegistrationBenchmarks
{
    private static readonly Action s_callback = static () => { };
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.Rent();
        _ = source.Token.Register(s_callback);
        source.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public bool NewRegisterDispose()
    {
        using var source = new CancellationTokenSource();
        _ = source.Token.Register(s_callback);
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentRegisterDispose()
    {
        CancellationTokenSource source = _pool.Rent();
        _ = source.Token.Register(s_callback);
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }
}

[BenchmarkCategory("CancellationTokenSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourceCanceledBenchmarks
{
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.Rent();
        source.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public bool NewCancelDispose()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentCancelDispose()
    {
        CancellationTokenSource source = _pool.Rent();
        source.Cancel();
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }
}

[BenchmarkCategory("CancellationTokenSource", "Linked")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourceLinkedBenchmarks
{
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);
    private readonly CancellationTokenSource _upstream = new();

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.RentLinked(_upstream.Token);
        source.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _upstream.Dispose();
        _pool.Dispose();
    }

    [Benchmark(Baseline = true)]
    public bool NewDispose()
    {
        using var source = new CancellationTokenSource();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool CreateLinkedDispose()
    {
        using CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(_upstream.Token);
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentConsumerLinkedDispose()
    {
        CancellationTokenSource source = _pool.Rent();
        CancellationTokenRegistration registration = _upstream.Token.UnsafeRegister(
            static state => ((CancellationTokenSource)state!).Cancel(),
            source);
        bool isCanceled = source.IsCancellationRequested;
        registration.Dispose();
        source.Dispose();
        return isCanceled;
    }

    [Benchmark]
    public bool RentLinkedDispose()
    {
        CancellationTokenSource source = _pool.RentLinked(_upstream.Token);
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }
}

[BenchmarkCategory("CancellationTokenSource", "Linked", "Canceled")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourceLinkedCanceledBenchmarks
{
    private readonly CancellationTokenSourcePool _pool = new(maxCapacity: 1);

    [GlobalSetup]
    public void WarmPool()
    {
        CancellationTokenSource source = _pool.Rent();
        source.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup() => _pool.Dispose();

    [Benchmark(Baseline = true)]
    public bool CreateLinkedCancelDispose()
    {
        using var upstream = new CancellationTokenSource();
        using CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(upstream.Token);
        upstream.Cancel();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentConsumerLinkedCancelDispose()
    {
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource source = _pool.Rent();
        CancellationTokenRegistration registration = upstream.Token.UnsafeRegister(
            static state => ((CancellationTokenSource)state!).Cancel(),
            source);
        upstream.Cancel();
        bool isCanceled = source.IsCancellationRequested;
        registration.Dispose();
        source.Dispose();
        return isCanceled;
    }

    [Benchmark]
    public bool RentLinkedCancelDispose()
    {
        using var upstream = new CancellationTokenSource();
        CancellationTokenSource source = _pool.RentLinked(upstream.Token);
        upstream.Cancel();
        bool isCanceled = source.IsCancellationRequested;
        source.Dispose();
        return isCanceled;
    }
}

[BenchmarkCategory("CancellationTokenSource")]
[MemoryDiagnoser(displayGenColumns: false)]
public class CancellationTokenSourcePoolContentionBenchmarks
{
    private const int OperationsPerInvocation = 327_680;
    private const int PoolCapacity = 32;

    private BenchmarkWorkerGroup? _workers;
    private CancellationTokenSourcePool? _pool;

    [Params(1, 4, 8, 16, 32)]
    public int WorkerCount { get; set; }

    [GlobalSetup(Target = nameof(NewDispose))]
    public void SetupNewDispose()
    {
        int operationsPerWorker = GetOperationsPerWorker();
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunNewDispose(operationsPerWorker));
    }

    [GlobalSetup(Target = nameof(RentDispose))]
    public void SetupPool()
    {
        var pool = _pool = new CancellationTokenSourcePool(maxCapacity: PoolCapacity);
        var sources = new CancellationTokenSource[PoolCapacity];

        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = pool.Rent();
        }

        foreach (CancellationTokenSource source in sources)
        {
            source.Dispose();
        }

        int operationsPerWorker = GetOperationsPerWorker();
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunPool(pool, operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _workers?.Dispose();
        _pool?.Dispose();
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void NewDispose() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentDispose() => _workers!.Run();

    private static void RunNewDispose(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            new CancellationTokenSource().Dispose();
        }
    }

    private int GetOperationsPerWorker()
    {
        if (OperationsPerInvocation % WorkerCount != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(OperationsPerInvocation)} must be divisible by {nameof(WorkerCount)}.");
        }

        return OperationsPerInvocation / WorkerCount;
    }

    private static void RunPool(CancellationTokenSourcePool pool, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            CancellationTokenSource source = pool.Rent();
            source.Dispose();
        }
    }
}
