using System.Diagnostics;

namespace Reservoir.Tests;

public class BenchmarkDocsTests
{
    [Test]
    [Arguments(null)]
    [Arguments(".NET 8.0")]
    [Arguments(".NET 10.0")]
    public async Task MultiRuntimeReportsSelectOneRuntimeAndItsMeasurementMetadata(string? runtime)
    {
        using var fixture = new Fixture();
        (int exitCode, string output) = await fixture.Run(runtime);
        await Assert.That(exitCode).IsEqualTo(0);
        string expectedRuntime = runtime ?? ".NET 10.0";
        string expectedMean = expectedRuntime == ".NET 8.0" ? "80.00 ns" : "10.00 ns";
        string otherMean = expectedRuntime == ".NET 8.0" ? "10.00 ns" : "80.00 ns";
        string docs = File.ReadAllText(fixture.DocsPath);
        string readme = File.ReadAllText(fixture.ReadmePath);
        await Assert.That(docs).Contains($"Results below select {expectedRuntime}");
        await Assert.That(docs).Contains(expectedMean);
        await Assert.That(docs).DoesNotContain(otherMean);
        await Assert.That(readme).Contains(expectedMean);
        await Assert.That(readme).DoesNotContain(otherMean);
        await Assert.That(readme).Contains(expectedRuntime == ".NET 8.0" ? ".NET 8.0.28" : ".NET 10.0.11");
        await Assert.That(readme).DoesNotContain("11.0.1");
        await Assert.That(output).Contains("Updated benchmark documentation");
    }

