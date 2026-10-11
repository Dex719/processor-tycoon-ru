using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The integration simulation of stage 1 (TSK-028; FR-5, FR-6, NFR-1, NFR-2, NFR-7): thirty game years, 1981-2010, day by day through the services of the feature on the
    /// shipped content, the fake game and the fake factory, with a bot for the player (the division in 1995, a card every two years, the automatic lines). The acceptance:
    /// the run finishes without an exception and without a broken invariant; the work of a day stays within 0.5 ms and of a month within 5 ms (the figures go to the log
    /// of the run: lines <c>perf</c>); the other makers keep at least 15 % of the market before 2000. Besides: the same seed gives the same run (NFR-6), the section of
    /// the save stays small and loads back without a word (NFR-3, NFR-8), a fabless player and a company that sells its factory keep working (FR-5 item 5), and the
    /// worst day of NFR-1 - 150 cards in 6 segments - fits the budget.
    /// <para>
    /// Stage 2 (TSK-035; FR-11, FR-12, FR-13, NFR-6, NFR-7) runs the market of the eras with the seven vendors, 1990-2035, without the player and with it: no exception and no
    /// broken invariant, the same history from the same seed, the mergers of 3dfx into NVIDIA (2001) and of ATI into AMD (2006) with the cards and their popularity, the vendors
    /// bound to the companies of the game that are paid and pay, and no vendor holding more than 70 % of a segment for more than three years.
    /// </para>
    /// </summary>
    [Covers(typeof(GpuMarketService), typeof(GpuLineup), typeof(GpuProduction), typeof(GpuResearch), typeof(GpuProjects), typeof(GpuDivision), typeof(GpuPerfMeter),
        typeof(GpuEras), typeof(GpuRivals), typeof(GpuRivalAI))]
    public class GpuSimulationTests
    {
        // A run takes about a second and is deterministic: every plan runs once for the whole class, and its report goes to the log once.
        private static readonly Dictionary<string, GpuSimulation> Runs = new Dictionary<string, GpuSimulation>(StringComparer.Ordinal);

        private static GpuSimulation Run(string key, Func<SimPlan> plan)
        {
            GpuSimulation sim;
            if (Runs.TryGetValue(key, out sim)) return sim;
            sim = new GpuSimulation(plan());
            sim.Run();
            Console.WriteLine(sim.Report(key == "reference"));
            Runs[key] = sim;
            return sim;
        }

        private static GpuSimulation Reference()
        {
            return Run("reference", () => new SimPlan());
        }

        private static GpuSimulation Twin()
        {
            return Run("twin", () => new SimPlan { Name = "reference, again" });
        }

        private static GpuSimulation OtherSeed()
        {
            return Run("seed", () => new SimPlan { Name = "another seed", Seed = 43 });
        }

        private static GpuSimulation Hoarder()
        {
            return Run("hoarder", () => new SimPlan { Name = "keeps every card on sale", ReplaceOnRelease = false });
        }

        private static GpuSimulation FablessAlways()
        {
            return Run("fabless", () => new SimPlan { Name = "fabless from the founding", FablessFromYear = 1995 });
        }

        private static GpuSimulation CpuFirst()
        {
            return Run("cpufirst", () => new SimPlan { Name = "CPU first, 50 of 60 lines for the CPUs", CpuFirst = true, CpuLines = 50 });
        }

        private static GpuSimulation SellsTheFactory()
        {
            return Run("sells", () => new SimPlan { Name = "sells the factory in 2002", FablessFromYear = 2002 });
        }

        private static string Join(IEnumerable<string> lines)
        {
            return string.Join("\n  ", new List<string>(lines).ToArray());
        }

        // ---- the run: no exception, no broken invariant (FR-5, FR-6, NFR-2) ----

        [Test]
        public void ThirtyYears_RunWithoutAnExceptionOrABrokenInvariant()
        {
            GpuSimulation sim = Reference();
            Assert.Equal(10957, sim.Days, "1 January 1981 to 31 December 2010, day by day");
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, "the services warned during the run:\n  " + Join(sim.World.Warnings));
            Assert.Equal(359, sim.Months.Count, "the 1st of every month but the first of the run: 30 years less a month");
            Assert.True(sim.State.Data.market.seeded, "the market was seeded once at the start");
            Assert.True(sim.State.Data.divisionFounded);
            Assert.True(sim.LastBackgroundPerf > sim.FirstBackgroundPerf * 1000d,
                "the card of the others is renewed every year and gets better with the decades: " + sim.FirstBackgroundPerf + " -> " + sim.LastBackgroundPerf);
        }

        [Test]
        public void TheReserveOfTheCards_NeverTakesMoreThanTheFactoryHas_NfrTwo()
        {
            // the run checks it after the reserve and after the CPUs on every day; here a tight factory, where the cards and the CPUs fight for the lines
            GpuSimulation tight = Run("tight", () => new SimPlan { Name = "a tight factory", CapacityLines = 12, CpuLines = 12 });
            Assert.True(tight.Violations.Count == 0, "invariants of the run:\n  " + Join(tight.Violations));
            Assert.True(tight.World.Game.Factory.Reservations > 3000, "the cards reserved their lines on most days: " + tight.World.Game.Factory.Reservations);
            Assert.True(tight.CardsReleased >= 7, "and the player still gets its cards out: " + tight.CardsReleased);
        }

        [Test]
        public void TheBot_FoundsIn1995_ResearchesAndReleasesACardEveryTwoYears()
        {
            GpuSimulation sim = Reference();
            GpuSaveSection data = sim.State.Data;
            Assert.Equal(1995, data.foundedOn.y);
            Assert.Equal(1, data.foundedOn.m);
            Assert.True(sim.Events.Contains("1995-01-01 division founded"), "the founding is the first day of 1995");
            Assert.True(sim.Game.SpentOn(GpuExpense.Development) > sim.World.Division.Cost, "the founding and the projects are paid under Development");

            foreach (string id in new[] { "arch.3d", "arch.tnl", "arch.shader", "arch.unified", "arch.gpgpu", "vram.sgram", "vram.ddr", "vram.gddr3", "vram.gddr5", "cool.fan", "cool.dual" })
                Assert.True(data.research.completed.Contains(id), "researched by 2010: " + id);
            Assert.True(sim.Game.SpentOn(GpuExpense.Research) > 0f, "the laboratory is paid under Research");

            Assert.Equal(8, sim.CardsReleased, "1995, 1997, ... 2009");
            Assert.Equal(sim.CardsReleased, sim.ProjectsStarted, "every project became a card");
            var years = new List<int>();
            foreach (string line in sim.Releases) years.Add(int.Parse(line.Substring(0, 4), CultureInfo.InvariantCulture));
            for (int i = 1; i < years.Count; i++)
                Assert.True(years[i] - years[i - 1] >= 2 && years[i] - years[i - 1] <= 3, "a card every two years: " + string.Join(", ", sim.Releases.ToArray()));
            Assert.Equal(1995, years[0], "the first card goes out in the year of the founding");

            Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales) > 1e9f, "the cards sell: " + sim.Game.EarnedOn(GpuRevenue.Sales));
            Assert.True(sim.Game.SpentOn(GpuExpense.Factories) > 0f, "the production is paid under Factories");
            Assert.Equal(sim.CardsReleased - 1, sim.CardsRetired, "each generation leaves when the next one comes; the last one is on sale");
            Assert.Equal(1, sim.PlayerCardsOnSale());
        }

        [Test]
        public void WithCpuFirst_TheCardsTakeOnlyTheLinesTheCpusLeave_AndTheirLinesStillGo()
        {
            GpuSimulation sim = CpuFirst();
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, Join(sim.World.Warnings));
            Assert.True(sim.MaxReserved <= 10, "FR-5 item 4: the cards got at most 60 - 50 = 10 lines on a day: " + sim.MaxReserved);
            Assert.True(sim.MaxReserved >= 1, "and they do get some: " + sim.MaxReserved);
            Assert.True(sim.CardsReleased >= 7, "the cards go out: " + sim.CardsReleased);
            Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales) > 0f);

            // the same player with the default switch is not held back by the CPUs
            Assert.True(Reference().MaxReserved > 10, "the default mode reserves more than that: " + Reference().MaxReserved);
        }

        // ---- the market (FR-6, NFR-7) ----

        [Test]
        public void TheOtherMakers_KeepAtLeastFifteenPercentOfTheMarketBefore2000()
        {
            GpuSimulation sim = Reference();
            Assert.True(sim.MinBackgroundShareBefore2000 >= 0.15f,
                "NFR-7: the others hold at least 15 % until 2000; lowest " + (sim.MinBackgroundShareBefore2000 * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " % on "
                + sim.MinBackgroundShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // before the division there is nobody but the others; the player takes a real part of the market once it sells, and never all of it
            foreach (SimMonth row in sim.Months)
            {
                if (row.Year < 1995) Assert.True(row.BackgroundShare > 0.999f && row.PlayerShare == 0f, row.Year + "-" + row.Month + ": the market is the others'");
            }
            Assert.True(sim.MaxPlayerShare > 0.2f, "the player sells: its best share is " + sim.MaxPlayerShare);
            Assert.True(sim.MaxPlayerShare < 0.85f, "and does not own the market: " + sim.MaxPlayerShare);
        }

        [Test]
        public void ThePlayerWhoKeepsEveryCardOnSale_LeavesTheOthersLessRoom_ButRunsClean()
        {
            // The case the calibration of TSK-045 looked at: two generations on sale at once left the others about 11 % in 1997-98 (the old GUI card of 1995 out-computed the first 3D
            // cards). The architectures of the old cards and the others (the price and the popularity of the background) were calibrated, and now the others hold the 15 % of NFR-7 here too.
            GpuSimulation sim = Hoarder();
            Assert.True(sim.Violations.Count == 0, "invariants of the run: " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, Join(sim.World.Warnings));
            Assert.True(sim.MinBackgroundShareBefore2000 >= 0.15f, "the others hold at least 15 % of the market: " + sim.MinBackgroundShareBefore2000);
            Assert.True(sim.CardsRetired >= 5, "the outdated cards are taken off: " + sim.CardsRetired);
        }

        [Test]
        public void TheRunIsTheSameForTheSameSeed_AndTheBackgroundDiffersForAnother()
        {
            GpuSimulation a = Reference();
            GpuSimulation b = Twin();
            Assert.Equal(a.SectionText(), b.SectionText(), "NFR-6: two runs of one seed leave one section");
            Assert.Equal(string.Join("|", a.Releases.ToArray()), string.Join("|", b.Releases.ToArray()));

            GpuSimulation other = OtherSeed();
            Assert.True(other.Violations.Count == 0, Join(other.Violations));
            Assert.NotEqual(a.SectionText(), other.SectionText(), "another stream of the background gives other cards");
            Assert.True(other.MinBackgroundShareBefore2000 >= 0.15f, "and the 15 % of the others holds for it too: " + other.MinBackgroundShareBefore2000);
        }

        // ---- the time of a day and of a month (NFR-1) ----

        [Test]
        public void TheDayAndTheMonth_StayWithinTheBudgetsOfNfrOne()
        {
            GpuSimulation[] runs = { Reference(), Twin(), OtherSeed() };
            double day = double.MaxValue, month = double.MaxValue, worstDay = 0;
            foreach (GpuSimulation sim in runs)
            {
                Assert.Equal(sim.Days, sim.Perf.Samples(GpuPerfMeter.Day), "every day was timed as a whole");
                Assert.Equal(359, sim.Perf.Samples(GpuPerfMeter.Month), "every 1st of a month was timed");
                Assert.True(sim.PerfLines.Count >= 358 && sim.PerfLines[0].StartsWith("perf 1981-"), "a line for every month of the run: " + sim.PerfLines.Count);
                day = Math.Min(day, sim.Perf.AverageMs(GpuPerfMeter.Day));
                month = Math.Min(month, sim.Perf.AverageMs(GpuPerfMeter.Month));
                worstDay = Math.Max(worstDay, sim.Perf.MaxMs(GpuPerfMeter.Day));
            }
            // the best of three runs: the first one pays for the compilation of the code, a busy machine slows another
            Console.WriteLine("  perf  stage 1, 1981-2010, best of 3 runs: a day " + day.ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget " + GpuPerfMeter.DayBudgetMs
                              + "), a month " + month.ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget " + GpuPerfMeter.MonthBudgetMs + "); the longest single day "
                              + worstDay.ToString("0.000", CultureInfo.InvariantCulture) + " ms");
            Assert.True(day <= GpuPerfMeter.DayBudgetMs, "NFR-1: a day of the feature takes at most 0.5 ms: " + day);
            Assert.True(month <= GpuPerfMeter.MonthBudgetMs, "NFR-1: a month of the feature takes at most 5 ms: " + month);
        }

        [Test]
        public void TheWorstDay_OfHundredAndFiftyCardsInSixSegments_FitsTheBudget()
        {
            // NFR-1 names this load: 150 products (the player's, the makers', the background) and 6 segments. A year of it day by day, every card sold in every segment.
            GpuContentDb content = SixSegments();
            GpuSimulation sim = null;
            double best = double.MaxValue;
            double bestMonth = double.MaxValue;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                sim = new GpuSimulation(new SimPlan
                {
                    Name = "150 cards, 6 segments", StartYear = 2005, EndYear = 2005, FoundYear = 0, CapacityLines = 200, CpuLines = 20, Content = content, AfterSeed = PutHundredAndFiftyCards,
                });
                sim.Run();
                best = Math.Min(best, sim.Perf.AverageMs(GpuPerfMeter.Day));
                bestMonth = Math.Min(bestMonth, sim.Perf.AverageMs(GpuPerfMeter.Month));
            }
            Console.WriteLine(sim.Report());
            Console.WriteLine("  perf  per handler, ms per day: " + sim.BreakdownText());
            Console.WriteLine("  perf  150 cards, 6 segments, best of 4 runs: a day " + best.ToString("0.0000", CultureInfo.InvariantCulture) + " ms, a month "
                              + bestMonth.ToString("0.0000", CultureInfo.InvariantCulture) + " ms");

            Assert.Equal(150, sim.State.Data.products.Count, "6 backgrounds, 44 cards of the player, 100 of the makers");
            Assert.Equal(6, sim.World.Market.OpenSegments.Count);
            Assert.True(sim.Violations.Count == 0, Join(sim.Violations));
            Assert.True(sim.World.Game.Factory.Reservations >= 360, "the cards of the player reserved their lines every day: " + sim.World.Game.Factory.Reservations);
            Assert.True(best <= GpuPerfMeter.DayBudgetMs, "NFR-1: a day with 150 cards takes at most 0.5 ms: " + best);
            Assert.True(bestMonth <= GpuPerfMeter.MonthBudgetMs, "NFR-1: a month takes at most 5 ms: " + bestMonth);
        }

        private static GpuContentDb SixSegments()
        {
            TextContentSource source = GpuFiles.ShippedSingle();
            JsonObject file = GpuFiles.ParseObject(source.Builtin["segments"], "segments.json");
            JsonArray records = file.GetArray("records");
            var first = (JsonObject)records[0];
            for (int i = 2; i <= 6; i++)
            {
                var copy = (JsonObject)first.DeepClone();
                copy.Set("id", "consumer" + i);
                records.Add(copy);
            }
            source.Set("segments", ModJson.Write(file, true));
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(source, problems);
            Assert.Equal(0, problems.Count, "the six segments are valid:\n  " + Join(problems));
            Assert.Equal(6, db.Segments.Count);
            return db;
        }

        private static void PutHundredAndFiftyCards(GpuSimulation sim)
        {
            GpuWorld world = sim.World;
            world.Founded();
            var segments = new List<string>();
            foreach (GpuSegmentDto seg in world.Content.Segments) segments.Add(seg.id);

            for (int i = 0; i < 44; i++)
            {
                GpuProductSave card = world.AddCard("g-p" + i, 2);
                card.price = 150f + i;
                card.stock = 5000000;
                OnEverySegment(card, segments, 0.3f);
            }
            for (int i = 0; i < 100; i++)
            {
                GpuProductSave card = world.AddCard("g-v" + i, 0);
                card.owner = GpuState.OwnerVendor;
                card.vendorId = "maker" + (i % 10);
                card.companyKey = string.Empty;
                card.price = 120f + i;
                card.stock = 1000000000;
                OnEverySegment(card, segments, 0.4f);
            }
        }

        private static void OnEverySegment(GpuProductSave card, List<string> segments, float popularity)
        {
            card.released = new GpuDate { y = 2004, m = 6, d = 1 };
            card.segIds = new List<string>(segments);
            card.pop = new List<float>();
            foreach (string unused in segments) card.pop.Add(popularity);
        }

        // ---- the save (NFR-3, NFR-8) ----

        [Test]
        public void TheSectionOfTheSave_AfterThirtyYears_IsSmall_AndLoadsBackWithoutAWord()
        {
            GpuSimulation sim = Reference();
            string text = sim.SectionText();
            int bytes = Encoding.UTF8.GetByteCount(text);
            Console.WriteLine("  sim   the section 'gpu-product-line' after 30 years: " + (bytes / 1024) + " KB, " + sim.State.Data.products.Count + " cards, " + sim.State.Data.designs.Count + " designs");
            Assert.True(bytes <= 2 * 1024 * 1024, "the section is at most 2 MB: " + bytes);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the section is JSON: " + error);
            var loaded = new GpuState(sim.World.Content);
            loaded.Read((JsonObject)node, GpuState.SchemaVersion);
            Assert.True(loaded.Warnings.Count == 0, "the load sanitized something:\n  " + Join(loaded.Warnings));
            Assert.Equal(text, ModJson.Write(loaded.Write(), false), "write, read and write again give the same section");
            Assert.Equal(sim.State.Data.products.Count, loaded.Data.products.Count);
        }

        // ---- fabless and the sale of the factory (FR-5 item 5, F18) ----

        [Test]
        public void AFablessPlayer_OrdersItsCardsFromTheFoundry_AndPaysTheRoyalty()
        {
            GpuSimulation sim = FablessAlways();
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, Join(sim.World.Warnings));
            Assert.Equal(0, sim.Game.Factory.Reservations, "a fabless company reserves nothing at a factory");
            Assert.True(sim.Game.SpentOn(GpuExpense.Outsourcing) > 0f, "the cards are paid under Outsourcing");
            Assert.True(sim.Game.SpentOn(GpuExpense.Royalties) > 0f, "and the 5 % royalty is paid on the sales");
            Assert.True(sim.CardsReleased >= 7, "the cards go out: " + sim.CardsReleased);
            Assert.True(sim.MaxPlayerShare > 0.1f, "and sell: " + sim.MaxPlayerShare);
        }

        [Test]
        public void ACompanyThatSellsItsFactory_KeepsSellingItsCards_AndSaysSoOnce()
        {
            GpuSimulation sim = SellsTheFactory();
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            int moves = 0;
            foreach (string warning in sim.World.Warnings)
            {
                if (warning.Contains("became fabless")) moves++;
                else Assert.Fail("an unexpected warning: " + warning);
            }
            Assert.Equal(1, moves, "the change of the type is carried over once, with one line in the log");
            Assert.True(sim.Game.Factory.Reservations > 1000, "before 2002 the cards were made at the factory: " + sim.Game.Factory.Reservations);
            Assert.True(sim.Game.SpentOn(GpuExpense.Outsourcing) > 0f, "and after it - at the foundry");
            Assert.True(sim.Game.SpentOn(GpuExpense.Royalties) > 0f);
            Assert.True(sim.MeanPlayerShare(2005) > 0.05f, "the cards still sell in 2005: " + sim.MeanPlayerShare(2005));
            Assert.Equal(8, sim.CardsReleased);
        }

        // ---- stage 2: the market of the eras with the vendors (TSK-035) ----

        private static SimPlan ErasPlan(string name, uint seed = 42, int endYear = 2035, int foundYear = 0)
        {
            return new SimPlan { Name = name, StartYear = 1990, EndYear = endYear, FoundYear = foundYear, Rivals = true, Content = GpuWorld.ShippedEras(), Seed = seed };
        }

        private static GpuSimulation Eras()
        {
            return Run("eras", () => ErasPlan("eras and vendors, no player"));
        }

        private static GpuSimulation ErasTwin()
        {
            return Run("erasTwin", () => ErasPlan("eras and vendors, again"));
        }

        private static GpuSimulation ErasOtherSeed()
        {
            return Run("erasSeed", () => ErasPlan("eras and vendors, another seed", 43));
        }

        private static GpuSimulation ErasWithPlayer()
        {
            return Run("erasPlayer", () => ErasPlan("eras and vendors, with the player", 42, 2012, 1995));
        }

        [Test]
        public void TheMarketOfTheEras_RunsFor46Years_WithoutAnExceptionOrABrokenInvariant()
        {
            GpuSimulation sim = Eras();
            Assert.Equal(16801, sim.Days, "1 January 1990 to 31 December 2035, day by day");
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, "the services warned during the run:\n  " + Join(sim.World.Warnings));
            Assert.Equal("eras", sim.State.Data.marketModeApplied);
            Assert.True(sim.State.Data.market.seeded);
            Assert.True(sim.VendorLaunches.Count > 300, "the vendors launched cards all through: " + sim.VendorLaunches.Count);
            Assert.True(sim.State.Data.products.Count <= sim.World.Content.Balance.limits.products, "the cards of the vendors are taken off, the section does not grow without end: " + sim.State.Data.products.Count);
            Assert.True(sim.State.Data.products.Count < 60, "a card per vendor and segment and two of each at most: " + sim.State.Data.products.Count);
            Assert.Equal(0, sim.Game.Factory.Reservations, "the vendors reserve no line at any factory");

            string text = sim.SectionText();
            Assert.True(Encoding.UTF8.GetByteCount(text) < 200 * 1024, "the section stays small: " + text.Length);
            Console.WriteLine("  sim   the other makers of the eras keep at least " + (sim.MinBackgroundShareBefore2000 * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " % before 2000 (lowest on "
                              + sim.MinBackgroundShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "; the target of NFR-7 is 15 %, TSK-045)");
            Assert.True(sim.MinBackgroundShareBefore2000 >= 0.15f, "NFR-7: the others of the eras hold at least 15 % until 2000: " + sim.MinBackgroundShareBefore2000);
            Console.WriteLine("  sim   the section after 46 years of the eras: " + (Encoding.UTF8.GetByteCount(text) / 1024) + " KB, " + sim.State.Data.products.Count + " cards, "
                              + sim.VendorLaunches.Count + " launches of the vendors");
        }

        [Test]
        public void TheVendorsAndTheEras_GiveTheSameHistoryFromTheSameSeed_NfrSix()
        {
            GpuSimulation a = Eras(), b = ErasTwin();
            Assert.Equal(string.Join("\n", a.VendorLaunches.ToArray()), string.Join("\n", b.VendorLaunches.ToArray()), "the launches of the vendors, card by card");
            Assert.Equal(string.Join("\n", a.VendorEvents.ToArray()), string.Join("\n", b.VendorEvents.ToArray()), "the entries, exits and mergers");
            Assert.Equal(a.SectionText(), b.SectionText(), "and the whole section of the save");

            GpuSimulation other = ErasOtherSeed();
            Assert.True(other.Violations.Count == 0, Join(other.Violations));
            Assert.False(string.Join("\n", a.VendorLaunches.ToArray()) == string.Join("\n", other.VendorLaunches.ToArray()), "another seed, other launches");
            Assert.Equal(string.Join("\n", a.VendorEvents.ToArray()), string.Join("\n", other.VendorEvents.ToArray()), "but the history of the makers is the calendar of the content");
        }

        [Test]
        public void TheCalendarOfTheMakers_IsTheHistoryOfTheContent()
        {
            GpuSimulation sim = Eras();
            Assert.Equal(new[]
            {
                "1995-01-01 entered nvidia",
                "1996-01-01 entered 3dfx",
                "2001-01-01 merged 3dfx into nvidia",
                "2003-01-01 exited s3",
                "2005-01-01 exited matrox",
                "2006-01-01 entered amd.gpu",
                "2006-01-01 merged ati into amd.gpu",
                "2022-01-01 entered intel.gpu",
            }, sim.VendorEvents.ToArray(), "AC-12.1 (3dfx in 1996), the mergers of 2001 and 2006, the exits of S3 and Matrox, the entry of Intel");
        }

        [Test]
        public void Ac122_AtiMergesIntoAmd_InJanuary2006_WithItsCardsAndTheirPopularity()
        {
            var before = new Dictionary<string, float>();   // the cards of ATI on 1 December 2005: id -> popularity
            var after = new Dictionary<string, GpuProductSave>();
            float revenueBefore = 0f, revenueAfter = 0f;
            GpuSimulation sim = new GpuSimulation(ErasPlan("ATI into AMD", 42, 2007));
            sim.Plan.OnMonth = s =>
            {
                DateTime d = s.Game.Date;
                if (d.Year == 2005 && d.Month == 12) foreach (GpuProductSave p in s.World.Rivals.AI.CardsOf("ati")) before[p.id] = p.pop[0];
                if (d.Year == 2005 && d.Month == 12) revenueBefore = s.Game.EarnedOn(GpuRevenue.Sales, "ai:1");
                if (d.Year == 2006 && d.Month == 1)
                {
                    foreach (GpuProductSave p in s.World.State.Data.products) after[p.id] = p;
                    revenueAfter = s.Game.EarnedOn(GpuRevenue.Sales, "ai:1");
                }
            };
            sim.Run();

            Assert.True(before.Count >= 4, "ATI had cards in its segments in 2005: " + before.Count);
            Assert.True(sim.VendorEvents.Contains("2006-01-01 merged ati into amd.gpu"));
            int moved = 0;
            foreach (KeyValuePair<string, float> card in before)
            {
                GpuProductSave now;
                if (!after.TryGetValue(card.Key, out now)) continue;   // the oldest of three in a segment goes
                moved++;
                Assert.Equal("amd.gpu", now.vendorId, card.Key + ": AMD's now");
                Assert.Equal("ai:1", now.companyKey, card.Key + ": and its money goes to the company AMD");
                Assert.True(now.pop[0] >= card.Value - 1e-6f, card.Key + ": the popularity is kept (" + card.Value + " -> " + now.pop[0] + ")");
            }
            Assert.True(moved >= 2, "ATI's cards pass to AMD: " + moved);
            Assert.True(revenueBefore == 0f, "AMD had no revenue from the cards before it came");
            Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales, "ai:1") > 0f, "and has it after: " + sim.Game.EarnedOn(GpuRevenue.Sales, "ai:1") + " (" + revenueAfter + " in January 2006)");
            Assert.True(sim.World.Rivals.AI.CardsOf("ati").Count == 0, "ATI has no cards");

            // the Radeon series goes on under AMD
            string[] atiLaunches = sim.VendorLaunches.Where(l => l.Contains(" ati ")).ToArray();
            string[] amdLaunches = sim.VendorLaunches.Where(l => l.Contains(" amd.gpu ")).ToArray();
            Assert.True(atiLaunches.Length > 0 && amdLaunches.Length > 0);
            Assert.True(NumberOfTheCard(amdLaunches[0]) > NumberOfTheCard(atiLaunches[atiLaunches.Length - 1]) - 1,
                "AMD numbers its Radeons after ATI's: " + atiLaunches[atiLaunches.Length - 1] + " -> " + amdLaunches[0]);
        }

        private static int NumberOfTheCard(string launchLine)
        {
            int perf = launchLine.IndexOf(" perf ", StringComparison.Ordinal);
            string head = launchLine.Substring(0, perf);
            return int.Parse(head.Substring(head.LastIndexOf(' ') + 1), CultureInfo.InvariantCulture);
        }

        [Test]
        public void The3dfxMergesIntoNvidia_InJanuary2001_AndItsCardsStayOnTheMarket()
        {
            var before = new Dictionary<string, string>();   // id -> name
            var after = new Dictionary<string, GpuProductSave>();
            GpuSimulation sim = new GpuSimulation(ErasPlan("3dfx into NVIDIA", 42, 2002));
            sim.Plan.OnMonth = s =>
            {
                DateTime d = s.Game.Date;
                if (d.Year == 2000 && d.Month == 12) foreach (GpuProductSave p in s.World.Rivals.AI.CardsOf("3dfx")) before[p.id] = p.name;
                if (d.Year == 2001 && d.Month == 1) foreach (GpuProductSave p in s.World.State.Data.products) after[p.id] = p;
            };
            sim.Run();

            Assert.True(before.Count >= 2, "3dfx had cards in 2000: " + before.Count);
            Assert.True(sim.VendorEvents.Contains("2001-01-01 merged 3dfx into nvidia"));
            int moved = 0;
            foreach (KeyValuePair<string, string> card in before)
            {
                GpuProductSave now;
                if (!after.TryGetValue(card.Key, out now)) continue;
                moved++;
                Assert.Equal("nvidia", now.vendorId, card.Key);
                Assert.Equal(card.Value, now.name, "a Voodoo stays a Voodoo");
            }
            Assert.True(moved >= 1, "cards of 3dfx are NVIDIA's from the 1st of January 2001: " + moved);
            Assert.Equal(0, sim.World.Rivals.AI.CardsOf("3dfx").Count);
        }

        [Test]
        public void NfrSeven_NoVendorHoldsMoreThanSeventyPercentOfASegmentForMoreThanThreeYears()
        {
            foreach (GpuSimulation sim in new[] { Eras(), ErasOtherSeed() })
            {
                string who;
                int months = sim.LongestMonthsAbove(0.7f, out who);
                Console.WriteLine("  sim   " + sim.Plan.Name + ": the longest run of a vendor over 70 % of a segment is " + months + " months (" + who + ")");
                Assert.True(months <= 36, sim.Plan.Name + ": " + months + " months over 70 %: " + who);
                Assert.True(sim.SegmentShares.Count > 1000, "the shares were recorded: " + sim.SegmentShares.Count);
            }
        }

        [Test]
        public void EverySegmentOfTheEras_HasSalesOfSeveralMakers_InItsGoodYears()
        {
            GpuSimulation sim = Eras();
            foreach (string seg in new[] { "gaming.high", "gaming.mid", "gaming.low" })
            {
                float nvidia = sim.MeanShare(2000, seg, "vendor:nvidia"), ati = sim.MeanShare(2000, seg, "vendor:ati"), voodoo = sim.MeanShare(2000, seg, "vendor:3dfx");
                int makers = (nvidia > 0.02f ? 1 : 0) + (ati > 0.02f ? 1 : 0) + (voodoo > 0.02f ? 1 : 0);
                Assert.True(makers >= 2, seg + " in 2000: " + nvidia + ", " + ati + ", " + voodoo);
            }
            Assert.True(sim.MeanShare(2022, "gaming.mid", "vendor:intel.gpu") > 0.02f, "Intel sells in the mainstream segment once it comes");
            Assert.True(sim.MeanShare(2022, "ai", "vendor:nvidia") > 0.05f, "and NVIDIA in the AI segment");
            Assert.True(sim.MeanShare(2012, "compute", "vendor:amd.gpu") + sim.MeanShare(2012, "compute", "vendor:nvidia") > 0.3f, "the compute segment is sold from 2007 and matured by 2012");
            Assert.Equal(0f, sim.MeanShare(2012, "office2d", "vendor:amd.gpu"), "the office segment closed in 2010");
        }

        [Test]
        public void TheBoundVendors_ArePaidForTheirSales_AndPayForTheirCards()
        {
            GpuSimulation sim = Eras();
            foreach (string company in new[] { "ai:1", "ai:0" })
            {
                int years = company == "ai:1" ? 30 : 14;   // AMD sells from 2006 to 2035, Intel from 2022 to 2035
                Console.WriteLine("  sim   " + company + " over " + years + " years: sales " + (sim.Game.EarnedOn(GpuRevenue.Sales, company) / 1e9f).ToString("0.0", CultureInfo.InvariantCulture)
                                  + " bn, development " + (sim.Game.SpentBy(company, GpuExpense.Development) / 1e9f).ToString("0.0", CultureInfo.InvariantCulture)
                                  + " bn, cost of the cards " + (sim.Game.SpentBy(company, GpuExpense.Factories) / 1e9f).ToString("0.0", CultureInfo.InvariantCulture) + " bn");
                Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales, company) > 0f, company + " earned from the cards it sold");
                Assert.True(sim.Game.SpentBy(company, GpuExpense.Development) > 0f, company + " paid for the development of its cards");
                Assert.True(sim.Game.SpentBy(company, GpuExpense.Factories) > 0f, company + " paid the cost of what it sold");
                Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales, company) > sim.Game.SpentBy(company, GpuExpense.Factories), company + ": the price is above the cost");
                Assert.Equal(0f, sim.Game.SpentBy(company, GpuExpense.Research) + sim.Game.SpentBy(company, GpuExpense.Outsourcing) + sim.Game.SpentBy(company, GpuExpense.Royalties),
                    company + ": no other article is touched");
            }
            Assert.Equal(0f, sim.Game.EarnedOn(GpuRevenue.Sales, "player"), "no player: its books are empty");
            Assert.Equal(0f, sim.Game.EarnedOn(GpuRevenue.Contracts, "ai:1"), "the contracts are not the market");
        }

        [Test]
        public void ABoundVendorInDefault_LaunchesNothing_ThroughTheWholeRun_AndTheOthersDo()
        {
            var plan = ErasPlan("AMD in default", 42, 2010);
            plan.StartYear = 2004;
            plan.BeforeSeed = s => s.Game.Companies["ai:1"].InDefault = true;
            GpuSimulation sim = Run("amdDefault", () => plan);

            Assert.True(sim.Violations.Count == 0, Join(sim.Violations));
            Assert.True(sim.VendorEvents.Contains("2006-01-01 entered amd.gpu"), "AMD comes all the same");
            Assert.False(sim.VendorLaunches.Any(l => l.Contains(" amd.gpu ")), "AC-13.3: but it launches no card while its company is in default");
            Assert.Equal(0f, sim.Game.SpentBy("ai:1", GpuExpense.Development), "and pays no development");
            Assert.True(sim.VendorLaunches.Any(l => l.Contains(" nvidia ")), "NVIDIA goes on");
            Assert.True(sim.World.Rivals.AI.CardsOf("amd.gpu").Count > 0, "the cards ATI left it stay on sale");
        }

        [Test]
        public void InAGameWithoutAmdAndIntel_TheVendorsRunAsVirtualOnes()
        {
            var plan = ErasPlan("without AMD and Intel", 42, 2030);
            plan.WithoutAmd = true;
            plan.WithoutIntel = true;
            GpuSimulation sim = Run("noAi", () => plan);

            Assert.True(sim.Violations.Count == 0, Join(sim.Violations));
            Assert.True(sim.VendorEvents.Contains("2006-01-01 entered amd.gpu"), "AC-12.4: AMD enters as a virtual vendor");
            Assert.False(sim.VendorEvents.Any(e => e.Contains("intel.gpu")), "Intel has no fallback: it never comes");
            Assert.Equal(0, sim.Game.Spent.Count + sim.Game.Earned.Count, "no company is paid or charged");
            Assert.True(sim.VendorLaunches.Any(l => l.Contains(" amd.gpu ")), "and AMD launches cards all the same");
            Assert.False(sim.VendorLaunches.Any(l => l.Contains(" intel.gpu ")));
            Assert.Equal(GpuState.VendorPending, sim.World.Rivals.Find("intel.gpu").State);
        }

        [Test]
        public void ThePlayerAmongTheVendors_SellsInTheSegmentsOfTheEras_AndTheRunIsClean()
        {
            GpuSimulation sim = ErasWithPlayer();
            Assert.True(sim.Violations.Count == 0, "invariants of the run:\n  " + Join(sim.Violations));
            Assert.True(sim.World.Warnings.Count == 0, "the services warned during the run:\n  " + Join(sim.World.Warnings));
            Assert.True(sim.CardsReleased >= 8, "the bot releases its cards: " + sim.CardsReleased);
            Assert.True(sim.MaxPlayerShare > 0.1f, "and sells: " + sim.MaxPlayerShare);
            Assert.True(sim.MaxPlayerShare < 0.9f, "and does not own the market: " + sim.MaxPlayerShare);
            Assert.True(sim.Game.EarnedOn(GpuRevenue.Sales) > 1e8f, "the revenue of the player: " + sim.Game.EarnedOn(GpuRevenue.Sales));
            foreach (GpuProductSave p in sim.State.Data.products.Where(x => x.owner == GpuState.OwnerPlayer && x.status == GpuState.StatusSelling))
            {
                Assert.True(p.segIds.Count >= 2, p.name + " competes in several segments of the eras: " + string.Join(",", p.segIds.ToArray()));
                Assert.Equal(p.segIds.Count, p.pop.Count);
            }
            // the vendors took no line: only the player's cards and the CPUs used the factory
            Assert.True(sim.MaxReserved <= 40, "the cards of the player reserved at most 40 lines of 60: " + sim.MaxReserved);
        }

        [Test]
        public void TheDayAndTheMonth_StayWithinTheBudgetsOfNfrOne_WithTheVendorsAndSixSegments()
        {
            GpuSimulation[] runs = { Eras(), ErasTwin(), ErasOtherSeed() };
            double day = double.MaxValue, month = double.MaxValue;
            foreach (GpuSimulation sim in runs)
            {
                Assert.Equal(sim.Days, sim.Perf.Samples(GpuPerfMeter.Day), "every day was timed as a whole");
                day = Math.Min(day, sim.Perf.AverageMs(GpuPerfMeter.Day));
                month = Math.Min(month, sim.Perf.AverageMs(GpuPerfMeter.Month));
            }
            Console.WriteLine("  perf  stage 2, 1990-2035, best of 3 runs: a day " + day.ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget " + GpuPerfMeter.DayBudgetMs
                              + "), a month " + month.ToString("0.0000", CultureInfo.InvariantCulture) + " ms (budget " + GpuPerfMeter.MonthBudgetMs + ")");
            Console.WriteLine("  perf  per handler, ms per day: " + runs[0].BreakdownText());
            Assert.True(day <= GpuPerfMeter.DayBudgetMs, "NFR-1: a day of the feature takes at most 0.5 ms: " + day);
            Assert.True(month <= GpuPerfMeter.MonthBudgetMs, "NFR-1: a month of the feature takes at most 5 ms: " + month);
        }

        // ---- TSK-045: the balance reference (NFR-7) ----
        //
        // The reference game of NFR-7: an IDM that starts in 1990 on Normal and founds the GPU division in 1995. Its player is the lineup bot: it researches the nodes of the tree as their
        // years come, waits for the architecture a gaming segment asks for (arch.3d for the segments that open in 1996), then keeps a card in each of the three gaming segments - high,
        // mainstream, low - and renews each of them as soon as the project is over and a year has passed, pricing at 0.9 of the budget of the segment, with the automatic lines on a factory
        // of 120 lines (40 for the CPUs). The CPU business is the one of GpuBalanceReference (the game's own market, 15 % of the money). The four marks of NFR-7 are read from the run;
        // balance-report.md has the numbers, the changes of the content that got them there and the reasons.

        private static GpuSimulation Balance(uint seed)
        {
            return Run("balance" + seed, () => new SimPlan
            {
                Name = "balance reference " + seed + ": IDM from 1990, Normal, the division in 1995", StartYear = 1990, EndYear = 2010, FoundYear = 1995, Rivals = true,
                Content = GpuWorld.ShippedEras(), Seed = seed, CapacityLines = 120, CpuLines = 40, CpuFirst = true, ReleaseEveryYears = 1,
                Lineup = new[] { "gaming.high", "gaming.mid", "gaming.low" },
                OnMonth = sim => TakeConsoleSnapshot(sim),
            });
        }

        private static string One(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The cards of GPU as a part of the whole revenue of the reference IDM in a year, 0..1.</summary>
        private static double GpuPartOfRevenue(GpuSimulation sim, int year, double cpuShare = GpuBalanceReference.ReferenceShare)
        {
            double gpu = sim.SalesIn(year), cpu = GpuBalanceReference.CpuRevenue(year, cpuShare);
            return gpu / Math.Max(1d, gpu + cpu);
        }

        private static void PrintBalance(GpuSimulation sim)
        {
            var table = new StringBuilder();
            for (int year = 1995; year <= 2010; year++)
            {
                table.Append("  bal   ").Append(year).Append(": cards ").Append((sim.SalesIn(year) / 1e6).ToString("0", CultureInfo.InvariantCulture)).Append(" M, CPUs ")
                     .Append((GpuBalanceReference.CpuRevenue(year) / 1e6).ToString("0", CultureInfo.InvariantCulture)).Append(" M, cards are ")
                     .Append(One(100 * GpuPartOfRevenue(sim, year))).Append(" % of the revenue; the others ").Append(One(sim.MeanBackgroundShare(year) * 100f)).Append(" %, the player ")
                     .Append(One(sim.MeanPlayerShare(year) * 100f)).Append(" %\n");
            }
            Console.Write(table.ToString());
            foreach (SimCard card in sim.CardLog.Take(6))
                Console.WriteLine("  bal   card " + card.Name + " " + card.Released.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": price " + card.Price.ToString("0", CultureInfo.InvariantCulture)
                                  + " of budget " + card.Budget.ToString("0", CultureInfo.InvariantCulture) + ", development " + One(card.DevCost / 1e6) + " M, pays back in " + sim.PaybackMonths(card)
                                  + " months, profit over 24 months " + One(sim.ProfitOver(card, 24) / 1e6) + " M");
        }

        /// <summary>The first card of the mainstream gaming segment that came out in the 3D era (1996 on), or null.</summary>
        private static SimCard FirstMainstreamCardOfThe3dEra(GpuSimulation sim)
        {
            return sim.CardLog.FirstOrDefault(c => c.SegmentId == "gaming.mid" && c.Released.Year >= 1996);
        }

        [Test]
        public void BalanceReference()
        {
            foreach (uint seed in new[] { 42u, 43u })
            {
                GpuSimulation sim = Balance(seed);
                string run = "seed " + seed + ": ";
                Assert.True(sim.Violations.Count == 0, run + "invariants of the run:\n  " + Join(sim.Violations));
                Assert.True(sim.World.Warnings.Count == 0, run + "the services warned during the run:\n  " + Join(sim.World.Warnings));
                if (seed == 42u) PrintBalance(sim);

                // 1. by 2005 the cards bring 15-45 % of the whole revenue of the player
                double part = GpuPartOfRevenue(sim, 2005);
                Console.WriteLine("  bal   " + run + "in 2005 the cards are " + One(100 * part) + " % of the revenue (CPU share 10 %: " + One(100 * GpuPartOfRevenue(sim, 2005, 0.10)) + " %, 20 %: "
                                  + One(100 * GpuPartOfRevenue(sim, 2005, 0.20)) + " %, 30 %: " + One(100 * GpuPartOfRevenue(sim, 2005, 0.30)) + " %)");
                Assert.True(part >= 0.15 && part <= 0.45, run + "NFR-7: in 2005 the revenue of the cards is 15-45 % of the whole: " + One(100 * part) + " %");
                Assert.True(GpuPartOfRevenue(sim, 2005, 0.20) >= 0.15, run + "...and still 15 % when the CPUs of the player hold a fifth of the CPU money");

                // 2. the others hold at least 15 % of the market before 2000, on every day of the run
                Console.WriteLine("  bal   " + run + "the others keep at least " + One(sim.MinBackgroundShareBefore2000 * 100f) + " % before 2000 (lowest on "
                                  + sim.MinBackgroundShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")");
                Assert.True(sim.MinBackgroundShareBefore2000 >= 0.15f, run + "NFR-7: the others hold at least 15 % until 2000: " + One(sim.MinBackgroundShareBefore2000 * 100f) + " % on "
                            + sim.MinBackgroundShareDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                // 3. no vendor holds more than 70 % of a segment for more than three years (the player's cards are in the totals, so this is the stricter reading of the mark)
                string who;
                int months = sim.LongestMonthsAbove(0.7f, out who);
                Console.WriteLine("  bal   " + run + "the longest run of a vendor over 70 % of a segment: " + months + " months (" + who + ")");
                Assert.True(months <= 36, run + "NFR-7: " + months + " months over 70 %: " + who);

                // 4. the first mainstream card of the 3D era pays back its development within 24 months at a price not above the budget of its segment
                SimCard first = FirstMainstreamCardOfThe3dEra(sim);
                Assert.NotNull(first, run + "the bot released a mainstream card in the 3D era");
                GpuProductSave product = sim.State.Data.products.Find(p => p.id == first.Id);
                Assert.True(product.stats.features.Contains("3d"), run + "it is a 3D card: " + string.Join(",", product.stats.features.ToArray()));
                int payback = sim.PaybackMonths(first);
                Console.WriteLine("  bal   " + run + "the first mainstream 3D card, " + first.Name + " of " + first.Released.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ": development " + One(first.DevCost / 1e6)
                                  + " M, price " + first.Price.ToString("0", CultureInfo.InvariantCulture) + " of budget " + first.Budget.ToString("0", CultureInfo.InvariantCulture) + ", pays back in " + payback + " months");
                Assert.True(first.Price <= first.Budget, run + "NFR-7: the price is not above the budget: " + first.Price + " > " + first.Budget);
                Assert.True(payback >= 1 && payback <= 24, run + "NFR-7: the development pays back within 24 months: " + payback);
            }
        }

        [Test]
        public void BalanceReference_TheBotRunsALineup_AndTheOthersAreTheNoNameCards()
        {
            // what the reference is: a lineup of three gaming segments from 1996, one card per segment per generation, the old generation taken off the market
            GpuSimulation sim = Balance(42);
            Assert.True(sim.CardsReleased >= 30, "a lineup over fifteen years: " + sim.CardsReleased);
            Assert.True(sim.CardLog[0].Released.Year == 1996, "the first cards are the 3D cards of 1996, not a 2D card of 1995: " + sim.CardLog[0].Released);
            foreach (SimCard card in sim.CardLog.Take(3))
                Assert.True(sim.State.Data.products.Find(p => p.id == card.Id).stats.features.Contains("3d"), card.Name + " has 3D");
            Assert.True(sim.CardLog.All(c => c.Price <= c.Budget + 0.01f), "every card is priced at 0.9 of the budget of its segment");
            Assert.True(sim.PlayerCardsOnSale() <= 4, "one generation of each segment is on sale: " + sim.PlayerCardsOnSale());
            Assert.True(sim.MaxReserved <= 120 - 40, "with \"CPU first\" the cards never took the lines of the CPUs: " + sim.MaxReserved);
            Assert.True(sim.MaxPlayerShare < 0.6f, "and the player does not own the market: " + sim.MaxPlayerShare);
        }

        // ---- the draft numbers of the console orders (FR-19, F35) on the reference ----

        /// <summary>The mainstream card the reference player has on sale on 1 January of a year and what a console order for it would be (F35 with the numbers of <c>balance.console</c>).</summary>
        private sealed class ConsoleSnapshot
        {
            public int Year;
            public string Card;
            public float Budget, UnitCost, Price;
            public int UnitsPerDay, LinesNeeded, LinesOfTheCards;
            public double RevenueDay, ProfitDay;
        }

        private static readonly Dictionary<GpuSimulation, List<ConsoleSnapshot>> ConsoleSnapshots = new Dictionary<GpuSimulation, List<ConsoleSnapshot>>();

        private static void TakeConsoleSnapshot(GpuSimulation sim)
        {
            DateTime date = sim.Game.Date;
            if (date.Month != 1 || (date.Year != 1998 && date.Year != 2002 && date.Year != 2005 && date.Year != 2008)) return;
            GpuBalanceDto.ConsoleBlock rules = sim.World.Content.Balance.console;
            GpuSegmentDto seg = sim.World.Content.Segment(rules.segment);
            SimCard made = sim.CardLog.LastOrDefault(c => c.SegmentId == rules.segment && c.Released < date);   // the newest card the bot made for the segment of the order
            GpuProductSave card = made == null ? null : sim.State.Data.products.Find(p => p.id == made.Id && p.status == GpuState.StatusSelling);
            if (seg == null || card == null) return;

            float budget = sim.World.Market.BudgetOf(seg, date.Year);
            float unitCost = sim.World.Production.UnitCostOf(card.id);
            float buyers = sim.World.Market.DailyBuyersOf(seg, date.Year);
            int units = (int)Math.Floor(rules.unitsShare * buyers);
            float price = Math.Max(rules.priceFloorMarkup * unitCost, rules.priceFactor * budget);
            int dpw = GpuDesignMath.DiesPerWaferPort(card.die, sim.Game.Wafer, sim.Game.Yield);
            float good = Math.Max(0.01f, 1f - card.stats.failureRate);
            int lines = dpw > 0 ? (int)Math.Ceiling(units * 30.0 / (GpuDesignMath.WafersPerLine * dpw * good)) : int.MaxValue;
            List<ConsoleSnapshot> list;
            if (!ConsoleSnapshots.TryGetValue(sim, out list)) ConsoleSnapshots[sim] = list = new List<ConsoleSnapshot>();
            list.Add(new ConsoleSnapshot
            {
                Year = date.Year, Card = card.name, Budget = budget, UnitCost = unitCost, Price = price, UnitsPerDay = units, LinesNeeded = lines,
                LinesOfTheCards = sim.Game.Factory.CapacityLines - sim.Game.Factory.CpuLines, RevenueDay = (double)price * units, ProfitDay = ((double)price - unitCost) * units,
            });
        }

        [Test]
        public void BalanceReference_TheDraftConsoleOrder_FitsTheFactoryAndPaysWell()
        {
            GpuSimulation sim = Balance(42);
            List<ConsoleSnapshot> list;
            Assert.True(ConsoleSnapshots.TryGetValue(sim, out list) && list.Count >= 3, "the reference has a mainstream 3D card on 1 January of 1998, 2002, 2005 and 2008");
            GpuBalanceDto.ConsoleBlock rules = sim.World.Content.Balance.console;
            foreach (ConsoleSnapshot snap in list)
            {
                Console.WriteLine("  bal   console order " + snap.Year + " for " + snap.Card + ": " + snap.UnitsPerDay + " a day at " + snap.Price.ToString("0", CultureInfo.InvariantCulture) + " (cost "
                                  + snap.UnitCost.ToString("0", CultureInfo.InvariantCulture) + ", budget " + snap.Budget.ToString("0", CultureInfo.InvariantCulture) + "), " + snap.LinesNeeded + " lines of the "
                                  + snap.LinesOfTheCards + " the cards may take, the profit " + One(snap.ProfitDay * 365 / 1e6) + " M a year");
                Assert.True(snap.Price >= rules.priceFloorMarkup * snap.UnitCost - 0.01f, snap.Year + ": the price is above the floor over the cost");
                Assert.True(snap.ProfitDay > 0d, snap.Year + ": an order is a profit");
                Assert.True(snap.LinesNeeded <= snap.LinesOfTheCards / 2, snap.Year + ": the order takes at most half of the lines the cards may take: " + snap.LinesNeeded + " of " + snap.LinesOfTheCards);
            }
            ConsoleSnapshot first = list[0];
            Assert.True(first.Year == 1998 && first.UnitsPerDay >= 1500 && first.UnitsPerDay <= 3000,
                "the order of 1998 is about the 2000 a day of AC-19.3: " + first.UnitsPerDay);
        }
    }
}
