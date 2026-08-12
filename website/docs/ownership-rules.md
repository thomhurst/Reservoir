---
sidebar_position: 4
title: Ownership rules
description: The correctness rules for pooled objects and collections.
---

# Ownership rules

Pooling is an ownership protocol. Follow these rules for every Reservoir pool.

## Return transfers ownership

After `Return(obj)` completes, do not read, mutate, dispose, or return `obj` again. Another thread may rent the same reference immediately.

Never return an object twice. Never return an object to a pool that did not rent it. Debug diagnostics throw `InvalidOperationException` for both mistakes.

## One active owner

Do not concurrently use an object while returning it. If work crosses an `await`, keep ownership until all operations using the object have completed, then return it in `finally`.

A `PooledLease` is stack-only and protects lexical synchronous scopes. Copies of a lease are safe to dispose, but a stale copy cannot release a later rental that reused the lease state.

## Collections arrive empty

`ListPool`, `DictionaryPool`, `HashSetPool`, `QueuePool`, `StackPool`, and `StringBuilderPool` clear an item before retaining it. Every successful rental therefore starts empty.

Custom dictionary and hash-set pools also verify the configured comparer before retention. Do not mutate comparer identity through unsupported means.

## Reset failure means destruction

When `TryReset` returns `false`, Reservoir destroys the object instead of retaining it. The default destruction path calls `IDisposable.Dispose()` when applicable. For portable custom cleanup, implement `IPooledObjectDestroyPolicy<T>` and its `Destroy` method.

If `TryReset` throws, Reservoir destroys the object and rethrows the reset exception. If a full pool cannot retain a return, the returned object is destroyed.

## Debug leak reports

When `DEBUG` or `RESERVOIR_DIAGNOSTICS` is defined, Reservoir tracks outstanding rentals. If a rental becomes unreachable without being returned, it writes a `Trace` error containing the rent-site stack trace and raises `ObjectPoolDiagnostics.LeakDetected` on the finalizer thread.

Handlers must return quickly and must not throw. Leak detection depends on garbage collection and is a diagnostic signal, not deterministic resource cleanup.
