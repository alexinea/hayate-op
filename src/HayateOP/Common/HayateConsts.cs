// ReSharper disable InconsistentNaming
namespace DotNetCore.HayateOP.Common;

/// <summary>
/// Default constant values for the Hayate object pool.
/// These constants provide the default configuration values used by <see cref="HayatePoolOptions"/>.
/// </summary>
public static class HayateConstant
{
    ///// <summary>
    ///// Default maximum number of concurrent borrows (corresponds to <see cref="HayatePoolOptions.MaxConcurrent"/>).
    ///// </summary>
    //public const int DEFAULT_MAX_CONCURRENT = 32;

    /// <summary>
    /// Default minimum pool capacity (corresponds to <see cref="HayatePoolOptions.MinPoolSize"/>).
    /// </summary>
    public const int DEFAULT_MIN_POOL_SIZE = 5;

    /// <summary>
    /// Default maximum pool capacity (corresponds to <see cref="HayatePoolOptions.MaxPoolSize"/>).
    /// </summary>
    public const int DEFAULT_MAX_POOL_SIZE = 50;

    #region Timeout

    /*
     * Timeout
     */

    /// <summary>
    /// Default acquire timeout, in seconds (corresponds to <see cref="HayatePoolOptions.DefaultAcquireTimeout"/>).
    /// </summary>
    public const int DEFAULT_ACQUIRE_TIMEOUT_SECONDS = 5;

    #endregion

    #region Creation Retry

    /// <summary>
    /// Default number of creation-retry attempts on failure (corresponds to <see cref="HayatePoolOptions.CreationRetryCount"/>).
    /// </summary>
    public const int DEFAULT_CREATION_RETRY_COUNT = 3;

    /// <summary>
    /// Default creation-retry delay, in milliseconds (recommended for <see cref="HayatePoolOptions.CreationRetryDelay"/>).
    /// </summary>
    public const int DEFAULT_CREATION_RETRY_DELAY_MILLISECONDS = 100;

    #endregion

    #region Sharding Strategy

    /// <summary>
    /// Default shard count (corresponds to <see cref="HayatePoolOptions.ShardCount"/>).
    /// </summary>
    public const int DEFAULT_SHARD_COUNT = 4;

    #endregion

    #region Scaling Strategy

    /// <summary>
    /// Default scaling-check interval, in milliseconds (corresponds to <see cref="HayatePoolOptions.ScalingIntervalMs"/>).
    /// </summary>
    public const int DEFAULT_SCALING_INTERVAL_MILLISECONDS = 5000;

    /// <summary>
    /// Default scale-up threshold, range 0~1 (corresponds to <see cref="HayatePoolOptions.ScaleUpThreshold"/>).
    /// </summary>
    public const double DEFAULT_SCALE_UP_THRESHOLD = 0.8;

    /// <summary>
    /// Default scale-down threshold, range 0~1 (corresponds to <see cref="HayatePoolOptions.ScaleDownThreshold"/>).
    /// </summary>
    public const double DEFAULT_SCALE_DOWN_THRESHOLD = 0.2;

    public const int DEFAULT_SCALE_UP_COOLDOWN_SECONDS = 3;

    public const int DEFAULT_SCALE_DOWN_COOLDOWN_SECONDS = 15;

    public const int DEFAULT_SCALE_UP_STEP = 5;

    /// <summary>
    /// Default scale-down step (number of objects removed per scale-down). Defaults to the same value as
    /// <see cref="DEFAULT_SCALE_UP_STEP"/>; callers may lower it to make scale-down more conservative.
    /// </summary>
    public const int DEFAULT_SCALE_DOWN_STEP = 5;

    #endregion

    #region Object Validation

    /// <summary>
    /// Default validation-scan interval, in milliseconds (corresponds to <see cref="HayatePoolOptions.ValidateIntervalMs"/>).
    /// </summary>
    public const int DEFAULT_VALIDATE_INTERVAL_MILLISECONDS = 30000;

    #endregion

    #region Eviction Policy

    /// <summary>
    /// Default maximum object lifetime, in minutes (corresponds to <see cref="HayatePoolOptions.MaxLifeTime"/>).
    /// </summary>
    public const int DEFAULT_MAX_LIFE_TIME_MINUTES = 10;

    /// <summary>
    /// Default maximum object idle time, in minutes (corresponds to <see cref="HayatePoolOptions.MaxIdleTime"/>).
    /// </summary>
    public const int DEFAULT_MAX_IDLE_TIME_MINUTES = 5;

