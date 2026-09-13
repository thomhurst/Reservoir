# Issue 158: Ubuntu reproduction

The investigation now has three successful Ubuntu A-B-A comparisons. Reservoir 1.9.0 shows recurring small slowdowns in pool operations and single contexts; its nested-context direction remains inconsistent against the two baseline controls. A release checkpoint demonstrates that measurable costs already exist in 1.6.10, before the nested reset exception handler. The guarded-reset change therefore cannot explain the whole signal. No production optimization is accepted from these measurements.

## Reproduction

- Harness commit: `41e04e46dda350f0eb616e158cb3104d3173aba8`, branch `issue-158-reproduction`.
- Workflow: https://github.com/thomhurst/Reservoir/actions/runs/34780086623
- Reservoir 1.4.0 source: `21584932fcd1b630881f3ec2cf3f38a90ee45e41`.
- Reservoir 1.9.0 source: `8c06265ee370951fd11fa002c7c7299ed9575cc3`.
- Both source SHAs also match the published packages' NuGet repository metadata.
- Kevlar: `801416a0e6f800b643cacacc402b45a42da48ad8`, built once per runner against exactly Reservoir 1.4.0. Its DLL remains fixed during all comparisons on that runner.
- Ubuntu 24.04, x64, AMD EPYC 7763 virtual machine with four vCPUs; SDK 10.0.401, runtime 10.0.12, BDN 0.15.8.
- Four workloads, capacity 128, unmarked trivial pool policy, synchronously completed Kevlar callbacks.
- A-B-A sequential phases on one VM. Each workload has three launches, five warmup iterations and ten measured iterations. Explicit PGO enabled; identical disassembly depth 8; no affinity restriction; no `--apples`.
- Full JSON, disassembly, package assets, loaded assembly versions/hashes, source SHAs and hardware information retained. Setup verifies returned values and actual loaded Reservoir versions. The summarizer rejects missing cases/launches, changed DLLs, allocations or Gen0 collections.

Local Dry validation passed all eight package/workload combinations. Release harness build and actionlint passed. No library source changed.

## Attempt 1

Raw artifact copy: [ubuntu-attempt-1.zip](prior-evidence/ubuntu-attempt-1.zip). Parsed results: [comparison-summary.json](comparison-summary.json). Per-launch means are included in that file.

| Workload | A: 1.4.0 Mean ± error (ns) | B: 1.9.0 Mean ± error (ns) | C: 1.4.0 Mean ± error (ns) | B/A | B/C | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Single rent/return | 15.807 ± 0.060 | 19.179 ± 2.383 | 15.816 ± 0.065 | 1.213 | 1.213 | 0 B/op |
| Nested rent/return | 37.730 ± 0.381 | 38.802 ± 0.263 | 37.932 ± 0.663 | 1.028 | 1.023 | 0 B/op |
| Single context | 101.188 ± 2.081 | 104.923 ± 1.030 | 102.182 ± 1.582 | 1.037 | 1.027 | 0 B/op |
| Nested context | 230.861 ± 7.726 | 249.945 ± 5.184 | 220.085 ± 10.618 | 1.083 | 1.136 | 0 B/op |

Error is BDN's 99.9% confidence-interval half-width. Ratios divide aggregate means. All 12 measured cases report zero Gen0 collections.

The single-pair candidate's launch means are 23.452, 16.598 and 16.724 ns. The first launch stays slow throughout its measured iterations; it is not a visible warmup transition. Both baseline phases remain near 15.81 ns. Thus the 21% aggregate increase should not be described as a stable per-launch penalty.

Nested-context baseline launch means range from 208.323 to 240.671 ns across A and C. Candidate launch means are 253.811, 256.693 and 240.780 ns. The signal warrants a repeat, with the baseline variation retained in the interpretation.

## Attempt 2

Workflow attempt: https://github.com/thomhurst/Reservoir/actions/runs/34780086623/attempts/2

Remote artifact: https://github.com/thomhurst/Reservoir/actions/runs/34780086623/artifacts/10325172615

Parsed results: [comparison-summary.json](comparison-summary.json).

| Workload | A: 1.4.0 Mean ± error (ns) | B: 1.9.0 Mean ± error (ns) | C: 1.4.0 Mean ± error (ns) | B/A | B/C | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Single rent/return | 15.933 ± 0.099 | 16.688 ± 0.092 | 15.769 ± 0.049 | 1.047 | 1.058 | 0 B/op |
| Nested rent/return | 37.381 ± 0.155 | 38.917 ± 0.409 | 37.236 ± 0.117 | 1.041 | 1.045 | 0 B/op |
| Single context | 103.533 ± 1.705 | 108.703 ± 1.306 | 101.686 ± 0.406 | 1.050 | 1.069 | 0 B/op |
| Nested context | 259.276 ± 18.021 | 240.574 ± 3.439 | 228.390 ± 9.337 | 0.928 | 1.053 | 0 B/op |

All 12 measured cases again report zero Gen0 collections. Both repetitions therefore contain 24 successful measured cases, all 0 B/op.

The single-pair candidate launch means are now 16.604, 16.614 and 16.878 ns. The initial run's 23.452 ns launch does not recur. A roughly 0.8–0.9 ns penalty remains against both baseline controls.

