using System;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-8 item 6 and NFR-1 (TSK-001, TSK-003): the daily cache of the two channels (<c>DailyValueCache</c>, which
    /// <c>ShockChannels</c> wires to <c>ModTicks.Today</c>, <c>ModModifiers.HasAny</c> and <c>ModModifiers.Get</c>): one reading of the
    /// channel per game day, a new reading after <c>Invalidate</c> and after the report <c>ChannelChanged</c> of the cache's own channel, the
    /// fast path without a reading, and the safe answers (an unknown day is never stored, an unusable reading is 1).
    /// </summary>
    [Covers(typeof(DailyValueCache))]
    public class ChannelCacheTests
    {
        private const string Memory = "production.memoryCost";
        private const string Wafer = "production.waferCost";

        /// <summary>The world the cache looks at: the day, whether something is registered, and what the channel says.</summary>
        private sealed class World
        {
            public long Day = 100;
            public bool HasAny = true;
            public float Reading = 1.6f;
            public int HasAnyCalls;

            public DailyValueCache Cache(string channel = Memory)
            {
                return new DailyValueCache(channel, () => Day, () =>
                {
                    HasAnyCalls++;
                    return HasAny;
                }, () => Reading);
            }
        }

        // ---- one reading a day ----

        [Test]
        public void Value_ManyCallsInOneDay_ReadTheChannelOnce()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();

            for (int i = 0; i < 1000; i++) Assert.Equal(1.6f, cache.Value);

            Assert.Equal(1, cache.Reads, "ModModifiers.Get once a day (NFR-1)");
            Assert.Equal(1, cache.Refreshes);
        }

        [Test]
        public void Value_NextDay_ReadsAgain()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.Day = 101;
            world.Reading = 1.4f;
            Assert.Equal(1.4f, cache.Value);
            Assert.Equal(1.4f, cache.Value);

            Assert.Equal(2, cache.Reads);
        }

        [Test]
        public void Value_ChangeOfTheChannelInTheMiddleOfADay_IsNotSeenWithoutAReport()
        {
            // The cache is the day's value (FR-8 item 6): only the reports below drop it before the day ends.
            var world = new World();
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.Reading = 1.2f;
            Assert.Equal(1.6f, cache.Value);
            Assert.Equal(1, cache.Reads);
        }

        [Test]
        public void Value_ADayThatComesBack_ReadsAgain()
        {
            // A save loaded on an earlier date: the day differs from the stored one, so it is read anew even without GameReady.
            var world = new World { Day = 500 };
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.Day = 400;
            world.Reading = 1.1f;
            Assert.Equal(1.1f, cache.Value);
        }

        // ---- Invalidate and ChannelChanged ----

        [Test]
        public void Invalidate_ReadsAgainInTheSameDay()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.Reading = 1.3f;
            cache.Invalidate();
            Assert.Equal(1.3f, cache.Value);
            Assert.Equal(1.3f, cache.Value);

            Assert.Equal(2, cache.Reads);
        }

        [Test]
        public void Invalidate_BeforeTheFirstRead_IsHarmless()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();
            cache.Invalidate();
            cache.Invalidate();
            Assert.Equal(1.6f, cache.Value);
            Assert.Equal(1, cache.Reads);
        }

        [Test]
        public void OnChannelChanged_OwnChannel_ReadsAgain()
        {
            var world = new World();
            DailyValueCache cache = world.Cache(Memory);
            Assert.Equal(1.6f, cache.Value);

            world.Reading = 1.9f;
            cache.OnChannelChanged(Memory);
            Assert.Equal(1.9f, cache.Value);
            Assert.Equal(2, cache.Reads);
        }

        [Test]
        public void OnChannelChanged_OtherChannel_KeepsTheValue()
        {
            var world = new World();
            DailyValueCache cache = world.Cache(Memory);
            Assert.Equal(1.6f, cache.Value);

            world.Reading = 1.9f;
            cache.OnChannelChanged(Wafer);
            cache.OnChannelChanged("production.output");
            cache.OnChannelChanged("PRODUCTION.MEMORYCOST");   // names are exact
            cache.OnChannelChanged(string.Empty);
            Assert.Equal(1.6f, cache.Value);
            Assert.Equal(1, cache.Reads);
        }

        [Test]
        public void OnChannelChanged_NoChannelNamed_ReadsAgain()
        {
            // ModModifiers.Invalidate() without a name: everything may have changed.
            var world = new World();
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.Reading = 1.5f;
            cache.OnChannelChanged(null);
            Assert.Equal(1.5f, cache.Value);
        }

        [Test]
        public void TwoCaches_AreIndependent()
        {
            var memoryWorld = new World { Reading = 1.6f };
            var waferWorld = new World { Reading = 0.8f };
            DailyValueCache memory = memoryWorld.Cache(Memory);
            DailyValueCache wafer = waferWorld.Cache(Wafer);
            Assert.Equal(1.6f, memory.Value);
            Assert.Equal(0.8f, wafer.Value);

            memoryWorld.Reading = 1.2f;
            waferWorld.Reading = 0.7f;
            memory.OnChannelChanged(Memory);   // the report of the memory channel reaches both caches, as ShockChannels does
            wafer.OnChannelChanged(Memory);
            Assert.Equal(1.2f, memory.Value);
            Assert.Equal(0.8f, wafer.Value, "the wafer cache did not hear its own channel");

            wafer.OnChannelChanged(Wafer);
            Assert.Equal(0.7f, wafer.Value);
        }

        // ---- the fast path ----

        [Test]
        public void Value_NothingRegistered_IsOne_WithoutReadingTheChannel()
        {
            var world = new World { HasAny = false, Reading = 99f };
            DailyValueCache cache = world.Cache();

            for (int day = 100; day < 130; day++)
            {
                world.Day = day;
                Assert.Equal(1f, cache.Value);
            }

            Assert.Equal(0, cache.Reads, "HasAny is false: ModModifiers.Get is never called");
        }

        [Test]
        public void Value_ChecksHasAnyOncePerDay_NotPerCall()
        {
            var world = new World { HasAny = false };
            DailyValueCache cache = world.Cache();
            for (int i = 0; i < 500; i++) Assert.Equal(1f, cache.Value);
            Assert.Equal(1, world.HasAnyCalls);
        }

        [Test]
        public void Value_SomethingRegisteredLater_IsSeenAfterAReport()
        {
            // A contribution appears in the middle of a day: the core reports ChannelChanged and the next question reads the channel.
            var world = new World { HasAny = false };
            DailyValueCache cache = world.Cache();
            Assert.Equal(1f, cache.Value);

            world.HasAny = true;
            Assert.Equal(1f, cache.Value, "still the day's value");
            cache.OnChannelChanged(Memory);
            Assert.Equal(1.6f, cache.Value);
            Assert.Equal(1, cache.Reads);
        }

        [Test]
        public void Value_TheShockEnds_FallsBackToOne()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();
            Assert.Equal(1.6f, cache.Value);

            world.HasAny = false;       // the last contribution is gone
            cache.Invalidate();
            Assert.Equal(1f, cache.Value);
        }

        // ---- safe answers ----

        [Test]
        public void Value_UnknownDay_IsNeverStored()
        {
            var world = new World { Day = -1 };
            DailyValueCache cache = world.Cache();

            Assert.Equal(1.6f, cache.Value);
            world.Reading = 1.2f;
            Assert.Equal(1.2f, cache.Value, "no day, no cache");
            Assert.Equal(2, cache.Reads);

            world.Day = 100;
            Assert.Equal(1.2f, cache.Value);
            world.Reading = 1.7f;
            Assert.Equal(1.2f, cache.Value, "a known day is stored again");

            world.Day = -1;
            Assert.Equal(1.7f, cache.Value, "the day went unknown: not the stored value");
        }

        [Test]
        public void Value_UnusableReading_IsOne()
        {
            var world = new World();
            DailyValueCache cache = world.Cache();

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f })
            {
                world.Reading = bad;
                cache.Invalidate();
                Assert.Equal(1f, cache.Value, "reading " + bad);
            }

            world.Reading = 0f;
            cache.Invalidate();
            Assert.Equal(0f, cache.Value, "0 is a number the channel's fuse allows to a caller; the formulas decide");
        }

        [Test]
        public void Value_ASecondCache_DoesNotShareTheDay()
        {
            var world = new World();
            DailyValueCache a = world.Cache(Memory);
            DailyValueCache b = world.Cache(Wafer);
            Assert.Equal(1.6f, a.Value);
            Assert.Equal(1.6f, b.Value);
            Assert.Equal(1, a.Reads);
            Assert.Equal(1, b.Reads);
        }

        [Test]
        public void Channel_IsTheName_GivenAtConstruction()
        {
            var world = new World();
            Assert.Equal(Wafer, world.Cache(Wafer).Channel);
        }

        [Test]
        public void Constructor_RejectsMissingFunctions()
        {
            Assert.Throws<ArgumentNullException>(() => new DailyValueCache(Memory, null, () => true, () => 1f));
            Assert.Throws<ArgumentNullException>(() => new DailyValueCache(Memory, () => 1L, null, () => 1f));
            Assert.Throws<ArgumentNullException>(() => new DailyValueCache(Memory, () => 1L, () => true, null));
        }

        [Test]
        public void Value_ExceptionOfTheReading_ReachesTheCaller_AndTheCacheStaysUsable()
        {
            // ShockChannels catches it and answers 1 (and reports the fault); the cache itself stores nothing from a failed reading.
            bool fail = true;
            var cache = new DailyValueCache(Memory, () => 100L, () => true, () =>
            {
                if (fail) throw new InvalidOperationException("provider failed");
                return 1.5f;
            });

            Assert.Throws<InvalidOperationException>(() => { float ignored = cache.Value; });
            fail = false;
            Assert.Equal(1.5f, cache.Value, "the next question reads again");
        }
    }
}
