# Runtime coverage

CI runs both TUnit suites on Windows x64, Linux x64, and Linux ARM64 (`ubuntu-24.04-arm`). The modern suite runs against the net8.0 and net10.0 assets. This includes concurrent rent/return/clear/dispose, scoped/manual TLS ownership, stale leases, cancellation sources, and tracked-tier collection tests. ARM64 exercises the tracked tier's interlocked take instead of the x86/x64 asymmetric-clear path.

`Reservoir.NetStandard.Tests` remains a full TUnit suite consuming the netstandard2.0 asset on .NET 8. It validates that asset on a modern runtime, including runtime API discovery; it does not emulate older runtime collection layouts.

`Reservoir.Legacy.Tests` is a standalone net48 consumer forced to reference the netstandard2.0 assembly. The Windows job executes it on the runner's installed .NET Framework 4.8 family runtime with a two-minute step timeout. The executable verifies that it actually loaded .NET Framework and the netstandard2.0 Reservoir asset, and prints the runtime version.

The legacy smoke covers:

- Dictionary, HashSet, Queue, Stack, and List reset and reference reuse.
- Real .NET Framework private backing arrays when `EnsureCapacity` is absent. Small returned collections preserve capacity; oversized collections are rejected even after their Count becomes zero.
- Explicit custom destruction through generic and runtime-policy pools, plus scoped reset and clear.
- Missing `CancellationTokenSource.TryReset`: both manual and scoped returns permanently dispose sources, fresh rentals are not canceled, linked cancellation propagates, and returning a linked source unregisters the upstream callback.

This is selected compatibility coverage, not a claim that every .NET Standard 2.0-compatible runtime or patch has been tested. The legacy smoke is separate because the current TUnit runner targets modern .NET.

From the repository root on Windows:

```powershell
dotnet restore Reservoir.slnx
dotnet build Reservoir.slnx -c Release --no-restore
dotnet test tests/Reservoir.Tests/Reservoir.Tests.csproj -c Release --no-build
dotnet test tests/Reservoir.NetStandard.Tests/Reservoir.NetStandard.Tests.csproj -c Release --no-build
./tests/Reservoir.Legacy.Tests/bin/Release/net48/Reservoir.Legacy.Tests.exe
```

Linux builds the legacy consumer using reference assemblies but executes only the modern TUnit suites. CI's ARM64 job validates that its host is ARM64 and prints `dotnet --info` so architecture and runtime versions remain visible in the run logs.
