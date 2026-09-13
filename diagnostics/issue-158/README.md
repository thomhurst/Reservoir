# Issue 158 reproduction

Task-scoped diagnostic harness for Reservoir package comparisons. This branch is an experiment, not a production optimization.

The first harness commit (`41e04e46dda350f0eb616e158cb3104d3173aba8`) compares 1.4.0 with 1.9.0. Two Ubuntu repetitions retain inconsistent nested-context direction but small pool/single-context slowdowns. The current workflow compares 1.4.0 with the 1.6.10 checkpoint to determine whether those costs predate nested reset exception handling. Benchmark source and settings are identical.

The workflow builds Kevlar once from `801416a0e6f800b643cacacc402b45a42da48ad8` with exactly Reservoir 1.4.0. It then builds separate BenchmarkDotNet executables against exact Reservoir versions, passing package selection into each generated build. Setup checks loaded versions and records assembly SHA-256 hashes.

The release source commits are `21584932fcd1b630881f3ec2cf3f38a90ee45e41` (1.4.0), `d78bfa45a8f66c339850564bb20002cf574319cf` (1.6.10), and `8c06265ee370951fd11fa002c7c7299ed9575cc3` (1.9.0). Measurements use the published packages, not new source builds.

Four workloads cover manual single/nested rent-return at capacity 128 with an unmarked trivial policy, and single/nested Kevlar contexts. All callbacks complete synchronously. This does not measure suspended async execution.

`.github/workflows/issue-158.yml` runs on `ubuntu-latest`. Two Dry validation phases precede A-B-A measurement, all sequentially on the same runner. Each measured phase has three launches, five warmup iterations, ten measured iterations, explicit `DOTNET_TieredPGO=1`, and identical disassembly settings. CPU affinity is unrestricted consistently across phases. BDN 0.15.8 matches the original investigation. No `--apples` is used.

The summary validates all expected cases and launches, verifies the fixed Kevlar hash and matching baseline hashes, preserves Mean/error/stddev/per-launch means, and fails on allocations or Gen0 collections. Ratios compare B with both A controls; C/A quantifies drift. Dry timing is excluded.

Download artifacts before using **Re-run all jobs** for an independent repetition: GitHub removed the prior attempt's artifact from the run during the first repetition. Compare within each runner; do not pool absolute timings from different runners. Artifacts include full JSON, disassembly, logs, dependency assets, source SHAs, and assembly hashes. `prior-evidence/ubuntu-attempt-1.zip` preserves the first attempt; the second attempt remains available on run 34780086623.
