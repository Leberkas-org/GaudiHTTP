# Pooling Contract

Object pooling infrastructure for reusable per-connection and per-request objects. All pooled types in GaudiHTTP depend on these contracts.

Scope: `GaudiHTTP.Pooling` namespace only. Excludes .NET framework pools (`ArrayPool`, `MemoryPool`), servus.akka pools (`PooledArrayMemoryOwner`, `CrossThreadBufferPool`, `ObjectPool<T>`), and the internal state of specific pooled types.

## Requirements

### Requirement: IResetable contract

Every poolable object implements `IResetable`, which extends `IDisposable`. `Reset()` restores the object to a pristine, allocation-free state suitable for reuse. `OnRented()` is an optional lifecycle hook invoked after the object is retrieved from the pool.

#### Scenario: Reset clears all mutable state
- **WHEN** `Reset()` is called on an `IResetable` implementor
- **THEN** every mutable field and property is set to its default/initial value (null for references, zero for numerics, false for booleans, default for value types)

#### Scenario: Reset does not allocate
- **WHEN** `Reset()` is called
- **THEN** no new objects are allocated -- reset clears or nulls fields, it does not replace them with fresh instances

#### Scenario: Reset does not dispose owned resources
- **WHEN** a poolable object holds disposable sub-resources (buffers, CTS, streams)
- **THEN** `Reset()` nulls the reference but does NOT call `Dispose()` on sub-resources -- the caller is responsible for disposing sub-resources before returning the object to the pool

#### Scenario: OnRented is called after pool retrieval
- **WHEN** an object is retrieved from the pool via `Rent`
- **THEN** `OnRented()` is invoked before the object is handed to the caller

### Requirement: Poolable<TSelf> base class

`Poolable<TSelf>` provides self-return-on-Dispose semantics. Subclasses implement `OnReset()` to define their reset logic. Dispose returns the object to the singleton `ConnectionObjectPool.Instance`.

#### Scenario: Dispose returns object to pool
- **WHEN** `Dispose()` is called on a `Poolable<TSelf>` instance
- **THEN** the object is returned to `ConnectionObjectPool.Instance` via `Return<TSelf>()`

#### Scenario: Double-dispose is a safe no-op
- **WHEN** `Dispose()` is called a second time on the same instance
- **THEN** the call is silently ignored -- the object is NOT returned to the pool a second time, and `Reset()` is NOT called again

#### Scenario: Double-dispose guard uses Interlocked
- **WHEN** `Dispose()` is called from any thread (including finalizer, non-actor thread, or using-block on a different thread)
- **THEN** the `_returned` flag is toggled atomically via `Interlocked.Exchange`, making double-dispose safe regardless of calling thread

#### Scenario: OnRented resets the return guard
- **WHEN** `OnRented()` is called on a `Poolable<TSelf>` after retrieval from the pool
- **THEN** the `_returned` flag is cleared (atomically), re-enabling self-return on the next `Dispose()`

### Requirement: ConnectionObjectPool singleton lifecycle

`ConnectionObjectPool` is a process-wide singleton (`Instance`). It manages per-type `DefaultObjectPool<T>` instances keyed by `System.Type`, backed by `ConcurrentDictionary`.

#### Scenario: Per-type pool isolation
- **WHEN** objects of different types are rented and returned
- **THEN** each type has its own independent pool -- renting type A never returns an instance of type B

#### Scenario: Pool is keyed by closed generic type
- **WHEN** `Poolable<PumpSlot<int>>` and `Poolable<PumpSlot<string>>` are used
- **THEN** they map to separate pools because `typeof(PumpSlot<int>) != typeof(PumpSlot<string>)`

### Requirement: Rent contract

`Rent<T>(Func<T> factory)` retrieves an object from the pool or creates one via the factory. The factory is latched on first use and reused for all subsequent creations of that type.

#### Scenario: Rent returns a reset instance
- **WHEN** `Rent<T>(factory)` retrieves a previously returned object
- **THEN** the object has already had `Reset()` called (during `Return`) and `OnRented()` called (during `Rent`), so it is in a clean state

#### Scenario: Rent creates via factory when pool is empty
- **WHEN** the pool has no available instances of type T
- **THEN** the factory delegate is invoked to create a new instance

#### Scenario: Factory is latched, not per-call
- **WHEN** `Rent<T>` is called multiple times with different factory delegates
- **THEN** only the first non-null factory is stored -- subsequent factories are ignored (first-writer-wins via `??=`)

#### Scenario: Rent without prior factory registration throws
- **WHEN** the pool needs to create a new instance but no factory has been registered yet (no prior `Rent` call provided one)
- **THEN** `InvalidOperationException` is thrown

