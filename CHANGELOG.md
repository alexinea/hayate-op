# Changelog

All notable changes to Hayate Object Pool are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Breaking changes are described in full — with migration guidance — in
[`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md).

## [Unreleased]

### Added

- **The circuit breaker can now trip on a failure percentage and recover on a run of healthy probes**
  (G-18): `HayateCircuitBreakerOptions` offered four settings — a consecutive-failure threshold, two
  probe timings and the probe itself — which covers the dependency that stops answering altogether and
  misses the one that answers one call in three. Three settings close that gap, and each of them
  defaults to the behaviour that existed before it. `FailurePercentageThreshold` (percent, `0` = off)
  trips on the share of failed reports over the reports collected since the breaker last recovered, so
  a pool whose traffic never builds a streak can still take itself out of service. `MinimumThroughput`
  holds that ratio back until the sample is big enough, because one failure out of one report is 100%.
  `SuccessThreshold` (default `1`) is the recovery side: the half-open breaker then waits for that many
  consecutive healthy probes before it closes. A report is either a failure (`SetUnavailable`) or a
  success (`SetAvailable`); the two trip rules are alternatives rather than a conjunction, so turning
  the percentage on cannot make a pool harder to protect; and an explicit `SetAvailable` still closes
  the breaker at once whatever the run has reached. All three bind from configuration and travel
  through `CopyTo` / `Equals` / `GetHashCode` like the settings that were already there.

### Fixed

- **An asynchronous borrow now promotes an aged object to the old generation** (G-19): the generational
  optimization has two halves — promote an object older than `GenerationThresholdMs`, and validate the
  old generation once every `OldGenerationValidationInterval` borrows rather than on every one — and
  only the synchronous borrow path ran them. `AcquireAsync` read the object's age for lifetime rotation
  and for nothing else, so a pool whose borrowing went through it promoted nothing: every object stayed
  young, `TakeSnapshot().ObjectDetails` reported generation 0 for an object that had sat idle far past
  the threshold, and a `ValidateOnBorrow` pool paid the full validation cost on every borrow that the
  synchronous path skips. Both halves now sit on the asynchronous path at the positions they occupy on
  the synchronous one, sharing the single clock read that path already takes, so the switch costs it a
  subtraction and a compare instead of a second `Stopwatch` call. A pool that leaves the optimization
  on — the default — sees the two paths agree on promotion and on the validation trace; nothing else
  changes, and the lean fast path is untouched because it forces the optimization off.

## [3.1.0] - 2026-10-16

Capability and performance window. Everything a caller of the library touches is a pure addition — a
ceiling on concurrent borrows, a value-type lease, reset-timing switches, two more borrow orders,
two storage backends that are off by default, a ready-made `CancellationTokenSource` pool, an
optional analyzer package, and a sentence in the timeout messages that names the usual cause of the
wait — so a pool that asks for none of them runs the statements it ran before. The one exception
reaches an author who **implements** `IHayateObjectPool<T>` outside this repository: the interface
gained `TryAcquire`. Read [`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md) before adopting;
that entry is the only breaking one.

The 3.1 capability line adds three things to the borrow path's vocabulary: a ceiling on how many
objects may be out on loan at once (G-5), a value-type lease so a scoped borrow allocates nothing
(G-6), and two switches that move the automatic reset of an `IHayateResettable` object from the return
side of the cycle to the borrow side (G-7). All three are pure additions — every default is the
behaviour of the release before them, so a pool that asks for none of them is unchanged.

The line also lands the two storage backends the plan evaluated as a pair (G-9, G-10) — a lock-free
store for a shard's idle set, and a per-thread slot in front of it. Both default off, and both are pure
additions for the same reason as the three above: a pool that turns neither on runs the statements it
ran before. They are switched independently, so a measurement can attribute each gain on its own rather
than forcing a pairwise configuration.

The line also closes the one gap the reset switches exposed (G-20). A pool built through the
DependencyInjection or Configuration package ignored the two switches while `GetOptions()` reported
them; the fix is in the two extension packages rather than in the switches, and is recorded under
Fixed.

The line also adds a ready-to-use pool for one of the objects asynchronous code pools most often (G-8):
a `CancellationTokenSource` pool in the `Specialized` extension package, whose reuse depends on the
runtime the process is on and which reports that rather than hiding it.

The line also lands a compiler-side surface (G-12), in a separate optional package so the core stays
free of a Roslyn dependency: six analyzers that report pool misuse where it is written, and three code
fixes. The one Error among them is a deliberate decision, explained in its entry below.

The line also closes a small diagnostics debt carried since 2.9 (G-16): the exception a waiting borrow
ends in now names the cause the caller reading it is usually in a position to fix — borrowed objects
that never come back.

The line also adds a non-throwing form of the borrow (G-17): `TryAcquire`, in both a zero-budget and a
timeout-taking overload, for call sites that would otherwise wrap a borrow in a `try`/`catch` only to
ask "is one free right now?". Like the ceiling, the lease and the switches, it is a pure addition — the
throwing forms and every existing behaviour are untouched.

The line also removes the policy class as the price of admission for a configured pool (G-13):
`WithFactory`, `WithReset` and `WithReturnPredicate` describe a pool's creation, reset and return
decision as lambdas on the builder itself, so the full option surface — sizes, reject policy, metrics,
the reset-timing switches, lean mode — is reachable without writing a six-member policy class first.

The line also turns the return path's three caller mistakes into an optional hard error (G-14):
`ErrorOnIncorrectUsage` makes a null return, a return of an object the pool does not own and a second
return of an object the pool already holds throw instead of being logged and handled. Off by default,
so the shipped behaviour is untouched — the switch is meant for debug and test builds, where the
exception names the mistake at the line that makes it. Verifying it exposed a hole in the forgiving
path itself (also G-14): the refusal that follows a second return destroyed the object but left it
countable, and the next borrow handed out its `null` value. That is fixed on both storage backends and
recorded under Fixed.

The line also widens the borrow order (G-21): the switch that chose which end of a shard's idle list a
borrow came from gains two orders no end can answer — `Lru`, which serves the object borrowed longest
ago, and `Priority`, which serves the one a caller-supplied `IComparer<T>` ranks highest. Both leave
the idle list in return order, so the eviction sampler keeps reading the end it always read, and both
are off by default. Verifying them exposed a hole in the reload path (also G-21): a `BorrowStrategy`
change was accepted on a live pool and then ignored, because every shard snapshots the order when it is
built. That is now refused, and recorded under Fixed.

The line also gives a policy a way to hear about destruction (G-22): an optional
`IHayateObjectLifecycleObserver<T>` reports every object the engine destroys, together with the reason
it went, so a policy that maintains external state — a counter, a handle, a lease it took out in
`Create` — can keep that state true without re-deriving the reason from a log line. It is a second,
optional interface rather than two more members on `IHayateObjectPolicy<T>`, because that interface is
the user's main extension point and the core targets `netstandard2.0` / `net48`, where a default
interface member cannot keep an existing implementer compiling; the engine probes for the observer with
`is`, so a policy that does not implement it runs the statements it ran before. A pure addition.

The line also stops a shard's slot array from putting eight of its slots in one cache line (G-23): the
lock-free store now lays the array out by capacity — slots one cache line apart for a shard small
enough to afford the references, the compact array above that — while both layouts keep running the one
compare-exchange protocol G-9 landed. The layout is decided per shard at construction, so a pool whose
shards straddle the boundary holds both, and the striped store the same research item proposed was
deliberately not ported: the shard already is the stripe. No option, no member and no behaviour change.

The line also makes the test tree's own "zero warnings" claim executable (A5-6). The thirteen test
projects and the three tools under `tests/` were carrying 866 distinct warnings that no gate looked at:
nullable reference types still off in six projects, the xUnit analyzers' three complaints (1051, 1069
and 1031), a dead benchmark field, and two binding redirects fighting the automatic ones. They are
answered at the site rather than switched off, with one deliberate exception: 1031 is suppressed in the
two assemblies that disable parallelization — the condition xUnit's own documentation names as making
the rule inapplicable — while the two assemblies that leave parallelism on are fixed instead. Nothing
in the shipped packages changes; `tests/Directory.Build.props` only decides whether a warning stops the
build, and it now does, for every project under `tests/`, including one added later.

### Added

- **A ceiling on concurrent borrows** (G-5): `MaxActiveObjects` caps how many objects may be lent out
  at once. The pool already bounded the objects it holds (a shard's capacity, derived from
  `MaxPoolSize`) and decided what a miss does (`RejectPolicy`), but neither capped how many borrowers
  are in flight — so a pool sized for memory rather than for a downstream dependency handed out every
  object it owned, and the database, socket peer or remote service behind those objects saw the whole
  burst. `0` — the default — disables it, so the default configuration is unchanged. At the ceiling a
  caller gets the outcome its `RejectPolicy` already gives a miss: `Abort` refuses, `Block` waits for
  a return, `BlockTimeout` waits the timeout out and then gives up; the two creation policies wait
  instead of creating, which is the one place the ceiling overrules the policy, because an object
  created to satisfy the request would itself be out on loan. A ceiling above `MaxPoolSize` is
  rejected at build time — it could never fire, the rule `SoftCapacity` already follows — as is the
  combination with lean mode, which keeps no borrow bookkeeping to count against. The slot is claimed
  with a compare-and-exchange on the hand-out path rather than read from a derived count, so racing
  borrowers cannot all see the last free slot and overshoot.
- **`HayatePoolLease<T>`, with the four `AcquireLeased` / `AcquireLeaseAsync` overloads that produce it** (G-6): a
  value-type counterpart to the existing `HayatePoolScope<T>` class, so a scoped borrow no longer has
  to allocate a lease object — the lease lives in the caller's frame, and the return-once guarantee is
  carried by an `Interlocked` claim exactly as the class scope does. The class scope is unchanged and
  stays the right choice when the lease must outlive the frame or be treated as a reference. The type
  is a **mutable** struct rather than the `ref struct` the plan first specified: a `ref struct` local
  cannot cross an `await` boundary (`CS4007`), so its asynchronous disposal — which the class scope's
  own contract requires — would be unreachable, and a `readonly struct` is ruled out too, because the
  once-only claim needs `Interlocked.Exchange` on a field (`CS8340`). The mutable form keeps every
  capability of the class scope, including `await using`, and gives up only the compile-time guarantee
  that a lease cannot be boxed. Asynchronous disposal reuses the existing internal
  `IHayateAsyncReturnPool<T>`, gated to `net6.0+` like the class scope.
- **Reset timing switches** (G-7): `ResetOnReturn` (default `true`) and `ResetOnBorrow` (default
  `false`) move the automatic reset of an `IHayateResettable` object from the return side of the cycle
  to the borrow side, so a pool that over-creates pays for reset only on the objects it actually hands
  out. On by default is the behaviour before 3.1, so a pool that never asks is unchanged. The two are
  orthogonal timing, not a mode: both may be on (a cycle then resets twice) and both may be off (a
  pool of stateless payloads never resets), and `IsValid` states no rule about them. They govern only
  the reset the pool and `DefaultHayateObjectPolicy<T>` drive — a custom `IHayateObjectPolicy<T>`
  supplied through `WithPolicy` keeps its own `OnRelease` / `OnAcquire`, which the switches never
  touch. Lean pins the reset back to the return side, writing **both** halves rather than clearing
  only the borrow side, so a caller who had moved the reset to the borrow side lands on the return
  side and not on no reset at all. `HayateUnboundedPool<T>` takes no options object and stays fixed to
  the return side. What moving the reset costs is measured in
  [`docs/hot-path-costs.md`](docs/hot-path-costs.md): the reset moves rather than multiplies — roughly
  3 ns of hook cost, with a 4 KB reset shifting a 3% round trip from the return path to the borrow
  path.
- **A lock-free store for a shard's idle set** (G-9): `EnableLockFreeStorage` /
  `WithLockFreeStorage()` replace the per-shard spin lock and its linked idle list with a slot array
  whose entries are claimed by compare-exchange, so a borrow no longer takes the shard lock to take an
  object. A wrapper owns one slot for as long as it lives, and the claim is
  `ReferenceEquals(slot, Interlocked.CompareExchange(ref slots[i], null, slot))` — the read *is* the
  exchange, and a slot holds nothing but `null` or the one wrapper that owns it, so there is no link to
  follow and therefore none to go stale. That is why the slot itself needs no version: the
  version-packed head of the design's free-slot stack is the one place a version is needed, because slot
  *indices* are recycled even though wrappers are not. Off by default — with the switch off every field
  keeps its default value and the borrow path runs the statements it always ran. The form gives up two
  things, and both are refused rather than silently dropped: a slot array keeps no order, so a
  `BorrowStrategy` other than the default `Fifo` fails the build with the reason (a pool that never
  asked for an order holds `Fifo` and simply builds), and the array is sized when the shard is built, so
  `ReloadConfig` rejects a `MaxPoolSize` above it while still accepting one below. The eviction sampler
  now walks a contiguous run of slots instead of the coldest end, so `MaxIdleTime` and `MaxLifeTime`
  still reclaim every object past its limit, but the sample is no longer age-ordered. Nothing else moves:
  claiming an idle object is the same exchange a borrow performs, so the three `Remove` call sites,
  `Count` at its ten, `GetAll`, `SnapshotHead` and `DrainForClear` keep their meaning — which is exactly
  why this slot form was chosen over the index-addressed linked store the blueprint uses. Lean forces
  both this switch and the front slot off: lean has its own storage, and a switch that cannot take effect
  must not be silently ignored.
- **A per-thread slot in front of the shard path** (G-10): `EnableThreadLocalFrontSlot` /
  `WithThreadLocalFrontSlot()` keep one idle object per borrowing thread on the pool instance and check
  it before any shard, so a thread that has borrowed gets its own object back without touching a shard.
  Depth is one slot — the shape the lean fast path already proves out — so a thread cycling through
  several objects in sequence will not hit it every time. A return is parked only when the returning
  thread has already borrowed from this pool, the blueprint's `Rents` rule ported as-is: without it a
  thread that never borrows — an I/O completion callback, a hand-off worker — parks objects into a slot
  nobody will ever read, and those objects are unreachable to every other borrower while still counting
  as live. The count is taken where a borrow is *handed over* rather than where an object is taken, so a
  take the active-object ceiling refuses and retries does not count as a borrow this thread made. A
  parked object is still idle and still reachable: the pool's idle and borrowed counts carry the tier's
  own count, the eviction sweep reads the tier's slots, and `Clear` / `Dispose` drain the tier back into
  the shards before sweeping, so a cleared pool behaves the same whether an object was idle in a shard or
  in a slot. A clear wins a race against a park in flight — the generation counter is read before the
  park and checked after it, and an object that lost is destroyed rather than left parked. The park also
  unlinks the wrapper from the borrowed list (it is idle, not in flight) and the slot hit links it back,
  because answering only one half would let an object borrowed out of the slot and then abandoned fall
  outside `K2` silently. Both the synchronous and the asynchronous borrow read the slot first, since the
  release half is shared. The tier is per pool instance, so two named pools of one element type keep
  separate front slots.
- **A pool of `CancellationTokenSource` instances** (G-8): `CancellationTokenSourcePool` in the
  `Specialized` extension package, aligned with Reservoir's pool of the same name — `Rent()` costs one
  line, `using` on the rented source returns it, and `RentLinked(CancellationToken)` makes the rental
  follow an upstream token. A `CancellationTokenSource` is one of the most frequently pooled objects in
  asynchronous code — the per-operation timeout or cancellation scope that would otherwise be allocated
  and collected on every request — and it is also one of the few that cannot be reused naively: a used
  source carries the timer and the callbacks its renter left behind, and the one call that clears them,
  `CancellationTokenSource.TryReset()`, is documented as unsafe while the source is still in use. The
  pool therefore owns the whole lifetime: a source is handed out exclusively, and its reset runs on the
  return path, after the rental has ended, so a reset can never race a renter still using the source.
  The rented instance is a `PooledCancellationTokenSource` — a `CancellationTokenSource` that knows its
  pool, so disposing it returns it through the pool's ordinary return path and a second dispose of the
  same rental is a no-op rather than a duplicate return. Reuse is runtime-dependent, and the pool says
  so rather than hiding it: `TryReset()` does not exist on .NET Framework 4.8, and without it a returned
  source cannot be cleaned — its stale callbacks and timer would fire during the next rental — so rather
  than fork its source per target framework the pool locates the method at run time and exposes the
  answer as `IsReuseSupported` (`false` on .NET Framework 4.8, `true` on .NET 6.0 and later). Where reuse
  is unsupported the pool still hands out a correct source every time — it destroys a returned source
  instead of parking it and creates a fresh one for the next rental — so the same call site runs on
  every target framework, with only the allocation profile differing. Validation is what keeps a
  canceled source out of the pool: a source whose cancellation has been requested, or whose timer has
  already fired, is destroyed on return and caught on borrow, because no reset can make it usable again.
  `RentLinked` registers the link on the pooled source — with `useSynchronizationContext: false`, so the
  callback cannot be posted back to a UI context the renter happened to be on — rather than creating a
  linked source through `CancellationTokenSource.CreateLinkedTokenSource`, so the rental is still a
  pooled instance that returns on disposal; disposal drops the link and waits for an upstream callback
  that is already running before the source can be handed out again, so an upstream token canceled after
  the return can no longer reach the reused source, and a token that cannot be canceled produces an
  ordinary rental. Clearing or disposing the pool drops the sources it holds rather than releasing them,
  and costs nothing: a parked source has already been reset — its timer disarmed and its callbacks
  dropped — so it holds nothing that needs releasing.
