using System;
using DotNetCore.HayateOP.Common;

namespace DotNetCore.HayateOP;

/// <summary>
/// Runtime options for the Hayate object pool.
/// </summary>
/// <remarks>
/// These options balance throughput, latency, and resource usage. Time-based options prefer
/// <see cref="TimeSpan"/>; options whose name ends with <c>Ms</c> are expressed in milliseconds.
/// </remarks>
public class HayatePoolOptions
{
    /// <summary>
    /// Minimum number of objects prewarmed in the pool.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_MIN_POOL_SIZE"/> (5).
    /// </summary>
    /// <remarks>
    /// Purpose: pre-create objects at startup to reduce cold-start jitter.<br />
    /// Special case: setting it to 0 disables prewarming.<br />
    /// Boundary: should be &gt;= 0 and not greater than <see cref="MaxPoolSize"/>.<br />
    /// Recommended range: 0~64.
    /// </remarks>
    public int MinPoolSize { get; set; } = HayateConstant.DEFAULT_MIN_POOL_SIZE;

    /// <summary>
    /// Maximum number of objects the pool is allowed to maintain.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_MAX_POOL_SIZE"/> (50).
    /// </summary>
    /// <remarks>
    /// Purpose: caps memory and downstream resource usage.<br />
    /// Special case: too small a cap amplifies waiting and timeouts.<br />
    /// Special case: <c>0</c> turns the pool off rather than making it unlimited. It disables
    /// <b>cold-boot creation</b> — the path that serves a borrow on a completely empty pool by creating
    /// the first object synchronously — and because each shard's capacity is derived from this value, the
    /// derived capacity is 0 as well, so a returned object is rejected instead of retained and is
    /// destroyed. Nothing is created and nothing is stored: every borrow then ends in a timeout, a
    /// rejection or an indefinite wait, according to <see cref="RejectPolicy"/>. This value is accepted
    /// only together with <see cref="MinPoolSize"/> = 0, since <see cref="IsValid"/> requires the ceiling
    /// to be at least the floor. Some third-party pools read <c>0</c> as "no limit"; this option means the
    /// opposite, and the two readings are not interchangeable.<br />
    /// Boundary: should be &gt;= 1 and not less than <see cref="MinPoolSize"/>.<br />
    /// Recommended range: 32~2048.
    /// </remarks>
    public int MaxPoolSize { get; set; } = HayateConstant.DEFAULT_MAX_POOL_SIZE;

    /// <summary>
    /// Soft capacity: the number of idle objects the pool keeps before it starts destroying
    /// returned objects instead of retaining them. Zero (the default) disables the ceiling.<br />
    /// Default value: 0 (disabled — the pool retains up to <see cref="MaxPoolSize"/> idle objects).
    /// </summary>
    /// <remarks>
    /// Purpose: bound the retained set on the <i>return</i> path, so a pool that lent out its whole
    /// ceiling during a burst drops the objects the burst no longer needs instead of keeping them
    /// until eviction or scale-down reclaims them.<br />
    /// Difference from <see cref="MaxPoolSize"/>: that is a hard ceiling on how many objects the pool
    /// ever holds, and it constrains <c>Acquire</c>; this one constrains <c>Release</c> and can only
    /// ever shrink the retained set. A pool with a soft capacity below its hard ceiling therefore still
    /// lends out the full ceiling — it just keeps fewer objects back.<br />
    /// The ceiling is <i>soft</i>: the check is a read of the idle count followed by the store, so two
    /// returns racing for the last slot may both be retained. It is a memory-shape control, not a
    /// mutual-exclusion guarantee.<br />
    /// Trade-off: objects dropped on return are disposed and must be created again if the next burst
    /// needs them, so this lowers the hit rate for a spiky workload. Raising the ceiling is the wrong
    /// fix when the burst is the normal case; it is the right one when the pool's retained memory is
    /// what has to be bounded.<br />
    /// Boundary: <c>0</c> (disabled) or a value between <see cref="MinPoolSize"/> and
    /// <see cref="MaxPoolSize"/>; a ceiling below the floor would make the floor unreachable, and one
    /// above the hard ceiling could never fire, so both are rejected rather than accepted and ignored.<br />
    /// The lean fast path honours it by capping the slots it fills on return, and the unbounded model
    /// does not read it — that model's retained set is bounded by its own
    /// <c>HayateUnboundedPool&lt;T&gt;.MaxIdle</c>, which already drops a return once the queue is full.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Retain at most 8 idle objects out of a 64-object ceiling.
    /// var pool = new HayatePoolBuilder&lt;MyBuffer&gt;()
    ///     .WithMaxSize(64)
    ///     .WithSoftCapacity(8)
    ///     .Build();
    /// </code>
    /// </example>
    public int SoftCapacity { get; set; }

    /// <summary>
    /// Hard ceiling on how many objects may be out on loan at the same time. Zero (the default)
    /// disables the ceiling.<br />
    /// Default value: 0 (disabled — the pool lends out as many objects as it holds).
    /// </summary>
    /// <remarks>
    /// Purpose: bound the <i>concurrency</i> a pool allows, independently of how many objects it keeps.
    /// It is the third constraint on the borrow path, next to the two that already exist: a shard's
    /// capacity bounds the objects the pool holds (idle plus borrowed, derived from
    /// <see cref="MaxPoolSize"/>), and the reject policy decides what happens when no object is free.
    /// Neither of them caps how many borrowers may be in flight at once when the pool is large, so a
    /// pool sized for memory rather than for a downstream dependency will happily hand out every object
    /// it owns — and a database, a socket peer or a remote service behind those objects sees the whole
    /// burst at once. This option is what caps that number.<br />
    /// Difference from <see cref="MaxPoolSize"/>: that one bounds the objects the pool may create and
    /// retain, so a pool at its ceiling refuses to <i>create</i>. This one bounds how many of them may
    /// be out simultaneously, so a pool at its ceiling refuses to <i>lend out</i> while still holding
    /// idle objects that could not be handed over. It is meaningful only below
    /// <see cref="MaxPoolSize"/>: at or above it the borrowed count can never reach the ceiling, so the
    /// combination is rejected rather than accepted and ignored.<br />
    /// What happens at the ceiling is decided by <see cref="RejectPolicy"/>, exactly as it decides a
    /// miss — the ceiling is a second reason for the same decision, not a separate policy:
    /// <c>Abort</c> refuses the request outright, <c>Block</c> waits until a borrower returns an object,
    /// and <c>BlockTimeout</c> waits for <see cref="DefaultAcquireTimeout"/> before giving up. The two
    /// creation policies are the deliberate exception: <c>CreateNew</c> and <c>CreateOnDemand</c> both
    /// <b>wait instead of creating</b>, because an object created to satisfy the request would be an
    /// object out on loan, which is precisely what the ceiling forbids. At the ceiling the ceiling wins
    /// over the policy's create-on-miss behaviour; the timeout then expires exactly as it does for a
    /// caller that waited out a return.<br />
    /// Cost: with the ceiling off the borrow path pays one perfectly predicted branch on a readonly
    /// field. With it on, an accepted borrow pays one atomic compare-and-exchange, and a borrow that
    /// reaches the ceiling pays one pass over the shards to re-derive the true borrowed count before it
    /// parks. Nothing is added to the return path beyond one decrement per return.<br />
    /// Boundary: <c>0</c> (disabled) or a value between 1 and <see cref="MaxPoolSize"/>; cannot be
    /// combined with <see cref="EnableLean"/>, whose fast path keeps no borrow bookkeeping at all, so
    /// the combination fails validation rather than silently ignoring the ceiling. Fixed at construction
    /// like every other feature switch: <c>ReloadConfig</c> does not apply it.<br />
    /// Recommended range: set it to the concurrency the dependency behind the pool can actually serve —
    /// a connection pool's size, a licence count, a peer's connection limit. Leave it at 0 when the
    /// objects are pure in-memory values with no downstream to protect.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Keep up to 512 objects for reuse, but never have more than 32 out at once.
    /// var options = new HayatePoolOptions { MaxPoolSize = 512, MaxActiveObjects = 32 };
    /// </code>
    /// </example>
    public int MaxActiveObjects { get; set; }

    #region Timeouts

    /// <summary>
    /// Default borrow timeout.<br />
    /// Default value: <c>TimeSpan.FromSeconds(5)</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: serves as the wait upper bound for the parameterless <c>Acquire</c>.<br />
    /// Special case: under the blocking policy, a timeout triggers the reject-policy branch.<br />
    /// Boundary: should be greater than <see cref="TimeSpan.Zero"/>.<br />
    /// Recommended range: 1~30 seconds.
    /// </remarks>
    public TimeSpan DefaultAcquireTimeout { get; set; } = TimeSpan.FromSeconds(HayateConstant.DEFAULT_ACQUIRE_TIMEOUT_SECONDS);

    #endregion

    #region Reject policy

    /// <summary>
    /// Reject policy applied when a borrow fails.<br />
    /// Default value: <see cref="HayatePoolRejectPolicy.BlockTimeout"/>.
    /// </summary>
    /// <remarks>
    /// Purpose: defines the action taken after the wait times out.<br />
    /// Special case: the current implementation has explicit branches for <c>Abort</c>, <c>CreateNew</c>
    /// and <c>CreateOnDemand</c>; other values throw <see cref="InvalidOperationException"/>.<br />
    /// Boundary: must be a valid enum value.<br />
    /// Recommended range: use <c>BlockTimeout</c> for general scenarios; <c>CreateNew</c> is an option for
    /// graceful-degradation fallbacks, and <c>CreateOnDemand</c> for callers that expect the reference
    /// <c>DefaultObjectPool</c> behaviour of "create on a miss instead of blocking".
    /// </remarks>
    public HayatePoolRejectPolicy RejectPolicy { get; set; } = HayatePoolRejectPolicy.BlockTimeout;

    #endregion

    #region Creation retry

    /*
     * Creation retry
     */

    /// <summary>
    /// Maximum number of retries after a creation failure.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_CREATION_RETRY_COUNT"/> (3).
    /// </summary>
    /// <remarks>
    /// Purpose: improves creation success rate under transient failures.<br />
    /// Special case: setting it to 0 fails immediately without retrying.<br />
    /// Boundary: should be &gt;= 0.<br />
    /// Recommended range: 1~5.
    /// </remarks>
    public int CreationRetryCount { get; set; } = HayateConstant.DEFAULT_CREATION_RETRY_COUNT;

    /// <summary>
    /// Delay between creation retries.<br />
    /// Default value: <c>TimeSpan.FromMilliseconds(100)</c> (currently uses
    /// <see cref="HayateConstant.DEFAULT_CREATION_RETRY_DELAY_MILLISECONDS"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: controls the backoff between consecutive retries.<br />
    /// Special case: the current default is relatively large (30s), which differs from the common
    /// expectation for a "creation retry delay".<br />
    /// Boundary: should be &gt;= <see cref="TimeSpan.Zero"/>.<br />
    /// Recommended range: 50~1000ms.
    /// </remarks>
    public TimeSpan CreationRetryDelay { get; set; } = TimeSpan.FromMilliseconds(HayateConstant.DEFAULT_CREATION_RETRY_DELAY_MILLISECONDS);

