---
sidebar_position: 1
slug: /intro
title: Reservoir
description: Bounded, thread-safe object pooling for .NET applications and libraries.
---

# Keep the performance. Share the types.

Reservoir is a high-performance object-pooling toolkit for .NET. Warm `ObjectPool<T,TPolicy>`
operations allocate **0 B**, shared retention is bounded, and the conventional runtime package
works cleanly across application and library boundaries. Scoped fast paths can additionally retain
one object per participating thread. Legacy collection fallbacks may trim or replace backing
storage.

The package provides:

- **One public type identity.** Libraries can expose Reservoir types without embedding distinct copies.
- **Normal dependency flow.** NuGet resolves Reservoir transitively through `PackageReference` graphs.
- **Cross-assembly optimization.** The JIT can inline hot methods and specialize concrete struct policies.
- **Explicit shared-tier bounds.** Every pool has a maximum shared retained-object count. Scoped
  fast paths may additionally retain one object per participating thread.

Reservoir includes the general-purpose `ObjectPool<T,TPolicy>`, a convenient policy/factory overload, collection and `StringBuilder` pools, scoped leases, and a `CancellationTokenSource` pool.

## Requirements

- .NET Standard 2.0-compatible runtime or later

## Choose a starting point

| Need | Start with |
| --- | --- |
| Reuse your own reference type | [`ObjectPool<T,TPolicy>`](api/object-pools.md) |
| Prefer a factory or policy object | [`ObjectPool<T>`](api/object-pools.md#factory-and-interface-policy-overload) |
| Reuse a common collection | [Collection pools](api/collection-pools.md) |
| Reuse `StringBuilder` | [`StringBuilderPool`](api/collection-pools.md#stringbuilder) |
| Reuse uncanceled timeout sources | [`CancellationTokenSourcePool`](api/cancellation-token-sources.md) |

Continue with [installation](installation.md), then complete the [quick start](quick-start.md).
