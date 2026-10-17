# Hayate Object Pool

A high-performance, feature-rich object pool implementation for .NET, designed to optimize resource management and reduce garbage collection overhead.

## Features

- **High Performance Core**: Optimized for both single-threaded and multi-threaded scenarios.
- **Sharding Support**: Reduces lock contention through sharded architecture.
- **Configuration Presets**: `HayatePoolPreset` names nine ready-made configurations — `Default`, `Lean`, `Full`, `HighThroughput`, `LowLatency`, `MemoryConstrained`, `ConnectionPool`, `BatchProcessing`, `Deterministic` — and they stay composable: a preset owns its field set, and a later `With*` call overrules it.
- **Borrow Order**: `Fifo` (default), `Lifo`, `Lru` or caller-ranked `Priority` hand-out order from the idle list, selected with `HayateBorrowStrategy`.
- **Soft Capacity**: Bounds the pool's *retained* set on the return path — returns past the ceiling are disposed instead of stored, while the pool still lends out its whole hard ceiling.
- **Bucketed Buffer Pool**: Optional `HayateBufferPool<T>` — a fixed ladder of size buckets with round-up-to-nearest-size routing, for workloads that rent arrays of a few known sizes.
- **Auto-Scaling**: Dynamically adjusts pool size based on usage patterns (aggressive scale-up / conservative scale-down steps).
- **Object Validation**: Validates objects on borrow, return, and while idle.
- **Lifecycle Observation**: An optional `IHayateObjectLifecycleObserver<T>` tells a policy about every object the engine destroys, with the `HayateDestroyReason` it went — so external bookkeeping stays true without a log line to parse.
- **Eviction Policy**: Automatically removes expired or idle objects on a background run, under a rule you can replace (`IHayateEvictionPolicy<T>`).
- **Generation Optimization**: Reduces validation frequency for long-lived objects.
- **Leak Detection**: Tracks and reports potential object leaks; stack-trace capture is opt-in (`Off` / `Sampled` / `EveryAcquire`) since 2.1.
- **Abandoned Recovery**: Optional reclamation of borrowed objects never returned past `RemoveAbandonedTimeout` (CHOPIN `AbandonedConfig` semantics; destructive, off by default).
- **Metrics & Monitoring**: Comprehensive statistics and snapshot capabilities, plus an `EnableDiagnostics` master switch that takes the whole bookkeeping surface (counters, timing stats, metrics sink, per-operation trace) off the borrow and return paths in one call.
- **Multiple Reject Policies**: `Abort`, `Block`, `BlockTimeout`, `CreateNew`, and `CreateOnDemand`.
- **Dependency Injection Integration**: Seamless integration with `Microsoft.Extensions.DependencyInjection`.
- **ASP.NET Core Endpoints**: Built-in management endpoints for monitoring and configuration.
- **Health Checks**: ASP.NET Core health check integration to monitor pool status in production.
- **Diagnostics**: `System.Diagnostics` bridge for advanced performance monitoring and telemetry.
- **Configuration Binding**: Load pool settings from `appsettings.json` (global + per-pool).
- **Cross-Version Compatibility**: Supports .NET Framework 4.8 (core, ObjectPoolCompat, DependencyInjection, Specialized and Buffers packages) and .NET 6/7/8/9/10 (all packages).

