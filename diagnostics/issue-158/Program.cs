using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

string reservoirDll = Environment.GetEnvironmentVariable("RESERVOIR_DLL")
    ?? throw new InvalidOperationException("RESERVOIR_DLL must identify the source-built library.");
string phase = Environment.GetEnvironmentVariable("DIAG_PHASE") ?? "validation";
string kevlarDll = Environment.GetEnvironmentVariable("KEVLAR_DLL")
    ?? throw new InvalidOperationException("KEVLAR_DLL must identify the fixed baseline DLL.");
bool dry = Environment.GetEnvironmentVariable("DIAG_DRY") == "1";
Job job = dry ? Job.Dry : Job.Default.WithLaunchCount(3).WithWarmupCount(5).WithIterationCount(10);
job = job.WithId(phase)
    .WithArguments([new MsBuildArgument($"/p:RESERVOIR_DLL={reservoirDll}"),
        new MsBuildArgument($"/p:KevlarDll={kevlarDll}")])
    .WithEnvironmentVariable("EXPECTED_RESERVOIR_VERSION", "1.9.0")
    .WithEnvironmentVariable("DOTNET_TieredPGO", "1");

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
    .Run(args, DefaultConfig.Instance.AddJob(job));
var reports = summaries.SelectMany(summary => summary.Reports).ToArray();
if (reports.Length != 8 || reports.Any(report => !report.Success || report.ResultStatistics is null))
{
    throw new InvalidOperationException("Expected eight successful benchmark results.");
}
