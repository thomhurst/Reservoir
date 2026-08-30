namespace Reservoir.Docs.Samples.CancellationTokenSources;

using Reservoir;

internal static class CancellationTokenSourceExamples
{
    internal static async Task RentAsync()
    {
        using CancellationTokenSource source = CancellationTokenSourcePool.Shared.Rent();
        source.CancelAfter(TimeSpan.FromSeconds(30));
        await ProcessAsync(source.Token);
    }

    internal static async Task RentLinkedAsync(CancellationToken callerToken)
    {
        using CancellationTokenSource source =
            CancellationTokenSourcePool.Shared.RentLinked(callerToken);

        source.CancelAfter(TimeSpan.FromSeconds(30));
        await ProcessAsync(source.Token);
    }

    internal static void RentScoped()
    {
        using var lease = CancellationTokenSourcePool.Shared.RentScoped(
            out CancellationTokenSource source);

        source.CancelAfter(TimeSpan.FromSeconds(5));
        RunSynchronousWork(source.Token);
    }

    internal static void DedicatedPool()
    {
        using var pool = new CancellationTokenSourcePool(maxCapacity: 32);
    }

    private static Task ProcessAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void RunSynchronousWork(CancellationToken cancellationToken)
    {
    }
}
