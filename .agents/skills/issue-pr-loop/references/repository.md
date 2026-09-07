# Reservoir

Before heavy local work, follow [the shared performance lock workflow](../../../../scripts/PerformanceLock.md). Reserve `performance` through the current shared `C:/git/Dekaf/scripts/AgentLocks.ps1`, using `$performanceLocks` separately from this repository's `$agentLocks`. Acquire it after the item lock and release it first. Repository-local Redis namespaces do not provide cross-repository isolation.

Read [AGENTS.md](../../../../AGENTS.md) for validation and performance acceptance. Preserve zero-allocation warm Rent/Return paths and ownership/concurrency contracts; performance changes require repeatable before/after evidence. Use the project files and workflows for current framework/test coverage and the website build.

No Aspire AppHost. The queue's Redis lock container requires Docker; its namespace is `reservoir`.
