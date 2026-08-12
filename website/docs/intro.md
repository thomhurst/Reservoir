---
sidebar_position: 1
slug: /intro
title: Reservoir
description: Bounded, thread-safe object pooling that compiles into your .NET application.
---

# Keep the performance. Lose the dependency.

Reservoir is a high-performance object-pooling toolkit for .NET. Warm `ObjectPool<T,TPolicy>` rent and return operations allocate **0 B**, retention is bounded, and every implementation ships as C# source that compiles into your assembly. Legacy collection fallbacks may trim or replace backing storage.

That distribution model changes the trade-off:

- **No runtime dependency.** No Reservoir DLL appears beside your application.
- **No version conflict.** Each project compiles the exact source selected by its package reference.
- **No hidden ownership.** Your assembly contains the code and the JIT optimizes it with the rest of your application.
- **No unbounded cache.** Every pool has an explicit maximum retained-object count.

Reservoir includes the general-purpose `ObjectPool<T,TPolicy>`, a convenient policy/factory overload, collection and `StringBuilder` pools, scoped leases, diagnostics, and a `CancellationTokenSource` pool.

## Requirements

- .NET Standard 2.0 or later
- C# 12.0 or later

## Choose a starting point

| Need | Start with |
| --- | --- |
| Reuse your own reference type | [`ObjectPool<T,TPolicy>`](api/object-pools.md) |
| Prefer a factory or policy object | [`ObjectPool<T>`](api/object-pools.md#factory-and-interface-policy-overload) |
| Reuse a common collection | [Collection pools](api/collection-pools.md) |
| Reuse `StringBuilder` | [`StringBuilderPool`](api/collection-pools.md#stringbuilder) |
| Reuse uncanceled timeout sources | [`CancellationTokenSourcePool`](api/cancellation-token-sources.md) |

Continue with [installation](installation.md), then complete the [quick start](quick-start.md).
