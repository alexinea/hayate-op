namespace DotNetCore.HayateOP.Tests;

/// <summary>
/// Pool-level availability circuit breaker (EnableCircuitBreaker, default off).
/// Acceptance: consecutive dependency failure reports trip the breaker and the whole pool fails fast;
/// a successful background probe recovers the pool and fires OnAvailable exactly once; without a probe
/// the pool stays unavailable until SetAvailable; the failure streak survives borrows and is cleared by
/// recovery, so sporadic failures separated by a recovery count from scratch; the lean profile forces the
/// feature off; the options deep-copy contract keeps mutating a copy from reconfiguring the source.
/// G-18 (3.2) widens the two rules the breaker runs on: the trip rule gains a failure percentage over a
/// sampled window with a minimum throughput, and the recovery rule gains a run of consecutive healthy
/// probes. Every one of them defaults to the behaviour that existed before the setting did.
/// </summary>
public class CircuitBreakerTests
{
    private class TestObject { }

    private static HayatePoolBuilder<TestObject> BaseBuilder() => new HayatePoolBuilder<TestObject>()
        .WithPoolName("cb-pool")
        .WithMinSize(2)
        .WithMaxSize(4);

    [Fact(Timeout = 30_000)]
    public void DisabledBreaker_ShouldIgnoreFailureReports_AndAlwaysBeAvailable()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder().Build();   // EnableCircuitBreaker defaults to false

        for (var i = 0; i < 5; i++) pool.SetUnavailable("dependency down");

        Assert.True(pool.CheckAvailable());

