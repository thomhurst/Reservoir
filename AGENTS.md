# Repository Guidelines

Follow `.editorconfig` and existing code conventions. Use TUnit for regression tests; cover concurrency and ownership boundaries when affected.

## Local workload coordination

Before local benchmarks, profiling, stress runs, builds, tests, restores, or other heavy work, follow [the shared performance lock workflow](scripts/PerformanceLock.md). All four repositories reserve the same Redis `performance` key through `C:/git/Dekaf/scripts/AgentLocks.ps1`; this repository's item-lock backend is separate. Reading and editing can continue while another agent owns the reservation.

## Validation

- SDK: `global.json` selects the .NET 10.0.4xx feature band, rolls forward only to the latest patch in that band, and excludes preview SDKs. CI setup reads this file directly.
- Restore/build: `dotnet restore Reservoir.slnx`, then `dotnet build Reservoir.slnx -c Release --no-restore` (warnings as errors).
- Tests: `dotnet test tests/Reservoir.Tests/Reservoir.Tests.csproj -c Release --no-build`; also run `tests/Reservoir.NetStandard.Tests/Reservoir.NetStandard.Tests.csproj` with the same options.
- Website: Node 24; run `npm ci` and `npm run build` from `website/`.
- Benchmarks: `dotnet run -c Release -f net10.0 --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short --runtimes net8.0 net10.0 --apples`.

## Performance Engineering

Throughput, latency, and zero allocation are primary goals; measured micro-optimizations are welcome. Warm `Rent`/`Return` and established hot paths must remain 0 B allocated with no Gen0 collections.

Every performance change requires repeatable before/after Release benchmarks on identical hardware and configuration, preferably a same-run baseline. Use BenchmarkDotNet allocation diagnostics; report Mean, Ratio, Allocated, and noise. Inspect IL/JIT assembly when needed to explain results. Do not merge unproven gains or allocation/correctness regressions.

## Commits and PRs

Use Conventional Commits. PRs must explain motivation and impact, link relevant issues, and list validation. Include benchmark data for hot-path changes and screenshots for visible documentation updates. Library tests and the website build must pass before review.
