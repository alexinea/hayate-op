using System;
using DotNetCore.HayateOP.Common;

namespace DotNetCore.HayateOP;

/// <summary>
/// Circuit-breaker settings for the pool-level availability guard, used when
/// <see cref="HayatePoolOptions.EnableCircuitBreaker"/> is on.
/// </summary>
/// <remarks>
/// The breaker protects the pool as a whole rather than an individual object. When the dependency behind
/// the pool fails, the application reports it with <c>SetUnavailable</c>; once the configured number of
/// consecutive failures has accumulated, the pool marks itself unavailable and every further acquire fails
/// immediately instead of handing out objects that are likely to be broken. While unavailable, a background
/// probe runs on the pool's shared timer — the probe is the only background work the breaker adds, it starts
/// only when the breaker trips and stops as soon as the pool recovers.<br />
/// Two trip rules are available and either one is enough: a consecutive failure streak
/// (<see cref="FailureThreshold"/>) and, when enabled, a failure percentage over a sampled window
/// (<see cref="FailurePercentageThreshold"/> together with <see cref="MinimumThroughput"/>). Recovery is
/// either a single probe or a run of healthy probes (<see cref="SuccessThreshold"/>), and an explicit
/// <c>SetAvailable</c> closes the breaker at once whatever the run has reached.
/// </remarks>
/// <example>
/// <code>
/// var options = new HayatePoolOptions
/// {
///     EnableCircuitBreaker = true,
///     CircuitBreaker = new HayateCircuitBreakerOptions
///     {
///         FailureThreshold = 3,
///         ResetTimeout = TimeSpan.FromSeconds(30),
///         ProbeInterval = TimeSpan.FromSeconds(5),
///         Probe = () =&gt; database.Ping()
///     }
/// };
/// </code>
/// </example>
public class HayateCircuitBreakerOptions : IEquatable<HayateCircuitBreakerOptions>
{
    /// <summary>
    /// Number of consecutive reported dependency failures that trips the breaker.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_THRESHOLD"/> (3).
    /// </summary>
    /// <remarks>
    /// Purpose: absorbs transient hiccups — a single failure report does not take the pool out of service.<br />
    /// Special case: the streak counts consecutive reports without an intervening recovery. It is reset to 0
    /// by <c>SetAvailable</c> (explicitly, or through a successful probe), so a pool that recovered needs
    /// repeated fresh failures before it trips again; sporadic failures spread over a healthy lifetime also
    /// accumulate, and an application that knows the dependency is fine can call <c>SetAvailable</c> at any
    /// time to clear the pending streak. Set it to 1 to trip on the first report.<br />
    /// Boundary: values below 1 are normalized to 1 by <see cref="HayatePoolOptions.ApplyFeatureSwitches"/>.
    /// </remarks>
    public int FailureThreshold { get; set; } = HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_THRESHOLD;

    /// <summary>
    /// Failure percentage, in percent, at which the breaker trips. Default value:
    /// <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_PERCENTAGE_THRESHOLD"/> (0, the rule is off).
    /// </summary>
    /// <remarks>
    /// Purpose: complements <see cref="FailureThreshold"/> where a consecutive streak is a poor signal — a
    /// dependency that fails one call in three never produces three failures back to back, yet it is
    /// plainly broken. A report is either a failure (<c>SetUnavailable</c>) or a success
    /// (<c>SetAvailable</c>); the ratio is taken over the reports collected since the breaker last changed
    /// state, because the window is reset on every trip and every recovery. It therefore describes the
    /// current run of the breaker rather than the pool's whole lifetime.<br />
    /// Special case: the two trip rules are alternatives, never a conjunction — the breaker trips when
    /// <em>either</em> the streak or the percentage is reached, so turning this on cannot make a pool
    /// harder to protect. The ratio is only evaluated once <see cref="MinimumThroughput"/> reports have
    /// been collected.<br />
    /// Boundary: values below 0 (and <c>NaN</c>) are normalized to 0, values above 100 to 100, by
    /// <see cref="HayatePoolOptions.ApplyFeatureSwitches"/>.
    /// </remarks>
    public double FailurePercentageThreshold { get; set; } = HayateConstant.DEFAULT_CIRCUIT_BREAKER_FAILURE_PERCENTAGE_THRESHOLD;

    /// <summary>
    /// Number of reports that must have been collected before <see cref="FailurePercentageThreshold"/> is
    /// evaluated. Default value:
    /// <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_MINIMUM_THROUGHPUT"/> (0).
    /// </summary>
    /// <remarks>
    /// Purpose: keeps a cold pool from tripping on its first report — at a 50% threshold a single failure
    /// out of a single report is 100% and would take the pool out of service before it had served anything.
    /// Set it to the smallest sample you consider meaningful (the SNESOXOP reference uses 10).<br />
    /// Interaction: has no effect while <see cref="FailurePercentageThreshold"/> is 0, because the
    /// percentage rule itself is off. <see cref="FailureThreshold"/> is unaffected — the streak still trips
    /// on its own.<br />
    /// Boundary: values below 0 are normalized to 0 by <see cref="HayatePoolOptions.ApplyFeatureSwitches"/>.
    /// </remarks>
    public int MinimumThroughput { get; set; } = HayateConstant.DEFAULT_CIRCUIT_BREAKER_MINIMUM_THROUGHPUT;

