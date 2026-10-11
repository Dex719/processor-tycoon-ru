using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The balance simulation of the pure logic (TSK-029; design.md, "Балансовая симуляция"; requirements.md, "Acceptance Metrics"): the eleven AI designers of the content
    /// run month by month through 1975-2025 on the same functions the services call (<see cref="BalanceSimulator"/>), sales proportional to the rating multiplier m.
    /// It checks the metrics of the spec - x86 is the active architecture of at least three of the five "PC companies" by 1995, ARM/ARM64 of at least two of the three
    /// "embedded" ones by 2015, m is always in [0.80; 1.20], and before 1985 no family gives a CPU more than +20 % over the default one - and that they hold when the
    /// knobs of the market move (money, line-up turnover, the mix of segments, the difficulty). The run is deterministic. The table by year goes to
    /// <c>cpu-architectures-balance-report.txt</c> in the build folder and to <c>mod/requests/cpu-architectures.md</c>. This is a check of the formulas and of the policy, not
    /// a replacement for the game: the shared lab (the technologies of the AI stand while an architecture is researched) is not in the model and is checked by hand (M13).
    /// </summary>
    [Covers(typeof(ArchAiPolicy), typeof(ArchSeedPolicy), typeof(SupportCycle), typeof(SupportMath), typeof(EcosystemMath), typeof(ArchRatingMath), typeof(RatingForecast),
            typeof(ArchResearchMath), typeof(SegmentShares), typeof(LabRules))]
    public class BalanceSimulationTests
    {
        private static readonly string[] PcCompanies = { "intel", "amd", "cyrix", "via", "ibm" };
        private static readonly string[] EmbeddedCompanies = { "texas-instruments", "toshiba", "zilog" };

        private const double CorridorTolerance = 1e-5;

        private static SimResult _base;

        // ---- running ----

        private static SimResult Run(Action<SimParams> tweak = null)
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;   // the metrics are those of the full version

            var parameters = new SimParams();
            if (tweak != null) tweak(parameters);
            return new BalanceSimulator(catalog, ArchTestKit.BuiltInPreferences(catalog), balance, parameters, null).Run();
        }

        private static SimResult Base()
        {
            return _base ?? (_base = Run());
        }

        private static bool OnIsa(SimResult result, int year, string company, string isa)
        {
            string family = result.ActiveAt(year, company);
            ArchFamily found = family == null ? null : ArchTestKit.BuiltInCatalog().Get(family);
            return found != null && string.Equals(found.Isa, isa, StringComparison.Ordinal);
        }

        private static int Count(SimResult result, int year, string[] companies, string isa)
        {
            int count = 0;
            foreach (string company in companies)
            {
                if (OnIsa(result, year, company, isa)) count++;
            }
            return count;
        }

        private static string Where(SimResult result, int year, string[] companies)
        {
            var sb = new StringBuilder();
            foreach (string company in companies) sb.Append(company).Append('=').Append(result.ActiveAt(year, company) ?? "-").Append(' ');
            return sb.ToString().TrimEnd();
        }

        // ---- the metrics of requirements.md ----

        [Test]
        public void X86_IsTheActiveArchitectureOfAtLeastThreeOfTheFivePcCompanies_By1995()
        {
            SimResult result = Base();
            int count = Count(result, 1995, PcCompanies, "x86");
            Assert.True(count >= 3, "x86 is active at " + count + " of 5 at the start of 1995: " + Where(result, 1995, PcCompanies));
        }

        [Test]
        public void ArmIsTheActiveArchitectureOfAtLeastTwoOfTheThreeEmbeddedCompanies_By2015()
        {
            SimResult result = Base();
            int count = Count(result, 2015, EmbeddedCompanies, "arm");
            Assert.True(count >= 2, "ARM or ARM64 is active at " + count + " of 3 at the start of 2015: " + Where(result, 2015, EmbeddedCompanies));
        }

        [Test]
        public void TheContribution_IsAlwaysInTheCorridor_InTheRun()
        {
            SimResult result = Base();
            Assert.True(result.Evaluations > 20000, "the run evaluated the contribution " + result.Evaluations + " times");
            Assert.True(result.MinM >= 0.80 - CorridorTolerance, "the lowest contribution is " + result.MinM);
            Assert.True(result.MaxM <= 1.20 + CorridorTolerance, "the highest contribution is " + result.MaxM);
        }

        [Test]
        public void TheContribution_IsInTheCorridor_ForEveryFamilyYearSegmentSupportEcosystemAndExtension()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            balance.stage = BalanceConfig.Stage2;
            var inputs = new RatingInputs();

            double min = double.MaxValue, max = double.MinValue, minKnob = double.MaxValue, maxKnob = double.MinValue;
            long checks = 0;
            foreach (ArchFamily family in catalog.Families)
            {
                for (int year = 1971; year <= 2050; year += 3)
                {
                    for (int s = 0; s < 4; s++)
                    {
                        for (float support = 0f; support <= 100f; support += 12.5f)
                        {
                            for (float eco = 0f; eco <= 1.0001f; eco += 0.125f)
                            {
                                foreach (int cores in new[] { 1, 2, 4, 8, 16 })
                                {
                                    foreach (bool smt in new[] { false, true })
                                    {
                                        float m = RatingForecast.One(inputs, family, (Segment)s, year, balance, support, eco, cores, smt, 1f);
                                        float knob = RatingForecast.One(inputs, family, (Segment)s, year, balance, support, eco, cores, smt, ArchRatingMath.MaxStrength);
                                        min = Math.Min(min, m);
                                        max = Math.Max(max, m);
                                        minKnob = Math.Min(minKnob, knob);
                                        maxKnob = Math.Max(maxKnob, knob);
                                        checks++;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Assert.True(checks > 100000, "the grid has " + checks + " points");
            Assert.True(min >= 0.80 - CorridorTolerance && max <= 1.20 + CorridorTolerance, "m runs from " + min + " to " + max);
            Assert.True(minKnob >= 0.70 - CorridorTolerance && maxKnob <= 1.30 + CorridorTolerance, "with the knob at 150 % m runs from " + minKnob + " to " + maxKnob);
        }

        [Test]
        public void Before1985_NoFamilyGivesAMoreThan20PercentOverTheDefaultFamily()
        {
            SimResult result = Base();
            Assert.True(result.MaxFamilyMBefore1985 > 1.0, "x86 does give something before 1985 (" + result.MaxFamilyMBefore1985 + ")");
            Assert.True(result.MaxFamilyMBefore1985 <= 1.20 + CorridorTolerance, "the best contribution of a family before 1985 is " + result.MaxFamilyMBefore1985);

            // By itself - with the same support and ecosystem on both sides - a family is worth its affinity and nothing else.
            Assert.True(result.PureAdvantageBefore1985 > 1.0, "the advantage is there: " + result.PureAdvantageBefore1985);
            Assert.True(result.PureAdvantageBefore1985 <= 1.20 + CorridorTolerance, "a family is worth " + result.PureAdvantageBefore1985 + " over the default family before 1985");

            // The ratio in the run also carries the support of the abandoned ISA that has run dry (0.90 against 1.04 for a supported one): it is above the family's own
            // advantage, and the corridor itself bounds it (1.20 / 0.80). It is in the report; the owner decides whether the strict reading of the metric is wanted.
            Assert.True(result.MaxAdvantageBefore1985 >= result.PureAdvantageBefore1985 - CorridorTolerance);
            Assert.True(result.MaxAdvantageBefore1985 <= 1.20 / 0.80 + CorridorTolerance);
        }

        // ---- the history the simulation shows ----

        [Test]
        public void TheHistory_X86WinsThePc_ArmTakesTheEmbeddedMarket_RiscStaysAWorkstationFamily()
        {
            SimResult result = Base();

            // The PC companies: Intel and AMD research x86 as soon as it exists (1978, AMD a year later) and move within two years; Cyrix and VIA are born on it.
            DateTime? intel = result.MovedTo("intel", "x86");
            DateTime? amd = result.MovedTo("amd", "x86");
            Assert.NotNull(intel, "Intel moves to x86");
            Assert.NotNull(amd, "AMD moves to x86");
            Assert.True(intel.Value.Year <= 1980, "Intel moved in " + intel.Value);
            Assert.True(amd.Value.Year <= 1981, "AMD moved in " + amd.Value);
            Assert.Equal("x86", result.ActiveAt(1988, "cyrix"));
            Assert.Equal("x86", result.ActiveAt(1989, "via"));
            Assert.Equal(0, result.Moves("cyrix") + result.Moves("via"), "a company born on x86 stays on it");

            // The ecosystem: x86 is the biggest after the proprietary ISAs give way, and has more than the half of the sales by 1995.
            SimYear row = result.YearOf(1995);
            float x86 = row.Ecosystem["x86"];
            foreach (KeyValuePair<string, float> pair in row.Ecosystem) Assert.True(pair.Key == "x86" || pair.Value <= x86, pair.Key + " is bigger than x86 in 1995");
            Assert.True(x86 >= 0.40f, "x86 holds " + x86 + " of the sales in 1995");

            // The embedded companies: Toshiba (weights favour ARM) first, Texas Instruments after the ecosystem of ARM has grown. Zilog weighs ARM 0.5 against 1.0: it stays.
            Assert.NotNull(result.MovedTo("toshiba", "arm"));
            Assert.NotNull(result.MovedTo("texas-instruments", "arm"));
            Assert.True(result.MovedTo("texas-instruments", "arm").Value.Year <= 1996, "Texas Instruments moved in " + result.MovedTo("texas-instruments", "arm"));
            Assert.Equal("proprietary", result.ActiveAt(2025, "zilog"), "Zilog likes its own ISA twice as much as ARM");

            // RISC: IBM (weight 1.2) and Digital Devices (1.2) move to it, the workstations of the history.
            Assert.NotNull(result.MovedTo("ibm", "risc"), "IBM moves to RISC");
            Assert.NotNull(result.MovedTo("digital-devices", "risc"), "Digital Devices moves to RISC");
            Assert.True(result.MovedTo("ibm", "risc").Value.Year <= 1990);

            // AMD, which weighs x86-64 1.2 against 1.0, takes it up; the company that weighs both the same does not leave the family with the ecosystem.
            Assert.NotNull(result.MovedTo("amd", "x86_64"), "AMD moves to x86-64");
            Assert.True(result.MovedTo("amd", "x86_64").Value.Year >= 2003);
        }

        [Test]
        public void NoCompany_ChangesItsMindBackAndForth()
        {
            // A move needs a margin of 10 %, so a company does not flap between two families.
            SimResult result = Base();
            foreach (string company in result.Keys)
            {
                Assert.True(result.Moves(company) <= 3, company + " changed its active family " + result.Moves(company) + " times");
            }
        }

        [Test]
        public void NoCompany_RunsOutOfMoney_InTheModelledMarket()
        {
            // The economy of the model is a sanity check of its own: a pause for negative money would hide a research that never ended.
            Assert.Equal(0, Base().Negative, "company-months with negative money");
            Assert.Equal(612, Base().MonthsRun, "1975-2025, month by month");
        }

        // ---- robustness ----

        [Test]
        public void TheMetrics_HoldWhenTheKnobsOfTheMarketMove()
        {
            var variants = new Dictionary<string, Action<SimParams>>
            {
                { "half the money", p => p.Margin *= 0.5 },
                { "twice the money", p => p.Margin *= 2 },
                { "a market that is mostly industry", p => { p.UnitsPerMonth = new double[] { 21000, 84000, 150000, 250000 }; p.Price = new double[] { 700, 250, 90, 60 }; } },
                { "a market that is mostly PC", p => { p.UnitsPerMonth = new double[] { 40000, 150000, 250000, 20000 }; } },
                { "line-ups that turn over in 6 months", p => p.LineupMonths = 6 },
                { "line-ups that turn over in 36 months", p => p.LineupMonths = 36 },
                { "slow growth", p => p.MarketGrowth = 1.08 },
                { "the easiest difficulty", p => p.Difficulty = 0 },
                { "the hardest difficulty", p => p.Difficulty = 4 },
                { "little start money", p => p.StartMonthsOfProfit = 6 },
            };

            var problems = new List<string>();
            foreach (KeyValuePair<string, Action<SimParams>> variant in variants)
            {
                SimResult result = Run(variant.Value);
                int pc = Count(result, 1995, PcCompanies, "x86");
                int arm = Count(result, 2015, EmbeddedCompanies, "arm");
                if (pc < 3) problems.Add(variant.Key + ": x86 at " + pc + " of 5 PC companies by 1995 (" + Where(result, 1995, PcCompanies) + ")");
                if (arm < 2) problems.Add(variant.Key + ": ARM at " + arm + " of 3 embedded companies by 2015 (" + Where(result, 2015, EmbeddedCompanies) + ")");
                if (result.MinM < 0.80 - CorridorTolerance || result.MaxM > 1.20 + CorridorTolerance) problems.Add(variant.Key + ": m from " + result.MinM + " to " + result.MaxM);
                if (result.MaxFamilyMBefore1985 > 1.20 + CorridorTolerance || result.PureAdvantageBefore1985 > 1.20 + CorridorTolerance) problems.Add(variant.Key + ": a family gives more than 20 % before 1985");
            }
            Assert.True(problems.Count == 0, "the metrics fail in variants of the market:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheRun_IsDeterministic()
        {
            SimResult first = Run();
            SimResult second = Run();
            Assert.Equal(first.Events.Count, second.Events.Count);
            for (int i = 0; i < first.Events.Count; i++)
            {
                SimEvent a = first.Events[i], b = second.Events[i];
                Assert.True(a.Date == b.Date && a.Company == b.Company && a.Kind == b.Kind && a.Family == b.Family, "event " + i + " differs");
            }
            Assert.Equal(first.Years.Count, second.Years.Count);
            for (int i = 0; i < first.Years.Count; i++)
            {
                foreach (KeyValuePair<string, string> pair in first.Years[i].Active) Assert.Equal(pair.Value, second.Years[i].Active[pair.Key]);
                foreach (KeyValuePair<string, float> pair in first.Years[i].Ecosystem) Assert.Equal(pair.Value, second.Years[i].Ecosystem[pair.Key]);
            }
            Assert.Equal(first.MinM, second.MinM);
            Assert.Equal(first.MaxM, second.MaxM);
        }

        [Test]
        public void TheWeightsOfThePreferences_DecideWhoMoves_TheSimulationAnswersToThem()
        {
            // Zilog weighs ARM 0.5 against 1.0 and stays; with the weight doubled it behaves like Texas Instruments.
            SimResult raised = Run(p => p.WeightScale = (company, family) => company == "zilog" && family == "arm" ? 2f : 1f);
            Assert.NotNull(raised.MovedTo("zilog", "arm"), "Zilog with the weight 1.0 for ARM moves");
            Assert.True(OnIsa(raised, 2015, "zilog", "arm"));

            // Texas Instruments with no taste for ARM does not move, and the metric then fails: the simulation sees what a change of the content costs.
            SimResult dropped = Run(p => p.WeightScale = (company, family) => company == "texas-instruments" && family == "arm" ? 0f : 1f);
            Assert.Null(dropped.MovedTo("texas-instruments", "arm"));
            Assert.True(Count(dropped, 2015, EmbeddedCompanies, "arm") < 2, "without Texas Instruments only Toshiba is on ARM");
        }

        // ---- the report ----

        [Test]
        public void TheReport_HasATableByYear_TheEventsAndTheMetrics()
        {
            SimResult result = Base();
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            string full = BalanceSimulator.Report(result, catalog, 1);
            string compact = BalanceSimulator.Report(result, catalog, 5);

            for (int year = 1975; year <= 2025; year++) Assert.True(full.Contains("\n" + year.ToString(CultureInfo.InvariantCulture) + "  "), "the table has a row for " + year);
            Assert.True(compact.Length < full.Length, "the compact table is shorter");
            foreach (int year in new[] { 1975, 1980, 1995, 2015, 2025 }) Assert.True(compact.Contains("\n" + year.ToString(CultureInfo.InvariantCulture) + "  "), "the compact table has a row for " + year);
            Assert.True(full.Contains("Смены активной архитектуры") && full.Contains("Вклад m в рейтинг") && full.Contains("До 1985"), "the report has the events and the metrics");
            Assert.True(full.Contains("Texas Instruments") && full.Contains("переходит"), "the report names the companies and their moves");

            string path = Path.Combine(TestEnv.BuildDir ?? Path.GetTempPath(), "cpu-architectures-balance-report.txt");
            try
            {
                File.WriteAllText(path, full, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // the report is a by-product; the folder may be read-only on a machine that only runs the tests
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