    #endregion

    #region Execution mode

    /// <summary>
    /// Whether to use the lean (wrapper-free) fast path.<br />
    /// Default value: <c>false</c> (the general-purpose sharded engine).
    /// </summary>
    /// <remarks>
    /// Purpose: for pure pooling workloads — objects that need no validation, eviction, leak
    /// forensics, metrics or auto-scaling — the pool can store the pooled value directly in a
    /// bounded array and borrow/return it through <c>Interlocked</c> operations. That removes the
    /// per-object wrapper allocation, the per-shard free-list lock, the registry lookup on return
    /// and every diagnostic call from the borrow/return path.<br />
    /// Special case: lean is a <i>mode</i>, not a knob — enabling it forces
    /// <see cref="EnableSharding"/>, <see cref="EnableAutoScaling"/>, <see cref="EnableValidation"/>,
    /// <see cref="EnableEviction"/>, <see cref="EnableGenerationOptimization"/>,
    /// <see cref="EnableLeakDetection"/>, <see cref="EnableDiagnostics"/>,
    /// <see cref="EnableMetrics"/> and
    /// <see cref="EnableAllocationTracking"/> off and clears the capacity-alarm thresholds, because
    /// each of them needs per-object bookkeeping or a background timer that the fast path does not
    /// maintain. The normalized result is produced by <see cref="ApplyFeatureSwitches"/> (also
    /// called from <see cref="IsValid"/>) and is visible through the pool's <c>GetOptions</c>.
    /// Ordering is therefore irrelevant: <c>WithLean()</c> followed by <c>WithEnableMetrics(true)</c>
    /// still ends up in lean mode.<br />
    /// Consequences to be aware of: the lean path keeps no cumulative counters
    /// (<see cref="HayatePoolStats.TotalCreated"/> and friends report 0), rejects
    /// <see cref="HayateEvictReason"/>, cannot resize its retention buffer through
    /// <c>ReloadConfig</c>, and trusts the caller of <c>Release</c> to hand back an object that
    /// really came from this pool.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable for high-frequency pooling of small, stateless objects; keep
    /// disabled whenever validation, eviction, leak detection or metrics are required.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableLean = true, MinPoolSize = 16, MaxPoolSize = 64 };
    /// </code>
    /// </example>
    public bool EnableLean { get; set; } = false;

    /// <summary>
    /// When set together with <see cref="EnableLean"/>, the lean buffer's slot array is rented from
    /// <c>ArrayPool{T}</c> and grows on demand up to <see cref="MaxPoolSize"/>
    /// (O-D, the ArrayPool direct-storage backend): the buffer no longer occupies
    /// <c>MaxPoolSize</c> slots for the pool's whole lifetime, so large pools keep only the array
    /// the demand actually reached, and the storage returns to the shared pool on
    /// <c>Dispose</c>. The semantics are otherwise identical to lean mode — same ceiling, same
    /// wrapper-free borrow/return, same reject policies. Available on net6.0 and above
    /// (<c>System.Buffers.ArrayPool</c> is a BCL type there); on the older targets the flag is
    /// ignored and lean mode keeps its fixed buffer.
    /// </summary>
    /// <remarks>
    /// Not part of the feature-profile normalization: like <see cref="EnableLean"/> it is a storage
    /// shape, not a bookkeeping switch, so <see cref="UseLeanProfile"/> leaves it alone.
    /// </remarks>
    public bool EnableArrayPoolStorage { get; set; } = false;

    #endregion

    #region Storage backend (G-9 / G-10)

    /// <summary>
    /// Whether the general-purpose engine holds idle objects in a lock-free per-shard slot store
    /// instead of the per-shard <c>SpinLock</c> + linked free list (G-9).<br />
    /// Default value: <c>false</c> (the spin-lock free list, which is what every release before the
    /// switch did).
    /// </summary>
    /// <remarks>
    /// Purpose: the shard free list is the one lock the general-purpose borrow and return paths
    /// take, and one <c>SpinLock</c> per shard is what a burst of borrowers landing on the same
    /// shard contends on. With this switch on, an idle object sits in a slot the wrapper owns and is
    /// taken, or claimed for destruction, by a single <c>Interlocked.CompareExchange</c> on that
    /// slot — so a borrow or a return that finds the object never enters the shard lock, and neither
    /// does the eviction / idle-validation claim.<br />
    /// Special case: <b>retrieval order is no longer return order.</b> A slot array has no order to
    /// keep — a wrapper owns its slot for as long as it is parked — so the sequence a borrow walks is
    /// the slot layout, not the sequence the returns happened in. The eviction sampler's
    /// "oldest idle first" preference goes the same way: an object past
    /// <see cref="MaxIdleTime"/> or <see cref="MaxLifeTime"/> is still reclaimed when it is
    /// sampled, but the sample is no longer age-ordered. Correctness of both limits is unchanged.<br />
    /// For that reason the combination with <see cref="BorrowStrategy"/> = <c>Lifo</c> is
    /// <i>refused</i> at build time rather than silently downgraded to <c>Fifo</c>: an explicit
    /// request for an order the backend cannot honour fails the build, exactly as the lean fast path
    /// refuses a borrow order it cannot honour. A pool that never asked for anything holds the
    /// default <c>Fifo</c> and simply builds.<br />
    /// Special case: the slot array is sized when the shard is built, so a pool with this backend on
    /// refuses a <c>ReloadConfig</c> that would raise <see cref="MaxPoolSize"/> past that size —
    /// lowering it stays allowed. The same rejection the lean fast path makes for its own
    /// construction-sized retention buffer.<br />
    /// This is a storage shape, not a bookkeeping switch, so the feature profiles leave it alone;
    /// lean mode does force it off, because lean has its own storage and a switch that is on but
    /// cannot apply is the failure mode this option set exists to avoid.<br />
    /// Boundary: boolean switch. Off by default; with it off the borrow and return paths gain one
    /// predicted branch and nothing else.<br />
    /// Recommended range: enable for a pool whose borrow/return rate makes shard-lock contention
    /// measurable and whose consumers do not depend on the idle set ageing in return order. Requires
    /// the default <see cref="BorrowStrategy"/> (<c>Fifo</c>) and the general-purpose mode.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableLockFreeStorage = true, MaxPoolSize = 64 };
    /// </code>
    /// </example>
    public bool EnableLockFreeStorage { get; set; } = false;

    /// <summary>
    /// Whether the general-purpose engine keeps a per-thread front slot that holds one idle object
    /// ahead of the shard scan (G-10).<br />
    /// Default value: <c>false</c> (every borrow starts at a shard).
    /// </summary>
    /// <remarks>
    /// Purpose: the steady-state borrow/return cycle of a single thread is "take the object I
    /// returned a moment ago". A per-thread slot turns that cycle into one compare-exchange on a
    /// field only this thread touches, and skips the shard walk entirely; a miss falls through to
    /// the unchanged shard path, which is the lock-free store when
    /// <see cref="EnableLockFreeStorage"/> is on and the spin-lock free list when it is not.<br />
    /// Special case (<i>the non-obvious part</i>): <b>only a thread that has borrowed may park a
    /// return in its own slot.</b> Without that rule a thread that never borrows — an I/O completion
    /// callback, a hand-off worker — parks objects in a slot nobody will ever read, and those objects
    /// are unreachable to every other borrower while still counting as live. The shared layer then
    /// starves with objects sitting in it. A slot therefore records whether its thread has borrowed,
    /// and a return from a thread that has not takes the ordinary shard path.<br />
    /// Special case: an object parked in a front slot is idle but lives in no shard, so
    /// <c>Clear</c> and the eviction sweep reach it through the per-thread slots as well, and the
    /// pool's idle and borrowed counts include it.<br />
    /// This is a storage shape, not a bookkeeping switch, so the feature profiles leave it alone;
    /// lean mode does force it off, for the same reason as <see cref="EnableLockFreeStorage"/>.<br />
    /// Boundary: boolean switch. Off by default; each parked object costs one atomic on the return
    /// path and one on the borrow that hits it, in exchange for skipping the shard path.<br />
    /// Recommended range: enable together with <see cref="EnableLockFreeStorage"/> for a pool whose
    /// borrowing threads also return, so the slot is read by the thread that filled it.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableThreadLocalFrontSlot = true, MaxPoolSize = 64 };
    /// </code>
    /// </example>
    public bool EnableThreadLocalFrontSlot { get; set; } = false;

    #endregion

    #region Sharding strategy

    /// <summary>
    /// Enables sharding. When disabled, the pool is forced into a single shard and all
    /// shard-related configuration is ignored.
    /// </summary>
    public bool EnableSharding { get; set; } = true;

    /// <summary>
    /// Number of shards. Default value: <see cref="HayateConstant.DEFAULT_SHARD_COUNT"/> (4).
    /// </summary>
    /// <remarks>
    /// Purpose: reduce concurrent contention through multiple shards.<br />
    /// Special case: too many shards raise management cost and amplify prewarm skew.<br />
    /// Boundary: should be &gt;= 1.<br />
    /// Recommended range: 2~16.
    /// </remarks>
    public int ShardCount { get; set; } = HayateConstant.DEFAULT_SHARD_COUNT;

    /// <summary>
    /// Shard affinity mode for the borrow path. Defaults to
    /// <see cref="HayateShardAffinityMode.None"/> (sequential scan, preserving the original
    /// semantics).
    /// </summary>
    /// <remarks>
    /// None: starts scanning from shard 0 (identical to 2.4 and earlier, with zero extra
    /// overhead).<br />
    /// Thread: maps the starting shard stably by managed thread ID (the same thread always prefers
    /// the same shard first, improving cache/handle locality; when the starting shard is busy it
    /// continues scanning the remaining shards ring-wise, without losing availability).<br />
    /// Custom: uses the starting shard index returned by the <see cref="CustomShardAffinity"/>
    /// delegate; when the delegate returns null or an out-of-range value, it falls back to None.
    /// Note: this setting has no effect when sharding is closed (single shard).
    /// </remarks>
    public HayateShardAffinityMode ShardAffinityMode { get; set; } = HayateShardAffinityMode.None;

    /// <summary>
    /// Starting-shard-index delegate for <see cref="HayateShardAffinityMode.Custom"/> mode.
    /// The return value should fall in [0, ShardCount); an out-of-range or null return makes this
    /// borrow fall back to a sequential scan. Only invoked when <see cref="ShardAffinityMode"/> is
    /// Custom (at most once per borrow).
    /// </summary>
    public Func<int>? CustomShardAffinity { get; set; }

    #endregion

    #region Borrow order

