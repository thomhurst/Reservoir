namespace Reservoir.Tests;

public class ValueTaskSourcePoolTests
{
    [Test]
    public async Task GenericSynchronousCompletionReturnsSourceToPool()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        PooledValueTaskSource<int> expected = pool.Rent();
        ValueTask<int> operation = new(expected, expected.Version);

        expected.SetResult(42);
        int result = await operation;

        PooledValueTaskSource<int> actual = pool.Rent();
        await Assert.That(result).IsEqualTo(42);
        await Assert.That(actual).IsSameReferenceAs(expected);

        actual.SetResult(0);
        await actual.CreateValueTask();
    }

    [Test]
    public async Task NonGenericSynchronousCompletionReturnsSourceToPool()
    {
        var pool = new ValueTaskSourcePool(maxCapacity: 1);
        PooledValueTaskSource expected = pool.Rent();
        ValueTask operation = expected.CreateValueTask();

        expected.SetResult();
        await operation;

        PooledValueTaskSource actual = pool.Rent();
        await Assert.That(actual).IsSameReferenceAs(expected);

        actual.SetResult();
        await actual.CreateValueTask();
    }

    [Test]
    public async Task AsynchronousCompletionResumesAwaiterAndReturnsSource()
    {
        var pool = new ValueTaskSourcePool<int>(
            maxCapacity: 1,
            runContinuationsAsynchronously: true);
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();
        Task<int> consumer = ConsumeAsync(operation);

        await Task.Yield();
        await Assert.That(consumer.IsCompleted).IsFalse();

        source.SetResult(42);
        int result = await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        PooledValueTaskSource<int> reused = pool.Rent();

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(reused).IsSameReferenceAs(source);
        await Assert.That(pool.RunContinuationsAsynchronously).IsTrue();

        reused.SetResult(0);
        await reused.CreateValueTask();
    }

    [Test]
    public async Task ExceptionCompletionReturnsSourceToPool()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();
        var expected = new InvalidOperationException("Expected failure.");

        source.SetException(expected);

        await Assert.That(async () => await operation).Throws<InvalidOperationException>();

        PooledValueTaskSource<int> reused = pool.Rent();
        await Assert.That(reused).IsSameReferenceAs(source);
        reused.SetResult(0);
        await reused.CreateValueTask();
    }

    [Test]
    public async Task StaleValueTaskThrowsWithoutCorruptingReusedSource()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> stale = source.CreateValueTask();
        source.SetResult(1);
        _ = await stale;

        PooledValueTaskSource<int> reused = pool.Rent();
        ValueTask<int> current = reused.CreateValueTask();
        reused.SetResult(2);

        await Assert.That(async () => await stale).Throws<InvalidOperationException>();
        await Assert.That(await current).IsEqualTo(2);
    }

    [Test]
    public async Task ConcurrentConsumersAllowExactlyOneConsumption()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();
        source.SetResult(42);
        using var start = new Barrier(3);

        Task<(bool Success, int Value)> first = Task.Run(() => Consume(operation, start));
        Task<(bool Success, int Value)> second = Task.Run(() => Consume(operation, start));
        start.SignalAndWait();
        (bool Success, int Value)[] results = await Task.WhenAll(first, second);

        await Assert.That(results.Count(result => result.Success)).IsEqualTo(1);
        await Assert.That(results.Single(result => result.Success).Value).IsEqualTo(42);

        PooledValueTaskSource<int> reused = pool.Rent();
        await Assert.That(reused).IsSameReferenceAs(source);
        reused.SetResult(0);
        await reused.CreateValueTask();
    }

    [Test]
    public async Task PendingResultAccessThrowsWithoutReturningSource()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();

        await Assert.That(() => operation.GetAwaiter().GetResult())
            .Throws<InvalidOperationException>();

        source.SetResult(42);
        await Assert.That(await operation).IsEqualTo(42);
    }

    [Test]
    public async Task ConcurrentStressPreservesResultsAndOwnership()
    {
        const int workerCount = 8;
        const int iterations = 1_000;
        var pool = new ValueTaskSourcePool<int>(
            maxCapacity: workerCount,
            runContinuationsAsynchronously: true);

        Task[] workers = Enumerable.Range(0, workerCount)
            .Select(worker => StressPool(pool, worker, iterations))
            .ToArray();

        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));
    }

#if !DEBUG && !RESERVOIR_DIAGNOSTICS
    [Test]
    public async Task WarmProduceAndConsumeAllocatesNothing()
    {
        var pool = new ValueTaskSourcePool<int>(maxCapacity: 1);
        CompleteSynchronously(pool, 0);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1_000; i++)
        {
            CompleteSynchronously(pool, i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0);
    }
#endif

    [Test]
    public async Task InvalidCapacityThrows()
    {
        await Assert.That(() => new ValueTaskSourcePool(0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ValueTaskSourcePool<int>(0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ContinuationConfigurationUsesDefaultCapacity()
    {
        using var pool = new ValueTaskSourcePool<int>(runContinuationsAsynchronously: true);

        await Assert.That(pool.RunContinuationsAsynchronously).IsTrue();
        await Assert.That(pool.MaximumRetained).IsGreaterThanOrEqualTo(32);
    }

    private static async Task<int> ConsumeAsync(ValueTask<int> operation) => await operation;

    private static (bool Success, int Value) Consume(ValueTask<int> operation, Barrier start)
    {
        start.SignalAndWait();

        try
        {
            return (true, operation.GetAwaiter().GetResult());
        }
        catch (InvalidOperationException)
        {
            return (false, 0);
        }
    }

    private static async Task StressPool(
        ValueTaskSourcePool<int> pool,
        int worker,
        int iterations)
    {
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int expected = (worker * iterations) + iteration;
            PooledValueTaskSource<int> source = pool.Rent();
            ValueTask<int> operation = source.CreateValueTask();

            if ((iteration & 1) == 0)
            {
                source.SetResult(expected);
                int actual = await operation;
                ValidateResult(expected, actual);
            }
            else
            {
                Task<int> consumer = ConsumeAsync(operation);
                await Task.Yield();
                source.SetResult(expected);
                int actual = await consumer;
                ValidateResult(expected, actual);
            }
        }
    }

    private static void ValidateResult(int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected result {expected}, but received {actual}.");
        }
    }

    private static int CompleteSynchronously(ValueTaskSourcePool<int> pool, int result)
    {
        PooledValueTaskSource<int> source = pool.Rent();
        ValueTask<int> operation = source.CreateValueTask();
        source.SetResult(result);
        return operation.GetAwaiter().GetResult();
    }
}
