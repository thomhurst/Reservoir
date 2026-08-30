namespace Reservoir.Docs.Samples.Readme;

using Reservoir;

static class Example
{
    public static void Process(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, BufferPolicy>(maxCapacity: 64);

        using var lease = pool.RentScoped(out Buffer buffer);
        buffer.Write(payload);
    }

    private sealed class Buffer
    {
        public int Length { get; set; }
        public void Write(ReadOnlySpan<byte> value) => Length += value.Length;
    }

    private readonly struct BufferPolicy : IPooledObjectPolicy<Buffer>
    {
        public Buffer Create() => new();

        public bool TryReset(Buffer buffer)
        {
            buffer.Length = 0;
            return true;
        }
    }
}

internal static class CollectionExamples
{
    internal static void Shared()
    {
        List<int> values = ListPool<int>.Shared.Rent();
        try
        {
            values.Add(42);
            Consume(values);
        }
        finally
        {
            ListPool<int>.Shared.Return(values);
        }
    }

    internal static void Scoped()
    {
        using ListPool<int>.Lease lease = ListPool<int>.Shared.RentScoped(out List<int> values);
        values.Add(42);
        Consume(values);
    }

    internal static void ThreadLocal()
    {
        List<int> values = ListPool<int>.ThreadLocalShared.Rent();
        try
        {
            Consume(values);
        }
        finally
        {
            ListPool<int>.ThreadLocalShared.Return(values);
        }
    }

    private static void Consume(List<int> values)
    {
    }
}
