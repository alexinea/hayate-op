using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNetCore.HayateOP.Policies;

namespace DotNetCore.HayateOP.Tests
{
    /// <summary>
    /// Acceptance for the generational optimization (<see cref="HayatePoolOptions.EnableGenerationOptimization"/>):
    /// an object older than <see cref="HayatePoolOptions.GenerationThresholdMs"/> is promoted to the old
    /// generation, and the old generation is validated once every
    /// <see cref="HayatePoolOptions.OldGenerationValidationInterval"/> borrows instead of on every one.
    /// </summary>
    /// <remarks>
    /// G-19: the promotion branch and the skip it feeds used to exist only on the synchronous borrow path,
    /// so an asynchronous pool promoted nothing and paid the full validation cost on every borrow. The
    /// cases below run the same script through both paths and require the same trace, which is what makes
    /// the two sites one rule rather than two.
    /// </remarks>
    public class GenerationOptimizationTests
    {
        private class TestObject { }

        /// <summary>The pooled type of the counting cases: the pool's behaviour, not the value, is under test.</summary>
        private sealed class CountingObject { }

        /// <summary>
        /// Counts the borrow-side validation calls the pool makes, so the two borrow paths can be compared
        /// on one script. Validation always succeeds — a rejected object would be destroyed and replaced,
        /// which would put a new young object into the trace.
        /// </summary>
        private sealed class CountingPolicy : IHayateObjectPolicy<CountingObject>
        {
            private int _validateCount;

            public int ValidateCount => Volatile.Read(ref _validateCount);

            public CountingObject Create() => new CountingObject();

            public bool OnRelease(CountingObject item) => true;

            public bool Validate(CountingObject item)
            {
                Interlocked.Increment(ref _validateCount);
                return true;
            }

            public void OnAcquire(CountingObject item) { }

            public void OnPassivate(CountingObject item) { }

            public void OnDestroy(CountingObject item) { }
        }

        /// <summary>
        /// A one-object pool whose promotion threshold is zero ticks, so any object older than its own
        /// creation instant is already old generation and the case needs no wait. Sharding is off because
        /// the <c>GenerationThresholdMs &gt;= 1000</c> floor is only enforced for a sharded pool, and
        /// <c>WithGenerationThreshold</c> refuses anything below it.
        /// </summary>
        private static IHayateObjectPool<CountingObject> CreateFastPromotionPool(
            CountingPolicy policy,
            int oldGenerationValidationInterval,
            bool generationOptimization = true)
        {
            var options = new HayatePoolOptions
            {
                MinPoolSize = 1,
                MaxPoolSize = 1,
                EnableSharding = false,
                EnableValidation = true,
                ValidateOnBorrow = true,
                EnableGenerationOptimization = generationOptimization,
                GenerationThresholdMs = 0,
                OldGenerationValidationInterval = oldGenerationValidationInterval,
                EnableLeakDetection = false,
            };

            return new HayatePoolBuilder<CountingObject>().WithPreset(options).WithPolicy(policy).Build();
        }

        /// <summary>A one-object pool on the configuration the builder documents: a 1000ms threshold.</summary>
        private static IHayateObjectPool<CountingObject> CreateThresholdPool(CountingPolicy policy)
        {
            return new HayatePoolBuilder<CountingObject>()
                .WithMinSize(1)
                .WithMaxSize(1)
                .WithEnableValidation(true)
                .WithValidateOnBorrow(true)
                .WithEnableGenerationOptimization(true)
                .WithGenerationThreshold(1000)
                .WithOldGenerationValidationInterval(3)
                .WithPolicy(policy)
                .Build();
        }

        /// <summary>Reads the generation marker of the pool's single object out of a snapshot.</summary>
        private static int GenerationOf(IHayateObjectPool<CountingObject> pool)
        {
            var details = pool.TakeSnapshot().ObjectDetails;
            Assert.Single(details);
            return details[0].Generation;
        }

        [Fact]
        public void EnableGenerationOptimization_ShouldWork()
        {
            using var pool = new HayatePoolBuilder<TestObject>()
                .WithEnableGenerationOptimization(true)
                .WithGenerationThreshold(1000)
                .WithOldGenerationValidationInterval(3)
                .Build();

            // Verify the pool works normally
            var obj = pool.Acquire();
            pool.Release(obj);
            Assert.NotNull(obj);
        }

