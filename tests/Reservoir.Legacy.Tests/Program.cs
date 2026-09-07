using System.Reflection;
using System.Runtime.Versioning;
using Reservoir;

namespace Reservoir.Legacy.Tests;

internal static class Program
{
    private static void Main()
    {
        Require(typeof(object).Assembly.GetName().Name == "mscorlib", "Run this smoke on .NET Framework, not CoreCLR.");
        string framework = typeof(ObjectPool<>).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Require(framework == ".NETStandard,Version=v2.0", "The consumer must load Reservoir's netstandard2.0 asset.");
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; Reservoir: {framework}");

        CollectionFallbacks();
        CustomDestruction();
        CancellationFallback();
        Console.WriteLine("Legacy runtime smoke passed.");
    }

    private static void CollectionFallbacks()
    {
        var dictionaries = new DictionaryPool<int, int>(maxRetainedCapacity: 32, maxCapacity: 1);
        CheckCollection(dictionaries.Rent, dictionaries.Return, (value, i) => value.Add(i, i), value => value.Count, value => value.Clear(), "buckets");
        var sets = new HashSetPool<int>(maxRetainedCapacity: 32, maxCapacity: 1);
        CheckCollection(sets.Rent, sets.Return, (value, i) => value.Add(i), value => value.Count, value => value.Clear(), "m_buckets");
        var queues = new QueuePool<int>(maxRetainedCapacity: 32, maxCapacity: 1);
        CheckCollection(queues.Rent, queues.Return, (value, i) => value.Enqueue(i), value => value.Count, value => value.Clear(), "_array");
        var stacks = new StackPool<int>(maxRetainedCapacity: 32, maxCapacity: 1);
        CheckCollection(stacks.Rent, stacks.Return, (value, i) => value.Push(i), value => value.Count, value => value.Clear(), "_array");
        var lists = new ListPool<int>(maxRetainedCapacity: 32, maxCapacity: 1);
        CheckCollection(lists.Rent, lists.Return, (value, i) => value.Add(i), value => value.Count, value => value.Clear());
    }

    private static void CheckCollection<T>(Func<T> rent, Action<T> giveBack, Action<T, int> add,
        Func<T, int> count, Action<T> clear, string? backingField = null) where T : class
    {
        FieldInfo? field = null;
        if (backingField is not null)
        {
            Require(typeof(T).GetMethod("EnsureCapacity", [typeof(int)]) is null,
                $"{typeof(T)} must exercise the private-field fallback on this runtime.");
            field = typeof(T).GetField(backingField, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field is not null, $"Expected the real legacy {typeof(T)}.{backingField} field.");
        }

        T small = rent();
        for (int i = 0; i < 4; i++)
        {
            add(small, i);
        }
        int capacity = field is null ? 0 : ((Array)field.GetValue(small)!).Length;
        giveBack(small);
        T reused = rent();
        Require(ReferenceEquals(small, reused), $"{typeof(T)} did not reuse an in-bound collection.");
        Require(count(reused) == 0, $"{typeof(T)} was not reset.");
        if (field is not null)
        {
            Require(((Array)field.GetValue(reused)!).Length == capacity,
                $"{typeof(T)} unexpectedly trimmed its backing array instead of using the capacity fallback.");
        }

        for (int i = 0; i < 256; i++)
        {
            add(reused, i);
        }
        clear(reused); // Empty Count must not hide an oversized backing array.
        giveBack(reused);
        T replacement = rent();
        Require(!ReferenceEquals(reused, replacement), $"{typeof(T)} retained an oversized empty collection.");
        Require(count(replacement) == 0, $"{typeof(T)} replacement was not empty.");
        giveBack(replacement);
        Console.WriteLine($"Collection reset/reuse/capacity passed: {typeof(T).Name}");
    }

    private static void CustomDestruction()
    {
        using var generic = new ObjectPool<Item, Policy>(maxCapacity: 1);
        Item rejected = generic.Rent();
        rejected.Reject = true;
        generic.Return(rejected);
        Require(rejected.DestroyCount == 1, "Generic explicit destruction was not called once.");

        var lease = generic.RentScoped();
        Item retained = lease.Value;
        lease.Dispose();
        generic.Clear();
        Require(retained.ResetCount == 1 && retained.DestroyCount == 1, "Scoped reset/clear failed.");

        using var runtime = new ObjectPool<Item>(new Policy(), maxCapacity: 1);
        Item runtimeItem = runtime.Rent();
        runtimeItem.Reject = true;
        runtime.Return(runtimeItem);
        Require(runtimeItem.DestroyCount == 1, "Runtime explicit destruction was not called once.");
        Console.WriteLine("Generic, scoped, and runtime-policy destruction passed.");
    }

    private static void CancellationFallback()
    {
        Require(typeof(CancellationTokenSource).GetMethod("TryReset", Type.EmptyTypes) is null,
            "This smoke must exercise the missing TryReset fallback.");
        using var pool = new CancellationTokenSourcePool(maxCapacity: 1);
        CancellationTokenSource first = pool.Rent();
        first.Dispose();
        RequireDisposed(first);
        CancellationTokenSource next = pool.Rent();
        Require(!ReferenceEquals(first, next) && !next.IsCancellationRequested,
            "Legacy cancellation sources must be replaced, not reused without reset.");
        next.Cancel();
        next.Dispose();
        RequireDisposed(next);

        var lease = pool.RentScoped(out CancellationTokenSource scoped);
        lease.Dispose();
        RequireDisposed(scoped);

        using var upstream = new CancellationTokenSource();
        CancellationTokenSource linked = pool.RentLinked(upstream.Token);
        upstream.Cancel();
        Require(linked.IsCancellationRequested, "Legacy linked registration did not propagate cancellation.");
        linked.Dispose();
        RequireDisposed(linked);

        using var laterUpstream = new CancellationTokenSource();
        CancellationTokenSource unlinked = pool.RentLinked(laterUpstream.Token);
        unlinked.Dispose();
        RequireDisposed(unlinked);
        laterUpstream.Cancel(); // Must not invoke Cancel on the now-disposed rental.
        Console.WriteLine("Missing TryReset and linked-registration fallback passed.");
    }

    private static void RequireDisposed(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        throw new InvalidOperationException("The source was not permanently disposed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Item
    {
        internal bool Reject;
        internal int ResetCount;
        internal int DestroyCount;
    }

    private readonly struct Policy : IPooledObjectDestroyPolicy<Item>
    {
        public Item Create() => new();
        public bool TryReset(Item item)
        {
            item.ResetCount++;
            return !item.Reject;
        }
        void IPooledObjectDestroyPolicy<Item>.Destroy(Item item) => item.DestroyCount++;
    }
}
