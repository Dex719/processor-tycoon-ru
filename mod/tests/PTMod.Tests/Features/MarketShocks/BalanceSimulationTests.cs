using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Companies;
using PTMod.Features.MarketShocks;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-015 (FR-2, FR-9, FR-10, NFR-5): the balance of the pack on 50 game years, over the shipped <c>config.json</c> and
    /// <c>shocks.json</c>, played by <see cref="ShockSimulator"/> (the rules of the lottery, the pace, the targets and the channel limits of the event
    /// system). 20 runs of 50 years from 1971 (the first day of the game) unless a test says otherwise; the seeds are fixed, so a run is always the same. The numbers a reader of
    /// the "Evidence" of TSK-015 wants are printed by <see cref="Report_PrintsTheNumbersOfTheEvidence"/>.
    /// </summary>
    public class BalanceSimulationTests
    {
        private static readonly string[] Categories = { "ms.supply", "ms.industry", "ms.price", "ms.demand", "ms.trade" };

        // FR-10.1: strikes a year on the whole industry at "Обычно".
        private static readonly double[] Expected = { 1.3, 0.4, 0.7, 0.4, 1.0 };

        private const int Years = 50;
        private const int Runs = 20;

        // ---- helpers ----

        internal static List<ShockSimulator> Many(int runs, int years, Action<SimSetup> tune = null, bool track = false, int firstSeed = 1)
        {
            var list = new List<ShockSimulator>(runs);
            for (int i = 0; i < runs; i++)
            {
                var setup = new SimSetup { Seed = firstSeed + i, TrackChannels = track };
                if (tune != null) tune(setup);
                var sim = new ShockSimulator(setup);
                sim.RunYears(years);
                list.Add(sim);
            }
            return list;
        }

        private static readonly Dictionary<float, List<ShockSimulator>> BySeverity = new Dictionary<float, List<ShockSimulator>>();
        private static List<ShockSimulator> _share;

        /// <summary>The 20 runs of 50 years at an intensity, with the channels sampled every day; played once for all the tests of the class.</summary>
        internal static List<ShockSimulator> At(float severity)
        {
            List<ShockSimulator> sims;
            if (!BySeverity.TryGetValue(severity, out sims)) BySeverity[severity] = sims = Many(Runs, Years, setup => setup.Severity = severity, true);
            return sims;
        }

        /// <summary>200 runs of 50 years at the normal intensity without sampling: about 13 000 incidents of ms.supply (AC-2.1 asks for 10 000).</summary>
        internal static List<ShockSimulator> Share()
        {
            return _share ?? (_share = Many(200, Years));
        }

        /// <summary>Strikes a year of the category that the lottery chose (the follow-up blows of a warning and the two cycles are not lottery draws).</summary>
        private static double Rate(List<ShockSimulator> sims, string category, int years)
        {
            double total = 0;
            foreach (ShockSimulator sim in sims) total += sim.CountFirings(category, "Lottery");
            return total / sims.Count / years;
        }

        private static string Num(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        // ---- AC-10.1: the pace of the categories ----

        [Test]
        public void Frequencies_50Years_20Runs_AreWithin25PercentOfFr101()
        {
            List<ShockSimulator> sims = At(1f);
            for (int i = 0; i < Categories.Length; i++)
            {
                double rate = Rate(sims, Categories[i], Years);
                Assert.InRange(rate, Expected[i] * 0.75, Expected[i] * 1.25,
                    Categories[i] + ": " + Num(rate) + " strikes a year, FR-10.1 says " + Num(Expected[i]) + " (+-25 %)");
            }
        }

        [Test]
        public void Frequencies_StayInTheirBand_AlsoForTheMatureThirtyYears_1990To2020()
        {
            // AC-10.1 speaks of 30 years. The years before 1990 have fewer shocks to draw from (the date conditions), so the band is checked on the last
            // 30 of the 50 as well, with the same bounds. The upper bound of the price (0.875) is the one the model comes nearest to (about 0.86).
            List<ShockSimulator> sims = At(1f);
            for (int i = 0; i < Categories.Length; i++)
            {
                double total = 0;
                foreach (ShockSimulator sim in sims)
                {
                    total += sim.CountFirings(Categories[i], sim.DayOf(new DateTime(1990, 1, 1)), sim.DayOf(new DateTime(2020, 1, 1)), "Lottery");
                }

                double rate = total / sims.Count / 30.0;
                Assert.InRange(rate, Expected[i] * 0.75, Expected[i] * 1.25,
                    Categories[i] + " 1990-2020: " + Num(rate) + " a year, FR-10.1 says " + Num(Expected[i]));
            }
        }

        [Test]
        public void IndustryShocks_AreCappedByTheirCooldowns_NotByTheLottery()
        {
            // Why the chance of ms.industry is 0.0035 and not the 0.0018 of the table of design.md: the three shocks wait 10, 6 and 6 years between two
            // of their own (3650, 2190, 2190 days), so the category cannot strike more often than 1/10 + 1/6 + 1/6 = 0.43 a year, whatever the lottery.
            // The chance of the table (nominal 0.40 a year) used only 0.28 of that; the shipped one uses 0.34.
            List<ShockSimulator> sims = At(1f);
            double rate = Rate(sims, "ms.industry", Years);
            Assert.True(rate <= 365.0 * (1.0 / 3650 + 1.0 / 2190 + 1.0 / 2190) + 1e-9, "the cooldowns cap the category: " + Num(rate));

            Assert.True(rate > 0.30, "the category strikes " + Num(rate) + " a year; the 0.0018 of the table gave 0.28");
        }

        [Test]
        public void Cycles_RunWithoutAGap_MemoryEveryFourYears_SiliconEveryFiveYears()
        {
            ShockSimulator sim = At(1f)[0];
            AssertCycle(sim, "ms.memory_cycle", new DateTime(1976, 1, 1), 1461);
            AssertCycle(sim, "ms.silicon_cycle", new DateTime(1980, 1, 1), 1826);
        }

        private static void AssertCycle(ShockSimulator sim, string id, DateTime first, int spanDays)
        {
            var days = new List<int>();
            foreach (SimFiring firing in sim.Firings) if (firing.EventId == id) days.Add(firing.Day);

            Assert.True(days.Count >= 8, id + " fired " + days.Count + " times in 50 years");
            Assert.Equal(sim.DayOf(first), days[0], id + ": the first wave starts in the first month the condition is true");
            for (int i = 1; i < days.Count; i++)
            {
                int gap = days[i] - days[i - 1];
                // The wave lasts its period (1460 / 1825 days); the next one starts at the first of a month at or after the end of the previous one.
                Assert.InRange(gap, spanDays - 2, spanDays + 31, id + ": the gap between waves " + i + " and " + (i + 1));
            }
            Assert.Equal("Condition", FirstOrigin(sim, id), id + " starts by its Condition trigger, not by the lottery");
        }

        private static string FirstOrigin(ShockSimulator sim, string id)
        {
            foreach (SimFiring firing in sim.Firings) if (firing.EventId == id) return firing.Origin;
            return null;
        }

        [Test]
        public void Frequency_Setting_ScalesTheLottery_HalfAndOneAndAHalf()
        {
            double half = Rate(Many(Runs, Years, s => s.PackFrequency = 0.5f), "ms.supply", Years);
            double one = Rate(Many(Runs, Years, s => s.PackFrequency = 1f), "ms.supply", Years);
            double more = Rate(Many(Runs, Years, s => s.PackFrequency = 1.5f), "ms.supply", Years);

            Assert.True(half < one && one < more, "0.5 < 1 < 1.5 gives " + Num(half) + " < " + Num(one) + " < " + Num(more));
            Assert.InRange(half / one, 0.5, 0.65, "half the chance, a little less than half the strikes' loss to the pause between two");
            Assert.InRange(more / one, 1.2, 1.5, "one and a half the chance");
        }

        // ---- AC-2.1, AC-9.1: whom the incidents hit ----

        [Test]
        public void PlayerShare_OfTheIncidents_IsItsShareOfCapacity_Within2Points()
        {
            // 30 of 150 lines: 20 %. 200 runs of 50 years give about 13 000 incidents (AC-2.1 asks for 10 000).
            List<ShockSimulator> sims = Share();
            int incidents = 0, player = 0;
            foreach (ShockSimulator sim in sims)
            {
                foreach (SimFiring firing in sim.Firings)
                {
                    if (firing.Category != "ms.supply") continue;
                    incidents++;
                    if (firing.Targets[0] == CompanyKey.Player) player++;
                }
            }

            Assert.True(incidents >= 10000, "incidents: " + incidents);
            double share = (double)player / incidents;
            Assert.InRange(share, 0.18, 0.22, "the player holds 20 % of the lines and was hit by " + Num(share * 100) + " % of " + incidents + " incidents");
        }

        [Test]
        public void EveryCompany_IsHitInProportionToItsLines_Within2Points()
        {
            List<ShockSimulator> sims = Share();
            var hits = new Dictionary<string, int>();
            int incidents = 0;
            foreach (ShockSimulator sim in sims)
            {
                foreach (SimFiring firing in sim.Firings)
                {
                    if (firing.Category != "ms.supply") continue;
                    incidents++;
                    int count;
                    hits.TryGetValue(firing.Targets[0], out count);
                    hits[firing.Targets[0]] = count + 1;
                }
            }

            int lines = 0;
            foreach (SimCompany company in sims[0].Companies) lines += company.Lines;
            foreach (SimCompany company in sims[0].Companies)
            {
                int count;
                hits.TryGetValue(company.Key, out count);
                double share = (double)count / incidents;
                double capacity = (double)company.Lines / lines;
                Assert.InRange(share, capacity - 0.02, capacity + 0.02, company.Key + ": " + Num(capacity * 100) + " % of the lines, " + Num(share * 100) + " % of the hits");
            }
        }

        private static double _smallShare;
        private static int _smallIncidents;

        /// <summary>A player with 5 of 100 lines among nine AI companies (30, 20, 12, 10, 8, 6, 4, 3 and 2 lines): the share of the incidents it got.</summary>
        private static double SmallPlayerShare(out int incidents)
        {
            if (_smallIncidents == 0)
            {
                Action<SimSetup> tune = s =>
                {
                    int[] lines = { 30, 20, 12, 10, 8, 6, 4, 3, 2 };
                    double[] shares = { 0.30, 0.20, 0.14, 0.11, 0.09, 0.06, 0.04, 0.03, 0.02 };
                    s.Companies = new List<SimCompany> { new SimCompany(CompanyKey.Player, 5, 0.01, 8000000) };
                    for (int i = 0; i < lines.Length; i++) s.Companies.Add(new SimCompany(CompanyKey.AiPrefix + (i + 1), lines[i], shares[i], shares[i] * 500000000));
                };

                int total = 0, player = 0;
                foreach (ShockSimulator sim in Many(200, Years, tune))
                {
                    foreach (SimFiring firing in sim.Firings)
                    {
                        if (firing.Category != "ms.supply") continue;
                        total++;
                        if (firing.Targets[0] == CompanyKey.Player) player++;
                    }
                }
                _smallShare = (double)player / total;
                _smallIncidents = total;
            }

            incidents = _smallIncidents;
            return _smallShare;
        }

        [Test]
        public void PlayerShare_FollowsAnotherDistributionOfLines_AFivePercentPlayer()
        {
            int incidents;
            double share = SmallPlayerShare(out incidents);
            Assert.InRange(share, 0.03, 0.07, "5 of 100 lines and " + Num(share * 100) + " % of " + incidents + " incidents");
        }

        [Test]
        public void ACompanyWithoutAFab_IsNeverHitByAFactoryShock()
        {
            Action<SimSetup> tune = s => s.Companies[3].Lines = 0;
            foreach (ShockSimulator sim in Many(10, Years, tune))
            {
                string fabless = sim.Setup.Companies[3].Key;
                foreach (SimFiring firing in sim.Firings)
                {
                    if (firing.Category == "ms.supply" || firing.EventId == "ms.earthquake" || firing.EventId == "ms.photoresist_shortage")
                        Assert.False(Array.IndexOf(firing.Targets, fabless) >= 0, firing.EventId + " hit " + fabless + ", which has no fab");
                }
            }
        }

        // ---- AC-10.2, AC-10.3: the limits of the channels ----

        [Test]
        public void Channels_NeverLeaveTheirLimits_OnAnyIntensity()
        {
            foreach (float severity in new[] { 0.5f, 1f, 1.5f, 2f })
            {
                foreach (ShockSimulator sim in At(severity))
                {
                    foreach (KeyValuePair<string, SimChannelStat> pair in sim.Channels)
                    {
                        float min, max;
                        sim.LimitsOf(pair.Key, out min, out max);
                        Assert.True(pair.Value.Min >= min - 1e-6, "intensity " + severity + ", " + pair.Key + " fell to " + Num(pair.Value.Min) + " (limit " + min + ")");
                        Assert.True(pair.Value.Max <= max + 1e-6, "intensity " + severity + ", " + pair.Key + " rose to " + Num(pair.Value.Max) + " (limit " + max + ")");
                    }
                }
            }
        }

        [Test]
        public void Chaos_OutputStaysAbove0_4_MemoryAndWafersBelow2_0()
        {
            List<ShockSimulator> sims = At(2f);
            double minOutput = 1, maxMemory = 1, maxWafer = 1, minDemand = 1, maxDemand = 1, maxUnit = 1, maxOperating = 1;
            foreach (ShockSimulator sim in sims)
            {
                SimChannelStat stat;
                if (sim.Channels.TryGetValue("production.output", out stat)) minOutput = Math.Min(minOutput, stat.Min);
                if (sim.Channels.TryGetValue("production.memoryCost", out stat)) maxMemory = Math.Max(maxMemory, stat.Max);
                if (sim.Channels.TryGetValue("production.waferCost", out stat)) maxWafer = Math.Max(maxWafer, stat.Max);
                if (sim.Channels.TryGetValue("production.unitCost", out stat)) maxUnit = Math.Max(maxUnit, stat.Max);
                if (sim.Channels.TryGetValue("factory.operatingCost", out stat)) maxOperating = Math.Max(maxOperating, stat.Max);
                foreach (string segment in new[] { "highend", "midrange", "lowend", "industries" })
                {
                    if (!sim.Channels.TryGetValue("market.demand." + segment, out stat)) continue;
                    minDemand = Math.Min(minDemand, stat.Min);
                    maxDemand = Math.Max(maxDemand, stat.Max);
                }
            }

            Assert.True(minOutput >= 0.4 - 1e-6, "output fell to " + Num(minOutput));
            Assert.True(maxMemory <= 2.0 + 1e-6, "memory rose to " + Num(maxMemory));
            Assert.True(maxWafer <= 2.0 + 1e-6, "wafers rose to " + Num(maxWafer));
            Assert.True(minDemand >= 0.6 - 1e-6 && maxDemand <= 1.6 + 1e-6, "demand " + Num(minDemand) + ".." + Num(maxDemand));
            Assert.True(maxUnit <= 1.6 + 1e-6, "unit cost rose to " + Num(maxUnit));
            Assert.True(maxOperating <= 1.8 + 1e-6, "operating cost rose to " + Num(maxOperating));
        }

        [Test]
        public void Chaos_ReallyPushesAgainstTheLimits_TheLimitsDoTheWork()
        {
            // On "Хаос" the shocks as resolved are cut by the limits (AC-7.4) and their sum is cut by the aggregate (AC-10.2): without the limits of
            // config.json the output would fall to about 0.25 and the memory price would pass 2.5.
            List<ShockSimulator> sims = At(2f);
            bool cutOutput = false, cutMemory = false;
            foreach (ShockSimulator sim in sims)
            {
                SimChannelStat stat;
                if (sim.Channels.TryGetValue("production.output", out stat) && stat.ClampedDays > 0) cutOutput = true;
                if (sim.Channels.TryGetValue("production.memoryCost", out stat) && stat.ClampedDays > 0) cutMemory = true;
            }

            Assert.True(cutOutput || cutMemory, "at intensity 2 nothing was ever cut by a limit: the check above proves nothing");
        }

        [Test]
        public void Ac102_AnEarthquakeOnTheTopOfASpike_GivesTwoPointZero_AndTheCutIsSeen()
        {
            ShockSimulator sim = Quiet(1995);
            Assert.True(sim.Fire("ms.dram_spike", "player"), "the spike fired");
            sim.RunDays(40);
            Assert.True(sim.Fire("ms.earthquake"), "the earthquake fired");
            sim.RunDays(31);

            float raw;
            bool clamped;
            float value = sim.ChannelValue("production.memoryCost", "player", out raw, out clamped);
            Assert.Near(1.6 * 1.3, raw, 1e-5, "both on the plateau: 1.6 x 1.3 = 2.08");
            Assert.Near(2.0, value, 1e-6, "the channel gives 2.0");
            Assert.True(clamped, "and says it was cut");
        }

        [Test]
        public void Ac103_Chaos_AFireCutsOutputTo0_4_AndTheSumsAreDoubled()
        {
            Func<float, ShockSimulator> fire = severity =>
            {
                ShockSimulator sim = Quiet(1995, severity, true);
                sim.RunDays(100);   // es.hasRevenueHistory: the first three monthly reports
                Assert.True(sim.Fire("ms.fab_fire", "player"), "the fire fired");
                sim.RunDays(4);   // the letter's 3 days run out and the standard repair starts
                return sim;
            };

            ShockSimulator normal = fire(1f);
            ShockSimulator chaos = fire(2f);

            Assert.Near(0.55, normal.ChannelValue("production.output", "player"), 1e-6, "normal: x0.55");
            Assert.Near(0.4, chaos.ChannelValue("production.output", "player"), 1e-6, "chaos: x0.1 cut to the 0.4 of the limit");

            SimResolved normalMoney = FirstOf(normal, "Money"), chaosMoney = FirstOf(chaos, "Money");
            Assert.True(normalMoney.Amount > 0, "the repair costs something");
            Assert.InRange(chaosMoney.Amount, normalMoney.Amount * 2 - 1000, normalMoney.Amount * 2 + 1000, "the sums are doubled");

            SimResolved chaosOutput = FirstOf(chaos, "FactoryOutput");
            Assert.True(chaosOutput.Clamped, "the effect is marked as cut");
            Assert.False(FirstOf(normal, "FactoryOutput").Clamped, "and is not marked at normal intensity");
        }

        private static SimResolved FirstOf(ShockSimulator sim, string type)
        {
            foreach (SimResolved resolved in sim.Resolved) if (resolved.Type == type && resolved.Hit) return resolved;
            Assert.Fail("no resolved " + type);
            return null;
        }

        internal static ShockSimulator Quiet(int year, float severity = 1f, bool keepResolved = false)
        {
            var setup = new SimSetup { Start = new DateTime(year, 1, 1), Severity = severity, KeepResolved = keepResolved, TrackChannels = true }.Quiet();
            return new ShockSimulator(setup);
        }

        // ---- NFR-5: one shock ----

        [Test]
        public void SingleShock_AtNormal_KeepsOutputAbove55Percent_AndCostsBelowPlus60()
        {
            string[] costs = { "production.unitCost", "production.memoryCost", "production.waferCost", "factory.operatingCost" };
            foreach (PlayerPolicy policy in new[] { PlayerPolicy.First, PlayerPolicy.Last })
            {
                var seen = new List<string>();
                foreach (string id in ShockSimulatorIds())
                {
                    ShockSimulator sim = Quiet(1995);
                    sim.Setup.Player = policy;
                    string company = FiringCompany(sim, id);
                    if (!sim.Fire(id, company, true)) continue;   // the event has nothing to strike here (a warning with no choice taken is not a shock)
                    sim.RunDays(3000);
                    seen.Add(id);

                    SimChannelStat stat;
                    if (sim.Channels.TryGetValue("production.output", out stat)) Assert.True(stat.Min >= 0.55 - 1e-6, id + " (" + policy + "): output fell to " + Num(stat.Min));
                    foreach (string channel in costs)
                    {
                        if (sim.Channels.TryGetValue(channel, out stat)) Assert.True(stat.Max <= 1.6 + 1e-6, id + " (" + policy + "): " + channel + " rose to " + Num(stat.Max));
                    }
                }

                Assert.Equal(21, seen.Count, "the single shocks tried (" + policy + ")");
            }
        }

        private static IEnumerable<string> ShockSimulatorIds()
        {
            foreach (ShockSimulator.Def def in new ShockSimulator(new SimSetup { Start = new DateTime(1995, 1, 1) }).Defs) yield return def.Id;
        }

        private static string FiringCompany(ShockSimulator sim, string id)
        {
            return sim.DefOf(id).TargetType == "AllCompanies" ? null : CompanyKey.Player;
        }

        // ---- AC-10.4: the coordination with the historical events (K1) ----

        [Test]
        public void K1_NoGlobalRandomShockStarts_WhileAHistoricalEventRuns_AndTheyDoAfter()
        {
            string[] held =
            {
                "ms.earthquake", "ms.photoresist_shortage", "ms.logistics_crunch", "ms.dram_spike_warning", "ms.dram_glut", "ms.wafer_shortage", "ms.energy_crisis",
                "ms.recession_warning", "ms.pc_boom", "ms.server_boom", "ms.embedded_boom",
            };

            int inside = 0, outside = 0, otherInside = 0;
            for (int seed = 1; seed <= 30; seed++)
            {
                var setup = new SimSetup { Start = new DateTime(1985, 1, 1), Seed = seed, TrackChannels = false };
                // 1990 and 1991 are historical: four years of chance on either side, two years of silence in the middle.
                int from = (int)(new DateTime(1990, 1, 1) - setup.Start).TotalDays, to = (int)(new DateTime(1991, 12, 31) - setup.Start).TotalDays;
                setup.HistoryWindows.Add(new[] { from, to });
                var sim = new ShockSimulator(setup);
                sim.RunYears(10);

                foreach (SimFiring firing in sim.Firings)
                {
                    bool inWindow = firing.Day >= from && firing.Day <= to;
                    if (Array.IndexOf(held, firing.EventId) >= 0 && firing.Origin == "Lottery")
                    {
                        if (inWindow) inside++;
                        else outside++;
                    }
                    else if (inWindow && (firing.Category == "ms.supply" || firing.Category == "ms.trade"))
                    {
                        otherInside++;
                    }
                }
            }

            Assert.Equal(0, inside, "global random shocks that started inside the historical window");
            Assert.True(outside > 30, "the same shocks do start outside of it (" + outside + ")");
            Assert.True(otherInside > 30, "incidents at companies and trade shocks go on inside the window (" + otherInside + "): they are not part of K1");
        }

        [Test]
        public void K1_ASpikeThatTheWarningStartedBeforeTheWindow_IsNotCancelled()
        {
            // Coordination goes one way (FR-10 item 5): the blow of a warning that was given before the historical event is a Manual firing and K1 does not
            // touch it. 80 % of the forecasts come true, so about 16 of 20 warnings are followed by a blow that lands inside the window.
            int spikes = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var setup = new SimSetup { Start = new DateTime(1995, 1, 1), Seed = seed, TrackChannels = false }.Quiet();
                setup.HistoryWindows.Add(new[] { 5, 400 });
                var sim = new ShockSimulator(setup);
                Assert.True(sim.Fire("ms.dram_spike_warning", null, true), "the warning fired");
                sim.RunDays(30);
                foreach (SimFiring firing in sim.Firings) if (firing.EventId == "ms.dram_spike" && firing.Day >= 5) spikes++;
            }
            Assert.InRange(spikes, 10, 20, "blows of the 20 warnings that landed inside the historical window");
        }

        // ---- the news and the report ----

        [Test]
        public void Report_PrintsTheNumbersOfTheEvidence()
        {
            var sb = new StringBuilder();
            sb.AppendLine("market-shocks balance report (" + Runs + " runs x " + Years + " years from 1971, intensity 1, frequency 1)");

            List<ShockSimulator> sims = At(1f);
            for (int i = 0; i < Categories.Length; i++)
                sb.AppendLine("  " + Categories[i] + ": " + Num(Rate(sims, Categories[i], Years)) + " a year (FR-10.1: " + Num(Expected[i]) + ")");
            sb.AppendLine("  ms.cycle: " + Num((double)Count(sims, "ms.cycle") / Runs / Years) + " a year (memory every 4 years from 1976, silicon every 5 from 1980)");

            int incidents = 0, player = 0, news = 0, journal = 0;
            List<ShockSimulator> many = Share();
            foreach (ShockSimulator sim in many)
            {
                foreach (SimFiring firing in sim.Firings)
                {
                    if (firing.Category != "ms.supply") continue;
                    incidents++;
                    if (firing.Targets[0] == CompanyKey.Player) player++;
                }
                news += sim.News.Count;
                journal += sim.Journal.Count;
            }
            sb.AppendLine("  the player's share of the incidents of ms.supply: " + Num(100.0 * player / incidents) + " % of " + incidents + " (30 of 150 lines = 20 %)");
            int smallIncidents;
            double small = SmallPlayerShare(out smallIncidents);
            sb.AppendLine("  a player with 5 of 100 lines (nine AI companies): " + Num(100.0 * small) + " % of " + smallIncidents + " incidents");
            sb.AppendLine("  news to the player about rivals: " + news + " of " + journal + " journal entries in 200 runs (top " + ShockRegistration.RivalNewsTopN + " only)");

            foreach (float severity in new[] { 0.5f, 1f, 1.5f, 2f })
                sb.AppendLine("  intensity " + severity.ToString("0.0", CultureInfo.InvariantCulture) + ": " + Extremes(At(severity)));

            Console.WriteLine(sb.ToString());
        }

        private static int Count(List<ShockSimulator> sims, string category)
        {
            int total = 0;
            foreach (ShockSimulator sim in sims) total += sim.CountFirings(category);
            return total;
        }

        private static string Extremes(List<ShockSimulator> sims)
        {
            string[] channels =
            {
                "production.output", "production.unitCost", "production.memoryCost", "production.waferCost", "factory.operatingCost",
                "market.demand.highend", "market.demand.midrange", "market.demand.lowend", "market.demand.industries",
            };
            var parts = new List<string>();
            foreach (string channel in channels)
            {
                double min = 1, max = 1, rawMin = 1, rawMax = 1;
                foreach (ShockSimulator sim in sims)
                {
                    SimChannelStat stat;
                    if (!sim.Channels.TryGetValue(channel, out stat)) continue;
                    min = Math.Min(min, stat.Min);
                    max = Math.Max(max, stat.Max);
                    rawMin = Math.Min(rawMin, stat.RawMin);
                    rawMax = Math.Max(rawMax, stat.RawMax);
                }
                parts.Add(channel.Replace("production.", string.Empty).Replace("market.demand.", "demand.").Replace("factory.", string.Empty) + " " + Num(min) + ".." + Num(max)
                          + (rawMin < min - 1e-9 || rawMax > max + 1e-9 ? " (raw " + Num(rawMin) + ".." + Num(rawMax) + ")" : string.Empty));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
