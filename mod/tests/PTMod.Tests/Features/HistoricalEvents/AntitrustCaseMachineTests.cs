using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The antitrust cases (TSK-009; FR-5, design.md formulas 3 and 4): the amounts of AC-5.1 to AC-5.3, the expected loss of an appealing target over a hundred thousand rolls,
    /// the correction for a lost dominance, the consent decree and the warning, and the running of the months by <see cref="CaseCore"/> with a fake economy.
    /// </summary>
    [Covers(typeof(CaseModes), typeof(CaseActions), typeof(CaseStages), typeof(CaseKeys), typeof(CaseParams), typeof(CaseOpKind), typeof(CaseOp), typeof(CaseStep),
        typeof(CaseQuote), typeof(AntitrustCaseMachine), typeof(CaseCore))]
    public class AntitrustCaseMachineTests
    {
        private const string Target = "ai:3";
        private const string Entry = "hist.2009.eufine";
        private const int Day = 14000;

        private static CaseParams P(string json)
        {
            var problems = new List<string>();
            CaseParams p = CaseParams.Read(HistFixture.Obj(json), problems);
            Assert.Equal(0, problems.Count(l => !l.EndsWith("unknown member is ignored")), "the numbers must read: " + string.Join(" | ", problems));
            return p;
        }

        /// <summary>The numbers of the example event of the design (the EU fine of 2009, with a new fine after an annulment), as the effect writes them.</summary>
        private const string EuJson =
            "{'mode':'fine','finePct':0.04,'remedyChannel':'contract.reward','remedyMultiplier':0.95,'remedyDays':730,'appealMonths':60,'pAnnul':0.35,'pReduce':0.35," +
            "'reduceTo':0.35,'interestPerYear':0.04,'guaranteeFeePerYear':0.01,'dominanceLostShare':0.40,'reimpose':true,'reimposeDelayMonths':18,'reimposeP':0.5," +
            "'reimposeShare':0.355,'secondAppealMonths':15,'secondPReduce':0.6,'secondReduceTo':0.63}";

        private static CaseParams EuFine()
        {
            return P(EuJson);
        }

        private static CaseDto Announced(CaseParams p, float fine, string target = Target)
        {
            return AntitrustCaseMachine.Open("eu2009", Entry, target, null, p, new JsonObject(), fine, Day).Case;
        }

        // ---- the amounts of the design ----

        [Test]
        public void Ac51_TheFineIsFourPercentOfTheRevenue_AndTheQuotesShowTheDesignNumbers()
        {
            CaseParams p = EuFine();
            float fine = AntitrustCaseMachine.Fine(p, 25e9f);
            Assert.Near(1e9, fine, 1e3, "F = 0.04 x 25 bn = 1 bn");

            Assert.Near(1e9, AntitrustCaseMachine.Quote(CaseActions.Pay, p, fine, 25e9f).Upfront, 1e3, "pay shows 1 bn");
            Assert.Near(1e9, AntitrustCaseMachine.Quote(CaseActions.Provision, p, fine, 25e9f).Upfront, 1e3, "the reserve shows 1 bn");

            CaseQuote appeal = AntitrustCaseMachine.Quote(CaseActions.Appeal, p, fine, 25e9f);
            Assert.Near(1e7, appeal.AnnualFee, 1e2, "the appeal shows 10 million a year for the guarantee");
            Assert.Equal(0f, appeal.Upfront, "and nothing now");
            Assert.Equal(60, appeal.Months);
        }

        [Test]
        public void Ac52_AnAppealUpheldAfterSixtyMonths_CostsOnePointTwoBillion()
        {
            CaseParams p = EuFine();
            CaseDto c = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(c, CaseActions.Appeal, p, 0f, Day);
            Assert.Equal(CaseStages.Appeal, c.stage);
            Assert.Equal(Day + HistTime.MonthsToDays(60), c.rulingDay, "the ruling after 60 months");

            CaseStep ruling = AntitrustCaseMachine.Rule(c, p, 0.60f, 0.90f, 25e9f, c.rulingDay);
            Assert.Near(1.2e9, ruling.NetCost, 1e4, "1 bn x (1 + 0.04 x 5) = 1.2 bn");
            Assert.True(ruling.Ops.All(o => o.Kind != CaseOpKind.Spend || o.Expense == HistExpense.Fines), "it is booked as a fine");
            Assert.Equal(CaseStages.Paid, c.stage);
        }

        [Test]
        public void Ac53_AReserveThatIsAnnulled_IsRefundedAsInterestIncome_AndAFurtherFineMayFollow()
        {
            CaseParams p = EuFine();
            CaseDto c = Announced(p, 1e9f);

            CaseStep paid = AntitrustCaseMachine.Choose(c, CaseActions.Provision, p, 0f, Day);
            Assert.Near(1e9, paid.NetCost, 1e3, "the reserve is paid at once");
            Assert.Near(1e9, c.provisioned, 1e3);

            int rulingDay = c.rulingDay;
            CaseStep annulled = AntitrustCaseMachine.Rule(c, p, 0.60f, 0.10f, 25e9f, rulingDay);
            CaseOp refund = annulled.Ops.Single(o => o.Kind == CaseOpKind.Earn);
            Assert.Near(1e9, refund.Amount, 1e3, "1 bn is returned to the player");
            Assert.Equal(HistIncome.Interest, refund.Income, "the owner's decision: the revenue origin Interest");
            Assert.Equal(CaseStages.Reimposition, c.stage);
            Assert.Equal(rulingDay + HistTime.MonthsToDays(18), c.reimposeDay, "a new fine may come after 18 months");

            CaseStep again = AntitrustCaseMachine.Reimpose(c, p, 0.20f, Entry, "Intel", c.reimposeDay);
            Assert.Near(3.55e8, c.fine, 1e3, "355 million (EU 2023: 376 of 1060)");
            Assert.Equal(CaseStages.SecondAwaiting, c.stage);
            CaseOp fire = again.Ops.Single(o => o.Kind == CaseOpKind.Fire);
            Assert.Equal(CaseCore.ReimposeEventId, fire.Key);
            Assert.Equal(new[] { Entry, "Intel", string.Empty, string.Empty, "eu2009" }, fire.Args, "the arguments of the table: entry, name, second name, second key, case");

            CaseStep pay = AntitrustCaseMachine.Choose(c, CaseActions.ReimposePay, p, 0f, c.reimposeDay);
            Assert.Near(3.55e8, pay.NetCost, 1e3);
        }

        [Test]
        public void TheReimposition_MissesWhenTheRollIsAtLeastTheProbability_AndTheCaseCloses()
        {
            CaseParams p = EuFine();
            CaseDto c = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(c, CaseActions.Appeal, p, 0f, Day);
            AntitrustCaseMachine.Rule(c, p, 0.6f, 0.10f, 1f, c.rulingDay);

            CaseStep none = AntitrustCaseMachine.Reimpose(c, p, 0.50f, Entry, "Intel", c.reimposeDay);
            Assert.Equal(CaseStages.Closed, c.stage);
            Assert.Equal(0, none.Ops.Count);
        }

        [Test]
        public void ASecondAppeal_PaysTheReducedAmountOrTheWhole()
        {
            CaseParams p = EuFine();
            CaseDto c = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(c, CaseActions.Appeal, p, 0f, Day);
            AntitrustCaseMachine.Rule(c, p, 0.6f, 0.10f, 1f, c.rulingDay);
            AntitrustCaseMachine.Reimpose(c, p, 0.10f, Entry, "Intel", c.reimposeDay);

            CaseDto copy = Clone(c);
            AntitrustCaseMachine.Choose(c, CaseActions.ReimposeAppeal, p, 0f, c.reimposeDay);
            Assert.Equal(CaseStages.SecondAppeal, c.stage);
            Assert.Equal(c.reimposeDay + HistTime.MonthsToDays(15), c.rulingDay, "the ruling after 15 months");

            CaseStep reduced = AntitrustCaseMachine.Rule(c, p, 0.6f, 0.30f, 1f, c.rulingDay);
            Assert.Near(3.55e8 * 0.63, reduced.NetCost, 1e3, "EU 2024: 237 of 376");

            AntitrustCaseMachine.Choose(copy, CaseActions.ReimposeAppeal, p, 0f, copy.reimposeDay);
            CaseStep upheld = AntitrustCaseMachine.Rule(copy, p, 0.6f, 0.90f, 1f, copy.rulingDay);
            Assert.Near(3.55e8, upheld.NetCost, 1e3, "or the whole of it, without interest");
        }

        [Test]
        public void ExpectedLoss_OfAnAppealingTarget_IsAboutFiftyEightPercentOfTheFine_OverAHundredThousandRolls()
        {
            // design.md formula 3: 0.35 x 0 + 0.35 x 0.35 F + 0.30 x 1.2 F = 0.48 F; the guarantee 0.05 F; the new fine 0.35 x 0.5 x (0.6 x 0.63 + 0.4) x 0.355 F = 0.05 F.
            CaseParams p = EuFine();
            const float F = 1e9f;
            var rng = new Xoshiro128(2009);
            double total = 0;
            const int trials = 100000;

            for (int i = 0; i < trials; i++)
            {
                var c = new CaseDto { caseId = "eu2009", targetKey = Target, mode = CaseModes.Fine, stage = CaseStages.Awaiting, fine = F };
                AntitrustCaseMachine.Choose(c, CaseActions.Appeal, p, 0f, Day);

                for (int month = 0; month < 60; month++) total += AntitrustCaseMachine.Monthly(c).NetCost;
                total += AntitrustCaseMachine.Rule(c, p, 0.60f, rng.Value(), 1f, c.rulingDay).NetCost;

                if (c.stage != CaseStages.Reimposition) continue;
                AntitrustCaseMachine.Reimpose(c, p, rng.Value(), Entry, "x", c.reimposeDay);
                if (c.stage != CaseStages.SecondAwaiting) continue;

                AntitrustCaseMachine.Choose(c, CaseActions.ReimposeAppeal, p, 0f, c.reimposeDay);
                total += AntitrustCaseMachine.Rule(c, p, 0.60f, rng.Value(), 1f, c.rulingDay).NetCost;
            }

            double loss = total / trials / F;
            Assert.Near(0.58, loss, 0.01, "the expected loss is 0.58 F, measured " + loss);
        }

        [Test]
        public void ALostDominance_RaisesTheChanceOfAnnulmentByTenPoints()
        {
            CaseParams p = EuFine();

            CaseDto strong = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(strong, CaseActions.Appeal, p, 0f, Day);
            AntitrustCaseMachine.Rule(strong, p, 0.55f, 0.40f, 1f, strong.rulingDay);
            Assert.Equal(CaseStages.Paid, strong.stage, "with the share kept the roll 0.40 falls in the reduction (0.35 to 0.70)");

            CaseDto weak = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(weak, CaseActions.Appeal, p, 0f, Day);
            AntitrustCaseMachine.Rule(weak, p, 0.30f, 0.40f, 1f, weak.rulingDay);
            Assert.Equal(CaseStages.Reimposition, weak.stage, "with the share below 0.40 the annulment reaches 0.45, so 0.40 is annulled");

            CaseDto edge = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(edge, CaseActions.Appeal, p, 0f, Day);
            AntitrustCaseMachine.Rule(edge, p, 0.40f, 0.40f, 1f, edge.rulingDay);
            Assert.Equal(CaseStages.Paid, edge.stage, "a share of exactly 0.40 has not lost the dominance");
        }

        [Test]
        public void AReserveThatIsReduced_RefundsTheDifference_AndAnAppealReducedPaysTheReducedAmount()
        {
            CaseParams p = EuFine();

            CaseDto reserve = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(reserve, CaseActions.Provision, p, 0f, Day);
            CaseStep refunded = AntitrustCaseMachine.Rule(reserve, p, 0.6f, 0.50f, 1f, reserve.rulingDay);
            Assert.Near(0.65e9, refunded.Ops.Single(o => o.Kind == CaseOpKind.Earn).Amount, 1e3, "the reserve of 1 bn minus the 0.35 bn that stays");
            Assert.Equal(0, refunded.Ops.Count(o => o.Kind == CaseOpKind.Spend));

            CaseDto appeal = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(appeal, CaseActions.Appeal, p, 0f, Day);
            CaseStep paid = AntitrustCaseMachine.Rule(appeal, p, 0.6f, 0.50f, 1f, appeal.rulingDay);
            Assert.Near(0.35e9, paid.NetCost, 1e3);
        }

        [Test]
        public void AReserveThatIsUpheld_PaysOnlyTheInterest()
        {
            CaseParams p = EuFine();
            CaseDto reserve = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(reserve, CaseActions.Provision, p, 0f, Day);
            CaseStep upheld = AntitrustCaseMachine.Rule(reserve, p, 0.6f, 0.95f, 1f, reserve.rulingDay);
            Assert.Near(0.2e9, upheld.NetCost, 1e4, "1.2 bn due minus the 1 bn already paid");
        }

        // ---- reputation and the remedy ----

        [Test]
        public void TheDecisionCostsFiveReputation_AndPayingAtOnceGivesTwoBack()
        {
            CaseParams p = EuFine();
            CaseStep open = AntitrustCaseMachine.Open("eu2009", Entry, Target, null, p, new JsonObject(), 1e9f, Day);
            CaseOp announced = open.Ops.Single(o => o.Kind == CaseOpKind.Reputation);
            Assert.Equal(-5f, announced.Amount);
            Assert.Equal("hist.rep.antitrust.fine", announced.Key);

            CaseStep pay = AntitrustCaseMachine.Choose(open.Case, CaseActions.Pay, p, 0f, Day);
            CaseOp bonus = pay.Ops.Single(o => o.Kind == CaseOpKind.Reputation);
            Assert.Equal(2f, bonus.Amount);
            Assert.Equal("hist.rep.antitrust.paid", bonus.Key);
            Assert.Equal(CaseStages.Paid, open.Case.stage);

            CaseStep appeal = AntitrustCaseMachine.Choose(Announced(p, 1e9f), CaseActions.Appeal, p, 0f, Day);
            Assert.Equal(0, appeal.Ops.Count(o => o.Kind == CaseOpKind.Reputation), "appealing earns nothing back");
        }

        [Test]
        public void TheBehaviouralRemedy_IsAnEntryOfContractRewardForTheTarget()
        {
            CaseStep open = AntitrustCaseMachine.Open("eu2009", Entry, Target, null, EuFine(), new JsonObject(), 1e9f, Day);
            CaseOp remedy = open.Ops.Single(o => o.Kind == CaseOpKind.Remedy);
            Assert.Equal(Target, remedy.CompanyKey);
            Assert.Equal("contract.reward", remedy.Channel);
            Assert.Equal(0.95f, remedy.Multiplier);
            Assert.Equal(730, remedy.Days);
        }

        [Test]
        public void AFineWithoutARemedyChannel_HasNoRemedy()
        {
            CaseStep open = AntitrustCaseMachine.Open("c", Entry, Target, null, P("{'mode':'fine'}"), new JsonObject(), 1e9f, Day);
            Assert.Equal(0, open.Ops.Count(o => o.Kind == CaseOpKind.Remedy));
        }

        // ---- consent decree and warning ----

        [Test]
        public void ConsentSettle_IsARemedyWithoutAFine_AndReputationMinusThree()
        {
            CaseParams p = P("{'mode':'consent','remedyChannel':'contract.reward','remedyMultiplier':0.95,'remedyDays':730}");
            CaseDto c = AntitrustCaseMachine.Open("ftc1998", "hist.1998.ftc", Target, null, p, new JsonObject(), 0f, Day).Case;
            Assert.Equal(CaseStages.ConsentAwaiting, c.stage);

            CaseStep settle = AntitrustCaseMachine.Choose(c, CaseActions.Settle, p, 0f, Day);
            Assert.Equal(0f, settle.NetCost);
            Assert.Equal(1, settle.Ops.Count(o => o.Kind == CaseOpKind.Remedy));
            Assert.Equal(-3f, settle.Ops.Single(o => o.Kind == CaseOpKind.Reputation).Amount);
            Assert.Equal(CaseStages.Closed, c.stage);
        }

        [Test]
        public void ConsentFight_PaysNineMonthsOfLegalCosts_ThenHalfWinsAndHalfPaysTheFineAndTheRemedy()
        {
            CaseParams p = P("{'mode':'consent','remedyChannel':'contract.reward','remedyMultiplier':0.95,'remedyDays':730,'legalCostPct':0.003,'legalMonths':9,'winP':0.5,'lossFinePct':0.01}");
            float revenue = 30e9f;
            CaseQuote quote = AntitrustCaseMachine.Quote(CaseActions.Fight, p, 0f, revenue);
            Assert.Near(0.003 * 30e9, quote.LegalPerMonth, 1e3, "0.3 % of the revenue a month");
            Assert.Equal(9, quote.Months);

            CaseDto won = AntitrustCaseMachine.Open("ftc1998", "e", Target, null, p, new JsonObject(), 0f, Day).Case;
            AntitrustCaseMachine.Choose(won, CaseActions.Fight, p, quote.LegalPerMonth, Day);
            Assert.Equal(CaseStages.ConsentFight, won.stage);
            Assert.Equal(Day + HistTime.MonthsToDays(9), won.rulingDay);
            Assert.Near(0.003 * 30e9, AntitrustCaseMachine.Monthly(won).NetCost, 1e3, "the legal costs are booked as fines");

            CaseStep clear = AntitrustCaseMachine.Rule(won, p, 0.6f, 0.30f, revenue, won.rulingDay);
            Assert.Equal(CaseStages.Closed, won.stage, "a roll below 0.5 wins: closed");
            Assert.Equal(1f, clear.Ops.Single(o => o.Kind == CaseOpKind.Reputation).Amount, "reputation +1");
            Assert.Equal(0f, clear.NetCost);

            CaseDto lost = AntitrustCaseMachine.Open("ftc1998b", "e", Target, null, p, new JsonObject(), 0f, Day).Case;
            AntitrustCaseMachine.Choose(lost, CaseActions.Fight, p, quote.LegalPerMonth, Day);
            CaseStep loss = AntitrustCaseMachine.Rule(lost, p, 0.6f, 0.70f, revenue, lost.rulingDay);
            Assert.Near(0.01 * 30e9, loss.NetCost, 1e3, "the fine of 1 % of the revenue");
            Assert.Equal(1, loss.Ops.Count(o => o.Kind == CaseOpKind.Remedy));
            Assert.Equal(-5f, loss.Ops.Single(o => o.Kind == CaseOpKind.Reputation).Amount, "reputation -5");
        }

        [Test]
        public void AWarning_ImposesTheRemedyAndTheBenefitAtOnce_AndIsOver()
        {
            CaseParams p = P("{'mode':'warning','remedyChannel':'contract.reward','remedyMultiplier':0.97,'remedyDays':730," +
                             "'beneficiaryChannel':'contract.signChance','beneficiaryMultiplier':1.10,'beneficiaryDays':730}");
            CaseStep open = AntitrustCaseMachine.Open("jftc2005", "hist.2005.jftc", Target, "ai:5", p, new JsonObject(), 0f, Day);

            Assert.Equal(CaseStages.Closed, open.Case.stage);
            CaseOp[] remedies = open.Ops.Where(o => o.Kind == CaseOpKind.Remedy).ToArray();
            Assert.Equal(2, remedies.Length);
            Assert.True(remedies.Any(o => o.CompanyKey == Target && o.Channel == "contract.reward" && Math.Abs(o.Multiplier - 0.97f) < 1e-6f));
            Assert.True(remedies.Any(o => o.CompanyKey == "ai:5" && o.Channel == "contract.signChance" && Math.Abs(o.Multiplier - 1.10f) < 1e-6f));
            Assert.Equal(-2f, open.Ops.Single(o => o.Kind == CaseOpKind.Reputation).Amount);
        }

        // ---- the rules of the stages ----

        [Test]
        public void AnActionThatDoesNotFitTheStage_IsNotAccepted_AndChangesNothing()
        {
            CaseParams p = EuFine();
            CaseDto c = Announced(p, 1e9f);
            AntitrustCaseMachine.Choose(c, CaseActions.Appeal, p, 0f, Day);

            CaseStep again = AntitrustCaseMachine.Choose(c, CaseActions.Pay, p, 0f, Day);
            Assert.False(again.Accepted);
            Assert.Equal(CaseStages.Appeal, c.stage);
            Assert.False(AntitrustCaseMachine.Choose(Announced(p, 1e9f), CaseActions.Settle, p, 0f, Day).Accepted, "a fine has no settlement");
            Assert.False(AntitrustCaseMachine.Rule(Announced(p, 1e9f), p, 0.6f, 0.5f, 1f, Day).Accepted, "a case that waits for its target cannot be ruled on");
            Assert.False(AntitrustCaseMachine.Monthly(Announced(p, 1e9f)).Accepted, "and costs nothing while it waits");
        }

        [Test]
        public void TheStages_KnowWhichAreOpenWhichAreActiveAndWhatTheDefaultIs()
        {
            Assert.True(CaseStages.IsOpen(CaseStages.Appeal) && CaseStages.IsOpen(CaseStages.Reimposition) && CaseStages.IsOpen(CaseStages.ConsentFight));
            Assert.False(CaseStages.IsOpen(CaseStages.Paid) || CaseStages.IsOpen(CaseStages.Closed) || CaseStages.IsOpen(99));
            Assert.True(CaseStages.CountsAsActive(CaseStages.Paid), "a fine that was paid is still a finding of liability");
            Assert.False(CaseStages.CountsAsActive(CaseStages.Closed) || CaseStages.CountsAsActive(99));
            Assert.Equal(CaseActions.Appeal, CaseStages.DefaultAction(CaseStages.Awaiting));
            Assert.Equal(CaseActions.ReimposeAppeal, CaseStages.DefaultAction(CaseStages.SecondAwaiting));
            Assert.Equal(CaseActions.Settle, CaseStages.DefaultAction(CaseStages.ConsentAwaiting));
            Assert.Null(CaseStages.DefaultAction(CaseStages.Appeal));
            Assert.True(CaseStages.IsAwaitingChoice(CaseStages.ConsentAwaiting) && !CaseStages.IsAwaitingChoice(CaseStages.Appeal));
        }

        // ---- the numbers ----

        [Test]
        public void TheNumbers_ReadWithTheDesignDefaults_AndRejectWhatIsOutOfRange()
        {
            var problems = new List<string>();
            CaseParams d = CaseParams.Read(new JsonObject(), problems);
            Assert.Equal(0, problems.Count);
            Assert.Equal(0.04f, d.FinePct);
            Assert.Equal(60, d.AppealMonths);
            Assert.Equal(0.355f, d.ReimposeShare);
            Assert.Equal(CaseModes.Fine, d.Mode);
            Assert.Equal(CaseActions.Open, CaseParams.ActionOf(null));
            Assert.Equal(CaseActions.Pay, CaseParams.ActionOf(HistFixture.Obj("{'action':'pay'}")));

            foreach (string bad in new[]
            {
                "{'finePct':0.9}", "{'finePct':'a lot'}", "{'mode':'jail'}", "{'action':'run'}", "{'pAnnul':0.8,'pReduce':0.5}", "{'appealMonths':0}", "{'remedyChannel':'contract.reward'}",
                "{'beneficiaryChannel':'contract.signChance'}", "{'reimposeP':1.5}", "{'guaranteeFeePerYear':-1}",
            })
            {
                var lines = new List<string>();
                CaseParams.Read(HistFixture.Obj(bad), lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }

            var unknown = new List<string>();
            CaseParams.Read(HistFixture.Obj("{'finePcts':0.04,'_note':'x'}"), unknown);
            Assert.True(unknown.Any(l => l.Contains("finePcts")), "a typo is named");
            Assert.False(unknown.Any(l => l.Contains("_note")), "a comment is not");
            Assert.True(CaseParams.MemberNames.Contains("finePct") && CaseParams.MemberNames.Contains("reimposeShare"));
        }

        [Test]
        public void ARawNumberOfTheCase_SurvivesInTheCase_SoLaterStepsReadTheSame()
        {
            JsonObject raw = HistFixture.Obj("{'mode':'fine','finePct':0.02,'appealMonths':36}");
            CaseDto c = AntitrustCaseMachine.Open("c", Entry, Target, "ai:5", CaseParams.Read(raw, null), raw, 5e8f, Day).Case;

            CaseParams back = CaseCore.ParamsOf(c);
            Assert.Equal(36, back.AppealMonths);
            Assert.Equal(Entry, c.@params.GetString("entryId"));
            Assert.Equal("ai:5", c.@params.GetString("beneficiary"));
            Assert.False(raw.ContainsKey("entryId"), "the effect's own object is not changed");
        }

        // ---- the months, with a fake economy (CaseCore) ----

        private sealed class Env
        {
            public readonly HistSaveData Data = new HistSaveData();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly FakeFirer Firer = new FakeFirer();
            public readonly List<string> Log = new List<string>();
            public FixedRng Rng = new FixedRng(0.9f);
            public float Share = 0.60f;
            public CaseCore Core;

            public Env()
            {
                Economy.Revenue[Target] = 25e9f;
                Economy.Revenue["player"] = 25e9f;
                Economy.Names[Target] = "Intel";
                Core = new CaseCore(() => Data, Economy, Mods, Firer, () => Rng, key => Share, a => a.ToString("0"), Log.Add);
            }

            public bool OpenEu(string target = Target)
            {
                JsonObject raw = HistFixture.Obj(EuJson);
                CaseParams p = CaseParams.Read(raw, null);
                return Core.Open("eu2009", Entry, target, null, p, raw, AntitrustCaseMachine.Fine(p, Economy.Revenue12m(target)));
            }

            /// <summary>Advances the clock by a month and runs the monthly pass.</summary>
            public void Month()
            {
                Economy.Today += 30;
                Core.OnMonth();
            }

            public CaseDto Case
            {
                get { return Core.Find("eu2009"); }
            }
        }

        [Test]
        public void Core_AppealRunsForSixtyMonths_ChargesTheGuaranteeEveryMonth_AndRulesOnTheDay()
        {
            var env = new Env();
            Assert.True(env.OpenEu());
            Assert.True(env.Core.Choose("eu2009", CaseActions.Appeal, 0f));

            int months = 0;
            while (env.Case.stage == CaseStages.Appeal && months < 100)
            {
                env.Month();
                months++;
            }

            Assert.Equal(CaseStages.Paid, env.Case.stage, "the roll 0.9 upholds the fine");
            Assert.True(months >= 60 && months <= 62, "the ruling comes with the first month pass after 60 months (" + months + ")");
            float fees = env.Economy.Spent(Target, "Interest");
            Assert.Near(0.05 * 1e9, fees, 0.003 * 1e9, "the guarantee costs 1 % a year, five years: " + fees);
            Assert.Near(1.2e9, env.Economy.Spent(Target, "Fines"), 1e5, "and the fine with the interest of five years");
            Assert.True(env.Economy.Notes(Target).Any(n => n.Origin == "hist.note.case.upheld"), "the player is told (a note for the target)");
        }

        [Test]
        public void Core_TheAcquisitionChannelIsInvalidatedWhenAnOpenCaseOpensAndEnds()
        {
            var env = new Env();
            Assert.False(env.Core.HasOpenCase(Target));
            env.OpenEu();
            Assert.True(env.Core.HasOpenCase(Target));
            Assert.False(env.Core.HasOpenCase("player"));
            Assert.Equal(1, env.Mods.InvalidationsOf("acquisition.price"), "opening");

            env.Core.Choose("eu2009", CaseActions.Pay, 0f);
            Assert.False(env.Core.HasOpenCase(Target), "a paid fine is not open any more");
            Assert.Equal(2, env.Mods.InvalidationsOf("acquisition.price"), "ending");
        }

        [Test]
        public void Core_HistCaseActive_CountsAPaidFineButNotAClosedCase_AndKnowsTheTarget()
        {
            var env = new Env();
            Assert.False(env.Core.IsActive("eu2009", false), "no case yet");
            env.OpenEu();
            Assert.True(env.Core.IsActive("eu2009", false));
            Assert.True(env.Core.IsActive("eu2009", true), "its target is an AI company");
            env.Core.Choose("eu2009", CaseActions.Pay, 0f);
            Assert.True(env.Core.IsActive("eu2009", true), "paying is a finding of liability");

            var player = new Env();
            player.OpenEu("player");
            Assert.True(player.Core.IsActive("eu2009", false));
            Assert.False(player.Core.IsActive("eu2009", true), "the player is the target");

            var closed = new Env { Rng = new FixedRng(0.1f) };
            closed.Core.Open("eu2009", Entry, Target, null, P("{'mode':'fine','reimpose':false}"), new JsonObject(), 1e9f);
            closed.Core.Choose("eu2009", CaseActions.Appeal, 0f);
            for (int i = 0; i < 70 && closed.Case.stage == CaseStages.Appeal; i++) closed.Month();
            Assert.Equal(CaseStages.Closed, closed.Case.stage, "annulled for good");
            Assert.False(closed.Core.IsActive("eu2009", false), "a case that ended without liability is not active");
        }

        [Test]
        public void Core_AnAnnulmentWithANewFine_FiresTheReimposeEventForTheTarget()
        {
            var env = new Env { Rng = new FixedRng(0.1f, 0.2f) };
            env.OpenEu();
            env.Core.Choose("eu2009", CaseActions.Appeal, 0f);

            int guard = 0;
            while (env.Case.stage != CaseStages.SecondAwaiting && guard++ < 200) env.Month();

            Assert.Equal(CaseStages.SecondAwaiting, env.Case.stage);
            Assert.Equal(1, env.Firer.Calls.Count);
            Assert.Equal("hist.decide.antitrust.reimpose", env.Firer.Calls[0].EventId);
            Assert.Equal(Target, env.Firer.Calls[0].Company);
            Assert.Equal("eu2009", env.Firer.Calls[0].Args[4]);
            Assert.Equal(Entry, env.Firer.Calls[0].Args[0]);
            Assert.Equal("Intel", env.Firer.Calls[0].Args[1]);
            Assert.Near(3.55e8, env.Case.fine, 1e3);
            Assert.True(env.Core.Choose("eu2009", CaseActions.ReimposeAppeal, 0f));
        }

        [Test]
        public void Core_WhenTheNewFineCannotBeAnnounced_TheTargetTakesTheDefaultChoice()
        {
            var env = new Env { Rng = new FixedRng(0.1f, 0.2f) };
            env.Firer.Answer = FireResult.UnknownEvent;
            env.OpenEu();
            env.Core.Choose("eu2009", CaseActions.Appeal, 0f);

            int guard = 0;
            while (env.Case.stage != CaseStages.SecondAppeal && guard++ < 200) env.Month();
            Assert.Equal(CaseStages.SecondAppeal, env.Case.stage, "the unknown event leaves the target the default: appeal");
        }

        [Test]
        public void Core_ACaseThatWaitsTooLongForItsTarget_TakesTheDefaultChoice()
        {
            var env = new Env();
            env.OpenEu();
            Assert.Equal(CaseStages.Awaiting, env.Case.stage);

            env.Economy.Today += 100;
            env.Core.OnMonth();
            Assert.Equal(CaseStages.Awaiting, env.Case.stage, "100 days: the timer of the event is still running");

            env.Economy.Today += 100;
            env.Core.OnMonth();
            Assert.Equal(CaseStages.Appeal, env.Case.stage, "after 180 days the default choice of the event, an appeal, is applied");
        }

        [Test]
        public void Core_ABankruptTarget_ClosesTheCaseWithoutPayments()
        {
            var env = new Env();
            env.OpenEu();
            env.Core.Choose("eu2009", CaseActions.Appeal, 0f);
            env.Economy.Dead.Add(Target);

            env.Month();
            Assert.Equal(CaseStages.Closed, env.Case.stage);
            Assert.Equal(0, env.Economy.Flows.Count(f => f.Kind == "spend"), "nothing is paid by a company that is gone");
            Assert.False(env.Core.HasOpenCase(Target));

            var gone = new Env();
            gone.Economy.Dead.Add(Target);
            Assert.False(gone.OpenEu(), "a case against a company that is gone is not opened");
        }

        [Test]
        public void Core_ACaseThatFailsThreeMonthsInARow_IsClosedWithALine()
        {
            var env = new Env();
            env.OpenEu();
            env.Core.Choose("eu2009", CaseActions.Appeal, 0f);
            env.Economy.ThrowOnSpend = true;

            env.Month();
            env.Month();
            Assert.Equal(CaseStages.Appeal, env.Case.stage, "two failures: still open");
            env.Month();
            Assert.Equal(CaseStages.Closed, env.Case.stage, "the third closes it");
            Assert.True(env.Log.Any(l => l.Contains("failed 3 months in a row")), string.Join(" | ", env.Log));
        }

        [Test]
        public void Core_ASecondOpeningOfTheSameCase_IsIgnored_AndAWrongActionIsRefused()
        {
            var env = new Env();
            Assert.True(env.OpenEu());
            Assert.False(env.OpenEu(), "the same id twice");
            Assert.Equal(1, env.Core.Cases.Count);

            Assert.False(env.Core.Choose("nope", CaseActions.Pay, 0f), "no such case");
            Assert.False(env.Core.Choose("eu2009", CaseActions.Settle, 0f), "a fine has no settlement");
            Assert.Equal(CaseStages.Awaiting, env.Case.stage);
        }

        [Test]
        public void Core_TheRulesAreTheSameForThePlayerAndAnAiTarget()
        {
            var ai = new Env();
            ai.OpenEu(Target);
            ai.Core.Choose("eu2009", CaseActions.Pay, 0f);

            var player = new Env();
            player.OpenEu("player");
            player.Core.Choose("eu2009", CaseActions.Pay, 0f);

            Assert.Equal(ai.Economy.Spent(Target, "Fines"), player.Economy.Spent("player", "Fines"));
            Assert.Equal(ai.Economy.Reputation(Target), player.Economy.Reputation("player"));
            Assert.Equal(1, ai.Mods.Entries.Count);
            Assert.Equal(1, player.Mods.Entries.Count);
            Assert.Equal("hist.mod.remedy", player.Mods.Entries[0].Reason);
            Assert.Equal("player", player.Mods.Entries[0].Company);
        }

        [Test]
        public void Core_FinishedCasesAreKeptForTheChronicle_ButOnlyTheLatestTwentyFour()
        {
            var env = new Env();
            for (int i = 0; i < 30; i++)
            {
                CaseParams p = P("{'mode':'warning'}");
                env.Core.Open("w" + i, "e", Target, null, p, new JsonObject(), 0f);
                env.Economy.Today++;
            }
            env.Core.OnMonth();
            Assert.Equal(24, env.Core.Cases.Count);
            Assert.NotNull(env.Core.Find("w29"));
            Assert.Null(env.Core.Find("w0"));
        }

        private static CaseDto Clone(CaseDto c)
        {
            return new CaseDto
            {
                caseId = c.caseId, targetKey = c.targetKey, mode = c.mode, stage = c.stage, fine = c.fine, paid = c.paid, provisioned = c.provisioned,
                rulingDay = c.rulingDay, reimposeDay = c.reimposeDay, guaranteeFeePerMonth = c.guaranteeFeePerMonth, openedDay = c.openedDay, @params = c.@params,
            };
        }
    }
}