    /// <summary>
    /// Which idle object a borrow is served. Defaults to
    /// <see cref="HayateBorrowStrategy.Fifo"/>, which is the behaviour of every release before the
    /// switch existed.
    /// </summary>
    /// <remarks>
    /// The idle list is stored in return order whichever value is set, and no value reorders it; what
    /// differs is which node is handed out next. <see cref="HayateBorrowStrategy.Lifo"/> keeps the most
    /// recently returned instance hot, which suits a consumer that borrows, uses and returns in a tight
    /// loop; <see cref="HayateBorrowStrategy.Fifo"/> spreads usage across the whole idle set, which
    /// suits an instance whose per-instance state should age uniformly. <see cref="HayateBorrowStrategy.Lru"/>
    /// and <see cref="HayateBorrowStrategy.Priority"/> instead walk the list and pick a node by, respectively,
    /// the oldest borrow timestamp and a caller-supplied <see cref="IComparer{T}"/> — see each value for
    /// what that changes and what it costs.<br />
    /// The eviction scan samples the oldest-returned end under every strategy, so the objects offered to
    /// the <see cref="IHayateEvictionPolicy{T}"/> never depend on this setting.<br />
    /// Scope: the general-purpose engine (sharded or collapsed to a single shard). Every value but
    /// <see cref="HayateBorrowStrategy.Fifo"/> is unavailable on the lean fast path (no ordered idle
    /// list), on the lock-free store (no order at all) and alongside the per-shard front tier (consulted
    /// before the shard), and a pool that asks for one of those combinations fails the build instead of
    /// honouring it in part. <see cref="HayateBorrowStrategy.Priority"/> additionally needs a comparer,
    /// which lives on the builder rather than here because this type is not generic — selecting it without
    /// one also fails the build. Unknown enum values fall back to Fifo.
    /// </remarks>
    public HayateBorrowStrategy BorrowStrategy { get; set; } = HayateBorrowStrategy.Fifo;

    #endregion

    #region Scaling strategy

    /// <summary>
    /// Enables automatic scaling. When disabled, the pool size is fixed to MinPoolSize and all
    /// scaling-related configuration is ignored.
    /// </summary>
    public bool EnableAutoScaling { get; set; } = true;

    /// <summary>
    /// Scaling check interval (milliseconds).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_SCALING_INTERVAL_MILLISECONDS"/> (5000ms).
    /// </summary>
    /// <remarks>
    /// Purpose: controls how often scaling decisions are made.<br />
    /// Special case: too small causes frequent adjustments; too large makes the pool slow to
    /// respond.<br />
    /// Boundary: should be &gt; 0.<br />
    /// Recommended range: 1000~10000ms.
    /// </remarks>
    public int ScalingIntervalMs { get; set; } = HayateConstant.DEFAULT_SCALING_INTERVAL_MILLISECONDS;

    /// <summary>
    /// Scale-up trigger threshold (utilization).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_SCALE_UP_THRESHOLD"/> (0.8).
    /// </summary>
    /// <remarks>
    /// Purpose: scale up when utilization reaches the threshold.<br />
    /// Special case: too close to <see cref="ScaleDownThreshold"/> causes flapping.<br />
    /// Boundary: should be between 0 and 1, and greater than the scale-down threshold.<br />
    /// Recommended range: 0.70~0.90.
    /// </remarks>
    public double ScaleUpThreshold { get; set; } = HayateConstant.DEFAULT_SCALE_UP_THRESHOLD;

    /// <summary>
    /// Scale-down trigger threshold (utilization).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_SCALE_DOWN_THRESHOLD"/> (0.2).
    /// </summary>
    /// <remarks>
    /// Purpose: scale down when utilization stays below the threshold for a long time.<br />
    /// Special case: too high causes objects to be destroyed and recreated too often.<br />
    /// Boundary: should be between 0 and 1, and less than the scale-up threshold.<br />
    /// Recommended range: 0.10~0.40.
    /// </remarks>
    public double ScaleDownThreshold { get; set; } = HayateConstant.DEFAULT_SCALE_DOWN_THRESHOLD;
    public int ScaleUpCooldownSeconds { get; set; } = HayateConstant.DEFAULT_SCALE_UP_COOLDOWN_SECONDS;

    public int ScaleDownCooldownSeconds { get; set; } = HayateConstant.DEFAULT_SCALE_DOWN_COOLDOWN_SECONDS;

    public int ScaleUpStep { get; set; } = HayateConstant.DEFAULT_SCALE_UP_STEP;

    /// <summary>
    /// Scale-down step (number of objects removed per scale-down).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_SCALE_DOWN_STEP"/> (5).
    /// </summary>
    /// <remarks>
    /// Purpose: decoupled from <see cref="ScaleUpStep"/> so callers can tune "aggressive scale-up,
    /// conservative scale-down".<br />
    /// Boundary: must be &gt;= 1; if it exceeds the current available object count, the result is
    /// clamped to <see cref="MinPoolSize"/>.
    /// </remarks>
    public int ScaleDownStep { get; set; } = HayateConstant.DEFAULT_SCALE_DOWN_STEP;

    #endregion

    #region Validation

    /// <summary>
    /// Enables object validation. When disabled, all borrow/return/idle validation logic is
    /// ignored.
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// Whether to validate object validity before borrowing.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: filter out invalid objects before <c>Acquire</c>.<br />
    /// Special case: enabling it adds latency to the borrow path.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable when objects are prone to becoming invalid; disable for pure
    /// in-memory lightweight objects.
    /// </remarks>
    public bool ValidateOnBorrow { get; set; }

    /// <summary>
    /// Whether to validate object validity before returning.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: filter out abnormal objects at return time.<br />
    /// Special case: the current main flow does not use this switch yet; treat it as a reserved
    /// configuration.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: keep the default until the implementation is wired in, then enable per
    /// business needs.
    /// </remarks>
    public bool ValidateOnReturn { get; set; }

    /// <summary>
    /// Whether to periodically validate idle objects (property name kept from the current
    /// implementation).<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: periodically clean up invalid objects in the idle queue.<br />
    /// Special case: the current main flow does not use this switch yet; treat it as a reserved
    /// configuration.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: keep the default.
    /// </remarks>
    public bool ValidateWhileIdle { get; set; }

    /// <summary>
    /// Periodic validation interval (milliseconds).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_VALIDATE_INTERVAL_MILLISECONDS"/> (30000ms).
    /// </summary>
    /// <remarks>
    /// Purpose: controls the frequency of the background validation task.<br />
    /// Special case: even when the relevant switches are off, too small an interval increases timer
    /// wake-up frequency.<br />
    /// Boundary: should be &gt; 0.<br />
    /// Recommended range: 10000~60000ms.
    /// </remarks>
    public int ValidateIntervalMs { get; set; } = HayateConstant.DEFAULT_VALIDATE_INTERVAL_MILLISECONDS;


    #endregion

    #region Misuse detection

    /// <summary>
    /// Whether a misuse detected on the return path throws instead of being handled the forgiving
    /// way.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: turn the three caller mistakes — returning <c>null</c>, returning an object this pool
    /// does not own, and returning an object the pool already holds — into an
    /// <see cref="InvalidOperationException"/>, so a test or a debug run fails where the mistake is
    /// made instead of where its effects surface. The research term is YOPSHARP's
    /// <c>ErrorOnIncorrectUsage</c>, and the switch carries that name.<br />
    /// Special case: enabling it costs on the return path — with the thread-local front slot on, the
    /// already-returned check enumerates the slot tiers — so it is meant for debug and test builds,
    /// exactly as the original documents it. The default path is unchanged statement for statement:
    /// the misuse is logged, counted, and handled.<br />
    /// Boundary: covers the three caller mistakes only. A <c>ValidateOnReturn</c> failure and an
    /// <c>OnRelease</c> rejection are verdicts about the object, not mistakes by the caller, and stay
    /// non-throwing; a shard at capacity is a normal steady-state outcome. In lean mode only
    /// <c>null</c> is detectable, because the lean return path keeps no ownership lookup. The value is
    /// snapshotted when the pool is built and is not changed by <c>ReloadConfig</c>.<br />
    /// Recommended range: off in production, on in test and debug builds when a problem is suspected.
    /// </remarks>
    public bool ErrorOnIncorrectUsage { get; set; }

    #endregion

    #region Reset

    /// <summary>
    /// Whether an object implementing <see cref="IHayateResettable"/> is reset on its way back into the
    /// pool.<br />
    /// Default value: <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: the historical behaviour — state is wiped at return time, so every idle object is already
    /// clean and the borrow path does no reset work.<br />
    /// Special case: governs only the reset that the pool and
    /// <see cref="Policies.DefaultHayateObjectPolicy{T}"/> drive through
    /// <see cref="IHayateResettable"/>. A custom <see cref="Policies.IHayateObjectPolicy{T}"/> owns its
    /// <c>OnRelease</c> and <c>OnAcquire</c> hooks and is not affected by either reset switch — setting
    /// <see cref="ResetOnBorrow"/> does not move a custom policy's own reset.<br />
    /// Boundary: <see cref="EnableLean"/> forces this back on and <see cref="ResetOnBorrow"/> off (see
    /// <see cref="ApplyFeatureSwitches"/>), because the lean fast path is meant to stay minimal on the
    /// borrow side. Both switches may be on at once, which resets twice per cycle — legal, and sometimes
    /// wanted when borrowers may hand back an object they have already dirtied and the next borrower must
    /// not see it either.<br />
    /// Recommended range: keep the default unless the reset is expensive enough to be worth moving.
    /// </remarks>
    public bool ResetOnReturn { get; set; } = true;

    /// <summary>
    /// Whether an object implementing <see cref="IHayateResettable"/> is reset on its way out to a borrower
    /// instead of on its way back in.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: moves the reset cost from the return path to the borrow path. An object that is never
    /// borrowed again is then never reset at all, so a pool that over-creates pays for reset only on the
    /// objects it actually hands out.<br />
    /// Special case: the same jurisdiction limit as <see cref="ResetOnReturn"/> — this switch reaches the
    /// pool-driven <see cref="IHayateResettable"/> reset only, never a custom
    /// <see cref="Policies.IHayateObjectPolicy{T}"/>. The hook it drives is the policy's acquire hook, and
    /// the pool runs that on a create-on-miss hand-out as well as on a borrow from the idle set, so a
    /// freshly created object is reset once before its first borrower sees it — a reset for state it never
    /// had, and the one place where this switch costs on an object that was not reused. The research term
    /// for this mode is <c>Take</c> (YOPSHARP <c>PooledItemInitialization.Take</c>); the property is named
    /// after <see cref="ValidateOnBorrow"/> so the borrow-side / return-side pairs read alike.<br />
    /// Boundary: forced off by <see cref="EnableLean"/> — the lean profile and lean normalization both
    /// clear it, so a lean pool always resets on return. Read the effective value back through
    /// <c>GetOptions</c> rather than assuming the call took effect.<br />
    /// Recommended range: off by default. Turn it on when reset is cheap relative to borrowing, or when
    /// idle objects would otherwise hold onto state (buffers, references) that should not outlive the
    /// borrow that produced it.
    /// </remarks>
    public bool ResetOnBorrow { get; set; }


    #endregion

    #region Eviction

    /// <summary>
    /// Enables idle-object eviction. When disabled, no eviction logic runs and all eviction-related
    /// configuration is ignored.
    /// </summary>
    public bool EnableEviction { get; set; } = true;

