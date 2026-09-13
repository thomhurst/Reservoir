# Guarded reset optimization for issue 158

Candidate: `94a5573e46f36d8d27039d16d3b2a3dfbd1fe254`.
Baseline: `8c06265ee370951fd11fa002c7c7299ed9575cc3` (1.9.0).

The change moves only construction of the aggregate reset/destruction exception into a `NoInlining` helper. The nested handlers, destruction, rethrow, exception identities, ordering, and message remain unchanged. No TLS changes are included.

This addresses guarded-reset overhead identified in [issue 158](https://github.com/thomhurst/Reservoir/issues/158). It does not establish the original nested-Kevlar slowdown as a persistent package regression.

## Measurements

All comparisons run baseline and candidate sequentially in the same `ubuntu-latest` job with identical benchmark code and settings. Each measured case has three process launches. The source fixture additionally repeats the baseline after the candidate. Results below are ns/op, mean plus/minus the BDN 99.9% confidence-interval half-width; ratios divide the candidate mean by the baseline mean.

### Capacity 32, .NET 10

| Run | Method | Baseline | Candidate | Ratio |
| --- | --- | ---: | ---: | ---: |
| Short, JIT capture | ScopedRentReturn | 11.726 +/- 0.0277 | 11.470 +/- 0.0220 | 0.978 |
| Short, repeat | ScopedRentReturn | 11.950 +/- 0.6240 | 11.493 +/- 0.0586 | 0.962 |
| Medium | ScopedRentReturn | 12.260 +/- 0.3310 | 11.500 +/- 0.0090 | 0.938 |
| Medium | RentReturn | 18.100 +/- 0.0620 | 18.220 +/- 0.0690 | 1.007 |

The scoped candidate is stable around 11.5 ns across these runners. The baseline varies more, so the 6.2% Medium result is not a universal speedup. The longer manual-rent comparison resolves the earlier noisy 1.9-3.3% slowdown estimates to 0.7%, with overlapping intervals. Manual rent is not claimed to improve.

- [Short with JIT capture](https://github.com/thomhurst/Reservoir/actions/runs/34785183396), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34785183396/artifacts/10326019042).
- [Short repetition](https://github.com/thomhurst/Reservoir/actions/runs/34785560809), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34785560809/artifacts/10325819989).
- [Longer manual/scoped comparison](https://github.com/thomhurst/Reservoir/actions/runs/34786021981), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34786021981/artifacts/10326452470).

### Capacity 128, .NET 10, fixed Kevlar DLL

The fixture builds both Reservoir libraries from full source with identical version/framework settings and holds Kevlar `801416a0e6f800b643cacacc402b45a42da48ad8` constant. Kevlar is built against Reservoir 1.4.0; the selected source-built 1.9.0 assembly is loaded for each phase. Setup records and checks assembly versions and SHA-256 hashes. These context callbacks complete synchronously; suspended asynchronous execution is not measured.

| Method | Baseline A | Candidate B | Baseline C | B/A | B/C |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scoped | 12.940 +/- 0.080 | 12.530 +/- 0.038 | 12.975 +/- 0.058 | 0.968 | 0.966 |
| ManualTls | 16.329 +/- 0.350 | 15.774 +/- 0.017 | 16.067 +/- 0.033 | 0.966 | 0.982 |
| SingleContext | 104.123 +/- 1.207 | 102.080 +/- 0.969 | 106.835 +/- 2.370 | 0.980 | 0.955 |
| NestedContext | 254.698 +/- 6.057 | 255.903 +/- 6.204 | 249.969 +/- 5.995 | 1.005 | 1.024 |

The scoped gain is 3.2-3.4% against both stable controls. The original nested-context workload shows no clear gain or regression. Single-context intervals overlap the first control; its apparent gain is not treated as established.

One candidate `NestedManualTls` launch measured 126.98 ns, versus 40.45 and 41.08 ns in its other launches. All baseline launches were approximately 40-44 ns. The anomalous launch remains in the report; it is not removed as an outlier. `NestedScoped` was also noisy, requiring a focused repetition.

[Full A-B-A run](https://github.com/thomhurst/Reservoir/actions/runs/34785187385), [artifact with JSON, per-launch measurements, assembly, hashes, and source SHAs](https://github.com/thomhurst/Reservoir/actions/runs/34785187385/artifacts/10326054705).

### Protected-path repetition

The [focused A-B-A run](https://github.com/thomhurst/Reservoir/actions/runs/34786315629) uses the same four benchmark methods and settings on an Intel Xeon Platinum 8370C runner. The full run above used AMD EPYC 7763. Comparisons remain within each runner; absolute times are not pooled across hardware. The fixture's post-run checks select four expected methods; benchmark bodies and setup are unchanged. [Full focused artifact](https://github.com/thomhurst/Reservoir/actions/runs/34786315629/artifacts/10326273378).

| Method | Baseline A | Candidate B | Baseline C | B/A | B/C |
| --- | ---: | ---: | ---: | ---: | ---: |
| NestedManualTls | 35.325 +/- 0.382 | 35.015 +/- 0.178 | 35.285 +/- 0.228 | 0.991 | 0.992 |
| NestedScoped | 33.746 +/- 0.796 | 34.513 +/- 0.441 | 33.567 +/- 0.589 | 1.023 | 1.028 |
| Scoped | 11.073 +/- 0.040 | 11.202 +/- 0.294 | 11.071 +/- 0.040 | 1.012 | 1.012 |
| SingleContext | 93.013 +/- 2.028 | 90.993 +/- 1.369 | 93.229 +/- 2.245 | 0.978 | 0.976 |

The large nested-TLS anomaly does not repeat. Intel scoped results do not confirm the AMD gain. Nested-scoped means are 2.3-2.8% higher; the individual 99.9% intervals overlap and launch means vary substantially (baseline A: 34.768/32.256/34.379 ns; candidate: 34.473/35.322/33.820 ns; baseline C: 34.745/33.162/32.838 ns). This is mixed performance evidence, not proof of equality or a universal improvement. These results must remain visible when deciding whether to merge.

### .NET 8 and netstandard2.0

The .NET 8 comparisons do not establish a guarded-path speedup or regression. Portable manual TLS, scoped, and shared cases have overlapping before/after intervals. Full measurements are retained:

- [.NET 8 with JIT capture](https://github.com/thomhurst/Reservoir/actions/runs/34785183760), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34785183760/artifacts/10325839488).
- [.NET 8 repetition](https://github.com/thomhurst/Reservoir/actions/runs/34785561004), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34785561004/artifacts/10326358469).
- [netstandard2.0 assembly on .NET 8](https://github.com/thomhurst/Reservoir/actions/runs/34785562498), [artifact](https://github.com/thomhurst/Reservoir/actions/runs/34785562498/artifacts/10326352070).

BDN reports 0 B/op and zero Gen0 collections throughout. One Medium candidate sample records 32 total bytes across 67,108,864 operations; the other samples report zero. The raw total is retained rather than presented as literally zero bytes in every diagnostic sample. No allocation was added to the successful reset path.

## Generated code and correctness

For `ObjectPoolBenchmarks.PayloadPolicy`, Tier1 guarded-reset code shrinks from 258 to 169 bytes on .NET 10 and from 293 to 195 bytes on .NET 8. Both remove the normal `push r15`/`pop r15` pair. Sizes include handlers and describe these instantiations, not all policies or the complete reachable code graph.

The Release solution build passes with zero warnings. All 594 modern and 300 portable TUnit tests pass, including `ResetFailureDiagnosticsTests` and lifecycle/concurrency coverage. [CI](https://github.com/thomhurst/Reservoir/actions/runs/34785187630) passes Linux x64/ARM64, Windows x64, NativeAOT, legacy smoke, and packaging. The website build passes with Node 24.14.1.

## Rejected TLS alternatives

The final change excludes the TLS experiments. Extracting the miss fallback (`32f81570ed744b7c095ad9e955aca2174ac92b0c`) regressed nested scopes by 11-13%. Joining the manual fallback through a boolean/out-value TLS take (`7a7e425f2424ce62284bb05d12fa51013818516a`) shrank callers and sometimes helped Kevlar, but the longer capacity-32 comparison regressed manual rent by 20.7% on .NET 10 and 3.2% on .NET 8. Returning a nullable TLS hit (`4e7412c754e1e3c8ee351a15fb21ca036bb729e9`) also regressed protected cases. None is proposed for shipping.

Relevant rejected measurements: [nested scopes](https://github.com/thomhurst/Reservoir/actions/runs/34782234108), [longer manual rent](https://github.com/thomhurst/Reservoir/actions/runs/34784382770), [nullable-hit .NET 8](https://github.com/thomhurst/Reservoir/actions/runs/34784755157), [TLS rewrite A-B-A repeat](https://github.com/thomhurst/Reservoir/actions/runs/34784374926).
