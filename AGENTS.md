# Repository Guidelines

Follow `.editorconfig` and existing code conventions. Use TUnit for regression tests; cover concurrency and ownership boundaries when affected.

## Benchmark execution and local work

Run performance benchmarks on GitHub Actions `ubuntu-latest` runners to avoid local machine noise. This repository no longer requires the local Redis `performance` lock, including for local restores, builds, tests, or website builds. PR/issue ownership locks still apply.

Agent output outside a worktree (evidence, throwaway checkouts, downloaded tools) has fixed locations; never write to the repository's parent directory itself or to the main checkout. See [issue-pr-loop § Isolation and ownership](.agents/skills/issue-pr-loop/SKILL.md#isolation-and-ownership).

## Validation

- SDK: `global.json` selects the .NET 10.0.4xx feature band, rolls forward only to the latest patch in that band, and excludes preview SDKs. CI setup reads this file directly.
- Restore/build: `dotnet restore Reservoir.slnx`, then `dotnet build Reservoir.slnx -c Release --no-restore` (warnings as errors).
- Tests: `dotnet test tests/Reservoir.Tests/Reservoir.Tests.csproj -c Release --no-build`; also run `tests/Reservoir.NetStandard.Tests/Reservoir.NetStandard.Tests.csproj` with the same options.
- Runtime coverage: CI runs the full suites on Linux x64/ARM64 and Windows x64, plus a bounded .NET Framework smoke on Windows. See [tests/README.md](tests/README.md) for coverage and local commands.
- Website: Node 24; run `npm ci` and `npm run build` from `website/`.
- Benchmarks: use [Benchmark comparison](.github/workflows/benchmark-compare.yml) on `ubuntu-latest` with explicit baseline and candidate commit SHAs, relevant filters, and matching runtimes/job settings. Use [Benchmarks](.github/workflows/benchmarks.yml) for the full suite on `ubuntu-latest`.

For a bounded benchmark setup check and short measurement, run `gh workflow run benchmarks.yml --ref main -f 'filter=*.ObjectPoolBenchmarks.RentReturn' -f job=short`. The workflow validates both runtimes with `--job Dry` before measuring with `--job Short`; Dry output cannot establish performance or allocation guarantees. For the full suite, use `-f 'filter=*' -f job=short`. Never add `--apples` to these commands: BenchmarkDotNet 0.15.8 interacts with `OperationsPerInvoke` such that batched contention cases become effectively unbounded. See [reproduction details](website/docs/benchmarks.md#reproduce).

## Performance Engineering

Throughput, latency, and zero allocation are primary goals; measured micro-optimizations are welcome. Warm `Rent`/`Return` and established hot paths must remain 0 B allocated with no Gen0 collections.

Every performance change requires repeatable before/after Release benchmarks on a GitHub Actions `ubuntu-latest` runner. Run the baseline and candidate sequentially in the same job on the same runner with identical benchmark code and configuration; separate jobs or workflow runs do not guarantee identical hardware. Local measurements are diagnostic only and cannot establish performance acceptance. Use BenchmarkDotNet allocation diagnostics; report Mean, Ratio, Allocated, and noise, and link the workflow run and artifacts with both commit SHAs. Hosted runners can still be noisy: repeat inconclusive comparisons before claiming a gain. Inspect IL/JIT assembly when needed to explain results. Do not merge unproven gains or allocation/correctness regressions.

## Commits and PRs

Use Conventional Commits. PRs must explain motivation and impact, link relevant issues, and list validation. Include benchmark data for hot-path changes and screenshots for visible documentation updates. Run the library tests, and the website build when the website changes, locally before opening a PR; CI runs them again once the PR is ready.

Open every agent-created PR as a draft (`gh pr create --draft`). Build and test CI skips drafts, so review fixes do not spend CI runs. Mark the PR ready only after every required reviewer approves its current head and every finding is dispositioned, then arm auto-merge immediately with `pwsh scripts/Enable-AutoMerge.ps1 -Pr <N>`. Never arm a draft, and never call `gh pr merge --auto` directly. The lifecycle, required reviewers, and merge commands are in [issue-pr-loop § Draft review lifecycle](.agents/skills/issue-pr-loop/SKILL.md#draft-review-lifecycle) and its [repository guidance](.agents/skills/issue-pr-loop/references/repository.md#draft-review-lifecycle); follow them without starting the queue.

Every review cycle, check both review threads (`pulls/<N>/reviews`, `pulls/<N>/comments`, GraphQL `reviewThreads`) and PR conversation comments (`issues/<N>/comments`). Reviewers differ: Claude often posts its review as a conversation comment, while other agents use review threads. A finding on either surface needs a disposition before merge.

Resolve each PR review thread, whether a human or a bot opened it, as soon as you have dispositioned it: the fix is pushed to the PR head and your reply names the commit, or your reply pushes back on the finding with evidence. Leave a thread open only while it has no disposition. If the reviewer replies after your disposition, unresolve the thread and handle the reply.

## Autonomous self-improvement

Treat every problem you notice as work to act on, whether you find it while testing, developing, reading code, reviewing, or doing anything else. Examples include bugs, flaky or failing tests, incorrect docs, misleading comments, broken tooling, and gaps in these instructions.

- If the fix is trivial and low-risk, fix it as part of the current work and mention it in the PR description.
- If the fix is out of scope for the current work, raise a GitHub issue at the point you discover it so it can be fixed later. Include what you saw, where you saw it (file and line, command, or CI run), how to reproduce it, and the expected behavior. Search existing issues first, and add to a matching issue instead of opening a duplicate.
- Do not ignore a problem because it existed before your change or is unrelated to it.
