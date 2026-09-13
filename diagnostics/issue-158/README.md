# Issue 158 reproduction

Task-scoped diagnostic harness for Reservoir 1.4.0 versus 1.9.0. This branch is an experiment, not a production optimization.

The workflow builds Kevlar once from `801416a0e6f800b643cacacc402b45a42da48ad8` with exactly Reservoir 1.4.0. It then builds separate BenchmarkDotNet executables against exact Reservoir versions, passing package selection into each generated build. Setup checks loaded versions and records assembly SHA-256 hashes.

The release source commits are `21584932fcd1b630881f3ec2cf3f38a90ee45e41` (1.4.0) and `8c06265ee370951fd11fa002c7c7299ed9575cc3` (1.9.0). Measurements use the published packages, not new source builds.

Four workloads cover manual single/nested rent-return at capacity 128 with an unmarked trivial policy, and single/nested Kevlar contexts. All callbacks complete synchronously. This does not measure suspended async execution.

`.github/workflows/issue-158.yml` runs on `ubuntu-latest`. Two Dry validation phases precede A-B-A measurement, all sequentially on the same runner. Each measured phase has three launches, five warmup iterations, ten measured iterations, explicit `DOTNET_TieredPGO=1`, and identical disassembly settings. CPU affinity is unrestricted consistently across phases. BDN 0.15.8 matches the original investigation. No `--apples` is used.

The summary validates all expected cases and launches, verifies the fixed Kevlar hash and matching baseline hashes, preserves Mean/error/stddev/per-launch means, and fails on allocations or Gen0 collections. Ratios compare B with both A controls; C/A quantifies drift. Dry timing is excluded.

Use the workflow's **Re-run all jobs** action for an independent repetition. Compare within each runner; do not pool absolute timings from different runners. Artifacts include full JSON, disassembly, logs, dependency assets, source SHAs, and assembly hashes.