Nested contexts are 7.2% faster than A but 5.3% slower than C. Baseline A includes a 294.953 ns launch, while baseline C launch means range from 211.253 to 242.156 ns. Candidate launches range from 236.012 to 247.575 ns. This does not establish a stable end-to-end nested-context regression.

Do not average absolute measurements between attempts; each attempt uses its own hosted VM. First-attempt raw data was downloaded before the rerun removed its artifact from the run, and is preserved in the checkpoint branch at `diagnostics/issue-158/prior-evidence/ubuntu-attempt-1.zip`.

## Linux code generation

The trivial reset wrapper is 42 bytes in both baseline controls and 170 bytes in 1.9.0. The single-pair caller is 1,277 bytes in A, 1,953 bytes in B and 1,269 bytes in C. Kevlar's context rent method is also larger in 1.9.0.

Identical baseline binaries produce some different method sizes between A and C. These disassembly snapshots confirm code growth and variation, but do not identify the code executed by the unusually slow first candidate launch. No causal attribution to instruction-cache pressure or the guarded-reset commit follows from the byte counts alone.

## Release checkpoint: 1.4.0 versus 1.6.10

The 1.6.10 checkpoint includes the final disabled-by-default manual TLS path and storage changes, but predates the nested reset exception handler. It separates release ranges without reverting a correctness fix.

Checkpoint harness commit: `8760ca04e80da648c3d2b9917a79b4b43386ab4c`. Reservoir 1.6.10 source: `d78bfa45a8f66c339850564bb20002cf574319cf`. Benchmark C# source and measurement settings remain identical; only candidate package selection and evidence handling change. Local Dry validation passed all four 1.6.10 workloads.

Checkpoint workflow: https://github.com/thomhurst/Reservoir/actions/runs/34781306080

Checkpoint artifact: https://github.com/thomhurst/Reservoir/actions/runs/34781306080/artifacts/10324863886

The artifact also contains the archived first 1.4.0/1.9.0 attempt under `prior-evidence/`. Parsed checkpoint results: [comparison-summary.json](comparison-summary.json).

| Workload | A: 1.4.0 Mean ± error (ns) | B: 1.6.10 Mean ± error (ns) | C: 1.4.0 Mean ± error (ns) | B/A | B/C | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Single rent/return | 15.808 ± 0.105 | 16.351 ± 0.129 | 15.845 ± 0.065 | 1.034 | 1.032 | 0 B/op |
| Nested rent/return | 38.368 ± 0.879 | 38.240 ± 0.308 | 37.440 ± 0.053 | 0.997 | 1.021 | 0 B/op |
| Single context | 102.772 ± 1.378 | 104.473 ± 0.481 | 101.942 ± 0.792 | 1.017 | 1.025 | 0 B/op |
| Nested context | 223.541 ± 10.853 | 263.404 ± 6.168 | 223.845 ± 13.327 | 1.178 | 1.177 | 0 B/op |

All 12 checkpoint cases report zero Gen0 collections. Across all three completed comparisons, all 36 measured cases report 0 B/op and zero Gen0 collections.

The checkpoint's single-pair launch means are 16.232, 16.240 and 16.596 ns. Its nested-context launch means are 252.025, 263.094 and 273.956 ns. Baseline nested launches still vary widely, so the approximately 17.7% checkpoint effect should be independently repeated before treating that magnitude as stable. It is an observed earlier-release slowdown, not proof of a universal regression or a comparison of 1.6.10 against 1.9.0 on identical hardware.

The single-pair reset wrapper remains **42 bytes** in 1.6.10, matching 1.4.0, while the single-pair caller is already **1,953 bytes**, matching the 1.9.0 snapshot. In the nested-context case, `KevlarContext.Rent` is **2,061 bytes** in 1.6.10 versus approximately **1,080 bytes** in the 1.4.0 controls. The context reset wrapper itself is 2,331 bytes in the checkpoint versus 2,563 bytes in those controls. Thus the checkpoint's slower nested result does not require a larger reset wrapper.

## Disposition and remaining uncertainty

- Keep the reset failure correctness fix. These measurements do not justify removing or weakening its exception/ownership contract.
- The first source range to investigate is now **1.4.0 through 1.6.10**, particularly optional manual TLS inlining, padding/pre-CAS reads, and stripe changes. The 1.9.0 nested exception handler is not necessary for the earlier-release signals observed here. This does not exclude an additional cost from that handler.
- Use a matched source comparison to separate TLS caller growth from storage changes. Neither has been established as the cause by these package comparisons.
- A stable end-to-end 1.9.0 nested-context regression remains unconfirmed: its repeated Ubuntu results change direction against A, and baseline process variation is material. Single-pair and nested-context measurements must not be conflated.
- .NET 8, matched PGO-on/off experiments, Windows hosted reproduction, suspended async execution, and enabled TLS/scoped protection cases remain unmeasured by this harness. No source optimization or PR is proposed, so these data are not a completed acceptance matrix for a fix.

Validation performed: Release harness build, actionlint, 12 local Dry cases across three package versions, all remote Dry phases, three complete A-B-A comparisons, and independent local revalidation of every downloaded summary. Full library/website suites were not rerun because this work changes only the task-scoped diagnostic harness and evidence; library behavior is unchanged.