        [Fact]
        public void DisableGenerationOptimization_ShouldTreatAllAsYoung()
        {
            using var pool = new HayatePoolBuilder<TestObject>()
                .WithEnableGenerationOptimization(false)
                .WithEnableValidation(true)
                .WithValidateOnBorrow(true)
                .Build();

            // Verify that every borrow is validated
            for (int i = 0; i < 10; i++)
            {
                var obj = pool.Acquire();
                pool.Release(obj);
            }
        }

        /// <summary>
        /// G-19: the same eight-borrow script through both paths must produce the same validation trace
        /// and the same generation marker. With an interval of 3 the old generation is validated on the
        /// first, fourth and seventh borrow after promotion — skip, skip, validate — which is the trace
        /// asserted below, so the case pins the rule itself and not merely agreement between the paths.
        /// </summary>
        [Fact]
        public async Task Acquire_And_AcquireAsync_Agree_OnPromotion_And_ValidationSkips()
        {
            var syncPolicy = new CountingPolicy();
            var asyncPolicy = new CountingPolicy();
            using var syncPool = CreateFastPromotionPool(syncPolicy, 3);
            using var asyncPool = CreateFastPromotionPool(asyncPolicy, 3);

            // Past the zero threshold, so the first borrow below promotes the object on its way out.
            await Task.Delay(20, CancellationToken.None);

            var syncTrace = new List<int>();
            var asyncTrace = new List<int>();
            for (var i = 0; i < 8; i++)
            {
                var sync = syncPool.Acquire();
                syncPool.Release(sync);
                syncTrace.Add(syncPolicy.ValidateCount);

                var async = await asyncPool.AcquireAsync(CancellationToken.None);
                asyncPool.Release(async);
                asyncTrace.Add(asyncPolicy.ValidateCount);
            }

            // 1 young borrow (validated), then the promoted object skipped twice and validated once.
            var expected = new[] { 1, 1, 1, 2, 2, 2, 3, 3 };
            Assert.Equal(expected, syncTrace);
            Assert.Equal(expected, asyncTrace);
            Assert.Equal(1, GenerationOf(syncPool));
            Assert.Equal(1, GenerationOf(asyncPool));
        }

        /// <summary>
        /// G-19: on the configuration the builder documents, an object younger than the threshold stays
        /// young on both paths, and the same object past it is promoted by both.
        /// </summary>
        [Fact]
        public async Task AcquireAsync_PromotesAnAgedObject_AfterTheConfiguredThreshold()
        {
            var syncPolicy = new CountingPolicy();
            var asyncPolicy = new CountingPolicy();
            using var syncPool = CreateThresholdPool(syncPolicy);
            using var asyncPool = CreateThresholdPool(asyncPolicy);

            var first = syncPool.Acquire();
            syncPool.Release(first);
            var firstAsync = await asyncPool.AcquireAsync(CancellationToken.None);
            asyncPool.Release(firstAsync);

            Assert.Equal(0, GenerationOf(syncPool));
            Assert.Equal(0, GenerationOf(asyncPool));

            // Past GenerationThresholdMs, so the next borrow sees an old object on both paths.
            await Task.Delay(1100, CancellationToken.None);

            var second = syncPool.Acquire();
            syncPool.Release(second);
            var secondAsync = await asyncPool.AcquireAsync(CancellationToken.None);
            asyncPool.Release(secondAsync);

            Assert.Equal(1, GenerationOf(syncPool));
            Assert.Equal(1, GenerationOf(asyncPool));
        }

        /// <summary>
        /// G-19: with the optimization switched off nothing is promoted and nothing is skipped — an aged
        /// object is still validated on every borrow, on both paths.
        /// </summary>
        [Fact]
        public async Task DisableGenerationOptimization_AcquireAsync_LeavesEveryObjectYoung()
        {
            var syncPolicy = new CountingPolicy();
            var asyncPolicy = new CountingPolicy();
            using var syncPool = CreateFastPromotionPool(syncPolicy, 3, generationOptimization: false);
            using var asyncPool = CreateFastPromotionPool(asyncPolicy, 3, generationOptimization: false);

            await Task.Delay(20, CancellationToken.None);

            for (var i = 0; i < 3; i++)
            {
                var sync = syncPool.Acquire();
                syncPool.Release(sync);

                var async = await asyncPool.AcquireAsync(CancellationToken.None);
                asyncPool.Release(async);
            }

            Assert.Equal(0, GenerationOf(syncPool));
            Assert.Equal(0, GenerationOf(asyncPool));
            Assert.Equal(3, syncPolicy.ValidateCount);
            Assert.Equal(3, asyncPolicy.ValidateCount);
        }
    }
}