    /// <summary>
    /// How long the pool stays unavailable before the background probe starts.<br />
    /// Default value: <c>TimeSpan.FromSeconds(30)</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: gives the failing dependency room to recover without being probed on every tick. No probe
    /// runs during this window, so a short value is safe but increases the probe rate against a dependency
    /// that is still down.<br />
    /// Boundary: values &lt;= <see cref="TimeSpan.Zero"/> are normalized to the default.
    /// </remarks>
    public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromSeconds(HayateConstant.DEFAULT_CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS);

    /// <summary>
    /// Interval between background probes once <see cref="ResetTimeout"/> has elapsed.<br />
    /// Default value: <c>TimeSpan.FromSeconds(5)</c> (constructed from
    /// <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_PROBE_INTERVAL_SECONDS"/>).
    /// </summary>
    /// <remarks>
    /// Purpose: bounds how long the pool stays unavailable after the dependency recovers. This is also the
    /// resolution at which the probe is scheduled, because it is the only period the breaker contributes to
    /// the shared background timer.<br />
    /// Boundary: values &lt;= <see cref="TimeSpan.Zero"/> are normalized to the default.
    /// </remarks>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(HayateConstant.DEFAULT_CIRCUIT_BREAKER_PROBE_INTERVAL_SECONDS);

    /// <summary>
    /// The background probe that decides whether the dependency is healthy again. Default <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Return <c>true</c> to bring the pool back into service — the pool then closes the breaker and raises
    /// <see cref="HayatePoolOptions.OnAvailable"/>; return <c>false</c> to stay unavailable and be probed
    /// again after the next <see cref="ProbeInterval"/>. The delegate runs on the pool's background timer,
    /// never on a borrow or return path, and an exception it throws is caught and logged as a failed probe.<br />
    /// When this is <c>null</c> no automatic recovery happens: the pool stays unavailable until the
    /// application calls <c>SetAvailable</c>, which is the right choice when only the caller can know that
    /// the dependency is back.
    /// </remarks>
    public Func<bool>? Probe { get; set; }

    /// <summary>
    /// Number of consecutive successful probes required before the open breaker closes again.<br />
    /// Default value: <see cref="HayateConstant.DEFAULT_CIRCUIT_BREAKER_SUCCESS_THRESHOLD"/> (1).
    /// </summary>
    /// <remarks>
    /// Purpose: one healthy probe proves the dependency answered once, not that it recovered. Raising this
    /// makes the half-open state demand a run of healthy answers, at the cost of keeping the pool out of
    /// service for up to <c>SuccessThreshold × ProbeInterval</c> after the dependency comes back.<br />
    /// Interaction: only the background probe is counted. <c>SetAvailable</c> is the application stating
    /// that the dependency is healthy, so it closes the breaker immediately whatever this value is. A probe
    /// that reports unhealthy, throws, or is not configured breaks the run, and the next healthy probe
    /// starts a fresh one.<br />
    /// Boundary: values below 1 are normalized to 1 by <see cref="HayatePoolOptions.ApplyFeatureSwitches"/>.
    /// </remarks>
    public int SuccessThreshold { get; set; } = HayateConstant.DEFAULT_CIRCUIT_BREAKER_SUCCESS_THRESHOLD;

    /// <summary>
    /// Creates a new <see cref="HayateCircuitBreakerOptions"/> and copies all current settings into it.
    /// </summary>
    /// <returns>A new options instance with the same settings as this one.</returns>
    /// <example>
    /// <code>
    /// var copy = options.CircuitBreaker.CopyTo();
    /// </code>
    /// </example>
    public HayateCircuitBreakerOptions CopyTo()
    {
        return CopyTo(new HayateCircuitBreakerOptions());
    }