- **Six Roslyn analyzers and three code fixes** (G-12): a new optional package,
  `DotNetCore.HayateOP.Extensions.Analyzers`, that reports pool misuse at build time. `HYO009` reports a
  discarded lease or scope as an **Error** — `HayatePoolScope<T>` has no finalizer and
  `HayatePoolLease<T>` is a `struct`, so a dropped lease is a permanent leak that nothing will ever
  notice, and the compiler refusing the build is the honest signal; `HYO001` (a pooled object dropped
  without release), `HYO003` (an acquire on a pool disposed earlier in the same method) and `HYO006`
  (`OnRelease` disposing the item the pool still owns) are Warnings, while `HYO004` (a blocking acquire
  in an `async` method) and `HYO005` (an async acquire ignoring a `CancellationToken` in scope) are
  Info, so nothing else blocks a build. Three code fixes ship with it — wrap an acquire in a `using`,
  switch a blocking acquire to `AcquireAsync`, forward the surrounding token — each offered only for the
  rule it belongs to. The rules key on the containing namespace rather than on the method name, so a
  project that does not use the library gets no diagnostic at all. Targets `netstandard2.0` against
  Roslyn 3.8, the oldest host the current line-up shares, and is a pure addition: nothing in the core,
  the extensions or the samples references it.
- **A non-throwing borrow, `TryAcquire`** (G-17): two new members on `IHayateObjectPool<T>` —
  `TryAcquire(out T item)` and `TryAcquire(TimeSpan timeout, out T item)` — for the call site that
  asks "is an object free right now?" without treating the answer as an error. Every pool type
  implements them, including the three pools in the `Specialized` extension package, which forward the
  try to the pool they wrap. The first form is the second with a zero budget, so a caller can check
  the pool's current state without naming a duration at all. What counts as a refusal follows the
  borrow the caller already knows: the circuit breaker being open reports `false` rather than throwing
  `HayatePoolUnavailableException`, an `Abort` miss reports `false` rather than throwing
  `InvalidOperationException`, a `Block` or `BlockTimeout` miss reports `false` rather than throwing
  `TimeoutException`, and a borrow that stops at the `MaxActiveObjects` ceiling reports `false` rather
  than waiting or creating. Two failures stay failures, because a try is a question about the pool's
  state and not a promise to swallow bugs: a negative timeout still throws
  `ArgumentOutOfRangeException`, exactly as `Acquire` does, and an object factory that throws still
  surfaces its exception — wrapped as before, so a misconfigured pool cannot pass silently as an empty
  pool. `Block`'s documented idiosyncrasy — it ignores its timeout and returns only when an object is
  free — deliberately does not carry over: in try mode the timeout is a hard cutoff, because the one
  thing a caller asking with a budget cannot mean by "try" is "maybe never". Statistics count a
  refused try the same way they count the refused borrow it replaced: the missed-borrow counters move
  under the same metrics gate as before, and the ceiling's throttle counter is not gated at all, so
  the two borrow forms report the same numbers for the same events. The lean path, the general path
  and the asynchronous wrapper all answer the same questions the same way.
- **A policy described by lambdas, on the builder** (G-13): `WithFactory`, `WithReset` and
  `WithReturnPredicate` on `HayatePoolBuilder<T>` describe a pool's creation, its reset on the way
  back into the idle state, and the predicate that decides whether a returned object is taken back —
  the three things a policy class exists to say, expressed as lambdas. The builder previously had one
  policy entry, `WithPolicy`, which takes an `IHayateObjectPolicy<T>`: correct for a policy with a
  lifetime of its own, and a six-member class to write for the pool whose factory is a lambda. What
  the one-call `HayatePool.Simple` already did for the no-options case — build its policy from
  delegates — is now the same mechanism on the full builder, so every other option on the chain still
  applies: sizes, reject policy, metrics, the reset-timing switches, lean mode, named pools. The
  delegates are last-one-wins against `WithPolicy` in both directions, so mixing the two ways of
  describing a policy resolves by the order the calls were made rather than by an undocumented
  precedence. Two layers are deliberately kept apart: `WithReset` runs on the policy's passivate hook,
  while `ResetOnReturn` / `ResetOnBorrow` continue to govern only the library default policy's reset
  of an `IHayateResettable` object — a policy the caller describes, by class or by lambda, owns its
  own hooks, which is what a policy class has always meant. A chain that asks for a hook without a
  factory fails at build time, next to the other build-time rejections, rather than at the first
  borrow. `HayatePool.Simple` itself is unchanged in signature and behaviour. No existing signature,
  option or behaviour changed.
- **An optional hard error for a misused return** (G-14): `ErrorOnIncorrectUsage` (default `false`) and
  the builder's `WithErrorOnIncorrectUsage()` turn the three caller mistakes on the return path into an
  `InvalidOperationException` instead of a log line — returning `null`, returning an object this pool
  does not own, and returning an object the pool already holds. The default path is unchanged statement
  for statement: the misuse is still logged, counted and handled, exactly as
  [`docs/ownership.md`](docs/ownership.md) records. The switch costs on the return path — with the
  thread-local front slot on, the already-returned check enumerates the slot tiers — so it is meant for
  debug and test builds, where the exception names the mistake at the line that makes it rather than
  where its effects surface. Two boundaries are deliberate: a `ValidateOnReturn` failure and an
  `OnRelease` rejection are verdicts about the object rather than mistakes by the caller, so they stay
  non-throwing; and in lean mode only the `null` case is detectable, because the lean return path keeps
  no ownership lookup. A refused return leaves the object with the caller rather than destroying it —
  the forgiving path destroys a foreign object, but a return the pool refuses cannot also take
  ownership of it. No existing signature, option or behaviour changed.
- **Two more borrow orders: least recently used, and caller-ranked priority** (G-21):
  `HayateBorrowStrategy` gains `Lru` — the borrow serves the object borrowed longest ago, by the
  `LastBorrowedAt` stamp the borrow path has recorded unconditionally since 2.5, so the order adds no
  per-object state — and `Priority` — the borrow serves the object a caller-supplied `IComparer<T>`
  ranks highest, installed through the new `WithBorrowPriority(IComparer<T>)`, which selects the
  strategy as well so a comparer cannot be handed over and then ignored. `Lru` orders by *borrow* where
  `Fifo` orders by *return*, which is the whole of the difference: the two agree while every object is
  returned in the order it was borrowed, and diverge as soon as one is not. Objects the comparer calls
  equal are served oldest-returned first, the end `Fifo` serves, so the order stays deterministic
  without the comparer having to be a total one. Neither value reorders the idle list — both walk it
  and unlink the node they chose — so the eviction scan keeps sampling the oldest-returned end and the
  objects offered to an `IHayateEvictionPolicy<T>` never depend on this setting. Four boundaries are
  deliberate, and each fails the build rather than being honoured in part, the rule the existing
  `Lifo`-on-lean guard already followed: no order but `Fifo` is available on the lean fast path (no
  ordered idle list), on the lock-free store (no order at all), or alongside the per-shard front tier
  (consulted before the shard, so the order would hold on a tier miss only); and `Priority` without a
  comparer cannot express which object should be served. The comparer lives on the builder rather than
  in `HayatePoolOptions`, which is not generic and so cannot name `IComparer<T>` — the route
  `CustomShardAffinity` already takes — and in the DependencyInjection and Configuration packages it is
  resolved from the container when, and only when, the configured strategy asks for one, so a comparer
  registered for some unrelated purpose is never picked up by a pool that did not ask. `RoundRobin`
  from the reference implementation was deliberately not added: on a list every return appends to the
  tail, serving the head *is* a round robin over the idle set, so the value would have been a synonym
  with a cost and no behaviour. No existing signature, option or behaviour changed; the default is
  `Fifo`, statement for statement.
- **An optional way to hear about destruction, and why it happened** (G-22): a policy may additionally
  implement `IHayateObjectLifecycleObserver<T>` to be told about every object the engine destroys,
  through `OnEvict(T item, HayateDestroyReason reason)` — and, for the one case that is a verdict about
  the object rather than a retirement, `OnValidationFailed(T item)`. `HayateDestroyReason` names nine
  causes: a failed `Validate` on the borrow, return or idle-sweep path (`ValidationFailed`, routed to
  the second hook), an eviction by policy or a manual `Evict` (`Evicted`), a return the pool had no room
  for — a full shard, the soft ceiling, a pre-warm overflow, a scale-down growth race
  (`CapacityOverflow`) — a return `OnRelease` refused (`ReturnRejected`), a `Clear()` / `Dispose()` drain
  (`PoolCleared`), an automatic scale-down (`ScaledDown`), an abandoned object reclaimed by leak
  recovery (`Abandoned`), a lifetime-rotation hand-out (`Rotated`), a foreign return, or a second return
  rejected because the shard already holds the wrapper (`Misuse`). It is a **separate, optional interface** rather
  than two more members on `IHayateObjectPolicy<T>`, and that is the whole of the design: that interface
  is the user's main extension point — every policy class in the tree, and every one downstream,
  implements it — and the core targets `netstandard2.0` and `net48`, which have no default interface
  members, so a new member there would be a breaking change for every implementer. The engine probes
  for the observer with `is` and does nothing when it is absent, the shape the asynchronous policy
  contract already uses, so a policy that does not implement it is unaffected — statement for
  statement. Two rules make the hook
  usable for bookkeeping: it runs **before** the object is torn down, so the observer sees a live value,
  and **exactly once** per object, from the single point the destroy path already funnels through. An
  exception thrown by an observer is caught and logged, never allowed to abort a drain in progress — the
  notification is a courtesy to the policy, not a precondition for releasing the object. Three
  boundaries are deliberate. The hook is **synchronous only**: it runs on the same paths as `Validate`
  and `OnAcquire`, and an asynchronous-only policy still implements the synchronous observer, so there
  is no async twin to keep in step. `Clear()` / `Dispose()` **do** report `PoolCleared` — the drain is
  the one destruction a caller most wants to account for — while still, by the long-standing contract of
  [`docs/disposal.md`](docs/disposal.md) §5, not running `OnDestroy`; the two are independent. And the
  lean fast path reports only the reasons it can actually see — a refused return, a full buffer and a
  drain — because it never calls `Validate`, so `OnValidationFailed` is unreachable there rather than
  reported wrongly. The existing `HayateEvictReason` was deliberately not reused: it classifies the
  manual `Evict` API alone, and a background eviction never produces one — the engine asks
  `IHayateEvictionPolicy<T>.ShouldEvict` — so widening `OnEvict` to take it would have forced a second
  implementation of the eviction rule at the call site, the duplication the code deliberately avoids.
  No existing signature, option or behaviour changed.
- **A shard's slot array spaces its slots a cache line apart when it can afford to** (G-23): with
  `EnableLockFreeStorage` on, the store's slot array is now laid out by capacity. At or below 64 slots
  per shard the slots sit one cache line apart — 8 references on a 64-bit runtime, 16 on a 32-bit one —
  and logical slot 0 starts one stride in, so two threads exchanging two different slots stop
  ping-ponging the one line the compact array puts them in. Above the boundary the array stays compact,
  because the spacing costs one reference per stride position and that cost grows with the array while
  the contention it removes is bounded by the pool's concurrency rather than by the array's size. Both
  layouts run the same protocol — one compare-exchange per take, return and claim — and differ only in
  the mapping from a wrapper's logical slot to its array position, which every slot access applies
  (the second-return guard, the return, the take scan, `Remove`, `GetAll`, `SnapshotHead` and the clear
  drain), so `Count`, the idle sample, the removal semantics and the reload ceiling keep their meaning
  on either layout. The decision is per shard and is taken once, when the pool is built, from that
  shard's own capacity: a pool whose shards straddle the boundary holds both layouts at once, and the
  array being sized once is already enforced by the reload ceiling that G-9 added. It applies to the
  lock-free store alone — the lean path has its own storage — and it is not a new option, a new member
  or a behaviour change: with the store off the borrow and return paths are untouched, and above the
  boundary the layout is the identity, where the only thing left of it is one predicted branch on the
  take scan.

### Changed

- **A borrow timeout now names the likely cause** (G-16): the three `TimeoutException` messages a
  waiting borrow can end in — the synchronous general-purpose path, the synchronous lean path and the
  asynchronous one — gained a sentence naming the usual cause of the wait, borrowed objects that are
  never returned. The original sentence is untouched and the hint follows it, so everything that keys on
  the existing text keeps working: the four `Assert.Contains` in the preparation pool's tests,
  log-parsing rules, and alert filters that route on message fragments. The two variants whose original
  sentence already reports the `MaxActiveObjects` ceiling ("all N objects ... are still out on loan")
  did not gain it — they already say what is wrong. No policy, option or signature changed.

### Fixed

- **A second return can no longer leave the pool handing out `null`** (G-14): the shard's append guard
  has always refused a second return of the same object and destroyed it — the documented "the pool
  loses one object" outcome — but the destroyed wrapper stayed linked in the shard's free list, so the
  idle count kept counting it and the next borrow popped that entry and handed out the wrapper's value,
  which destruction had cleared to `null`. A caller who had done nothing wrong received a null object
  from a pool that had one to give. Both storage backends are fixed: the locked list unlinks the entry
  when it refuses the return, and the lock-free store claims the slot out under the same identity check
  a take performs. The refusal now leaves the store consistent, the idle count drops immediately instead
  of at the next borrow, and the borrow that follows creates a fresh object. Found while verifying the
  misuse switch above.
- **The DependencyInjection and Configuration paths now honour the reset-timing switches** (G-20): a
  pool registered through `AddNamedPool<T>` / `RegisterHayatePool<T>` or through the configuration
  bindings ignored `ResetOnReturn` and `ResetOnBorrow` — the container seeded the library's default
  policy and the pool pinned it, so the policy kept the historical timing while `GetOptions()` reported
  the configured values. That is the worst shape of a silent failure: a pool reporting a timing it does
  not run, which is why it is fixed in the window that introduced the switches rather than left as a
  documented limit. A pool whose resolved policy is the library's own default is now built without
  pinning it, so the builder derives the policy from that pool's options — once per pool, so two names
  of one element type can run different timings. A policy the caller registered, in either order
  relative to the pool, and one a registered `IHayateObjectPolicyFactory<T>` returns, are pinned
  exactly as before, and the switches still never move a caller's own reset. The element-type
  registration and its resolution are unchanged, so
  [`docs/named-pool-policies.md`](docs/named-pool-policies.md) §5 and its second acceptance anchor
  still hold; §8 records what the pool now does differently with the policy that path resolves. The
  fallback is recognised by instance identity rather than by a "did we seed it?" flag, so a policy
  registered after the pool is honoured rather than replaced by the library's own.
- **A borrow-order change is no longer accepted at run time and then ignored** (G-21): `ReloadConfig`
  let `BorrowStrategy` be changed on a live pool, but every shard snapshots the order when it is
  constructed, so the shards kept serving the order they were built with while `GetOptions()` reported
  the new one — the "accepted and never applied" shape this switch exists to remove, and the same rule
  `SoftCapacity` and the lean retention buffer already follow. A reload that changes the order is now
  refused with the original value restored, and the pool keeps serving the order it was built with.
  The hole predates the two new orders — the option already had two values and was never guarded — but
  adding the two that walk the list is what made it visible, because a stale snapshot now also decides
  *how* the selection is performed, not only which end it starts from. Rebuild the pool to change the
  order.