    /// <summary>
    /// The maximum lifetime of a pooled object from the moment it is created — idle or borrowed.<br />
    /// Default value: <c>TimeSpan.FromMinutes(10)</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: cap object lifetime to reduce stale-state risk.<br />
    /// Special case: external-connection objects can use a shorter value.<br />
    /// Boundary: should be greater than <see cref="TimeSpan.Zero"/>.<br />
    /// Recommended range: 5~60 minutes.<br />
    /// <br />
    /// Which half of that meaning the pool enforces is a choice: the borrowed half is gated by
    /// <see cref="EnableLifetimeRotationOnBorrow"/>, which is off by default, so the default is the
    /// backward-compatible idle-side semantics — the eviction run and the manual <c>Evict</c> call
    /// evaluate this value against <b>idle</b> objects only, and an object held by the application for
    /// longer than this value is never treated as expired, which is the behaviour of every release
    /// before 2.9. With the switch on, this value also becomes a ceiling on what the borrow path may
    /// hand out.
    /// </remarks>
    public TimeSpan MaxLifeTime { get; set; } = TimeSpan.FromMinutes(HayateConstant.DEFAULT_MAX_LIFE_TIME_MINUTES);

    /// <summary>
    /// Rotates an object that has outlived <see cref="MaxLifeTime"/> on the borrow path instead of
    /// handing it out.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: make <see cref="MaxLifeTime"/> a ceiling on what may be handed out, not merely a limit
    /// on idle objects. With it off — the default — <see cref="MaxLifeTime"/> keeps the
    /// backward-compatible idle-side semantics, which is the behaviour of every release before 2.9.
    /// Without it an object borrowed and held by the application for longer than
    /// <see cref="MaxLifeTime"/> is never recycled, so a server-side connection lifetime or an
    /// intermediary's idle timeout can invalidate the object without the pool knowing — the scenario
    /// HikariCP's <c>maxLifetime</c> and SQLAlchemy's <c>pool_recycle</c> exist for.<br />
    /// Behaviour when enabled: a borrow that finds an expired object destroys it and creates a
    /// replacement through the same capacity-reservation path the create-on-miss policy uses, so a
    /// lifetime event never turns into a rejection. At most one rotation happens per borrow and the
    /// replacement is never re-checked, so a <see cref="MaxLifeTime"/> shorter than the time it takes
    /// to create an object cannot spin.<br />
    /// Cost: when the generational optimization already computes the object's age on the borrow path
    /// this reuses that computation and adds one integer comparison (while removing two clock reads);
    /// with the generational optimization off it adds one integer subtraction and one comparison, using
    /// the timestamp the borrow path records unconditionally anyway.<br />
    /// Construction-time switch: like the other feature switches it is captured when the pool is built
    /// and is not applied by <c>ReloadConfig</c>.<br />
    /// Boundary: cannot be combined with <see cref="EnableLean"/> — the lean fast path keeps no
    /// per-object timestamps and therefore cannot evaluate an object's age, so the combination fails
    /// validation rather than being silently ignored.
    /// </remarks>
    public bool EnableLifetimeRotationOnBorrow { get; set; }

    /// <summary>
    /// Maximum object idle time.<br />
    /// Default value: <c>TimeSpan.FromMinutes(5)</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: reclaim resources by evicting long-unused objects.<br />
    /// Special case: low-frequency workloads can relax this to reduce recreation.<br />
    /// Boundary: should be &gt;= <see cref="TimeSpan.Zero"/>.<br />
    /// Recommended range: 1~30 minutes.
    /// </remarks>
    public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(HayateConstant.DEFAULT_MAX_IDLE_TIME_MINUTES);

    /// <summary>
    /// Soft minimum evictable idle time.<br />
    /// Default value: <c>TimeSpan.FromMinutes(2)</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: keep objects resident for a minimum idle duration to reduce flapping.<br />
    /// Special case: may still be evicted under resource pressure.<br />
    /// Boundary: should be &gt;= <see cref="TimeSpan.Zero"/> and not greater than
    /// <see cref="MaxIdleTime"/>.<br />
    /// Recommended range: 0.5~10 minutes.
    /// </remarks>
    public TimeSpan SoftMinEvictableIdleTime { get; set; } = TimeSpan.FromMinutes(HayateConstant.DEFAULT_MIN_EVICTION_IDLE_TIME_MINUTES);

    /// <summary>
    /// Eviction scan interval (milliseconds).<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_EVICTION_INTERVAL_MILLISECONDS"/> (30000ms).
    /// </summary>
    /// <remarks>
    /// Purpose: controls how often the eviction task runs.<br />
    /// Special case: scanning too frequently raises CPU and lock contention.<br />
    /// Boundary: should be &gt; 0.<br />
    /// Recommended range: 10000~60000ms.
    /// </remarks>
    public int EvictionIntervalMs { get; set; } = HayateConstant.DEFAULT_EVICTION_INTERVAL_MILLISECONDS;

    /// <summary>
    /// Number of samples scanned per eviction run.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_EVICTION_RUNS_PER_EVICTION"/> (10).
    /// </summary>
    /// <remarks>
    /// Purpose: controls the cost and cleanup strength of a single eviction.<br />
    /// Special case: too small delays cleanup; too large affects peak latency.<br />
    /// Boundary: should be &gt;= 1.<br />
    /// Recommended range: 5~128.
    /// </remarks>
    public int NumTestsPerEvictionRun { get; set; } = HayateConstant.DEFAULT_EVICTION_RUNS_PER_EVICTION;

    #endregion

    #region Generation

    /// <summary>
    /// Enables generational optimization. When disabled, all objects are treated as the young
    /// generation and validation is never skipped.
    /// </summary>
    public bool EnableGenerationOptimization { get; set; } = true;

    /// <summary>
    /// Threshold (milliseconds) for an object to be promoted to an older generation.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_GEN_THRESHOLD_MILLISECONDS"/> (30000ms).
    /// </summary>
    /// <remarks>
    /// Purpose: mark long-lived objects so the policy layer can manage generations.<br />
    /// Special case: too small a threshold promotes objects prematurely.<br />
    /// Boundary: should be &gt;= 0.<br />
    /// Recommended range: 5000~120000ms.
    /// </remarks>
    public int GenerationThresholdMs { get; set; } = HayateConstant.DEFAULT_GEN_THRESHOLD_MILLISECONDS;

    /// <summary>
    /// Interval (in regular-validation passes) between full validations of the old generation.
    /// </summary>
    /// <value>
    /// Default value: 3;
    /// Recommended range: 1 ~ 10;
    /// Boundary: minimum 1, no hard maximum (recommended not to exceed 20);
    /// </value>
    /// <remarks>
    /// Core purpose: controls the validation frequency of the old generation. A value of N means
    /// that for every N regular validation passes, the old generation is fully validated once. This
    /// balances the completeness of old-generation validation against performance cost (a full
    /// old-generation validation is relatively expensive, so validating too often reduces overall
    /// throughput).
    ///
    /// Special cases:
    /// 1. When the value is 1: every regular validation pass triggers a full old-generation
    ///    validation. Use this for scenarios with very high consistency requirements and low
    ///    performance sensitivity (e.g. financial core-data checks).
    /// 2. When the value is &lt;= 0: the framework automatically corrects it to the default of 3;
    ///    the old-generation validation cannot be disabled this way (to disable it entirely, set
    ///    OldGenerationValidationEnabled = false separately).
    /// 3. When the value &gt; 10: the validation frequency is too low, which may let
    ///    old-generation anomalies accumulate for too long and increase troubleshooting difficulty.
    ///    Only use this temporarily in pure performance-first, high fault-tolerance scenarios.
    ///
    /// Recommended range:
    /// - General business scenarios (balance performance and validation completeness): 3 ~ 5;
    /// - High-performance, low-consistency scenarios: 6 ~ 10;
    /// - High-consistency, low-performance scenarios: 1 ~ 2;
    /// </remarks>
    public int OldGenerationValidationInterval { get; set; } = HayateConstant.DEFAULT_OLD_GEN_VALIDATION_INTERVAL;

    #endregion

    #region Leak detection

    /// <summary>
    /// Enables object leak detection. When disabled, call stacks are not recorded and no leak scan
    /// runs.
    /// </summary>
    public bool EnableLeakDetection { get; set; } = true;

    /// <summary>
    /// Leak detection threshold.<br />
    /// Default value: <c>TimeSpan.FromMinutes(30)</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_LEAK_DETECTION_THRESHOLD_SECONDS"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: defines how long a borrowed object can stay out before it is treated as a suspected
    /// leak.<br />
    /// Special case: the constant is named "SECONDS" but the default expression is built in
    /// minutes.<br />
    /// Boundary: should be greater than <see cref="TimeSpan.Zero"/>.<br />
    /// Recommended range: 10 seconds ~ 10 minutes (adjust to actual durations).
    /// </remarks>
    public TimeSpan LeakDetectionThreshold { get; set; } = TimeSpan.FromMinutes(HayateConstant.DEFAULT_LEAK_DETECTION_THRESHOLD_SECONDS);

    /// <summary>
    /// Leak trace capture mode.<br />
    /// Default value: <see cref="HayateLeakTraceCaptureMode.Off"/>.<br />
    /// </summary>
    /// <remarks>
    /// Purpose: controls whether the borrow hot path captures a call stack (for evidence),
    /// decoupled from leak detection itself (threshold judgement + LeakCount).<br />
    /// Behavior change: prior to 2.0, a full stack trace was captured on every borrow by default
    /// (tens of microseconds of CPU / 10~40KB allocated per borrow); starting with 2.1 the default
    /// is <c>Off</c> and LeakTraces holds a placeholder entry. To capture stacks, explicitly choose
    /// <see cref="HayateLeakTraceCaptureMode.Sampled"/> or
    /// <see cref="HayateLeakTraceCaptureMode.EveryAcquire"/>.<br />
    /// Boundary: only takes effect when <see cref="EnableLeakDetection"/> is <c>true</c>.
    /// </remarks>
    public HayateLeakTraceCaptureMode LeakTraceCaptureMode { get; set; } = HayateLeakTraceCaptureMode.Off;

    /// <summary>
    /// Leak-trace sample denominator (1/N).<br />
    /// Default value: <c>1024</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_LEAK_TRACE_SAMPLE_RATE"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: only effective in <see cref="HayateLeakTraceCaptureMode.Sampled"/> mode; captures a
    /// stack once every N borrows (the first borrow always captures); N=1 is equivalent to
    /// capturing every time.<br />
    /// Boundary: when &lt;= 0, the build/runtime auto-corrects it to the default 1024 (see
    /// <see cref="ApplyFeatureSwitches"/>).
    /// </remarks>
    public int LeakTraceSampleRate { get; set; } = HayateConstant.DEFAULT_LEAK_TRACE_SAMPLE_RATE;

    #endregion

    #region Abandoned recovery

