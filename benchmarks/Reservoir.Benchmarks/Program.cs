using BenchmarkDotNet.Running;

if (args.Length == 0)
{
    args = ["--filter", "*"];
}

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);
