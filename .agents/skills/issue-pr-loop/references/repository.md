# Reservoir

Run before/after Release benchmarks through [Benchmark comparison](../../../../.github/workflows/benchmark-compare.yml) on GitHub Actions `ubuntu-latest`, with baseline and candidate sequentially in the same job on the same runner. Record both commit SHAs and link the run and artifacts. Local measurements are diagnostic only. This repository no longer requires the local Redis `performance` lock for benchmarks or local validation; retain PR/issue ownership locks.

Read [AGENTS.md](../../../../AGENTS.md) for validation and performance acceptance. Preserve zero-allocation warm Rent/Return paths and ownership/concurrency contracts; performance changes require repeatable before/after evidence. Use the project files and workflows for current framework/test coverage and the website build.

No Aspire AppHost. The queue's Redis lock container requires Docker; its namespace is `reservoir`.