Release notes live in [`CHANGELOG.md`](CHANGELOG.md); breaking changes and behavioural notes are
documented in [`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md).

Two shorter entry points: [`docs/ownership.md`](docs/ownership.md) states the return contract — what
returning an object transfers, and which misuses the engine actually detects — and
[`docs/ai-assistant-guide.md`](docs/ai-assistant-guide.md) is a single-page orientation for AI coding
assistants: the shape decision, the canonical skeleton, the traps, and the facts that are easy to get
wrong.

Everything outside the core lives in an optional package: DI, configuration, health checks, management
endpoints, diagnostics, the specialized pools, the bucketed buffer pool, and the Roslyn analyzers
([`docs/analyzers.md`](docs/analyzers.md), which also covers why exactly one of its rules is an Error).

## Installation

### NuGet Packages

| Package                                                | Description                                                                                          |
| :----------------------------------------------------- | :--------------------------------------------------------------------------------------------------- |
| `DotNetCore.HayateOP`                                | Core library with basic object pool functionality.                                                   |
| `DotNetCore.HayateOP.Extensions.DependencyInjection` | Seamless integration with Microsoft.Extensions.DependencyInjection.                                  |
| `DotNetCore.HayateOP.Extensions.Configuration`       | Integration with Microsoft.Extensions.Configuration for loading pool settings from appsettings.json. |
| `DotNetCore.HayateOP.Extensions.HealthCheck`         | Health Checks integration (`Microsoft.Extensions.Diagnostics.HealthChecks`) to monitor pool status.  |
| `DotNetCore.HayateOP.Extensions.Endpoints`           | Built-in management endpoints for monitoring and configuration.                                      |
| `DotNetCore.HayateOP.Extensions.Diagnostics`         | Integration with System.Diagnostics for advanced performance monitoring and telemetry.               |
| `DotNetCore.HayateOP.Extensions.Specialized`         | Ready-to-use specialized pools for `MemoryStream`, `StringBuilder` and `CancellationTokenSource`.    |
| `DotNetCore.HayateOP.Extensions.Buffers`             | Bucketed array pool (`HayateBufferPool<T>`) with round-up-to-nearest-size routing over declared buckets. |
| `DotNetCore.HayateOP.Extensions.Analyzers`           | Roslyn analyzers and code fixes that move pool misuse from run time to build time.                              |

#### Supported Target Frameworks

| Package | Target Frameworks |
| :--- | :--- |
| `DotNetCore.HayateOP` | .NET Standard 2.0; .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.ObjectPoolCompat` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.DependencyInjection` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.Specialized` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.Buffers` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.Analyzers` | .NET Standard 2.0 (Roslyn host requirement; runs in the compiler, not in your app) |
| `DotNetCore.HayateOP.Extensions.Configuration` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| `DotNetCore.HayateOP.Extensions.HealthCheck` | .NET Framework 4.8; .NET 6/7/8/9/10 |
| All other `DotNetCore.HayateOP.Extensions.*` packages | .NET 6/7/8/9/10 |

> **.NET Framework 4.8 support (added in 2.4)** targets `net48` for classic ASP.NET 4.x / WPF / WinForms
> hosts that consume the core pool or the `ObjectPool<T>` / DI bridges. Prefer `PackageReference` and add a
> `bindingRedirect` when the host already loads `Microsoft.Extensions.*` (6.x/8.x/10.x) assemblies.
> The Specialized package's net48 asset additionally depends on `System.Memory` (4.5.5), which backs the
> `Span<char>` surface of `HayateValueStringBuilder`; it is that package's only net48-only NuGet
> dependency and the core library stays free of it (added in the 2.8 window).
> **.NET Standard 2.0 support (added in 2.5)** ships for the **core package only** (`DotNetCore.HayateOP`),
> targeting Unity / Xamarin / older .NET hosts that cannot consume `net6.0`+ or `net48` assets. On
> `netstandard2.0` / `net48`, allocation tracking reports `0` (no `GC.GetAllocatedBytesForCurrentThread`)
> and `Math.Clamp` falls back to an equivalent `Max/Min` composition. The asset was reviewed and
> **retained for 3.0**: the core library contains no `netstandard2.0`-only conditional block (its two
> `NETSTANDARD` gates are shared with `net48`, and the remaining 41 are `net6.0+` feature gates or
> `DEBUG`), so the target costs one compile leg and nothing else, while dropping it would remove a
> package asset — a breaking change with no matching benefit.
> **Unity / IL2CPP guidance**: the core package is a single `netstandard2.0` assembly with no external
> dependencies, which is the shape Unity's IL2CPP AOT pipeline consumes directly — the core borrow /
> return path uses no reflection, no `Expression` compilation and no dynamic code generation. For AOT
> hosts the `Deterministic` preset is the recommended configuration: it runs the lean fast path with
> every background activity closed (no timer, no process-exit subscription, no pre-warm task), so the
> pool does nothing an explicit call did not ask for. Empirical IL2CPP AOT / trimming validation of a
> real Unity project is tracked as backlog for the 2.8 window's follow-ups; until that lands, treat the
> compatibility claim as structural (assembly shape + coding style) rather than certified.
> `Endpoints`, `OpenTelemetry` and `Diagnostics` do not ship `net48` or `netstandard2.0` assets —
> `Endpoints` because it is ASP.NET Core–based, `OpenTelemetry` because the SDK's .NET Framework support
> is narrow, `Diagnostics` by design. `Configuration` and `HealthCheck` gained a `net48` asset in 2.9.

### .NET CLI

```bash
# Core package
dotnet add package DotNetCore.HayateOP

# Extensions
dotnet add package DotNetCore.HayateOP.Extensions.DependencyInjection
dotnet add package DotNetCore.HayateOP.Extensions.Configuration
dotnet add package DotNetCore.HayateOP.Extensions.HealthCheck
dotnet add package DotNetCore.HayateOP.Extensions.Endpoints
dotnet add package DotNetCore.HayateOP.Extensions.Diagnostics
dotnet add package DotNetCore.HayateOP.Extensions.Specialized
```

## Quick Start

### Basic Usage

```csharp
using DotNetCore.HayateOP;

// Create a simple pooled object
public class MyPooledObject : IHayateResettable
{
    public int Data { get; set; }

    public void Reset()
    {
        Data = 0;
    }
}

// Create and configure the pool
var pool = new HayatePoolBuilder<MyPooledObject>()
    .WithMinSize(10)
    .WithMaxSize(100)
    .Build();

// Acquire an object from the pool
var obj = pool.Acquire();
obj.Data = 42;

// Use the object...

// Return the object to the pool (ALWAYS release in a finally block)
pool.Release(obj);

// Dispose the pool when done
pool.Dispose();
```

### One-call pools

`HayatePool.Simple` builds a pool from nothing more than a size and a factory — no options object, no
policy class. The size you pass is the ceiling, not something to fill eagerly: nothing is created until
the first borrow, and a miss inside that size creates another object instead of waiting for a return, so
"a pool of n objects" is what the call gives you. At the size itself the borrow waits for a return and
then creates one anyway, which is how the create-on-miss policies have always behaved. Everything else
keeps its documented default.

```csharp
using DotNetCore.HayateOP;

using var pool = HayatePool.Simple<MyPooledObject>(32, () => new MyPooledObject());

// Optional third argument: a callback run on every borrow (e.g. reopening a connection).
using var connections = HayatePool.Simple<MyConnection>(8, () => new MyConnection(cs), c => c.Open());
```

When you need anything tuned — a minimum idle count, an interval, a single feature switch — reach for
`HayatePoolBuilder<T>` instead; nothing set by the shortcut is hidden afterwards.

### Shared pools

`HayatePool.Shared<T>()` returns *the* pool for an element type: the first caller builds it, every caller
after that gets the same instance. Reach for it when the application needs a pool for a type and does not
care who created it — one type, one pool, rather than a pool per call site.

```csharp
using DotNetCore.HayateOP;

// Anywhere in the application, the same instance comes back.
var pool = HayatePool.Shared<MyBuffer>();
using var lease = pool.AcquireScoped();
lease.Value.Write(payload);
```

The pool is an ordinary `IHayateObjectPool<T>`, so it is configured like one — but it is configured once,
by the call that creates it:

```csharp
// At start-up, when the shape is known.
var pool = HayatePool.Shared<MyBuffer>(o =>
{
    o.MaxPoolSize = 4096;
    o.MinPoolSize = 64;
});

// A later call with a different callback returns the pool above and ignores that callback, so check
// first when the shape matters:
if (!HayateSharedPoolRegistry.Default.TryGet<MyBuffer>(out _))
    throw new InvalidOperationException("MyBuffer's shared pool was never configured.");
```

The catalog behind it, `HayateSharedPoolRegistry`, has a process-wide `Default` instance and can be
instantiated as many times as you like, each with a registry of its own:

| Call | What it does |
| :--- | :--- |
| `HayatePool.Shared<T>()` | The process-wide pool for `T`, created on first use |
| `HayatePool.Shared<T>(configure)` | The same pool, configured while it is created |
| `GetOrCreate<T>()` / `GetOrCreate<T>(configure)` | The same two calls on a catalog you own |
| `GetOrCreateNamed<T>(name, configure)` | A named shared pool, for several per element type |
| `TryGet<T>(out pool)` / `TryGet<T>(name, out pool)` | Resolve without creating anything |
| `Remove<T>(name)` | Unregister the pool **and dispose it** |
| `Clear()` / `Dispose()` | Release every pool the catalog created |
| `Registry` / `Count` | The underlying `IHayateObjectPoolRegistry`, and the number of pools held |

The catalog is a thin layer over `IHayateObjectPoolRegistry` rather than a second registry, which makes
shared pools first-class on the management surface: `Registry.GetAll()` lists them with their metadata,
and a catalog built on the application's registry — the same one the DI packages register — puts them in
front of the management endpoints, metrics and diagnostics alongside the DI-created pools.

```csharp
using DotNetCore.HayateOP;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHayatePoolSupport().RegisterHayatePool<MyBuffer>(o => o.MaxPoolSize = 100);

var provider = services.BuildServiceProvider();
var registry = provider.GetRequiredService<IHayateObjectPoolRegistry>();

var catalog = new HayateSharedPoolRegistry(registry);
catalog.GetOrCreate<MyBuffer>(o => o.MaxPoolSize = 512);

// The registry now enumerates the DI pool and the shared pool together.
foreach (var meta in registry.GetAll())
    Console.WriteLine($"{meta.PoolName}: {meta.ElementType}");
```

Ownership runs one way. The catalog owns the pools it creates, so `Remove`, `Clear` and `Dispose` dispose
them — the opposite of `IHayateObjectPoolRegistry.Remove`, which only unregisters — while a pool the
catalog did not create is left untouched, which is what lets it share a registry with DI. Two rules follow
for callers: **do not dispose a shared pool you were handed** (the catalog, not the borrower, decides when
it goes away, so `using var pool = HayatePool.Shared<T>()` would tear down a pool other callers are still
borrowing from), and treat `Remove` and `Clear` as shutdown and test-reconfiguration operations rather
than something to call under load.

### Configuration presets

`HayatePoolPreset` names the configurations the pool is most often asked to take, so a common shape costs
one word instead of forty settings. A preset is a starting point rather than a lock: it assigns the
options it owns, leaves every other option at the value it already had, and a later `With*` call still
overrules it.

```csharp
using DotNetCore.HayateOP;

using var pool = new HayatePoolBuilder<MyConnection>()
    .WithPreset(HayatePoolPreset.ConnectionPool)
    .WithMaxSize(32)                     // sizing is yours: the preset does not own it
    .Build();

// Without a builder, or on an options object you already have:
var options = HayatePoolPresets.Create(HayatePoolPreset.HighThroughput);
new HayatePoolOptions().UsePreset(HayatePoolPreset.MemoryConstrained);
```

| Preset | Reach for it when | What it changes | What to know |
| :--- | :--- | :--- | :--- |
| `Default` | You want the shipped behaviour, or an explicit reset | The whole option surface, restored from the shipped defaults | The only preset that owns sizing, timeouts, the reject policy and the callbacks — it clears them |
| `Lean` | A pure borrow/return workload on a hot path | The wrapper-free fast path: no sharding, no background pass, no per-object bookkeeping | The fast path has nowhere to keep per-object state, so the feature switches are forced off and any borrow order but the default `Fifo` fails the build |
| `Full` | Bringing a pool up, or diagnosing one | Every optional feature switch on: sharding, auto-scaling, validation, eviction, generation optimization, leak detection, metrics, allocation tracking | Nothing validates until you ask it to, and the numeric thresholds keep their shipped values |
| `HighThroughput` | A busy pool where creation is the expensive part | Scale up below the shipped threshold in steps of eight, scale down slowly, retain objects for 15 idle minutes, optional bookkeeping off | Leak forensics is off — the first switch to turn back on when an object goes missing |
| `LowLatency` | A request path where a background pass is a latency spike | No scaling, validation, eviction or generation pass and no optional bookkeeping, plus the first acquire waits for the warm floor | With eviction off nothing is ever retired: pair it with `WithValidateOnBorrow` or a `MaxLifeTime` where a stale object is a real risk |
| `MemoryConstrained` | A footprint that has to stay small | One shard, a zero idle floor, quick reclamation (1 minute idle, 10-second scan), diagnostics off | The zero floor arrives with `CreateOnDemand`: with a block policy the second borrower would wait out the whole acquire timeout |
| `ConnectionPool` | Expensive, finite external handles | Bounded capacity, `ValidateOnBorrow` plus an idle validation sweep, recycling at 30 minutes, abandoned-handle reclaim at 60 seconds, circuit breaker, metrics | Validation only does something for a pooled type that implements `IHayateValidatable`; for any other type it reports healthy, so the preset is safe to apply regardless of the pooled type |
| `BatchProcessing` | Bursty bulk work that then drains | A one-second scaling reaction, scale-up at 60% in steps of ten, capacity released after 2 idle minutes, a 5-minute leak threshold | The leak warning is moved out of the way of a healthy run rather than switched off — a batch worker may legitimately hold its object for minutes |
| `Deterministic` | AOT / IL2CPP hosts and other deterministic environments, where nothing may run without the caller asking for it | The lean fast path, plus the two switches Lean leaves open that could still move work off the calling thread: pre-warming stays synchronous (`WaitForWarmup` off) and no process-exit subscription (`EnableAutoDisposeWithSystem` off) | The built pool touches nothing except on an explicit `Acquire` / `Release` — no timer, no event subscription, no background task. Every owned switch is overrulable: re-enabling eviction after the preset reintroduces a timer by choice |

An unrecognised preset value throws rather than being ignored, and `WithPreset(HayatePoolOptions)` applies a
preset of your own through the same path, so a configuration your team keeps in its own code composes
exactly like the catalogue does. `HayatePoolOptions.UsePreset(...)` is the same call on an options object,
and both preserve a value set afterwards — `WithPreset(X).WithMaxSize(256)` keeps X and changes only the
ceiling.

### Fluent Builder & Auto-Scaling

Every option exposed by `HayatePoolOptions` has a matching `With*` method on `HayatePoolBuilder<T>`.
Auto-scaling uses **independent** scale-up and scale-down steps, so you can be aggressive when growing
and conservative when shrinking:

```csharp
using DotNetCore.HayateOP;

var pool = new HayatePoolBuilder<MyPooledObject>()
    .WithMinSize(10)
    .WithMaxSize(100)
    .WithEnableSharding(true)
    .WithShardCount(4)
    .WithEnableAutoScaling(true)
    .WithScalingInterval(5000)          // decision cadence (ms)
    .WithScaleUpThreshold(0.8)         // grow when usage >= 80%
    .WithScaleDownThreshold(0.2)       // shrink when usage < 20%
    .WithScaleUpStep(10)               // objects added per scale-up
    .WithScaleDownStep(5)              // objects removed per scale-down
    .WithScaleUpCooldownSeconds(3)
    .WithScaleDownCooldownSeconds(15)
    .WithRejectPolicy(HayatePoolRejectPolicy.BlockTimeout)
    .WithAcquireTimeout(TimeSpan.FromSeconds(5))
    .Build();
```

> **Tip:** When auto-scaling is disabled, the pool never grows on its own (scaling callbacks
> and timeout-triggered scale-up are gated by `EnableAutoScaling`); your explicit `MaxPoolSize`
> is kept as the hard capacity cap. (Breaking change in 2.1: previously `MaxPoolSize` was
> forced to `MinPoolSize`, which silently collapsed capacity to 0 when `MinPoolSize = 0`.)
> When sharding is disabled, `ShardCount` is forced to `1`.

### Custom Object Policy

Implement `IHayateObjectPolicy<T>` to control object creation, validation, and lifecycle callbacks.
Note that **`OnRelease` returns a `bool`**: returning `false` tells the pool the object is bad and must
be destroyed instead of returned to the pool.

```csharp
using DotNetCore.HayateOP;
using DotNetCore.HayateOP.Policies;

public class CustomPooledObjectPolicy : IHayateObjectPolicy<MyCustomObject>
{
    public MyCustomObject Create()
    {
        return new MyCustomObject
        {
            CreatedAt = DateTime.UtcNow,
            InstanceId = Guid.NewGuid()
        };
    }

    public bool Validate(MyCustomObject obj)
    {
        return obj != null && !obj.IsDisposed;
    }

    public void OnAcquire(MyCustomObject obj)
    {
        obj.LastAcquiredAt = DateTime.UtcNow;
        obj.AcquireCount++;
    }

    public void OnPassivate(MyCustomObject obj)
    {
        obj.Reset();
    }

    public bool OnRelease(MyCustomObject obj)
    {
        // Return false to reject the object (it will be destroyed).
        // Return true to return it to the pool.
        return !obj.IsDisposed;
    }

    public void OnDestroy(MyCustomObject obj)
    {
        obj.Dispose();
    }
}

// Use the custom policy
var pool = new HayatePoolBuilder<MyCustomObject>()
    .WithPolicy(new CustomPooledObjectPolicy())
    .WithMinSize(10)
    .WithMaxSize(100)
    .Build();
```

When the policy is only those three hooks — build it, reset it, decide whether to keep it — the
builder takes them as lambdas instead of a class, and every other option on the chain still applies:

```csharp
var pool = new HayatePoolBuilder<MyConnection>()
    .WithFactory(() => new MyConnection(connectionString))
    .WithReset(c => c.ClearCommand())
    .WithReturnPredicate(c => !c.IsStale)   // false destroys the object instead of parking it
    .WithMinSize(4)
    .WithMaxSize(32)
    .Build();
```

`WithReset` is the passivate hook: it runs when a returned object is on its way back into the idle
state, before the return is accepted. `WithReturnPredicate` is the release hook: returning `false`
destroys the object rather than parking it, exactly as a policy class's `OnRelease` does. Both are
separate from the `ResetOnReturn` / `ResetOnBorrow` switches, which govern only the library default
policy's reset of an `IHayateResettable` object — a policy you describe, by class or by lambda, owns
its own hooks. Mixing the two spellings resolves by the last call on the chain: a later `WithPolicy`
replaces the lambdas, and a later lambda replaces the policy. A chain that asks for a hook without a
factory fails at build time rather than at the first borrow.

### Hearing about destruction

A policy can also be told about every object the engine destroys, and why. Implement the optional
`IHayateObjectLifecycleObserver<T>` alongside `IHayateObjectPolicy<T>` — the same object, a second
interface — and the engine probes for it:

```csharp
public class AccountingPolicy : IHayateObjectPolicy<MyConnection>, IHayateObjectLifecycleObserver<MyConnection>
{
    // ... Create / Validate / OnAcquire / OnPassivate / OnRelease / OnDestroy ...

    public void OnEvict(MyConnection item, HayateDestroyReason reason)
    {
        _liveCount--;                              // every non-validation destroy lands here
        _logger.LogDebug("released {Id} ({Reason})", item.Id, reason);
    }

    public void OnValidationFailed(MyConnection item)
    {
        _liveCount--;                              // validation failures are the other destroy partition
        _quarantined.Add(item.Id);                 // the object never reached a caller
    }
}
```

`OnEvict` reports every destruction except validation failures; `OnValidationFailed` reports the one case
the object is rejected by `Validate` rather than retired, and `HayateDestroyReason.ValidationFailed` is
the only reason that is routed to it rather than to `OnEvict`. The two hooks partition the events —
nothing is reported twice, nothing is dropped. `HayateDestroyReason` names nine causes:

| Reason | What produced it |
| :--- | :--- |
| `ValidationFailed` | `Validate` said no on the borrow, return or idle-sweep path → `OnValidationFailed` |
| `Evicted` | the eviction policy retired it, or a manual `Evict` did |
| `CapacityOverflow` | the shard was full, the soft ceiling was reached, a pre-warm overflowed, or a scale-down grew past it |
| `ReturnRejected` | `OnRelease` refused the return |
| `PoolCleared` | a `Clear()` or `Dispose()` drain |
| `ScaledDown` | automatic scale-down removed it |
| `Abandoned` | leak recovery reclaimed an object that was never returned |
| `Rotated` | a lifetime rotation replaced it on hand-out |
| `Misuse` | a foreign object was returned, or the shard rejected a second return because it already held the wrapper |

A `Release(null)` is still a misuse of the call, but there is no object to destroy or pass to either
observer hook, so it produces no lifecycle notification.

Three things worth knowing before you rely on it:

* **It is a courtesy, not a gate.** The hook runs **before** the object is torn down, so you see a live
  value, and **exactly once** per object. Destructions of different objects can happen concurrently, so
  the observer must synchronize any shared state itself. An exception thrown from either hook is caught
  and logged rather than propagated, so a broken observer cannot abort a `Clear()` or a drain in progress —
  do not use it to *prevent* a destruction, only to observe one.
* **`Clear()` / `Dispose()` report, `OnDestroy` still does not.** A drain notifies the observer with
  `PoolCleared`, but it still does not call `IHayateObjectPolicy.OnDestroy` — that long-standing
  contract is unchanged, and [`docs/disposal.md`](docs/disposal.md#5-two-differences-to-be-aware-of) §5
  explains why the two are independent.
* **The lean path sees less.** Lean never calls `Validate`, so `OnValidationFailed` is unreachable
  there — not misreported, simply never raised. Lean reports only the reasons it can see: a refused
  return, a full buffer and a drain. If you need the full set, do not pool that type on the lean
  profile.

A policy that does not implement `IHayateObjectLifecycleObserver<T>` is unaffected, statement for
statement — this is a pure addition.

### Async Usage

```csharp
using DotNetCore.HayateOP;

var obj = await pool.AcquireAsync();
try
{
    // use the object
}
finally
{
    pool.Release(obj);
}
```

Since 2.4, `AcquireAsync` also accepts a timeout and a `CancellationToken` (mirroring the
synchronous `Acquire(TimeSpan)` semantics — timeout throws `TimeoutException`, external
cancellation propagates `TaskCanceledException`):

```csharp
// wait at most 5 seconds, but abort early if the token fires
var obj = await pool.AcquireAsync(TimeSpan.FromSeconds(5), cancellationToken);
```

### Non-throwing borrows

`TryAcquire` answers "is an object free right now?" without an exception for the answer. The
zero-budget form checks the pool's current state; the timeout form waits up to the budget you name.
Both report `false` for a refusal — an open circuit breaker, an `Abort` miss, a wait that runs out,
or a borrow stopped at the `MaxActiveObjects` ceiling — and hand the object back on success:

```csharp
if (pool.TryAcquire(out var obj))
{
    try
    {
        obj.Data = 42;
        // use the object
    }
    finally
    {
        pool.Release(obj);
    }
}
else
{
    // pool is busy — fall back, skip, or measure how often this happens
}

// or wait for up to 50 ms for a return
if (pool.TryAcquire(TimeSpan.FromMilliseconds(50), out var obj2))
{
    // ...
}
```

Two failures stay failures even here, because a try is a question about the pool's state and not a
promise to swallow bugs: a negative timeout throws `ArgumentOutOfRangeException` exactly as
`Acquire` does, and a factory that throws surfaces its exception, so a misconfigured pool cannot
pass silently as an empty one. One asymmetry with `Block` is deliberate: `Block` normally ignores
its timeout and returns only when an object is free, but in try mode the timeout is a hard cutoff —
the one thing a caller asking with a budget cannot mean by "try" is "maybe never".

### Misuse as a hard error

The return path is deliberately forgiving: a `null` return, an object from another pool and a second
return of an object the pool already holds are each logged, counted and handled rather than thrown at
the caller ([`docs/ownership.md`](docs/ownership.md) records the whole table). That is right for
production and wrong for a test that has just made one of those mistakes and wants to know now.
`ErrorOnIncorrectUsage` moves the three into the exception path:

```csharp
var pool = new HayatePoolBuilder<MyResource>()
    .WithFactory(() => new MyResource())
    .WithErrorOnIncorrectUsage()   // debug and test builds
    .Build();

var obj = pool.Acquire();
pool.Release(obj);
pool.Release(obj);   // InvalidOperationException: the pool already holds this object
```

Three facts decide when to use it. It costs on the return path — with the thread-local front slot on,
the already-returned check enumerates the slot tiers — so it belongs in debug and test builds, which
is what the original switch's documentation says too. A `ValidateOnReturn` failure and an `OnRelease`
rejection are verdicts about the object rather than mistakes by the caller, so they stay non-throwing
whatever the switch says. And lean mode detects only the `null` case, because its return path keeps no
ownership lookup: a foreign object is pooled there by contract. When a return is refused, the object
stays with the caller instead of being destroyed on its behalf.

### Scoped borrows

`AcquireScoped()` returns a lease that hands its object back when it is disposed, so a `using` block
replaces the manual `try`/`finally` and cannot forget the return — including when the body throws:

```csharp
using DotNetCore.HayateOP;

using (var lease = pool.AcquireScoped())
{
    lease.Value.Data = 42;
}   // returned here, even if the body threw

// Asynchronous equivalent
using (var lease = await pool.AcquireScopeAsync(cancellationToken))
{
    lease.Value.Data = 42;
}
```

The return happens exactly once: disposing a lease twice is a no-op, never a second return. One lease is
allocated per borrow — the price of never dropping a borrowed object on an exception path — so this is a
convenience for ordinary code rather than something to add to the hottest loop.

### Keyed pools

When objects are not interchangeable — a connection per tenant, a client per endpoint — give every key
its own pool with `ParameterizedHayatePool<TKey, TValue>`:

```csharp
using DotNetCore.HayateOP;

var pools = new ParameterizedHayatePool<string, Connection>(
    connectionString => new Connection(connectionString),
    maxSizePerKey: 8);

var connection = pools.GetObject("tenant-a");
try { connection.Query("..."); }
finally { pools.ReturnObject("tenant-a", connection); }
```

Each key gets an ordinary pool of its own, so objects are only ever reused by the key that created them,
and capacity, eviction, validation and the circuit breaker are all per key: one tenant exhausting its
eight connections does not slow down the next. Sub-pools are created on first use and disposed with the
keyed pool; `KeysInPoolCount` tells you how many exist, and `TryRemove(key)` retires one — worth knowing
if the key space is open-ended, because keys otherwise accumulate.

`getObject` needs the key to return an object, because a keyed pool cannot tell which sub-pool owns one:
the same object may exist under two keys, and searching every sub-pool would make returning cost more
than borrowing. `GetPool(key)` hands back the sub-pool itself when you want its statistics, a snapshot or
its own eviction, and every sub-pool is registered in the pool registry under `Name[key]`, so management
endpoints and metrics can address a single key.

### Unbounded pool (second pool model)

`HayateUnboundedPool<T>` is the ArrayPool-style counterpart to the bounded engine: borrowing never
blocks, never waits and never rejects — when the idle queue is empty the next borrow creates a new
object, whatever the momentary demand. The lease is **ownership**: returning is optional, and a
borrowed object that is never returned is simply collected by the garbage collector, so a forgotten
return cannot leak or block anyone. The only knob is `maxIdle` — how many objects the pool retains;
a return that arrives while that many are parked destroys the object instead, so memory does not
grow with peak demand:

```csharp
using DotNetCore.HayateOP;

var pool = new HayateUnboundedPool<MessageBuffer>(maxIdle: 64);

// Serves any burst: reuse when idle objects exist, creation when they do not.
var buffer = pool.Acquire();
try { /* use the buffer */ }
finally { pool.Release(buffer); }   // optional — not returning is fine too
```

It is a full `IHayateObjectPool<T>` (scoped borrows, stats, snapshots and the DI surface work
unchanged), implements the same reset/validate-on-return hooks as the bounded engine, and reports
`TotalDestroyed` for the returns that `MaxIdle` rejected. Choose it for transient buffers and
short-lived large objects under bursty demand; choose the bounded engine when capacity must be
gated against an external resource. `HayateUnboundedPool<T>` has no `MaxSize`, no timeouts, no
rejection policies and no background features — there is nothing to wait for and nothing to scale.

### Preparation / reconnect strategy

`IHayatePreparationStrategy<T>` (N3) closes the connection-pool gap: a strategy can check whether a
borrowed object is actually usable and repair it asynchronously before the caller sees it, which is
the classic reconnect flow for SMTP, database or broker connections. Install it with
`WithPreparation` — the returned `HayatePreparationPool<T>` wraps any `IHayateObjectPool<T>`
(bounded engine, lean, keyed sub-pool, or the unbounded pool):

```csharp
using DotNetCore.HayateOP;

var pool = new HayatePoolBuilder<SmtpConnection>()
    .WithMaxPoolSize(10)
    .Build()
    .WithPreparation(
        new SmtpReconnectStrategy(),      // IHayatePreparationStrategy<SmtpConnection>
        onDiscard: conn => conn.Dispose());

var conn = await pool.AcquireAsync();    // ready or repaired, or the reconnect error throws
```

Every borrow runs `IsReadyAsync`; a not-ready object goes through `PrepareAsync` (the reconnect).
A failed prepare hands the object to `onDiscard` and retries with another borrow (budget
`maxPrepareAttempts`, default 3), then throws `HayatePoolPreparationException` with the last
failure attached — a caller never receives a silently broken object. The asynchronous borrow awaits
the inner pool's own asynchronous borrow and forwards the timeout to it, so a borrow that has to wait
suspends the caller instead of occupying a thread, and `AcquireAsync(timeout, …)` converges on the
timeout it was given. The synchronous `Acquire` runs the identical chain by blocking (its `timeout`
overload forwards the timeout as well); the six policy hooks are unchanged.

### Specialized pools

The optional [`DotNetCore.HayateOP.Extensions.Specialized`](https://www.nuget.org/packages/DotNetCore.HayateOP.Extensions.Specialized)
package ships the ready-to-use `MemoryStream` / `StringBuilder` / `CancellationTokenSource` pools that
generic engines leave to you (the first two aligned with P89OP's `CodeProject.ObjectPool.Specialized`,
the third with Reservoir's `CancellationTokenSourcePool`):

```csharp
using DotNetCore.HayateOP.Specialized;

// A MemoryStream whose Dispose returns it to the pool — `using` is the whole borrow/return pair.
using var stream = MemoryStreamPool.Instance.GetObject();
stream.Write(payload, 0, payload.Length);   // empty and back in the pool after the block

// The same for string building; GetObject(string) starts from an initial value.
using var sb = StringBuilderPool.Instance.GetObject("order: ");
sb.StringBuilder.Append(42);
var text = sb.ToString();
```

Streams are created with 4KB and builders with 4096 characters; a returned object that grew past the
pool's maximum capacity (512KB by default, tunable through `MaximumMemoryStreamCapacity` /
`MaximumStringBuilderCapacity`) is destroyed on return instead of being parked, and returned objects are
reset (empty stream, cleared builder) before the next borrow. Each pool is an ordinary
`IHayateObjectPool<T>` underneath, so `AcquireAsync`, `GetStats` and friends work as usual, and each owns
its own lifetime — construct it, or use the `Instance` singleton and never dispose it.

**Declared-capacity borrows.** `GetObject(minCapacity)` lets the borrower state how much the object must
hold up front, so the pool reserves that capacity once instead of growing through the ladder on every
use, and mixed-size workloads stop churning through grow-past-the-maximum-and-destroy: sizes above the
minimum are served by lazily created internal capacity tiers (an exponential ladder anchored at the
minimum), a tier parks its builders/streams even above the pool's global maximum — the buffer exists by
declaration — and only growth beyond the declared envelope destroys an object:

```csharp
using var sb = StringBuilderPool.Instance.GetObject(200_000);   // capacity ≥ 200K chars, no grow ladder
```

**Convert and return.** `ToStringReturn()` / `ToByteArrayReturn()` produce the result and end the borrow
in one exactly-once call — the return runs even when the conversion throws, so nothing leaks:

```csharp
var sb = StringBuilderPool.Instance.GetObject();
sb.StringBuilder.Append("order: ").Append(42);
var text = sb.ToStringReturn();   // builder back in the pool here
byte[] bytes = stream.ToByteArrayReturn();
```

**Formatting helpers.** `StringBuilderPool.Format` / `Concat` / `Join` are generic, `params`-free
alternatives to `string.Format` / `string.Concat` / `string.Join` (ZString-style, up to eight
arguments): no `object[]`, no boxing of the arguments, one borrow/return cycle, current-culture
semantics matching `string.Format`.

**Zero-allocation value builder.** For the transient build-a-line-and-return-it flow,
`HayateValueStringBuilder` skips `StringBuilder` entirely: a `ref struct` whose buffer is rented from
`ArrayPool<char>`, with `ToString()` as the build's only allocation (the buffer goes back to the pool
in the same call — dispose-by-ToString). It has no pool around it, no statistics and no capacity
ceiling; long-lived builders that need observability or a capacity guard stay on
`StringBuilderPool`:

```csharp
using var sb = new HayateValueStringBuilder();   // ref struct — no heap, no async, no lambdas
sb.Append("order: ");
sb.Append(id);
var text = sb.ToString();                        // the one allocation; buffer returned here
```

`SharedStringBuilder` rounds the package out for the opposite use case: one `StringBuilder` shared by
everyone behind a lock, where the holder's `Dispose` clears it and releases the lock — zero per-use
allocations, at the price of serialising all users, so hold it briefly:

```csharp
var message = SharedStringBuilder.Build(b => b.Append("hello, ").Append(name));
```

**Cancellation-source pool.** `CancellationTokenSourcePool` covers the object asynchronous code pools
most often — the per-operation timeout or cancellation scope that would otherwise be allocated and
collected on every request — and it is the one specialized pool whose reuse depends on the runtime it
runs on. `CancellationTokenSource.TryReset()`, the only call that clears a used source's timer and
callbacks, does not exist on .NET Framework 4.8, so rather than fork its source per target framework
the pool locates the method at run time and reports the answer as `IsReuseSupported` (`true` on .NET 6.0
and later, `false` on .NET Framework 4.8). Where it is missing, every returned source is destroyed
instead of parked — the pool still hands out a correct source every time, it just creates a new one
rather than reusing:

```csharp
using var cts = CancellationTokenSourcePool.Instance.Rent();
cts.CancelAfter(TimeSpan.FromSeconds(5));
await httpClient.GetAsync(url, cts.Token);   // the source is back in the pool after the block

// A rental that follows the caller's token, for as long as the rental lives.
using var linked = CancellationTokenSourcePool.Instance.RentLinked(requestAborted);
await Task.Delay(TimeSpan.FromSeconds(30), linked.Token);
```

The rented instance is a `PooledCancellationTokenSource` — a `CancellationTokenSource` that knows its
pool, so `using` returns it instead of ending its life and a second dispose of the same rental is a
no-op. A canceled source, or one whose timer has already fired, is destroyed on return rather than
parked, because no reset can make it usable again.

### Bucketed buffer pool (declared size buckets)

The optional [`DotNetCore.HayateOP.Extensions.Buffers`](https://www.nuget.org/packages/DotNetCore.HayateOP.Extensions.Buffers)
package carries `HayateBufferPool<T>` — the one piece of surface the core pool deliberately does not
carry, because `System.Buffers.ArrayPool<T>` covers it for most workloads. Use it when you want that
shape under an explicit, inspectable configuration: the bucket ladder is declared up front, every
bucket's retained count is fixed by declaration, and nothing sizes or trims buckets behind your back.

```csharp
using DotNetCore.HayateOP.Buffers;

using var pool = new HayateBufferPool<byte>(
    new HayateSegmentDefinition(256, 4),      // arrays of exactly 256 elements, retain up to 4
    new HayateSegmentDefinition(1024, 2),
    new HayateSegmentDefinition(4096, 1));

using (PooledBuffer<byte> buffer = pool.Rent(300))   // routes to the 1024 bucket
{
    // buffer.Array.Length == 1024 — round up to the nearest declared size, never grow
}
```

Routing is "round up to the nearest declared size": a request is served by the smallest bucket whose
size covers it, and the returned array is exactly that bucket's size — at least what was asked, never
grown afterwards. A request above the largest declared size is rejected with `ArgumentException`
rather than served by an undeclared bucket; the ladder is exactly what was configured. Each bucket
creates arrays on demand and caps only what it *retains*: any number of buffers may be outstanding at
once, and a return past the bucket's capacity is dropped for the garbage collector. The pool is
thread-safe (one lock per bucket, so different sizes do not contend), runs no background workers, and
`Dispose` drops every parked array. The package ships `net48` and `net6.0`–`net10.0` assets and has no
dependencies.

### Analyzers and code fixes

The optional [`DotNetCore.HayateOP.Extensions.Analyzers`](https://www.nuget.org/packages/DotNetCore.HayateOP.Extensions.Analyzers)
package turns six classes of pool misuse into compiler diagnostics, so the mistake is caught where
the code is written rather than at the first GC pass.

```bash
dotnet add package DotNetCore.HayateOP.Extensions.Analyzers
```

| Rule | Default severity | Reports |
| :--- | :--- | :--- |
| `HYO009` | Error | a lease or scope whose result is thrown away — nothing will ever dispose it |
| `HYO001` | Warning | a pooled object acquired and dropped without being released |
| `HYO003` | Warning | an acquire on a pool that was disposed earlier in the same method body |
| `HYO004` | Info | a blocking `Acquire` inside an `async` method |
| `HYO005` | Info | an async acquire that ignores a `CancellationToken` the enclosing method has |
| `HYO006` | Warning | `OnRelease` disposing the item the pool still owns |

`HYO009` is the one that is an Error on purpose: `HayatePoolScope<T>` has no finalizer and
`HayatePoolLease<T>` is a `struct`, so a discarded lease is a permanent leak with no later chance to
notice — the compiler refusing to build is the honest signal. Everything else is a Warning or Info and
never blocks a build. Three code fixes ship with it — wrap the acquire in a `using`, switch to
`AcquireAsync`, forward the token — each offered only for the rule it belongs to.

Two things the package deliberately does not do: it never rewrites more than the call it was given,
and it never reports anything in a project that does not use the library (the rules key on the
namespace, not on the method name). Design, the reasons for each severity, and the known limits are in
[`docs/analyzers.md`](docs/analyzers.md).

### With Dependency Injection

```csharp
using DotNetCore.HayateOP;

var builder = WebApplication.CreateBuilder(args);

// Register Hayate Object Pool services
builder.Services.AddHayatePoolSupport()
       .RegisterHayatePool<MyPooledObject>(options =>
{
    options.MinPoolSize = 10;
    options.MaxPoolSize = 100;
    options.EnableValidation = true;
    options.EnableAutoScaling = true;
});

var app = builder.Build();

// Use the pool in your services
app.MapGet("/", (IHayateObjectPool<MyPooledObject> pool) =>
{
    var obj = pool.Acquire();
    try
    {
        // Use the object
        return Results.Ok("Object acquired successfully");
    }
    finally
    {
        pool.Release(obj);
    }
});

app.Run();
```

### Named pools and typed clients

One pool per type is the common case, but a host talking to a primary and a replica — or to one
backend per tenant — needs several independently sized pools of the same type. `AddNamedPool<T>`
declares one, `IHayateNamedPoolAccessor` resolves it by name, and `AddPool<T, TClient>` binds a client
class to a named pool so the binding is decided at registration rather than in the client's
constructor.

```csharp
builder.Services.AddHayatePoolSupport()
       .AddNamedPool<MyConnection>("primary", o => o.MaxPoolSize = 64)
       .AddNamedPool<MyConnection>("replica", o => o.MaxPoolSize = 16)
       .AddPool<MyConnection, PrimaryClient>("primary")
       .AddPool<MyConnection, ReplicaClient>("replica");

// Anywhere else:
var replica = provider.GetRequiredService<IHayateNamedPoolAccessor>().GetPool<MyConnection>("replica");
```

A named pool is registered under `MyConnection:primary` (see `HayateServiceKey`), so logs, statistics
and the management endpoints tell the pools apart; the unnamed `IHayateObjectPool<T>` registration
stays reserved for the pool registered with `RegisterHayatePool<T>()`. Pools are built on first use,
and resolution never creates one on demand — an unknown name throws instead of silently starting an
unconfigured pool. Named pools deliberately avoid Microsoft.Extensions.DependencyInjection keyed
services, which do not exist in the 6.0 / 7.0 abstractions this library targets, so the same code
behaves identically from net48 through net10.0.

Outside a container, `HayatePoolFactory` builds named pools at run time, for the cases where the set
of names is only known while the process is running:

```csharp
var factory = new HayatePoolFactory(new HayateObjectPoolRegistry());
var pool = factory.GetOrCreatePool<MyConnection>($"tenant-{id}", o => o.MaxPoolSize = 8);
```

### Configuration from `appsettings.json`

Bind global defaults and per-pool overrides directly from configuration. Global settings live under
`HayatePool:Global`; per-pool overrides live under `HayatePool:Pools:{TypeName}`.

```json
{
  "HayatePool": {
    "Global": {
      "MinPoolSize": 5,
      "MaxPoolSize": 50,
      "EnableMetrics": true,
      "ScalingIntervalMs": 5000,
      "ScaleUpThreshold": 0.8,
      "ScaleDownThreshold": 0.2
    },
    "Pools": {
      "MyPooledObject": {
        "MinPoolSize": 10,
        "MaxPoolSize": 100,
        "ShardCount": 4,
        "ScaleUpStep": 10,
        "ScaleDownStep": 5
      }
    }
  }
}
```

```csharp
using DotNetCore.HayateOP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHayatePoolSupport()
    .RegisterGlobalConfig(builder.Configuration)        // binds HayatePool:Global
    .RegisterHayatePool<MyPooledObject>(builder.Configuration); // binds HayatePool:Pools:MyPooledObject

var app = builder.Build();
app.Run();
```

### With Management Endpoints

```csharp
using DotNetCore.HayateOP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHayatePoolSupport()
                 .RegisterHayatePool<MyPooledObject>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Map Hayate Object Pool management endpoints
app.MapHayatePoolEndpoints();

app.Run();
```

**Available Endpoints**:

* `GET /hayateop` **- Pool overview**
* `GET /hayateop/{poolName}` **- Pool details**
* `POST /hayateop/{poolName}` **- Update pool configuration**
* `GET /hayateop/{poolName}/stats` **- Pool statistics**
* `POST /hayateop/{poolName}/clear` **- Clear all objects from pool**

> **Known limitation (tracked as T05):** the per-pool endpoints currently resolve `{poolName}` via
> reflection (`Type.GetType`), which requires an assembly-qualified type name and is fragile. For
> reliable, programmatic per-pool access, resolve `IHayateObjectPool<T>` through DI instead:
> `app.MapGet("/pools/{name}", (IHayateObjectPool<MyPooledObject> pool) => pool.GetStats());`

### Health Checks

```csharp
using DotNetCore.HayateOP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHayatePoolSupport()
    .RegisterHayatePool<MyPooledObject>()
    .RegisterHealthChecks<MyPooledObject>();   // adds HayateOpHealthCheck<MyPooledObject>

var app = builder.Build();

// Standard ASP.NET Core health endpoint backed by the pool health check.
//   Unhealthy — the pool's circuit breaker is open (every borrow is refused), or a
//               configured object health probe rejected a real pooled object.
//   Degraded  — every concurrent slot is busy; the pool works, the next borrower waits.
//   Healthy   — otherwise.
app.MapHealthChecks("/health");

app.Run();
```

The check reports on the pool's own counters, which is all most hosts need. When "the pool
has a free object" and "the object still works" are different questions — which, for anything
holding a network resource, they are — supply an object-level probe and the check will borrow a
real object and ask it:

```csharp
// The pool supplies the mechanism: borrow, ask, give the object back on every path.
// The implementation supplies the meaning, which for a connection pool is a PING.
public sealed class ConnectionProbe : IHayateObjectHealthProbe<MyConnection>
{
    public async Task<bool> IsHealthyAsync(MyConnection c, CancellationToken ct = default)
        => await c.PingAsync(ct);
}

builder.Services.AddHayatePoolSupport()
    .RegisterHayatePool<MyConnection>()
    .RegisterHealthChecks<MyConnection, ConnectionProbe>();
```

The probe is skipped, not waited for, when no slot is free — a health check that blocks is worse
than one that reports less.

### Diagnostics

```csharp
using DotNetCore.HayateOP;
using DotNetCore.HayateOP.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHayatePoolSupport()
    .RegisterHayatePool<MyPooledObject>();

// System.Diagnostics bridge for pool metrics (implements IHayateMetrics).
// AddHayatePoolSupport registers the no-op EmptyHayateMetrics via TryAdd,
// so adding HayateDiagnostics here replaces it as the active IHayateMetrics.
builder.Services.AddSingleton<IHayateMetrics, HayateDiagnostics>();

var app = builder.Build();
app.Run();
```

> The bridge is `HayateDiagnostics` (implements `IHayateMetrics`), which writes events to the
> `HayateOP` `DiagnosticListener` when `HayatePoolOptions.EnableMetrics` is on — and therefore only
> while the `HayatePoolOptions.EnableDiagnostics` master switch is also on. Subscribe via
> `DiagnosticListener.AllListeners` to capture `HayateOP.Object.Acquire`, `HayateOP.Object.OnRelease`,
> `HayateOP.Object.Miss`, and `HayateOp.Pool.Scaled` events. Alternatively, supply it to a
> single pool via the builder's `WithMetrics(...)`.

### Configuration Options

These are the **default** values of `HayatePoolOptions`. All of them are also configurable through the
fluent `HayatePoolBuilder<T>` API.

```csharp
var options = new HayatePoolOptions
{
    // Pool size
    MinPoolSize = 5,
    MaxPoolSize = 50,

    // Timeouts & reject policy
    DefaultAcquireTimeout = TimeSpan.FromSeconds(5),
    RejectPolicy = HayatePoolRejectPolicy.BlockTimeout,
    CreationRetryCount = 3,
    CreationRetryDelay = TimeSpan.FromMilliseconds(100),

    // Sharding
    EnableSharding = true,
    ShardCount = 4,

    // Auto-scaling
    EnableAutoScaling = true,
    ScalingIntervalMs = 5000,
    ScaleUpThreshold = 0.8,
    ScaleDownThreshold = 0.2,
    ScaleUpCooldownSeconds = 3,
    ScaleDownCooldownSeconds = 15,
    ScaleUpStep = 5,
    ScaleDownStep = 5,

    // Validation
    EnableValidation = true,
    ValidateOnBorrow = false,
    ValidateOnReturn = false,
    ValidateWhileIdle = false,
    ValidateIntervalMs = 30000,

    // Reset timing (3.1)
    ResetOnReturn = true,    // reset an IHayateResettable object on its way back into the pool
    ResetOnBorrow = false,   // ... or on its way out to a borrower instead; EnableLean forces this off

    // Eviction
    EnableEviction = true,
    MaxLifeTime = TimeSpan.FromMinutes(10),
    MaxIdleTime = TimeSpan.FromMinutes(5),
    SoftMinEvictableIdleTime = TimeSpan.FromMinutes(2),
    EvictionIntervalMs = 30000,
    NumTestsPerEvictionRun = 10,

    // Generation optimization
    EnableGenerationOptimization = true,
    GenerationThresholdMs = 30000,
    OldGenerationValidationInterval = 3,

    // Leak detection
    EnableLeakDetection = true,
    LeakDetectionThreshold = TimeSpan.FromMinutes(30),
    LeakTraceCaptureMode = HayateLeakTraceCaptureMode.Off, // Off (default) | Sampled | EveryAcquire
    LeakTraceSampleRate = 1024, // 1/N sampling, Sampled mode only

    // Abandoned recovery (K2; destructive opt-in, both toggles off by default)
    RemoveAbandonedOnBorrow = false,        // reclaim abandoned objects on the borrow path
    RemoveAbandonedOnMaintenance = false,   // reclaim abandoned objects on the background maintenance pass
    RemoveAbandonedTimeout = TimeSpan.FromSeconds(300), // abandoned judgment timeout (aligned with CHOPIN)
    LogAbandoned = false,                   // log a warning (with the lease trace) when reclaiming
    RemoveAbandonedIntervalMs = 30000,      // maintenance-pass cadence

    // Metrics
    EnableDiagnostics = true,   // master switch over the whole diagnostic surface
    EnableMetrics = false
};
```

### Hot-path cost of each feature switch

Every optional feature above buys something and costs something. Measured on .NET 10.0,
single-threaded `Acquire+Release` round trip, 250 pre-warmed objects, one run
(2026-09-10):

| Configuration | Mean | Allocated |
| :--- | ---: | ---: |
| `Microsoft.Extensions.ObjectPool` (reference) | 26.950 ns | 0 B |
| HayateOP lean fast path (`WithLean()`) | 27.063 ns | 0 B |
| HayateOP general engine, every feature off | 286.773 ns | 384 B |
| HayateOP general engine, sharding only | 285.857 ns | 384 B |
| HayateOP general engine, every feature on | 414.987 ns | 0 B |

The short version of how to trim:

| Switch | Where the cost lands |
| :--- | :--- |
| `EnableLean` | Removes the wrapper allocation, the shard lock, the return-side registry probe and every diagnostic write — about 10.6x against the same feature set |
| `EnableSharding` | One extra idle-list probe per shard on a miss; free in single-threaded steady state |
| `EnableLockFreeStorage` | Replaces the shard spin lock and its idle list with a slot array, so a take is one compare-exchange per slot scanned — no lock, no list walk, and no allocation. Off by default → one predicted branch. It gives up `BorrowStrategy` (any value but `Fifo` fails the build) and any `ReloadConfig` that would widen the ceiling |
| `EnableThreadLocalFrontSlot` | One hop before the shard scan, plus one atomic on each park and each hit to keep the pool's own idle count true. A hit never reaches a shard. Off by default → one predicted branch. Because a hit is served before the shard is asked, it gives up `BorrowStrategy` (any value but `Fifo` fails the build) |
| `BorrowStrategy` | `Fifo` (the default) reads the end the borrow path has always read — no added cost; `Lifo` costs one predicted branch on a constructor-time flag; `Lru` and `Priority` cost one walk of the idle list (plus one comparer call per node for `Priority`) |
| `EnableGenerationOptimization` | One extra `Stopwatch.GetTimestamp()` per borrow — the only switch that adds a QPC read to the borrow path |
| `ValidateOnBorrow` / `ValidateOnReturn` | One `IHayateObjectPolicy.Validate` call per borrow / return; the policy's own work (often a health probe) dominates |
| `ResetOnReturn` / `ResetOnBorrow` | One `IHayateResettable.Reset` call per return / per borrow, on the default policy only. Return-side is the default (before 3.1 it was unconditional); borrow-side is off by default and forced off by `EnableLean`. Moving the reset relocates it rather than adding one — measured with a 4 KB-clearing `Reset`, ~119 ns/op shifts from the return path to the borrow path (borrow 174 → 293 ns/op) and the round trip moves by 3%. The case for the borrow side is the object that is created and never borrowed again: it is then never reset at all |
| `LeakTraceCaptureMode` | The real cost of the leak-detection surface: `EveryAcquire` captures a call stack on every borrow; `Off` (the default) costs two branches |
| `RemoveAbandonedOnBorrow` | One bounded oldest-borrow scan per borrow when enabled; off by default → constant branch, zero cost |
| `RemoveAbandonedOnMaintenance` | Background only: drives the shared timer, never touches the borrow or return path |
| `WarnAtRatio` / `CriticalAtRatio` | The most expensive optional switch: when non-zero it walks every shard, taking one lock per shard, on **every** borrow and return |
| `EnableDiagnostics` | The master switch over the whole diagnostic surface. Off, it removes the `TotalAcquired` atomic add, the metrics block and the per-operation debug trace — the general-engine configuration with the least hot-path work short of the lean path, which forces it off |
| `EnableMetrics` | A min/max CAS loop, a metrics callback and a debug log per operation — and it is also what keeps `TotalCreated` / `TotalReleased` / `TotalMissed` alive |
| `EnableAllocationTracking` | Two `GC.GetAllocatedBytesForCurrentThread()` calls per operation; unavailable on `net48` / `netstandard2.0` |
| `EnableEviction`, `EnableAutoScaling`, `ValidateWhileIdle` | Background only: they drive the shared timer and never touch the borrow or return path. A pool with all of them off creates no timer at all |

The full per-switch reference — including which switches are pure background work, which
diagnostic members are paid for only when called, and a trim-by-scenario table — is in
[`docs/hot-path-costs.md`](docs/hot-path-costs.md). That page's §5 is the one to read *before*
choosing a pool at all: it states the break-even line (`Create()` must cost more than a round trip),
shows what a pool that charges a semaphore per borrow looks like when measured, and describes the
striped-lookup alternative for resources that handle concurrency themselves.

### Breaking changes

HayateOP follows Semantic Versioning. Breaking changes so far:

| Version | Change |
| :--- | :--- |
| 3.1 | `IHayateObjectPool<T>` gained `TryAcquire(out T)` and `TryAcquire(TimeSpan, out T)` — breaking for implementers only; callers are unaffected |
| 3.0 | `IHayateLogger` gained `LogTrace`, `LogCritical`, `IsEnabled` and `BeginScope` — breaking for implementers; `HayateLoggerBase` is the migration path |
| 2.5 | `HayateObject<T>.AcquireTrace` (string) replaced by `HayateLeaseContext`; new lifecycle fields (`CreatedAtTick`, `LeaseCount`, `OwnerPoolName`) |
| 2.4 | Idle pools actually scale down (the contradicting internal gate was removed) |
| 2.2 | `Build()` fails fast when custom metrics are registered while metrics are disabled |
| 2.1 | Leak stack-trace capture decoupled from leak detection and off by default |

Full migration guidance — plus the behavioural notes worth reading before adopting (cold boot, `CreateNew` laziness, lean mode's ~100 ms blocking wake-up granularity, the one-call factory's grow-on-a-miss policy) — lives in [`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md). A per-version summary of every change is in [`CHANGELOG.md`](CHANGELOG.md).

## Monitoring and Diagnostics

### Turning the diagnostic surface off

Every counter, timing statistic, metrics callback and per-operation debug trace the pool keeps is
bookkeeping on the borrow and return paths. `EnableDiagnostics` is the one switch that removes all of
it at once:

```csharp
// Fluent builder
var pool = new HayatePoolBuilder<MyPooledObject>()
    .WithPoolName("hot")
    .WithEnableDiagnostics(false)
    .Build();

// Options object (the equivalent)
var options = new HayatePoolOptions { EnableDiagnostics = false };
```

With diagnostics off, `Acquire` performs no counter write, reports nothing to `IHayateMetrics` and
emits no trace entry; `GetStats()` and `TakeSnapshot()` still work and report 0 for every cumulative
counter and timing statistic. Three boundaries are worth knowing:

* **It is a master switch, not a peer of `EnableMetrics`.** Switching it off normalizes
  `EnableMetrics` and `EnableAllocationTracking` off with it, and `GetOptions()` shows the collapsed
  state. This is deliberate — a pool that cannot record anything should not advertise a collection
  switch as on.
* **It is the only way to stop `TotalAcquired`.** That counter is normally written on every borrow
  whatever the metrics switch says, because it is the borrow-count contract callers verify
  borrow/return balance against. `EnableDiagnostics = false` is an explicit statement that the balance
  is not wanted either, so the counter stops with the rest. The full audit is in
  [`docs/metrics-gating.md`](docs/metrics-gating.md).
* **Lifecycle and problem logs are untouched.** `Information` / `Warning` / `Error` entries —
  construction, disposal, capacity alarms, prepare failures, abandoned reclaims — keep being written;
  only the per-operation `Debug` trace is suppressed. Likewise, counters owned by another switch (leak
  detection, the capacity alarm) keep following their own switch.

`RegisterHayatePool` (dependency injection) consults the master switch before attaching the custom
sink, so a DI user is never fast-failed by a configuration they did not write. Registering a sink
directly with `WithMetrics()` while diagnostics are off **does** fail at `Build()`, with a message
naming both switches — a sink with no surface to write to is a mistake in either configuration.

The lean fast path (`WithLean()`) forces `EnableDiagnostics` off, since that path keeps no
bookkeeping by construction, and `UseFullProfile()` re-opens it explicitly.

### Get current statistics

```csharp
var stats = pool.GetStats();

Console.WriteLine($"Pooled objects: {stats.PooledCount}");
Console.WriteLine($"Total created: {stats.TotalCreated}");
Console.WriteLine($"Total acquired: {stats.TotalAcquired}");
Console.WriteLine($"Total released: {stats.TotalReleased}");
Console.WriteLine($"Total missed: {stats.TotalMissed}");
Console.WriteLine($"Leak detected: {stats.LeakDetectedCount}");
```

### Take a snapshot for diagnostics

```csharp
var snapshot = pool.TakeSnapshot();

Console.WriteLine($"Snapshot taken at: {snapshot.Timestamp}");
Console.WriteLine($"Borrowed count: {snapshot.BorrowedCount}");
Console.WriteLine($"Leak count: {snapshot.LeakCount}");

foreach (var leakTrace in snapshot.LeakTraces)
{
    Console.WriteLine($"Leak trace: {leakTrace}");
}
```

### Inspect per-object metadata

A snapshot carries one row per live object, idle and borrowed alike. Each row is a point-in-time
copy for diagnostics — reading it does not borrow anything, and the rows carry no consistency
guarantee between themselves.

```csharp
var snapshot = pool.TakeSnapshot();

foreach (var detail in snapshot.ObjectDetails)
{
    // How many times this object has been handed out, and which thread took it last (0 = never borrowed).
    Console.WriteLine($"leases={detail.LeaseCount} lastThread={detail.LastGetThreadId}");

    // When the object was created. The field is raw ticks; wrap it to get an instant.
    var created = new DateTimeOffset(detail.CreatedAtTick, TimeSpan.Zero);
    Console.WriteLine($"created={created:O} borrowed={detail.IsBorrowed}");

    // The most recent lease duration, and the shard that owns the object.
    Console.WriteLine($"lastLeaseMs={detail.LeaseTimeMs} shard={detail.ShardIndex}");
}
```

`LastGetThreadId` is an id, not a handle: it tells you which thread borrowed the object last,
which is what you want when checking whether a pool is handing objects across threads (and so
whether state left on an object is visible elsewhere), or whether one thread is monopolising a
shard. The runtime recycles ids after a thread dies, so treat one as stable only while its
thread is alive. Objects served by the lean fast path carry no metadata at all — that path
stores the pooled value directly instead of wrapping it.

### Abandoned object recovery

Leak detection is forensics: it **counts** borrowed objects that stay out past
`LeakDetectionThreshold`, it never touches them. **Abandoned recovery** (CHOPIN's
`AbandonedConfig`) goes one step further — it **destroys** borrowed objects that are not returned
within `RemoveAbandonedTimeout` (default 300 s), reclaiming the capacity they silently hold. An
object is "abandoned" when a lease was borrowed and never released — typically a caller that lost
track of it.

Reclamation is **destructive and off by default**, because the pool cannot tell an abandoned
object from a legitimately long-lived lease: reclaiming destroys the value while a caller may
still hold the reference. Opt in explicitly when your leases are short and bounded:

```csharp
var pool = new HayatePoolBuilder<MyResource>()
    .WithRemoveAbandonedOnBorrow()            // reclaim on the borrow path
    .WithRemoveAbandonedOnMaintenance()       // and/or on the background maintenance pass
    .WithRemoveAbandonedTimeout(TimeSpan.FromMinutes(2))
    .WithLogAbandoned()                       // warn with the captured lease trace on reclaim
    .Build();
```

Semantics and costs:

- `RemoveAbandonedOnBorrow` runs a bounded, oldest-borrow-first scan on every borrow (the FIFO
  borrowed list guarantees the scan stops at the first lease not yet past the timeout, so the
  bounded budget is exact). `RemoveAbandonedOnMaintenance` drives the shared background timer at
  `RemoveAbandonedIntervalMs` (default 30 s) and reclaims everything past the timeout.
- A reclaimed object is destroyed (policy `OnDestroy` + `Dispose`), removed from the registry, and
  counted in `HayatePoolStats.AbandonedRemovedCount` — it is **not** also reported as a leak, so
  recovery and the forensics counters never double-flag the same object.
- Releasing an already-reclaimed object is treated as a foreign return (the pool's existing
  "object does not belong to this pool" path): it is disposed again and logged, and the pool keeps
  working. Reclamation is a documented opt-in contract, not a safety net.
- Both toggles are forced off in lean mode (no wrapper registry) and default to `false`, so the
  default configuration stays forensics-only — exactly the M4 behavior.

### Custom eviction policy

The background eviction run removes an idle object when the pool's rule says so. That rule is a
pluggable object: `IHayateEvictionPolicy<T>` is asked about each idle candidate, and its answer is
what the run acts on.

```csharp
// Retire a pooled connection after 1,000 leases, or on the usual time rules.
private sealed class RetireWellUsedConnections : IHayateEvictionPolicy<Connection>
{
    public bool ShouldEvict(in HayateEvictionCandidate<Connection> candidate)
        => candidate.LeaseCount >= 1_000
           || candidate.Age > candidate.MaxLifeTime
           || candidate.IdleTime > candidate.MaxIdleTime;
}

var pool = new HayatePoolBuilder<Connection>()
    .WithMaxLifeTime(TimeSpan.FromMinutes(30))
    .WithEvictionPolicy(new RetireWellUsedConnections())
    .Build();
```

A candidate carries the idle instance (`Item`), its `Age`, its `IdleTime`, its `LeaseCount`, its
shard's `IdleCount` and `MinIdleCount`, and the effective `MaxLifeTime` / `MaxIdleTime` /
`SoftMinEvictableIdleTime` — so a policy never reads `HayatePoolOptions` and stays correct across a
configuration reload. `MinIdleCount` is the shard's share of `MinPoolSize`
(`MinPoolSize / ShardCount`, integer division), which is exactly why the built-in soft-idle rule is
`IdleTime > SoftMinEvictableIdleTime && IdleCount > MinIdleCount`.

A pool without `WithEvictionPolicy` evaluates `HayateDefaultEvictionPolicy<T>.Instance` — the rule
above, and the rule the pool applied before this extension point existed, so installing that instance
explicitly is the same as installing nothing. The policy owns the decision: one that always returns
`false` keeps every idle object (the configuration for expensive objects that are safe to keep). The
explicit `Evict(reason)` API and abandoned-object recovery are unaffected, because neither goes
through the policy.

Only idle objects are offered. A borrowed object never reaches a policy, and no verdict can destroy
one under its borrower: after the verdict the shard re-checks ownership in its claim protocol, so an
object lent out between the test and the removal is skipped. Each shard contributes at most
`NumTestsPerEvictionRun` candidates per run, taken from the head of its idle list, and a custom policy
on a pool that never runs an eviction scan (`EnableEviction = false`, or the lean profile, which
disables idle eviction by construction) fails the build rather than silently never running.

### Object lifetime and borrow-side rotation

`MaxLifeTime` is the maximum lifetime of a pooled object from the moment it is created — idle or
borrowed. Which half of that meaning the pool enforces is a choice: the borrowed half is gated by
`EnableLifetimeRotationOnBorrow`, which is off by default, so the default is the backward-compatible
idle-side semantics — the eviction run and the manual `Evict` call evaluate this value against **idle**
objects only, and an object held by the application for longer than this value is never treated as
expired, which is the behaviour of every release before 2.9. With the switch on, this value also becomes
a ceiling on what the borrow path may hand out.

```csharp
var pool = new HayatePoolBuilder<Connection>()
    .WithMaxLifeTime(TimeSpan.FromMinutes(30))
    .WithEnableLifetimeRotationOnBorrow()   // opt-in; off by default
    .Build();
```

**Off** (the default) — only **idle** objects are ever retired for age, by the background eviction run or
by `Evict(Expired)`. A connection you borrow and then hold for an hour never ages, so a server-side
`max_connection_lifetime` or a middleware idle timeout can close it without the pool ever learning. That
is the case HikariCP's `maxLifetime` and SQLAlchemy's `pool_recycle` exist for.

**On**, a borrow that finds an idle object past it destroys that object and hands out a replacement
instead. The replacement is created through the same capacity-reservation path a create-on-miss uses, so
a lifetime event never degrades into a rejection, and at most one rotation happens per borrow — the
replacement is never re-checked, so a `MaxLifeTime` shorter than the time it takes to create an object
cannot spin. When the per-borrow budget is spent, the aged object is handed out anyway and the event is
logged: availability outranks lifetime. An object that is currently borrowed is not touched — the
rotation happens on the borrow path and never takes an object away from its borrower. `AcquireAsync`
applies the same rule, and `HayatePoolStats.LifetimeRotatedCount` /
`HayatePoolSnapshot.LifetimeRotatedCount` count the rotations.

The check is close to free. The borrow path already computes the object's age when
`EnableGenerationOptimization` is on, which is the default, and rotation reuses that computation; against
a pool without the switch it removes two clock reads and one floating-point conversion per borrow, so the
generational promotion itself gets faster and the added work is one integer comparison.

The lean fast path cannot honour the switch — it stores the pooled value directly in a bounded array and
keeps no per-object timestamps, so it has no age to compare. Every other feature lean cannot honour is
quietly switched off, because the mode wins; this one fails the build instead:

```csharp
// Throws InvalidOperationException: the lean fast path cannot evaluate an object's age.
new HayatePoolBuilder<Connection>().WithLean().WithEnableLifetimeRotationOnBorrow().Build();
```

Silently ignoring the switch is exactly the failure it exists to remove — an owner who believes objects
are rotated on hand-out while they are not.

### Borrow order (FIFO / LIFO / LRU / priority)

A borrow is served from the shard's idle list, and which object it takes is a choice:

```csharp
var pool = new HayatePoolBuilder<Connection>()
    .WithBorrowStrategy(HayateBorrowStrategy.Lifo)   // hand back the connection that came in last
    .Build();

var ranked = new HayatePoolBuilder<Job>()
    .WithBorrowPriority(Comparer<Job>.Create((a, b) => a.Priority.CompareTo(b.Priority)))
    .Build();                                        // hand back the highest-priority job; the call selects Priority
```

| Value | Serves | Costs on the borrow path |
| --- | --- | --- |
| `Fifo` (default) | the longest-idle object — the head of the idle list | nothing; this is what every release before the switch did |
| `Lifo` | the object that came back most recently — the tail | one predicted branch on a constructor-time flag |
| `Lru` | the object borrowed longest ago, by `LastBorrowedAt` | one walk of the idle list |
| `Priority` | the object the `WithBorrowPriority` comparer ranks highest | one walk of the idle list, plus one comparer call per node |

`Lru` orders by *borrow* where `Fifo` orders by *return*, and that is the whole of the difference: the
two agree while every object is returned in the order it was borrowed, and diverge as soon as one is
not. An object that has never been borrowed carries a zero stamp and so ranks lowest of all. For
`Priority`, a positive `IComparer<T>.Compare` result means "borrow this one first", and objects the
comparer calls equal are served oldest-returned first — the end `Fifo` would have served — so the order
is deterministic without the comparer having to be a total order.

The idle list is kept in return order under every value and none of them reorders it, so the switch
changes the order of borrows and nothing else — in particular the eviction scan still samples the
oldest-returned end, which is why the same `IHayateEvictionPolicy<T>` sees the same cold objects under
any strategy, and abandoned recovery still examines the oldest borrow first.

Rules of thumb: `Lifo` suits a tight borrow/use/return loop, or an instance whose per-instance state is
cheap to keep and worth keeping warm; `Fifo` suits an instance whose state should age uniformly, or one
where spreading usage across the idle set is the point; `Lru` suits a pool whose objects are
interchangeable but whose per-object warm-up is what you are trying to preserve, when returns do not
come back in the order the borrows went out; `Priority` suits a pool whose objects are *not*
interchangeable — a job with a rank, a channel with a cost — where the next borrow has a right answer
that no ordering of the list can express. The default reproduces the behaviour of every release before
the switch, so a pool that never calls `WithBorrowStrategy` is unaffected.

The comparer lives on the builder rather than in `HayatePoolOptions`, because that type is not generic
and cannot name `IComparer<T>`. In the DependencyInjection and Configuration packages it is resolved
from the container, and only when the configured strategy actually asks for one:

```csharp
services.AddSingleton<IComparer<Job>>(Comparer<Job>.Create((a, b) => a.Priority.CompareTo(b.Priority)));
services.AddHayatePoolSupport().RegisterHayatePool<Job>(o => o.BorrowStrategy = HayateBorrowStrategy.Priority);
```

Every value but `Fifo` applies to the general-purpose engine alone, sharded or collapsed to a single
shard, and each combination the engine cannot honour fails the build rather than being silently turned
into the default:

```csharp
// Throws InvalidOperationException: the lean fast path holds no ordered idle list.
new HayatePoolBuilder<Connection>().WithLean().WithBorrowStrategy(HayateBorrowStrategy.Lru).Build();

// Throws InvalidOperationException: the lock-free store keeps no order at all.
new HayatePoolBuilder<Connection>().WithLockFreeStorage().WithBorrowStrategy(HayateBorrowStrategy.Lru).Build();

// Throws InvalidOperationException: the front tier is consulted before the shard, so the order could
// only hold on a tier miss.
new HayatePoolBuilder<Connection>().WithThreadLocalFrontSlot().WithBorrowStrategy(HayateBorrowStrategy.Lru).Build();

// Throws InvalidOperationException: selecting Priority alone cannot say which object to serve.
new HayatePoolBuilder<Connection>().WithBorrowStrategy(HayateBorrowStrategy.Priority).Build();
```

The order is fixed when the pool is built — every shard snapshots it — so `ReloadConfig` refuses a
change to it rather than accepting one it cannot apply.

### Soft capacity (bound the retained set)

`MaxPoolSize` caps how many objects the pool will ever lend out, but nothing below it caps how many the
pool *keeps*: after a burst drains the ceiling, every object comes back and stays until eviction or
scale-down reclaims it. `WithSoftCapacity(n)` bounds that retained set — once the pool already holds
`n` idle objects, a returned object is disposed instead of stored:

```csharp
var pool = new HayatePoolBuilder<MyBuffer>()
    .WithMaxSize(64)          // still lends out 64 objects during a burst
    .WithSoftCapacity(8)      // ... but keeps at most 8 of them after it ends
    .Build();
```

The ceiling is enforced on the return path only, and only as a read of the idle count followed by the
store: two returns racing for the last slot may both be retained, which is what makes the ceiling
*soft*. It is a memory-shape control, not a mutual-exclusion guarantee. A pool with a soft capacity
below its hard ceiling still serves the full ceiling — it just keeps fewer objects back. Objects
dropped on return go through the regular destroy path (the policy's `OnDestroy` hook fires) and must be
created again if the next burst needs them, so the switch trades hit rate for bounded retained memory;
that is the right trade when the pool's idle memory is what has to be bounded, and the wrong one when
the burst is the normal case.

Rules and boundaries:

- `0` (the default) disables the ceiling and keeps the previous behaviour of retaining up to
  `MaxPoolSize` idle objects.
- A live ceiling must sit between `MinPoolSize` and `MaxPoolSize` — below the floor the pool could not
  hold the minimum it promises, above the hard ceiling the check could never fire — and `IsValid`
  rejects both rather than accepting a knob that silently does nothing.
- `ReloadConfig` rejects a change to `SoftCapacity` the same way: the return path reads a
  construction-time snapshot (and in lean mode the ceiling also fixes the fast path's slot-scan limit),
  so a runtime change could not take effect. Rebuild the pool to change it.
- The lean fast path honours the ceiling structurally — the single fast lane plus `SoftCapacity - 1`
  slots — so it costs the return hot path nothing beyond the existing slot scan. The unbounded pool
  model does not read the switch: its retained set is already bounded by its own `MaxIdle`, which
  drops a return once the queue is full.

### Storage backends (lock-free shard store, thread-local front slot)

Two switches that change *where* an idle object lives and *how* a borrow claims it. Both are off by
default, and with them off the borrow and return paths keep exactly the statements they had. They are
evaluated as a pair and switched independently — each gain can be attributed on its own — and both
apply to the general engine: the lean profile forces them off, because lean has its own storage and a
switch that cannot apply must not be silently ignored.

```csharp
var pool = new HayatePoolBuilder<MyBuffer>()
    .WithMaxSize(64)
    .WithLockFreeStorage()        // claim a slot, not a lock
    .WithThreadLocalFrontSlot()   // ... and keep one object per thread in front of it
    .Build();
```

**`WithLockFreeStorage()`** replaces the per-shard spin lock and its linked idle list with a slot
array whose entries are claimed by compare-exchange. Each wrapper owns one slot for as long as it
lives, which is what lets a borrow take an object without entering the shard lock at all.

- **Retrieval order is given up, and refused rather than ignored.** A slot array has no order to keep,
  so `BorrowStrategy` cannot be honoured: `EnableLockFreeStorage` with anything other than the default
  `Fifo` throws from `Build()`.
- **The ceiling can no longer be widened.** The slot array is sized when the shard is built, so
  `ReloadConfig` rejects a `MaxPoolSize` above it (lowering stays allowed, and the store clamps to the
  smaller of the two).
- **The slot array is laid out by capacity.** A shard at or below 64 slots gets its slots one cache
  line apart, with logical slot 0 a line in from the array header, so two threads exchanging two
  different slots stop sharing a line; above that it keeps the compact array, because the spacing costs
  one reference per stride position and what it removes does not grow with the array. The layout is
  decided per shard when the pool is built — a pool whose shards straddle the boundary holds both — and
  both layouts run the same protocol, so everything above still holds: `Count`, `GetAll`, `Remove`,
  `DrainForClear`, the reload ceiling and the given-up retrieval order mean the same thing on either
  layout.
- **The idle sample stops being the coldest end.** The eviction sampler walks a contiguous run of
  slots instead of the oldest end of the list. `MaxIdleTime` and `MaxLifeTime` reclaim exactly what
  they always did — an object past its limit is destroyed when it is sampled — but sampling no longer
  guarantees that the oldest objects are the ones sampled.
- Everything else keeps its meaning: `Count`, `GetAll`, the registry, the borrowed list and
  `DrainForClear` are unchanged, because claiming-and-removing an idle object is the same exchange a
  borrow performs.

**`WithThreadLocalFrontSlot()`** keeps one idle object per borrowing thread, on the pool instance, and
checks it before any shard. A thread that has borrowed gets its own object back without touching a
shard; on a miss the borrow falls through to the shard path unchanged. Depth is one slot, so a thread
cycling through several objects in sequence will not hit it every time.

- **Only a thread that has borrowed may park.** A slot records how many objects its thread has
  borrowed, and a return is parked only when that count is non-zero. Without the rule a thread that
  never borrows — an I/O completion callback, a hand-off worker — parks objects into a slot nobody will
  ever read: unreachable to every other borrower while still counted as live.
- **A parked object is still idle, and still reachable.** It lives in no shard, so the pool's idle and
  borrowed numbers explicitly carry the front tier's count, and the eviction sweep reads the tier's own
  slots. `Clear` and `Dispose` drain the tier back into the shards first, so a cleared pool's semantics
  do not depend on where an object happened to be parked.
- **A clear wins the race against a parking return.** A park in flight when a clear starts is
  reconciled after the fact, and the object that lost is destroyed rather than left parked.
- The tier is per pool instance, so two named pools of one element type do not share a front slot.

### Reload configuration at runtime

```csharp
pool.ReloadConfig(config =>
{
    config.MaxPoolSize = 200;
    config.EnableAutoScaling = true;
});
```

### Pre-warming the pool

The construction-time warm-up fills the configured minimum before the pool is handed over. `PreWarm`
does the same thing on demand, for any target, at any point in the pool's life:

```csharp
using var pool = new HayatePoolBuilder<MyResource>()
    .WithMaxSize(32)
    .Build();

var created = pool.PreWarm(32);   // pay the creation cost before the traffic arrives

var resource = pool.Acquire();    // served from the warmed set — nothing is created here
```

The count is a floor on *idle* objects, not on total objects, so calling it after borrowing creates
replacements instead of finding the pool already full. The pool's own ceiling still applies — the
maximum pool size, or the retained-object limit in the unbounded model — and a request above it warms
up to the ceiling rather than failing. A negative count throws `ArgumentOutOfRangeException`, and a
creation failure throws `InvalidOperationException` instead of being logged and swallowed: the
construction-time warm-up keeps going because the pool is still usable, but here the caller asked for
the objects explicitly. The call is safe alongside concurrent borrows and returns, and concurrent calls
never take the pool past its ceiling — a caller that finds the target already met creates nothing and
returns `0`.

Two things the call deliberately does **not** do. It does not change the configuration: warming beyond
the configured minimum is not a retention promise, so the extra objects age like any other idle object
and are reclaimed by the idle timeout and by background scale-down. And it does not prepare anything: on
a pool carrying a preparation strategy every borrow still runs the readiness check, so warming removes
the creation cost but never turns a warmed object into a ready one.

| Pool model | What `PreWarm` warms |
| :--- | :--- |
| Sharded engine | The shortfall, created round-robin; capacity is read per shard, so a pool that has scaled down warms to what it currently allows |
| Lean fast path | The shortfall, through the reservation path that enforces the ceiling on the live count |
| Unbounded pool | Up to `MaxIdle`, claimed and parked in the same order a return uses; this model creates on a miss and has no background warm-up, so this call is the only way to make its first borrows cheap |
| Preparation decorator | Forwards to the inner pool; the objects it creates are created, not prepared |
| Specialized pools | The default-capacity base pool (`StringBuilderPool` / `MemoryStreamPool`), not the tiering layer |

```csharp
// After a reload raised the minimum, pay for the new floor now rather than on the next borrow:
pool.ReloadConfig(config => config.MinPoolSize = 40);
pool.PreWarm(40);
```

### Clear all objects

```csharp
pool.Clear();
```

### Releasing objects and disposing the pool

Disposing the pool releases everything the pool is holding — a host does **not** have to
dispose each pooled object itself. This matches the contract of
`Microsoft.Extensions.ObjectPool.DisposableObjectPool<T>`.

```csharp
using var pool = new HayatePoolBuilder<MyResource>()
    .WithMaxSize(32)
    .Build();

var resource = pool.Acquire();
try { /* use resource */ }
finally { pool.Release(resource); }   // always return what you borrow
// leaving the using block disposes the pool and releases everything still in it
```

| Call | Effect |
| :--- | :--- |
| `Clear()` | Disposes the objects the pool holds right now, leaving the pool usable |
| `Dispose()` | `Clear()` plus releasing the background timer and the return-signal gate |

Three points worth knowing: checked-out objects are left alone and remain the caller's
responsibility; a user `Dispose` that throws is caught and logged so the drain continues;
and a pool that has been disposed is not guarded against further use, so treat it as
unusable. [`docs/disposal.md`](docs/disposal.md) covers the ownership model, the exact
behaviour of the drain on both engines, the side-by-side comparison with
`DisposableObjectPool<T>`, and the two differences from it. The companion page for the *lease* side of
ownership — do not touch a returned object, do not return it twice, do not return it to another pool,
and which of those three the engine actually detects — is
[`docs/ownership.md`](docs/ownership.md).

### Disposing with the process (optional)

A pool owned by the process itself never gets its `Dispose` call, because nothing user-facing
runs at that point. `WithAutoDisposeWithSystem()` closes that gap: the pool subscribes to
process exit — and to a terminal Ctrl+C, since either can end a process — and releases its
objects on the way out.

```csharp
var pool = new HayatePoolBuilder<MyResource>()
    .WithAutoDisposeWithSystem()
    .Build();   // released at process exit; still fine to dispose it yourself
```

Nothing on the borrow or return path takes part in it, and the subscription is removed on
disposal, so a pool you dispose yourself never stays reachable from the process-wide hook.
Hosted applications normally have a shutdown step of their own — prefer disposing there, or
point the pool at that signal instead:

```csharp
var pool = new HayatePoolBuilder<MyResource>()
    .WithShutdownHook(new MyHostLifetimeHook(lifetime))   // any IHayateShutdownHook
    .WithAutoDisposeWithSystem()
    .Build();
```

### Pool-level circuit breaker (optional)

Pools whose objects front a network dependency (database, message broker, remote service)
can take themselves out of service when that dependency keeps failing, instead of handing
out objects that are likely to be broken and letting every caller discover the outage on
its own. The feature is opt-in (`EnableCircuitBreaker = false` by default), so a pool that
does not ask for it behaves exactly as before, and the lean profile always forces it off.

```csharp
var pool = new HayatePoolBuilder<MyDbConnection>()
    .WithCircuitBreaker(failureThreshold: 3,
                        resetTimeout: TimeSpan.FromSeconds(30),
                        probeInterval: TimeSpan.FromSeconds(5),
                        probe: () => database.Ping())
    .WithOnUnavailable(args => logger.LogError("pool down: {Reason}", args.Reason))
    .WithOnAvailable(_ => logger.LogInformation("pool recovered"))
    .Build();
```

| Member | Role |
| :--- | :--- |
| `SetUnavailable(reason)` | The application reports a dependency failure; the `FailureThreshold`-th consecutive report opens the breaker |
| `SetAvailable()` | Reports the dependency is healthy again: closes the breaker and clears a pending failure streak — the manual recovery path when no probe is configured |
| `CheckAvailable()` | Cheap availability probe: a single volatile read, `false` while the breaker is open |
| `OnAvailable` / `OnUnavailable` | Fire exactly once per availability transition, never on a state that does not change |

While the breaker is open every `Acquire` / `AcquireAsync` throws
`HayatePoolUnavailableException` (derived from `InvalidOperationException`, so existing
catch blocks keep working) **before** doing any pool work — no shard scan, no wait, no
create. Recovery is automatic when a `probe` is configured: the first probe runs one
`ResetTimeout` after the trip, then one per `ProbeInterval`, and a throwing probe counts
as a failed probe. Without a probe no background work is created at all and the pool
stays unavailable until the application calls `SetAvailable()`. The probe timer exists
only while the pool is unavailable — an available pool holds no extra handle and pays a
single predictable branch per borrow.

Two further rules are available on the options object, and neither changes anything at its
default. `FailurePercentageThreshold` (percent, `0` = off) trips on the share of failed
reports over the reports collected since the breaker last recovered, which catches the
dependency that fails one call in three and so never produces a streak; `MinimumThroughput`
holds the ratio back until the sample is big enough, because a single failure out of a single
report is 100%. `SuccessThreshold` (default `1`) is the recovery side: the half-open breaker
then waits for that many consecutive healthy probes before it closes, while an explicit
`SetAvailable()` still closes it immediately.

### OpenTelemetry metrics (optional package)

`DotNetCore.HayateOP.Extensions.OpenTelemetry` bridges `IHayateMetrics` onto
`System.Diagnostics.Metrics` (the native data source of the OpenTelemetry .NET SDK).
The package itself has **zero external NuGet dependencies** — `Meter` is built into
.NET 6+; you bring your own OpenTelemetry SDK/exporter (OTLP, Prometheus, …) on the
consumer side.

Published instruments (meter `DotNetCore.HayateOP`):

| Instrument | Type | Tags |
| :--- | :--- | :--- |
| `HayatePoolAcquire` | Counter | `pool.name` |
| `HayatePoolRelease` | Counter | `pool.name`, `valid` |
| `HayatePoolMiss` | Counter | `pool.name` |
| `HayatePoolScaled` | Counter | `pool.name`, `action` |
| `HayatePoolWaitTime` | Histogram (ms) | `pool.name` |
| `HayatePoolSize` | ObservableGauge | `pool.name` |
| `HayatePoolAvailable` | ObservableGauge | `pool.name` |

```csharp
builder.Services.AddHayatePoolSupport()
    .RegisterHayatePool<MyPooledObject>(builder.Configuration);

// Publish HayateOP metrics to System.Diagnostics.Metrics
builder.Services.AddHayateOpenTelemetryMetrics();

// Consume them with the OpenTelemetry SDK (or observe via `dotnet-counters`)
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("DotNetCore.HayateOP"));
```

#### The core package's own meter (`HayateOP`)

The instrument set above is implemented once, in `HayateMetricsMeter` in the core package; the
OpenTelemetry package is the meter-name and packaging front for it. The exporter package is
therefore optional — `Meter` is a .NET 6+ base class library type (`System.Diagnostics.Metrics`), so
a pool can be observed with nothing but `DotNetCore.HayateOP`.

```csharp
using DotNetCore.HayateOP;
using DotNetCore.HayateOP.Metrics;

var registry = new HayateObjectPoolRegistry();
using var meter = new HayateMetricsMeter(registry);   // meter name "HayateOP"

var pool = new HayatePoolBuilder<MyPooledObject>()
    .WithPoolName("orders")
    .WithEnableMetrics(true)   // counters and the wait-time histogram are gated on it …
    .WithMetrics(meter)        // … and on EnableDiagnostics, the master switch, on by default
    .Build();

// The observable gauges read the pools out of the registry.
registry.Register("orders", pool);
```

```bash
dotnet-counters monitor --counters HayateOP
```

`HayateMetricsMeter(registry, meterName)` publishes under another name — that is the overload the
OpenTelemetry bridge uses to keep its `DotNetCore.HayateOP` meter. Attach **one** sink per pool,
either the core meter or the bridge: both publish the same instruments, so wiring both to one pool
would count every event twice. On netstandard2.0 / net48 no meter is compiled into the core package
at all, and the lean fast path keeps no counters by construction, so its counters read zero.

## Performance

BenchmarkDotNet compares HayateOP against `Microsoft.Extensions.ObjectPool.DefaultObjectPool<T>`
(baseline), plain `new` (no-pool lower bound), marklauter `MSL.Pool`, RRode `TinyPools`, Hertzole
`PowerPools`, labijie `Chopin.Pooling` and CnCSharp-Dev `PoolingLib` across the `Acquire`, `Release` and
`AcquireAsync` paths, with `[MemoryDiagnoser]` / `[ThreadingDiagnoser]` and P50/P90/P95/P99 latency
columns. `MSL.Pool` exposes an async-only lease API and `TinyPools`, `PowerPools`, `Chopin.Pooling` and
`PoolingLib` synchronous-only ones, so the lines cover complementary halves of the matrix: `MSL.Pool`
appears in the `Release`, `AcquireAsync` and concurrent suites, `TinyPools`, `PowerPools`,
`Chopin.Pooling` and `PoolingLib` in the `Acquire+Release`, `Release` and concurrent suites, and none of
them appears in the other's.

```bash
dotnet run -c Release --project tests/HayateOP.Benchmarks
```

Archived runs and the machine-readable baseline used by the CI performance-regression gate live in
[`docs/benchmarks/`](docs/benchmarks); the full methodology, the percentile columns and the
run-to-run variance are recorded in
[`docs/benchmarks/2026-09-10-o1-lean.md`](docs/benchmarks/2026-09-10-o1-lean.md). Single-threaded
`Acquire+Release` round-trip, every row below measured in one run on .NET 10.0 (2026-09-10):

| Configuration | Mean | Allocated |
| :--- | ---: | ---: |
| `Microsoft.Extensions.ObjectPool` (reference) | 27.0 ns | 0 B |
| **HayateOP lean fast path** (`WithLean()`) | **27.1 ns** | **0 B** |
| HayateOP general engine, every feature off | 286.8 ns | 384 B |
| HayateOP general engine, 4 shards | 285.9 ns | 384 B |
| HayateOP general engine, all features on | 415.0 ns | 0 B |

The lean fast path (`HayatePoolOptions.EnableLean`) stores the pooled value in a bounded array and
moves it through `Interlocked` compare-exchange, so an `Acquire+Release` round-trip allocates nothing
at all and costs what MEOP's minimal `Get()/Return()` costs — measured at parity in the same run,
while running roughly ten times faster than the general-purpose engine with every optional feature
disabled. It is the right choice for high-frequency pooling of small, stateless objects; the
general-purpose engine remains the choice whenever validation, eviction, leak detection, metrics,
auto-scaling or sharding are required, and its per-operation cost stays above MEOP's because it
tracks per-object state and a shard index that MEOP does not.

**ArrayPool direct-storage backend** (O-D): `WithArrayPoolStorage()` keeps every lean property but
rents the retention array from `ArrayPool<T>.Shared` and grows it on demand (×2) up to `MaxPoolSize`,
instead of allocating a fixed `MaxPoolSize`-sized array for the pool's whole lifetime — a large pool
holds only the storage its demand actually reached, and the array returns to the shared pool on
`Dispose`. Borrow/return cost is unchanged (measured at parity with fixed-buffer lean: 27.8 ns / 0 B
for `Acquire+Release`, same as MEOP). net6.0+ only; older targets ignore the flag and keep the fixed
buffer.

### String building (specialized package)

A dedicated comparison column (`SpecializedStringComparisonBenchmarks` in the benchmarks project,
net10, one build of `"order: 42 units"` per operation) puts the specialized string surfaces
head-to-head with `string.Format` and Cysharp.ZString:

| Approach | Mean | Allocated |
| :--- | ---: | ---: |
| **HayateValueStringBuilder** (Z1 value builder) | **36.6 ns** | **56 B** |
| Cysharp.ZString value builder | 36.1 ns | 56 B |
| ZString.Format | 73.4 ns | 56 B |
| `string.Format` (BCL baseline) | 89.2 ns | 80 B |
| StringBuilder direct (fresh per call) | 45.3 ns | 160 B |
| SharedStringBuilder (lock-guarded) | 51.8 ns | 120 B |
| StringBuilderPool (borrow + ToStringReturn) | 341.6 ns | 440 B |
| StringBuilderPool.Format (generic helper) | 383.6 ns | 440 B |

Readings: the Z1 value builder is at parity with ZString — its 56 B is the final string alone, the
build itself allocates nothing. The pooled rows carry the general engine's fixed 384 B borrow-cycle
bookkeeping (see the core table above) on top of the final string, so `StringBuilderPool` trades
allocation for observability and the capacity guard; where that floor matters and the flow is
transient, prefer `HayateValueStringBuilder`. The declared-capacity tier rows in
`SpecializedStringBuilderTierBenchmarks` quantify the tiering benefit separately: a 300K-character
build that the default borrow cannot retain costs 1,229,416 B per operation; the declared borrow
brings it to 614,864 B (0.500x) and 1,077 µs down to 413 µs.

## Known limitations

Behavioural notes to be aware of before adopting HayateOP — cold boot on an empty pool, the lazy
`CreateNew` policy, lean mode's ~100 ms blocking wake-up granularity, and `ApplyFeatureSwitches` capacity
semantics — are collected in
[`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md#behavioural-notes-non-breaking).

## Samples

Runnable examples live under [`samples/`](samples):

* **`samples/HayateOP.Samples`** — a console tour covering the fluent builder, custom policy
  (`bool OnRelease`), synchronous / timeout / async acquire, statistics & snapshots, runtime config
  reload, clearing, Dependency Injection, configuration binding, and diagnostics registration.
* **`samples/HayateOP.Samples.Endpoints`** — an ASP.NET Core app wiring DI, configuration binding,
  health checks, diagnostics, the management endpoints, and a DI-resolved pool endpoint
  (`/test-hayateop`).

## Contributing

Contributions are welcome.

Please feel free to submit issues, feature requests, or pull requests.

### Development Requirements

- .NET 10 SDK (for development)
- .NET 6 SDK (for compatibility testing)
- Visual Studio 2026, VS Code, or Rider

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

## License

This project is licensed under the MIT License - see the [LICENSE] file for details.
