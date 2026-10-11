using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.PressReviews;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The calibration of the review formulas (FR-2 "Acceptance Metrics", design.md "Тесты", TSK-021): on a synthetic market the metascores of the whole pipeline - the
    /// rising bar of the reference CPUs, the four axes, the weakest-axis cap, the marks of the outlets of the year with their noise - must be spread the way the design
    /// wants (median 55…70; at most 15 % at 85 or above; at most 15 % below 40), and a CPU that merely equals the year-old leader must not be called good. The numbers
    /// are the real ones: <c>review-config.json</c> and <c>outlets.json</c> as built in, difficulty Normal. <b>A red test is the signal to adjust
    /// <c>review-config.json</c></b> (the curve, the weights, the cap, the strictness, the biases of the outlets), not to loosen the test.
    /// <para>
    /// <b>The synthetic market.</b> 5000 CPUs, each reviewed on a day of 1980…2000 in a segment drawn at random, against five reference CPUs aged 0…2 years. The
    /// scatter "±25 % on the axes" is a log-normal one: each of the three ratio axes of the CPU (performance against the best aged reference, value and efficiency against
    /// the median aged reference) is the reference level times <c>exp(σ·z)</c>, <c>σ = ln 1.25</c>, <c>z ~ N(0, 1)</c> (one standard deviation is 25 %). The levels are those the
    /// review itself uses - the references aged to the day of the review by <c>ReviewMath.TrendFactor</c> - because the design means the CPUs of a market that grows
    /// with its references: a CPU "around the references" is around what the references would be today. The thermal axis is absolute: the temperature is
    /// 20 °C under the safe one, scattered by 10 °C, which gives an axis near 62 with a spread of about 25 points.
    /// </para>
    /// </summary>
    public class CalibrationTests
    {
        private const int Trials = 5000;
        private const int Normal = 2;                         // Player.DifficultyLevel of "Normal": strictness 1.00
        private const double Sigma = 0.22314355131420976;     // ln 1.25
        private const float SafeTemperature = 95f;

        private static readonly int[] Segments = { 0, 1, 2, 3 };

        // ---- the market ----

        private sealed class Market
        {
            public readonly ReviewConfig Config = PressContentFixture.Config();
            public readonly List<Outlet> Outlets = PressContentFixture.Outlets();
            public readonly IRandomSource Dice;

            public Market(uint seed)
            {
                Dice = new ModRngSource(new Xoshiro128(seed));
            }
        }

        private struct Aged
        {
            public double BestIps;
            public double MedianIpsPerPrice;
            public double MedianIpsPerWatt;
        }

        /// <summary>Five references of the given ages, each a little different from the others.</summary>
        private static List<CpuSnapshot> References(IRandomSource dice, float[] ages, float now, Segment segment)
        {
            var list = new List<CpuSnapshot>();
            for (int i = 0; i < ages.Length; i++)
            {
                list.Add(new CpuSnapshot
                {
                    SaveId = 100 + i, CompanyKey = "ai:" + (i + 1), Name = "Ref " + i,
                    Ips = 1000f * Spread(dice), Price = 200f * Spread(dice), PowerConsumption = 20f * Spread(dice),
                    Temperature = SafeTemperature - 20f, SafeTemperature = SafeTemperature,
                    ReleaseYearFraction = now - ages[i], Segment = segment,
                });
            }
            return list;
        }

        /// <summary>0.7 … 1.3, the difference between one reference and another.</summary>
        private static float Spread(IRandomSource dice)
        {
            return 1f + 0.1f * dice.NextGaussian();
        }

        /// <summary>The levels the review compares with, worked out as the design says: aged by <c>g^(tau·age)</c>, the best Ips and the medians of Ips/Price and Ips/Power.</summary>
        private static Aged AgedLevels(IList<CpuSnapshot> peers, float now, ReviewConfig cfg)
        {
            float g = ReviewMath.EraGrowth((int)Math.Floor(now), cfg);
            var ips = new List<double>();
            var perPrice = new List<double>();
            var perWatt = new List<double>();
            for (int i = 0; i < peers.Count; i++)
            {
                double aged = peers[i].Ips * ReviewMath.TrendFactor(now - peers[i].ReleaseYearFraction, g, cfg.Trend.Tau);
                ips.Add(aged);
                perPrice.Add(aged / peers[i].Price);
                perWatt.Add(aged / Math.Max(peers[i].PowerConsumption, ReviewMath.MinPower));
            }
            return new Aged { BestIps = Max(ips), MedianIpsPerPrice = Median(perPrice), MedianIpsPerWatt = Median(perWatt) };
        }

        private static double Max(List<double> values)
        {
            double best = 0.0;
            foreach (double v in values) best = Math.Max(best, v);
            return best;
        }

        private static double Median(List<double> values)
        {
            var sorted = new List<double>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        /// <summary>A CPU whose three ratio axes are the given multiples of the reference levels and whose temperature is given.</summary>
        private static CpuSnapshot CpuAt(Aged level, double performanceRatio, double valueRatio, double efficiencyRatio, float temperature, float now, Segment segment)
        {
            double ips = performanceRatio * level.BestIps;
            return new CpuSnapshot
            {
                SaveId = 7, CompanyKey = "player", Name = "Subject", Ips = (float)ips,
                Price = (float)(ips / (valueRatio * level.MedianIpsPerPrice)),
                PowerConsumption = (float)(ips / (efficiencyRatio * level.MedianIpsPerWatt)),
                Temperature = temperature, SafeTemperature = SafeTemperature, ReleaseYearFraction = now, Segment = segment,
            };
        }

        private static ReviewRecord Review(Market market, CpuSnapshot cpu, IList<CpuSnapshot> peers, DateTime day, float now)
        {
            var input = new ReviewInput { Cpu = cpu, Peers = new List<CpuSnapshot>(peers), NowYearFraction = now, Difficulty = Normal };
            var pending = new PendingReview { cpuSaveId = cpu.SaveId, companyKey = "player", releasedOn = PressDates.ToText(day), publishOn = PressDates.ToText(day) };
            return ReviewComposer.Compose(pending, "Subject Co", input, day, market.Outlets, market.Config, market.Dice);
        }

        private static DateTime RandomDay(IRandomSource dice, int firstYear, int lastYear)
        {
            int year = firstYear + dice.Range(0, lastYear - firstYear + 1);
            return new DateTime(year, 1, 1).AddDays(dice.Range(0, 365));
        }

        private static float YearFraction(DateTime day)
        {
            return PressDates.YearFraction(day);
        }

        // ---- the spread of the metascores ----

        [Test]
        public void TheSpreadOfTheMetascores_OnASyntheticMarketOf5000Cpus_MeetsTheTargets()
        {
            var market = new Market(20261010);
            var scores = new List<int>(Trials);
            var bySegment = new List<int>[4];
            for (int i = 0; i < bySegment.Length; i++) bySegment[i] = new List<int>();

            for (int trial = 0; trial < Trials; trial++)
            {
                Segment segment = (Segment)Segments[market.Dice.Range(0, Segments.Length)];
                DateTime day = RandomDay(market.Dice, 1980, 2000);
                float now = YearFraction(day);

                float[] ages = new float[5];
                for (int i = 0; i < ages.Length; i++) ages[i] = 2f * market.Dice.Range(0, 1001) / 1000f;   // 0…2 years
                List<CpuSnapshot> peers = References(market.Dice, ages, now, segment);
                Aged level = AgedLevels(peers, now, market.Config);

                CpuSnapshot cpu = CpuAt(level,
                    Math.Exp(Sigma * market.Dice.NextGaussian()), Math.Exp(Sigma * market.Dice.NextGaussian()), Math.Exp(Sigma * market.Dice.NextGaussian()),
                    SafeTemperature - 20f + 10f * market.Dice.NextGaussian(), now, segment);

                int score = Review(market, cpu, peers, day, now).metascore;
                scores.Add(score);
                bySegment[(int)segment].Add(score);
            }

            ScoreSummary all = ScoreDistribution.Summarize(scores);
            string detail = "the whole market: " + ScoreDistribution.Line(1980, all)
                + "; High End median " + ScoreDistribution.Summarize(bySegment[0]).Median + ", Mid Range " + ScoreDistribution.Summarize(bySegment[1]).Median
                + ", Low End " + ScoreDistribution.Summarize(bySegment[2]).Median + ", Industries " + ScoreDistribution.Summarize(bySegment[3]).Median
                + ". Adjust Content/press-reviews/review-config.json (curve, weights, cap, strictness, outlet biases), not the test.";

            Assert.Equal(Trials, all.Count);
            Assert.True(all.Median >= ScoreDistribution.MedianMin && all.Median <= ScoreDistribution.MedianMax, "the median must be 55..70: " + detail);
            Assert.True(all.ShareHigh <= ScoreDistribution.MaxShareHigh, "at most 15 % at 85 or above: " + detail);
            Assert.True(all.ShareLow <= ScoreDistribution.MaxShareLow, "at most 15 % below 40: " + detail);
            Assert.True(ScoreDistribution.IsWithinTargets(all), detail);
        }

        [Test]
        public void EverySegmentAlone_StaysInTheTargets_TooOnAnotherDraw()
        {
            // a second market with another seed, judged by segment: the formulas must not be right on average and wrong in one segment
            var market = new Market(77);
            var bySegment = new List<int>[4];
            for (int i = 0; i < bySegment.Length; i++) bySegment[i] = new List<int>();

            for (int trial = 0; trial < Trials; trial++)
            {
                Segment segment = (Segment)Segments[trial % Segments.Length];
                DateTime day = RandomDay(market.Dice, 1980, 2000);
                float now = YearFraction(day);
                float[] ages = new float[5];
                for (int i = 0; i < ages.Length; i++) ages[i] = 2f * market.Dice.Range(0, 1001) / 1000f;
                List<CpuSnapshot> peers = References(market.Dice, ages, now, segment);
                Aged level = AgedLevels(peers, now, market.Config);

                CpuSnapshot cpu = CpuAt(level,
                    Math.Exp(Sigma * market.Dice.NextGaussian()), Math.Exp(Sigma * market.Dice.NextGaussian()), Math.Exp(Sigma * market.Dice.NextGaussian()),
                    SafeTemperature - 20f + 10f * market.Dice.NextGaussian(), now, segment);
                bySegment[(int)segment].Add(Review(market, cpu, peers, day, now).metascore);
            }

            string[] names = { "High End", "Mid Range", "Low End", "Industries" };
            for (int s = 0; s < 4; s++)
            {
                ScoreSummary summary = ScoreDistribution.Summarize(bySegment[s]);
                Assert.True(ScoreDistribution.IsWithinTargets(summary), names[s] + ": " + ScoreDistribution.Line(1980, summary));
            }
        }

        // ---- a CPU as good as last year's leader ----

        /// <summary>No noise and the first quote: what a review is without the luck of the outlets.</summary>
        private sealed class QuietDice : IRandomSource
        {
            public float NextGaussian()
            {
                return 0f;
            }

            public int Range(int minInclusive, int maxExclusive)
            {
                return minInclusive;
            }
        }

        /// <summary>Five references with a leader exactly a year old, and a CPU equal to that leader by Ips (value and efficiency at the market's level, a typical temperature).</summary>
        private static CpuSnapshot EqualToTheYearOldLeader(Market market, int year, float now, Segment segment, out List<CpuSnapshot> peers)
        {
            peers = new List<CpuSnapshot>();
            for (int i = 0; i < 5; i++)
            {
                float age = i == 0 ? 1f : 1f + 0.1f * i;   // the leader is exactly a year old, the others a bit older
                peers.Add(new CpuSnapshot
                {
                    SaveId = 100 + i, CompanyKey = "ai:" + (i + 1), Name = "Ref " + i, Ips = i == 0 ? 1000f : 800f - 50f * i, Price = 200f, PowerConsumption = 20f,
                    Temperature = SafeTemperature - 20f, SafeTemperature = SafeTemperature, ReleaseYearFraction = now - age, Segment = segment,
                });
            }
            Aged level = AgedLevels(peers, now, market.Config);

            double ratio = 1000.0 / level.BestIps;   // below 1: the bar has grown by g^tau in the year
            float g = ReviewMath.EraGrowth(year, market.Config);
            Assert.True(ratio < 1.0 && ratio > 0.7, "the year-old leader has aged by g^tau (g " + g + "): the CPU is at " + ratio + " of the bar");

            CpuSnapshot cpu = CpuAt(level, ratio, 1.0, 1.0, SafeTemperature - 20f, now, segment);
            Assert.Near(1000.0, cpu.Ips, 0.01, "the CPU equals the year-old leader by Ips");
            return cpu;
        }

        [Test]
        public void ACpuEqualToTheYearOldLeader_WithAverageOtherAxes_IsNotCalledGood()
        {
            // The leader of the segment is a year old and the CPU matches its Ips; value and efficiency are those of the market (ratio 1), the temperature is typical.
            // The bar has grown by g^tau in that year, so the CPU is behind it: 40..60, not the 65 of "average" and not a "good" 70.
            var quiet = new Market(5);
            var noisy = new Market(6);
            var scores = new List<int>();
            foreach (int year in new[] { 1982, 1986, 1991, 1995, 1999 })
            {
                var day = new DateTime(year, 6, 15);
                float now = YearFraction(day);

                List<CpuSnapshot> peers;
                CpuSnapshot cpu = EqualToTheYearOldLeader(quiet, year, now, Segment.HighEnd, out peers);

                // by the formulas alone (no luck of the outlets)
                var input = new ReviewInput { Cpu = cpu, Peers = peers, NowYearFraction = now, Difficulty = Normal };
                var pending = new PendingReview { cpuSaveId = cpu.SaveId, companyKey = "player", releasedOn = PressDates.ToText(day), publishOn = PressDates.ToText(day) };
                int exact = ReviewComposer.Compose(pending, "Subject Co", input, day, quiet.Outlets, quiet.Config, new QuietDice()).metascore;
                scores.Add(exact);

                // and with the marks of the outlets drawn many times: the middle of them is in the same band
                var drawn = new List<int>();
                for (int draw = 0; draw < 200; draw++) drawn.Add(Review(noisy, cpu, peers, day, now).metascore);
                double median = ScoreDistribution.Summarize(drawn).Median;
                Assert.True(median >= 40 && median <= 60, year + ": with the noise of the outlets the median of 200 reviews is " + median);
            }

            Assert.True(scores.TrueForAll(score => score >= 40 && score <= 60),
                "a CPU equal to the year-old leader, average on the other axes, scores 40..60 by the formulas; got " + string.Join(", ", scores));
        }

        [Test]
        public void ACpuEqualToTheYearOldLeader_InAnySegment_IsBelowGood_AndHigherEndSegmentsAreStricterAboutSpeed()
        {
            var market = new Market(6);
            double[] medians = new double[4];
            for (int segment = 0; segment < 4; segment++)
            {
                var scores = new List<int>();
                for (int year = 1981; year <= 1999; year += 3)
                {
                    var day = new DateTime(year, 3, 10);
                    float now = YearFraction(day);

                    List<CpuSnapshot> peers;
                    CpuSnapshot cpu = EqualToTheYearOldLeader(market, year, now, (Segment)segment, out peers);
                    for (int draw = 0; draw < 50; draw++) scores.Add(Review(market, cpu, peers, day, now).metascore);
                }
                medians[segment] = ScoreDistribution.Summarize(scores).Median;
                Assert.True(medians[segment] < 65, "segment " + segment + ": a CPU that is only as fast as last year's leader is not above average (" + medians[segment] + ")");
            }

            // speed counts for most in High End and for least in Industries: the same CPU is judged more harshly where speed is the point
            Assert.True(medians[0] <= medians[3], "High End " + medians[0] + " vs Industries " + medians[3]);
        }

        // ---- the bar rises ----

        [Test]
        public void TheSameCpu_ScoresLowerAgainstOlderReferences()
        {
            // a CPU equal to the RAW references of age 0, 1 and 2 years: the older the references, the higher the bar they have grown to (design.md formula 2)
            var market = new Market(9);
            var day = new DateTime(1995, 6, 15);
            float now = YearFraction(day);

            int previous = int.MaxValue;
            foreach (float age in new[] { 0f, 1f, 2f })
            {
                var peers = new List<CpuSnapshot>();
                for (int i = 0; i < 3; i++)
                {
                    peers.Add(new CpuSnapshot
                    {
                        SaveId = 100 + i, CompanyKey = "ai:" + (i + 1), Name = "Ref " + i, Ips = 1000f, Price = 200f, PowerConsumption = 20f,
                        Temperature = SafeTemperature - 20f, SafeTemperature = SafeTemperature, ReleaseYearFraction = now - age, Segment = Segment.HighEnd,
                    });
                }
                var cpu = new CpuSnapshot
                {
                    SaveId = 7, CompanyKey = "player", Name = "Subject", Ips = 1000f, Price = 200f, PowerConsumption = 20f,
                    Temperature = SafeTemperature - 20f, SafeTemperature = SafeTemperature, ReleaseYearFraction = now, Segment = Segment.HighEnd,
                };

                var input = new ReviewInput { Cpu = cpu, Peers = peers, NowYearFraction = now, Difficulty = Normal };
                float performance = ReviewMath.Compute(input, market.Config).AxisScores[(int)Axis.Performance];
                int score = Review(market, cpu, peers, day, now).metascore;

                if (age == 0f) Assert.Near(65.0, performance, 0.01, "a CPU equal to today's references is exactly average on the performance axis");
                Assert.True(score < previous, "age " + age + ": " + score + " must be below " + previous);
                previous = score;
            }
        }
    }
}