    /// <summary>
    /// Whether to reclaim abandoned objects on the borrow path.<br />
    /// Default value: <c>false</c> (forensics-only, identical to the leak-detection surface —
    /// no automatic reclamation).
    /// </summary>
    /// <remarks>
    /// Purpose: an "abandoned" object is one that was borrowed but not returned within
    /// <see cref="RemoveAbandonedTimeout"/> — typically a lease the caller lost track of.
    /// When this is on, every borrow first scans the oldest outstanding borrows (bounded per
    /// borrow) and reclaims the ones past the timeout, exactly like CHOPIN's
    /// <c>RemoveAbandonedOnBorrow</c>.<br />
    /// Risk: reclamation destroys a borrowed object and disposes its value while a caller may
    /// still hold the reference. That is the opt-in contract — the default keeps the safe
    /// forensics-only behavior, so long-lived leases are never reclaimed unless the user
    /// explicitly enables recovery.<br />
    /// Boundary: only meaningful when <see cref="RemoveAbandonedTimeout"/> is greater than
    /// <see cref="TimeSpan.Zero"/> (enforced by <see cref="IsValid"/>). Forced off in lean mode.
    /// </remarks>
    public bool RemoveAbandonedOnBorrow { get; set; }

    /// <summary>
    /// Whether to reclaim abandoned objects on the background maintenance pass.<br />
    /// Default value: <c>false</c> (forensics-only, identical to the leak-detection surface —
    /// no automatic reclamation).
    /// </summary>
    /// <remarks>
    /// Purpose: when on, the shared background timer runs an abandoned-recovery pass every
    /// <see cref="RemoveAbandonedIntervalMs"/> milliseconds (the same timer that drives
    /// eviction / auto-scaling / idle validation), reclaiming every borrowed object past
    /// <see cref="RemoveAbandonedTimeout"/> — CHOPIN's <c>RemoveAbandonedOnMaintenance</c>.<br />
    /// Risk: same opt-in contract as <see cref="RemoveAbandonedOnBorrow"/> — reclamation
    /// disposes a value a caller may still hold, so it is off by default.<br />
    /// Boundary: only meaningful when <see cref="RemoveAbandonedTimeout"/> is greater than
    /// <see cref="TimeSpan.Zero"/> (enforced by <see cref="IsValid"/>). Forced off in lean mode.
    /// </remarks>
    public bool RemoveAbandonedOnMaintenance { get; set; }

    /// <summary>
    /// The abandoned-object judgment timeout.<br />
    /// Default value: <c>TimeSpan.FromSeconds(300)</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_REMOVE_ABANDONED_TIMEOUT_SECONDS"/>; 300 s aligns with
    /// CHOPIN's <c>AbandonedConfig.RemoveAbandonedTimeout</c>).
    /// </summary>
    /// <remarks>
    /// Purpose: how long a borrowed object may stay out before it is treated as abandoned and
    /// eligible for reclamation (when <see cref="RemoveAbandonedOnBorrow"/> or
    /// <see cref="RemoveAbandonedOnMaintenance"/> is on).<br />
    /// Note: this is independent of <see cref="LeakDetectionThreshold"/> — leak detection is a
    /// forensics counter (default 30 minutes), reclamation is an opt-in destructive action
    /// (default 5 minutes).<br />
    /// Boundary: should be greater than <see cref="TimeSpan.Zero"/> (enforced by
    /// <see cref="IsValid"/> when either reclamation toggle is on).
    /// </remarks>
    public TimeSpan RemoveAbandonedTimeout { get; set; } = TimeSpan.FromSeconds(HayateConstant.DEFAULT_REMOVE_ABANDONED_TIMEOUT_SECONDS);

    /// <summary>
    /// Whether to log a warning (with the captured lease trace, if any) when an abandoned object
    /// is reclaimed.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: the forensics counterpart of reclamation — CHOPIN's <c>LogAbandoned</c>. The
    /// lease trace is only present when <see cref="LeakTraceCaptureMode"/> captured one; the log
    /// otherwise states that the object was abandoned without a captured stack.<br />
    /// Boundary: only meaningful while reclamation (<see cref="RemoveAbandonedOnBorrow"/> or
    /// <see cref="RemoveAbandonedOnMaintenance"/>) is on; without reclamation nothing is ever
    /// logged.
    /// </remarks>
    public bool LogAbandoned { get; set; }

    /// <summary>
    /// The background maintenance cadence for abandoned recovery, in milliseconds.<br />
    /// Default value: <c>30000</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_REMOVE_ABANDONED_INTERVAL_MILLISECONDS"/>; the same
    /// cadence as <see cref="EvictionIntervalMs"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: how often the background pass scans for abandoned objects when
    /// <see cref="RemoveAbandonedOnMaintenance"/> is on.<br />
    /// Boundary: values &lt;= 0 are floored to 1 ms by <see cref="ApplyFeatureSwitches"/> so an
    /// enabled maintenance pass can never spin with a zero period.
    /// </remarks>
    public int RemoveAbandonedIntervalMs { get; set; } = HayateConstant.DEFAULT_REMOVE_ABANDONED_INTERVAL_MILLISECONDS;

    #endregion

    #region Capacity alarm

    /// <summary>
    /// Capacity warning threshold (utilization, borrowed count / MaxPoolSize).<br />
    /// Default value: <c>0</c> (0 means capacity warnings are disabled, with zero hot-path
    /// overhead).
    /// </summary>
    /// <remarks>
    /// Purpose: when the borrow water level crosses this ratio, fires the
    /// <see cref="OnCapacityWarning"/> callback once.<br />
    /// Special case: edge-debounced — crossing the line fires only once; after falling back below
    /// the threshold and resetting silently, it can fire again.<br />
    /// Boundary: 0 (disabled) or (0, 1]; values &gt; 1 are clamped to 1 by
    /// <see cref="ApplyFeatureSwitches"/>.
    /// </remarks>
    public double WarnAtRatio { get; set; }

    /// <summary>
    /// Capacity critical threshold (utilization, borrowed count / MaxPoolSize).<br />
    /// Default value: <c>0</c> (0 means critical alarms are disabled).
    /// </summary>
    /// <remarks>
    /// Purpose: when the borrow water level crosses this ratio, fires the
    /// <see cref="OnCapacityCritical"/> callback once.<br />
    /// Special case: when enabled together with <see cref="WarnAtRatio"/>, it must not be smaller
    /// than it (normalized by auto-raising to WarnAtRatio); crossing directly from Normal to
    /// Critical fires only the Critical callback, without a supplementary Warning.<br />
    /// Boundary: 0 (disabled) or (0, 1]; values &gt; 1 are clamped to 1 by
    /// <see cref="ApplyFeatureSwitches"/>.
    /// </remarks>
    public double CriticalAtRatio { get; set; }

    /// <summary>
    /// Capacity warning callback (fires once on a state flip when utilization &gt;=
    /// <see cref="WarnAtRatio"/>). Default <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The callback runs inline on the borrow/return path and should stay lightweight (return
    /// within milliseconds); exceptions thrown inside it are caught and logged by the pool and do
    /// not affect the borrow/return main flow.
    /// </remarks>
    public Action<HayatePoolCapacityAlarmEventArgs>? OnCapacityWarning { get; set; }

    /// <summary>
    /// Capacity critical callback (fires once on a state flip when utilization &gt;=
    /// <see cref="CriticalAtRatio"/>). Default <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The callback runs inline on the borrow/return path and should stay lightweight (return
    /// within milliseconds); exceptions thrown inside it are caught and logged by the pool and do
    /// not affect the borrow/return main flow.
    /// </remarks>
    public Action<HayatePoolCapacityAlarmEventArgs>? OnCapacityCritical { get; set; }

    #endregion

    #region Circuit breaker

    /// <summary>
    /// Whether to enable the pool-level availability circuit breaker.<br />
    /// Default value: <c>false</c> (the feature is opt-in, so a pool that does not ask for it behaves
    /// exactly as before).
    /// </summary>
    /// <remarks>
    /// Purpose: when the dependency behind the pool fails, the application reports it with
    /// <c>SetUnavailable</c>; after <see cref="HayateCircuitBreakerOptions.FailureThreshold"/> consecutive
    /// reports the pool marks itself unavailable and every further <c>Acquire</c> / <c>AcquireAsync</c> fails
    /// immediately instead of handing out objects that are likely to be broken — the whole pool is taken out
    /// of service rather than each caller discovering the failure on its own. A background probe then decides
    /// when the dependency is healthy again and brings the pool back automatically.<br />
    /// Special case: the feature is switched off by the lean fast path during normalization, which keeps the
    /// lean borrow/return path byte-identical to the published measurement. It is also fixed at build time:
    /// the pool snapshots the setting in its constructor, so <c>ReloadConfig</c> cannot turn it on or off.<br />
    /// Cost: when disabled, the borrow path pays one perfectly predicted branch. When enabled, an available
    /// pool pays one volatile read per borrow, and an unavailable pool fails before doing any work at all.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable for pools whose objects come from a network dependency (database, message
    /// broker, remote service) so a dependency outage degrades into fast failures instead of a queue of
    /// timeouts. Leave it disabled for in-memory objects, which cannot fail as a group.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableCircuitBreaker = true };
    /// </code>
    /// </example>
    public bool EnableCircuitBreaker { get; set; } = false;

    /// <summary>
    /// The circuit-breaker settings, used when <see cref="EnableCircuitBreaker"/> is on.
    /// </summary>
    /// <remarks>
    /// Never <c>null</c>; the defaults keep the pool unavailable for 30 seconds and then probe every 5
    /// seconds, and no probe is configured — so out of the box recovery is manual via <c>SetAvailable</c>.
    /// Assign a configured instance to change the thresholds, or set the individual members on the instance
    /// that is already there.
    /// </remarks>
    public HayateCircuitBreakerOptions CircuitBreaker { get; set; } = new HayateCircuitBreakerOptions();

    /// <summary>
    /// Callback invoked when the pool becomes available again (the breaker closes). Default <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Fires once per transition, either from a successful probe or from an explicit <c>SetAvailable</c>; it
    /// does not fire when the pool was already available. The callback runs on whichever thread performed the
    /// transition — the background timer for a probe, the caller's thread for <c>SetAvailable</c> — and
    /// should stay lightweight; exceptions thrown inside it are caught and logged by the pool.
    /// </remarks>
    public Action<HayatePoolAvailabilityEventArgs>? OnAvailable { get; set; }

    /// <summary>
    /// Callback invoked when the pool becomes unavailable (the breaker trips). Default <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Fires once per transition, on the thread whose <c>SetUnavailable</c> call tripped the breaker; it does
    /// not fire again while the pool stays unavailable, and it does not fire for failure reports made before
    /// the threshold was reached. Exceptions thrown inside it are caught and logged by the pool.
    /// </remarks>
    public Action<HayatePoolAvailabilityEventArgs>? OnUnavailable { get; set; }

    #endregion

    #region Metrics

