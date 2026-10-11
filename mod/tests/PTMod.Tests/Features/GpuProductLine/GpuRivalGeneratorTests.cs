using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The generator of the cards of the vendors and of the background (TSK-018; F22, F23; FR-13 item 2, NFR-6) on the shipped content: the same seed gives the same
    /// cards, the parts are the newest of <c>techYear</c> (AC-13.2), the temperature stays at or under 85, video memory is a power of two within the memory's
    /// maximum, a vendor's price is at least 1.3 x the unit cost; and the stream wrapper over <c>ModRandom</c>.
    /// </summary>
    [Covers(typeof(GpuRivalGenerator), typeof(GpuRivalTech), typeof(GpuRivalDesign), typeof(IGpuRandom), typeof(GpuRandom))]
    public class GpuRivalGeneratorTests
    {
        private static GpuContentDb Db()
        {
            return GpuWorld.Shipped();
        }

        private static GpuSegmentDto Consumer()
        {
            GpuSegmentDto seg = Db().Segment("consumer");
            Assert.NotNull(seg, "the shipped segment 'consumer'");
            return seg;
        }

        /// <summary>The frontier of the industry by year, roughly the game's CPUs: 16 MHz in 1985, 250 MHz in 1997, 3.4 GHz in 2005, 5 GHz in 2022.</summary>
        private static float FrontierKHz(int year)
        {
            int[] years = { 1975, 1985, 1990, 1997, 2000, 2005, 2010, 2022, 2040 };
            float[] mhz = { 2f, 16f, 33f, 250f, 1000f, 3400f, 3800f, 5000f, 6000f };
            for (int i = 1; i < years.Length; i++)
            {
                if (year > years[i]) continue;
                float k = (year - years[i - 1]) / (float)(years[i] - years[i - 1]);
                return (mhz[i - 1] + (mhz[i] - mhz[i - 1]) * Math.Max(0f, k)) * 1000f;
            }
            return mhz[mhz.Length - 1] * 1000f;
        }

        /// <summary>The industry of a date with a tech lag: the node of the timeline, the frontier frequency, a 300 mm wafer at the foundry's yield.</summary>
        private static GpuRivalTech Tech(DateTime date, float lag)
        {
            GpuContentDb db = Db();
            var game = new FakeGpuGame().AddTimelineNodes();
            float techYear = GpuRivalGenerator.TechYear(date, lag);
            string name = db.NodeNameFor(techYear);
            NodeSpec node;
            Assert.True(game.TryGetNode(name, out node), "the fake game knows " + name);
            return new GpuRivalTech { TechYear = techYear, Node = node, MaxFreqKHz = FrontierKHz(date.Year), MaintenancePerLine = 14000f, WaferMaterial = 2000f };
        }

        private static IGpuRandom Rng(uint seed)
        {
            return new GpuRandom(new Xoshiro128(seed));
        }

        [Test]
        public void TechYear_IsTheYearAndItsMonthsMinusTheLag()
        {
            Assert.Near(2002.6667, GpuRivalGenerator.TechYear(new DateTime(2003, 3, 1), 0.5f), 1e-4, "AC-13.2: 1 March 2003, techLag 0.5");
            Assert.Near(1995.0, GpuRivalGenerator.TechYear(new DateTime(1997, 1, 20), 2f), 1e-6, "the background: two years of lag");
            Assert.Near(2010.9167, GpuRivalGenerator.TechYear(new DateTime(2010, 12, 31), 0f), 1e-4);
            Assert.Near(2010.0, GpuRivalGenerator.TechYear(new DateTime(2010, 1, 1), float.NaN), 1e-6, "a lag that is not a number is no lag");
        }

        [Test]
        public void Ac132_TheParts_AreTheNewestOfTheTechYear()
        {
            GpuRivalTech tech = Tech(new DateTime(2003, 3, 1), 0.5f);
            GpuDesignInput input = GpuRivalGenerator.DesignFor(Consumer(), tech, Db(), 2003, Rng(1));
            Assert.Equal("arch.shader", input.ArchId, "arch.shader (2002 <= 2002.67), not arch.unified (2006)");
            Assert.Equal("vram.ddr", input.MemId, "vram.ddr (1999), not vram.gddr3 (2004)");
            Assert.Equal("130nm", input.NodeName, "the node of the timeline at 2002.67");
            Assert.True(input.ClockMHz > 0f, "the card has a clock (the tab shows it)");

            // a year later the new memory is there
            GpuDesignInput later = GpuRivalGenerator.DesignFor(Consumer(), Tech(new DateTime(2005, 6, 1), 0.5f), Db(), 2005, Rng(1));
            Assert.Equal("vram.gddr3", later.MemId);
            Assert.Equal("arch.shader", later.ArchId);
        }

        [Test]
        public void BeforeTheFirstRecord_TheFirstOfEachListIsTaken()
        {
            // 1981 with the background's two years of lag: 1979, before arch.2d, vram.dram and cool.passive (all 1981)
            GpuRivalTech tech = Tech(new DateTime(1981, 6, 1), 2f);
            GpuDesignInput input = GpuRivalGenerator.DesignFor(Consumer(), tech, Db(), 1981, Rng(3));
            Assert.Equal("arch.2d", input.ArchId);
            Assert.Equal("vram.dram", input.MemId);
            Assert.Equal("cool.passive", input.CoolerId);
            Assert.True(GpuRivalGenerator.Evaluate(input, tech, Db(), 1981).Valid, "the first card of the market can be made");
        }

        [Test]
        public void TheSameSeed_GivesTheSameCards_AndAnotherSeedOtherCards()
        {
            IGpuRandom a = Rng(42), b = Rng(42), c = Rng(7);
            bool differs = false;
            for (int year = 1983; year <= 2030; year += 3)
            {
                var date = new DateTime(year, 5, 1);
                GpuRivalTech tech = Tech(date, 2f);
                GpuRivalDesign x = GpuRivalGenerator.Generate(Consumer(), tech, Db(), year, a, 300f, 1f);
                GpuRivalDesign y = GpuRivalGenerator.Generate(Consumer(), tech, Db(), year, b, 300f, 1f);
                GpuRivalDesign z = GpuRivalGenerator.Generate(Consumer(), tech, Db(), year, c, 300f, 1f);
                string at = " in " + year;
                Assert.Equal(x.Input.ArchId, y.Input.ArchId, at);
                Assert.Equal(x.Input.MemId, y.Input.MemId, at);
                Assert.Equal(x.Input.CoolerId, y.Input.CoolerId, at);
                Assert.Equal(x.Input.DieMm2, y.Input.DieMm2, at);
                Assert.Equal(x.Input.ClockMHz, y.Input.ClockMHz, at);
                Assert.Equal(x.Input.VramMB, y.Input.VramMB, at);
                Assert.Equal(x.Input.BusWidthBits, y.Input.BusWidthBits, at);
                Assert.Equal(x.Price, y.Price, at);
                Assert.Equal(x.Stats.EffectivePerf, y.Stats.EffectivePerf, at);
                if (x.Input.DieMm2 != z.Input.DieMm2 || x.Input.ClockMHz != z.Input.ClockMHz) differs = true;
            }
            Assert.True(differs, "another seed moves the die or the clock");
        }

        [Test]
        public void EveryYear_TheCardIsValid_Cool_AndWithinItsLimits()
        {
            GpuContentDb db = Db();
            GpuBalanceDto balance = db.Balance;
            IGpuRandom rng = Rng(2024);
            for (int year = 1981; year <= 2035; year++)
            {
                foreach (float lag in new[] { 0f, 0.5f, 2f })
                {
                    var date = new DateTime(year, 1 + year % 12, 1);
                    GpuRivalTech tech = Tech(date, lag);
                    GpuRivalDesign design = GpuRivalGenerator.Generate(Consumer(), tech, db, year, rng, 249f, 1f);
                    string at = " in " + year + " with lag " + lag;
                    Assert.True(design.Valid, "a valid card" + at + ": " + design.Stats.InvalidReasonKey);
                    Assert.True(design.Stats.TempC <= balance.rivals.targetTempC + 1e-3f, "Temp <= 85" + at + ": " + design.Stats.TempC);

                    GpuMemoryDto mem = db.Mem(design.Input.MemId);
                    float vram = design.Input.VramMB;
                    double log = Math.Log(vram, 2.0);
                    Assert.Near(Math.Round(log), log, 1e-6, "VRAM is a power of two" + at + ": " + vram);
                    Assert.True(vram <= mem.maxVramMB, "VRAM <= maxVramMB" + at + ": " + vram + " of " + mem.maxVramMB);
                    Assert.True(mem.busWidths.Contains(design.Input.BusWidthBits), "a bus of the memory" + at);

                    float maxDie = db.MaxDie(year);
                    Assert.InRange(design.Input.DieMm2, balance.design.minDie, maxDie, "the die" + at);
                    Assert.True(design.Input.ClockMHz <= design.Stats.MaxClockMHz * 1.0001f, "the clock is within the architecture" + at);
                    Assert.True(design.Price >= balance.rivals.priceFloorMarkup * design.Stats.UnitCost - 1e-3f, "price >= 1.3 x U" + at);
                }
            }
        }

        [Test]
        public void TheCooler_IsTheWeakestThatHolds85()
        {
            GpuContentDb db = Db();
            GpuRivalTech tech = Tech(new DateTime(2022, 6, 1), 0f);
            GpuDesignInput input = GpuRivalGenerator.DesignFor(Consumer(), tech, db, 2022, Rng(5));
            GpuStats stats = GpuRivalGenerator.Evaluate(input, tech, db, 2022);
            Assert.True(stats.TempC <= 85f);

            // every weaker cooler of the year would be hotter than 85
            float resistance = db.Cooler(input.CoolerId).thermalResistance;
            foreach (GpuCoolerDto cooler in db.Coolers)
            {
                if (cooler.year > tech.TechYear || cooler.thermalResistance <= resistance) continue;
                float temp = GpuDesignMath.TemperatureC(stats.PowerW, cooler.thermalResistance, db.Balance.design.ambientC);
                Assert.True(temp > 85f, cooler.id + " would hold it too: " + temp);
            }
            Assert.NotEqual("cool.vapor", input.CoolerId, "a 2022 card of half the largest die does not need the strongest cooler");
        }

        [Test]
        public void WhenNoCoolerHoldsTheTarget_TheClockComesDown()
        {
            TextContentSource source = GpuFiles.ShippedSingle().Edit("balance", "\"targetTempC\": 85", "\"targetTempC\": 40");
            GpuContentDb db = GpuFiles.Load(source);
            GpuSegmentDto seg = db.Segment("consumer");
            GpuRivalTech tech = Tech(new DateTime(2005, 6, 1), 0f);

            GpuDesignInput input = GpuRivalGenerator.DesignFor(seg, tech, db, 2005, Rng(9));
            GpuStats stats = GpuRivalGenerator.Evaluate(input, tech, db, 2005);
            Assert.Equal("cool.dual", input.CoolerId, "the strongest cooler of 2005");
            Assert.True(stats.TempC <= 40f + 1e-3f, "the clock was lowered to hold the target: " + stats.TempC);
            Assert.True(input.ClockMHz < 0.9f * stats.MaxClockMHz, "the clock is well below the frontier: " + input.ClockMHz + " of " + stats.MaxClockMHz);
            Assert.True(stats.Valid);
        }

        [Test]
        public void TheVideoMemory_IsThePowerOfTwoAtLeastTheRequirement()
        {
            Assert.Equal(4f, GpuRivalGenerator.PowerOfTwoAtLeast(3f, 16f));
            Assert.Equal(4f, GpuRivalGenerator.PowerOfTwoAtLeast(4f, 16f), "an exact power stays");
            Assert.Equal(0.0625f, GpuRivalGenerator.PowerOfTwoAtLeast(0.0625f, 0.25f), "fractions of a megabyte (1981)");
            Assert.Equal(0.125f, GpuRivalGenerator.PowerOfTwoAtLeast(0.07f, 0.25f));
            Assert.Equal(16384f, GpuRivalGenerator.PowerOfTwoAtLeast(20000f, 24576f), "halved until it fits a maximum that is not a power of two");
            Assert.Equal(64f, GpuRivalGenerator.PowerOfTwoAtLeast(100f, 64f));
            Assert.Equal(1f, GpuRivalGenerator.PowerOfTwoAtLeast(0f, 8f), "nothing required: 1 MB");
            Assert.Equal(0.25f, GpuRivalGenerator.PowerOfTwoAtLeast(0f, 0.25f), "and halved to the maximum");
            Assert.Equal(0f, GpuRivalGenerator.PowerOfTwoAtLeast(5f, 0f), "no memory");
            Assert.Equal(0f, GpuRivalGenerator.PowerOfTwoAtLeast(5f, float.NaN));
        }

        [Test]
        public void TheVideoMemory_FollowsTheSegmentAndItsFactor()
        {
            GpuContentDb db = Db();
            GpuSegmentDto seg = Consumer();
            GpuRivalTech tech = Tech(new DateTime(2005, 1, 1), 0f);
            GpuDesignInput input = GpuRivalGenerator.DesignFor(seg, tech, db, 2005, Rng(1));
            Assert.Equal(256f, input.VramMB, "2005: the requirement of the segment is 256 MB");
            Assert.Equal("vram.gddr3", input.MemId);
            Assert.Equal(256, input.BusWidthBits, "rivalBusTier 1: the second bus of GDDR3");
        }

        [Test]
        public void ThePrice_OfAVendor_IsAtLeast1_3TimesTheUnitCost_AndOfTheBackgroundTheBudget()
        {
            Assert.Equal(130f, GpuRivalGenerator.RivalPrice(100f, 50f, 1f, 1.3f), "a cheap budget: the floor");
            Assert.Equal(330f, GpuRivalGenerator.RivalPrice(100f, 300f, 1.1f, 1.3f), "the budget x priceFactor");
            Assert.Equal(0f, GpuRivalGenerator.RivalPrice(float.NaN, -1f, 1f, 1.3f));
            Assert.Equal(249f, GpuRivalGenerator.BackgroundPrice(249f, 1f));
            Assert.Equal(249f, GpuRivalGenerator.BackgroundPrice(249f, 0f), "a factor that is not set is 1");

            GpuRivalTech tech = Tech(new DateTime(2010, 1, 1), 1f);
            GpuRivalDesign low = GpuRivalGenerator.Generate(Consumer(), tech, Db(), 2010, Rng(4), 1f, 1f);
            Assert.True(low.Stats.UnitCost > 0f, "the card has a cost");
            Assert.Near(1.3 * low.Stats.UnitCost, low.Price, 1e-3 * low.Price, "price = 1.3 x U when the budget is below it");

            GpuRivalDesign background = GpuRivalGenerator.GenerateBackground(Consumer(), tech, Db(), 2010, Rng(4), 299f);
            Assert.Equal(299f * Db().Balance.background.priceFactor, background.Price, "the background sells at the budget times background.priceFactor");
            Assert.Equal(low.Input.DieMm2, background.Input.DieMm2, "the same stream gives the same card");
        }

        [Test]
        public void TheCostOfTheDie_ComesFromTheGameWhenItIsGiven()
        {
            GpuRivalTech tech = Tech(new DateTime(2005, 1, 1), 0f);
            var calls = new List<int>();
            GpuDesignInput input = GpuRivalGenerator.DesignFor(Consumer(), tech, Db(), 2005, Rng(1));
            GpuStats port = GpuRivalGenerator.Evaluate(input, tech, Db(), 2005);

            tech.DieCost = (die, material, dpw, fr) =>
            {
                calls.Add(dpw);
                return 1000f;
            };
            GpuStats game = GpuRivalGenerator.Evaluate(input, tech, Db(), 2005);
            Assert.Equal(1, calls.Count, "asked once, after the first pass");
            Assert.Equal(port.DpwYield, calls[0], "with the dies of the yield of the first pass");
            Assert.Equal(1000f, game.DieCost);
            Assert.True(game.UnitCost > port.UnitCost, "the game's die cost is in the unit cost");

            // the port: RealUnitCost at the maintenance and the wafer material of the maker
            float expected = GpuDesignMath.RealUnitCostPort(14000f, port.DpwFull, Db().Arch(input.ArchId).dieMaterialCost, port.DpwYield, port.FailureRate, 1f, 0f, 2000f);
            Assert.Near(expected, port.DieCost, expected * 1e-6);
        }

        [Test]
        public void Snapshot_KeepsTheCharacteristicsAndTheRawCosts()
        {
            GpuContentDb db = Db();
            GpuRivalTech tech = Tech(new DateTime(1997, 1, 1), 2f);
            GpuRivalDesign design = GpuRivalGenerator.GenerateBackground(Consumer(), tech, db, 1997, Rng(1), 249f);
            GpuStatsSave save = GpuRivalGenerator.Snapshot(design.Input, design.Stats, db, 1997);
            Assert.Equal(design.Stats.EffectivePerf, save.effectivePerf);
            Assert.Equal(design.Stats.PowerW, save.powerW);
            Assert.Equal(design.Stats.FailureRate, save.failureRate);
            Assert.Equal(design.Stats.Units, save.units);
            Assert.Equal(db.Arch(design.Input.ArchId).dieMaterialCost, save.dieMaterial);
            Assert.True(save.perGoodExtras > 0f, "memory, board and cooler of the year");
            Assert.Equal(new List<string>(design.Stats.Features).Count, save.features.Count);
        }

        [Test]
        public void TheGenerator_RefusesMissingArguments()
        {
            GpuRivalTech tech = Tech(new DateTime(2000, 1, 1), 0f);
            Assert.Throws<ArgumentNullException>(() => GpuRivalGenerator.DesignFor(null, tech, Db(), 2000, Rng(1)));
            Assert.Throws<ArgumentNullException>(() => GpuRivalGenerator.DesignFor(Consumer(), tech, null, 2000, Rng(1)));
            Assert.Throws<ArgumentNullException>(() => GpuRivalGenerator.DesignFor(Consumer(), tech, Db(), 2000, null));

            // a maker without a frequency (the companies are not ready yet) gives a card that cannot be made
            tech.MaxFreqKHz = 0f;
            GpuRivalDesign design = GpuRivalGenerator.Generate(Consumer(), tech, Db(), 2000, Rng(1), 249f, 1f);
            Assert.False(design.Valid);
            Assert.Equal(GpuDesignMath.ReasonClock, design.Stats.InvalidReasonKey);
        }

        [Test]
        public void TheStreams_HaveTheirNames_AndRangeIsInclusive()
        {
            Assert.Equal("gpu-product-line/rivals", GpuRandom.RivalsStream);
            Assert.Equal("gpu-product-line/background", GpuRandom.BackgroundStream);
            Assert.Throws<ArgumentNullException>(() => new GpuRandom(null));

            var rng = new GpuRandom(new Xoshiro128(11));
            bool low = false, high = false;
            for (int i = 0; i < 400; i++)
            {
                int value = rng.Range(0, 3);
                Assert.InRange(value, 0, 3);
                low |= value == 0;
                high |= value == 3;
                float r = rng.Next01();
                Assert.True(r >= 0f && r < 1f);
            }
            Assert.True(low && high, "both ends of [0, 3] come");
            Assert.Equal(5, rng.Range(5, 5), "an empty range gives the low end");
            Assert.Equal(5, rng.Range(5, 2));
            for (int i = 0; i < 50; i++) Assert.InRange(rng.Range(int.MaxValue - 2, int.MaxValue), int.MaxValue - 2, int.MaxValue);

            // the wrapper draws exactly what the stream draws
            IModRng a = new Xoshiro128(99), b = new Xoshiro128(99);
            var wrapped = new GpuRandom(a);
            Assert.Equal(b.Value(), wrapped.Next01());
            Assert.Equal(b.Range(10, 21), wrapped.Range(10, 20));
        }
    }
}
