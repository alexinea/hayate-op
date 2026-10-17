using DotNetCore.HayateOP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HayateOP.Tests.Configuration
{
    /// <summary>
    /// G-18 (3.2): the breaker settings added in this window are settable from configuration like the ones
    /// that were already there. Binding a property is only half of the claim — the other half is that the
    /// bound value reaches the state machine — so the percentage case also makes the pool act on it.
    /// The success threshold is the exception: it only means anything with a probe delegate, and no
    /// configuration section can express one, so its binding is pinned here and its behaviour in
    /// <c>CircuitBreakerTests</c>.
    /// </summary>
    public class CircuitBreakerConfigurationTests
    {
        /// <summary>An item the pool creates on demand; nothing about it matters to the breaker.</summary>
        private sealed class Item { }

        /// <summary>
        /// A small single-shard pool that creates on demand and never retires anything, with the breaker
        /// switched on and <paramref name="breakerKeys"/> layered on top. The breaker keys go into the global
        /// section because that is the section the pool's merged options start from, and a pool section that
        /// mentions <c>CircuitBreaker</c> would replace the whole settings object rather than add to it.
        /// </summary>
        private static IServiceProvider Container(Dictionary<string, string?> breakerKeys)
        {
            var configData = new Dictionary<string, string?>
            {
                { "HayatePool:Global:ShardCount", "1" },
                { "HayatePool:Global:MinPoolSize", "0" },
                { "HayatePool:Global:MaxPoolSize", "2" },
                { "HayatePool:Global:EnableAutoScaling", "false" },
                { "HayatePool:Global:EnableEviction", "false" },
                { "HayatePool:Global:RejectPolicy", "CreateOnDemand" },
                { "HayatePool:Global:EnableCircuitBreaker", "true" }
            };

            foreach (var pair in breakerKeys) configData[pair.Key] = pair.Value;

            var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

            var services = new ServiceCollection();
            services.AddHayatePoolSupport()
                .RegisterGlobalConfig(config)
                .RegisterHayatePool<Item>(config);

            return services.BuildServiceProvider();
        }

        [Fact(Timeout = 30_000)]
        public void CircuitBreakerSettings_ShouldBindFromConfiguration_AndBeRunByThePool()
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            var provider = Container(new Dictionary<string, string?>
            {
                { "HayatePool:Global:CircuitBreaker:FailureThreshold", "100" },
                { "HayatePool:Global:CircuitBreaker:FailurePercentageThreshold", "50" },
                { "HayatePool:Global:CircuitBreaker:MinimumThroughput", "2" },
                { "HayatePool:Global:CircuitBreaker:SuccessThreshold", "3" }
            });

            var pool = provider.GetRequiredService<IHayateObjectPool<Item>>();
            var breaker = pool.GetOptions().CircuitBreaker;

            // The section binds into the nested settings object, and the values survive both the merge and
            // the normalization that follows it.
            Assert.Equal(100, breaker.FailureThreshold);
            Assert.Equal(50d, breaker.FailurePercentageThreshold);
            Assert.Equal(2, breaker.MinimumThroughput);
            Assert.Equal(3, breaker.SuccessThreshold);

            // ...and the pool runs the bound ratio rather than merely reporting it: two failures out of three
            // reports is 66% against a 50% threshold, over a floor of two. A streak of 100 can never form
            // here, so the trip can only come from the percentage that was read out of the configuration.
            pool.SetUnavailable("failure 1");
            pool.SetAvailable();
            Assert.True(pool.CheckAvailable());

            pool.SetUnavailable("failure 2");
            Assert.False(pool.CheckAvailable());
        }

        [Fact(Timeout = 30_000)]
        public void CircuitBreakerSettings_ShouldKeepTheirDefaults_WhenTheSectionIsSilent()
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            var provider = Container(new Dictionary<string, string?>());

            var pool = provider.GetRequiredService<IHayateObjectPool<Item>>();
            var breaker = pool.GetOptions().CircuitBreaker;

            // The three settings this window added default to the behaviour that predates them: no percentage
            // rule, no floor, and recovery on the first healthy probe.
            Assert.True(pool.GetOptions().EnableCircuitBreaker);
            Assert.Equal(3, breaker.FailureThreshold);
            Assert.Equal(0d, breaker.FailurePercentageThreshold);
            Assert.Equal(0, breaker.MinimumThroughput);
            Assert.Equal(1, breaker.SuccessThreshold);
        }
    }
}
