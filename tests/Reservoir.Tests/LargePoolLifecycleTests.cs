using System.Collections.Concurrent;

namespace Reservoir.Tests;

public class LargePoolLifecycleTests
{
    [Test]
    [Arguments(65)]
    [Arguments(4096)]
    public async Task ConcurrentClearAndDisposeDestroyEachItemExactlyOnce(int capacity)
    {
        var state = new AuditState();
        using var pool = new ObjectPool<Item, Policy>(new Policy(state), capacity);
        const int workerCount = 4;
        using var start = new Barrier(workerCount + 1);

        Action<CancellationToken>[] workers = Enumerable.Range(0, workerCount)
            .Select<int, Action<CancellationToken>>(_ => token =>
            {
                start.SignalAndWait(token);
                for (int i = 0; i < 20_000; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Item item;
                    try
                    {
                        item = pool.Rent();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (Interlocked.CompareExchange(ref item.State, 1, 0) != 0)
                    {
                        state.Failures.Enqueue("Rented an owned or destroyed item.");
                    }

                    Thread.SpinWait(4);
                    if (Interlocked.Exchange(ref item.State, 0) != 1)
                    {
                        state.Failures.Enqueue("Item was destroyed during use.");
                    }

                    pool.Return(item);
                    Interlocked.Increment(ref state.Returned);
                }
            }).ToArray();

        Action<CancellationToken> clearing = token =>
        {
            start.SignalAndWait(token);
            for (int i = 0; i < 500; i++)
            {
                token.ThrowIfCancellationRequested();
                pool.Clear();
                Thread.Yield();
            }

            // Ensure at least one completed return even if the clearer was scheduled first.
            while (Volatile.Read(ref state.Returned) == 0)
            {
                token.ThrowIfCancellationRequested();
                Thread.Yield();
            }

            pool.Dispose();
        };

        await ConcurrentTestWorkers.RunAsync(workers.Append(clearing));

        await Assert.That(state.Failures).IsEmpty();
        await Assert.That(state.Created).IsEqualTo(state.Destroyed);
        await Assert.That(state.Returned).IsGreaterThan(0);
    }

    private sealed class Item
    {
        // 0 = available, 1 = held by a caller, 2 = destroyed.
        internal int State;
    }

    private sealed class AuditState
    {
        internal readonly ConcurrentQueue<string> Failures = new();
        internal int Created;
        internal int Destroyed;
        internal int Returned;
    }

    private readonly struct Policy(AuditState state) : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create()
        {
            Interlocked.Increment(ref state.Created);
            return new Item();
        }

        public bool TryReset(Item obj) => true;

        public void Destroy(Item obj)
        {
            if (Interlocked.Exchange(ref obj.State, 2) != 0)
            {
                state.Failures.Enqueue("Destroyed an owned or already destroyed item.");
            }

            Interlocked.Increment(ref state.Destroyed);
        }
    }
}
