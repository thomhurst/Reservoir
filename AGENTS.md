# Repository Guidelines

Follow `.editorconfig` and existing code conventions. Use TUnit for regression tests; cover concurrency and ownership boundaries when affected.

## Benchmark execution and local work

Run performance benchmarks on GitHub Actions `ubuntu-latest` runners to avoid local machine noise. This repository no longer requires the local Redis `performance` lock, including for local restores, builds, tests, or website builds. PR/issue ownership locks still apply.

## Validation

- SDK: `global.json` selects the .NET 10.0.4xx feature band, rolls forward only to the latest patch in that band, and excludes preview SDKs. CI setup reads this file directly.
- Restore/build: `dotnet restore Reservoir.slnx`, then `dotnet build Reservoir.slnx -c Release --no-restore` (warnings as errors).
- Tests: `dotnet test tests/Reservoir.Tests/Reservoir.Tests.csproj -c Release --no-build`; also run `tests/Reservoir.NetStandard.Tests/Reservoir.NetStandard.Tests.csproj` with the same options.
- Runtime coverage: CI runs the full suites on Linux x64/ARM64 and Windows x64, plus a bounded .NET Framework smoke on Windows. See [tests/README.md](tests/README.md) for coverage and local commands.
- Website: Node 24; run `npm ci` and `npm run build` from `website/`.
- Benchmarks: use [Benchmark comparison](.github/workflows/benchmark-compare.yml) on `ubuntu-latest` with explicit baseline and candidate commit SHAs, relevant filters, and matching runtimes/job settings. Use [Benchmarks](.github/workflows/benchmarks.yml) for the full suite on `ubuntu-latest`.

## Performance Engineering

Throughput, latency, and zero allocation are primary goals; measured micro-optimizations are welcome. Warm `Rent`/`Return` and established hot paths must remain 0 B allocated with no Gen0 collections.

Every performance change requires repeatable before/after Release benchmarks on a GitHub Actions `ubuntu-latest` runner. Run the baseline and candidate sequentially in the same job on the same runner with identical benchmark code and configuration; separate jobs or workflow runs do not guarantee identical hardware. Local measurements are diagnostic only and cannot establish performance acceptance. Use BenchmarkDotNet allocation diagnostics; report Mean, Ratio, Allocated, and noise, and link the workflow run and artifacts with both commit SHAs. Hosted runners can still be noisy: repeat inconclusive comparisons before claiming a gain. Inspect IL/JIT assembly when needed to explain results. Do not merge unproven gains or allocation/correctness regressions.

## Commits and PRs

Use Conventional Commits. PRs must explain motivation and impact, link relevant issues, and list validation. Include benchmark data for hot-path changes and screenshots for visible documentation updates. Library tests and the website build must pass before review.