    /// <summary>
    /// Master switch for the whole diagnostic surface of the general-purpose engine: the cumulative
    /// counters, the timing statistics, the <c>IHayateMetrics</c> sink and the per-operation debug
    /// trace.<br />
    /// Default value: <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: give an extreme-lightweight deployment one hard switch that takes the entire
    /// bookkeeping surface off the borrow and return paths, instead of trimming the individual feature
    /// switches one by one. This is what also stops <see cref="HayatePoolStats.TotalAcquired"/> — the
    /// one counter <see cref="EnableMetrics"/> deliberately leaves running (see
    /// <c>docs/metrics-gating.md</c>) — so a pool with diagnostics off performs no counter write, no
    /// metrics callback and no per-operation trace entry at all.<br />
    /// Special case: it is a master gate, so switching it off normalizes <see cref="EnableMetrics"/>
    /// and <see cref="EnableAllocationTracking"/> to <c>false</c> as well; the collapsed configuration
    /// is visible through <c>GetOptions</c>. Registering a custom <c>IHayateMetrics</c> while diagnostics
    /// are off fails the build rather than silently discarding the registration, exactly as it does with
    /// metrics off. With diagnostics off every cumulative counter and every timing statistic reports 0,
    /// and the engine writes no per-operation debug entry; lifecycle and problem logs
    /// (<c>Information</c> / <c>Warning</c> / <c>Error</c>) are untouched, so construction, disposal and
    /// failure reporting still reach the log. Counters owned by another feature switch — leak detection
    /// and the capacity alarm — keep following their own switch.<br />
    /// Boundary: boolean master switch, fixed at construction like every other feature switch, so
    /// <c>ReloadConfig</c> cannot change it on a live pool.<br />
    /// Recommended range: leave it on (the default) unless the pool sits on a measured hot path where
    /// every counter write and trace entry is unwanted. The lean fast path forces it off, because that
    /// path keeps no counters and writes no diagnostics by construction.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableDiagnostics = false };
    /// </code>
    /// </example>
    public bool EnableDiagnostics { get; set; } = true;

    /// <summary>
    /// Whether to enable metrics collection.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: emit pool runtime metrics for observability.<br />
    /// Special case: enabling it on high-frequency paths adds a small overhead; it is a sub-switch of
    /// <see cref="EnableDiagnostics"/>, which normalizes it to <c>false</c> when the master switch is off.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable in test/production; disable under extreme-performance benchmarks.
    /// </remarks>
    public bool EnableMetrics { get; set; } = false;

    /// <summary>
    /// Whether to enable allocation tracking.<br />
    /// Default value: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Purpose: count the per-thread allocation delta (in bytes) on the borrow/return path, to
    /// locate hidden allocations on the hot path; corroborates the benchmark's <c>B/Op</c> metric.<br />
    /// Special case: each borrow/return makes one extra allocation-query API call (near zero
    /// allocation) and still has a small overhead; the <c>net48</c> / <c>netstandard2.0</c> target
    /// frameworks lack this API, so tracking is silently unavailable there (count stays 0). It is a
    /// sub-switch of <see cref="EnableDiagnostics"/>, which normalizes it to <c>false</c> when the
    /// master switch is off.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable during diagnosis/tuning; disable on production hot paths.
    /// </remarks>
    public bool EnableAllocationTracking { get; set; } = false;

    #endregion

    #region Warmup readiness

    /// <summary>
    /// Whether to run warmup in the background and have borrow operations wait for warmup to
    /// finish.<br />
    /// Default value: <c>false</c> (consistent with 2.4 and earlier — synchronous warmup at
    /// construction, with no extra wait on borrow).
    /// </summary>
    /// <remarks>
    /// Purpose: when <c>true</c>, construction returns immediately and the <see cref="MinPoolSize"/>
    /// warmup completes on a background thread; before warmup finishes, all <c>Acquire</c> /
    /// <c>AcquireAsync</c> block on the readiness signal, so callers always get a ready pool
    /// (avoiding large creation cost landing on the first business request during cold start).<br />
    /// Special case: mutually exclusive with cold-pool bootstrap — during the wait, the "synchronous
    /// create on empty pool" bootstrap is not triggered; bootstrap yields to the readiness signal;
    /// if warmup fails, the signal is still set (no permanent block) and the failure reason is
    /// logged.<br />
    /// Boundary: boolean switch.<br />
    /// Recommended range: enable for services that can tolerate a small startup delay yet want zero
    /// creation cost on the first request.
    /// </remarks>
    public bool WaitForWarmup { get; set; } = false;

    #endregion

    #region Lifetime

    /// <summary>
    /// Whether the pool disposes itself when the process is shutting down.<br />
    /// Default value: <c>false</c> — disposal stays the owner's responsibility unless asked otherwise.
    /// </summary>
    /// <remarks>
    /// Purpose: a pool whose owner is the process itself never gets its <c>Dispose</c> call, because
    /// nothing user-facing runs at that point. Switching this on subscribes the pool to process exit (and
    /// to a terminal Ctrl+C), so pooled objects are released on the way out — which matters when those
    /// objects hold resources that outlive the process, such as file handles, connections or pooled
    /// buffers pinned outside the GC's reach.<br />
    /// Cost: nothing on the borrow or return path. The subscription is taken once at construction and
    /// removed on disposal, so the only ongoing footprint is a single entry in the hook's handler list.<br />
    /// Boundary: fixed at construction, like every other lifetime-affecting switch; <c>ReloadConfig</c>
    /// neither subscribes nor unsubscribes. Disposal is idempotent, so it is safe whichever comes first —
    /// the explicit call or the shutdown notification.<br />
    /// Recommended range: leave off for short-lived pools whose owning scope can dispose them, which is
    /// the normal case and keeps lifetime explicit. Turn on for long-lived process-wide pools whose
    /// objects own unmanaged resources. Hosted applications that already have a shutdown step (an
    /// application lifetime, a container) should dispose there instead, or supply that signal through
    /// <c>WithShutdownHook</c>, and stay off the process-wide hook.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { EnableAutoDisposeWithSystem = true };
    /// </code>
    /// </example>
    public bool EnableAutoDisposeWithSystem { get; set; } = false;

    #endregion

    #region Profiles

    /// <summary>
    /// Applies the lean profile in one call: the wrapper-free fast path with every bookkeeping feature
    /// switched off, so a pure pooling workload runs at the reference <c>DefaultObjectPool</c> cost.
    /// </summary>
    /// <remarks>
    /// The profile only sets the execution mode together with the feature switches — the same normalized
    /// state <see cref="ApplyFeatureSwitches"/> derives from <see cref="EnableLean"/>, written out
    /// explicitly so the collapsed configuration is visible on the options object itself. Pool sizing
    /// (<see cref="MinPoolSize"/>, <see cref="MaxPoolSize"/>), the timeouts and the reject policy are
    /// deliberately <i>not</i> part of the profile: they describe the workload rather than the feature
    /// set, so a profile can be applied and then tuned, in either call order.<br />
    /// Because the profile pins <see cref="EnableLean"/>, it wins over any feature toggle enabled before
    /// it, and a later explicit <c>EnableXxx = true</c> is still normalized away by
    /// <see cref="ApplyFeatureSwitches"/> — lean is a mode, not a knob. Use
    /// <see cref="UseFullProfile"/> to leave the mode.
    /// </remarks>
    /// <returns>The same options instance, for chaining.</returns>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { MinPoolSize = 16, MaxPoolSize = 64 }.UseLeanProfile();
    /// </code>
    /// </example>
    public HayatePoolOptions UseLeanProfile()
    {
        EnableLean = true;

        EnableSharding = false;
        EnableAutoScaling = false;

        EnableValidation = false;
        ValidateOnBorrow = false;
        ValidateOnReturn = false;
        ValidateWhileIdle = false;

        // The lean fast path is meant to stay minimal on the borrow side, so the profile pins the reset to
        // the historical side rather than merely clearing the borrow side: a caller who had moved the reset
        // to borrow would otherwise be left with no reset at all, which is a silent drop rather than the
        // downgrade the mode promises. Both values are written out because that is what this profile does
        // with every switch ApplyFeatureSwitches derives — the collapsed state is visible on the object.
        ResetOnReturn = true;
        ResetOnBorrow = false;

        EnableEviction = false;
        EnableGenerationOptimization = false;
        EnableLeakDetection = false;

        // Abandoned recovery is a destructive opt-in: the lean profile (and lean mode itself)
        // has no wrapper registry for borrowed objects, so both toggles are forced off.
        RemoveAbandonedOnBorrow = false;
        RemoveAbandonedOnMaintenance = false;

        EnableDiagnostics = false;
        EnableMetrics = false;
        EnableAllocationTracking = false;

        WarnAtRatio = 0;
        CriticalAtRatio = 0;
        ShardAffinityMode = HayateShardAffinityMode.None;

        return this;
    }

    /// <summary>
    /// Applies the full profile in one call: every optional feature switch is turned on, so the pool runs
    /// with sharding, auto-scaling, validation, eviction, generation optimization, leak detection, metrics
    /// and allocation tracking all active.
    /// </summary>
    /// <remarks>
    /// "Full" means every feature <i>switch</i>. The numeric thresholds, the intervals and the validation
    /// sub-switches keep their documented defaults, so <see cref="WarnAtRatio"/> and
    /// <see cref="CriticalAtRatio"/> stay at 0 (the capacity alarm stays disabled),
    /// <see cref="ValidateOnBorrow"/>, <see cref="ValidateOnReturn"/> and
    /// <see cref="ValidateWhileIdle"/> stay <c>false</c>, <see cref="ResetOnBorrow"/> stays <c>false</c>
    /// (so the reset stays on the return side, where <see cref="ResetOnReturn"/> already puts it), and
    /// sizing plus reject semantics are untouched.
    /// The profile is the exact opposite of <see cref="UseLeanProfile"/>: it clears
    /// <see cref="EnableLean"/>, so applying it after the lean profile leaves a full-featured pool — and it
    /// re-opens <see cref="EnableDiagnostics"/>, which the lean profile closed.<br />
    /// Note that the shipped defaults already enable the six core features, so the profile differs from a
    /// default-configured pool by switching the two observability features
    /// (<see cref="EnableMetrics"/>, <see cref="EnableAllocationTracking"/>) on as well. Both can be
    /// turned back off afterwards with a normal feature call, and <see cref="EnableDiagnostics"/> — already
    /// on by default — is written out explicitly so the full state is visible on the profile.
    /// </remarks>
    /// <returns>The same options instance, for chaining.</returns>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { MaxPoolSize = 256 }.UseFullProfile();
    /// </code>
    /// </example>
    public HayatePoolOptions UseFullProfile()
    {
        EnableLean = false;

        EnableSharding = true;
        EnableAutoScaling = true;

        EnableValidation = true;
        EnableEviction = true;
        EnableGenerationOptimization = true;
        EnableLeakDetection = true;

        EnableDiagnostics = true;
        EnableMetrics = true;
        EnableAllocationTracking = true;

        return this;
    }