    [Test]
    public async Task RuntimeNamedJobsMatchBenchmarkDotNetMultiRuntimeExports()
    {
        using var fixture = new Fixture();
        foreach (List<Row> rows in fixture.Reports.Values)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i] = rows[i] with { Job = rows[i].Runtime };
            }
        }
        fixture.Metadata = fixture.Metadata
            .Replace("ShortRun : .NET 8.0", ".NET 8.0 : .NET 8.0")
            .Replace("ShortRun : .NET 10.0", ".NET 10.0 : .NET 10.0");
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.ReadmePath)).Contains(".NET 10.0.11");
    }

    [Test]
    public async Task RatiosUseSelectedRuntimeBaselineAndNormalizeDurationUnits()
    {
        using var fixture = new Fixture();
        List<Row> rows = fixture.Reports["CorePoolComparisonBenchmarks"];
        int baseline = rows.FindIndex(row => row.Method == "New" && row.Runtime == ".NET 10.0");
        int candidate = rows.FindIndex(row => row.Method == "Reservoir" && row.Runtime == ".NET 10.0");
        rows[baseline] = rows[baseline] with { Mean = "1 µs" };
        rows[candidate] = rows[candidate] with { Mean = "500 ns" };
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsEqualTo(0);
        string readme = File.ReadAllText(fixture.ReadmePath);
        await Assert.That(readme).Contains("| `new` | 1.00 µs | 1.00 |");
        await Assert.That(readme).Contains("| **Reservoir** | **500.00 ns** | **0.50** |");
    }

    [Test]
    public async Task SingleRuntimeReportsRemainSupported()
    {
        using var fixture = new Fixture([".NET 10.0"]);
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.ReadmePath)).Contains(".NET 10.0.11");
    }

    [Test]
    public async Task AmbiguousJobsRequireExplicitSelection()
    {
        using var fixture = new Fixture(extraJob: true);
        (int exitCode, string output) = await fixture.Run();
        await Assert.That(exitCode).IsNotEqualTo(0);
        // PowerShell may wrap the diagnostic between "Specify" and "-Job" on CI.
        await Assert.That(output).Contains("Expected one measurement job");
        await Assert.That(output).Contains("-Job");
        await fixture.AssertUnchanged();

        (exitCode, _) = await fixture.Run(job: "Alternative");
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.ReadmePath)).Contains("20.00 ns");
    }

    [Test]
    [Arguments("missing-runtime")]
    [Arguments("missing-result")]
    [Arguments("different-job")]
    [Arguments("missing-metadata")]
    [Arguments("ambiguous-metadata")]
    public async Task InvalidReportsDoNotChangeDocumentation(string scenario)
    {
        using var fixture = new Fixture();
        string expectedError;
        switch (scenario)
        {
            case "missing-runtime":
                foreach (List<Row> rows in fixture.Reports.Values)
                {
                    rows.RemoveAll(row => row.Runtime == ".NET 10.0");
                }
                expectedError = "Expected one measurement job";
                break;
            case "missing-result":
                fixture.Reports["ObjectPoolBenchmarks"].RemoveAll(row =>
                    row.Runtime == ".NET 10.0" && row.Method == "ScopedOutRentReturn");
                expectedError = "ScopedOutRentReturn";
                break;
            case "different-job":
                List<Row> objectRows = fixture.Reports["ObjectPoolBenchmarks"];
                int index = objectRows.FindIndex(row => row.Runtime == ".NET 10.0" && row.Method == "ScopedOutRentReturn");
                objectRows[index] = objectRows[index] with { Job = "Unrelated" };
                expectedError = "ScopedOutRentReturn";
                break;
            case "missing-metadata":
                fixture.Metadata = fixture.Metadata.Replace("  ShortRun : .NET 10.0.11, X64 RyuJIT\n", "");
                expectedError = "Could not read benchmark environment metadata";
                break;
            case "ambiguous-metadata":
                fixture.Metadata += "  ShortRun : .NET 10.0.12, X64 RyuJIT\n";
                expectedError = "Could not read benchmark environment metadata";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        (int exitCode, string output) = await fixture.Run();
        await Assert.That(exitCode).IsNotEqualTo(0);
        await Assert.That(output).Contains(expectedError);
        await fixture.AssertUnchanged();
    }

    [Test]
    [Arguments("64 B")]
    [Arguments("")]
    public async Task EveryClaimedWarmResultRejectsAllocationOrMissingMeasurement(string allocated)
    {
        using var fixture = new Fixture();
        (string Report, string[] Methods)[] claimed =
        [
            ("ObjectPoolBenchmarks", ["ScopedOutRentReturn", "ScopedRentReturn", "RentReturn"]),
            ("StringBuilderPoolBenchmarks", ["ThreadStaticCache", "Reservoir"]),
            ("CorePoolComparisonBenchmarks", ["Reservoir"]),
            ("CollectionPoolAllocationBenchmarks", ["ObjectPool", "ListPool", "DictionaryPool", "HashSetPool", "QueuePool", "StackPool", "StringBuilderPool"]),
            ("ListPoolBenchmarks", ["Reservoir"]),
            ("ObjectPoolCapacityScalingBenchmarks", ["RentReturn"]),
            ("ObjectPoolBurstBenchmarks", ["DrainAndRefill"])
        ];
        int checkedRows = 0;
        foreach ((string report, string[] methods) in claimed)
        {
            List<Row> rows = fixture.Reports[report];
            for (int index = 0; index < rows.Count; index++)
            {
                Row original = rows[index];
                if (original.Runtime != ".NET 10.0" || !methods.Contains(original.Method))
                {
                    continue;
                }

                rows[index] = original with { Allocated = allocated };
                (int exitCode, string output) = await fixture.Run();
                await Assert.That(exitCode).IsNotEqualTo(0);
                await Assert.That(output).Contains(original.Method);
                await fixture.AssertUnchanged();
                rows[index] = original;
                checkedRows++;
            }
        }
        await Assert.That(checkedRows).IsEqualTo(24);
    }

    [Test]
    [Arguments("NaN B")]
    [Arguments("-1 B")]
    [Arguments("0 frogs")]
    [Arguments("-")]
    [Arguments("0.001 B")]
    public async Task InvalidOrPositiveAllocationCannotBecomeZeroClaim(string allocated)
    {
        using var fixture = new Fixture();
        List<Row> rows = fixture.Reports["ObjectPoolBenchmarks"];
        int index = rows.FindIndex(row => row.Runtime == ".NET 10.0" && row.Method == "ScopedOutRentReturn");
        rows[index] = rows[index] with { Allocated = allocated };
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsNotEqualTo(0);
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task MissingAllocationColumnPreventsPublication()
    {
        using var fixture = new Fixture { ReportWithoutAllocationColumn = "ObjectPoolBenchmarks" };
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsNotEqualTo(0);
        await fixture.AssertUnchanged();
    }

    [Test]
    public async Task InvalidDocumentationMarkerDoesNotPartiallyPublish()
    {
        using var fixture = new Fixture();
        string invalidDocs = File.ReadAllText(fixture.DocsPath).Replace("BENCHMARK_RESULTS_LINK_END", "MISSING_LINK_END");
        string originalReadme = File.ReadAllText(fixture.ReadmePath);
        File.WriteAllText(fixture.DocsPath, invalidDocs);
        (int exitCode, _) = await fixture.Run();
        await Assert.That(exitCode).IsNotEqualTo(0);
        await Assert.That(File.ReadAllText(fixture.ReadmePath)).IsEqualTo(originalReadme);
        await Assert.That(File.ReadAllText(fixture.DocsPath)).IsEqualTo(invalidDocs);
    }

    [Test]
    public async Task ClaimsUseValidatedResultsAndOnlySelectedWarmRows()
    {
        using var fixture = new Fixture(extraJob: true);
        foreach ((string report, List<Row> rows) in fixture.Reports)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                Row row = rows[index];
                bool outsideClaim = row.Runtime != ".NET 10.0" || row.Job != "ShortRun" ||
                    row.Method is "New" or "NewList" or "NewStringBuilder" or "MicrosoftExtensionsObjectPool" or "ConcurrentBag" or "EmptyRent";
                rows[index] = row with { Allocated = outsideClaim ? "64 B" : "0.00 B" };
            }
        }
        (int exitCode, _) = await fixture.Run(job: "ShortRun");
        await Assert.That(exitCode).IsEqualTo(0);
        string docs = File.ReadAllText(fixture.DocsPath);
        await Assert.That(docs).Contains("24 validated warm results");
        await Assert.That(docs).DoesNotContain("Every measured warm Reservoir path");
        await Assert.That(docs).Contains("**0 B per operation**");
        await Assert.That(docs).Contains("TLS `StringBuilder` cache measured 10.00 ns and 0 B");
        await Assert.That(docs).Contains("manual rent/return measured 10.00 ns and 0 B");
        await Assert.That(File.ReadAllText(fixture.ReadmePath)).DoesNotContain("Every measured warm Reservoir path");
    }

    private sealed record Row(string Method, string Runtime, string Job, string Mean, string Count, string Capacity, string Allocated = "0 B");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"Reservoir.BenchmarkDocs.{Guid.NewGuid():N}");
        private readonly string _script;
        private readonly string _initialReadme;
        private readonly string _initialDocs;
        internal Dictionary<string, List<Row>> Reports { get; } = new();
        internal string Metadata { get; set; }
        internal string? ReportWithoutAllocationColumn { get; set; }
        internal string ReadmePath => Path.Combine(_root, "README.md");
        internal string DocsPath => Path.Combine(_root, "website", "docs", "benchmarks.md");

        internal Fixture(string[]? runtimes = null, bool extraJob = false)
        {
            runtimes ??= [".NET 8.0", ".NET 10.0"];
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "build", "Update-BenchmarkDocs.ps1")))
            {
                repository = repository.Parent;
            }
            if (repository is null)
            {
                throw new InvalidOperationException("Cannot locate the benchmark documentation updater.");
            }

            _script = Path.Combine(repository.FullName, "build", "Update-BenchmarkDocs.ps1");
            _initialReadme = File.ReadAllText(Path.Combine(repository.FullName, "README.md"));
            _initialDocs = File.ReadAllText(Path.Combine(repository.FullName, "website", "docs", "benchmarks.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(DocsPath)!);
            Directory.CreateDirectory(Path.Combine(_root, "results"));
            File.WriteAllText(ReadmePath, _initialReadme);
            File.WriteAllText(DocsPath, _initialDocs);

            Metadata = "BenchmarkDotNet v0.15.8, Windows 11\n" +
                "Intel Core i7-12700K 3.60GHz, 1 CPU, 20 logical cores\n" +
                "  [Host] : .NET 11.0.1, X64 RyuJIT\n";
            foreach (string runtime in runtimes)
            {
                Metadata += $"  ShortRun : {runtime}.{(runtime == ".NET 8.0" ? "28" : "11")}, X64 RyuJIT\n";
            }
            if (extraJob)
            {
                Metadata += "  Alternative : .NET 10.0.11, X64 RyuJIT\n";
            }

            AddReport("CorePoolComparisonBenchmarks", ["New", "Reservoir", "MicrosoftExtensionsObjectPool", "ConcurrentBag"]);
            AddReport("CollectionPoolAllocationBenchmarks", ["ObjectPool", "ListPool", "DictionaryPool", "HashSetPool", "QueuePool", "StackPool", "StringBuilderPool"]);
            AddReport("ListPoolBenchmarks", ["NewList", "Reservoir"], ["8", "128", "2048"], "Count");
            AddReport("ObjectPoolBenchmarks", ["RentReturn", "ScopedRentReturn", "ScopedOutRentReturn"]);
            AddReport("ObjectPoolCapacityScalingBenchmarks", ["RentReturn", "EmptyRent"], ["32", "256", "4096", "65536"], "Capacity");
            AddReport("ObjectPoolBurstBenchmarks", ["DrainAndRefill"], ["32", "256", "4096", "65536"], "Capacity");
            AddReport("StringBuilderPoolBenchmarks", ["NewStringBuilder", "Reservoir", "ThreadStaticCache"]);

            void AddReport(string name, string[] methods, string[]? values = null, string? parameter = null)
            {
                var rows = new List<Row>();
                foreach (string runtime in runtimes)
                {
                    string[] jobs = extraJob && runtime == ".NET 10.0" ? ["ShortRun", "Alternative"] : ["ShortRun"];
                    foreach (string job in jobs)
                    {
                        foreach (string method in methods)
                        {
                            foreach (string value in values ?? [""])
                            {
                                string mean = runtime == ".NET 8.0" ? "80 ns" : "10 ns";
                                if (job == "Alternative")
                                {
                                    mean = "20 ns";
                                }
                                rows.Add(new Row(method, runtime, job, mean, parameter == "Count" ? value : "", parameter == "Capacity" ? value : ""));
                            }
                        }
                    }
                }
                Reports.Add(name, rows);
            }
        }

        internal async Task<(int ExitCode, string Output)> Run(string? runtime = null, string? job = null)
        {
            foreach ((string name, List<Row> rows) in Reports)
            {
                string csv = "Method,Runtime,Job,Mean,Ratio,Allocated,Count,Capacity\n" + string.Join('\n', rows.Select(row =>
                    $"{row.Method},{row.Runtime},{row.Job},{row.Mean},9.99,{row.Allocated},{row.Count},{row.Capacity}"));
                if (name == ReportWithoutAllocationColumn)
                {
                    csv = csv.Replace(",Allocated,", ",Unavailable,");
                }
                File.WriteAllText(Path.Combine(_root, "results", $"Reservoir.Benchmarks.{name}-report.csv"), csv);
            }
            File.WriteAllText(Path.Combine(_root, "results", "Reservoir.Benchmarks.CorePoolComparisonBenchmarks-report-github.md"), Metadata);
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in new[] { "-NoProfile", "-File", _script, "-ResultsDirectory", Path.Combine(_root, "results"), "-RepositoryRoot", _root })
            {
                start.ArgumentList.Add(argument);
            }
            if (runtime is not null)
            {
                start.ArgumentList.Add("-Runtime");
                start.ArgumentList.Add(runtime);
            }
            if (job is not null)
            {
                start.ArgumentList.Add("-Job");
                start.ArgumentList.Add(job);
            }

            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            return (process.ExitCode, await output + await error);
        }

        internal async Task AssertUnchanged()
        {
            await Assert.That(File.ReadAllText(ReadmePath)).IsEqualTo(_initialReadme);
            await Assert.That(File.ReadAllText(DocsPath)).IsEqualTo(_initialDocs);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
