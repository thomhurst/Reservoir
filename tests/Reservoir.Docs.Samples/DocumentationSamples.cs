using System.Text;

namespace Reservoir.Docs.Samples;

internal static class DocumentationSamples
{
    internal static void RentAndReturn(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, QuickStartBufferPolicy>(maxCapacity: 64);
        Buffer buffer = pool.Rent();

        try
        {
            buffer.Write(payload);
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    internal static void ScopedLease(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, QuickStartBufferPolicy>(maxCapacity: 64);
        using var lease = pool.RentScoped(out Buffer buffer);
        buffer.Write(payload);
    }

    internal static void CustomPolicy()
    {
        using var pool = new ObjectPool<Buffer, BufferPolicy>(
            policy: new BufferPolicy(maxRetainedBytes: 64 * 1024),
            maxCapacity: 128);
        pool.Return(pool.Rent());
    }

    internal static void Resettable()
    {
        var pool = new ObjectPool<ResettableBuffer, ResettablePooledObjectPolicy<ResettableBuffer>>();
        using var lease = pool.RentScoped();
        lease.Value.Write([1, 2, 3]);
    }

    internal static void FactoryAndRuntimePolicy()
    {
        using var factoryPool = new ObjectPool<Buffer>(() => new Buffer(), maxCapacity: 32);
        using var policyPool = new ObjectPool<Buffer>(new RuntimeBufferPolicy(), maxCapacity: 32);
        factoryPool.Return(factoryPool.Rent());
        policyPool.Return(policyPool.Rent());
    }

    internal static int Collections()
    {
        List<int> values = ListPool<int>.Shared.Rent();

        try
        {
            values.Add(42);
            return values[0];
        }
        finally
        {
            ListPool<int>.Shared.Return(values);
        }
    }

    internal static void DedicatedCollections()
    {
        var listPool = new ListPool<int>(maxRetainedCapacity: 256, maxCapacity: 32);
        var dictionaryPool = new DictionaryPool<string, int>(
            comparer: StringComparer.OrdinalIgnoreCase,
            maxRetainedCapacity: 512,
            maxCapacity: 16);
        var hashSetPool = new HashSetPool<string>(StringComparer.Ordinal, 512, 16);
        var queuePool = new QueuePool<int>(256, 32);
        var stackPool = new StackPool<int>(256, 32);
        var builderPool = new StringBuilderPool(2048, 32);

        listPool.Return(listPool.Rent());
        dictionaryPool.Return(dictionaryPool.Rent());
        hashSetPool.Return(hashSetPool.Rent());
        queuePool.Return(queuePool.Rent());
        stackPool.Return(stackPool.Rent());
        builderPool.Return(builderPool.Rent());
    }

    internal static string BuildString(int requestId)
    {
        StringBuilder builder = StringBuilderPool.Shared.Rent();

        try
        {
            return builder.Append("request-").Append(requestId).ToString();
        }
        finally
        {
            StringBuilderPool.Shared.Return(builder);
        }
    }

    internal static async Task CancellationTokenSourceAsync(
        Func<CancellationToken, Task> operation)
    {
        using CancellationTokenSource source = CancellationTokenSourcePool.Shared.Rent();
        source.CancelAfter(TimeSpan.FromSeconds(30));
        await operation(source.Token);
    }

    internal static async Task LinkedCancellationTokenSourceAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken callerToken)
    {
        using CancellationTokenSource source =
            CancellationTokenSourcePool.Shared.RentLinked(callerToken);
        source.CancelAfter(TimeSpan.FromSeconds(30));
        await operation(source.Token);
    }

    internal static void ScopedCancellationTokenSource(Action<CancellationToken> operation)
    {
        using var lease = CancellationTokenSourcePool.Shared.RentScoped(
            out CancellationTokenSource source);
        source.CancelAfter(TimeSpan.FromSeconds(5));
        operation(source.Token);
    }
}

internal class Buffer
{
    internal Buffer(int initialCapacity = 0)
    {
        Capacity = initialCapacity;
    }

    internal int Capacity { get; }

    internal int Length { get; set; }

    internal void Write(ReadOnlySpan<byte> value) => Length += value.Length;

    internal void Clear() => Length = 0;

    internal void ReleaseNativeMemory()
    {
    }
}

internal sealed class ResettableBuffer : Buffer, IResettable
{
    public bool TryReset()
    {
        Clear();
        return true;
    }
}

internal readonly struct QuickStartBufferPolicy : IPooledObjectPolicy<Buffer>
{
    public Buffer Create() => new();

    public bool TryReset(Buffer buffer)
    {
        buffer.Clear();
        return true;
    }
}

internal readonly struct BufferPolicy(int maxRetainedBytes)
    : IPooledObjectDestroyPolicy<Buffer>
{
    public Buffer Create() => new(initialCapacity: 4096);

    public bool TryReset(Buffer buffer)
    {
        buffer.Clear();
        return buffer.Capacity <= maxRetainedBytes;
    }

    public void Destroy(Buffer buffer) => buffer.ReleaseNativeMemory();
}

internal sealed class RuntimeBufferPolicy : IPooledObjectPolicy<Buffer>
{
    public Buffer Create() => new();

    public bool TryReset(Buffer buffer)
    {
        buffer.Clear();
        return true;
    }
}
