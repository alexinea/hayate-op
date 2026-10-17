# Hot-path cost of each feature switch

`HayatePoolOptions` exposes roughly forty knobs. This page states, for every feature
switch, **where** its cost lands — the borrow path, the return path, the background
worker, or a diagnostic path that is only paid when called — so a configuration can be
trimmed to what the workload actually needs.

Two kinds of statement appear below and they are deliberately kept apart:

* **Measured** figures come from the archived benchmark run
  [`docs/benchmarks/2026-09-10-o1-lean.md`](benchmarks/2026-09-10-o1-lean.md) and are
  quoted with the configuration they were measured on. Only whole configurations were
  benchmarked, not one switch at a time.
* **Structural** statements describe what the code adds to a path, read from the
  implementation. They are exact about *where* the work happens and deliberately silent
  about a nanosecond figure that was never measured.

## 1. The measured ladder

Single-threaded `Acquire+Release` round trip on .NET 10.0, `MinPoolSize = 250` /
`MaxPoolSize = 300`, all pools pre-warmed, every row measured in one run
(2026-09-10):

| Configuration | Mean | Allocated |
| :--- | ---: | ---: |
| `Microsoft.Extensions.ObjectPool` (reference) | 26.950 ns | 0 B |
| **HayateOP Lean** (`WithLean()`) | **27.063 ns** | **0 B** |
| HayateOP Sharded4 (sharding only, 4 shards) | 285.857 ns | 384 B |
| HayateOP AllOff (general engine, every optional feature off) | 286.773 ns | 384 B |
| HayateOP Full (general engine, every feature on) | 414.987 ns | 0 B |

Read from the four Hayate rows:

* **`EnableLean` is worth about 10.6x** on the same feature set — 27.1 ns against
  286.8 ns — because it removes the per-object wrapper, the per-shard free-list lock, the
  registry probe on return and every diagnostic write.
* **Sharding is free in single-threaded steady state** (285.9 ns against 286.8 ns, i.e.
  0.3%, inside the run-to-run noise). It is bought for contention, not for latency — see
  the 100-thread suite in the benchmark report.
* **The six general-engine features together cost about 129 ns per round trip**
  (415.0 ns against 285.9 ns). That aggregate is the only measurement the report
  supports for the feature set as a whole; the per-switch attribution in §2 is structural.

Two caveats that matter when reading any absolute number here:

* The two sub-50 ns rows sit at the resolution limit of the benchmark machine, and
  repeated runs of the *same* benchmark move by up to 2.4x. The ratio and the allocation
  column are the stable results.
* The `Allocated` column for the general engine is not stable across runs (0-1065 B for
  the same benchmark) because lazily created internal structures are amortised over the
  measured operation count. Do not read those cells as a steady-state allocation rate —
  the one reproducible allocation result is the lean path's **0 B**.

## 2. Where each switch costs

### Borrow path

