# Benchmark execution

Reservoir no longer requires the shared local Redis `performance` lock. Local restores, builds, tests, website builds, and other local work do not need a performance reservation. PR/issue ownership locks still apply.

Run performance acceptance benchmarks on GitHub Actions `ubuntu-latest` runners to avoid local machine noise. Use [Benchmark comparison](../.github/workflows/benchmark-compare.yml) with explicit baseline and candidate commit SHAs. Both sides must run sequentially in the same job on the same runner with identical benchmark code and configuration.

Follow [AGENTS.md](../AGENTS.md) for performance acceptance and reporting. Local measurements are diagnostic only; retain the workflow run URL, artifacts, commit SHAs, allocation results, and noise assessment as evidence. Repeat inconclusive comparisons because hosted runners can also experience noise.
