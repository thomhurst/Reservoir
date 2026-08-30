namespace Reservoir.Docs.Samples.QuickStart;

using Reservoir;

static class Example
{
    public static void Process(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, BufferPolicy>(maxCapacity: 64);
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

internal static class ScopedExamples
{
    internal static void LeaseValue(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, BufferPolicy>();
        using var lease = pool.RentScoped();
        Buffer buffer = lease.Value;
        buffer.Write(payload);
    }

    internal static void LeaseOut(ReadOnlySpan<byte> payload)
    {
        var pool = new ObjectPool<Buffer, BufferPolicy>();
        using var lease = pool.RentScoped(out Buffer buffer);
        buffer.Write(payload);
    }

    private sealed class Buffer
    {
        internal void Write(ReadOnlySpan<byte> value)
        {
        }
    }

    private readonly struct BufferPolicy : IPooledObjectPolicy<Buffer>
    {
        public Buffer Create() => new();

        public bool TryReset(Buffer buffer) => true;
    }
}

internal static class CollectionExample
{
    internal static void Shared()
    {
        List<int> numbers = ListPool<int>.Shared.Rent();

        try
        {
            numbers.Add(1);
            numbers.Add(2);
            Consume(numbers);
        }
        finally
        {
            ListPool<int>.Shared.Return(numbers);
        }
    }

    private static void Consume(List<int> numbers)
    {
    }
}
