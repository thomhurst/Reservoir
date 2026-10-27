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
Scoped and manual rentals return to the same bounded store, so idle objects never exceed
`MaximumRetained`. The bound excludes outstanding rentals and lease ownership bookkeeping.

Specialized collection and cancellation-token-source `Lease` values provide the same stale-copy
protection. They cannot cross an `await`; use manual rental APIs for async ownership.

## Collections arrive empty

`ListPool`, `DictionaryPool`, `HashSetPool`, `QueuePool`, `StackPool`, and `StringBuilderPool` clear an item before retaining it. Every successful rental therefore starts empty.

Custom dictionary and hash-set pools also verify the configured comparer before retention. Do not mutate comparer identity through unsupported means.

## Reset failure means destruction

When `TryReset` returns `false`, Reservoir calls `IPooledObjectPolicy<T>.Destroy` instead of retaining the object. Every netstandard2.0 policy must implement this method. Modern targets provide default `IDisposable.Dispose()` cleanup when applicable; an explicit or public implementation can supply custom cleanup. The optional `IPooledObjectDestroyPolicy<T>` marker inherits this same method.

If an unmarked policy's `TryReset` throws, Reservoir attempts destruction exactly once and never retains the object. When destruction succeeds, the original reset exception is rethrown. When destruction also throws, an `AggregateException` contains the original reset exception first and the destruction exception second, preserving both stack traces. A failed return still transfers ownership; do not retry it or destroy the object again.

A generic policy explicitly marked `INonThrowingResetPolicy` opts out of this guard. If its reset throws despite that contract, the exception propagates without destruction. Apply the marker only when reset cannot throw.

If a full pool cannot retain a return, the returned object is destroyed.
