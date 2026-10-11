using System;
using System.Collections.Generic;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-003: the chances of a contract. The numbers of the examples are those of design.md ("Формулы и баланс") and requirements.md (AC-2.1-2.4, AC-11.1):
    /// 60/40 gives 78.9/21.1 %, 50/30/20 gives 69.4/20.8/9.7 %, 1000/1 gives 100/0 %, 50/50 gives 66.7/33.3 % to the company that offered first.
    /// </summary>
    [Covers(typeof(OddsMath), typeof(OfferInput), typeof(OfferOdds))]
    public class OddsMathTests
    {
        private const double Precision = 1e-9;

        private static OfferInput Offer(string company, int cpuId, float ips, int seen = 0)
        {
            return new OfferInput(company, cpuId, ips, seen);
        }

        private static List<OfferOdds> Table(params OfferInput[] offers)
        {
            return OddsMath.DrawOrder(OddsMath.BestPerCompany(offers), null);
        }

        // ---- the examples of the spec ----

        [Test]
        public void SixtyAndForty_GiveTheShares_78_9_And_21_1()
        {
            List<OfferOdds> rows = Table(Offer("ai:1", 10, 60f), Offer("player", 20, 40f));

            Assert.Equal(2, rows.Count);
            Assert.Equal(10, rows[0].CpuId);
            Assert.Equal(60, rows[0].GameChance);
            Assert.Equal(60, rows[0].VanillaChance);
            Assert.Near(0.6 / 0.76, rows[0].ActualProbability, Precision);
            Assert.Near(78.9, rows[0].ActualProbability * 100, 0.05);

            Assert.Equal(20, rows[1].CpuId);
            Assert.Equal(40, rows[1].GameChance);
            Assert.Near(0.16 / 0.76, rows[1].ActualProbability, Precision);
            Assert.Near(21.1, rows[1].ActualProbability * 100, 0.05);
        }

        [Test]
        public void FiftyThirtyTwenty_Give_69_4_20_8_9_7()
        {
            List<OfferOdds> rows = Table(Offer("a", 1, 50f), Offer("b", 2, 30f), Offer("player", 3, 20f));

            Assert.Equal(new[] { 50, 30, 20 }, new[] { rows[0].GameChance, rows[1].GameChance, rows[2].GameChance });
            Assert.Near(0.5 / 0.72, rows[0].ActualProbability, Precision);
            Assert.Near(0.3 * 0.5 / 0.72, rows[1].ActualProbability, Precision);
            Assert.Near(0.2 * 0.35 / 0.72, rows[2].ActualProbability, Precision);
            Assert.Near(69.4, rows[0].ActualProbability * 100, 0.05);
            Assert.Near(20.8, rows[1].ActualProbability * 100, 0.05);
            Assert.Near(9.7, rows[2].ActualProbability * 100, 0.05);
        }

        [Test]
        public void ThousandAndOne_GiveChances_100_And_1_ButProbabilities_100_And_0()
        {
            List<OfferOdds> rows = Table(Offer("ai:1", 1, 1000f), Offer("player", 2, 1f));

            Assert.Equal(100, rows[0].GameChance);
            Assert.Equal(1, rows[1].GameChance);
            Assert.Near(1.0, rows[0].ActualProbability, Precision);
            Assert.Near(0.0, rows[1].ActualProbability, Precision);
        }

        [Test]
        public void EqualIps_TheCompanyThatOfferedFirstIsDrawnFirst()
        {
            List<OfferOdds> rows = Table(Offer("first", 1, 50f), Offer("second", 2, 50f));

            Assert.Equal("first", rows[0].CompanyKey);
            Assert.Equal(0, rows[0].DrawPosition);
            Assert.Equal("second", rows[1].CompanyKey);
            Assert.Equal(1, rows[1].DrawPosition);
            Assert.Equal(50, rows[0].GameChance);
            Assert.Near(0.5 / 0.75, rows[0].ActualProbability, Precision);
            Assert.Near(0.25 / 0.75, rows[1].ActualProbability, Precision);
            Assert.Near(66.7, rows[0].ActualProbability * 100, 0.05);
            Assert.Near(33.3, rows[1].ActualProbability * 100, 0.05);
        }

        [Test]
        public void DrawOrder_BreaksTiesByFirstSeenIndex_NotByThePositionInTheInput()
        {
            // The same two offers handed over in the reverse order: the one that appeared first still goes first.
            var best = new List<OfferInput> { Offer("late", 2, 50f, 1), Offer("early", 1, 50f, 0) };

            List<OfferOdds> rows = OddsMath.DrawOrder(best, null);

            Assert.Equal("early", rows[0].CompanyKey);
            Assert.Equal("late", rows[1].CompanyKey);
        }

        [Test]
        public void DrawOrder_SortsByIpsFromTheLargest_AndKeepsTheVanillaColumn()
        {
            List<OfferOdds> rows = Table(Offer("low", 1, 20f), Offer("high", 2, 60f), Offer("mid", 3, 20f));

            Assert.Equal(new[] { 2, 1, 3 }, new[] { rows[0].CpuId, rows[1].CpuId, rows[2].CpuId });
            Assert.Equal(new[] { 0, 1, 2 }, new[] { rows[0].DrawPosition, rows[1].DrawPosition, rows[2].DrawPosition });
            Assert.Equal(new[] { 60, 20, 20 }, new[] { rows[0].VanillaChance, rows[1].VanillaChance, rows[2].VanillaChance });
        }

        // ---- the chances ----

        [Test]
        public void VanillaChance_IsClampedTo_1_And_100()
        {
            Assert.Equal(1, OddsMath.VanillaChance(1f, 1001f));      // 0.0999 -> 0 -> 1
            Assert.Equal(100, OddsMath.VanillaChance(1000f, 1001f)); // 99.9 -> 100
            Assert.Equal(100, OddsMath.VanillaChance(5f, 5f));
            Assert.Equal(100, OddsMath.VanillaChance(9f, 5f));       // an inconsistent sum cannot give more than 100
        }

        [Test]
        public void VanillaChance_RoundsAHalfToTheEvenNumber_LikeMathfRoundToInt()
        {
            Assert.Equal(12, OddsMath.VanillaChance(1f, 8f));   // 12.5 -> 12
            Assert.Equal(38, OddsMath.VanillaChance(3f, 8f));   // 37.5 -> 38
            Assert.Equal(62, OddsMath.VanillaChance(5f, 8f));   // 62.5 -> 62
            Assert.Equal(88, OddsMath.VanillaChance(7f, 8f));   // 87.5 -> 88
        }

        [Test]
        public void VanillaChance_OfZeroSum_IsTheMinimum_NotAnException()
        {
            Assert.Equal(1, OddsMath.VanillaChance(0f, 0f));
            Assert.Equal(1, OddsMath.VanillaChance(5f, 0f));
            Assert.Equal(1, OddsMath.VanillaChance(float.NaN, 10f));
        }

        [Test]
        public void EmptyAndMissingInput_GiveEmptyResults()
        {
            Assert.Equal(0, OddsMath.BestPerCompany(null).Count);
            Assert.Equal(0, OddsMath.BestPerCompany(new List<OfferInput>()).Count);
            Assert.Equal(0, OddsMath.DrawOrder(null, null).Count);
            Assert.Equal(0, OddsMath.DrawOrder(new List<OfferInput>(), new int[0]).Count);
            Assert.Equal(0, OddsMath.SequentialProbabilities(null).Length);
            Assert.Equal(0, OddsMath.SequentialProbabilities(new int[0]).Length);
            Assert.Equal(0f, OddsMath.SumIps(null));
            Assert.Equal(0f, OddsMath.MaxIps(null));
        }

        [Test]
        public void OneCompany_HasChance100_AndProbability1()
        {
            List<OfferOdds> rows = Table(Offer("player", 7, 12.5f));

            Assert.Equal(1, rows.Count);
            Assert.Equal(100, rows[0].VanillaChance);
            Assert.Equal(100, rows[0].GameChance);
            Assert.Near(1.0, rows[0].ActualProbability, Precision);
        }

        [Test]
        public void DrawOrder_WithGameChances_UsesThemForTheProbability_AndKeepsTheVanillaChance()
        {
            // Mods lifted the second company from 40 to 50: the game chances run parallel to the input, not to the draw order.
            var best = new List<OfferInput> { Offer("player", 20, 40f, 0), Offer("ai:1", 10, 60f, 1) };

            List<OfferOdds> rows = OddsMath.DrawOrder(best, new[] { 50, 60 });

            Assert.Equal(10, rows[0].CpuId);
            Assert.Equal(60, rows[0].GameChance);
            Assert.Equal(20, rows[1].CpuId);
            Assert.Equal(50, rows[1].GameChance);
            Assert.Equal(40, rows[1].VanillaChance);
            Assert.Near(0.75, rows[0].ActualProbability, Precision);
            Assert.Near(0.25, rows[1].ActualProbability, Precision);
        }

        [Test]
        public void DrawOrder_WithGameChancesOfAnotherLength_Throws()
        {
            var best = new List<OfferInput> { Offer("a", 1, 10f), Offer("b", 2, 20f) };

            Assert.Throws<ArgumentException>(() => OddsMath.DrawOrder(best, new[] { 50 }));
        }

        // ---- the sequential probabilities ----

        [Test]
        public void SequentialProbabilities_AddUpToOne_ToTheNinthDecimal()
        {
            var cases = new[]
            {
                new[] { 60, 40 },
                new[] { 50, 30, 20 },
                new[] { 100, 1 },
                new[] { 1, 1, 1, 1, 1, 1, 1 },
                new[] { 33, 33, 33 },
                new[] { 99, 98, 1, 1 },
            };
            foreach (int[] chances in cases)
            {
                double sum = 0;
                foreach (double p in OddsMath.SequentialProbabilities(chances)) sum += p;
                Assert.Near(1.0, sum, Precision, string.Join("/", chances));
            }
        }

        [Test]
        public void SequentialProbabilities_OfOnlyZeros_AreAllZero()
        {
            double[] p = OddsMath.SequentialProbabilities(new[] { 0, 0, 0 });

            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, p);
        }

        [Test]
        public void SequentialProbabilities_CutChancesToTheValidRange()
        {
            double[] over = OddsMath.SequentialProbabilities(new[] { 250, 50 });
            double[] negative = OddsMath.SequentialProbabilities(new[] { -5, 50 });

            Assert.Near(1.0, over[0], Precision);
            Assert.Near(0.0, over[1], Precision);
            Assert.Near(0.0, negative[0], Precision);
            Assert.Near(1.0, negative[1], Precision);
        }

        [Test]
        public void SequentialProbabilities_OfAChanceOf100_LeavesNothingForTheOffersBehindIt()
        {
            double[] p = OddsMath.SequentialProbabilities(new[] { 30, 100, 80 });

            Assert.Near(0.3, p[0], Precision);
            Assert.Near(0.7, p[1], Precision);
            Assert.Near(0.0, p[2], Precision);
        }

        // ---- the best offer of each company ----

        [Test]
        public void BestPerCompany_TakesTheFastestCpuOfEachCompany_InTheOrderOfTheFirstOffer()
        {
            var offers = new List<OfferInput>
            {
                Offer("ai:2", 1, 10f),
                Offer("player", 2, 30f),
                Offer("ai:2", 3, 25f),     // better than the first CPU of ai:2
                Offer("player", 4, 20f),   // worse than the first CPU of the player
                Offer("ai:9", 5, 5f),
            };

            List<OfferInput> best = OddsMath.BestPerCompany(offers);

            Assert.Equal(3, best.Count);
            Assert.Equal(new[] { "ai:2", "player", "ai:9" }, new[] { best[0].CompanyKey, best[1].CompanyKey, best[2].CompanyKey });
            Assert.Equal(new[] { 3, 2, 5 }, new[] { best[0].CpuId, best[1].CpuId, best[2].CpuId });
            Assert.Equal(new[] { 0, 1, 2 }, new[] { best[0].FirstSeenIndex, best[1].FirstSeenIndex, best[2].FirstSeenIndex });
            Assert.Equal(5, offers.Count, "the input is not changed");
        }

        [Test]
        public void BestPerCompany_OfTwoEqualCpusOfOneCompany_KeepsTheOneOfferedFirst()
        {
            var offers = new List<OfferInput> { Offer("ai:1", 7, 40f), Offer("ai:1", 8, 40f) };

            Assert.Equal(7, OddsMath.BestPerCompany(offers)[0].CpuId);
        }

        [Test]
        public void BestPerCompany_TreatsAMissingKeyAsOneCompany()
        {
            var offers = new List<OfferInput> { Offer(null, 1, 10f), Offer(null, 2, 20f) };

            List<OfferInput> best = OddsMath.BestPerCompany(offers);

            Assert.Equal(1, best.Count);
            Assert.Equal(2, best[0].CpuId);
        }

        // ---- the candidate ----

        [Test]
        public void WithCandidate_RemovesEveryCpuOfTheCandidatesCompany_AndPutsTheCandidateLast()
        {
            var offers = new List<OfferInput>
            {
                Offer("player", 1, 50f, 0),
                Offer("ai:3", 2, 40f, 1),
                Offer("player", 3, 30f, 2),
                Offer("ai:4", 4, 10f, 3),
            };

            List<OfferInput> result = OddsMath.WithCandidate(offers, Offer("player", 5, 35f));

            Assert.Equal(new[] { 2, 4, 5 }, new[] { result[0].CpuId, result[1].CpuId, result[2].CpuId });
            Assert.Equal(3, result.Count);
            Assert.True(result[2].FirstSeenIndex > result[1].FirstSeenIndex, "the candidate came last");
            Assert.Equal(4, offers.Count, "the input is not changed");
        }

        [Test]
        public void WithCandidate_ComparesCompaniesByKey_NotByName()
        {
            var offers = new List<OfferInput> { Offer("ai:1", 1, 10f), Offer("ai:10", 2, 20f), Offer("AI:1", 3, 30f) };

            List<OfferInput> result = OddsMath.WithCandidate(offers, Offer("ai:1", 4, 5f));

            Assert.Equal(new[] { 2, 3, 4 }, new[] { result[0].CpuId, result[1].CpuId, result[2].CpuId });
        }

        [Test]
        public void WithCandidate_OfNoOffers_IsTheCandidateAlone()
        {
            Assert.Equal(1, OddsMath.WithCandidate(null, Offer("player", 1, 10f)).Count);
            Assert.Equal(1, OddsMath.WithCandidate(new List<OfferInput>(), Offer("player", 1, 10f)).Count);
        }

        [Test]
        public void WithCandidate_TheCandidateReplacesTheStrongerCpuOfItsOwnCompany_AC_2_4()
        {
            // The player offered A (50) and B (30); an AI offered 40. The window for B asks "as if B replaced A".
            var offers = new List<OfferInput>
            {
                Offer("player", 100, 50f, 0),
                Offer("ai:1", 200, 40f, 1),
                Offer("player", 101, 30f, 2),
            };
            OfferInput b = Offer("player", 101, 30f, 2);

            List<OfferInput> best = OddsMath.BestPerCompany(OddsMath.WithCandidate(offers, b));
            List<OfferOdds> rows = OddsMath.DrawOrder(best, null);

            Assert.Equal(200, rows[0].CpuId);
            Assert.Equal(101, rows[1].CpuId);
            Assert.Equal(57, rows[0].GameChance);   // 40 / 70
            Assert.Equal(43, rows[1].GameChance);   // 30 / 70
            Assert.Near(0.57 / (1 - 0.43 * 0.57), rows[0].ActualProbability, Precision);
        }

        // ---- hints ----

        [Test]
        public void IpsForChance_Of50Percent_IsTheSumOfTheOthers()
        {
            Assert.Equal(60f, OddsMath.IpsForChance(0.5, 60f));
            Assert.Equal(0f, OddsMath.IpsForChance(0.5, 0f));
        }

        [Test]
        public void IpsForChance_FollowsShareOverOneMinusShare()
        {
            Assert.Near(40.0, OddsMath.IpsForChance(0.8, 10f), 1e-4);
            Assert.Near(10.0 / 3.0, OddsMath.IpsForChance(0.25, 10f), 1e-4);
            Assert.Equal(0f, OddsMath.IpsForChance(0.0, 10f));
            Assert.Equal(0f, OddsMath.IpsForChance(-1.0, 10f));
            Assert.True(float.IsPositiveInfinity(OddsMath.IpsForChance(1.0, 10f)));
            Assert.True(float.IsNaN(OddsMath.IpsForChance(double.NaN, 10f)));
        }

        [Test]
        public void IpsForChance_GivesTheShareItWasAskedFor()
        {
            float others = 73.5f;
            float ips = OddsMath.IpsForChance(0.5, others);

            Assert.Equal(50, OddsMath.VanillaChance(ips, ips + others));
        }

        [Test]
        public void SumIps_MaxIps_AndWithoutCompany_DescribeTheRivals()
        {
            var best = new List<OfferInput> { Offer("player", 1, 20f), Offer("ai:1", 2, 35f), Offer("ai:2", 3, 25f) };

            List<OfferInput> rivals = OddsMath.WithoutCompany(best, "player");

            Assert.Equal(2, rivals.Count);
            Assert.Equal(60f, OddsMath.SumIps(rivals));
            Assert.Equal(35f, OddsMath.MaxIps(rivals));
            Assert.Equal(3, OddsMath.WithoutCompany(best, "nobody").Count);
        }

        [Test]
        public void ShadowedOwn_NamesTheCpusOfTheCompanyThatTheReviewDoesNotCount_AC_2_4()
        {
            var offers = new List<OfferInput>
            {
                Offer("player", 100, 50f, 0),
                Offer("ai:1", 200, 40f, 1),
                Offer("player", 101, 30f, 2),
            };

            Assert.Equal(new[] { 101 }, OddsMath.ShadowedOwn(offers, Offer("player", 101, 30f)).ToArray());
            Assert.Equal(100, OddsMath.StrongestOwnCpuId(offers, Offer("player", 101, 30f)));
        }

        [Test]
        public void ShadowedOwn_CountsACandidateThatIsNotOfferedYet()
        {
            var offers = new List<OfferInput> { Offer("player", 100, 30f, 0) };

            // A faster candidate would be the one that counts, the offered one would be shadowed.
            Assert.Equal(new[] { 100 }, OddsMath.ShadowedOwn(offers, Offer("player", 101, 50f)).ToArray());
            Assert.Equal(101, OddsMath.StrongestOwnCpuId(offers, Offer("player", 101, 50f)));

            // A slower candidate is the shadowed one.
            Assert.Equal(new[] { 101 }, OddsMath.ShadowedOwn(offers, Offer("player", 101, 10f)).ToArray());
            Assert.Equal(100, OddsMath.StrongestOwnCpuId(offers, Offer("player", 101, 10f)));
        }

        [Test]
        public void ShadowedOwn_OfALoneCandidate_IsEmpty()
        {
            var offers = new List<OfferInput> { Offer("ai:1", 200, 40f, 0) };

            Assert.Equal(0, OddsMath.ShadowedOwn(offers, Offer("player", 101, 30f)).Count);
            Assert.Equal(101, OddsMath.StrongestOwnCpuId(offers, Offer("player", 101, 30f)));
            Assert.Equal(0, OddsMath.ShadowedOwn(null, Offer("player", 101, 30f)).Count);
        }

        // ---- the estimate of FR-11 ----

        [Test]
        public void PessimisticProbability_AddsThePossibleBiddersAndRecomputes_AC_11_1()
        {
            // No offers yet (the candidate is alone: 100 %); two AI companies could bid with 50 and 70 against the player's 60.
            var best = new List<OfferInput> { Offer("player", 1, 60f) };
            var potential = new List<OfferInput> { Offer("ai:1", 2, 50f), Offer("ai:2", 3, 70f) };

            double p = OddsMath.PessimisticProbability(best, potential, "player", 1);

            // Chances 39 / 33 / 28 in the order 70, 60, 50.
            Assert.Near(0.33 * 0.61 / (1 - 0.61 * 0.67 * 0.72), p, Precision);
            Assert.Near(0.285234, p, 1e-6);
        }

        [Test]
        public void PessimisticProbability_WithoutPossibleBidders_IsTheCurrentProbability()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f, 0), Offer("player", 1, 40f, 1) };
            double now = 0;
            foreach (OfferOdds row in OddsMath.DrawOrder(best, null))
            {
                if (row.CpuId == 1) now = row.ActualProbability;
            }

            Assert.Near(now, OddsMath.PessimisticProbability(best, null, "player", 1), Precision);
            Assert.Near(now, OddsMath.PessimisticProbability(best, new List<OfferInput>(), "player", 1), Precision);
        }

        [Test]
        public void PessimisticProbability_ABetterCpuOfACompanyAlreadyInPlayReplacesItsOffer()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 30f, 0), Offer("player", 1, 30f, 1) };
            var potential = new List<OfferInput> { Offer("ai:1", 3, 90f) };

            double p = OddsMath.PessimisticProbability(best, potential, "player", 1);

            // ai:1 now bids 90 against 30: chances 75 / 25 in that order.
            Assert.Near(0.25 * 0.25 / (1 - 0.25 * 0.75), p, Precision);
        }

        [Test]
        public void PessimisticProbability_IgnoresPossibleBiddersOfTheCandidatesOwnCompany()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f, 0), Offer("player", 1, 40f, 1) };
            var potential = new List<OfferInput> { Offer("player", 9, 500f) };

            double with = OddsMath.PessimisticProbability(best, potential, "player", 1);
            double without = OddsMath.PessimisticProbability(best, null, "player", 1);

            Assert.Near(without, with, Precision);
        }

        [Test]
        public void PessimisticProbability_AppliesTheModMultiplierToTheCandidateOnly()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f, 0), Offer("player", 1, 40f, 1) };

            double plain = OddsMath.PessimisticProbability(best, null, "player", 1);
            double lifted = OddsMath.PessimisticProbability(best, null, "player", 1, 1.25);

            // 40 % x 1.25 = 50 % against 60 %: 0.5 * 0.4 / (1 - 0.4 * 0.5)
            Assert.Near(0.5 * 0.4 / (1 - 0.4 * 0.5), lifted, Precision);
            Assert.True(lifted > plain, "a bonus helps");
            Assert.Near(plain, OddsMath.PessimisticProbability(best, null, "player", 1, double.NaN), Precision);
        }

        [Test]
        public void PessimisticProbability_OfACandidateThatIsNotInTheList_Throws()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f) };

            Assert.Throws<ArgumentException>(() => OddsMath.PessimisticProbability(best, null, "player", 1));
        }
    }
}