### Requirement: Return contract

`Return<T>(T obj)` places an object back into the pool after calling `Reset()`. Return does not require a factory -- the first pool interaction for a type may be a Return (a directly-constructed object disposing itself).

#### Scenario: Return calls Reset before pooling
- **WHEN** `Return(obj)` is called
- **THEN** the pool policy invokes `obj.Reset()` before the object becomes available for future `Rent` calls

#### Scenario: Return before any Rent does not throw
- **WHEN** a directly-constructed `Poolable<TSelf>` calls `Dispose()` (which calls `Return`) and no `Rent` has ever been called for that type
- **THEN** the Return succeeds -- a pool holder is created lazily with a null factory, and the object is stored for future reuse

#### Scenario: Return of a never-rented object is valid
- **WHEN** an object is constructed via `new` (not via `Rent`) and then disposed/returned
- **THEN** it enters the pool normally and can be retrieved by a subsequent `Rent` call

### Requirement: Pool capacity

The pool retains up to `maximumRetained` (256) instances per type. This is the `DefaultObjectPool<T>` retention limit.

#### Scenario: Excess returns are discarded
- **WHEN** more than 256 instances of the same type are returned to the pool
- **THEN** excess instances are silently dropped (not pooled) -- `DefaultObjectPool<T>` does not grow beyond its maximum

#### Scenario: Pool does not shrink
- **WHEN** pooled objects sit idle for an extended period
- **THEN** the pool retains them indefinitely -- there is no eviction, TTL, or shrink mechanism

### Requirement: Thread safety

`ConnectionObjectPool` is safe to call from any thread. This is required because Akka dispatcher threads hop across connections, and `Dispose()` on `Poolable<TSelf>` can fire from finalizers or arbitrary non-actor threads.

#### Scenario: Concurrent Rent and Return across threads
- **WHEN** multiple threads call `Rent` and `Return` for the same type concurrently
- **THEN** all operations complete without corruption -- `ConcurrentDictionary` guards pool-holder creation, and `DefaultObjectPool<T>` provides internal thread-safe storage

#### Scenario: Dispose from non-actor thread
- **WHEN** a `Poolable<TSelf>` is disposed from a finalizer, a `using` block on a thread-pool thread, or any thread other than the actor thread that rented it
- **THEN** the self-return completes safely due to `Interlocked` guarding `_returned` and `DefaultObjectPool<T>` thread safety

### Requirement: Pooled object state invariants

After a Rent-Return-Rent cycle, the object must be indistinguishable from a freshly created one (from the consumer's perspective).

#### Scenario: Clean-slate after reuse cycle
- **WHEN** an object is rented, mutated, returned, and rented again
- **THEN** all mutable state reflects the reset defaults -- the second renter sees no residual data from the first rental

#### Scenario: Owned sub-resources must be disposed before return
- **WHEN** a pooled object owns disposable resources (buffers, linked CTS, streams)
- **THEN** the caller MUST dispose those resources before returning/disposing the pooled object, because `Reset()` only nulls references -- it does not chase and dispose sub-resources

### Requirement: CachedSegmentMemoryPool single-tenant caching

`CachedSegmentMemoryPool` is a `MemoryPool<byte>` that caches exactly one byte array for reuse. It is designed for single-threaded, single-outstanding-rent scenarios (one buffer rented at a time per pool instance).

#### Scenario: Cache hit reuses the same array
- **WHEN** a buffer is rented, disposed, and a new buffer of equal or smaller size is rented
- **THEN** the cached array is returned without allocation

#### Scenario: Cache miss allocates a new array
- **WHEN** the cached array is null (first rent) or too small for the requested size
- **THEN** a new `byte[]` is allocated with `Math.Max(requestedSize, 4096)` minimum

#### Scenario: Only one buffer is cached
- **WHEN** a buffer is disposed back to the pool
- **THEN** its backing array replaces the cached array -- only the most recently returned array is retained

#### Scenario: Concurrent rent is not supported
- **WHEN** `Rent` is called while a previous rental is still outstanding
- **THEN** a new array is allocated (the cache is bypassed) -- the pool does NOT track multiple outstanding rentals, because it is designed for single-tenant use

#### Scenario: Dispose of CachedOwner is double-dispose safe
- **WHEN** `Dispose()` is called twice on the same `CachedOwner`
- **THEN** the second call is a no-op -- `Interlocked.Exchange` on the backing array reference ensures the array is returned to the cache exactly once

#### Scenario: Pool dispose releases the cache
- **WHEN** `CachedSegmentMemoryPool.Dispose()` is called
- **THEN** the cached array reference is set to null, allowing GC to collect it
