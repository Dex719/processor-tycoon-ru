using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>What one run of the Monte-Carlo balance simulation counted. Public fields only: the determinism test compares two runs field by field.</summary>
    internal sealed class BalanceStats
    {
        public int Designs;

        /// <summary>The mean of the probability of a hidden flaw over the designs (not of the rolls).</summary>
        public double MeanP;
        public double MinP;
        public double MaxP;

        /// <summary>How many rolls came out as a flaw, and of what severity.</summary>
        public int Flawed;
        public int Minor;
        public int Major;
        public int Critical;

        /// <summary>Critical flaws among the flawed ones, by the rolls.</summary>
        public double CriticalShare;

        /// <summary>The same share without a die: the sum of p * (share of Critical among the weights at that p) over the sum of p.</summary>
        public double ExpectedCriticalShare;

        public int Arith;
        public int Stability;
        public int Thermal;
        public int Security;
    }

    /// <summary>
    /// TSK-024 (FR-2, FR-4, NFR-6; requirements, "Acceptance Metrics"): the Monte-Carlo test of the balance. It draws 10 000 typical designs
    /// (<see cref="SyntheticDesigns"/>: difficulty 2, no rush), takes their probability from <see cref="FlawRiskModel"/>, rolls the flaw and its severity and type
    /// the way the release does, and checks the three numbers of the spec: the mean probability is 8-15 %, at most 20 % of the flaws are <c>Critical</c>, and a
    /// <c>Major</c> flaw of a CPU that sold a million units is found in 6-18 months on average. The discovery is run through the real
    /// <see cref="FlawCycle"/> (the monthly monitor), not through a copy of its rule. The same seed gives the same numbers.
    /// </summary>
    [Covers(typeof(FlawRiskModel), typeof(FlawRiskInput), typeof(FlawRiskFactors), typeof(DiscoveryModel), typeof(FlawCycle), typeof(FlawFacts), typeof(IFlawCycleHost))]
    public class FlawBalanceSimulationTests
    {
        private const int Designs = 10000;
        private const uint Seed = 20261010u;

        private static FlawBalance _balance;

        /// <summary>The numbers the game runs with: the shipped <c>flaws.json</c> read by the validator, not a copy of them in the test.</summary>
        private static FlawBalance Balance
        {
            get
            {
                if (_balance == null)
                {
                    var report = new FlawContentReport();
                    _balance = FlawContentValidator.Parse(FlawTestKit.ShippedFlawsText(), report);
                    Assert.Equal(0, report.Errors.Count, "the shipped flaws.json is clean: " + string.Join("; ", report.Errors));
                }
                return _balance;
            }
        }

        // ---- the three numbers of the spec ----

        [Test]
        public void TenThousandTypicalDesigns_MeanProbability_IsEightToFifteenPercent()
        {
            BalanceStats stats = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);

            Assert.Equal(Designs, stats.Designs);
            Assert.InRange(stats.MeanP, 0.08, 0.15, "the mean probability of a hidden flaw, difficulty 2 and no rush");
        }

        [Test]
        public void TenThousandTypicalDesigns_AtMostTwentyPercentOfTheFlawsAreCritical()
        {
            BalanceStats stats = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);

            Assert.True(stats.Flawed > 500, "enough flaws to count: " + stats.Flawed);
            Assert.True(stats.CriticalShare <= 0.20, "Critical share by the rolls: " + stats.CriticalShare);
            Assert.True(stats.ExpectedCriticalShare <= 0.20, "Critical share by the weights: " + stats.ExpectedCriticalShare);
            Assert.Equal(stats.Flawed, stats.Minor + stats.Major + stats.Critical, "every flaw has a severity");
            Assert.Equal(stats.Flawed, stats.Arith + stats.Stability + stats.Thermal + stats.Security, "every flaw has a type");
        }

        [Test]
        public void TheDice_AgreeWithTheWeights()
        {
            // The rolls are a sample of the formula: the Critical share they give is the share the weights give, within the noise of ~1300 flaws.
            BalanceStats stats = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);

            Assert.Near(stats.ExpectedCriticalShare, stats.CriticalShare, 0.03, "Critical share: the dice against the weights");
            Assert.Near(stats.MeanP, stats.Flawed / (double)Designs, 0.01, "the flawed share is the mean probability");
            Assert.True(stats.Minor > stats.Major && stats.Major > stats.Critical, "Minor is the commonest flaw and Critical the rarest");
        }

        [Test]
        public void MajorFlaw_AtAMillionUnits_IsFoundInSixToEighteenMonthsOnAverage_ByTheMonthlyMonitor()
        {
            double mean = MeanMonthsToFind(FlawSeverity.Major, 1000000, 4000, Seed);

            Assert.InRange(mean, 6.0, 18.0, "months from the release to the discovery of a Major flaw, 1 000 000 units sold");
        }

        [Test]
        public void MajorFlaw_AtAMillionUnits_MatchesTheHazardTable_AboutTwelveMonths()
        {
            // design.md: h = 0.082, "1/0.082 ~ 12 months"; a flaw not found by month 36 is forced then, which pulls the mean a little under 12.
            FlawBalance b = Balance;
            float h = DiscoveryModel.MonthlyHazard(FlawSeverity.Major, 1000000, b);
            Assert.Near(0.082, h, 0.001, "the hazard of Major at a million units");

            double theory = TruncatedMean(h, b.Discovery.MaxHiddenMonths[(int)FlawSeverity.Major]);
            Assert.InRange(theory, 11.0, 12.5, "the mean of the hazard law");

            double simulated = MeanMonthsToFind(FlawSeverity.Major, 1000000, 6000, Seed + 1);
            Assert.Near(theory, simulated, 0.8, "the monitor against the law, mean months");
        }

        [Test]
        public void TheSimulation_RunsOnTheShippedBalance_WhichIsTheDefaultOfTheCode()
        {
            Assert.Equal(0, FlawTestKit.Diff(FlawBalance.Defaults(), Balance).Count, "the numbers of the simulation are the numbers of the game: " + string.Join("; ", FlawTestKit.Diff(FlawBalance.Defaults(), Balance)));
        }

        // ---- the Monte-Carlo run itself ----

        [Test]
        public void TheSameSeed_GivesTheSameNumbers_AnotherSeedGivesOtherOnes()
        {
            BalanceStats first = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);
            BalanceStats second = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);
            BalanceStats other = Simulate(Seed + 1, Designs, new SyntheticPopulation(), 1f);

            Assert.Equal(0, FlawTestKit.Diff(first, second).Count, "same seed: " + string.Join("; ", FlawTestKit.Diff(first, second)));
            Assert.True(FlawTestKit.Diff(first, other).Count > 0, "another seed must give another sample");
            Assert.InRange(other.MeanP, 0.08, 0.15, "and the other sample passes too");
            Assert.True(other.CriticalShare <= 0.20, "Critical share of the other sample: " + other.CriticalShare);
        }

        [Test]
        public void TheDiscoveryRun_IsReproducible_WithTheSameSeed()
        {
            double first = MeanMonthsToFind(FlawSeverity.Major, 1000000, 1500, Seed);
            double second = MeanMonthsToFind(FlawSeverity.Major, 1000000, 1500, Seed);
            double other = MeanMonthsToFind(FlawSeverity.Major, 1000000, 1500, Seed + 5);

            Assert.Equal(first, second, "same seed, same mean");
            Assert.NotEqual(first, other, "another seed, another mean");
        }

        [Test]
        public void ManySeeds_AllPassTheThreeNumbers()
        {
            // Not luck: twenty independent samples of 10 000 designs (and of 800 flaws to find) all stay inside the bounds.
            for (uint seed = 1; seed <= 20; seed++)
            {
                BalanceStats stats = Simulate(seed * 7919u, Designs, new SyntheticPopulation(), 1f);
                Assert.InRange(stats.MeanP, 0.08, 0.15, "mean probability, seed " + seed);
                Assert.True(stats.CriticalShare <= 0.20, "Critical share, seed " + seed + ": " + stats.CriticalShare);
                Assert.InRange(MeanMonthsToFind(FlawSeverity.Major, 1000000, 800, seed * 104729u), 6.0, 18.0, "months to find, seed " + seed);
            }
        }

        // ---- what the numbers depend on ----

        [Test]
        public void MoreUnitsSold_MeansAFasterDiscovery_AndTheGraverTheFlawTheSooner()
        {
            double hundredThousand = MeanMonthsToFind(FlawSeverity.Major, 100000, 3000, Seed);
            double million = MeanMonthsToFind(FlawSeverity.Major, 1000000, 3000, Seed);
            double tenMillion = MeanMonthsToFind(FlawSeverity.Major, 10000000, 3000, Seed);
            Assert.True(tenMillion < million && million < hundredThousand, "10M " + tenMillion + " < 1M " + million + " < 100k " + hundredThousand);

            double minor = MeanMonthsToFind(FlawSeverity.Minor, 1000000, 3000, Seed);
            double critical = MeanMonthsToFind(FlawSeverity.Critical, 1000000, 3000, Seed);
            Assert.True(critical < million && million < minor, "Critical " + critical + " < Major " + million + " < Minor " + minor);
        }

        [Test]
        public void AFlawThatNobodyFinds_IsForcedWhenTheLimitOfItsSeverityComes()
        {
            // maxHiddenMonths 36 for Major: every record is out of Hidden by month 36 at the latest, the ones the dice missed through the forced rule.
            var months = new List<int>();
            int forced = RunDiscovery(FlawSeverity.Major, 1000000, 3000, Seed, months);

            Assert.True(months.All(m => m >= 1 && m <= 36), "found within 36 months: " + months.Max());
            Assert.True(forced > 0, "some flaws were missed by the dice for 36 months and forced");
            double share = forced / (double)months.Count;
            Assert.Near(Math.Pow(1.0 - 0.082, 35), share, 0.02, "the share forced is the chance to be missed 35 times in a row");
        }

        [Test]
        public void Difficulty_ScalesTheProbability_ByItsMultiplier()
        {
            FlawBalance b = Balance;
            double normal = Simulate(Seed, Designs, new SyntheticPopulation { Difficulty = 2 }, 1f).MeanP;

            for (int level = 0; level < FlawBalance.DifficultyCount; level++)
            {
                double mean = Simulate(Seed, Designs, new SyntheticPopulation { Difficulty = level }, 1f).MeanP;
                double factor = FlawRiskModel.DifficultyMultiplier(level, b);   // difficulty 2 is 1.0
                Assert.Near(factor, mean / normal, 0.04, "difficulty " + level + ": the mean follows the multiplier");
            }

            double easy = Simulate(Seed, Designs, new SyntheticPopulation { Difficulty = 0 }, 1f).MeanP;
            double hard = Simulate(Seed, Designs, new SyntheticPopulation { Difficulty = 5 }, 1f).MeanP;
            Assert.True(easy < normal && normal < hard, "easier is safer");
        }

        [Test]
        public void FlawFrequencySetting_ScalesTheProbability_AndRushRaisesIt()
        {
            double normal = Simulate(Seed, Designs, new SyntheticPopulation(), 1f).MeanP;
            double rare = Simulate(Seed, Designs, new SyntheticPopulation(), 0.5f).MeanP;
            double often = Simulate(Seed, Designs, new SyntheticPopulation(), 2f).MeanP;
            double rushed = Simulate(Seed, Designs, new SyntheticPopulation { Rush = true }, 1f).MeanP;

            Assert.Near(0.5, rare / normal, 0.01, "half as often");
            Assert.InRange(often / normal, 1.8, 2.0, "twice as often (a few designs hit pMax)");
            Assert.True(rushed > normal, "a rush project is riskier");
            Assert.Near(0.06 * 1.003, rushed - normal, 0.006, "by kRush times the multipliers of the era");
        }

        [Test]
        public void TheProbability_NeverLeavesZeroAndPMax()
        {
            BalanceStats stats = Simulate(Seed, Designs, new SyntheticPopulation { Difficulty = 5 }, 2f);

            Assert.True(stats.MinP > 0.0, "a perfect design is still a little risky: " + stats.MinP);
            Assert.True(stats.MaxP <= 0.5 + 1e-6, "pMax: " + stats.MaxP);
        }

        [Test]
        public void TheTypes_FollowTheirWindowOfYears_AndEveryEarlyTypeAppears()
        {
            // 1980..2010: "security" starts in 2015, so it never appears; the three other types all do.
            BalanceStats stats = Simulate(Seed, Designs, new SyntheticPopulation(), 1f);

            Assert.Equal(0, stats.Security, "no security flaw before 2015");
            Assert.True(stats.Arith > 0 && stats.Stability > 0 && stats.Thermal > 0, "arith " + stats.Arith + ", stability " + stats.Stability + ", thermal " + stats.Thermal);
            Assert.True(stats.Arith > stats.Thermal, "arith has the larger weight (3 against 2) and thermal designs are rare");

            BalanceStats late = Simulate(Seed, Designs, new SyntheticPopulation { FromYear = 2015, ToYear = 2025 }, 1f);
            Assert.True(late.Security > 0, "and from 2015 it does");
        }

        [Test]
        public void TheMeanProbability_GrowsWithTheEra_AsTheDesignsGetHarder()
        {
            double early = Simulate(Seed, Designs, new SyntheticPopulation { FromYear = 1980, ToYear = 1984 }, 1f).MeanP;
            double middle = Simulate(Seed, Designs, new SyntheticPopulation { FromYear = 1985, ToYear = 1999 }, 1f).MeanP;
            double late = Simulate(Seed, Designs, new SyntheticPopulation { FromYear = 2000, ToYear = 2010 }, 1f).MeanP;

            Assert.True(early < middle && middle < late, "early " + early + " < middle " + middle + " < late " + late);
            Assert.InRange(early, 0.05, 0.12, "the first years: the plain design of AC-2.1 is 0.070");
        }

        // ---- the population itself ----

        [Test]
        public void Population_IsTheOneOfTheAcceptanceMetric_AndItsSharesAreTheOnesItSays()
        {
            var population = new SyntheticPopulation();
            List<FlawRiskInput> designs = SyntheticDesigns.Generate(Designs, new Xoshiro128(Seed), population);
            FlawBalance b = Balance;

            Assert.Equal(Designs, designs.Count);
            Assert.True(designs.All(d => d.Difficulty == 2 && !d.Rush && !d.IsAi), "difficulty 2, no rush, a player's design");
            Assert.True(designs.All(d => d.Year >= 1980 && d.Year <= 2010), "one campaign");

            var factors = designs.Select(d => FlawRiskModel.Factors(d, b)).ToList();
            Assert.Near(0.15, factors.Count(f => f.OC > 0f) / (double)Designs, 0.02, "share overclocked");
            Assert.Near(0.08, factors.Count(f => f.TH > 0f) / (double)Designs, 0.02, "share overheated");
            Assert.Near(0.80, designs.Count(d => d.HasNodeRecord) / (double)Designs, 0.02, "share on a known node");
            Assert.True(factors.All(f => f.OC <= 0.4f + 1e-4f && f.TH <= 0.15f + 1e-4f && f.NI >= 0f && f.NI <= 1f && f.FR >= 0.02f && f.FR <= 0.8f), "inside their ranges");
        }

        [Test]
        public void Population_EveryDesignDrawsTheSameNumberOfValues_WhateverBranchItTakes()
        {
            foreach (float roll in new[] { 0.0f, 0.5f, 0.99f })
            {
                var rng = FakeRng.Always(roll);
                SyntheticDesigns.One(rng, new SyntheticPopulation());
                Assert.Equal(SyntheticDesigns.DrawsPerDesign, rng.Draws, "draws with every roll at " + roll);
            }
        }

        [Test]
        public void Population_IsDeterministic_AndTheOptionsReachTheDesigns()
        {
            List<FlawRiskInput> a = SyntheticDesigns.Generate(200, new Xoshiro128(5u), new SyntheticPopulation());
            List<FlawRiskInput> b = SyntheticDesigns.Generate(200, new Xoshiro128(5u), new SyntheticPopulation());
            Assert.Equal(0, FlawTestKit.Diff(a, b).Count, "same generator state, same designs");

            List<FlawRiskInput> ai = SyntheticDesigns.Generate(50, new Xoshiro128(5u), new SyntheticPopulation { IsAi = true, Rush = true, Difficulty = 4, FromYear = 1990, ToYear = 1990 });
            Assert.True(ai.All(d => d.IsAi && d.Rush && d.Difficulty == 4 && d.Year == 1990), "the options are in the designs");
        }

        // ---- the machinery ----

        /// <summary>Generates the designs and rolls them like the release does: the probability, then (if it hits) the severity and the type.</summary>
        private static BalanceStats Simulate(uint seed, int count, SyntheticPopulation population, float flawFrequency)
        {
            FlawBalance b = Balance;
            var designRng = new Xoshiro128(seed);
            var rollRng = new Xoshiro128(seed ^ 0x9E3779B9u);
            List<FlawRiskInput> designs = SyntheticDesigns.Generate(count, designRng, population);

            var stats = new BalanceStats { Designs = designs.Count, MinP = double.MaxValue, MaxP = double.MinValue };
            double sumP = 0.0, expectedCritical = 0.0;

            foreach (FlawRiskInput design in designs)
            {
                float p = FlawRiskModel.Probability(design, b, flawFrequency);
                sumP += p;
                stats.MinP = Math.Min(stats.MinP, p);
                stats.MaxP = Math.Max(stats.MaxP, p);

                float[] weights = FlawRiskModel.SeverityWeights(p, b);
                expectedCritical += p * (weights[2] / (double)(weights[0] + weights[1] + weights[2]));

                if (!rollRng.Chance(p)) continue;

                stats.Flawed++;
                switch (FlawRiskModel.PickSeverity(p, b, rollRng))
                {
                    case FlawSeverity.Minor: stats.Minor++; break;
                    case FlawSeverity.Major: stats.Major++; break;
                    default: stats.Critical++; break;
                }

                FlawTypeDef type = FlawRiskModel.PickType(FlawRiskModel.Factors(design, b), design.Year, b.Types, rollRng);
                switch (type.Id)
                {
                    case FlawTypeIds.Arithmetic: stats.Arith++; break;
                    case FlawTypeIds.Stability: stats.Stability++; break;
                    case FlawTypeIds.Thermal: stats.Thermal++; break;
                    case FlawTypeIds.Security: stats.Security++; break;
                }
            }

            stats.MeanP = sumP / Math.Max(1, designs.Count);
            stats.CriticalShare = stats.Flawed == 0 ? 0.0 : stats.Critical / (double)stats.Flawed;
            stats.ExpectedCriticalShare = sumP <= 0.0 ? 0.0 : expectedCritical / sumP;
            return stats;
        }

        /// <summary>The mean number of months a hidden flaw of the severity stays hidden when its CPU has sold <paramref name="units"/> units, through the real monthly monitor.</summary>
        private static double MeanMonthsToFind(FlawSeverity severity, long units, int records, uint seed)
        {
            var months = new List<int>();
            RunDiscovery(severity, units, records, seed, months);
            return months.Average();
        }

        /// <summary>
        /// Runs <paramref name="records"/> hidden flaws released on the same day through <see cref="FlawCycle.OnMonth"/> once a month, up to the last forced month. Fills
        /// <paramref name="months"/> with the month each one was found in; returns how many were found by the forced rule.
        /// </summary>
        private static int RunDiscovery(FlawSeverity severity, long units, int records, uint seed, List<int> months)
        {
            FlawFacts facts = FakeCycleHost.Selling(units);
            var host = new FakeCycleHost { Rng_ = new Xoshiro128(seed), Day = FlawCycleKit.MonthStart(0), Balance_ = Balance };
            var list = new FlawRecord[records];
            for (int i = 0; i < records; i++)
            {
                list[i] = FlawCycleKit.Record(FlawState.Hidden, id: i + 1, baseCpu: 1000 + i, severity: severity);
                host.Facts[1000 + i] = facts;
            }
            var cycle = new FlawCycle(FlawCycleKit.Store(list), host);

            int last = Balance.Discovery.MaxHiddenMonths[(int)severity];
            var foundIn = new int[records];
            for (int month = 1; month <= last; month++)
            {
                host.Day = FlawCycleKit.MonthStart(month);
                cycle.OnMonth();
                for (int i = 0; i < records; i++)
                {
                    if (foundIn[i] == 0 && list[i].State != FlawState.Hidden) foundIn[i] = month;
                }
            }

            int forced = 0;
            for (int i = 0; i < records; i++)
            {
                Assert.True(foundIn[i] > 0, "record " + (i + 1) + " is still hidden after the limit of its severity (the forced rule)");
                months.Add(foundIn[i]);
                if (list[i].Source == DiscoverySource.Forced) forced++;
            }
            return forced;
        }

        /// <summary>The mean of a geometric waiting time with chance <paramref name="h"/> per month, cut off at month <paramref name="limit"/> (found then for sure).</summary>
        private static double TruncatedMean(double h, int limit)
        {
            double mean = 0.0;
            double alive = 1.0;
            for (int k = 1; k < limit; k++)
            {
                mean += k * alive * h;
                alive *= 1.0 - h;
            }
            return mean + limit * alive;
        }
    }
}
