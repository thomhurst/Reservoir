using BenchmarkDotNet.Running;
using System.Reflection;
using System.Runtime.Versioning;

BenchmarkAsset.Verify();

if (args.Length == 0)
{
    args = ["--filter", "*"];
}

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);

internal static class BenchmarkAsset
{
    internal static void Verify()
    {
        string? framework = typeof(Reservoir.IPooledObjectPolicy<>).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        Console.WriteLine($"Reservoir asset: {framework}");
#if RESERVOIR_NETSTANDARD
        if (framework != ".NETStandard,Version=v2.0")
        {
            throw new InvalidOperationException("The portable benchmark suite must load the netstandard2.0 asset.");
        }
#endif
    }
}