| Switch | Effect on the borrow path | Source of the cost |
| :--- | :--- | :--- |
| `EnableLean` | Replaces the general engine entirely | No wrapper, no shard lock, no registry probe, no diagnostic write — see §1 |
| `EnableSharding` / `ShardCount` | One idle-list probe per shard until a hit | A hit normally comes from the first scanned shard, so the added cost is a function of misses, not of `ShardCount` |
| `EnableLockFreeStorage` | Replaces the shard's spin lock and idle list with a slot array: a take is one compare-exchange per slot scanned, and it costs no lock acquisition and no list walk | A wrapper owns one slot for its lifetime, so the scan length is the slot array's, not the idle set's. It is also where `BorrowStrategy` is given up — a slot array has no order to keep — so any value but `Fifo` fails the build instead of being silently dropped. A shard of 64 slots or fewer lays those slots a cache line apart (G-23), which costs the array eight references per object and buys the removal of false sharing between two threads exchanging two different slots; a take's probe reads the slot count off a field and, on a spaced shard, lands on its own line per probe rather than sharing one with seven neighbours — cheaper on the first-probe hit the small idle set usually gives, and more lines touched on a long scan |
| `EnableThreadLocalFrontSlot` | One hop before the shard scan; a hit takes the object without looking at a shard at all | The hop is one load and one compare-exchange on a slot this thread owns, so a hit is cheaper than any shard visit; a miss adds that exchange to the same borrow. It also costs one atomic per successful borrow (the `Rents` count the park rule reads), and the first borrow on a thread materialises that thread's slot — one small object, once per thread |
| `ShardAffinityMode` | `None` costs nothing; `Thread` / `Custom` evaluate a start shard once per borrow | One thread-ID read or one user delegate invocation per `Acquire` (falling back to a sequential scan on a null / out-of-range / throwing delegate) |
| `BorrowStrategy` | `Fifo` (the default) reads the end the borrow path has always read — no added cost; `Lifo` reads the tail of the idle list instead; `Lru` and `Priority` walk the list and pick a node (smallest `LastBorrowedAt`, or the comparer's winner) | One predicted branch on a constructor-time flag for `Fifo`/`Lifo`; one walk of the idle list for `Lru` and `Priority`, plus one comparer call per node for `Priority`. The list is never reordered, so the strategy changes the order of borrows only |
| `EnableGenerationOptimization` | One extra `Stopwatch.GetTimestamp()`, a ticks-to-milliseconds division and a compare, plus a `ValidationSkipCount` write for old-generation objects | This is the only switch that adds a QPC read to the borrow path, and only on the synchronous one: the asynchronous borrow reads the clock for its lifetime check regardless, so there it adds the subtraction and the compare alone (G-19 put the promotion and the skip on that path too, so both paths now run the same three statements) |
| `ValidateOnBorrow` | One `IHayateObjectPolicy.Validate` call per borrow, plus `Destroy` on a failed verdict | Only consulted when `EnableValidation` is also on. The cost is the policy's own work — often a connection-health probe |
| `ResetOnBorrow` | One `IHayateResettable.Reset` call per borrow, for a pooled type that implements the marker | Off by default, so the historical configuration pays one predicted-false branch. `EnableLean` forces it off, so the lean path never pays it at all. Governs the reset the *default* policy drives only — a custom `IHayateObjectPolicy<T>` owns its `OnAcquire` and is untouched. This is where the cost of `ResetOnReturn = false` lands — see the relocation measurement below the two tables |
| `EnableLeakDetection` | Two predicted branches when `LeakTraceCaptureMode.Off` | The scan itself runs in `TakeSnapshot()`, not here (§ diagnostic paths) |
| `LeakTraceCaptureMode` | `Off` nothing; `Sampled` one `Interlocked.Increment` plus a modulo every N borrows; `EveryAcquire` **one call-stack capture per borrow** | The dominant cost of the whole leak-detection surface: tens of microseconds and 10-40 KB per borrow for `EveryAcquire` |
| `EnableDiagnostics` (**master**) | When off, removes the `TotalAcquired` atomic add, the whole `EnableMetrics` block and the per-borrow debug trace — one branch otherwise | The only switch that stops `TotalAcquired`, which `EnableMetrics` deliberately leaves running. Implied by `EnableLean` |
| `EnableMetrics` | A min/max CAS-loop update, an `Interlocked.Exchange` stamping `LastActivityTime`, an `IHayateMetrics.RecordObjectAcquired` call and a debug log entry | Shares the already-running stopwatch for the wait time. Requires `EnableDiagnostics` |
| `EnableAllocationTracking` | Two `GC.GetAllocatedBytesForCurrentThread()` calls (one before, one after) plus a counter pair | Unavailable on `net48` / `netstandard2.0`, where the counters stay 0. Requires `EnableDiagnostics` |
| `WarnAtRatio` / `CriticalAtRatio` | When either is non-zero, `CheckCapacityAlarm` runs on every borrow **and** every return and walks every shard to compute the utilization ratio | The walk takes one `SpinLock` per shard (`_shards.Sum(s => s.Count)`), so this is the most expensive optional switch per operation. At the default 0 it is one branch |
| `EnableCircuitBreaker` | One predicted branch when off (and in lean mode, which forces it off); one volatile read when on and the pool is available; while the breaker is open the borrow throws before touching any shard, wait gate or policy | The probe side is a timer that exists only while the pool is unavailable — see the background workers table |
| `WaitForWarmup` | One branch when off; when on, borrows block on the warm-up signal until pre-warm finishes | The block is one-time per pool |
| `MinPoolSize` / `MaxPoolSize` | No per-operation cost | Sizing only; `MinPoolSize` is paid once during pre-warm |
| `RejectPolicy` | Consulted only after a miss | Behavioural, not a cost knob: `Block` / `BlockTimeout` park on the signal gate and wake when a signal is published, `CreateOnDemand` turns a miss into a synchronous creation. Lean mode is the exception — it still re-checks on a ~100 ms slice |
| `DefaultAcquireTimeout` | No per-operation cost | Evaluated on the timeout path only |

Per-object metadata is not in the table because it is not a switch. The borrow timestamp,
`LeaseCount` and `LastGetThreadId` are written unconditionally on every borrow, together
costing one `Environment.CurrentManagedThreadId` read, one `Stopwatch.GetTimestamp()` and
three field writes — no allocation and no synchronization, because at that instant the
wrapper is owned exclusively by the borrowing thread. The lean fast path pays none of it:
it stores the pooled value directly instead of a wrapper.

### Return path

| Switch | Effect on the return path | Source of the cost |
| :--- | :--- | :--- |
| `EnableLean` | Replaces the general engine entirely | No registry probe, no free-list lock |
| `EnableLockFreeStorage` | A return is one compare-exchange into the wrapper's own slot — no lock, no list append, no node allocation | The slot index travels on the wrapper, so the store never has to allocate one on the way in. A full store refuses the return, and the object is destroyed by the caller as it always was |
| `EnableThreadLocalFrontSlot` | A return is parked in this thread's slot when the thread has borrowed and the slot is free; otherwise it takes the shard path unchanged | One compare-exchange, one generation recheck and one atomic on the idle count. The recheck is the clear-wins rule: a park that lost to a clear in flight is destroyed rather than left parked. A thread that has never touched this pool materialises its slot here in order to read the count, so a hand-off thread that only ever returns pays one small allocation, once |
| `ValidateOnReturn` | One `Validate` call per return, plus `Destroy` on a failed verdict | Also requires `EnableValidation` |
| `ResetOnReturn` | One `IHayateResettable.Reset` call per return, for a pooled type that implements the marker | On by default — this row was an unconditional cost of the default policy's `OnRelease` before 3.1 made it a switch. Turning it off moves the cost to the borrow path (`ResetOnBorrow`) or removes it entirely, which is only safe for a stateless payload. `EnableLean` forces it back on. Same jurisdiction limit as `ResetOnBorrow`: the default policy only |
| `EnableDiagnostics` (**master**) | When off, removes the whole `EnableMetrics` block and the per-return debug trace — one branch otherwise | Implied by `EnableLean` |
| `EnableMetrics` | A min/max CAS-loop update for the lease time, the `TotalReleased` counter, an `Interlocked.Exchange` stamping `LastActivityTime` and an `IHayateMetrics.RecordObjectReleased` call | Requires `EnableDiagnostics`. The stamp sits at the top of `CompleteReturn`, so it is written on every return branch — parked, dropped by the soft ceiling, rejected by the policy — and on the failure path, because a return that was attempted is still activity |
| `EnableAllocationTracking` | Two allocation queries plus a counter pair | Requires `EnableDiagnostics` |
| `WarnAtRatio` / `CriticalAtRatio` | Same shard-walking utilisation probe as on the borrow path | — |
| `EnableAutoScaling` | A watermark check after a rejected return | Only when `MinPoolSize > 0` and the pool fell below it |
| `SoftCapacity` | One idle-count read plus one compare per return, then `Destroy` instead of the store when the ceiling is reached | `0` (default) folds the branch away; the unbounded model does not read it (its `MaxIdle` already bounds the queue) |

#### What moving the reset actually costs

`ResetOnReturn` / `ResetOnBorrow` do not add a reset — they move the one the default policy already
performed. Measured on .NET 10.0, single-threaded, 250 pre-warmed objects, 250 borrows then 250 returns
per round, 200 warm-up rounds and 200 measured rounds per replicate, median of 9 independent pools:

| Pooled type's `Reset()` | Path | `ResetOnReturn` (default) | `ResetOnBorrow` | Delta |
| :--- | :--- | ---: | ---: | ---: |
| `Array.Clear` over 4 KB | borrow | 174.4 ns/op | 293.2 ns/op | **+118.8 ns/op (1.68x)** |
| | return | 235.6 ns/op | 130.4 ns/op | −105.2 ns/op |
| | round trip | 410.0 ns/op | 423.6 ns/op | +13.6 ns/op (+3%) |
| one `int` write | borrow | 168.4 ns/op | 171.2 ns/op | +2.8 ns/op (1.02x) |
| | return | 138.8 ns/op | 133.6 ns/op | −5.2 ns/op |
| | round trip | 307.2 ns/op | 304.8 ns/op | −2.4 ns/op |

Two things to read off that. The hook itself is worth about **3 ns/op** — the trivial-reset rows differ by
less than the replicate spread — so a configuration that leaves the switch off pays essentially nothing for
the switch existing. And the borrow-side delta (+118.8 ns) is the cost of the `Reset` body, arriving almost
exactly as the return side gives it up (−105.2 ns): the round trip moves by 3%, inside the noise of the
harness. The choice is therefore about **which side of the cycle is latency-sensitive**, not about total
cost — with one exception that is the real argument for the borrow side: an object that is created and
never borrowed again is never reset at all, so a pool that over-creates pays for reset only on the objects
it actually hands out. That saving does not show up in a steady-state round trip, which is why the table
above shows no win.

### Background workers

These never touch the borrow or return path; they run on the pool's single shared timer.
A pool with all of them disabled creates **no timer at all**.

| Switch | Period | Work per run |
| :--- | :--- | :--- |
| `EnableEviction` | `EvictionIntervalMs` (30 s) | Scans `NumTestsPerEvictionRun` samples per shard and claims + destroys the matches (`OnDestroy`, then the object's own `Dispose`) |
| `EnableAutoScaling` | `ScalingIntervalMs` (5 s) | Compares utilisation against the up/down thresholds and grows or shrinks the shard capacities |
| `ValidateWhileIdle` | `ValidateIntervalMs` (30 s) | Walks the whole idle list of every shard, calls `Validate` per idle object, claims + destroys the failures |
| `EnableCircuitBreaker` (probe) | Only while the pool is unavailable: first run one `ResetTimeout` after the trip, then one per `ProbeInterval` | Runs the configured probe and closes the breaker on a healthy verdict. The timer is created on the trip and disposed on the recovery — an available pool holds no handle for it, and a pool without a configured probe never creates one |

> `EnableValidation` contributes its interval to the shared timer's tick period even when
> `ValidateWhileIdle` is off and the idle pass therefore returns immediately. Since
> auto-scaling is on by default at the shorter 5 s period, this only becomes visible once
> eviction and auto-scaling are both off.

### Diagnostic paths — paid only when called

| Member | Cost |
| :--- | :--- |
| `TakeSnapshot()` | O(n) walk of every registered object; additionally walks the leak-detection branch (threshold check and `LeakDetectedCount` increment, plus trace formatting) when `EnableLeakDetection` is on; when metrics are on it also folds the borrowed count it already derives into `PeakActiveObjects` |
| `GetStats()` | O(shards) for the counts; reads the cumulative and timing fields atomically; when metrics are on it also folds the borrowed count it already derives into `PeakActiveObjects` |
| `Evict(reason)` | Walks the idle list of every shard; unavailable on the lean path, which keeps no per-object timestamps |

`PeakActiveObjects` (G-1) is the second member in the diagnostic-path group whose cost is a
consequence of a design choice rather than of a switch, and the choice is the same one the
capacity alarm made in the other direction. The engine keeps **no paired borrow counter**: the
shard's `TryTake` physically unlinks the object before handing it out, so an "objects currently
out on loan" count does not exist anywhere and both `GetStats()` and `TakeSnapshot()` derive it as
`TrackedObjectCount - Σ shard.Count` — a walk that takes one `SpinLock` per shard. Sampling the
peak at those two read sites therefore costs one extra compare on a path that has already paid
that walk, whereas counting it on the borrow path would require the walk *per borrow* — precisely
the cost that made the capacity alarm the most expensive optional switch in the borrow table.
The trade-off is stated rather than hidden: the value is a high-water mark over the moments the
pool was read, so a burst that starts and ends between two reads is recorded only if it is still
in flight when one of them runs. A caller who needs the peak of a burst must read inside it, or
raise the alarm ratios and let `CheckCapacityAlarm` do the per-operation walk instead.

## 3. Trimming by scenario

| Scenario | Configuration |
| :--- | :--- |
| High-frequency pooling of small, stateless objects | `UseLeanProfile()` — 27.1 ns / 0 B, parity with `DefaultObjectPool` |
| General engine on a measured hot path, bookkeeping not needed | `WithEnableDiagnostics(false)` — keeps sharding, validation, eviction and the async surface, but drops every counter, the metrics sink and the per-operation trace. The nearest general-engine equivalent of the lean profile's diagnostic stance |
| Connection / session pool | Keep validation (`ValidateOnBorrow`) and eviction; keep leak detection with `LeakTraceCaptureMode.Off`; turn metrics and allocation tracking off. The cost that matters is the policy's `Validate`, not the pool's |
| Batch processing | Sharding on; eviction and the capacity alarm off — objects churn continuously and the watermark only adds a shard walk per operation |
| Latency-critical steady state | Leave the capacity alarm at 0 and enable it only while tuning; it is the one switch that takes a lock per shard on every borrow and return |
| Memory-constrained host | Eviction on with a short `MaxIdleTime`; metrics and allocation tracking off |
| Contract that must never block | `RejectPolicy = CreateOnDemand` (or the lean path), which creates on a miss instead of waiting out the timeout |

## 4. The two switches above the counters

Counting is nested, not flat. `EnableMetrics` is not only an observability switch — it also
decides whether three of the four cumulative counters are written at all. With metrics off,
`TotalCreated`, `TotalReleased` and `TotalMissed` stop being maintained (they report 0), while
`TotalAcquired` keeps being incremented unconditionally on every borrow. Turning metrics
off therefore buys back those counter writes, and anything reading the statistics object
has to know which of the two groups it is reading.

Above it, `EnableDiagnostics` is the master switch over the whole surface. It is the one
configuration that also stops `TotalAcquired` and the per-operation debug trace — the trace
is written whenever metrics are *off* and diagnostics are *on*, so trimming it needed a
switch above both — which makes it the general-engine configuration with the least hot-path
work short of the lean path itself. Switching it off normalizes `EnableMetrics` and
`EnableAllocationTracking` off with it, so the two sub-switches below it imply nothing about
each other but both imply the master switch. The audit — including why `TotalAcquired` is
deliberately left unconditional under `EnableMetrics`, and what the master switch does and
does not touch — is in [`docs/metrics-gating.md`](metrics-gating.md).

The G-1 operational members (reuse efficiency, creation rate, throughput, peak concurrency,
uptime, last activity) are on the `EnableMetrics` side of that split, and they are what makes
the switch's cost visible in both directions: with metrics off, the borrow and return paths lose
the activity stamp along with the counter writes, and the statistics object reports every one of
them at its "not measured" reading together with a `MetricsEnabled` flag that says so. Two of the
six are not counters at all — `StartedAt` is stamped once in the constructor and
`PeakActiveObjects` is sampled on the read paths described above — so trimming metrics removes
work from the paths *and* removes the readings, rather than leaving a caller to infer the state
from a zero.

## 5. When pooling is the wrong tool

Everything above answers "what does each switch cost". This section answers the question that comes
first: **whether to pool at all**.

The line is a comparison, not a constant. Pooling pays when the object costs more to create than the
round trip costs to perform:

> pool only if `Create()` &gt; `Acquire + Release`

### The evidence for the negative case

`markjasongalang/TinyObjectPool` is worth reading precisely because it published the measurement that
argues against its own premise. Its BenchmarkDotNet run pools a `MyObject { public string Name }` —
an object that costs about 5-6 ns to construct — and the pooled path is **9.3x slower** at 5 objects
(264.39 ns against 28.78 ns), staying around 10-12x across the 50-5000 range and trading that for
0 B against 120 B per operation. Decomposed, `Rent()` pays a `SemaphoreSlim.Wait`, a `lock`, a
`Stack.Pop` and a wrapper allocation — roughly 250 ns of round trip — to avoid a 6 ns allocation.

Those absolute figures come from that project's machine and are **not comparable** with the ladder in
§1; what transfers is the shape of the argument. A pool that puts a semaphore, a monitor and a
wrapper on the borrow path charges more for the round trip than the object is worth, and no
allocation column makes that back.

### Where HayateOP sits

HayateOP's own round trip is far cheaper, which moves the line but does not remove it:

| Configuration | Acquire+Release | Allocated |
| :--- | ---: | ---: |
| HayateOP Lean (`WithLean()`) | **28.07 ns** | **0 B** |
| HayateOP AllOff (general engine, all optional features off) | 298.61 ns | 384 B |

These two rows are the CI baseline (`docs/benchmarks/baseline/baseline.json`, captured 2026-09-22 on
a GitHub Actions runner, .NET 10.0). They are **not** the run behind §1 — that one is the archived
2026-09-10 measurement — so the two tables should not be mixed; the rows within each table are
comparable with each other.

The lean path deliberately avoids everything the negative case above pays for: no per-object wrapper,
no semaphore on the synchronous borrow path, no reverse lookup on return. That is why it lands in the
same order of magnitude as the reference pool in §1, and why it is the only configuration in which
pooling a very cheap object is even arguable. A general-engine round trip of roughly 300 ns means a
general-engine pool of sub-microsecond objects is a net loss regardless of what the allocation column
says.

The corollary for the ownership rules in [`docs/ownership.md`](ownership.md): the check that makes
the general engine catch a foreign or double return is part of what the lean path removes. Choosing
lean to make pooling cheap enough is therefore also choosing to give up that check — the two
decisions are the same decision.

### The third option: striped lookup

When the resource can handle concurrency on its own, neither pooling nor `new` is the right answer —
**striped lookup** is. If what you need is "one lock (or `SemaphoreSlim`, or limiter) per key,
reused", keep `N` of them in an array and index by `hash(key) % N`. There is no borrow, no return, no
ownership to transfer and nothing to leak:

* the same key always resolves to the same instance, which is exactly what keyed serialization needs;
* the lookup can never be exhausted, because entries are never checked out;
* the correctness argument rests on the resource being shareable — a lock is, a connection is not.

The trade is that two keys can collide on one instance, so a striped lookup is correct only when
sharing is acceptable. A borrow/return pool is what you want when the resource must be **exclusive**
for the duration of a lease. Reaching for a pool on a shareable resource buys ownership accounting
that the workload did not need, and pays for it on every operation.

## See also

* [`docs/benchmarks/2026-09-10-o1-lean.md`](benchmarks/2026-09-10-o1-lean.md) — the raw
  benchmark run behind §1, including the percentile columns, the variance analysis and
  the reproduction commands.
* [`docs/ownership.md`](ownership.md) — the return contract, what the engine checks and what the
  lean path deliberately does not.
* [`docs/storage-backends.md`](storage-backends.md) — the two storage backends behind
  `EnableLockFreeStorage` and `EnableThreadLocalFrontSlot`: the layouts, the protocol, what each gives
  up and how it is refused rather than ignored.
* [`docs/BREAKING-CHANGES.md`](BREAKING-CHANGES.md) — the behavioural notes worth reading
  before trimming (cold boot, `CreateNew` laziness, lean mode's ~100 ms blocking wake-up
  granularity).
