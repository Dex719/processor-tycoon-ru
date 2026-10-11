using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-017, FR-11 (AC-11.1): the estimate of the possible participants. Which AI CPUs may still bid (the rule of <c>AIBehaviourContract.OnTick</c> and the conditions of the panel), how many monthly moves
    /// of the AI are left before the review, which of the possible offers change anything, the pessimistic probability with all of them in, and the lines of the block with the notes "estimate" and
    /// "on difficulty 5 the AI is more active". The adapter that asks the game is read from the sources.
    /// </summary>
    [Covers(typeof(AiBidRules), typeof(AiBidVerdict), typeof(AiCpuFact), typeof(AiMoveCalendar), typeof(PotentialBidders), typeof(PotentialEstimate),
        typeof(PotentialLines), typeof(PotentialPanelInput), typeof(PotentialRow))]
    public class PotentialBiddersTests
    {
        private const double Precision = 1e-9;

        private static OfferInput Offer(string company, int cpu, float ips, int seen = 0)
        {
            return new OfferInput(company, cpu, ips, seen);
        }

        private static AiCpuFact Fact(string company, int cpu, float ips, float payment = 100f, float cost = 40f)
        {
            return new AiCpuFact
            {
                CompanyKey = company,
                CpuId = cpu,
                Ips = ips,
                PaymentPerUnit = payment,
                UnitCostAtPayment = () => cost,
            };
        }

        // ---- the margin of the AI ----

        [Test]
        public void Margin_IsThePriceMinusTheCostOverThePrice()
        {
            Assert.Near(0.6, AiBidRules.Margin(100f, 40f), 1e-6);
            Assert.Near(0.35, AiBidRules.Margin(200f, 130f), 1e-6);
            Assert.True(AiBidRules.Margin(100f, 130f) < 0f, "a CPU that costs more than the price has a negative margin");
        }

        [Test]
        public void MarginPasses_EqualToTheThresholdPasses_BelowDoesNot()
        {
            // The literal of the game is 0.45f: a margin of exactly 45 % is not skipped.
            Assert.True(AiBidRules.MarginPasses(AiBidRules.Margin(100f, 55f), 0.45));
            Assert.True(AiBidRules.MarginPasses(0.46f, 0.45));
            Assert.False(AiBidRules.MarginPasses(0.449f, 0.45));
            Assert.False(AiBidRules.MarginPasses(AiBidRules.Margin(100f, 55.01f), 0.45));
            Assert.False(AiBidRules.MarginPasses(-0.3f, 0.45));
        }

        [Test]
        public void MarginPasses_UsesTheThresholdOfTheRules_NotAFixedOne()
        {
            Assert.True(AiBidRules.MarginPasses(0.3f, 0.25));
            Assert.False(AiBidRules.MarginPasses(0.3f, 0.35));
            Assert.True(AiBidRules.MarginPasses(0f, 0.0), "a threshold of nothing lets every margin through");
        }

        [Test]
        public void MarginPasses_ANotANumberIsNotSkipped_AsInTheGame()
        {
            // The game skips a CPU when margin < 0.45f; a NaN is not smaller, so it is not skipped. Zero price and zero cost is the only way to get one.
            Assert.True(float.IsNaN(AiBidRules.Margin(0f, 0f)));
            Assert.True(AiBidRules.MarginPasses(float.NaN, 0.45));
            Assert.False(AiBidRules.MarginPasses(AiBidRules.Margin(0f, 10f), 0.45), "a free contract with a cost is minus infinity");
        }

        // ---- the verdict on one CPU ----

        [Test]
        public void Check_AFittingCpuQualifies()
        {
            Assert.Equal(AiBidVerdict.Qualifies, AiBidRules.Check(Fact("ai:1", 10, 50f), 0.45));
        }

        [Test]
        public void Check_NamesTheFirstReasonThatRulesTheCpuOut()
        {
            AiCpuFact retired = Fact("ai:1", 10, 50f);
            retired.IsRetired = true;
            retired.SellOnContracts = false;
            retired.FailsRequirements = () => true;
            Assert.Equal(AiBidVerdict.Retired, AiBidRules.Check(retired, 0.45), "withdrawn comes first");

            AiCpuFact notForContracts = Fact("ai:1", 10, 50f);
            notForContracts.SellOnContracts = false;
            notForContracts.FailsRequirements = () => true;
            Assert.Equal(AiBidVerdict.NotForContracts, AiBidRules.Check(notForContracts, 0.45));

            AiCpuFact fails = Fact("ai:1", 10, 50f, 100f, 99f);
            fails.FailsRequirements = () => true;
            Assert.Equal(AiBidVerdict.FailsRequirements, AiBidRules.Check(fails, 0.45), "the requirements before the margin");

            Assert.Equal(AiBidVerdict.MarginTooLow, AiBidRules.Check(Fact("ai:1", 10, 50f, 100f, 60f), 0.45));
        }

        [Test]
        public void Check_AsksTheExpensiveQuestionsOnlyWhenTheCheapOnesPass()
        {
            int requirementCalls = 0;
            int costCalls = 0;

            AiCpuFact retired = Fact("ai:1", 10, 50f);
            retired.IsRetired = true;
            retired.FailsRequirements = () => { requirementCalls++; return false; };
            retired.UnitCostAtPayment = () => { costCalls++; return 1f; };
            AiBidRules.Check(retired, 0.45);
            Assert.Equal(0, requirementCalls);
            Assert.Equal(0, costCalls);

            AiCpuFact fails = Fact("ai:1", 10, 50f);
            fails.FailsRequirements = () => { requirementCalls++; return true; };
            fails.UnitCostAtPayment = () => { costCalls++; return 1f; };
            AiBidRules.Check(fails, 0.45);
            Assert.Equal(1, requirementCalls);
            Assert.Equal(0, costCalls, "the cost of a unit is not worked out for a CPU that fails a requirement");

            AiCpuFact fits = Fact("ai:1", 10, 50f);
            fits.FailsRequirements = () => { requirementCalls++; return false; };
            fits.UnitCostAtPayment = () => { costCalls++; return 1f; };
            AiBidRules.Check(fits, 0.45);
            Assert.Equal(2, requirementCalls);
            Assert.Equal(1, costCalls);
        }

        [Test]
        public void Check_WithoutTheQuestionsTheCpuIsTakenAsFitting_AndNullIsAnError()
        {
            var bare = new AiCpuFact { CompanyKey = "ai:1", CpuId = 1, Ips = 1f, PaymentPerUnit = 100f };

            Assert.Equal(AiBidVerdict.Qualifies, AiBidRules.Check(bare, 0.45), "no requirement check and a cost of nothing: the whole price is margin");
            Assert.Throws<ArgumentNullException>(() => AiBidRules.Check(null, 0.45));
        }

        // ---- the best qualifying CPU of each company ----

        [Test]
        public void BestQualifying_OneRowPerCompany_TheFastestFittingCpu_InTheOrderOfTheFirst()
        {
            var facts = new List<AiCpuFact>
            {
                Fact("ai:1", 10, 50f),
                Fact("ai:1", 11, 80f),
                Fact("ai:2", 20, 70f),
                Fact("ai:1", 12, 60f),
                Fact("ai:3", 30, 90f, 100f, 70f),                       // margin 30 %: too low
                Fact("ai:5", 50, 55f),
            };
            facts[1].IsRetired = true;                                   // the fastest of ai:1 is withdrawn
            facts[5].FailsRequirements = () => true;                     // ai:5 fails a requirement
            facts.Add(Fact("ai:4", 40, 40f));
            facts[6].SellOnContracts = false;                            // ai:4 does not sell on contracts

            List<OfferInput> result = AiBidRules.BestQualifying(facts, 0.45);

            Assert.Equal(2, result.Count);
            Assert.Equal("ai:1", result[0].CompanyKey);
            Assert.Equal(12, result[0].CpuId, "the fastest fitting CPU of ai:1");
            Assert.Equal(60f, result[0].Ips);
            Assert.Equal("ai:2", result[1].CompanyKey);
            Assert.Equal(20, result[1].CpuId);
            Assert.Equal(0, result[0].FirstSeenIndex);
            Assert.Equal(1, result[1].FirstSeenIndex);
        }

        [Test]
        public void BestQualifying_OfEqualSpeed_TheFirstStays()
        {
            List<OfferInput> result = AiBidRules.BestQualifying(new[] { Fact("ai:1", 10, 50f), Fact("ai:1", 11, 50f) }, 0.45);

            Assert.Equal(1, result.Count);
            Assert.Equal(10, result[0].CpuId);
        }

        [Test]
        public void BestQualifying_NothingFitsOrNothingGiven_IsEmpty()
        {
            Assert.Equal(0, AiBidRules.BestQualifying(null, 0.45).Count);
            Assert.Equal(0, AiBidRules.BestQualifying(new List<AiCpuFact>(), 0.45).Count);
            Assert.Equal(0, AiBidRules.BestQualifying(new[] { Fact("ai:1", 10, 50f, 100f, 90f) }, 0.45).Count);
            Assert.Equal(0, AiBidRules.BestQualifying(new AiCpuFact[] { null }, 0.45).Count, "a missing fact is skipped");
        }

        [Test]
        public void BestQualifying_ACpuTheGameCannotAnswerFor_IsReported_AndLeftOut()
        {
            AiCpuFact broken = Fact("ai:2", 20, 99f);
            broken.FailsRequirements = () => { throw new InvalidOperationException("the game says no"); };
            var facts = new[] { Fact("ai:1", 10, 50f), broken, Fact("ai:3", 30, 40f) };

            var reported = new List<int>();
            List<OfferInput> result = AiBidRules.BestQualifying(facts, 0.45, (fact, ex) =>
            {
                reported.Add(fact.CpuId);
                Assert.True(ex is InvalidOperationException);
            });

            Assert.Equal(new[] { 20 }, reported.ToArray());
            Assert.Equal(new[] { 10, 30 }, result.Select(r => r.CpuId).ToArray(), "the others are not lost");

            Assert.Throws<InvalidOperationException>(() => AiBidRules.BestQualifying(facts, 0.45), "without a handler the failure is not hidden");
        }

        // ---- the moves of the AI before the review ----

        [Test]
        public void MovesBefore_CountsTheFirstDaysOfTheMonthsBetweenTodayAndTheReviewMonth()
        {
            // Review in January 1987: the AI moves on 1 December and 1 November, but the move of 1 January comes after the contract is drawn.
            Assert.Equal(2, AiMoveCalendar.MovesBefore(new DateTime(1986, 10, 20), 1987, 1));
            Assert.Equal(1, AiMoveCalendar.MovesBefore(new DateTime(1986, 11, 20), 1987, 1));
            Assert.Equal(1, AiMoveCalendar.MovesBefore(new DateTime(1986, 11, 1), 1987, 1), "the move of today is done");
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1986, 12, 15), 1987, 1));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1986, 12, 31), 1987, 1), "the last day before the review: only the move of the review day is left, and it is late");
        }

        [Test]
        public void MovesBefore_AFullYearAhead_IsEleven()
        {
            Assert.Equal(11, AiMoveCalendar.MovesBefore(new DateTime(1986, 1, 1), 1987, 1));
            Assert.Equal(11, AiMoveCalendar.MovesBefore(new DateTime(1986, 1, 31), 1987, 1));
            Assert.Equal(10, AiMoveCalendar.MovesBefore(new DateTime(1986, 2, 1), 1987, 1));
        }

        [Test]
        public void MovesBefore_ThePresentOrThePastReviewMonth_IsNone()
        {
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1987, 1, 1), 1987, 1));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1987, 1, 20), 1987, 1));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1987, 3, 1), 1987, 1));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1990, 1, 1), 1987, 1));
        }

        [Test]
        public void MovesBefore_ADateThatIsNotAMonth_IsNone()
        {
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1986, 1, 1), 1987, 0));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1986, 1, 1), 1987, 13));
            Assert.Equal(0, AiMoveCalendar.MovesBefore(new DateTime(1986, 1, 1), 0, 5));
        }

        [Test]
        public void MovesBefore_OtherReviewMonths_AcrossTheYearBoundary()
        {
            // A contract another feature added in June, reviewed in June 1987.
            Assert.Equal(4, AiMoveCalendar.MovesBefore(new DateTime(1987, 1, 10), 1987, 6));
            Assert.Equal(6, AiMoveCalendar.MovesBefore(new DateTime(1986, 11, 10), 1987, 6));
        }

        // ---- which possible offers matter ----

        [Test]
        public void Relevant_ACompanyWithoutAnOffer_IsListed_AC_11_1()
        {
            var best = new List<OfferInput> { Offer("player", 1, 60f) };
            var potential = new List<OfferInput> { Offer("ai:1", 2, 50f), Offer("ai:2", 3, 70f) };

            List<OfferInput> relevant = PotentialBidders.Relevant(best, potential, "player");

            Assert.Equal(new[] { 2, 3 }, relevant.Select(r => r.CpuId).ToArray(), "slower or faster than the player's CPU does not matter: both would take a share");
        }

        [Test]
        public void Relevant_ACompanyAlreadyInPlay_IsListedOnlyWithAFasterCpu()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 30f, 0), Offer("player", 1, 30f, 1) };

            Assert.Equal(0, PotentialBidders.Relevant(best, new List<OfferInput> { Offer("ai:1", 3, 20f) }, "player").Count, "slower: the game keeps the offer it has");
            Assert.Equal(0, PotentialBidders.Relevant(best, new List<OfferInput> { Offer("ai:1", 3, 30f) }, "player").Count, "equal: the earlier offer stays");
            Assert.Equal(1, PotentialBidders.Relevant(best, new List<OfferInput> { Offer("ai:1", 3, 31f) }, "player").Count);
        }

        [Test]
        public void Relevant_NeverTheCandidatesOwnCompany_AndToleratesNothing()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f) };

            Assert.Equal(0, PotentialBidders.Relevant(best, new List<OfferInput> { Offer("player", 9, 500f) }, "player").Count);
            Assert.Equal(0, PotentialBidders.Relevant(best, null, "player").Count);
            Assert.Equal(1, PotentialBidders.Relevant(null, new List<OfferInput> { Offer("ai:3", 4, 10f) }, "player").Count, "with no offers at all everyone is new");
        }

        // ---- the pessimistic probability ----

        [Test]
        public void Estimate_AC_11_1_TwoAiCompaniesAgainstTheOnlyOffer()
        {
            // The contract has no offers but the player's (100 %); two AI companies could bid with 50 and 70 against the player's 60.
            var best = new List<OfferInput> { Offer("player", 1, 60f) };
            var potential = new List<OfferInput> { Offer("ai:1", 2, 50f), Offer("ai:2", 3, 70f) };

            PotentialEstimate estimate = PotentialBidders.Estimate(best, potential, "player", 1, 1.0, 1.0);

            Assert.Equal(2, estimate.NewBidders.Count, "possible participants: 2");
            Assert.Near(1.0, estimate.CurrentActual, Precision);
            Assert.Near(0.285234, estimate.PessimisticActual, 1e-6, "calculated from three offers: chances 39 / 33 / 28 in the order 70, 60, 50");
            Assert.Near(OddsMath.PessimisticProbability(best, potential, "player", 1), estimate.PessimisticActual, Precision);
        }

        [Test]
        public void Estimate_WhenNobodyNewCanCome_ThePessimisticProbabilityIsTheCurrentOne()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 60f, 0), Offer("player", 1, 40f, 1) };
            var potential = new List<OfferInput> { Offer("ai:1", 3, 10f), Offer("player", 9, 900f) };

            PotentialEstimate estimate = PotentialBidders.Estimate(best, potential, "player", 1, 0.2105, 1.0);

            Assert.Equal(0, estimate.NewBidders.Count);
            Assert.Near(0.2105, estimate.PessimisticActual, Precision, "exactly the figure shown above, not a recomputed one");
        }

        [Test]
        public void Estimate_AFasterCpuOfACompanyAlreadyInPlayReplacesItsOffer()
        {
            var best = new List<OfferInput> { Offer("ai:1", 2, 30f, 0), Offer("player", 1, 30f, 1) };
            var potential = new List<OfferInput> { Offer("ai:1", 3, 90f) };

            PotentialEstimate estimate = PotentialBidders.Estimate(best, potential, "player", 1, 0.5, 1.0);

            Assert.Equal(1, estimate.NewBidders.Count);
            Assert.Near(0.25 * 0.25 / (1 - 0.25 * 0.75), estimate.PessimisticActual, Precision);
        }

        [Test]
        public void Estimate_TheModMultiplierIsOnTheCandidateOnly()
        {
            var best = new List<OfferInput> { Offer("player", 1, 60f) };
            var potential = new List<OfferInput> { Offer("ai:1", 2, 60f) };

            double plain = PotentialBidders.Estimate(best, potential, "player", 1, 1.0, 1.0).PessimisticActual;
            double boosted = PotentialBidders.Estimate(best, potential, "player", 1, 1.0, 1.5).PessimisticActual;

            Assert.True(boosted > plain, "a multiplier above one helps the candidate");
            Assert.Near(OddsMath.PessimisticProbability(best, potential, "player", 1, 1.5), boosted, Precision);
        }

        // ---- the lines of the block ----

        private static PotentialPanelInput TwoBidders()
        {
            return new PotentialPanelInput
            {
                MovesLeft = 2,
                CurrentActual = 1.0,
                PessimisticActual = 0.285234,
                Rows = new List<PotentialRow>
                {
                    new PotentialRow { Company = "AMD", Cpu = "Am286", Ips = 50000f },
                    new PotentialRow { Company = "Intel", Cpu = "i386", Ips = 70000f },
                },
            };
        }

        private static string[] Keys(List<PanelLine> lines)
        {
            return lines.Select(l => l.Key).ToArray();
        }

        [Test]
        public void Lines_AC_11_1_TheBlockListsThePossibleParticipantsAndThePessimisticProbability()
        {
            List<PanelLine> lines = PotentialLines.Build(TwoBidders());

            Assert.Equal(
                new[] { "ct.odds.potential.title", "ct.odds.potential.moves", "ct.odds.potential.row", "ct.odds.potential.row", "ct.odds.potential.pessimistic", "ct.odds.potential.note" },
                Keys(lines));

            Assert.True(lines[0].Bold);
            Assert.Equal(ArgKind.Whole, lines[0].Args[0].Kind);
            Assert.Equal(2.0, lines[0].Args[0].Number, "possible participants: 2");
            Assert.Equal(2.0, lines[1].Args[0].Number, "the moves of the AI left");
        }

        [Test]
        public void Lines_TheFastestCpuIsListedFirst_RowsOfEqualSpeedKeepTheirOrder()
        {
            PotentialPanelInput input = TwoBidders();
            input.Rows.Add(new PotentialRow { Company = "Zilog", Cpu = "Z80", Ips = 50000f });

            List<PanelLine> rows = PotentialLines.Build(input).Where(l => l.Key == "ct.odds.potential.row").ToList();
            var plain = new PlainArgFormatter();

            Assert.Equal(new[] { "Intel", "AMD", "Zilog" }, rows.Select(r => r.Format(plain)[0]).ToArray());
            Assert.Equal("i386", rows[0].Format(plain)[1]);
            Assert.Equal(ArgKind.Ips, rows[0].Args[2].Kind);
            Assert.Equal(70000.0, rows[0].Args[2].Number);
            Assert.Equal(1, rows[0].Indent);
        }

        [Test]
        public void Lines_ThePessimisticLineShowsBothFigures_AndIsRedWhenItIsLow()
        {
            PanelLine normal = PotentialLines.Build(TwoBidders()).First(l => l.Key == "ct.odds.potential.pessimistic");
            var plain = new PlainArgFormatter();

            Assert.Equal(LineTone.Normal, normal.Tone);
            Assert.Equal(new[] { "29", "100" }, normal.Format(plain), "your probability if they all bid, and the one now");
            Assert.Equal(ArgKind.Actual, normal.Args[0].Kind);
            Assert.Equal(ArgKind.Actual, normal.Args[1].Kind);

            PotentialPanelInput low = TwoBidders();
            low.PessimisticActual = 0.05;
            Assert.Equal(LineTone.Bad, PotentialLines.Build(low).First(l => l.Key == "ct.odds.potential.pessimistic").Tone, "under the threshold of risk.lowActualChance");

            low.PessimisticActual = 0.10;
            Assert.Equal(LineTone.Normal, PotentialLines.Build(low).First(l => l.Key == "ct.odds.potential.pessimistic").Tone, "equal to the threshold is not low");

            low.LowActualChance = 0.2;
            Assert.Equal(LineTone.Bad, PotentialLines.Build(low).First(l => l.Key == "ct.odds.potential.pessimistic").Tone, "the threshold of the rules is used");
        }

        [Test]
        public void Lines_TheNoteSaysItIsAnEstimate_AndDifficultyFiveAddsItsOwnNote()
        {
            List<PanelLine> normal = PotentialLines.Build(TwoBidders());
            Assert.Equal(LineTone.Muted, normal.First(l => l.Key == "ct.odds.potential.note").Tone);
            Assert.False(normal.Any(l => l.Key == "ct.odds.potential.hard"), "no note about the AI on an ordinary difficulty");

            PotentialPanelInput hard = TwoBidders();
            hard.HardDifficulty = true;
            List<PanelLine> lines = PotentialLines.Build(hard);
            Assert.Equal("ct.odds.potential.hard", lines[lines.Count - 1].Key, "the last line");
            Assert.Equal(LineTone.Warn, lines[lines.Count - 1].Tone);
            Assert.True(lines.Any(l => l.Key == "ct.odds.potential.note"), "the estimate note stays");
        }

        [Test]
        public void Lines_WithNobodyNew_SayNoOneComes_AndGiveNoPessimisticFigure()
        {
            var input = new PotentialPanelInput { MovesLeft = 3, CurrentActual = 0.4, PessimisticActual = 0.4, Rows = new List<PotentialRow>() };

            List<PanelLine> lines = PotentialLines.Build(input);

            Assert.Equal(new[] { "ct.odds.potential.title", "ct.odds.potential.moves", "ct.odds.potential.none", "ct.odds.potential.note" }, Keys(lines));
            Assert.Equal(0.0, lines[0].Args[0].Number);

            input.Rows = null;
            Assert.Equal(Keys(lines), Keys(PotentialLines.Build(input)), "no list is the same as an empty one");
        }

        [Test]
        public void Lines_WithNoMoveLeft_TheBlockIsNotShown()
        {
            PotentialPanelInput input = TwoBidders();

            input.MovesLeft = 0;
            Assert.Equal(0, PotentialLines.Build(input).Count);
            input.MovesLeft = -3;
            Assert.Equal(0, PotentialLines.Build(input).Count);
            Assert.Equal(0, PotentialLines.Build(null).Count);
        }

        [Test]
        public void Lines_ALongListIsCut_WithTheNumberOfTheRest()
        {
            var input = TwoBidders();
            input.Rows.Clear();
            for (int i = 0; i < 5; i++) input.Rows.Add(new PotentialRow { Company = "C" + i, Cpu = "P" + i, Ips = 1000f * (i + 1) });
            input.MaxRows = 3;

            List<PanelLine> lines = PotentialLines.Build(input);
            List<PanelLine> rows = lines.Where(l => l.Key == "ct.odds.potential.row").ToList();

            Assert.Equal(3, rows.Count);
            Assert.Equal("C4", rows[0].Format(new PlainArgFormatter())[0], "the three fastest are shown");
            PanelLine more = lines.First(l => l.Key == "ct.odds.potential.more");
            Assert.Equal(2.0, more.Args[0].Number, "and two more");
            Assert.Equal(5.0, lines[0].Args[0].Number, "the title counts them all");

            input.MaxRows = 5;
            Assert.False(PotentialLines.Build(input).Any(l => l.Key == "ct.odds.potential.more"), "a list that fits is not cut");
            input.MaxRows = 0;
            Assert.Equal(5, PotentialLines.Build(input).Count(l => l.Key == "ct.odds.potential.row"), "no limit is every row");
        }

        // ---- the adapter and the panel (the sources; both touch the game) ----

        private static string Source(string relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheInspector_HasPotentialAiBidders_OfTheDesign()
        {
            string code = Source("ContractInspector.cs");

            Assert.True(Regex.IsMatch(code, @"public\s+List<OfferInput>\s+PotentialAiBidders\(GameContract\s+contract\)"), "the signature of design.md");
            Assert.True(Regex.IsMatch(code, @"public\s+PotentialReport\s+BuildPotential\(GameContract\s+contract,\s*GameCpu\s+candidate,\s*OddsReport\s+odds\)"));
        }

        [Test]
        public void TheAdapter_TakesTheMarginThresholdFromTheRules_NotALiteral()
        {
            string code = Source("ContractInspector.cs");

            Assert.True(code.Contains("AiContractMarginThreshold"), "ai.contractMarginThreshold of rules.json");
            Assert.False(Regex.IsMatch(code, @"\b0\.45f?\b"), "the literal of the game lives in the content file, not in the code");
        }

        [Test]
        public void TheAdapter_LeavesOutThePlayerAndBankruptCompanies_AndAsksTheGameLazily()
        {
            string code = Source("ContractInspector.cs");
            int start = code.IndexOf("public List<OfferInput> PotentialAiBidders(", StringComparison.Ordinal);
            int end = code.IndexOf("public PotentialReport BuildPotential(", StringComparison.Ordinal);
            Assert.True(start > 0 && end > start);
            string body = code.Substring(start, end - start);

            Assert.True(body.Contains("ModCompanies.All"), "the companies the mod knows");
            Assert.True(Regex.IsMatch(body, @"company\.IsPlayer\s*\|\|\s*company\.IsBankrupt"), "the player's company and bankrupt ones are not rivals");
            Assert.True(Regex.IsMatch(body, @"FailsRequirements\s*=\s*\(\)\s*=>\s*contract\.FailsAtAnyRequirement\(cpu\)"), "the requirements by the game's own check, asked only when needed");
            Assert.True(Regex.IsMatch(body, @"UnitCostAtPayment\s*=\s*\(\)\s*=>\s*cpu\.RealUnitCostWithNewPrice\(payment\)"), "the cost of a unit at the price of the contract, as the AI computes it");
            Assert.True(body.Contains("AiBidRules.BestQualifying("), "the rule is the tested one");
            Assert.False(body.Contains("AddOffer("), "the estimate changes nothing in the game");
        }

        [Test]
        public void TheAdapter_AsksNobodyWhenTheAiHasNoMoveLeft()
        {
            string code = Source("ContractInspector.cs");
            int start = code.IndexOf("public PotentialReport BuildPotential(", StringComparison.Ordinal);
            int end = code.IndexOf("public static string CompanyKeyOf(", start, StringComparison.Ordinal);
            Assert.True(start > 0 && end > start);
            string body = code.Substring(start, end - start);

            int calendar = body.IndexOf("AiMoveCalendar.MovesBefore(", StringComparison.Ordinal);
            int exit = body.IndexOf("if (report.MovesLeft <= 0) return report;", StringComparison.Ordinal);
            int ask = body.IndexOf("PotentialAiBidders(contract)", StringComparison.Ordinal);
            Assert.True(calendar > 0 && exit > calendar && ask > exit, "the moves are counted first and the AI is asked only when one is left");
            Assert.True(body.Contains("DifficultyLevel == HardestDifficulty"), "the note for difficulty 5");
            Assert.True(Regex.IsMatch(code, @"public\s+const\s+int\s+HardestDifficulty\s*=\s*5\s*;"));
        }

        [Test]
        public void ThePanel_ShowsTheBlockAfterTheBreakdownOfTheChance()
        {
            string code = Source("UI/OddsPanel.cs");

            int hints = code.IndexOf("OddsLines.Build(", StringComparison.Ordinal);
            int build = code.IndexOf("inspector.BuildPotential(_contract, _cpu, odds)", StringComparison.Ordinal);
            int lines = code.IndexOf("PotentialLines.Build(PotentialInputOf(", StringComparison.Ordinal);
            Assert.True(hints > 0 && build > hints && lines > build, "the block comes last, from the same report");
            Assert.True(code.Contains("feature.Rules.LowActualChance"), "the threshold of a bad result is the one of the rules");
        }

        [Test]
        public void TheOddsReport_KeepsTheBestOffersForTheEstimate()
        {
            Assert.True(Regex.IsMatch(Source("OddsReport.cs"), @"public\s+List<OfferInput>\s+BestOffers"));
            Assert.True(Source("ContractInspector.cs").Contains("report.BestOffers = best;"));
        }
    }
}
