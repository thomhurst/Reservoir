using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

string version = Environment.GetEnvironmentVariable("DIAG_VERSION") ?? "1.9.0";
string phase = Environment.GetEnvironmentVariable("DIAG_PHASE") ?? "validation";
string kevlarDll = Environment.GetEnvironmentVariable("KEVLAR_DLL")
    ?? throw new InvalidOperationException("KEVLAR_DLL must identify the fixed baseline DLL.");
bool dry = Environment.GetEnvironmentVariable("DIAG_DRY") == "1";
Job job = dry ? Job.Dry : Job.Default.WithLaunchCount(3).WithWarmupCount(5).WithIterationCount(10);
job = job.WithId(phase)
    .WithArguments([new MsBuildArgument($"/p:ReservoirVersion={version}"),
        new MsBuildArgument($"/p:KevlarDll={kevlarDll}")])
    .WithEnvironmentVariable("EXPECTED_RESERVOIR_VERSION", version)
    .WithEnvironmentVariable("DOTNET_TieredPGO", "1");

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance.AddJob(job));
var reports = summaries.SelectMany(summary => summary.Reports).ToArray();
if (reports.Length != 4 || reports.Any(report => !report.Success || report.ResultStatistics is null))
{
    throw new InvalidOperationException("Expected four successful benchmark results.");
}