    /// <summary>
    /// Copies all current settings into the supplied <paramref name="options"/> instance.
    /// </summary>
    /// <param name="options">The target options instance to copy values into.</param>
    /// <returns>The same <paramref name="options"/> instance, updated with the current settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <example>
    /// <code>
    /// var target = new HayateCircuitBreakerOptions();
    /// options.CircuitBreaker.CopyTo(target);
    /// </code>
    /// </example>
    public HayateCircuitBreakerOptions CopyTo(HayateCircuitBreakerOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options), "Target circuit-breaker options instance cannot be null");
        }

        options.FailureThreshold = this.FailureThreshold;
        options.FailurePercentageThreshold = this.FailurePercentageThreshold;
        options.MinimumThroughput = this.MinimumThroughput;
        options.SuccessThreshold = this.SuccessThreshold;
        options.ResetTimeout = this.ResetTimeout;
        options.ProbeInterval = this.ProbeInterval;
        options.Probe = this.Probe;

        return options;
    }

    /// <summary>
    /// Determines whether the supplied options are structurally equal to this instance.
    /// </summary>
    /// <param name="other">The instance to compare with.</param>
    /// <returns><c>true</c> when every setting matches; the <see cref="Probe"/> delegates are compared by
    /// reference. <c>false</c> when <paramref name="other"/> is <c>null</c> or any setting differs.</returns>
    /// <remarks>
    /// Value equality keeps the options comparable as plain data — a deep copy produced by
    /// <see cref="CopyTo()"/> equals its source — which is also what makes the reflection-based
    /// configuration-equivalence tests work when the breaker settings are nested inside
    /// <see cref="HayatePoolOptions"/>.
    /// </remarks>
    public bool Equals(HayateCircuitBreakerOptions? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return this.FailureThreshold == other.FailureThreshold
               && this.FailurePercentageThreshold == other.FailurePercentageThreshold
               && this.MinimumThroughput == other.MinimumThroughput
               && this.SuccessThreshold == other.SuccessThreshold
               && this.ResetTimeout == other.ResetTimeout
               && this.ProbeInterval == other.ProbeInterval
               && ReferenceEquals(this.Probe, other.Probe);
    }

    /// <summary>Determines whether the supplied object is a structurally equal options instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> when <paramref name="obj"/> is a <see cref="HayateCircuitBreakerOptions"/> with the same settings.</returns>
    public override bool Equals(object? obj)
    {
        return obj is HayateCircuitBreakerOptions other && this.Equals(other);
    }

    /// <summary>Returns a hash code combining all settings.</summary>
    /// <returns>A hash code consistent with <see cref="Equals(HayateCircuitBreakerOptions)"/>; the probe delegate contributes its own hash (0 when <c>null</c>).</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = this.FailureThreshold;
            hash = (hash * 397) ^ this.FailurePercentageThreshold.GetHashCode();
            hash = (hash * 397) ^ this.MinimumThroughput;
            hash = (hash * 397) ^ this.SuccessThreshold;
            hash = (hash * 397) ^ this.ResetTimeout.GetHashCode();
            hash = (hash * 397) ^ this.ProbeInterval.GetHashCode();
            hash = (hash * 397) ^ (this.Probe?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// Event arguments for the pool availability callbacks
/// (<see cref="HayatePoolOptions.OnAvailable"/> and <see cref="HayatePoolOptions.OnUnavailable"/>).
/// </summary>
public sealed class HayatePoolAvailabilityEventArgs
{
    /// <summary>The name of the pool whose availability changed.</summary>
    public string PoolName { get; }

    /// <summary>
    /// The reason reported with the failure, when there is one. It is the value passed to
    /// <c>SetUnavailable</c> that tripped the breaker, or the message of the exception thrown by a probe;
    /// it is <c>null</c> on a recovery, and when the breaker was tripped without a reason.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HayatePoolAvailabilityEventArgs"/> class.
    /// </summary>
    /// <param name="poolName">The name of the pool whose availability changed.</param>
    /// <param name="reason">The reported failure reason, or <c>null</c> when there is none.</param>
    public HayatePoolAvailabilityEventArgs(string poolName, string reason)
    {
        PoolName = poolName;
        Reason = reason;
    }

    /// <summary>
    /// Returns a human-readable description of the availability event.
    /// </summary>
    /// <returns>A formatted description of the pool and the reported reason.</returns>
    public override string ToString()
    {
        return Reason is null ? $"[{PoolName}] availability changed" : $"[{PoolName}] availability changed: {Reason}";
    }
}

/// <summary>
/// The exception thrown by <c>Acquire</c> / <c>AcquireAsync</c> while the pool-level circuit breaker is
/// open (<see cref="HayatePoolOptions.EnableCircuitBreaker"/>), i.e. the pool has been taken out of
/// service because the dependency behind it keeps failing.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing catch blocks keep working, while code
/// that needs to tell "the pool is out of service" apart from an abort-policy rejection or a timeout can
/// catch this type specifically. Semantic counterpart of SAFEOP's <c>UnavailableException</c>.
/// </remarks>
public sealed class HayatePoolUnavailableException : InvalidOperationException
{
    /// <summary>The name of the pool that is currently unavailable.</summary>
    public string PoolName { get; }

    /// <summary>
    /// The reason reported with the failure that tripped the breaker, or <c>null</c> when the failure was
    /// reported without one. Matches <see cref="HayatePoolAvailabilityEventArgs.Reason"/>.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HayatePoolUnavailableException"/> class.
    /// </summary>
    /// <param name="poolName">The name of the pool that is unavailable.</param>
    /// <param name="reason">The reported failure reason, or <c>null</c> when there is none.</param>
    public HayatePoolUnavailableException(string poolName, string reason)
        : base(ComposeMessage(poolName, reason))
    {
        PoolName = poolName;
        Reason = reason;
    }

    private static string ComposeMessage(string poolName, string reason)
    {
        return reason is null
            ? $"HayatePool [{poolName}] is currently unavailable: the pool-level circuit breaker is open after repeated dependency failures. Wait for the availability probe to recover the pool, or call SetAvailable once the dependency is healthy."
            : $"HayatePool [{poolName}] is currently unavailable: the pool-level circuit breaker is open after repeated dependency failures ({reason}). Wait for the availability probe to recover the pool, or call SetAvailable once the dependency is healthy.";
    }
}