    /// <summary>
    /// Applies a named preset: the ready-made configuration catalogue behind <see cref="HayatePoolPreset"/>.
    /// </summary>
    /// <param name="preset">The preset to apply.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined preset.</exception>
    /// <remarks>
    /// The named counterpart of <see cref="UseLeanProfile"/> and <see cref="UseFullProfile"/>, and the
    /// same mechanism: the preset assigns the options it owns and leaves every other option alone, so it
    /// can be applied first and tuned afterwards, in either call order, and a later feature call still
    /// overrules it. See <see cref="HayatePoolPreset"/> for the field set each preset owns.<br />
    /// <see cref="HayatePoolPreset.Default"/> is the exception: it owns the whole surface, including
    /// sizing, timeouts, the reject policy and the callbacks, and therefore restores the shipped defaults.
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new HayatePoolOptions { MaxPoolSize = 256 }.UsePreset(HayatePoolPreset.ConnectionPool);
    /// </code>
    /// </example>
    public HayatePoolOptions UsePreset(HayatePoolPreset preset)
    {
        HayatePoolPresets.Apply(preset, this);
        return this;
    }

    #endregion

    /// <summary>
    /// Creates a new <see cref="HayatePoolOptions"/> and copies all current settings into it.
    /// </summary>
    /// <returns>A new options instance with the same settings as this one.</returns>
    /// <example>
    /// <code>
    /// var copy = options.CopyTo();
    /// </code>
    /// </example>
    public HayatePoolOptions CopyTo()
    {
        var options = new HayatePoolOptions();
        return CopyTo(options);
    }

