using System;
using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-007, FR-5: the multiplier of the channel <c>market.rating.cpu</c> (formulas F2-F6) - AC-5.1, AC-5.2, the fading of the early adopter
    /// bonus and of the socket-change penalty, the clamps, the weight of the Industries segment and the range of the contribution of the feature.
    /// </summary>
    [Covers(typeof(RatingEffectModel), typeof(RatingInput), typeof(RatingBreakdown))]
    public class RatingEffectModelTests
    {
        private const double Eps = 1e-6;

        private static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        private static RatingInput Base(double own, double socket, double total, bool standard)
        {
            return new RatingInput { OwnIb = own, SocketIb = socket, TotalIb = total, IsStandard = standard };
        }

        // ---- B_ib: AC-5.1, AC-5.2 ----

        [Test]
        public void Compute_AC51_ThirtyPercentOnAClosedSocket_Gives1075AndForIndustries10375()
        {
            PlatformBalance balance = Balance();
            RatingBreakdown r = RatingEffectModel.Compute(Base(30, 30, 100, false), balance);

            Assert.Near(0.30, r.EffShare, Eps);
            Assert.Near(0.075, r.Bib, Eps);
            Assert.Near(0.0, r.Bearly, Eps);
            Assert.Near(0.0, r.Pchurn, Eps);
            Assert.Near(1.075, r.ConsumerMult, Eps);
            Assert.Near(1.0375, r.IndustriesMult, Eps);
        }

        [Test]
        public void Compute_AC52_AStandardSocketCountsOthersBaseAtTheSharedWeight()
        {
            PlatformBalance balance = Balance();

            // PGA 1995: A has 10 %, B has 20 % of the whole base.
            RatingBreakdown a = RatingEffectModel.Compute(Base(10, 30, 100, true), balance);
            RatingBreakdown b = RatingEffectModel.Compute(Base(20, 30, 100, true), balance);

            Assert.Near(0.10 + 0.25 * 0.20, a.EffShare, Eps);
            Assert.Near(0.0375, a.Bib, Eps);
            Assert.Near(0.20 + 0.25 * 0.10, b.EffShare, Eps);
            Assert.Near(0.05625, b.Bib, Eps);
        }

        [Test]
        public void Compute_ASocketThatIsNotStandard_IgnoresTheOthersBase()
        {
            PlatformBalance balance = Balance();
            RatingBreakdown closed = RatingEffectModel.Compute(Base(10, 30, 100, false), balance);
            Assert.Near(0.10, closed.EffShare, Eps);
        }

        [Test]
        public void Compute_TheBonusSaturatesAtTheSaturationShare()
        {
            PlatformBalance balance = Balance();
            RatingBreakdown atSaturation = RatingEffectModel.Compute(Base(40, 40, 100, false), balance);
            RatingBreakdown above = RatingEffectModel.Compute(Base(90, 90, 100, false), balance);

            Assert.Near(balance.Rating.IbMaxBonus, atSaturation.Bib, Eps);
            Assert.Near(balance.Rating.IbMaxBonus, above.Bib, Eps);
            Assert.Near(1.0 + balance.Rating.IbMaxBonus, above.ConsumerMult, Eps);
        }

        [Test]
        public void Compute_ATotalBaseOfZero_IsNeutral()
        {
            PlatformBalance balance = Balance();
            RatingBreakdown r = RatingEffectModel.Compute(Base(0, 0, 0, true), balance);

            Assert.Equal(0.0, r.EffShare);
            Assert.Equal(0f, r.Bib);
            Assert.Equal(1f, r.ConsumerMult);
            Assert.Equal(1f, r.IndustriesMult);
        }

        [Test]
        public void Compute_BrokenBases_AreKeptInRange()
        {
            PlatformBalance balance = Balance();

            // A socket base below the own base, an own base above the total, a negative base: nothing leaves [0, 1].
            Assert.Near(0.20, RatingEffectModel.Compute(Base(20, 5, 100, true), balance).EffShare, Eps);
            Assert.Near(1.0, RatingEffectModel.Compute(Base(500, 500, 100, true), balance).EffShare, Eps);
            Assert.Near(0.0, RatingEffectModel.Compute(Base(-5, -5, 100, true), balance).EffShare, Eps);
            Assert.Equal(0.0, RatingEffectModel.Compute(Base(1, 1, double.NaN, true), balance).EffShare);
        }

        // ---- B_early ----

        [Test]
        public void Compute_TheEarlyBonusFadesLinearly_FromTheBonusToZero()
        {
            PlatformBalance balance = Balance();
            var input = new RatingInput { TotalIb = 100, IsEarlyAdopter = true };

            input.MonthsSinceEarly = 0;
            Assert.Near(balance.Rating.EarlyBonus, RatingEffectModel.Compute(input, balance).Bearly, Eps);

            input.MonthsSinceEarly = 12;
            RatingBreakdown atYear = RatingEffectModel.Compute(input, balance);
            Assert.Near(0.06 * (1 - 12.0 / 60.0), atYear.Bearly, Eps);   // 0.048
            Assert.Near(0.048, atYear.Bearly, Eps);
            Assert.Near(1.048, atYear.ConsumerMult, Eps);
            Assert.Near(1.024, atYear.IndustriesMult, Eps);

            input.MonthsSinceEarly = 60;
            Assert.Near(0.0, RatingEffectModel.Compute(input, balance).Bearly, Eps);

            input.MonthsSinceEarly = 600;
            Assert.Near(0.0, RatingEffectModel.Compute(input, balance).Bearly, Eps);
        }

        [Test]
        public void Compute_ANonAdopter_GetsNoEarlyBonus_AndNegativeMonthsCountAsZero()
        {
            PlatformBalance balance = Balance();
            var notAdopter = new RatingInput { TotalIb = 100, IsEarlyAdopter = false, MonthsSinceEarly = 0 };
            Assert.Equal(0f, RatingEffectModel.Compute(notAdopter, balance).Bearly);

            var odd = new RatingInput { TotalIb = 100, IsEarlyAdopter = true, MonthsSinceEarly = -7 };
            Assert.Near(balance.Rating.EarlyBonus, RatingEffectModel.Compute(odd, balance).Bearly, Eps);
        }

        // ---- P_churn ----

        [Test]
        public void Compute_TheChurnPenaltyFadesLinearly_AC61Numbers()
        {
            PlatformBalance balance = Balance();
            float severity = ChurnDetector.Severity(true, 12, 0.25, false, balance);   // 0.667 (AC-6.1)
            var input = new RatingInput { TotalIb = 100, ChurnSeverity = severity };

            input.MonthsSinceIntro = 0;
            RatingBreakdown start = RatingEffectModel.Compute(input, balance);
            Assert.Near(0.0667, start.Pchurn, 1e-4);
            Assert.Near(0.9333, start.ConsumerMult, 1e-4);
            Assert.Near(0.9667, start.IndustriesMult, 1e-4);

            input.MonthsSinceIntro = 12;
            Assert.Near(0.0333, RatingEffectModel.Compute(input, balance).Pchurn, 1e-4);

            input.MonthsSinceIntro = 24;
            Assert.Near(0.0, RatingEffectModel.Compute(input, balance).Pchurn, Eps);

            input.MonthsSinceIntro = 100;
            Assert.Near(0.0, RatingEffectModel.Compute(input, balance).Pchurn, Eps);
        }

        [Test]
        public void Compute_NoSeverity_NoPenalty()
        {
            PlatformBalance balance = Balance();
            var input = new RatingInput { TotalIb = 100, ChurnSeverity = 0f, MonthsSinceIntro = 0 };
            Assert.Equal(0f, RatingEffectModel.Compute(input, balance).Pchurn);
        }

        [Test]
        public void Compute_ASeverityAboveOne_IsCutToOne()
        {
            PlatformBalance balance = Balance();
            var input = new RatingInput { TotalIb = 100, ChurnSeverity = 5f, MonthsSinceIntro = 0 };
            Assert.Near(balance.Rating.ChurnPenalty, RatingEffectModel.Compute(input, balance).Pchurn, Eps);
        }

        // ---- the parts together, the weight of the segments ----

        [Test]
        public void Compute_AddsTheBonusesAndSubtractsThePenalty()
        {
            PlatformBalance balance = Balance();
            var input = new RatingInput
            {
                OwnIb = 20, SocketIb = 20, TotalIb = 100, IsStandard = false,
                IsEarlyAdopter = true, MonthsSinceEarly = 12,
                ChurnSeverity = 0.5f, MonthsSinceIntro = 12,
            };

            RatingBreakdown r = RatingEffectModel.Compute(input, balance);

            double bib = 0.10 * (0.20 / 0.40);
            double early = 0.06 * (1 - 12.0 / 60.0);
            double churn = 0.10 * 0.5 * (1 - 12.0 / 24.0);
            Assert.Near(1 + bib + early - churn, r.ConsumerMult, Eps);
            Assert.Near(1 + 0.5 * (bib + early - churn), r.IndustriesMult, Eps);
        }

        [Test]
        public void Compute_IndustriesGetsTheConfiguredWeight()
        {
            PlatformBalance balance = Balance();
            balance.Rating.IndustriesWeight = 0.25f;
            RatingBreakdown r = RatingEffectModel.Compute(Base(40, 40, 100, false), balance);
            Assert.Near(1.10, r.ConsumerMult, Eps);
            Assert.Near(1.025, r.IndustriesMult, Eps);
        }

        // ---- the range of the contribution ----

        [Test]
        public void Compute_TheContributionOfTheDefaultBalance_StaysInTheDocumentedRange()
        {
            PlatformBalance balance = Balance();

            // The best case: the saturated base and a fresh early adopter bonus, no penalty: x1.16 (Industries x1.08).
            var best = new RatingInput { OwnIb = 90, SocketIb = 90, TotalIb = 100, IsEarlyAdopter = true, MonthsSinceEarly = 0 };
            RatingBreakdown top = RatingEffectModel.Compute(best, balance);
            Assert.Near(1.16, top.ConsumerMult, Eps);
            Assert.Near(1.08, top.IndustriesMult, Eps);

            // The worst case: no base, a fresh penalty at the full severity: x0.90 (Industries x0.95).
            var worst = new RatingInput { TotalIb = 100, ChurnSeverity = 1f, MonthsSinceIntro = 0 };
            RatingBreakdown bottom = RatingEffectModel.Compute(worst, balance);
            Assert.Near(0.90, bottom.ConsumerMult, Eps);
            Assert.Near(0.95, bottom.IndustriesMult, Eps);
        }

        [Test]
        public void Compute_AnySweepOfInputs_StaysInsideTheRangeAndTheGuardClamps()
        {
            PlatformBalance balance = Balance();
            var rng = new LedgerTestData.Lcg(5u);
            for (int i = 0; i < 2000; i++)
            {
                var input = new RatingInput
                {
                    OwnIb = rng.Next(100),
                    SocketIb = rng.Next(200),
                    TotalIb = 1 + rng.Next(300),
                    IsStandard = rng.Next(2) == 0,
                    IsEarlyAdopter = rng.Next(2) == 0,
                    MonthsSinceEarly = rng.Next(80),
                    ChurnSeverity = rng.Next(101) / 100f,
                    MonthsSinceIntro = rng.Next(40),
                };

                RatingBreakdown r = RatingEffectModel.Compute(input, balance);
                Assert.InRange(r.ConsumerMult, 0.90f - 1e-5f, 1.16f + 1e-5f, "consumer #" + i);
                Assert.InRange(r.IndustriesMult, 0.95f - 1e-5f, 1.08f + 1e-5f, "industries #" + i);
                Assert.InRange(r.ConsumerMult, balance.Rating.MinMult, balance.Rating.MaxMult);
            }
        }

        [Test]
        public void Compute_TheGuardClamps_HoldAgainstAnOverrideOfTheBalance()
        {
            PlatformBalance balance = Balance();
            balance.Rating.IbMaxBonus = 0.5f;
            balance.Rating.EarlyBonus = 0.5f;
            balance.Rating.ChurnPenalty = 0.5f;
            balance.Rating.MaxMult = 1.2f;
            balance.Rating.MinMult = 0.8f;

            var good = new RatingInput { OwnIb = 90, SocketIb = 90, TotalIb = 100, IsEarlyAdopter = true };
            Assert.Near(1.2, RatingEffectModel.Compute(good, balance).ConsumerMult, Eps);

            var bad = new RatingInput { TotalIb = 100, ChurnSeverity = 1f };
            Assert.Near(0.8, RatingEffectModel.Compute(bad, balance).ConsumerMult, Eps);
        }

        [Test]
        public void Compute_WithoutABalance_UsesTheDefaults()
        {
            RatingBreakdown r = RatingEffectModel.Compute(Base(30, 30, 100, false), null);
            Assert.Near(1.075, r.ConsumerMult, Eps);
            Assert.Near(0.30, RatingEffectModel.EffShare(30, 30, 100, false, null), Eps);
        }
    }
}