## [3.0.0] - 2026-09-24

Lifetime and wake-up semantics, with one signature change and one namespace move. 3.0 states in full
what `MaxLifeTime` always meant and leaves the enforcement of the borrowed half exactly where 2.9 put
it — behind an opt-in switch that is off by default (β). It removes the 100 ms quantisation blocking
borrows used to carry, so a waiter now wakes on a signal published by every site that makes an object
available, and the blocking timeout becomes a boundary rather than a floor (B6). The one-call factory
grows on a miss instead of timing the second borrow out (B6). A shard's ends are padded so
neighbouring stripes stop sharing a cache line (G-4). `IHayateLogger` gains four members — the
release's one signature change, breaking for implementers, with a new `HayateLoggerBase` as the
migration path (L7) — and the Microsoft.Extensions.Logging bridge moves into the namespace of the
package that ships it (L8). Named pools gain a pool-name-aware policy factory, so two names of one
element type can carry different policies without inventing a second element type (B8).

Read [`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md) before adopting: two of the entries above
are breaking, and three more are observable behaviour changes.

Three things in this release are not package changes. The target-framework matrix and the `net48`
`ArrayPool` position were both re-examined and kept (T-1 / T-2); the review is recorded in the README,
and the package support table now lists the `net48` assets Configuration and HealthCheck actually
ship. Every dependency version the packages declare is now written once, in
`asset/props/dependency.versions.props`, and checked by `scripts/check-dependency-versions.py` in CI
(L9) — the shipped asset face is unchanged, byte for byte. And the hot-path baseline was re-captured
on CI against the 3.0 tree, with the per-target thresholds carried forward (B6-① + G-4).

### Added

- **Pool-name-scoped policy resolution** (B8): `IHayateObjectPolicyFactory<T>` resolves the policy a
  pool is built with from the pool's registry name, so two named pools of one element type can carry
  different policies — "primary uses connection string A, replica uses connection string B" — without
  a second element type standing in for a second configuration. Register one with
  `AddHayatePolicyFactory<T>(factory)`, or with `AddHayatePolicyFactory<T, TFactory>()` when the
  container should build the factory itself. Nothing existing changes: with no factory registered,
  `BuildPool<T>` resolves `IHayateObjectPolicy<T>` by element type alone, which is what every earlier
  version did, and the factory is consulted once per pool build on the cold path rather than per
  borrow. It applies to the unnamed pool too, whose registry name is the bare element type name. The
  alternative form — turning `IHayateObjectPolicy<T>` itself into a keyed service — was rejected:
  keyed registration does not exist in the 6.0 / 7.0 abstractions this package targets, and it would
  change what an existing registration means. The decision and the contract are recorded in
  [`docs/named-pool-policies.md`](docs/named-pool-policies.md).
- **`IHayateLogger` gained four members, and `HayateLoggerBase` to migrate through** (L7): `LogTrace`,
  `LogCritical`, `IsEnabled(HayateLogLevel)` and `BeginScope` join the four the interface already had,
  so a logger written for Microsoft.Extensions.Logging or Serilog can be handed to the pool without
  the levels it exposes being dropped, and the pool can ask whether an entry is worth building before
  it builds it. This is the release's one signature change, and the library cannot soften it with a
  default interface method — the core package targets `netstandard2.0` and `net48`, and neither
  runtime supports them. `HayateLoggerBase` is the migration path: the four members 2.x had are the
  only ones it declares `abstract`, so a class written against 2.x changes its class clause and marks
  its existing methods `override` — **no member 3.0 added has to be implemented**. They are abstract
  rather than virtual with a body on purpose: a virtual member would let the one-clause change compile
  and then hide the inherited member instead of overriding it, and every call through `IHayateLogger`
  would reach the empty base method and record nothing.

### Changed

- **`MaxLifeTime` is documented as the maximum lifetime of a pooled object from the moment it is
  created — idle or borrowed** (β): the documented scope used to be the idle half only, which read as
  though a borrowed object could never age. The value, its default (10 minutes) and the pool's
  behaviour are all unchanged, and `EnableLifetimeRotationOnBorrow` — the switch that decides whether
  the borrowed half is enforced — is still `false` by default, so the default behaviour is the one
  every release before 2.9 had. What changed is that the option's XML documentation and the README now
  say the same sentence.
- **Blocking wake-up is signal-driven, not quantised** (B6-①): `Block`, `BlockTimeout` and the
  create-on-miss policies used to re-check their shard in fixed 100 ms slices, so a waiter could not
  observe a return sooner than the next slice boundary. The wait is now a one-shot
  `SemaphoreSlim.Wait(timeout)`, and every site that makes an object available publishes a signal
  through `SignalAvailability()` — seven sites, the return path among them. Two consequences are
  worth knowing before adopting: the blocking timeout is a **boundary rather than a floor** (a 400 ms
  timeout that used to surface as roughly 500 ms now surfaces at 400 ms), and `Block` waits until a
  signal is published with no timeout of its own. Re-measure a p99 that sat just under 100 ms. The
  lean variant keeps the slice, and its behaviour is unchanged.
- **The wait-based policies' growth contract is stated where it is enforced** (B6-E1 / B6-E2): a miss
  on a pool that already tracks objects waits rather than growing, even while `MaxPoolSize` would
  still allow more — so a pool whose objects are all lent out reaches `MaxPoolSize` only through the
  background scaler, and a caller that gives up first sees a `TimeoutException` with room left in the
  pool. Only a completely empty pool grows on the borrow path. The invariant behind the borrow path's
  re-check is named too, and 3.0 turns it into a mechanism rather than a coincidence: every site that
  makes an object available now publishes a signal, so the re-check stops being the only reason a
  waiter sees an object.
- **A shard's ends are padded so neighbouring stripes stop sharing a cache line** (G-4): the padding is
  split across two types — a leading pad on a non-generic abstract base, a trailing pad on the
  most-derived class — because a generic type cannot place a field after the fields its type
  parameters contribute without an extra level of inheritance. No public surface changes. What the
  padding is worth is **not yet measured**: the re-capture that landed with it moved every row, so its
  own contribution is untested rather than proved.
- **The Microsoft.Extensions.Logging bridge moved to `DotNetCore.HayateOP.DependencyInjection`** (L8):
  `HayateMicrosoftLoggerAdapter`, `HayateMicrosoftLoggerAdapter<T>` and `HayateMicrosoftLoggerFactory`
  sat in `DotNetCore.HayateOP.Logging` — the core package's namespace — while shipping in the
  DependencyInjection package, so the namespace pointed readers at a package that does not contain
  them. They now sit in the namespace of the package that does. Source that spelled the old namespace
  keeps compiling: obsolete shells were left behind that derive from the moved types and add nothing.
  Two costs are real — a file that imports both namespaces sees each of the three names twice
  (`CS0104`), and the shell is a separate type from the one the container hands out, so an `is` /
  `typeof` test written against the old name no longer matches.

### Fixed

- **`HayatePool.Simple(n, …)` grows on a miss instead of timing the second borrow out** (B6-②): the
  one-call factory left the reject policy at the library default, `BlockTimeout`, which creates only
  when the pool is completely empty — so a second concurrent borrow waited its timeout out even
  though the pool had room. It now sets `CreateOnDemand`, the policy the presets and
  `ParameterizedHayatePool` already set, and "a pool of `n` objects" is what the call gives you. The
  ceiling behaves differently too: at `n` the request no longer throws — `CreateOnDemand` waits the
  timeout out and then creates, so a pool under sustained overload can end up holding more than `n`.
  A workload that used to see one object and a wall of timeouts now sees objects created; note that
  `CreateOnDemand` reports metrics as off, so count creations in the factory rather than reading
  `TotalCreated`.
- **The package support table now lists the `net48` assets Configuration and HealthCheck ship**
  (T-pk): the README's table put both packages under "All other
  `DotNetCore.HayateOP.Extensions.*` packages — .NET 6/7/8/9/10", which contradicted the prose two
  paragraphs above it and the packages themselves — both carry a `net48` leg.
- **The allocation gate no longer fails on three rows whose reading is not reproducible** (CI
  tooling): `Acquire+Release | Full`, `Async Lean` and `Async Full` carry `allocGateExempt` in the
  baseline, with the reason recorded beside it. All three keep a background maintenance loop —
  eviction, auto-scaling, leak detection and metrics — running across the measured window, so their
  allocation reading varies with when the loop fires rather than with the code under test.
- **A re-captured hot-path baseline is adopted as a whole** (CI tooling): the 3.0 re-capture carries
  the per-target thresholds forward from the committed baseline, so a re-capture no longer reverts
  them. It describes the 3.0 tree; it does not isolate any single change's contribution.
- **The uptime-ratio test no longer races the Windows clock tick** (test-only; no runtime change):
  `StartedAt` comes from a clock whose resolution on Windows is 15.6 ms, so reading the uptime ratios
  immediately after construction could divide by a value the test had not waited for. The test now
  waits the tick out.

## [2.9.0] - 2026-09-22

Asynchronous-contract and logging release, no breaking API change. Highlights: the asynchronous
creation contract (`IHayateAsyncObjectPolicy<T>`, A1) with its disposal counterpart
(`IHayateAsyncObjectPool<T>`, A2) and the asynchronous return hooks that complete the batch
(B5), relaxed `new()` constraints on the twelve entry points that can accept a policy or a
factory (B1), a DI policy factory that resolves through the container (B2), metrics and a
logger on keyed sub-pools (B3), an object health probe and a circuit-breaker verdict in the
health check (B4), `net48` assets for the Configuration and HealthCheck packages (B7),
borrow-side lifetime rotation past `MaxLifeTime` (A3a-α, opt-in and off by default), per-pool
Microsoft.Extensions.Logging categories (L2), a built-in-logger fallback when no
`ILoggerFactory` is registered (L3), a `{Config}` template that no longer carries Serilog's
destructuring operator (L4) and a console logger that no longer re-formats an already
substituted message (L6). The performance baseline was re-captured on CI after A3a-α, and the
emit path now carries the per-target thresholds forward so a re-capture no longer drops them.
Two research follow-ups landed on top of that batch: `HayatePoolStats` gained ratio-class
operational metrics (G-1, with a `MetricsEnabled` flag that tells a closed metrics gate apart
from an idle pool), and the return contract, the pooling break-even line, the `MaxPoolSize = 0`
boundary and an AI-assistant entry point were documented (G-2). The benchmarks also gained a
concurrent head-to-head reference column (G-3, not gated).

### Added

- **Asynchronous creation contract** (A1, `net6.0`+): `IHayateAsyncObjectPolicy<T>`, the policy
  counterpart of `IHayateObjectPolicy<T>` whose `CreateAsync` builds a pooled object without blocking
  the borrowing thread — the shape a connection pool actually needs (`TcpClient.ConnectAsync`,
  `DbConnection.OpenAsync`, an SMTP `STARTTLS` handshake), which today can only be expressed as
  `GetAwaiter().GetResult()` inside the synchronous `Create()`. On ASP.NET Core that occupies a
  request thread; under a `SynchronizationContext` it can deadlock — a structural contradiction for
  a pool that sells itself on performance, not a missing optimisation. The engine dispatches on
  `policy is IHayateAsyncObjectPolicy<T>` at every creation site (general-purpose and lean,
  synchronous and asynchronous entry points), so `Acquire()` on an asynchronous policy waits on
  `CreateAsync` rather than running a second, divergent implementation, while a policy that does not
  implement the interface takes the existing branch unchanged — byte for byte, which is what makes
  the batch additive. `netstandard2.0` / `net48` produce neither the type nor a `Task.Run`
  stand-in: those consumers keep the fully synchronous pool, and the core package keeps its
  zero-dependency policy. First of the three-item asynchronous batch specified in
  [`docs/async-policy.md`](docs/async-policy.md) (A1 → A2 → B5, shipped as one sequence because
  appending members to the interface across two releases would break implementers twice); the
  remaining hooks `OnReleaseAsync` / `OnPassivateAsync` / `OnDestroyAsync` are declared and must be
  implemented now, but are wired by A2 / B5.
- **Asynchronous disposal contract** (A2, `net6.0`+): `IHayateAsyncObjectPool<T>`, an empty marker
  beside `IHayateObjectPool<T>` that adds `IAsyncDisposable`, so a pool can be drained the way
  pooled I/O objects are meant to be torn down — the rolling-restart / graceful-shutdown flow. The
  correct teardown for `NetworkStream`, `SslStream` and `DbConnection` is `DisposeAsync()`; the
  synchronous `Dispose()` either blocks on the network stack or leaves a half-closed connection
  behind. The interface is added beside the existing one instead of onto it (the α form of
  [`docs/async-policy.md`](docs/async-policy.md) §5): putting `IAsyncDisposable` straight on
  `IHayateObjectPool<T>` would force every third-party implementer to add a `DisposeAsync` member —
  a source-level break that belongs in a major release. Callers reach it with one type test:
  `if (pool is IHayateAsyncObjectPool<T> asyncPool) await asyncPool.DisposeAsync();`. The drain is
  implemented by every built-in pool — the sharded engine (general and lean), the preparation
  decorator (forwarding to the inner drain, with the synchronous fallback for a third-party inner)
  and the unbounded pool (reference-drop, so an already-completed task) — disposing each object
  through `IAsyncDisposable` where it implements it and `IDisposable` otherwise, and awaiting
  `OnDestroyAsync` where the policy provides one; the six destroy sites gain the `IAsyncDisposable`
  test. The synchronous `Dispose()` keeps its current semantics for every pool, and the drain's hook
  rules follow the mode it mirrors: the general-mode drain runs a destroy hook only for a policy
  that opted into the asynchronous contract (its synchronous twin never ran one), while the lean
  drain keeps running one for every policy, exactly as its synchronous twin always did.
  `netstandard2.0` / `net48` produce neither the type nor its members: those consumers keep the
  synchronous pool and the zero-dependency core. Second item of the three-item batch in
  `docs/async-policy.md` (A1 → A2 → B5); with A2 in, only `OnReleaseAsync` / `OnPassivateAsync`
  remain unwired, which is B5's work.
- **Asynchronous return contract** (B5, `net6.0`+): built-in pools now await
  `OnPassivateAsync` and then `OnReleaseAsync` whenever the policy implements
  `IHayateAsyncObjectPolicy<T>`; a synchronous `Release()` waits on the same sequence, while a
  synchronous-only policy keeps its existing hooks. `HayatePoolScope<T>` now implements
  `IAsyncDisposable`, allowing `await using` to wait for the return path. The lease remains
  exactly-once and falls back to synchronous `Release()` for third-party pools that have no
  asynchronous return capability. The general engine, Lean mode, and the preparation decorator
  preserve the async path; `netstandard2.0` and `net48` retain their synchronous-only API.
- **Relaxed `new()` on the pool-building entry points** (B1): `HayatePoolBuilder<T>`,
  `HayateUnboundedPool<T>` and the DI / configuration registrations no longer require a public
  parameterless constructor at compile time. This is what a connection-like type needs —
  `new NpgsqlConnection(cs)` has none — and until now the one entry point that can configure a pool
  fully, the fluent builder, was unusable for it at compile time; DI was blocked the same way. The
  requirement did not disappear, it moved: `DefaultHayateObjectPolicy<T>` keeps its own `new()`
  constraint, because it really does create objects with `new T()`, and the default policy is now
  resolved where it is actually needed — `Build()` on the builder, pool resolution in the container —
  where a type that cannot be created that way is reported with a message naming the type and the fix
  (`WithPolicy`, an `IHayateObjectPolicy<T>` registration, or `HayateUnboundedPool<T>(maxIdle, factory)`)
  instead of a compile error. `HayateObjectPolicies.Default<T>()` exposes that resolution, so a caller can
  obtain the library's default policy or decorate it. The entry points that cannot accept a policy keep
  the constraint deliberately, because there it documents a real requirement rather than a historical
  accident: the shared-pool catalog (`HayatePool.Shared<T>`, `HayateSharedPoolRegistry.GetOrCreate*`) and
  the non-generic pool factory build with the default policy and offer no way to supply another — their
  XML remarks now say so. Nothing changes for an existing caller: relaxing a constraint cannot break one,
  and a type with a parameterless constructor still resolves to the same default policy.
  `Extensions.ObjectPoolCompat` drops the `MakeGenericMethod` dispatch it needed to reach the builder
  around that constraint — the MEOP policy it already passes goes straight through.
- **Object-policy factory on the container path** (B2): `RegisterHayatePool<T>` takes a
  `Func<IServiceProvider, IHayateObjectPolicy<T>>`, so a pooled type that has no public parameterless
  constructor can be registered through DI at all — the gap B1 left open on this path — and a policy can
  be told what the container knows. A policy that needs a connection string reads it from configuration
  inside the factory: `RegisterHayatePool<MyConnection>(sp => new ConnectionPolicy(
  sp.GetRequiredService<IConfiguration>().GetConnectionString("db")!), opt => opt.MaxPoolSize = 64)`.
  The factory runs once, when the pool is first resolved, and the policy it returns is a singleton shared
  by every pool of `T`; passing one wins over a policy registered earlier, the way repeated DI
  registrations normally keep the last. The existing overload is untouched, and because a lambda whose
  body yields a policy binds to the new overload while `o => o.MaxPoolSize = 64` still binds to the old
  one, neither call site needs a cast. Registering the policy directly
  (`services.AddSingleton<IHayateObjectPolicy<T>>(sp => ...)`) remains an equivalent route that needs no
  HayateOP call at all.
- **Borrow-side lifetime rotation** (A3a, opt-in, default off): `EnableLifetimeRotationOnBorrow` /
  `WithEnableLifetimeRotationOnBorrow()` retires a pooled object that has outlived `MaxLifeTime` on the
  borrow path and hands out a replacement instead. This is the case HikariCP's `maxLifetime`,
  SQLAlchemy's `pool_recycle` and `SocketsHttpHandler.PooledConnectionLifetime` exist for, and the one
  today's pool cannot express: both paths that evaluate `MaxLifeTime` — the background eviction run and
  `Evict(Expired)` — only ever look at idle objects, so a connection borrowed and then held by the
  application for an hour never ages, and a server-side `max_connection_lifetime` or a middleware idle
  timeout closes it without the pool ever learning. With the switch on, `MaxLifeTime` is a ceiling on
  what the borrow path may hand out, and its documented meaning widens to match — the maximum lifetime
  of a pooled object from creation, idle or borrowed. Off — the default — it keeps exactly the pre-2.9
  meaning, a limit on idle objects only, so no existing pool changes behaviour.
  The check is not a new cost. The borrow path already computes the object's age when
  `EnableGenerationOptimization` is on, which is the default, and rotation reuses that computation;
  against a release without the switch it removes two `Stopwatch.GetTimestamp()` reads and one
  floating-point conversion from every borrow — one of those reads was a dead write, nothing observes
  `LastBorrowedAt` between the two writes, and the remaining age test now compares precomputed ticks
  with integers instead of converting to milliseconds — so the generational promotion itself gets
  faster and the added work is one integer comparison. The replacement is created through the same
  capacity-reservation path a create-on-miss uses, so a lifetime event never degrades into a rejection
  even under `HayatePoolRejectPolicy.Abort`; at most one rotation happens per borrow and the replacement
  is never re-checked, so a `MaxLifeTime` shorter than the time it takes to create an object cannot
  spin. When the per-borrow budget is spent, the aged object is handed out anyway and the event is
  logged — availability outranks lifetime. The asynchronous borrow path applies the same rule.
  `ReloadConfig` does not apply the switch, which is a construction-time decision like the other feature
  switches, but it does refresh the derived tick bounds, because `MaxLifeTime` and
  `GenerationThresholdMs` can themselves be reloaded and a stale bound would let the borrow path and the
  eviction paths disagree about the same object. Rotation is counted in
  `HayatePoolStats.LifetimeRotatedCount` and `HayatePoolSnapshot.LifetimeRotatedCount`.
  Lean cannot honour the switch: that path stores pooled values directly in a bounded array and keeps no
  per-object timestamps, so it has no age to compare. Every other feature lean cannot honour is quietly
  switched off, because the mode wins; this one fails validation instead —
  `IsValid()` returns `false` and `Build()` throws `InvalidOperationException`, in either builder order —
  because silently disabling it is precisely the failure the switch exists to remove, an owner who
  believes objects are rotated on hand-out while they are not.
- **Injectable metrics and logger on a keyed pool** (B3): `ParameterizedHayatePool<TKey, TValue>`'s
  convenience constructor — the one that takes `Func<TKey, TValue> create` — accepts an optional
  `IHayateMetrics` and `IHayateLogger` that every sub-pool it creates uses. It hard-coded the empty sink
  and the built-in no-op logger, so a keyed pool was invisible to OpenTelemetry and to structured logging
  however it was configured — and a pool keyed by connection string is exactly the place where per-key
  visibility matters: `pools.GetObject("tenant-a")` and `pools.GetObject("tenant-b")` were
  indistinguishable in every counter and every log line. Both arguments default to the two that were
  hard-coded, so an unchanged caller sees exactly what it saw before, and the escape hatch is untouched:
  the other constructor still hands you the whole sub-pool to build, which is also how a caller gives each
  *key* its own sink — the two arguments here are per keyed pool, shared by all of its sub-pools.
  Note that an injected sink only records once metrics are on (`EnableMetrics` is off by default) — pass
  `configure: (key, opt) => opt.EnableMetrics = true`, which the callback can also decide per key.
- **Object-level health probe, and a payload that carries the counters** (B4): `HayateOpHealthCheck<T>`
  accepts an `IHayateObjectHealthProbe<T>` and reports on what it says about a real borrowed object. A pool
  knows how many objects it holds and whether its own breaker is open, but it cannot know whether a pooled
  connection still answers — that needs a round trip, and only the code that owns the protocol can make it.
  The check supplies the mechanism (borrow, hand over, take the verdict, give the object back on every path
  including the throwing one); the implementation supplies the meaning, which for a connection pool is a
  PING. `RegisterHealthChecks<T, TProbe>()` wires one up from the container; the probe defaults to `null`,
  which is exactly the check that existed before. The probe is skipped when every slot is busy rather than
  waited for: a health check that blocks is worse than one that reports less, and "no slot free" is already
  the degraded answer. The `data` payload also grew from six fields to fourteen, and the eight new ones are
  the ones an operator actually acts on — `TotalAcquired`, `TotalDestroyed`, `CurrentSize`,
  `LeakDetectedCount`, `LeakSuspectedCount`, `AbandonedRemovedCount`, `LifetimeRotatedCount` and
  `CircuitBreakerOpen` — under the same names `HayatePoolStats` gives them. The timing and allocation
  averages were left out on purpose: they describe how the pool has been used, not whether it is usable,
  and two of them read `double.MaxValue` until their first sample.

- **net48 assets for the Configuration and HealthCheck packages** (B7): both now target `net48`
  alongside `net6.0`–`net10.0`, so binding a pool from `appsettings.json` and putting it behind
  `/health` work on a .NET Framework host — neither was available there at all, which made them the
  odd ones out among the extension packages: `DependencyInjection`, `ObjectPoolCompat`, `Specialized`
  and `Buffers` already shipped a net48 asset. A .NET Framework consumer therefore had the pool and
  the container registration, but not the two things that usually decide how it is configured and
  watched. No source change was needed: the `Microsoft.Extensions` 8.x packages these depend on carry
  netstandard2.0 / net462 assets, so net48 takes the 8.x line — verified by building it, not by
  reading the dependency graph. The dependency surface of the core package and of the other extension
  packages is untouched; only these two projects gained a condition. `OpenTelemetry` stays without a
  net48 asset (the SDK's .NET Framework support is narrow and it is not required by downstream),
  and `Endpoints` / `Diagnostics` remain not applicable to net48. Both packages' legacy suites now
  run on net48 in CI as well — building an asset is not proof that it loads, and what a .NET Framework
  consumer actually hits is binding redirects and mixed assembly versions.

- **Ratio-class operational metrics on `HayatePoolStats`** (G-1; additive, one behaviour change for
  existing readers): the statistics object could describe how much work a pool had done but not how
  well it was doing it. It gains six members — `ReuseEfficiency` (the share of borrows served from the
  pool rather than by a creation), `CreatesPerAcquire`, `AcquiresPerSecond`, `PeakActiveObjects`,
  `StartedAt` / `UptimeSeconds` and `LastActivityTime` — plus `MetricsEnabled`. These are the
  ratio-class counterparts of the four average-class members that were already there
  (`AverageWaitTimeMs`, `AverageLeaseTimeMs`, `AverageAcquireAllocatedBytes`,
  `AverageReleaseAllocatedBytes`), which are unchanged, as are all twenty-eight pre-existing fields:
  the new members are appended to the class, so no existing property changes type or meaning. The one
  place an existing reader does notice the addition is `ToString()`, whose rendering gains an
  `[Operational (gated by EnableMetrics)]` section — and an unset timestamp there prints as `-`
  rather than as year 1, the same treatment `MinWaitTimeMs` has always had.
  They are derived on read, so a caller holding a stats object sees the pool's current state rather
  than the state at the moment of the read; every denominator is guarded, so a pool that has not been
  borrowed from reports `0` rather than `NaN` or `Infinity`, and `ReuseEfficiency` is clamped into
  `[0, 1]` because `TotalMissed` also counts a borrow the reject policy refused — a borrow that never
  becomes a `TotalAcquired`, so the difference can go negative on a pool that rejects under load.
  **The behaviour change:** these members are maintained only while `EnableMetrics` is on, so an
  existing reader that runs with the default configuration (metrics off) now sees them at `0`,
  `null` or `default` rather than at a value. That is deliberate and it is the reason `MetricsEnabled`
  exists: `TotalAcquired` is written unconditionally while `TotalMissed` is not, so an ungated
  `ReuseEfficiency` would report a **perfect 1.0** on a pool that has never been measured — the one
  reading that looks like a measurement and is not. The flag lets a reader tell "the gate is closed"
  from "the pool is idle", which a bare zero cannot; a caller who wants the numbers turns
  `WithEnableMetrics(true)` on. Two of the six are tracked rather than derived, and both are gated at
  their own write site: `StartedAt` is stamped once in the constructor, and `LastActivityTime` on
  every borrow and every return branch (including a return the soft ceiling dropped or the policy
  rejected, because an attempted return is still activity). `PeakActiveObjects` is a high-water mark
  **sampled by `GetStats()` and `TakeSnapshot()`** rather than counted on the borrow path: the engine
  keeps no paired borrow counter — the shard's `TryTake` physically unlinks the object before handing
  it out, so the borrowed count is derived as `TrackedObjectCount - Σ shard.Count` at both read sites
  already — and counting it per borrow would put that walk, with one `SpinLock` per shard, on the
  borrow path that `EnableMetrics` exists to keep cheap. The trade-off is stated rather than hidden: a
  burst that starts and ends between two reads is recorded only if it is still in flight when one of
  them runs. The cost when the switch is off is one predicted branch per borrow and per return, on a
  `readonly` field the JIT folds away for a constant `false`; the lean profile, which closes the
  master diagnostic switch, reports every one of the new members at its "not measured" reading
  explicitly rather than leaving it to a default. `HayateUnboundedPool<T>` has no switches, so its
  ratios are live from the start and it reports `MetricsEnabled = true`; it does not track borrowed
  objects at all, so its peak stays 0 rather than inventing a number from a count the model does not
  keep. The `Specialized` aggregation sums the counters and merges the operational members — earliest
  origin, latest activity, highest peak — so a tiered pool's statistics stay a view of the whole pool.
  The audit of what the switch covers is in [`docs/metrics-gating.md`](docs/metrics-gating.md); where
  the cost lands is in [`docs/hot-path-costs.md`](docs/hot-path-costs.md).

### Changed

- **The return contract, the pooling break-even line and `MaxPoolSize = 0` are now stated where they
  belong** (G-2; documentation): `docs/ownership.md` is a new page for the rule that had no single
  home — returning an object transfers ownership, and the violations are not equally detectable (a
  foreign object and a double return while the object is idle are caught and destroyed; touching an
  object after its return is invisible; a double return after the object has been borrowed again is
  accepted and leaves two owners of one object, with no exception anywhere). It also states where the
  rule is not enforced at all: the lean profile keeps no registry, so it accepts and pools a foreign
  object — the same contract the reference zero-wrapper pool offers, and the deliberate price of
  removing the reverse lookup from the return path. `docs/hot-path-costs.md` gains §5, the question
  that comes before every other switch on that page: whether to pool at all. The line is
  `Create()` > `Acquire + Release`, evidenced by a published pool whose round trip charges a
  semaphore, a lock and a wrapper to avoid a ~6 ns allocation and lands 9.3x slower, and HayateOP's
  own two configurations are placed against it (lean 28.07 ns / 0 B; the general engine with every
  optional feature off 298.61 ns / 384 B — the CI baseline of 2026-09-22). **The `MaxPoolSize`
  documentation was wrong and is corrected**: it does not merely disable cold-boot creation. The
  value is divided into per-shard capacity, so `0` gives every shard a ceiling of `0` and a returned
  object is rejected and destroyed rather than retained. That was measured rather than reasoned:
  with `MaxPoolSize = 0` a borrow times out with nothing created, while the same object on a
  `MaxPoolSize = 1` pool is retained and lent out again by reference. `0` therefore neither creates
  nor retains — at least as strong as the "capacity 0 retains nothing" reading some other pools use,
  not a different thing — and the remark now says so, warning that some third-party pools read `0`
  as "no limit". The fourth item is `docs/ai-assistant-guide.md`: a single page for an AI coding
  assistant, carrying the shape decision (whose first three questions can rule pooling out
  altogether), the canonical skeleton, five things not to do, and fourteen facts that are easy to
  get wrong, each checked against the source.

- **An open circuit breaker is no longer reported as a healthy pool** (B4; behaviour change, not
  breaking): `HayateOpHealthCheck<T>` decided healthy-versus-degraded on `AvailableSlots` alone, so a pool
  whose breaker had tripped — a pool that refuses every borrow — was reported `Healthy` as long as it still
  happened to be holding idle objects. It is reported `Unhealthy` now, and the breaker state is in the
  payload as `CircuitBreakerOpen`. The two remaining verdicts are unchanged and asked in a fixed order: all
  slots busy is `Degraded` (the pool still works, the next borrower waits), and a probe that rejects the
  object it was handed is `Unhealthy`. A host that alerted on "the pool is degraded" is unaffected; one that
  read "not unhealthy" as "the dependency is fine" now gets the honest answer, which is the point.

- **A container with no logging provider no longer silences the pool** (L3; behaviour change, not
  breaking): the container and configuration paths asked for `ILoggerFactory` and, when there was none,
  wrapped the null in the Microsoft.Extensions.Logging adapter — which discards everything. A host that
  never called `AddLogging` has not asked for logging to be *off*; it has asked for no logging provider,
  and answering that with silence makes a pool with something to say indistinguishable from a quiet one.
  The fallback is now the built-in logger, which is what a bare `HayatePoolBuilder<T>` resolves, so the
  two paths agree — and on a DEBUG build the pool's warnings reach the console again, which is where they
  went before the container ever wrapped them. A host that *did* register an `ILoggerFactory` is
  unaffected: the fallback lives in `HayateMicrosoftLoggerFactory` and only fires when it was handed null.

- **Per-pool Microsoft.Extensions.Logging categories** (L2; behaviour change, not breaking): every pool
  registered through the container or through configuration now logs under its own MEL category — the
  pool name — instead of sharing one. Both paths used to hand each pool a logger built from
  `CreateLogger<T>()`, which is a single category for the unnamed pool and every named pool of the same
  element type, so `AddNamedPool<MyConnection>("primary")` and `AddNamedPool<MyConnection>("replica")`
  were indistinguishable downstream: a per-pool log level, a filter or a sink could not be expressed
  against them at all. The categories are now `MyConnection` for the unnamed pool and
  `MyConnection:primary` / `MyConnection:replica` for the named ones — the same names those pools already
  report in their own log lines, statistics and snapshots. Serilog, NLog and log4net all reach HayateOP
  through the MEL bridge, so routing MEL by category routes all three at once.
  One consequence to be aware of: the unnamed pool's category narrows from Microsoft.Extensions.Logging's
  own choice for `CreateLogger<T>()` — the namespace-qualified display name of the element type, such as
  `MyApp.MyConnection` — to the short type name `MyConnection`. That is what
  `IHayateLoggerFactory.CreateLogger` has always documented (the pool name, "or the element type name if
  unspecified"), and it is what makes the unnamed pool's category equal to its pool name, but a host that
  sets a log level or a filter against the namespace-qualified name has to update that configuration.
  Nothing in code has to change — this is configuration, not API.
  The switch is implemented as `HayateMicrosoftLoggerFactory`, a non-generic `IHayateLoggerFactory` that
  calls `CreateLogger(categoryName)`; the two places that used to pass a logger to `WithLogger` directly
  now pass it to `WithLoggerFactory`, so the builder's existing resolution rule still applies — an
  explicit `WithLogger` still wins over the factory, and a host with no `ILoggerFactory` registered gets
  no-op loggers exactly as before. `HayateMicrosoftLoggerAdapter` gains a non-generic form wrapping any
  `ILogger`; `HayateMicrosoftLoggerAdapter<T>` keeps its constructor and members and now derives from it.

### Fixed

- **A log call that could throw while reporting something** (L6; DEBUG builds only): the built-in console
  logger substituted the template's placeholders and then handed the result to `string.Format` a second
  time. By then there was nothing left to substitute — unless a substituted *value* happened to contain a
  brace, in which case the second pass read the value as a template of its own and threw
  `FormatException`. A pool that logged an object whose `ToString()` contains braces crashed on the line
  that existed only to report it, which is a poor way to find out. The second pass is gone. For a value
  without braces the rendered text is byte-identical — the format specifiers (`{Size:D3}` and friends) are
  applied by the substitution pass itself, not by the one that followed it — so the change is visible only
  when it saves you from the crash. Note that this is the DEBUG-only console logger; the Release build's
  empty implementation and every structured sink are unaffected.
  Coverage note: because the implementation is `#if DEBUG`, these cases compile to nothing on a Release
  build, and CI runs Release — a Debug job was added for the suite that holds them.

- **A Serilog-only operator in a pool log template** (L4): the "configuration reloaded" message addressed
  its payload as `{@Config}`. `@` is Serilog's destructuring operator — in a Serilog template it means
  "capture this object's members instead of calling ToString() on it" — and this message never reaches a
  Serilog template parser. It goes through Microsoft.Extensions.Logging, which copies the hole verbatim
  into a structured property literally named `@Config`. So the operator did nothing and the name was
  wrong: a sink filtering on `Config` found nothing, and what arrived was a scalar called `@Config`
  rather than the options object it is. The template now reads `{Config}`. Measured, because reasoning
  alone gets this backwards: the *rendered text* is byte-identical between the two spellings — MEL
  substitutes positionally and calls `ToString()` either way — so this changes the structured payload
  only, never what a human reads. A host that keyed a Serilog/Seq/OTel sink off `@Config` has to rename
  it to `Config`; nothing else moves.

- **The create-on-demand concurrency cases no longer measure the thread pool** (test-only; no runtime
  change): `CreateOnDemand_BelowCapacityMisses_ShouldEachCreateWithoutWaiting` timed the whole
  four-request burst and failed the net48 CI leg at 1984ms against a 1500ms bound. Nothing had waited
  out a timeout — the burst total landed *below* the 2s acquire timeout the pool is built with, and a
  request that degraded to wait-then-create can only return after that timeout, so the policy was
  provably not at fault. The 1984ms was the .NET Framework thread pool injecting the four `Task.Run` +
  `Barrier` participants on a 4-core runner, which happens before `Acquire` is ever entered. Both
  generations now time each request from the barrier release and assert on the slowest one, keeping
  scheduling outside the pool out of the measurement while a genuine wait-out regression still lands at
  ≥ 2s and trips the bound (verified by switching the burst to `CreateNew`, which fails the case at
  2072ms). The MEOP compat case `HayateCompatProvider_ConcurrentMissesBelowCapacity_AllServed` takes the
  same per-request timing, and its acquire timeout moves from 100ms to 2s: at 100ms a degraded `Get`
  returned inside the old 500ms burst bound, so that assertion could not detect the regression it was
  written for.
- **`Extensions.Endpoints` builds warning-free on `net10.0` again** (ASPDEPR002; no behaviour change):
  `WithOpenApi` — the `Microsoft.AspNetCore.OpenApi` extension that attached each operation's summary
  and description — is obsolete from .NET 10, and the package used it at all seven call sites, so the
  project emitted 14 diagnostics that broke the "`src` builds with zero warnings" bar every other
  project in the tree already held. On `net10.0` and later the framework's own `WithSummary` /
  `WithDescription` (from `Microsoft.AspNetCore.Routing`) carry the same metadata into the generated
  document; `net6.0` keeps its Swashbuckle branch, and `net7.0` / `net8.0` / `net9.0` keep
  `WithOpenApi`, which is neither obsolete nor replaced on them. The warning was pre-existing — the
  asynchronous batch never touched this package — and surfaced only when the batch-end gate swept every
  `src` project on a clean build instead of the core package alone.
- **A policy registered directly in the container is no longer replaced by the library default** (B2;
  behaviour change, not breaking): `RegisterHayatePool<T>` appended
  `AddSingleton<IHayateObjectPolicy<T>>(...)` unconditionally, so an application that registered its own
  policy *before* the call had it silently discarded — the library's default was resolved instead, because
  DI returns the last registration. It now uses `TryAddSingleton`, matching `AddNamedPool<T>`, so the
  application's policy wins whether it is registered before or after; `RegisterHayatePool<T>` on its own
  still falls back to the library default. Only a caller that registered a policy and expected it to be
  overridden is affected, which is the opposite of what registering one means; the factory overload above
  is unaffected and still wins over an earlier registration, as repeated registrations do.

- **A re-captured performance baseline no longer reverts the per-target thresholds** (CI tooling,
  `scripts/bench-compare.py`): `build_baseline_json()` wrote the measurements, the schema and the
  global thresholds, and nothing else — so the PG4 convergence, which is expressed as
  `warnMeanPercentOverride` / `failMeanPercentOverride` on individual rows, was dropped every time
  the `update_baseline` workflow emitted a fresh `ci-baseline`. A benchmark report cannot carry
  those keys, so the three sub-100 ns lean rows came back on the global 15/30 — a threshold their
  own run-to-run spread exceeds (~1.9× measured locally, 2.4× on the sub-50 ns rows), which would
  have turned the gate from never blocking into failing on ordinary noise, and the failure would
  have read as a regression. The emit path now inherits the curated per-target keys from the
  baseline it is replacing, matched by method name; `--baseline` already points at that file, so
  the workflow step needs no change. A method absent from the previous baseline is emitted exactly
  as before, and a capture that cannot read the previous baseline prints a `::warning::` rather
  than silently writing an un-stamped file. Verified end to end with synthetic reports: after an
  emit the three rows keep 30/60 while their measurements come from the report, a second-generation
  emit keeps them too, and the same +40% slowdown on `Acquire+Release | Hayate Lean` lands as a
  warning (exit 0) against the inherited baseline and as a failure (exit 1) against one emitted
  without it. The 2026-09-22 capture had been re-stamped by hand before this fix, which is what
  made the defect worth closing instead of documenting a second time.

## [2.8.0] - 2026-09-20

Pool-model expansion and specialization release, no breaking API change. Highlights: a
second pool model with lease-is-ownership semantics (`HayateUnboundedPool<T>`, N1), an
asynchronous preparation / reconnect decorator (`HayatePreparationPool<T>`, N3), the
ArrayPool direct-storage backend for the lean fast path (O-D), abandoned-object recovery
(K2), named pools with typed clients and a run-time pool factory (N4), the diagnostics
master switch (O11), a core `System.Diagnostics.Metrics` meter (N6), eight configuration
presets (C6), explicit borrow order and a pluggable eviction rule (O8 / O7), process-wide
shared pools (O-C), on-demand pre-warming (O-F), return-path soft capacity (T-R), the
bucketed buffer pool package (O-H), the `Deterministic` preset (O-G) and the
string-building specialization surface (Z1 / Z4a / Z5 / Z6 / Z-C-A). The asynchronous
preparation borrows now await the inner pool and honour the timeout (A4), and the
performance gate was promoted to a blocking, allocation-aware compare (Q1).

### Added

- **Deterministic preset** (O-G): `HayatePoolPreset.Deterministic` — the preset for hosts where nothing
  may run without the caller asking for it (AOT / IL2CPP runtimes and other deterministic
  environments). It is the lean fast path — no maintenance timer, no per-object bookkeeping, which
  Lean already guarantees structurally by disabling every concern the shared timer serves — plus the
  two switches Lean leaves open that could still move work off the calling thread, both closed:
  pre-warming stays synchronous with the constructor (`WaitForWarmup` off, so no background pre-warm
  task) and the pool never subscribes to process-exit events (`EnableAutoDisposeWithSystem` off, so
  `Dispose` happens only when the caller calls it). The built pool touches nothing except on an
  explicit `Acquire` / `Release`. The preset owns exactly its field set, so it composes like the other
  presets: sizing, timeouts and the reject policy are left alone, and every owned switch is
  overrulable by a later feature call — re-enabling eviction after the preset reintroduces a timer by
  choice rather than by surprise. The preset also pins the core package's AOT story: a single
  `netstandard2.0` assembly with no dependencies, a borrow/return path free of reflection and code
  generation — see the Unity / IL2CPP guidance in the README (empirical IL2CPP validation on a real
  Unity project remains backlog). Note that `Deterministic` and `Lean` therefore differ in two
  options, not in storage or speed; the preset exists to name the whole guarantee so a host audit can
  check one word.

- **Bucketed buffer pool** (O-H): the new optional package
  [`DotNetCore.HayateOP.Extensions.Buffers`](https://www.nuget.org/packages/DotNetCore.HayateOP.Extensions.Buffers)
  ships `HayateBufferPool<T>` — a bucketed array pool with round-up-to-nearest-size routing, the
  aligned counterpart of TinyPools' `MemoryPool<T>` + `SegmentDefinition` under HayateOP naming and
  validation conventions. The bucket ladder is declared up front
  (`new HayateBufferPool<byte>(new HayateSegmentDefinition(256, 4), …)`), a rent request routes to the
  smallest bucket whose size covers it and is handed back as a `PooledBuffer<T>` whose `Dispose` (or
  `Return`) parks the array in its home bucket, and a request above the largest declared size is
  rejected with `ArgumentException` rather than served by an undeclared bucket — the ladder is exactly
  what was configured. Each bucket creates arrays on demand and caps only what it *retains*: any
  number of buffers may be outstanding at once, and a return past the bucket's declared capacity is
  dropped for the garbage collector, so the pool bounds retained memory without limiting concurrency.
  Buckets guard their stacks with one lock each, so different sizes do not contend, and the pool runs
  no background workers. Returned arrays are not cleared — the same contract
  `ArrayPool<T>.Shared` has. This is deliberately an independent package rather than core surface:
  `System.Buffers.ArrayPool<T>` covers most workloads, and the value here is the explicit, inspectable
  configuration; it targets `net48` and `net6.0`–`net10.0` and has no dependencies. See the "Bucketed
  buffer pool" section of the README.

- **Soft capacity** (T-R): `HayatePoolOptions.SoftCapacity` (builder: `WithSoftCapacity(n)`) bounds the
  pool's *retained* set on the return path — once the pool already holds `SoftCapacity` idle objects, a
  returned object is disposed instead of stored, so a pool that lent out its whole hard ceiling during a
  burst drops the objects the burst no longer needs instead of keeping them until eviction or scale-down
  reclaims them. The default `0` keeps the previous behaviour of retaining up to `MaxPoolSize` idle
  objects, and the switch is additive: `MaxPoolSize` still caps what the pool ever lends out, so a pool
  with a soft capacity below its hard ceiling still serves the full ceiling — it only keeps fewer
  objects back. The check is a read of the idle count followed by the store, so two returns racing for
  the last slot may both be retained — it is a memory-shape control, not a mutual-exclusion guarantee —
  and the value must sit between `MinPoolSize` and `MaxPoolSize` (or be zero), because a ceiling below
  the floor would make the minimum unreachable and one above the hard ceiling could never fire; both
  are rejected by `IsValid` rather than accepted as a knob that silently does nothing. `ReloadConfig`
  rejects a change the same way, because the return path reads a construction-time snapshot (in lean
  mode the ceiling also fixes the fast path's slot-scan limit) and a runtime change could not take
  effect. The lean fast path honours the ceiling structurally — the fast lane plus
  `SoftCapacity - 1` slots — so it costs the return hot path nothing beyond the existing slot scan, and
  the unbounded pool model does not read it: that model's retained set is already bounded by its own
  `MaxIdle`, which drops a return once the queue is full. Dropped objects go through the regular
  destroy path (the policy's `OnDestroy` fires, the registry entry is removed) and the outcome is
  traced at debug level, not warned — a warning would fire once per return through a burst drain. See
  the "Soft capacity" section of the README and the return-path table in
  [`docs/hot-path-costs.md`](docs/hot-path-costs.md).

- **Shared pools** (O-C): `HayatePool.Shared<T>()` returns the process-wide pool for an element type —
  the first caller builds it, every caller after that gets the same instance — so a type has one pool
  instead of one per call site. It is the "just give me a pool" entry point beside
  `HayatePool.Simple<T>`, which builds a fresh pool on every call; the difference is ownership, not
  capability. A shared pool is an ordinary `IHayateObjectPool<T>` with the full configuration surface,
  created with the default settings and starting empty, so the first borrow pays the object-creation
  cost exactly once. Because creation happens once, only the call that creates the pool can configure
  it: the `configure` overload applies the callback while the pool is built, and a later call returns
  the existing instance and ignores its callback, rather than reconfiguring a pool that other callers
  are already using or making the shape depend on call order. `TryGet` is there for the caller that
  needs to know which of the two it is looking at, and `GetOrCreateNamed<T>(name, …)` — deliberately a
  separate method rather than an overload, so that a `null` name cannot bind to the delegate-taking
  member and quietly build the default pool — gives one element type several independently configured
  shared pools under the same logical names `HayateServiceKey` and the named DI registrations use.
  The catalog, `HayateSharedPoolRegistry`, is a thin layer over the existing
  `IHayateObjectPoolRegistry` rather than a second registry: it registers under the canonical name
  (`MyBuffer:shared`, which is also the pool name it logs and reports under), so `GetAll()` enumerates
  shared pools with their metadata and a catalog handed the application's registry makes them visible
  to the management endpoints, metrics and diagnostics beside the DI-created ones, and a pool that
  registry already holds is honoured instead of being shadowed. It is a normal class with a
  `Default` singleton on top, so a test or a subsystem can have a catalog of its own.
  Ownership is explicit and one-directional: the catalog owns the pools it creates, so `Remove<T>(…)`,
  `Clear()` and `Dispose()` dispose them — the opposite of `IHayateObjectPoolRegistry.Remove`, which
  only unregisters and leaves the lifetime to the registrar — while a pool the catalog did not create
  is never touched, which is what lets it share a registry with DI-managed pools. After `Dispose` the
  catalog reports empty and rejects further creation but keeps answering lookups, and creating is
  serialized, so simultaneous first calls for one key yield one pool rather than a race where the loser
  leaks a background-timer pool nobody can reach. A borrower must not dispose the pool it was handed:
  the catalog, not the caller, decides when a shared pool goes away. No existing signature, option or
  default changed; `Shared<T>()` is an addition to the one-call entry-point class, and nothing
  pre-existing routes through the catalog. See the "Shared pools" section of the README.

- **On-demand pre-warming** (O-F): `IHayateObjectPool<T>.PreWarm(int count)` tops the pool up to a
  requested number of idle objects and returns how many it created, so a process can pay the creation
  cost up front instead of letting the first borrowers pay it. The count is a floor on *idle* objects,
  not on total objects — objects currently lent out do not count towards it, so warming after a burst
  creates replacements rather than finding the pool already full. The pool's own ceiling still applies,
  the maximum pool size in the sharded engine and the retained-object limit in the unbounded model, and
  a request above the ceiling warms to the ceiling instead of failing. A negative count is rejected with
  `ArgumentOutOfRangeException`, and a creation failure is reported with `InvalidOperationException`
  rather than logged and swallowed: the construction-time warm-up carries on because the pool is still
  usable, but here the caller asked for the objects explicitly. The call is safe alongside concurrent
  borrows and returns, and concurrent calls never take the pool past its ceiling — a caller that finds
  the target already met creates nothing and returns `0`. This is the explicit counterpart of the
  construction-time warm-up switch rather than a replacement for it: construction warms the configured
  minimum, synchronously or in the background according to `WaitForWarmup`, while this call warms any
  target at any time — after a configuration reload raised the minimum, or to pre-pay creation without
  making the first borrow wait for it. Warming beyond the configured minimum is not a retention promise,
  so the extra objects age like every other idle object and are reclaimed by the idle timeout and by
  background scale-down; that the options themselves are left alone is checked rather than asserted, by
  a case that warms a pool and then verifies `MinPoolSize` and `MaxPoolSize` are exactly what they were.
  Every pool model answers the call. The sharded engine creates the shortfall round-robin and reads
  capacity per shard instead of deriving it from the configured maximum, so a pool that has already
  scaled down warms to what it currently allows and a shard that loses the last slot to a concurrent
  return has the object destroyed through the single destroy path rather than the live count pushed past
  the ceiling. The lean fast path creates through its reservation path, which enforces the ceiling on the
  live count. The unbounded model claims a slot and parks the object in the same claim-then-park order a
  return uses, which keeps its resident count and its idle queue in lockstep under concurrent borrows,
  returns and warm-ups; having no background warm-up of its own, it is the model where this call is the
  only way to make the first borrows cheap. The preparation decorator forwards to the inner pool, as it
  does for eviction, and the specialized pools warm their default-capacity base pool rather than the
  tiering layer. A warmed object is not a prepared one — warming creates and stops there, so every borrow
  still runs the preparation chain and the first borrows pay the readiness check and any repair exactly
  as before. No existing signature, option or default changed; `PreWarm` is an addition to the interface,
  alongside `Evict`. See the "Pre-warming the pool" section of the README.

- **Configuration presets** (C6): `HayatePoolPreset` names eight configurations for the shapes the pool
  is most often asked to take — `Default` (the shipped defaults), `Lean` (the wrapper-free fast path),
  `Full` (every feature switch on), `HighThroughput` (scale up early and in large steps, retain objects
  well past a lull, bookkeeping off), `LowLatency` (no background pass and no optional bookkeeping that
  can take a shard lock, first acquire waits for the warm floor), `MemoryConstrained` (one shard, zero
  idle floor, quick reclamation), `ConnectionPool` (bounded capacity, validate before a handle is handed
  out, scheduled recycling plus abandoned-handle reclaim, circuit breaker, metrics) and `BatchProcessing`
  (react every second, grow in steps of ten, release the burst capacity once the batch drains). All three
  entry points route through one catalogue — `HayatePoolPresets.Create(preset)`,
  `HayatePoolOptions.UsePreset(preset)` and `HayatePoolBuilder<T>.WithPreset(preset)` — and the builder
  also accepts a caller-supplied `HayatePoolOptions`, which is how a team keeps a named configuration of
  its own beside this one. A preset is a starting point rather than a lock: it assigns the options it owns
  and leaves every other option at the value it already had, so a preset composes with ordinary
  configuration, and any option it does own is overruled by a later `With*` call. That contract is checked
  rather than asserted — a case primes every writable option with a value other than the shipped default,
  applies each preset, and verifies that owned options follow the preset while unowned ones survive
  untouched. Sizing, timeouts and the reject policy are deliberately not owned, because the right ceiling
  depends on the machine and the workload; `MemoryConstrained` is the exception in that direction, where
  a zero idle floor *is* the trade-off, and it therefore also selects
  `HayatePoolRejectPolicy.CreateOnDemand`, since the block policies only shortcut creation while the pool
  tracks nothing and the next borrower would otherwise wait out the whole acquire timeout. `Default` is
  the exception in the other direction and owns the whole surface, sizing and callbacks included, so it
  doubles as an explicit reset to the shipped behaviour. An unrecognised preset value is rejected rather
  than ignored — the same fail-fast rule the borrow-order switch follows. No existing option, signature or
  default changed. See the "Configuration presets" section of the README.

- **Explicit borrow order** (O8): `HayateBorrowStrategy` selects which end of a shard's idle list a
  borrow is served from, and `HayatePoolBuilder<T>.WithBorrowStrategy(...)` installs it. `Fifo` — the
  default, and the order every release before the switch used — hands out the longest-idle object;
  `Lifo` hands out the one that came back most recently, which keeps a small working set hot in the CPU
  caches and is the order the reference CHOPIN pool hard-codes. The idle list itself is unchanged: it
  is still kept in return order, so its head is the oldest-returned end under both strategies, the
  eviction scan therefore keeps offering the coldest objects to the `IHayateEvictionPolicy<T>`, and
  abandoned-object recovery keeps walking its own list oldest-borrow-first. The switch governs the
  general-purpose engine; the lean fast path keeps no ordered idle list, so `Lifo` on a lean pool fails
  the build instead of being accepted and ignored — the outcome this item exists to remove. An
  unrecognised enum value falls back to `Fifo`, the same robustness rule the shard-affinity mode
  follows. On the borrow path the strategy costs one predicted branch on a constructor-time flag, so a
  pool that does not set it behaves and measures exactly as before. See the "Borrow order" section of
  the README.
- **Pluggable eviction rule** (O7, the counterpart of CHOPIN's `EvictionPolicyClassName`):
  `IHayateEvictionPolicy<T>` decides whether an idle object leaves the pool during the background
  eviction run and is installed with `HayatePoolBuilder<T>.WithEvictionPolicy(...)`. The policy is
  asked about every idle candidate and its verdict is what the run acts on — it **replaces** the
  built-in rule rather than extending it, so a policy that always returns `false` keeps every idle
  object. A pool that installs none asks `HayateDefaultEvictionPolicy<T>.Instance`, which is exactly
  the rule the run applied before the extension point existed (expired lifetime, exceeded idle time,
  or soft-min idleness above the shard's share of `MinPoolSize`), so existing behavior and the
  eviction log line are unchanged. A candidate carries everything a rule needs without reaching into
  the options — the idle instance itself, its `Age`, its `IdleTime`, its `LeaseCount`, the shard's
  `IdleCount` and its `MinIdleCount`, and the effective `MaxLifeTime` / `MaxIdleTime` /
  `SoftMinEvictableIdleTime` — which also keeps a policy correct across a configuration reload. Only
  idle objects are offered: a borrowed object never reaches a policy, and no verdict can destroy one
  under its borrower, because the shard re-checks ownership in its claim protocol before anything is
  removed; each shard contributes at most `NumTestsPerEvictionRun` candidates, taken from the head of
  its idle list. Installing a custom policy on a pool that never runs an eviction scan
  (`EnableEviction = false`, or the lean fast path, which disables idle eviction by construction)
  fails the build instead of leaving a rule that can never be consulted. Abandoned-object recovery
  (K2) and the explicit `Evict(reason)` API do not go through the policy. See the "Custom eviction
  policy" section of the README.
- **A `System.Diagnostics.Metrics` meter in the core package** (N6): the pool instrument set —
  `HayatePoolAcquire`, `HayatePoolRelease`, `HayatePoolMiss`, `HayatePoolScaled`, the
  `HayatePoolWaitTime` histogram and the `HayatePoolSize` / `HayatePoolAvailable` observable gauges —
  is now published by `HayateMetricsMeter` on a meter of the core package's own (name `"HayateOP"`),
  so a consumer that references nothing but `DotNetCore.HayateOP` can observe a pool with
  `dotnet-counters monitor --counters HayateOP`, or through the OpenTelemetry .NET SDK's
  `AddMeter("HayateOP")`, and needs no exporter package. The instrument names, units, tags,
  descriptions and the pull-based gauges live in that one implementation: the OpenTelemetry package's
  `HayateOtelMetrics` is now the meter-name and packaging front for it and keeps its
  `"DotNetCore.HayateOP"` meter name and its public tag constants (which now alias the core ones), so
  existing subscriptions and the instrument inventory are unchanged. A pool takes **one** sink — the
  core meter or the bridge meter — rather than both, since both publish the same instruments and
  wiring both would count every event twice. `Meter` is part of the .NET 6+ base class library, so
  the type is compiled out on netstandard2.0/net48 rather than pulling a NuGet dependency into the
  core package, and the gauges remain pull-based: their callbacks run on collection only, never on the
  borrow or return path. See the "OpenTelemetry metrics" section of the README.
- **Named pools, typed clients and a run-time pool factory** (N4, extending the 2.5 M11+ registry):
  several independently configured pools of one element type can now coexist and be addressed by
  name. `HayateServiceKey.Create<T>(name)` is the identity a named pool is addressed by — the element
  type plus a logical name, registered under the canonical name `{TypeName}:{name}` so logs,
  statistics, snapshots and the management endpoints tell the pools apart, while the unnamed pool of
  the same type keeps its bare type name. On the DI side `AddNamedPool<T>(name, configure)` declares
  one, `IHayateNamedPoolAccessor.GetPool<T>(name)` resolves it (and `GetPool<T>()` resolves the
  unnamed one), and `AddPool<T, TClient>(name)` binds a client class to a named pool, the way
  `AddHttpClient<TClient>()` does — the binding is decided at registration, so the client keeps a
  plain constructor taking `IHayateObjectPool<T>` and needs no attribute. The unnamed
  `IHayateObjectPool<T>` registration stays reserved for `RegisterHayatePool<T>()`, so "the pool of
  `T`" never silently resolves to one of several named pools. Pools are built on first use, and
  resolution never creates one on demand: an unknown name throws instead of silently starting an
  unconfigured pool. `RegisterNamedHayatePool<T>(configuration, name)` is the configuration-driven
  counterpart, merging `HayatePool:Global` with `HayatePool:Pools:{name}` exactly as
  `RegisterHayatePool<T>` does, hot reload included. Outside a container `HayatePoolFactory` builds
  named pools at run time — `GetOrCreate` returns the pool already registered for a key and only
  builds one when the key is new, which is what a multi-tenant or plugin host needs. Named pools are
  deliberately **not** built on Microsoft.Extensions.DependencyInjection keyed services: those only
  exist in version 8.0 and later of the abstractions package, so a keyed registration would be
  unavailable to the net6.0 / net7.0 targets this library ships, and would couple net48 behavior to
  the version of `Microsoft.Extensions.DependencyInjection` the application happens to resolve.
  Instead a named pool is one instance of `HayateNamedPoolRegistration<T>` in the container — the
  container's own "many registrations of one service type" facility, available on every supported
  target — and the observable behavior is identical from net48 through net10.0: one lazily built
  singleton per (type, name), owned and disposed by the container. Typed registry lookups
  (`TryGetPool<T>` / `GetPool<T>` / `GetRequiredPool<T>`) verify the element type rather than
  handing back a pool that cannot lend the requested type, and are extension methods so
  `IHayateObjectPoolRegistry` itself stays at its original size.
- **Diagnostics master switch** (O11, equivalent to P89OP's `Diagnostics.Enabled = false`): the new
  `HayatePoolOptions.EnableDiagnostics` (default `true`, settable through
  `HayatePoolBuilder<T>.WithEnableDiagnostics()`) is a **master gate over the whole diagnostic
  surface** — the cumulative counters, the timing statistics, the `IHayateMetrics` sink and the
  per-operation debug trace — rather than a peer of `EnableMetrics`. It is the one configuration that
  also stops `TotalAcquired`, the borrow-count contract that `EnableMetrics` deliberately leaves
  running (see `docs/metrics-gating.md`), so with diagnostics off a borrow performs no counter write,
  no metrics callback and no trace entry at all, and every cumulative counter and timing statistic
  reads 0. It is a master gate in the "mode wins" sense the options already use: switching it off
  normalizes `EnableMetrics` and `EnableAllocationTracking` off with it, and the collapsed result is
  visible through `GetOptions()`. The lean fast path forces it off (it keeps no bookkeeping by
  construction) and `UseFullProfile()` re-opens it explicitly. Lifecycle and problem logs
  (`Information` / `Warning` / `Error`) are untouched, so construction, disposal and failure reporting
  still reach the log; counters owned by another switch — leak detection, the capacity alarm — keep
  following their own switch. Registering a custom `IHayateMetrics` with diagnostics off fails the
  build, so a sink can never be silently discarded. The dependency-injection registration consults the
  master switch before attaching its custom sink, keeping the "a DI user is never fast-failed by a
  configuration they did not write" semantics. See the "Diagnostics" section of the README.
- **Abandoned-object recovery** (K2 → M4+, closing the CHOPIN `AbandonedConfig` gap): the M4 leak
  surface stays forensics-only, and borrowed objects that are never returned past
  `RemoveAbandonedTimeout` (default 300 s, aligned with CHOPIN) can now be reclaimed through
  `RemoveAbandonedOnBorrow` (a bounded, oldest-borrow-first scan on every borrow) and/or
  `RemoveAbandonedOnMaintenance` (the shared background timer at `RemoveAbandonedIntervalMs`,
  default 30 s). Reclamation is a destructive opt-in — both toggles default to `false`, so the
  default configuration behaves exactly like M4 (count in `LeakDetectedCount` /
  `LeakSuspectedCount`, never reclaim). A reclaimed object is destroyed under the shard claim
  protocol (no double-destroy on races), removed from the registry, counted in the new
  `HayatePoolStats.AbandonedRemovedCount` / `HayatePoolSnapshot.AbandonedRemovedCount`, and is not
  also reported as a leak. `LogAbandoned` logs a warning with the captured lease trace on reclaim;
  releasing an already-reclaimed object follows the pool's existing foreign-return path. Both
  toggles are forced off in lean mode.
- **ArrayPool direct-storage backend for the lean fast path** (O-D, folding the POP
  `PooledStack` storage shape into the O1/O2 design line): `WithArrayPoolStorage()` switches the
  lean buffer's slot array from a fixed `MaxPoolSize`-sized array to one rented from
  `ArrayPool<T>.Shared` that grows on demand (×2) up to `MaxPoolSize`. A large pool therefore only
  holds the storage its demand actually reached — the fixed array no longer sits in memory for the
  pool's whole lifetime — and the rented array returns to the shared pool on `Dispose`. Semantics
  are identical to fixed-buffer lean (same ceiling, same wrapper-free borrow/return, same reject
  policies, same stats/snapshot shape); the fast lane is shared. A logical slot limit keeps the
  retention ceiling exact even though `ArrayPool.Rent` returns bucket-aligned arrays that can be
  larger than requested. Available on net6.0+ (`ArrayPool<T>` is a BCL type there); on
  netstandard2.0/net48 the flag is ignored and lean keeps its fixed buffer, preserving the core
  package's no-dependency policy. The O2 allocation quantification for this backend lands in the
  benchmarks project (`Acquire+Release | Hayate ArrayPool (O-D)` row).
- **Asynchronous preparation / reconnect strategy** (N3, closing the marklauter connection-pool
  gap): `IHayatePreparationStrategy<T>` with `IsReadyAsync` / `PrepareAsync`, installed through
  `pool.WithPreparation(strategy, onDiscard, maxPrepareAttempts)` which wraps any
  `IHayateObjectPool<T>` — the bounded engine, the lean fast path, keyed sub-pools, or
  `HayateUnboundedPool<T>` — in a `HayatePreparationPool<T>`. Every borrow runs the ready-check;
  a not-ready object goes through the asynchronous prepare (the classic reconnect), so the caller
  receives a usable object or the reconnect error, never a silently broken one. A failed prepare
  hands the object to `onDiscard` (give it the object's real disposal) and retries with another
  borrow; when the budget (default 3) is exhausted the last failure propagates as
  `HayatePoolPreparationException`, and cancellation propagates with the object discarded. The
  synchronous `Acquire` runs the identical chain by blocking on it (documented sync-over-async
  caveat under a `SynchronizationContext`); everything else delegates to the inner pool. The six
  synchronous policy hooks are unchanged — the interface exists precisely because reconnecting a
  connection cannot be expressed in them.
- **Unbounded pool model** (N1, the second pool model, aligned with marklauter's `UnboundedPool`):
  `HayateUnboundedPool<T>` borrows ArrayPool-style — it never blocks, never waits and never rejects;
  when the idle queue is empty the next borrow creates a new object. The lease is ownership:
  returning is optional, and a borrowed object that is never returned is left to the garbage
  collector (the pool keeps no per-object bookkeeping, so a forgotten return cannot leak or block
  anyone). The only knob is `maxIdle` (default 32): a return that arrives while that many objects
  are parked destroys the object instead, bounding the resident set by policy rather than demand.
  The pool is a full `IHayateObjectPool<T>` — scoped borrows, `GetStats`/`TakeSnapshot` and the DI
  surface work unchanged — implements the same reset/validate-on-return hooks as the bounded engine
  (a failed validation destroys the object), ignores timeouts on the synchronous overload and
  completes `AcquireAsync` synchronously, and reports the new `HayatePoolStats.TotalDestroyed`
  counter for returns rejected past `MaxIdle` and destroy sweeps. There is no `MaxSize`, no
  rejection policy and no background machinery; `HayatePoolStats.TotalDestroyed` is 0 on the
  bounded engine. See the "Unbounded pool" section of the README for the model choice between the
  two.

- **Zero-allocation value builder** (Z1, from the sbpool performance assessment): `HayateValueStringBuilder`
  is a `ref struct` over a rented `ArrayPool<char>` buffer — doubling growth hands the old buffer
  straight back, `ToString()` materializes the content and returns the buffer in the same call
  (dispose-by-ToString, so the build's only allocation is the final string), `Dispose` is idempotent,
  and `TryCopyTo(Span<char>, out int)` / `AsSpan()` hand out the content without ending the borrow.
  Appends follow ZString's shape and return `void`: chained calls on a mutable ref struct would run
  on struct copies and lose their position state. Every member after the builder has ended throws
  `ObjectDisposedException`, so a use-after-return surfaces cleanly instead of reading a buffer the
  next renter owns. The type complements `StringBuilderPool` (transient format-and-return flows run
  here with no heap traffic; long-lived, observable builders stay on the pool) and does not touch the
  HayateOP engine. Available on every target framework: net6+ natively, net48 through `System.Memory`
  — the specialized package's first net48-only NuGet dependency (the core library is unaffected);
  a thread-static fast path stays a future enhancement.
- **Generic direct-write Build overloads** (Z6): `SharedStringBuilder.Build<T1..T8>(format, args)`
  formats against the shared builder with `string.Format` semantics but no `object[]` and no argument
  boxing — the values travel as their concrete types through the same direct-write formatting core the
  pool helpers use. The lock discipline is unchanged: the write and the snapshot happen under the
  lock, the builder is cleared before the lock is released, and a failing format releases the lock.
  The parsing core itself moved to a shared internal class, so the pool and shared surfaces parse
  identical grammar.
- **Span output surface** (Z5): `PooledStringBuilder.TryCopyTo(Span<char>, out int)` hands the content
  out as chars without materializing a string — on net6+ the copy walks the builder's chunk chain
  straight into the span, on net48 it degrades through one intermediate `ToString`, never worse than
  the string it replaces. `WriteTo(Stream)` (net6+) streams the content as UTF-8 through a rented
  scratch buffer with a persistent encoder, so a stream consumer skips the final string without a
  whole-content allocation and a surrogate pair split across a chunk boundary stays intact. Neither
  ends the borrow; both snapshot the content as it stands when called. The Z1 value builder already
  carried `TryCopyTo` / `AsSpan` from the start.
- **Fast append surface** (Z4a): `PooledStringBuilder` and `HayateValueStringBuilder` gain generic
  `Append<T>(value, format)` plus full sets of named primitive overloads (`int`, `long`, `short`,
  `byte`, `uint`, `ulong`, `ushort`, `sbyte`, `double`, `float`, `decimal`, `DateTime`,
  `DateTimeOffset`, `TimeSpan`, `Guid`). On net6+ an `ISpanFormattable` value formats straight into
  the destination — no intermediate string, no boxing — through a struct-constrained helper whose
  per-type instantiations devirtualize `TryFormat`; the named overloads exist because the generic
  path box-frames its type check on value types (24 bytes per call), the same reason ZString and
  Cosmos ship full sets of named appends. On net48 the surface degrades to `IFormattable`, the same
  intermediate string the builders' own primitive appends cost there. The Z-C-A `Format`/`Concat`/
  `Join` helpers route through the same writer, so their argument writes gained the direct path too,
  and a null format now formats through `IFormattable.ToString(null)` (string.Format semantics)
  instead of dropping custom default formatting.
- **Declared-capacity borrows with tier routing** (Z-C-A, folding the 2.7 C-A convenience batch into
  the specialized pools with ZString semantics): `StringBuilderPool.GetObject(int minCapacity)` /
  `Acquire(int minCapacity)` (and the `MemoryStreamPool` equivalents) let the borrower declare how much
  the object must hold. Requests within the creation capacity are served by the base tier as before;
  anything larger routes to a lazily created internal capacity tier — one engine pool per bucket of the
  exponential ladder anchored at the minimum, `ArrayPool`-style — so repeated borrows of the same size
  reuse a parked builder instead of paying the grow ladder every time. A tier accepts its declared size
  back even when that exceeds `MaximumStringBuilderCapacity` / `MaximumMemoryStreamCapacity`: the buffer
  exists by declaration rather than by accidental growth, which is what stops a mixed-size workload
  churning through grow → destroy → rebuild. Only growth beyond the declared envelope (and the global
  maximum) destroys an object. `Clear`, `Dispose`, `Evict`, `CheckAvailable`, `SetAvailable` /
  `SetUnavailable`, `ReloadConfig` and the stats/snapshot aggregation cover the tier engines, raising
  the minimum capacity retires the tiers with the parked builders (P89OP setter parity), and a manual
  `Release` routes the object back to the tier it came from. Each tier retains at most `maxPoolSize`
  objects, the same budget as the base pool.
- **Seeded borrows fill robustly** (Z-C-A): `GetObject(string)` now clears the builder before appending
  the seed — CPL's append-only fill relied on every return path clearing, which breaks silently the day
  one path forgets — and a seed longer than the creation capacity borrows through a capacity tier, so
  the builder already fits the seed instead of growing to it. A failed fill disposes the builder back
  to the pool instead of leaking it.
- **Convert-and-return** (Z-C-A, CPL's `ToStringReturn` / `ToByteArrayReturn`):
  `PooledStringBuilder.ToStringReturn()` and `PooledMemoryStream.ToByteArrayReturn()` produce the
  result and end the borrow in one call. The return is atomic with the conversion — it runs even when
  the conversion throws, so a failing path can never leak the object — and stays exactly-once per
  borrow, like `Dispose`.
- **Generic no-`params` formatting helpers** (Z-C-A, ZString's signature surface):
  `StringBuilderPool.Format<T1..T8>` / `Concat<T2..T8>` / `Join<T>` (char and string separators over
  `IEnumerable<T>`) borrow one builder from the shared pool, write the arguments through their concrete
  types and return the finished string — no `object[]`, no boxing of the arguments, one borrow/return
  cycle whose return is atomic with the conversion. The format parser implements the common
  `string.Format` grammar (`{{`/`}}` escapes, `{index}`, `{index,alignment}`, `{index,alignment:spec}`)
  with `string.Format`-matching current-culture semantics and the same `FormatException` behaviour for
  malformed holes and out-of-range indexes; values with a format specifier format through
  `IFormattable`. Allocation benchmarks live in `tests/HayateOP.Benchmarks`:
  `SpecializedStringBuilderTierBenchmarks` quantifies the tiering benefit — on net10 a 300K-character
  build that the default borrow cannot retain costs 1,229,426 B per operation, and the declared borrow
  brings it to 614,843 B (the final string alone), halving both allocation and time — while
  `SpecializedStringBuilderFormatBenchmarks` separates the helpers' own writes (no `object[]`, no
  boxing) from the engine's fixed borrow-cycle bookkeeping floor.

### Changed

- **Asynchronous preparation borrows are actually asynchronous** (A4, behavioral fix of N3): the
  borrows of `HayatePreparationPool<T>` did not await anything — `AcquireAsync` ran the inner pool's
  *synchronous* `Acquire()` inside the asynchronous method, so a borrow that had to wait occupied a
  thread for the whole of the inner acquire timeout (5 s by default) and the `timeout` argument of
  `AcquireAsync(TimeSpan, …)` — and of the synchronous `Acquire(TimeSpan)` — was silently dropped.
  The chain now awaits the inner pool's own asynchronous borrow and forwards the timeout to it, so an
  exhausted pool suspends the borrowing context instead of a thread, and a borrow converges on the
  timeout it was given (surfacing the inner pool's `TimeoutException`). No public signature changed:
  the synchronous `Acquire`/`Acquire(TimeSpan)` still block their caller by design — they now block on
  a borrow that honours the timeout. The lean, bounded, keyed and unbounded inner pools all gain the
  behavior, since the fix lives in the wrapper.
- **Performance gate promotion machinery** (Q1): `scripts/bench-compare.py` now implements the
  allocation gate that the threshold table always documented — allocation growth `>= allocFailBytes`
  fails and `>= allocWarnBytes` warns (previously both keys were parsed but never referenced) — and
  supports per-target `warnMeanPercentOverride` / `failMeanPercentOverride`. The PG4 convergence
  outcome applies them to the sub-100 ns lean rows (warn 30 / fail 60), which isolated local reruns
  measured across a ~1.9× range; the >=200 ns rows keep the global 15/30. `--emit-baseline-file`
  writes a complete, committable baseline (schema, thresholds, capture metadata, percentiles,
  `calibration: false`), and the workflow's `update_baseline` input produces it as the `ci-baseline`
  artifact. `perf-regression.yml` no longer carries a static `continue-on-error`: it reads the
  baseline's `calibration` flag and routes to a blocking or an advisory compare step accordingly, so
  committing the CI-native baseline hardens the gate in the same commit — the runbook lives in
  `docs/benchmarks/baseline/README.md`. Until that capture lands, the committed baseline stays in
  calibration mode and the gate stays advisory **by design, not by omission**.
- **Multi-TFM XML documentation fix** (root-cause fix folded into Q1): the canonical
  `$(AssemblyName).xml` is now produced by the highest target framework only; lower-TFM builds write
  their documentation into `obj/$(TargetFramework)/`, where it is packed as that TFM's own asset.
  Every `lib/<tfm>/*.xml` in the package now documents exactly that TFM's API surface (previously
  every TFM's asset carried whatever TFM built last — a net48 asset could ship documentation for
  net6+-only members it does not have, and a stray low-TFM build could strip higher-TFM members from
  the canonical file, which 2.6/2.7 worked around by hand-restoring the XML). The canonical file
  survives forced single-TFM rebuilds untouched, verified.

### Fixed

- **The ArrayPool storage switch now survives an option copy** (follow-up to O-D):
  `HayatePoolOptions.CopyTo` never carried `EnableArrayPoolStorage`, so every copy-based surface lost the
  storage shape without saying so. `GetOptions()` reported a fixed-buffer lean pool for a pool that runs
  on the rented-slot backend, and the configuration binding and the DI registration dropped a bound
  `EnableArrayPoolStorage` between the bound options and the builder — the setting was accepted, reported
  as off, and never applied. The switch now travels with `EnableLean`, which is the execution mode it
  belongs to. A reflection-driven copy-fidelity case writes a value other than the shipped default into
  **every** writable option and asserts the copy carries it, so an option declared without its `CopyTo`
  line fails the suite instead of reaching a release; reverting the fix fails three of the four new cases
  while the circuit-breaker non-aliasing case keeps passing.

## [2.7.0] - 2026-09-14

Usability and ecosystem release, no breaking API change. Highlights: nullable
reference type annotations across every `src` assembly (Q0), scoped borrows with
a one-call factory (S2), process-shutdown auto-dispose with a pluggable shutdown
signal (S3), per-object metadata on the wrapper and in snapshots (S4), native
keyed object pools (`ParameterizedHayatePool<TKey, TValue>`, O-A) and a
specialized pools extension package for `MemoryStream` / `StringBuilder` /
shared builders aligned with P89OP (O-B).

### Added

- **Nullable reference types enabled across all `src` assemblies** (Q0). `<Nullable>enable</Nullable>`
  is now set in `asset/props/target.feature.props`, and every nullable warning
  (CS8618 / CS8601 / CS8603 / CS8625 / CS8600 / CS8602 / CS8604 / CS8622 / CS8765 / CS8767) in the
  core pool and all seven extension packages has been resolved. The change is annotation-only: public
  API nullability was corrected with additive `?` / `!` annotations, so existing consumers recompile
  unchanged and no runtime behaviour differs (see `docs/BREAKING-CHANGES.md` §2.7). Builds are
  warning-free on net6.0–net10.0.

- **Scoped borrows** (S2): `pool.AcquireScoped()` (plus a `TimeSpan` overload) and
  `pool.AcquireScopeAsync(ct)` (plus a timeout overload) borrow an object together with a
  `HayatePoolScope<T>` lease that returns it when disposed, so `using` replaces the manual
  `try`/`finally` and the return cannot be forgotten — including when the body throws. Disposal is
  exactly-once and idempotent: a second `Dispose` is a no-op instead of a duplicate return, confirmed
  atomically so concurrent disposals collapse into one return. A failed borrow throws without producing
  a lease, so there is nothing left unreturned. Implemented as **extension methods** on
  `IHayateObjectPool<T>` — every existing implementation gets them without implementing anything, and
  no existing call site needs to change. The cost is one allocation per borrow.
- **One-call factory** (S2): `HayatePool.Simple<T>(poolSize, create, onGet)` builds a pool from a size
  and a factory lambda, with no options object and no policy class. Nothing is pre-created, and every
  other option keeps its documented default. It is deliberately placed on `HayatePool` rather than on
  `HayatePoolBuilder<T>`: the builder constrains `T : class, new()`, which would demand a parameterless
  constructor even when a factory supplies creation.
- **Process-shutdown auto-dispose** (S3): `HayatePoolOptions.EnableAutoDisposeWithSystem` (default
  `false`), plus the `HayatePoolBuilder.WithAutoDisposeWithSystem()` shortcut. A pool whose owner is the
  process never gets its `Dispose` call, so enabling this releases pooled objects — and the handles or
  connections they hold — on the way out. Nothing changes on the borrow or return path: the subscription
  is taken once at construction and removed on disposal, so a disposed pool never stays reachable from the
  process-wide hook, and disposal stays idempotent whichever route it took. `HayateProcessShutdownHook`
  subscribes to both `ProcessExit` and `Console.CancelKeyPress` (either can end a process), and each
  handler is shielded so one failing disposal cannot abandon the pools behind it.
- **Pluggable shutdown signal** (S3): `IHayateShutdownHook`, together with
  `HayatePoolBuilder.WithShutdownHook(hook)`, replaces the process-wide hook with any host signal — an
  application lifetime, a container, or a test double. Supplying a hook does not enable the feature on its
  own; it is combined with `WithAutoDisposeWithSystem()`.
- **Per-object metadata** (S4): `HayateObject<T>` now exposes `GetTimes` (cumulative borrow count, the
  same value as `LeaseCount` under the name used by the pool libraries this type is compared against),
  `LastGetThreadId` (managed thread id of the most recent borrow, `0` when never borrowed) and
  `CreateTime` (creation instant derived from `CreatedAtTick`). The borrow path records the thread id
  alongside the existing borrow timestamp and lease counter — one thread-id read and one field write
  per borrow, no allocation and no synchronization, and costing the lean fast path nothing because it
  stores the pooled value directly instead of a wrapper.
- **Snapshot carries the borrowing thread** (S4): `HayatePoolObjectDetail.LastGetThreadId` mirrors the
  wrapper field, so the per-object rows in `TakeSnapshot()` answer "which thread used this object last"
  — whether a pool is handing objects across threads, or one thread is quietly monopolising a shard.
  The id identifies the borrowing thread only, and the runtime recycles ids after a thread dies.
- **Keyed pooling** (O-A): `ParameterizedHayatePool<TKey, TValue>` gives every key an ordinary pool of its
  own, so objects are only ever reused by the key that created them and capacity, eviction, validation and
  the circuit breaker are all per key — one tenant exhausting its connections cannot slow the next.
  `GetObject(key)` / `GetObjectAsync(key)` borrow, `ReturnObject(key, value)` returns, and
  `GetPool(key)` hands back the sub-pool itself for its statistics, snapshot or eviction. Sub-pools are
  created on first use, registered in the pool registry under `Name[key]` so management endpoints and
  metrics can address a single key, and disposed with the keyed pool; `KeysInPoolCount` reports how many
  exist and `TryRemove(key)` retires one, which is how an open-ended key space stays bounded.
- **Two defaults a sub-pool cannot inherit** (O-A): the convenience constructor sizes shards to the per-key
  size (`min(default, maxSizePerKey)`), because sharding divides capacity across shards and a size of two
  split four ways leaves shards that can hold nothing; and it creates on demand
  (`RejectPolicy.CreateOnDemand`), because a sub-pool starts empty and holds nothing in reserve, so with the
  default wait-then-timeout policy every borrow past the first would stall until the background scaler
  caught up. Both are overridable through the `configure` callback, and the constructor that takes a
  sub-pool factory changes nothing at all.
- **Specialized pools extension package** (O-B): `DotNetCore.HayateOP.Extensions.Specialized` ships the
  ready-to-use `MemoryStream` / `StringBuilder` pools that generic engines leave to you, aligned with
  P89OP's `CodeProject.ObjectPool.Specialized` — `MemoryStreamPool` / `PooledMemoryStream`,
  `StringBuilderPool` / `PooledStringBuilder` and `SharedStringBuilder`, with the P89OP default capacity
  tiers (4KB/512KB bytes for streams, 4096/524288 characters for builders, default pool size 16) and the
  P89OP `Instance` singletons, `GetObject(string)` prefill, and clear-on-tighten capacity setters.
  Disposing a borrowed stream or builder returns it through the pool's ordinary return path, so `using`
  is the whole borrow/return pair and the return cannot be forgotten — including when the body throws.
  A returned stream stays open for the next borrower (only the pool really closes a buffer, when a
  returned object fails validation — most commonly one that grew past the maximum capacity); returned
  objects are reset (empty stream, cleared builder) before the next borrow; and disposal routes exactly
  one return, so a double dispose never double-parks an object. Each pool is an ordinary
  `IHayateObjectPool<T>` built on the core engine (create-on-demand, no sharding, no background
  features), so `AcquireAsync`, `GetStats` and the rest of the surface behave as usual. The package
  targets .NET Framework 4.8 and .NET 6/7/8/9/10.

### Changed

- Public API surface now carries accurate nullability annotations (non-breaking). See
  `docs/BREAKING-CHANGES.md` §2.7.

## [2.6.0] - 2026-09-12

Performance and ecosystem release, no breaking API change. Highlights: a lock-free
lean fast path that matches the Microsoft `DefaultObjectPool` reference on both
latency and allocations (`EnableLean`), wrapper re-allocation elimination, one-call
configuration profiles (`UseLeanProfile()` / `UseFullProfile()`), a create-on-demand
reject policy for the `Microsoft.Extensions.ObjectPool` compatibility layer, an
opt-in pool-level availability circuit breaker with automatic recovery, a merged
single-timer background scheduler, per-switch hot-path cost documentation, a
counter-gating contract, and a machine-readable performance baseline with a CI
regression gate.

### Added

- **`CreateOnDemand` reject policy**: a request that finds no idle object is served by creating one
  straight away while the pool still has room to grow, without waiting out the acquire timeout. An empty
  pool goes through the same synchronous first-object creation the block policies use, so the first
  borrow of a cold pool returns immediately; the timeout is only paid when the pool already holds
  `MaxPoolSize` objects and every one of them is lent out, where the request waits for a return and then
  creates one anyway rather than throwing. This matches the reference `DefaultObjectPool` contract of
  "create on a miss instead of blocking", with creation bounded by the configured capacity.
- **Lean (wrapper-free) fast path**: `HayatePoolOptions.EnableLean` — plus the
  `HayatePoolBuilder.WithLean()` shortcut — stores the pooled value directly in a bounded array and
  moves it through `Interlocked` compare-exchange, removing the per-object wrapper allocation, the
  per-shard free-list lock, the registry lookup on return and every diagnostic write from the
  borrow/return path. Pool sizing, pre-warming, the four reject policies, the object-policy hooks,
  `Clear`/`Dispose` and the async path all behave as usual.
  Lean is a *mode*, not a knob: enabling it switches sharding, auto-scaling, validation, eviction,
  generation optimization, leak detection, metrics, allocation tracking and the capacity alarm off
  during configuration normalization, whatever order the builder calls are made in. The normalized
  configuration is observable through `GetOptions()`.
  Trade-offs to be aware of: the lean path keeps no cumulative counters (`HayatePoolStats.TotalCreated`
  and friends report `0`), `Evict` throws `InvalidOperationException`, `ReloadConfig` rejects a
  `MaxPoolSize` change, and   `Release` trusts the caller because there is no registry to verify
  ownership against.
- **Wrapper recycling**: destroyed `HayateObject<T>` wrappers are parked on a bounded per-shard
  spare stack and reused by the next object creation, removing the per-wrapper allocation from
  create/destroy churn. No observable semantics change: pooled objects are still freshly created
  and counted as before (`TotalCreated` counts objects, not wrappers), a recycled wrapper carries
  pristine lease state (lease count, lease duration, generation and creation timestamps all reset),
  and a parked wrapper holds no reference to its destroyed pooled value. When a destroy's cleanup
  fails partway, the wrapper is not parked, so a stale registry entry can never survive onto a
  reused wrapper. The release-rejection path also drops a now-redundant registry removal.
- **Single background timer**: eviction, auto-scaling and idle validation are now driven by one timer
  instead of one timer per concern. The shared timer ticks at the smallest enabled period and each
  concern still fires on its own configured interval, so cadence is unchanged while the pool holds one
  timer handle instead of three. A pool whose three background features are all disabled — including
  the lean path, where they are normalized off — creates no timer at all and performs no periodic
  wake-ups. Overrunning ticks are skipped rather than overlapped, and `Dispose` releases the handle and
  drops the reference to it.
- **Configuration profiles**: `HayatePoolOptions.UseLeanProfile()` and `UseFullProfile()` — plus the
  `HayatePoolBuilder.WithLeanProfile()` / `WithFullProfile()` shortcuts — land a complete feature set in
  one call, removing the need to work through roughly forty options to express "minimal pooling" or
  "everything on". The lean profile writes out the state the lean mode normalizes to (lean on; sharding,
  auto-scaling, validation, eviction, generation optimization, leak detection, metrics, allocation
  tracking and the capacity alarm all off), and the full profile turns every optional feature switch back
  on. Neither profile touches pool sizing, timeouts or the reject policy, so a profile can be applied and
  then tuned in either call order. The profiles are order-deterministic against each other (the last one
  wins), and a later feature call still overrides a profile.
- **Pool-level availability circuit breaker**: `HayatePoolOptions.EnableCircuitBreaker` (default off) with
  `HayateCircuitBreakerOptions` (failure threshold 3, 30 s reset timeout, 5 s probe interval, optional
  health probe) and the `OnAvailable` / `OnUnavailable` callbacks. The application reports dependency
  failures with `pool.SetUnavailable(reason)`; the `FailureThreshold`-th consecutive report opens the
  breaker and every further `Acquire` / `AcquireAsync` throws the new `HayatePoolUnavailableException`
  (derived from `InvalidOperationException`, so existing catch blocks keep working) before doing any pool
  work, instead of handing out objects that are likely to be broken. `pool.CheckAvailable()` reports the
  state with a single volatile read. Recovery is automatic when a probe is configured — it runs on a timer
  that is created on the trip and disposed on the recovery, so an available pool holds no extra handle —
  or manual through `pool.SetAvailable()`, which is also how a pending failure streak is cleared. The
  feature is fixed at build time (the lean profile forces it off and `ReloadConfig` cannot toggle it),
  and the API surface (`CheckAvailable` / `SetUnavailable` / `SetAvailable`) aligns with
  SafeObjectPool's availability model.
- `CHANGELOG.md` and [`docs/BREAKING-CHANGES.md`](docs/BREAKING-CHANGES.md): release
  notes and migration guidance now live in dedicated documents, and the README links
  to them instead of duplicating them.
- GitHub Actions workflows: `ci.yml` (build + test on push and pull request),
  `publish-nuget.yml` (tag-triggered pack and push to nuget.org) and
  `perf-regression.yml` (BenchmarkDotNet comparison against the archived baseline).
- `netstandard2.0` assets for the core package, for hosts that can consume neither
  `net6.0`+ nor `net48`.

### Changed

- **`HayateObjectPoolCompatProvider` cold start no longer waits.** The provider now builds its pool with
  the `CreateOnDemand` policy instead of `CreateNew`, so a `Get` that finds no idle object creates one
  synchronously while the pool has room — including the very first `Get` of a cold pool, which used to
  pay up to `HayateCompatOptions.AcquireTimeout` (1s by default) before an object appeared. `AcquireTimeout`
  now bounds only the at-capacity request. A pool below capacity therefore serves concurrent misses by
  creating an object per request, exactly as `DefaultObjectPool` does, instead of making callers wait for
  a return.
- **All runtime text is now English.** Exception messages, OpenTelemetry metric
  descriptions and `HayatePoolStats.ToString()` section labels previously emitted
  Chinese text. Code that matches on message content — for example
  `Assert.Contains` in a test — may need to be updated.
- Documentation, XML doc comments, samples and CI-facing Markdown are English-only.
  Internal development phase identifiers (batch/step labels) have been removed from
  source comments, which previously leaked into IDE IntelliSense tooltips.
- Publishing to nuget.org moves from the local `scripts/Release.bat` to GitHub
  Actions; the batch script is retained as an offline fallback.

## [2.5.0] - 2026-09-10

Observability, registry/searchability, foundational benchmarks, and cross-target reach.
No breaking change beyond the lease-context rework listed below.

### Added

- **Capacity warnings**: `HayatePoolOptions.WarnAtRatio` / `CriticalAtRatio` and the
  `OnCapacityWarning` / `OnCriticalCapacity` callbacks. The callbacks fire once per state
  transition (debounced), not on every borrow/return.
- **Tolerant build entry point**: `HayatePoolBuilder.BuildOrThrow(bool)` — with `false`,
  pre-warm/creation failures degrade to a usable empty pool plus a log entry instead of throwing.
  The default `Build()` behaviour is unchanged.
- **Leak suspicion reporting with leak detection disabled**:
  `HayatePoolStats.LeakSuspectedCount`, reported by `TakeSnapshot()` for objects borrowed longer
  than the threshold while `EnableLeakDetection = false`. Forensic behaviour is unchanged.
- **Full registry surface**: `IHayateObjectPoolRegistry` gains `GetAll()`, `Remove()`,
  `Count` and `HayatePoolMetadata` (name, type, creation time). The management endpoints enumerate
  through the registry (the `Type.GetType` fallback is retained).
- **Shard affinity**: `ShardAffinityMode` (`None` default / `Thread` / `Custom`) with
  `WithShardAffinity` / `WithCustomShardAffinity`. `None` stays branch-free on the hot path.
- **Structured lease context**: immutable `HayateLeaseContext` (monotonic
  `LeaseId`, captured `StackFrame[]`, `BorrowedAt`) carried by an `AsyncLocal<HayateLeaseContext>`
  flow; new lifecycle fields `CreatedAtTick`, `LeaseCount`, `OwnerPoolName`; new
  `HayatePoolSnapshot.ObjectDetails` collection.
- **Categorised eviction**: `IHayateObjectPool<T>.Evict(HayateEvictReason)` for
  `Touched` / `Idle` / `Expired`; borrowed objects are never touched.
- **Per-pool logger factory**: `IHayateLoggerFactory` with
  `DefaultHayateLoggerFactory` / `DelegateHayateLoggerFactory` and `WithLoggerFactory`. Resolution
  order is explicit `WithLogger` > factory > built-in singleton (non-breaking).
- **BenchmarkDotNet orchestration**: `tests/HayateOP.Benchmarks` rebuilt as a standard
  BenchmarkDotNet project covering `Acquire` / `Release` / `AcquireAsync` against MEOP and plain
  `new`, with custom `P50/P90/P95/P99` columns and `[MemoryDiagnoser]` / `[ThreadingDiagnoser]`.
  Archived baseline: [`docs/benchmarks/`](docs/benchmarks).
- **Allocation tracking**: opt-in `EnableAllocationTracking` (default off) plus per-acquire /
  per-release allocation counters on `HayatePoolStats` and `HayatePoolSnapshot`.
- **Prometheus serializer**: zero-dependency Prometheus text exposition (0.0.4) with
  `AddHayatePrometheusExporter` / `UseHayatePrometheusExporter` (net8.0+ endpoint).
- **Warmup-ready signal**: `WaitForWarmup` (default `false`) gates `Acquire` on a
  `TaskCompletionSource` until pre-warming completes; pre-warm failures also release the gate.

### Changed

- **`netstandard2.0` assets**: the core package now also targets `netstandard2.0` for
  Unity / Xamarin / hosts that cannot consume `net6.0`+ or `net48`. Allocation tracking returns `0`
  and `Math.Clamp` falls back to a `Max`/`Min` equivalent on that target.
- `LastBorrowedAt` and `LastReleasedAt` are now recorded unconditionally on the borrow/release
  paths, so the suspected-leak and categorised-eviction semantics hold even with every feature
  switch disabled.

### Breaking

- `HayateObject<T>.AcquireTrace` (string) removed in favour of `HayateLeaseContext`. See
  [BREAKING-CHANGES](docs/BREAKING-CHANGES.md#25--structured-lease-context-and-lifecycle-fields).

## [2.4.0] - 2026-09-09

### Added

- **.NET Framework 4.8 support**: `net48` assets for the core, `ObjectPoolCompat` and
  DependencyInjection packages (classic ASP.NET 4.x / WPF / WinForms hosts).
- `AcquireAsync` overload accepting a timeout.

### Changed

- **Idle pools now actually scale down**: the internal scale-down gate that contradicted
  `ThresholdScalingStrategy` was removed. See
  [BREAKING-CHANGES](docs/BREAKING-CHANGES.md#24--idle-pools-actually-scale-down).

### Fixed

- Hardened thread-safety paths uncovered by the fuzz suite.

## [2.3.0] - 2026-09-09

### Changed

- **Timestamps are measured in `Stopwatch` ticks** instead of wall-clock time.
- Snapshot head sampling to reduce snapshot cost on large pools.

### Fixed

- Eviction and validation scheduling issues carried over from 2.2.

## [2.2.0] - 2026-09-09

### Added

- Metrics and eviction statistics persist through the configured callbacks.

### Changed

- **`Build()` fails fast on orphaned custom metrics**: registering `IHayateMetrics` while metrics
  collection is disabled now throws. See
  [BREAKING-CHANGES](docs/BREAKING-CHANGES.md#22--builder-fails-fast-on-orphaned-custom-metrics).

### Security

- Upgraded `Microsoft.OpenApi` to 2.7.6 (the ASP.NET Core Endpoints package).

## [2.1.0] - 2026-09-08

### Added

- Leak-forensics capture modes: `HayateLeakTraceCaptureMode` (`Off` / `Sampled` / `EveryAcquire`).
- Lock-free statistics counters and cached event names.
- Cold-boot acquire: a fully empty pool synchronously creates the first object instead of waiting
  for a return that may never arrive.

### Changed

- **Leak stack-trace capture decoupled from leak detection and off by default.** See
  [BREAKING-CHANGES](docs/BREAKING-CHANGES.md#21--leak-trace-capture).
- **`ApplyFeatureSwitches` capacity semantics**: with auto-scaling disabled the explicit
  `MaxPoolSize` is kept as the hard cap (previously forced to `MinPoolSize`).
- Custom metrics registered while metrics are disabled now fail fast (see 2.2 for the enforcement).

## [2.0-rc] - 2026-09-08

### Added

- Sharded architecture with atomic shard-local removal.
- Auto-scaling with aggressive scale-up / conservative scale-down.
- Object validation (borrow / return / idle), eviction policy, generation optimization,
  leak detection, metrics and snapshots.
- Reject policies: `Abort`, `Block`, `BlockTimeout`, `CreateNew`.
- Integration packages: DependencyInjection, Configuration, Diagnostics, Endpoints, HealthCheck,
  OpenTelemetry, ObjectPoolCompat.

## [1.0.0] - 2026-04-01

Initial release.

[Unreleased]: https://github.com/alexinea/object-pool/compare/v3.1...HEAD
[3.1.0]: https://github.com/alexinea/object-pool/compare/v3.0...v3.1
[3.0.0]: https://github.com/alexinea/object-pool/compare/v2.9...v3.0
[2.9.0]: https://github.com/alexinea/object-pool/compare/v2.8...v2.9
[2.8.0]: https://github.com/alexinea/object-pool/compare/v2.7...v2.8
[2.7.0]: https://github.com/alexinea/object-pool/compare/v2.6...v2.7
[2.6.0]: https://github.com/alexinea/object-pool/compare/v2.5...v2.6
[2.5.0]: https://github.com/alexinea/object-pool/compare/v2.4...v2.5
[2.4.0]: https://github.com/alexinea/object-pool/compare/v2.3...v2.4
[2.3.0]: https://github.com/alexinea/object-pool/compare/v2.2...v2.3
[2.2.0]: https://github.com/alexinea/object-pool/compare/v2.1...v2.2
[2.1.0]: https://github.com/alexinea/object-pool/compare/v2.0-rc...v2.1
[2.0-rc]: https://github.com/alexinea/object-pool/releases/tag/v2.0-rc
[1.0.0]: https://github.com/alexinea/object-pool/releases/tag/v1.0
