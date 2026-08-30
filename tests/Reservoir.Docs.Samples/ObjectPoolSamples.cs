namespace Reservoir.Docs.Samples.ObjectPools;

using Reservoir;

internal static class PolicyExample
{
    internal static void CreatePool()
    {
        var pool = new ObjectPool<Buffer, BufferPolicy>(
            policy: new BufferPolicy(maxRetainedBytes: 64 * 1024),
            maxCapacity: 128);
    }

    private readonly struct BufferPolicy(int maxRetainedBytes) : IPooledObjectDestroyPolicy<Buffer>
    {
        public Buffer Create() => new(initialCapacity: 4096);

        public bool TryReset(Buffer buffer)
        {
            buffer.Clear();
            return buffer.Capacity <= maxRetainedBytes;
        }

        public void Destroy(Buffer buffer) => buffer.ReleaseNativeMemory();
    }

    private sealed class Buffer(int initialCapacity = 0)
    {
        internal int Capacity { get; } = initialCapacity;

        internal void Clear()
        {
        }

        internal void ReleaseNativeMemory()
        {
        }
    }
}

static class ResettableExample
{
    private sealed class Buffer : IResettable
    {
        public int Length { get; private set; }

        public bool TryReset()
        {
            Length = 0;
            return true;
        }
    }

    private static ObjectPool<Buffer, ResettablePooledObjectPolicy<Buffer>> CreatePool()
        => new();
}

internal static class RuntimePolicyExamples
{
    internal static void CreatePools()
    {
        var factoryPool = new ObjectPool<Buffer>(() => new Buffer(), maxCapacity: 32);
        var policyPool = new ObjectPool<Buffer>(new RuntimeBufferPolicy(), maxCapacity: 32);
    }

    internal static async Task ThreadLocalAsync()
    {
        var pool = new ObjectPool<Buffer>(() => new Buffer());
        Buffer buffer = pool.RentThreadLocal();
        try
        {
            await SerializeAsync(buffer);
        }
        finally
        {
            pool.ReturnThreadLocal(buffer);
        }
    }

    private static Task SerializeAsync(Buffer buffer) => Task.CompletedTask;

    private sealed class Buffer
    {
    }

    private sealed class RuntimeBufferPolicy : IPooledObjectPolicy<Buffer>
    {
        public Buffer Create() => new();

        public bool TryReset(Buffer buffer) => true;
    }
}