        var item = pool.Acquire();
        Assert.NotNull(item);
        pool.Release(item);
    }

    [Fact(Timeout = 30_000)]
    public void ThresholdConsecutiveFailures_ShouldTrip_AndFailFastTheWholePool()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var unavailableCount = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
            .WithOnUnavailable(_ => Interlocked.Increment(ref unavailableCount[0]))
            .Build();

        // The pool serves normally before the trip.
        var pre = pool.Acquire();
        pool.Release(pre);
        var acquiredBeforeTrip = pool.GetStats().TotalAcquired;

        Assert.True(pool.CheckAvailable());

        // Two reports below the threshold change nothing.
        pool.SetUnavailable("connection failed 1");
        pool.SetUnavailable("connection failed 2");
        Assert.True(pool.CheckAvailable());

        // The third consecutive report trips the breaker exactly once.
        pool.SetUnavailable("connection failed 3");
        Assert.Equal(1, Volatile.Read(ref unavailableCount[0]));
        Assert.False(pool.CheckAvailable());

        // Fail-fast: the borrow throws before doing any pool work — even though idle objects exist,
        // nothing is handed out and the borrow counter does not advance.
        var exception = Assert.Throws<HayatePoolUnavailableException>(() => pool.Acquire());
        Assert.Equal("cb-pool", exception.PoolName);
        Assert.Equal("connection failed 3", exception.Reason);
        Assert.Equal(acquiredBeforeTrip, pool.GetStats().TotalAcquired);

        // Further reports while open are no-ops: no second announcement.
        pool.SetUnavailable("connection failed 4");
        Assert.Equal(1, Volatile.Read(ref unavailableCount[0]));
        Assert.False(pool.CheckAvailable());
    }

    [Fact(Timeout = 30_000)]
    public void SuccessfulProbe_ShouldRecover_AndFireOnAvailableOnce()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var unavailableCount = new int[1];
        var availableCount = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(1, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(100), () => true)
            .WithOnUnavailable(_ => Interlocked.Increment(ref unavailableCount[0]))
            .WithOnAvailable(_ => Interlocked.Increment(ref availableCount[0]))
            .Build();

        pool.SetUnavailable("dependency down");
        Assert.False(pool.CheckAvailable());
        Assert.Equal(1, Volatile.Read(ref unavailableCount[0]));

        // First probe runs one reset-timeout after the trip, then one per probe interval; poll with a
        // generous deadline so a slow CI machine cannot flake the case.
        var recovered = false;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (pool.CheckAvailable()) { recovered = true; break; }
            Thread.Sleep(25);
        }

        Assert.True(recovered);
        Assert.Equal(1, Volatile.Read(ref availableCount[0]));

        // The pool serves again and the callback carried no failure reason.
        var item = pool.Acquire();
        Assert.NotNull(item);
        pool.Release(item);
    }

    [Fact(Timeout = 30_000)]
    public void FailingProbe_ShouldKeepThePoolUnavailable()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var availableCount = new int[1];
        var probeCalls = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(1, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80),
                () => { Interlocked.Increment(ref probeCalls[0]); return false; })
            .WithOnAvailable(_ => Interlocked.Increment(ref availableCount[0]))
            .Build();

        pool.SetUnavailable("dependency down");

        // Give the probe several intervals to run; it must never recover the pool.
        Thread.Sleep(600);

        Assert.False(pool.CheckAvailable());
        Assert.True(Volatile.Read(ref probeCalls[0]) >= 1);
        Assert.Equal(0, Volatile.Read(ref availableCount[0]));
    }

    [Fact(Timeout = 30_000)]
    public void NoProbe_ShouldStayUnavailable_UntilSetAvailable()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var availableCount = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
            .WithOnAvailable(_ => Interlocked.Increment(ref availableCount[0]))
            .Build();

        pool.SetUnavailable("dependency down");
        Assert.False(pool.CheckAvailable());

        // The reset timeout elapses but no probe is configured, so nothing recovers the pool.
        Thread.Sleep(250);
        Assert.False(pool.CheckAvailable());

        // Manual recovery closes the breaker exactly once; SetAvailable on an available pool raises nothing.
        pool.SetAvailable();
        Assert.True(pool.CheckAvailable());
        Assert.Equal(1, Volatile.Read(ref availableCount[0]));

        pool.SetAvailable();
        Assert.Equal(1, Volatile.Read(ref availableCount[0]));
    }

    [Fact(Timeout = 30_000)]
    public void Streak_ShouldBeClearedByRecovery_AndBySetAvailableOnAnAvailablePool()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder()
            .WithCircuitBreaker(3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
            .Build();

        // Two failures, then an explicit recovery report: the streak resets, so two more failures still
        // do not trip — the breaker needs three *consecutive* reports.
        pool.SetUnavailable("transient 1");
        pool.SetUnavailable("transient 2");
        pool.SetAvailable();
        pool.SetUnavailable("transient 3");
        pool.SetUnavailable("transient 4");
        Assert.True(pool.CheckAvailable());

        // SetAvailable on an available pool also clears a pending streak.
        pool.SetAvailable();
        pool.SetUnavailable("transient 5");
        pool.SetUnavailable("transient 6");
        Assert.True(pool.CheckAvailable());

        // The third consecutive report (without an intervening recovery) finally trips.
        pool.SetUnavailable("transient 7");
        Assert.False(pool.CheckAvailable());
    }

    [Fact(Timeout = 30_000)]
    public void LeanProfile_ShouldForceTheBreakerOff()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = new HayatePoolBuilder<TestObject>()
            .WithMinSize(0)
            .WithLeanProfile()
            .WithEnableCircuitBreaker()          // even after the profile: lean is a mode, not a knob
            .Build();

        Assert.False(pool.GetOptions().EnableCircuitBreaker);

        pool.SetUnavailable("ignored in lean mode");
        Assert.True(pool.CheckAvailable());

        var item = pool.Acquire();
        Assert.NotNull(item);
        pool.Release(item);
    }

    [Fact(Timeout = 30_000)]
    public void OptionsCopyTo_ShouldDeepCopyTheBreakerSettings()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var probe = new Func<bool>(() => true);
        var original = new HayatePoolOptions
        {
            EnableCircuitBreaker = true,
            OnAvailable = _ => { },
            OnUnavailable = _ => { }
        };
        original.CircuitBreaker.FailureThreshold = 7;
        original.CircuitBreaker.ResetTimeout = TimeSpan.FromSeconds(11);
        original.CircuitBreaker.ProbeInterval = TimeSpan.FromSeconds(13);
        original.CircuitBreaker.Probe = probe;
        original.CircuitBreaker.FailurePercentageThreshold = 42.5d;
        original.CircuitBreaker.MinimumThroughput = 7;
        original.CircuitBreaker.SuccessThreshold = 2;

        var copy = original.CopyTo();

        // The settings object is copied, not shared, so mutating the copy cannot reconfigure the pool.
        Assert.False(ReferenceEquals(original.CircuitBreaker, copy.CircuitBreaker));
        Assert.Equal(original.CircuitBreaker, copy.CircuitBreaker);
        Assert.Same(probe, copy.CircuitBreaker.Probe);
        Assert.Equal(original.OnAvailable, copy.OnAvailable);

        // CopyTo and Equals both enumerate the settings by hand, so a setting missing from both would slip
        // past the equality check above; asserting each one directly is what catches that pair of omissions.
        Assert.Equal(42.5d, copy.CircuitBreaker.FailurePercentageThreshold);
        Assert.Equal(7, copy.CircuitBreaker.MinimumThroughput);
        Assert.Equal(2, copy.CircuitBreaker.SuccessThreshold);

        copy.CircuitBreaker.FailureThreshold = 99;
        copy.EnableCircuitBreaker = false;
        Assert.Equal(7, original.CircuitBreaker.FailureThreshold);
        Assert.True(original.EnableCircuitBreaker);

        // Normalization clamps out-of-range breaker settings to their defaults.
        var raw = new HayatePoolOptions { EnableCircuitBreaker = true };
        raw.CircuitBreaker.FailureThreshold = 0;
        raw.CircuitBreaker.ResetTimeout = TimeSpan.Zero;
        raw.CircuitBreaker.ProbeInterval = TimeSpan.FromSeconds(-1);
        raw.ApplyFeatureSwitches();
        Assert.Equal(3, raw.CircuitBreaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), raw.CircuitBreaker.ResetTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), raw.CircuitBreaker.ProbeInterval);

        // G-18: the three settings added in this window normalize on the same terms — a run below 1, a
        // negative floor and a percentage outside 0..100 all land on a value the breaker can run with. The
        // percentage is the one whose default means "off", so an out-of-range value has to land on 0 rather
        // than switch the rule on behind the caller's back.
        var outOfRange = new HayatePoolOptions { EnableCircuitBreaker = true };
        outOfRange.CircuitBreaker.SuccessThreshold = 0;
        outOfRange.CircuitBreaker.MinimumThroughput = -5;
        outOfRange.CircuitBreaker.FailurePercentageThreshold = -1d;
        outOfRange.ApplyFeatureSwitches();
        Assert.Equal(1, outOfRange.CircuitBreaker.SuccessThreshold);
        Assert.Equal(0, outOfRange.CircuitBreaker.MinimumThroughput);
        Assert.Equal(0d, outOfRange.CircuitBreaker.FailurePercentageThreshold);

        var tooHigh = new HayatePoolOptions { EnableCircuitBreaker = true };
        tooHigh.CircuitBreaker.FailurePercentageThreshold = 150d;
        tooHigh.ApplyFeatureSwitches();
        Assert.Equal(100d, tooHigh.CircuitBreaker.FailurePercentageThreshold);

        // NaN compares false against every bound, so a clamp written as a pair of comparisons would let it
        // through and then make the options unequal to their own copy.
        var notANumber = new HayatePoolOptions { EnableCircuitBreaker = true };
        notANumber.CircuitBreaker.FailurePercentageThreshold = double.NaN;
        notANumber.ApplyFeatureSwitches();
        Assert.Equal(0d, notANumber.CircuitBreaker.FailurePercentageThreshold);
        Assert.Equal(notANumber.CircuitBreaker, notANumber.CircuitBreaker.CopyTo());
    }

    /// <summary>
    /// G-18: the half-open breaker can be told to wait for a run of healthy probes instead of a single one.
    /// The probe counts its own calls, so "how many healthy answers it took" is an observable number rather
    /// than a race against the clock.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void SuccessThreshold_ShouldRequireThatManyConsecutiveHealthyProbes()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var probeCalls = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 1,
                ResetTimeout = TimeSpan.FromMilliseconds(50),
                ProbeInterval = TimeSpan.FromMilliseconds(150),
                SuccessThreshold = 3,
                Probe = () => { Interlocked.Increment(ref probeCalls[0]); return true; }
            })
            .Build();

        pool.SetUnavailable("dependency down");

        // Two healthy answers are not a run of three, so the pool is still out of service. The third probe
        // is a full interval away, which is what makes the assertion about state rather than about luck.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref probeCalls[0]) < 2 && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(Volatile.Read(ref probeCalls[0]) >= 2);
        Assert.False(pool.CheckAvailable());

        // The third consecutive answer closes the breaker, and the timer stops there: three is the exact
        // count, not a lower bound.
        while (!pool.CheckAvailable() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(pool.CheckAvailable());
        Assert.Equal(3, Volatile.Read(ref probeCalls[0]));
    }

    /// <summary>
    /// G-18: the default is 1, i.e. exactly the behaviour the breaker had before the setting existed — the
    /// first healthy probe recovers the pool. The counter is what pins it: a default that had quietly become
    /// 2 would still recover, only one interval later.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void SuccessThreshold_DefaultOfOne_ShouldRecoverOnTheFirstHealthyProbe()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var probeCalls = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 1,
                ResetTimeout = TimeSpan.FromMilliseconds(50),
                ProbeInterval = TimeSpan.FromMilliseconds(150),
                Probe = () => { Interlocked.Increment(ref probeCalls[0]); return true; }
            })
            .Build();

        Assert.Equal(1, pool.GetOptions().CircuitBreaker.SuccessThreshold);

        pool.SetUnavailable("dependency down");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!pool.CheckAvailable() && DateTime.UtcNow < deadline) Thread.Sleep(10);

        Assert.True(pool.CheckAvailable());
        Assert.Equal(1, Volatile.Read(ref probeCalls[0]));
    }

    /// <summary>
    /// G-18: an unhealthy answer breaks the half-open run, so the successes counted before it no longer add
    /// up to one. The probe is scripted healthy twice, unhealthy once, healthy afterwards, which puts
    /// recovery on the sixth answer (2 + 1 + 3); without the reset it would land on the fourth, and that
    /// difference is the whole point of the case.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void FailedProbe_ShouldBreakTheHalfOpenRun()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var probeCalls = new int[1];
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 1,
                ResetTimeout = TimeSpan.FromMilliseconds(50),
                ProbeInterval = TimeSpan.FromMilliseconds(60),
                SuccessThreshold = 3,
                Probe = () => Interlocked.Increment(ref probeCalls[0]) != 3
            })
            .Build();

        pool.SetUnavailable("dependency down");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!pool.CheckAvailable() && DateTime.UtcNow < deadline) Thread.Sleep(10);

        Assert.True(pool.CheckAvailable());
        Assert.Equal(6, Volatile.Read(ref probeCalls[0]));
    }

    /// <summary>
    /// G-18: the percentage rule exists for the dependency that fails one call in three — no streak ever
    /// forms, yet the pool keeps handing out objects that do not work. The consecutive threshold is set out
    /// of reach, so only the ratio can trip this pool.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void FailurePercentageThreshold_ShouldTripOnTheRatio_WhenNoStreakCanForm()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 100,
                FailurePercentageThreshold = 50d,
                MinimumThroughput = 4
            })
            .Build();

        // fail, success, fail, success: two failures out of four reports is exactly the threshold, but the
        // fourth report is a success, and a success is never a reason to take the pool out of service.
        pool.SetUnavailable("failure 1");
        pool.SetAvailable();
        pool.SetUnavailable("failure 2");
        pool.SetAvailable();
        Assert.True(pool.CheckAvailable());

        // The next failure puts the window at 3 of 5 = 60% and trips the breaker on that report.
        pool.SetUnavailable("failure 3");
        Assert.False(pool.CheckAvailable());
    }

    /// <summary>
    /// G-18: the floor keeps a cold pool from tripping on its first report — at any percentage a single
    /// failure out of a single report is 100%. Four failures out of four reports is far above the 10%
    /// threshold here, and still nothing happens until the sample reaches the floor.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void MinimumThroughput_ShouldHoldTheRatioBack_UntilTheSampleIsBigEnough()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 100,
                FailurePercentageThreshold = 10d,
                MinimumThroughput = 5
            })
            .Build();

        for (var i = 0; i < 4; i++) pool.SetUnavailable($"failure {i}");
        Assert.True(pool.CheckAvailable());

        // The fifth report brings the sample up to the floor, and the same 100% now counts.
        pool.SetUnavailable("failure 4");
        Assert.False(pool.CheckAvailable());
    }

    /// <summary>
    /// G-18: the sampled window belongs to the stretch between two recoveries, so a pool that just came back
    /// starts from nothing. Without that reset the failures that tripped the breaker would still be in the
    /// window and the very next one would take the pool out of service again — the recovery would be
    /// nominal, and every retry after it would look like a fresh outage.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void PercentageWindow_ShouldStartOver_AfterARecovery()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions
            {
                FailureThreshold = 100,
                FailurePercentageThreshold = 50d,
                MinimumThroughput = 4
            })
            .Build();

        // Three failures out of four reports is 75%, and the fourth report is what reaches the floor.
        pool.SetUnavailable("failure 1");
        pool.SetUnavailable("failure 2");
        pool.SetAvailable();
        pool.SetUnavailable("failure 3");
        Assert.False(pool.CheckAvailable());

        pool.SetAvailable();
        Assert.True(pool.CheckAvailable());

        // One failure out of one report is 100%, but the sample is back below the floor: the window did
        // start over, so the pool keeps serving.
        pool.SetUnavailable("failure after recovery");
        Assert.True(pool.CheckAvailable());
    }

    /// <summary>
    /// G-18: with the rule at its default the breaker keeps exactly the behaviour it had — an alternating
    /// pattern never builds a streak and never trips. Twenty rounds is far more than a percentage rule at any
    /// sane threshold would tolerate, so a default that had silently switched the rule on would fail here.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public void PercentageRule_DefaultOff_ShouldLeaveTheStreakAsTheOnlyTripRule()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var pool = BaseBuilder()
            .WithCircuitBreaker(new HayateCircuitBreakerOptions { FailureThreshold = 3 })
            .Build();

        Assert.Equal(0d, pool.GetOptions().CircuitBreaker.FailurePercentageThreshold);
        Assert.Equal(0, pool.GetOptions().CircuitBreaker.MinimumThroughput);

        for (var i = 0; i < 20; i++)
        {
            pool.SetUnavailable($"failure {i}");
            pool.SetAvailable();
        }

        Assert.True(pool.CheckAvailable());
    }
}
