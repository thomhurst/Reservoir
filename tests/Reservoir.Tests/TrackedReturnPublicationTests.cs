using System.Reflection;
using System.Runtime.InteropServices;

namespace Reservoir.Tests;

public class TrackedReturnPublicationTests
{
    [Test]
    public async Task ReturnPublicationPrecedesTheDisposedStateRecheck()
    {
        const int iterations = 1_000_000;
        using var start = new Barrier(2);
        using var finish = new Barrier(2);
        var slot = new TrackedInstanceThreadLocalFrontTier<Item>.Slot();
        var flags = new LifecycleFlags();
        var item = new Item();
        int completed = 0;
        int destroyed = 0;
        int failures = 0;

        // Exercise the same clear protocol as the loaded library asset and architecture.
        bool asymmetricClear = typeof(TrackedInstanceThreadLocalFrontTier<Item>)
            .GetField("s_asymmetricClear", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) is true;

        await ConcurrentTestWorkers.RunAsync([
            token =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    start.SignalAndWait(token);
                    if (Volatile.Read(ref flags.Disposed) != 0)
                    {
                        Interlocked.Increment(ref destroyed);
                    }
                    else
                    {
                        if (!TrackedInstanceThreadLocalFrontTier<Item>.TryReturn(slot, item))
                        {
                            throw new InvalidOperationException("Unexpected occupied slot.");
                        }

                        if (Volatile.Read(ref flags.Disposed) != 0
                            && TrackedInstanceThreadLocalFrontTier<Item>.TryRemove(slot, item))
                        {
                            Interlocked.Increment(ref destroyed);
                        }
                    }

                    // A fencing barrier immediately after Return could flush a late store and
                    // hide the bug. Observe disposal using plain acquire reads before joining.
                    while (Volatile.Read(ref completed) < i + 1)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    finish.SignalAndWait(token);
                }
            },
            token =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    slot.Item = null;
                    flags.Disposed = 0;
                    destroyed = 0;
                    start.SignalAndWait(token);
                    Interlocked.Exchange(ref flags.Disposed, 1);
                    if (asymmetricClear)
                    {
                        Interlocked.MemoryBarrierProcessWide();
                    }

                    if (Interlocked.Exchange(ref slot.Item, null) is not null)
                    {
                        Interlocked.Increment(ref destroyed);
                    }

                    Volatile.Write(ref completed, i + 1);
                    finish.SignalAndWait(token);
                    if (destroyed != 1 || slot.Item is not null)
                    {
                        failures++;
                    }
                }
            },
        ]);

        await Assert.That(failures).IsEqualTo(0);
    }

    private sealed class Item;

    // Keep the lifecycle flag off the slot's cache line so the test does not accidentally
    // serialize publication with disposal through false sharing.
    [StructLayout(LayoutKind.Explicit)]
    private sealed class LifecycleFlags
    {
        [FieldOffset(128)] internal int Disposed;
    }
}
