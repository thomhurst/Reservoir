# Repository Guidelines

## Project Structure & Module Organization

Core library code lives in `src/Reservoir` and targets `netstandard2.0`, `net8.0`, and `net10.0`. TUnit tests are in `tests/Reservoir.Tests`; documentation examples and package-consumer checks are sibling projects under `tests/`. Benchmarks live in `benchmarks/Reservoir.Benchmarks`; Docusaurus docs and UI live under `website/`. Build helpers live in `build/`; CI definitions live in `.github/workflows`.

## Build, Test, and Development Commands

- `dotnet restore Reservoir.slnx` restores solution dependencies.
- `dotnet build Reservoir.slnx -c Release --no-restore` performs the warnings-as-errors CI build.
- `dotnet test tests/Reservoir.Tests/Reservoir.Tests.csproj -c Release --no-build` runs tests for both target frameworks.
- `dotnet run -c Release -f net10.0 --project benchmarks/Reservoir.Benchmarks -- --filter "*" --job Short --runtimes net8.0 net10.0 --apples` runs a short benchmark comparison.
- From `website/`, run `npm ci`, then `npm start` for local docs or `npm run build` for production validation. Node 24 is required.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, LF endings, final newline, four-space C# indentation, and two-space project/XML indentation. Use file-scoped namespaces, braces, nullable annotations, and explicit types unless the assigned type is apparent. Public types and members use PascalCase; locals and parameters use camelCase; interfaces start with `I`. Run `dotnet format Reservoir.slnx` for broad formatting changes.

## Performance Engineering

Maximum throughput, minimal latency, and zero allocation are primary goals; pursue even micro-optimizations. Warm `Rent`/`Return` and other established hot paths must remain 0 B allocated; treat positive `Allocated` or Gen0 results as regressions. Profile representative workloads, then inspect allocations, IL, and JIT assembly. Use BenchmarkDotNet `MemoryDiagnoser`, `DisassemblyDiagnoser`, or EventPipe, plus `dotnet-trace` or PerfView when appropriate. Every performance change needs repeatable before/after benchmarks on identical hardware and configuration, preferably a same-run baseline. Report Mean, Ratio, Allocated, and noise. Do not merge unproven gains, allocation regressions, or correctness regressions.

## Testing Guidelines

Use TUnit `[Test]` and `[Arguments]`, with behavior-focused PascalCase names such as `ReturnThenRentYieldsSameInstance`. Add regression tests beside affected components and cover concurrency or ownership boundaries when relevant. No numeric coverage threshold exists; changed behavior needs focused tests. Run benchmarks in Release mode.

## Commit & Pull Request Guidelines

History uses Conventional Commits: `feat(cts): ...`, `fix: ...`, `perf(pool): ...`, `docs: ...`, and `refactor!:` for breaking changes. Keep commits scoped and imperative. Pull requests must explain motivation and impact, link issues, and list validation. Include before/after benchmark data for hot-path changes and screenshots for visible documentation updates. Ensure library tests and website build pass before review.
