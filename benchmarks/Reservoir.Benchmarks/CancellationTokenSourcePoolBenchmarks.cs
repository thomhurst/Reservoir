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
        _pool.Return(source);

        using CancellationTokenSourcePool.Lease lease = _pool.RentScoped();
    }

    [Benchmark(Baseline = true)]
    public bool NewDispose()
    {
        using var source = new CancellationTokenSource();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentReturn()
    {
        CancellationTokenSource source = _pool.Rent();
        bool isCanceled = source.IsCancellationRequested;
        _pool.Return(source);
        return isCanceled;
    }

    [Benchmark]
    public bool ScopedRentReturn()
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
        _pool.Return(source);
    }

    [Benchmark(Baseline = true)]
    public bool NewScheduleDispose()
    {
        using var source = new CancellationTokenSource();
        source.CancelAfter(_timeout);
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentScheduleReturn()
    {
        CancellationTokenSource source = _pool.Rent();
        source.CancelAfter(_timeout);
        bool isCanceled = source.IsCancellationRequested;
        _pool.Return(source);
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
        _pool.Return(source);
    }

    [Benchmark(Baseline = true)]
    public bool NewRegisterDispose()
    {
        using var source = new CancellationTokenSource();
        _ = source.Token.Register(s_callback);
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentRegisterReturn()
    {
        CancellationTokenSource source = _pool.Rent();
        _ = source.Token.Register(s_callback);
        bool isCanceled = source.IsCancellationRequested;
        _pool.Return(source);
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
        _pool.Return(source);
    }

    [Benchmark(Baseline = true)]
    public bool NewCancelDispose()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        return source.IsCancellationRequested;
    }

    [Benchmark]
    public bool RentCancelReturn()
    {
        CancellationTokenSource source = _pool.Rent();
        source.Cancel();
        bool isCanceled = source.IsCancellationRequested;
        _pool.Return(source);
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

    [Params(1, 4, 16, 32)]
    public int WorkerCount { get; set; }

    [GlobalSetup(Target = nameof(NewDispose))]
    public void SetupNewDispose()
    {
        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunNewDispose(operationsPerWorker));
    }

    [GlobalSetup(Target = nameof(RentReturn))]
    public void SetupPool()
    {
        var pool = new CancellationTokenSourcePool(maxCapacity: PoolCapacity);
        var sources = new CancellationTokenSource[PoolCapacity];

        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = pool.Rent();
        }

        foreach (CancellationTokenSource source in sources)
        {
            pool.Return(source);
        }

        int operationsPerWorker = OperationsPerInvocation / WorkerCount;
        _workers = new BenchmarkWorkerGroup(
            WorkerCount,
            () => RunPool(pool, operationsPerWorker));
    }

    [GlobalCleanup]
    public void Cleanup() => _workers?.Dispose();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation, Baseline = true)]
    public void NewDispose() => _workers!.Run();

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public void RentReturn() => _workers!.Run();

    private static void RunNewDispose(int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            new CancellationTokenSource().Dispose();
        }
    }

    private static void RunPool(CancellationTokenSourcePool pool, int operationCount)
    {
        for (int i = 0; i < operationCount; i++)
        {
            CancellationTokenSource source = pool.Rent();
            pool.Return(source);
        }
    }
}