    /// <summary>
    /// Default soft-minimum evictable idle time, in minutes (corresponds to <see cref="HayatePoolOptions.SoftMinEvictableIdleTime"/>).
    /// </summary>
    public const int DEFAULT_MIN_EVICTION_IDLE_TIME_MINUTES = 2;

    /// <summary>
    /// Default eviction-check interval, in milliseconds (corresponds to <see cref="HayatePoolOptions.EvictionIntervalMs"/>).
    /// </summary>
    public const int DEFAULT_EVICTION_INTERVAL_MILLISECONDS = 30000;

    /// <summary>
    /// Default number of samples tested per eviction run (corresponds to <see cref="HayatePoolOptions.NumTestsPerEvictionRun"/>).
    /// </summary>
    public const int DEFAULT_EVICTION_RUNS_PER_EVICTION = 10;

    #endregion

    #region Generational Policy

    /// <summary>
    /// Default generation-promotion threshold, in milliseconds (corresponds to <see cref="HayatePoolOptions.GenerationThresholdMs"/>).
    /// </summary>
    public const int DEFAULT_GEN_THRESHOLD_MILLISECONDS = 30000;

    /// <summary>
    /// Default old-generation validation interval, in occurrences (corresponds to <see cref="HayatePoolOptions.OldGenerationValidationInterval"/>).
    /// </summary>
    public const int DEFAULT_OLD_GEN_VALIDATION_INTERVAL = 3;

    #endregion

    #region Leak Detection

    /// <summary>
    /// Default leak-detection threshold constant, in seconds (used by <see cref="HayatePoolOptions.LeakDetectionThreshold"/>).
    /// </summary>
    public const int DEFAULT_LEAK_DETECTION_THRESHOLD_SECONDS = 30;

    /// <summary>
    /// The default leak-trace sampling denominator (used by <see cref="HayatePoolOptions.LeakTraceSampleRate"/>; only effective in Sampled mode).
    /// </summary>
    public const int DEFAULT_LEAK_TRACE_SAMPLE_RATE = 1024;

    #endregion

    #region Abandoned Recovery

    /// <summary>
    /// Default abandoned-object judgment timeout, in seconds (used by
    /// <see cref="HayatePoolOptions.RemoveAbandonedTimeout"/>; aligned with CHOPIN's
    /// <c>AbandonedConfig.RemoveAbandonedTimeout</c> default of 300).
    /// </summary>
    public const int DEFAULT_REMOVE_ABANDONED_TIMEOUT_SECONDS = 300;

    /// <summary>
    /// Default background maintenance cadence for abandoned-object recovery, in milliseconds
    /// (used by <see cref="HayatePoolOptions.RemoveAbandonedIntervalMs"/> when
    /// <see cref="HayatePoolOptions.RemoveAbandonedOnMaintenance"/> is on).
    /// </summary>
    public const int DEFAULT_REMOVE_ABANDONED_INTERVAL_MILLISECONDS = 30000;

    #endregion

    #region Circuit Breaker

    /// <summary>
    /// Default number of consecutive reported failures before the pool-level circuit breaker trips
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.FailureThreshold"/>).
    /// </summary>
    public const int DEFAULT_CIRCUIT_BREAKER_FAILURE_THRESHOLD = 3;

    /// <summary>
    /// Default wait before the background probe starts, in seconds
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.ResetTimeout"/>).
    /// </summary>
    public const int DEFAULT_CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS = 30;

    /// <summary>
    /// Default interval between background probes, in seconds
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.ProbeInterval"/>).
    /// </summary>
    public const int DEFAULT_CIRCUIT_BREAKER_PROBE_INTERVAL_SECONDS = 5;

    /// <summary>
    /// Default number of consecutive successful probes the open breaker requires before it closes
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.SuccessThreshold"/>).
    /// </summary>
    public const int DEFAULT_CIRCUIT_BREAKER_SUCCESS_THRESHOLD = 1;

    /// <summary>
    /// Default failure percentage, in percent, at which the breaker trips; 0 disables the percentage rule
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.FailurePercentageThreshold"/>).
    /// </summary>
    public const double DEFAULT_CIRCUIT_BREAKER_FAILURE_PERCENTAGE_THRESHOLD = 0d;

    /// <summary>
    /// Default minimum number of reports before the failure percentage is evaluated
    /// (corresponds to <see cref="HayateCircuitBreakerOptions.MinimumThroughput"/>).
    /// </summary>
    public const int DEFAULT_CIRCUIT_BREAKER_MINIMUM_THROUGHPUT = 0;

    #endregion
}