    /// <summary>
    /// Copies all current settings into the supplied <paramref name="options"/> instance.
    /// </summary>
    /// <param name="options">The target options instance to copy values into.</param>
    /// <returns>The same <paramref name="options"/> instance, updated with the current settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <example>
    /// <code>
    /// var target = new HayatePoolOptions();
    /// options.CopyTo(target);
    /// </code>
    /// </example>
    public HayatePoolOptions CopyTo(HayatePoolOptions options)
    {
        // Null guard to keep the method robust
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options), "Target options instance cannot be null");
        }

        // Basic pool size
        options.MinPoolSize = this.MinPoolSize;
        options.MaxPoolSize = this.MaxPoolSize;
        options.SoftCapacity = this.SoftCapacity;
        options.MaxActiveObjects = this.MaxActiveObjects;

        // Timeout
        options.DefaultAcquireTimeout = this.DefaultAcquireTimeout;

        // Reject policy
        options.RejectPolicy = this.RejectPolicy;

        // Creation retry
        options.CreationRetryCount = this.CreationRetryCount;
        options.CreationRetryDelay = this.CreationRetryDelay;

        // Execution mode (the ArrayPool storage switch is part of the mode, not of the bookkeeping
        // surface, so it travels with it: a copy that dropped it would silently hand back a pool
        // configured for a storage shape its source never had)
        options.EnableLean = this.EnableLean;
        options.EnableArrayPoolStorage = this.EnableArrayPoolStorage;

        // Storage backend (G-9 / G-10). Both are storage shapes rather than bookkeeping switches, so
        // they travel with the execution mode for the same reason the ArrayPool switch does: a copy
        // that dropped one would silently rebuild the pool on the default backend.
        options.EnableLockFreeStorage = this.EnableLockFreeStorage;
        options.EnableThreadLocalFrontSlot = this.EnableThreadLocalFrontSlot;

        // Sharding
        options.EnableSharding = this.EnableSharding;
        options.ShardCount = this.ShardCount;
        options.ShardAffinityMode = this.ShardAffinityMode;
        options.CustomShardAffinity = this.CustomShardAffinity;
        options.BorrowStrategy = this.BorrowStrategy;

        // Scaling
        options.EnableAutoScaling = this.EnableAutoScaling;
        options.ScalingIntervalMs = this.ScalingIntervalMs;
        options.ScaleUpThreshold = this.ScaleUpThreshold;
        options.ScaleDownThreshold = this.ScaleDownThreshold;
        options.ScaleUpCooldownSeconds = this.ScaleUpCooldownSeconds;
        options.ScaleDownCooldownSeconds = this.ScaleDownCooldownSeconds;
        options.ScaleUpStep = this.ScaleUpStep;
        options.ScaleDownStep = this.ScaleDownStep;

        // Validation
        options.EnableValidation = this.EnableValidation;
        options.ValidateOnBorrow = this.ValidateOnBorrow;
        options.ValidateOnReturn = this.ValidateOnReturn;
        options.ValidateWhileIdle = this.ValidateWhileIdle;
        options.ValidateIntervalMs = this.ValidateIntervalMs;

        // Misuse detection
        options.ErrorOnIncorrectUsage = this.ErrorOnIncorrectUsage;

        // Reset timing
        options.ResetOnReturn = this.ResetOnReturn;
        options.ResetOnBorrow = this.ResetOnBorrow;

        // Eviction
        options.EnableEviction = this.EnableEviction;
        options.MaxLifeTime = this.MaxLifeTime;
        options.EnableLifetimeRotationOnBorrow = this.EnableLifetimeRotationOnBorrow;
        options.MaxIdleTime = this.MaxIdleTime;
        options.SoftMinEvictableIdleTime = this.SoftMinEvictableIdleTime;
        options.EvictionIntervalMs = this.EvictionIntervalMs;
        options.NumTestsPerEvictionRun = this.NumTestsPerEvictionRun;

        // Generation
        options.EnableGenerationOptimization = this.EnableGenerationOptimization;
        options.GenerationThresholdMs = this.GenerationThresholdMs;
        options.OldGenerationValidationInterval = this.OldGenerationValidationInterval;

        // Leak detection
        options.EnableLeakDetection = this.EnableLeakDetection;
        options.LeakDetectionThreshold = this.LeakDetectionThreshold;
        options.LeakTraceCaptureMode = this.LeakTraceCaptureMode;
        options.LeakTraceSampleRate = this.LeakTraceSampleRate;

        // Abandoned recovery
        options.RemoveAbandonedOnBorrow = this.RemoveAbandonedOnBorrow;
        options.RemoveAbandonedOnMaintenance = this.RemoveAbandonedOnMaintenance;
        options.RemoveAbandonedTimeout = this.RemoveAbandonedTimeout;
        options.LogAbandoned = this.LogAbandoned;
        options.RemoveAbandonedIntervalMs = this.RemoveAbandonedIntervalMs;

        // Capacity alarm
        options.WarnAtRatio = this.WarnAtRatio;
        options.CriticalAtRatio = this.CriticalAtRatio;
        options.OnCapacityWarning = this.OnCapacityWarning;
        options.OnCapacityCritical = this.OnCapacityCritical;

        // Circuit breaker (the settings object is copied, not shared, so mutating the copy cannot
        // reconfigure the pool it came from)
        options.EnableCircuitBreaker = this.EnableCircuitBreaker;
        options.CircuitBreaker = this.CircuitBreaker.CopyTo();
        options.OnAvailable = this.OnAvailable;
        options.OnUnavailable = this.OnUnavailable;

        // Diagnostics (the master switch of the bookkeeping surface; carried explicitly so a copy
        // never re-opens a surface its source closed)
        options.EnableDiagnostics = this.EnableDiagnostics;

        // Metrics
        options.EnableMetrics = this.EnableMetrics;

        // Allocation tracking
        options.EnableAllocationTracking = this.EnableAllocationTracking;

        // Warmup readiness
        options.WaitForWarmup = this.WaitForWarmup;

        // Lifetime
        options.EnableAutoDisposeWithSystem = this.EnableAutoDisposeWithSystem;

        return options;
    }

    //public HayatePoolOptions AutoCopy(HayatePoolOptions options)
    //{
    //    options ??= new();

    //    var properties = typeof(HayatePoolOptions).GetProperties();
    //    foreach (var prop in properties)
    //    {
    //        if (!prop.CanRead || !prop.CanWrite) continue;
    //        prop.SetValue(options, prop.GetValue(this));
    //    }

    //    return options;
    //}

    /// <summary>
    /// Normalizes dependent options so the configuration is internally consistent and passes
    /// <see cref="IsValid"/>.
    /// </summary>
    /// <remarks>
    /// Disabling a feature (sharding, auto-scaling, validation) forces its related settings into a
    /// safe state; sample rates and alarm thresholds are clamped and ordering constraints are
    /// enforced. Call this before building or validating the pool.
    /// </remarks>
    /// <example>
    /// <code>
    /// options.ApplyFeatureSwitches();
    /// if (options.IsValid()) { /* build the pool */ }
    /// </code>
    /// </example>
    public void ApplyFeatureSwitches()
    {
        // Lean fast-path normalization. Declared first so the sharding branch below collapses the
        // pool to a single shard exactly as it would for an explicitly sharding-free pool.
        //
        // CQ-1 — the rule this file's three lean-conflict sites share. A switch that promises a bound
        // or a guarantee cannot be silently switched off: the owner would go on believing the bound
        // holds, and nothing in the pool would contradict them. A switch that promises a capability
        // the mode deliberately omits is normalized instead — "lean runs none of these" is the mode's
        // documented meaning, GetOptions() shows the collapsed state, and no promise is broken. So:
        // guarantee ⇒ refuse to build, capability ⇒ normalize. Call order plays no part in either
        // verdict: every setter writes its value unconditionally and normalization runs once, after
        // the last builder call, which is why the two verdicts cannot differ by call sequence.
        //
        // Where the verdict lands is a separate question. IsValid() carries the ones the options alone
        // decide; Build() carries the ones whose message has to name the call to remove, or that
        // involve a registration the options cannot see (the borrow-order guard is options-decidable
        // and sits there only to share that message shape with its neighbours — a placement, not a
        // second rule).
        //
        // The switches below are the capability half: lean stores the pooled value directly in a
        // bounded array and moves it through Interlocked operations, so it has nowhere to keep
        // per-object timestamps, generations, leak forensics, metrics samples, capacity-alarm levels
        // or shard free lists.
        if (EnableLean)
        {
            EnableSharding = false;
            EnableAutoScaling = false;
            EnableValidation = false;
            ValidateOnBorrow = false;
            ValidateOnReturn = false;
            ValidateWhileIdle = false;
            // Reset timing is not a storage problem — the lean borrow path does call the policy's
            // OnAcquire, so it could honour a borrow-side reset. It is pinned back to the return side
            // anyway, because a minimal borrow path is the whole point of the mode and a reset there works
            // against it. This is a deliberate choice, not a structural impossibility.
            // Both halves are written: clearing only the borrow side would leave a caller who had moved the
            // reset there with no reset at all, which is a silent drop rather than a downgrade.
            ResetOnReturn = true;
            ResetOnBorrow = false;
            EnableEviction = false;
            EnableGenerationOptimization = false;
            EnableLeakDetection = false;
            // Abandoned recovery is a destructive opt-in and the lean fast path stores values
            // directly (no wrapper registry to scan), so both toggles are forced off in lean mode.
            RemoveAbandonedOnBorrow = false;
            RemoveAbandonedOnMaintenance = false;
            EnableDiagnostics = false;
            EnableMetrics = false;
            EnableAllocationTracking = false;
            EnableCircuitBreaker = false;
            WarnAtRatio = 0;
            CriticalAtRatio = 0;
            ShardAffinityMode = HayateShardAffinityMode.None;
            // The general-purpose storage backends below are the shape of the *shard* free list and
            // of the pre-shard front tier. Lean has neither — it stores the pooled value directly in
            // a bounded array — so a switch left on here would be accepted and never applied, which
            // is exactly the failure this normalization exists to prevent. Unlike the ArrayPool
            // switch, these two do not change lean's own storage, so they are not preserved.
            EnableLockFreeStorage = false;
            EnableThreadLocalFrontSlot = false;
        }

        // Diagnostics master switch normalization. Closing it closes the whole bookkeeping surface, so
        // the two sub-switches it owns are turned off with it: leaving EnableMetrics or allocation
        // tracking on while nothing can be recorded would present a collapsed configuration that still
        // looks like it collects. The same "mode wins" rule as the lean block above — the disabled
        // combination is normalized rather than rejected, and the result is visible through GetOptions.
        if (!EnableDiagnostics)
        {
            EnableMetrics = false;
            EnableAllocationTracking = false;
        }

        // Sharding disabled: elastic single shard
        if (!EnableSharding)
        {
            ShardCount = 1;
        }

        // Auto-scaling disabled (behavior changed in 2.1):
        // Old semantics forced MaxPoolSize = MinPoolSize, collapsing capacity to 0 when Min=0,
        // and silently rejecting all returns because each shard's max was 0 (same root cause as the
        // historical "minimal pool Min had to be raised to 250" oddity).
        // New semantics: only auto-scaling is disabled (scale-up callbacks and timeout-driven forced
        // scale-up are both gated by EnableAutoScaling and cannot exceed Max), MaxPoolSize keeps the
        // user's explicit value as a hard cap; it is only raised to preserve ordering when Max < Min,
        // avoiding an IsValid validation failure.
        if (!EnableAutoScaling && MaxPoolSize < MinPoolSize)
        {
            MaxPoolSize = MinPoolSize;
        }

        // Validation disabled: force all validation toggles off
        if (!EnableValidation)
        {
            ValidateOnBorrow = false;
            ValidateOnReturn = false;
            ValidateWhileIdle = false;
        }

        // Leak-trace sample-rate guard: values <= 0 are auto-corrected to the default (only used in
        // Sampled mode, must be >= 1)
        if (LeakTraceSampleRate < 1)
        {
            LeakTraceSampleRate = HayateConstant.DEFAULT_LEAK_TRACE_SAMPLE_RATE;
        }

        // Abandoned-recovery maintenance cadence guard: an enabled maintenance pass must never spin
        // with a zero period, so values <= 0 are floored to 1 ms (the same floor as the sibling
        // eviction / scaling / validation intervals).
        if (RemoveAbandonedIntervalMs < 1)
        {
            RemoveAbandonedIntervalMs = 1;
        }

        // Affinity normalization: when Custom mode is selected without a delegate, fall back to None
        // (borrow-path robustness takes priority; we do not reject at the IsValid layer, so a missing
        // strategy cannot fail the entire pool build). Unknown enum values also fall back to None.
        if (ShardAffinityMode == HayateShardAffinityMode.Custom && CustomShardAffinity is null)
        {
            ShardAffinityMode = HayateShardAffinityMode.None;
        }
        else if (ShardAffinityMode != HayateShardAffinityMode.None &&
                 ShardAffinityMode != HayateShardAffinityMode.Thread &&
                 ShardAffinityMode != HayateShardAffinityMode.Custom)
        {
            ShardAffinityMode = HayateShardAffinityMode.None;
        }

        // Borrow-order normalization: an unrecognised value falls back to the default instead of
        // leaving the shard with a selection it cannot perform (the same robustness rule as the affinity
        // mode above). A value the engine *can* perform is deliberately *not* rewritten when the pool
        // cannot honour it — a Lifo request on the lean fast path, or any order but Fifo on the
        // lock-free store or alongside the front tier, cannot be honoured, and silently turning it into
        // Fifo is exactly the "accepted but never applied" outcome this switch exists to avoid. Those
        // combinations fail the build instead. G-21: the whitelist below is what keeps Lru and Priority
        // from being swallowed here — a new value that is not listed would be rewritten to Fifo before
        // Build ever sees it.
        if (BorrowStrategy != HayateBorrowStrategy.Fifo && BorrowStrategy != HayateBorrowStrategy.Lifo &&
            BorrowStrategy != HayateBorrowStrategy.Lru && BorrowStrategy != HayateBorrowStrategy.Priority)
        {
            BorrowStrategy = HayateBorrowStrategy.Fifo;
        }

        // Capacity alarm threshold normalization: negative values are treated as disabled (0), and
        // values greater than 1 are clamped to 1; when both thresholds are enabled, Critical must
        // not be below Warn (raise it to Warn to keep the state machine monotonic).
        if (WarnAtRatio < 0) WarnAtRatio = 0;
        else if (WarnAtRatio > 1) WarnAtRatio = 1;

        if (CriticalAtRatio < 0) CriticalAtRatio = 0;
        else if (CriticalAtRatio > 1) CriticalAtRatio = 1;

        if (WarnAtRatio > 0 && CriticalAtRatio > 0 && CriticalAtRatio < WarnAtRatio)
        {
            CriticalAtRatio = WarnAtRatio;
        }

        // Circuit-breaker normalization: a threshold below 1 would trip on a report that was never made, a
        // non-positive window would either probe the failing dependency on every tick or never probe it, and
        // a percentage outside 0..100 is not a ratio at all. Each one falls back to a value the pool can run
        // with rather than making the pool unusable. The percentage is the single setting whose default (0)
        // means "off", so clamping it is not the same as resetting it to a usable value — an out-of-range
        // value lands on 0 and leaves the percentage rule disabled rather than silently turning it on.
        if (EnableCircuitBreaker)
        {
            var breaker = CircuitBreaker ??= new HayateCircuitBreakerOptions();

            if (breaker.FailureThreshold < 1)
            {
                breaker.FailureThreshold = HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_THRESHOLD;
            }

            if (breaker.ResetTimeout <= TimeSpan.Zero)
            {
                breaker.ResetTimeout = TimeSpan.FromSeconds(HayateConstant.DEFAULT_CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS);
            }

            if (breaker.ProbeInterval <= TimeSpan.Zero)
            {
                breaker.ProbeInterval = TimeSpan.FromSeconds(HayateConstant.DEFAULT_CIRCUIT_BREAKER_PROBE_INTERVAL_SECONDS);
            }

            if (breaker.SuccessThreshold < 1)
            {
                breaker.SuccessThreshold = HayateConstant.DEFAULT_CIRCUIT_BREAKER_SUCCESS_THRESHOLD;
            }

            if (breaker.MinimumThroughput < 0)
            {
                breaker.MinimumThroughput = HayateConstant.DEFAULT_CIRCUIT_BREAKER_MINIMUM_THROUGHPUT;
            }

            if (double.IsNaN(breaker.FailurePercentageThreshold) || breaker.FailurePercentageThreshold < 0d)
            {
                breaker.FailurePercentageThreshold = HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_PERCENTAGE_THRESHOLD;
            }
            else if (breaker.FailurePercentageThreshold > 100d)
            {
                breaker.FailurePercentageThreshold = 100d;
            }
        }
    }

    /// <summary>
    /// Validates that the current option values form a legal, buildable configuration.
    /// </summary>
    /// <returns><c>true</c> if the configuration is valid; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Internally calls <see cref="ApplyFeatureSwitches"/> first, then checks pool-size bounds,
    /// scaling thresholds, time spans and feature-specific constraints.
    /// </remarks>
    /// <example>
    /// <code>
    /// if (!options.IsValid())
    ///     throw new InvalidOperationException("Invalid pool options");
    /// </code>
    /// </example>
    public bool IsValid()
    {
        ApplyFeatureSwitches();

        // Basic configuration checks
        if (MinPoolSize < 0 || MaxPoolSize < MinPoolSize) return false;
        if (DefaultAcquireTimeout <= TimeSpan.Zero) return false;
        if (ShardCount < 1 || ShardCount > 32) return false;
        if (CreationRetryCount < 0) return false;
        if (DefaultAcquireTimeout <= TimeSpan.Zero) return false;

        // Soft capacity (T-R): 0 disables the ceiling. A live one has to sit inside the pool's own
        // bounds — below the floor the pool could not hold the minimum it promises, and above the hard
        // ceiling the check could never fire — so both are rejected rather than accepted as a knob that
        // silently does nothing.
        if (SoftCapacity < 0) return false;
        if (SoftCapacity > 0 && (SoftCapacity < MinPoolSize || SoftCapacity > MaxPoolSize)) return false;

        // Active-object ceiling (G-5): 0 disables it. A live one has to sit at or below the hard ceiling,
        // for the same reason SoftCapacity does — above it the pool could never have that many objects out
        // at once, so the check could never fire and the setting would be a knob that silently does
        // nothing. The lean fast path keeps no borrow bookkeeping, so it cannot honour a ceiling either.
        // This is the guarantee half of the CQ-1 rule stated in ApplyFeatureSwitches: a ceiling the owner
        // asked for is not a capability to drop, so the pool refuses to build instead of quietly running
        // without it.
        if (MaxActiveObjects < 0) return false;
        if (MaxActiveObjects > 0 && MaxActiveObjects > MaxPoolSize) return false;
        if (EnableLean && MaxActiveObjects > 0) return false;

        // Checks when auto-scaling is enabled
        if (EnableAutoScaling)
        {
            if (ScaleUpThreshold <= ScaleDownThreshold) return false;
            if (ScaleUpThreshold < 0 || ScaleUpThreshold > 1) return false;
            if (ScaleDownThreshold < 0 || ScaleDownThreshold > 1) return false;
            if (ScaleUpStep < 1) return false;
            if (ScaleDownStep < 1) return false;
        }

        // Time-based validation
        if (EnableEviction)
        {
            if (MaxLifeTime <= TimeSpan.Zero) return false;
            if (MaxIdleTime <= TimeSpan.Zero) return false;
            if (SoftMinEvictableIdleTime <= TimeSpan.Zero) return false;
        }

        // A3a: the lean fast path stores pooled values directly in a bounded array, keeps no per-object
        // timestamps and therefore cannot evaluate an object's age — the borrow-side lifetime rotation
        // can never run there. Every other feature switch lean cannot honour is normalized away by
        // ApplyFeatureSwitches ("the mode wins"), but this one is rejected instead, and it is the second
        // guarantee of the CQ-1 rule stated there: silently switching the rotation off would leave an
        // owner who believes objects are being rotated on hand-out while they are not, which is the exact
        // failure this switch exists to remove.
        if (EnableLean && EnableLifetimeRotationOnBorrow) return false;

        if (EnableSharding)
        {
            if (GenerationThresholdMs < 1000) return false;
            if (OldGenerationValidationInterval < 1) return false;
        }

        if (EnableLeakDetection)
        {
            if (LeakDetectionThreshold <= TimeSpan.Zero) return false;
        }

        // Abandoned recovery is a destructive opt-in: a zero/negative timeout would reclaim
        // immediately-borrowed objects, so it is rejected when either toggle is on.
        if (RemoveAbandonedOnBorrow || RemoveAbandonedOnMaintenance)
        {
            if (RemoveAbandonedTimeout <= TimeSpan.Zero) return false;
        }

        return true;
    }
}
