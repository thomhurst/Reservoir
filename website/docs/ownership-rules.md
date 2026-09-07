---
sidebar_position: 4
title: Ownership rules
description: The correctness rules for pooled objects and collections.
---

# Ownership rules

Pooling is an ownership protocol. Follow these rules for every Reservoir pool.

## Return transfers ownership

After `Return(obj)` completes, do not read, mutate, dispose, or return `obj` again. Another thread may rent the same reference immediately.

Never return an object twice. Never return an object to a pool that did not rent it.

## One active owner

Do not concurrently use an object while returning it. If work crosses an `await`, keep ownership until all operations using the object have completed, then return it in `finally`.

A `PooledLease` is stack-only and protects lexical synchronous scopes. Copies of a lease are safe
to dispose, but a stale copy cannot release a later rental that reused the lease state.
`RentScoped()` object-pool rentals return through a per-pool thread-local tier.
`RentScopedShared()` instead returns only to bounded shared storage while preserving the same
lease-copy protection. Its idle-object bound excludes outstanding rentals and ownership
bookkeeping; other modes can still retain thread-local objects in the same pool. Manual rentals use the same
thread-local tier only when constructed with `threadLocalFastPath: true` — a return then prefers
the returning thread's slot when that thread has rented from the pool, overflowing to the bounded
shared tier otherwise — so each thread that rents retains up to one object beyond
`MaximumRetained`, while a thread that only returns never parks one. By default, manual returns
go straight to the bounded shared tier.

Specialized collection and cancellation-token-source `Lease` values provide the same stale-copy
protection and return through a per-pool thread-local tier. They cannot cross an `await`; use
manual rental APIs for async ownership.

## Collections arrive empty

`ListPool`, `DictionaryPool`, `HashSetPool`, `QueuePool`, `StackPool`, and `StringBuilderPool` clear an item before retaining it. Every successful rental therefore starts empty.

Custom dictionary and hash-set pools also verify the configured comparer before retention. Do not mutate comparer identity through unsupported means.

`ThreadLocalShared` follows the same ownership rules. It stores an item on the thread that calls
`Return`, which may differ from the thread that rented it after an async continuation.

## Reset failure means destruction

When `TryReset` returns `false`, Reservoir calls `IPooledObjectPolicy<T>.Destroy` instead of retaining the object. Every netstandard2.0 policy must implement this method. Modern targets provide default `IDisposable.Dispose()` cleanup when applicable; an explicit or public implementation can supply custom cleanup. The optional `IPooledObjectDestroyPolicy<T>` marker inherits this same method.

If an unmarked policy's `TryReset` throws, Reservoir attempts destruction exactly once and never retains the object. When destruction succeeds, the original reset exception is rethrown. When destruction also throws, an `AggregateException` contains the original reset exception first and the destruction exception second, preserving both stack traces. A failed return still transfers ownership; do not retry it or destroy the object again.

A generic policy explicitly marked `INonThrowingResetPolicy` opts out of this guard. If its reset throws despite that contract, the exception propagates without destruction. Apply the marker only when reset cannot throw.

If a full pool cannot retain a return, the returned object is destroyed.
