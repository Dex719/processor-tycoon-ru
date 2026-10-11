using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The patent disputes (TSK-013; FR-9, design.md formula 8): every branch of the arithmetic, the payments in <c>Royalties</c> and <c>Fines</c>, the entries of
    /// <c>research.cost</c> for the company, the skip for a company the game already penalizes, and AC-9.1.
    /// </summary>
    [Covers(typeof(DisputeModes), typeof(DisputeActions), typeof(DisputeKeys), typeof(DisputeParams), typeof(DisputeSpec), typeof(DisputeQuote), typeof(DisputeMath), typeof(DisputeCore))]
    public class DisputeMathTests
    {
        private const string Plaintiff = "ai:0";
        private const string Defendant = "player";

        private sealed class Env
        {
            public readonly HistSaveData Data = new HistSaveData();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly FakeFirer Firer = new FakeFirer();
            public readonly List<string> Log = new List<string>();
            public FixedRng Rng = new FixedRng(0.5f);
            public DisputeParams Params = new DisputeParams();
            public DisputeCore Core;

            public Env()
            {
                Economy.Revenue[Defendant] = 4.8e9f;     // 400 million a month
                Economy.Monthly[Defendant] = 400e6f;
                Economy.Revenue[Plaintiff] = 20e9f;
                Economy.Monthly[Plaintiff] = 1.6e9f;
                Economy.Names[Plaintiff] = "AMD";
                Economy.Names[Defendant] = "Player Co";
                Core = new DisputeCore(() => Data, Economy, Mods, Firer, () => Rng, () => Params, a => a.ToString("0"), Log.Add);
            }

            public void Month()
            {
                Economy.Today += 30;
                Core.OnMonth();
            }

            public bool Answer(string action, string mode = DisputeModes.LicenseWithdrawal)
            {
                return Core.Choose("hist.1987.arbitration", mode, Plaintiff, Defendant, action);
            }

            public DisputeDto Dispute
            {
                get { return Core.Find("hist.1987.arbitration"); }
            }

            public void RunUntilOver(int maxMonths = 120)
            {
                for (int i = 0; i < maxMonths && Core.Disputes.Count > 0; i++) Month();
            }
        }

        // ---- the licence ----

        [Test]
        public void Ac91_ALicence_PaysOnePointTwoFivePercentOfTheMonthlyRevenue_AsRoyaltiesBothWays()
        {
            var env = new Env();
            Assert.True(env.Answer(DisputeActions.License));
            env.Month();

            Assert.Near(5e6, env.Economy.Spent(Defendant, "Royalties"), 1, "400 million x 0.0125 = 5 million paid by the defendant");
            Assert.Near(5e6, env.Economy.Earned(Plaintiff, "Royalties"), 1, "and received by the plaintiff");
            Assert.Equal(0f, env.Economy.Spent(Defendant, "Fines"));
        }

        [Test]
        public void TheLicence_LastsThirtySixMonths_ThenTheDisputeIsOver()
        {
            var env = new Env();
            env.Answer(DisputeActions.License);
            env.RunUntilOver();

            int payments = env.Economy.Flows.Count(f => f.Kind == "spend" && f.Company == Defendant);
            Assert.True(payments >= 35 && payments <= 37, "36 monthly payments: " + payments);
            Assert.Equal(0, env.Core.Disputes.Count);
        }

        // ---- arbitration ----

        [Test]
        public void Arbitration_CostsLegalFeesEveryMonth_AndAWinMakesThePlaintiffPayThePercentOfItsRevenue()
        {
            var env = new Env { Rng = new FixedRng(0.50f) };
            env.Answer(DisputeActions.Arbitrate);
            Assert.Equal(env.Economy.Today + HistTime.MonthsToDays(48), env.Dispute.resolveDay);

            env.RunUntilOver();

            float perMonth = 0.002f * 4.8e9f;
            float legal = env.Economy.Spent(Defendant, "Fines");
            int months = (int)(legal / perMonth + 0.5f);
            Assert.True(months >= 47 && months <= 49, "48 months of legal costs: " + months);

            Assert.Near(0.03 * 20e9, env.Economy.Spent(Plaintiff, "Fines"), 1e3, "the plaintiff pays 3 % of its revenue");
            Assert.Near(0.03 * 20e9, env.Economy.Earned(Defendant, "Royalties"), 1e3, "to the defendant");
            Assert.Equal(0, env.Mods.Entries.Count, "a win leaves the research cost alone");
            Assert.Equal(1, env.Rng.Draws, "one roll");
        }

        [Test]
        public void ALostArbitration_MakesResearchThirtyPercentDearer_ForSevenHundredAndThirtyDays()
        {
            var env = new Env { Rng = new FixedRng(0.70f) };
            env.Answer(DisputeActions.Arbitrate);
            env.RunUntilOver();

            FakeModifiers.Entry entry = env.Mods.Entries.Single();
            Assert.Equal("research.cost", entry.Channel);
            Assert.Equal(Defendant, entry.Company);
            Assert.Equal(1.3f, entry.Multiplier);
            Assert.Equal(730, entry.Days);
            Assert.Equal(0f, env.Economy.Spent(Plaintiff), "and the plaintiff pays nothing");
        }

        // ---- own development, redesign, settlement ----

        [Test]
        public void OwnDevelopment_RaisesTheResearchCost_ForThirtySixMonths_AtOnce()
        {
            var env = new Env();
            env.Answer(DisputeActions.Independent);

            FakeModifiers.Entry entry = env.Mods.Entries.Single();
            Assert.Equal("research.cost", entry.Channel);
            Assert.Equal(1.3f, entry.Multiplier);
            Assert.Equal(HistTime.MonthsToDays(36), entry.Days);
            Assert.Equal(0f, env.Economy.Spent(Defendant), "no payments: only the research is dearer");
        }

        [Test]
        public void Redesign_RaisesTheResearchCostByTwentyPercent_ForTwentyFourMonths()
        {
            var env = new Env();
            env.Answer(DisputeActions.Redesign, DisputeModes.Infringement);

            FakeModifiers.Entry entry = env.Mods.Entries.Single();
            Assert.Equal(1.2f, entry.Multiplier);
            Assert.Equal(730, entry.Days);
        }

        [Test]
        public void ASettlement_PaysThreePercentOfTheRevenueOnce_FromFinesToRoyalties_AndEndsTheDispute()
        {
            var env = new Env();
            env.Answer(DisputeActions.Settle, DisputeModes.Infringement);

            Assert.Near(0.03 * 4.8e9, env.Economy.Spent(Defendant, "Fines"), 1e3);
            Assert.Near(0.03 * 4.8e9, env.Economy.Earned(Plaintiff, "Royalties"), 1e3);
            Assert.Equal(0, env.Core.Disputes.Count);
        }

        [Test]
        public void AFight_IsWonWithNothingToPay_OrLostForOnePointFiveTimesTheSettlement()
        {
            var won = new Env { Rng = new FixedRng(0.54f) };
            won.Answer(DisputeActions.Fight, DisputeModes.Infringement);
            won.RunUntilOver();
            Assert.Equal(0f, won.Economy.Earned(Plaintiff), "a roll below 0.55 wins: nothing to the plaintiff");
            Assert.True(won.Economy.Notes(Defendant).Any(n => n.Origin == DisputeKeys.NoteFightWon));

            var lost = new Env { Rng = new FixedRng(0.56f) };
            lost.Answer(DisputeActions.Fight, DisputeModes.Infringement);
            lost.RunUntilOver();
            Assert.Near(1.5 * 0.03 * 4.8e9, lost.Economy.Earned(Plaintiff, "Royalties"), 1e3, "1.5 x 3 % of the defendant's revenue");
            Assert.True(lost.Economy.Spent(Defendant, "Fines") >= 1.5 * 0.03 * 4.8e9, "and the legal costs besides");
            Assert.True(lost.Economy.Notes(Defendant).Any(n => n.Origin == DisputeKeys.NoteLost));
        }

        [Test]
        public void TheFight_LastsTwentyFourMonths()
        {
            var env = new Env();
            env.Answer(DisputeActions.Fight, DisputeModes.Infringement);
            Assert.Equal(env.Economy.Today + HistTime.MonthsToDays(24), env.Dispute.resolveDay);
        }

        // ---- the company the game already penalizes ----

        [Test]
        public void ForACompanyTheGameAlreadyPenalizes_NoResearchMultiplierIsAdded_ButThePaymentsStay()
        {
            var independent = new Env();
            independent.Economy.Unlucky.Add(Defendant);
            independent.Answer(DisputeActions.Independent);
            Assert.Equal(0, independent.Mods.Entries.Count, "the game already multiplies the cost by 6 (Cyrix)");

            var redesign = new Env();
            redesign.Economy.Unlucky.Add(Defendant);
            redesign.Answer(DisputeActions.Redesign, DisputeModes.Infringement);
            Assert.Equal(0, redesign.Mods.Entries.Count);

            var arbitration = new Env { Rng = new FixedRng(0.9f) };
            arbitration.Economy.Unlucky.Add(Defendant);
            arbitration.Answer(DisputeActions.Arbitrate);
            arbitration.RunUntilOver();
            Assert.Equal(0, arbitration.Mods.Entries.Count, "and a lost arbitration adds none either");
            Assert.True(arbitration.Economy.Spent(Defendant, "Fines") > 0f, "but its legal costs were paid");

            var license = new Env();
            license.Economy.Unlucky.Add(Defendant);
            license.Answer(DisputeActions.License);
            license.Month();
            Assert.True(license.Economy.Spent(Defendant, "Royalties") > 0f, "the licence is paid in full");
            Assert.True(license.Log.Count == 0, "no complaint");
        }

        // ---- opening, answering, suing ----

        [Test]
        public void TheDispute_BeginsWithoutAnAnswer_AndTheSameIdOpensOnce()
        {
            var env = new Env();
            DisputeDto first = env.Core.Open("hist.1987.arbitration", DisputeModes.LicenseWithdrawal, Plaintiff, Defendant);
            Assert.NotNull(first);
            Assert.Equal(string.Empty, first.choice);
            Assert.Equal(env.Economy.Today + DisputeCore.AwaitingDays, first.resolveDay);

            Assert.True(ReferenceEquals(first, env.Core.Open("hist.1987.arbitration", DisputeModes.LicenseWithdrawal, Plaintiff, Defendant)), "the same dispute again");
            Assert.Equal(1, env.Core.Disputes.Count);
        }

        [Test]
        public void ADispute_NeedsTwoDifferentCompaniesOfTheGame_AndAKnownMode()
        {
            var env = new Env();
            Assert.Null(env.Core.Open("", DisputeModes.Infringement, Plaintiff, Defendant));
            Assert.Null(env.Core.Open("d", "duel", Plaintiff, Defendant));
            Assert.Null(env.Core.Open("d", DisputeModes.Infringement, Plaintiff, Plaintiff));
            Assert.Null(env.Core.Open("d", DisputeModes.Infringement, "nobody", Defendant));
            env.Economy.Dead.Add(Plaintiff);
            Assert.Null(env.Core.Open("d", DisputeModes.Infringement, Plaintiff, Defendant));
        }

        [Test]
        public void AnAnswer_IsAcceptedOnce_AndOnlyIfItFitsTheKindOfDispute()
        {
            var env = new Env();
            Assert.False(env.Answer(DisputeActions.Fight, DisputeModes.LicenseWithdrawal), "a licence withdrawal has no fight");
            Assert.False(env.Answer(DisputeActions.Open), "opening is not an answer");
            Assert.True(env.Answer(DisputeActions.Arbitrate));
            Assert.False(env.Answer(DisputeActions.License), "one answer only");
            Assert.Equal(DisputeActions.Arbitrate, env.Dispute.choice);
        }

        [Test]
        public void TheClaimOfThePlaintiff_FiresTheDefendantsEvent_TheNextDay()
        {
            var env = new Env();
            Assert.True(env.Core.Sue("hist.1987.arbitration", DisputeModes.LicenseWithdrawal, "hist.1987.arbitration", Defendant, Plaintiff));

            FakeFirer.Call call = env.Firer.Calls.Single();
            Assert.Equal("hist.decide.patent.withdrawal", call.EventId);
            Assert.Equal(Plaintiff, call.Company, "the defendant gets the decision");
            Assert.Equal(1, call.Delay);
            Assert.Equal(new[] { "hist.1987.arbitration", "AMD", "Player Co", Defendant, "hist.1987.arbitration" }, call.Args, "the arguments of the table: the plaintiff is the second company");

            Assert.True(env.Core.Sue("d2", DisputeModes.Infringement, "e", Defendant, Plaintiff));
            Assert.Equal("hist.decide.patent.infringement", env.Firer.Calls[1].EventId);

            env.Firer.Answer = FireResult.UnknownEvent;
            Assert.False(env.Core.Sue("d3", DisputeModes.Infringement, "e", Defendant, Plaintiff), "an event the pack lacks");
            Assert.False(env.Core.Sue("d4", "duel", "e", Defendant, Plaintiff));
            env.Economy.Dead.Add(Plaintiff);
            Assert.False(env.Core.Sue("d5", DisputeModes.Infringement, "e", Defendant, Plaintiff), "a defendant that is gone");
        }

        [Test]
        public void ADisputeNobodyAnswered_IsDroppedAfterAWhile()
        {
            var env = new Env();
            env.Core.Open("hist.1987.arbitration", DisputeModes.LicenseWithdrawal, Plaintiff, Defendant);
            env.Economy.Today += 100;
            env.Core.OnMonth();
            Assert.Equal(1, env.Core.Disputes.Count, "100 days: the timer of the event is still running");
            env.Economy.Today += 100;
            env.Core.OnMonth();
            Assert.Equal(0, env.Core.Disputes.Count);
            Assert.Equal(0, env.Economy.Flows.Count(f => f.Kind == "spend"), "nothing was paid");
        }

        [Test]
        public void ABankruptParty_EndsTheDisputeWithoutPayments()
        {
            var env = new Env();
            env.Answer(DisputeActions.License);
            env.Month();
            float paid = env.Economy.Spent(Defendant);

            env.Economy.Dead.Add(Plaintiff);
            env.Month();
            Assert.Equal(0, env.Core.Disputes.Count);
            Assert.Equal(paid, env.Economy.Spent(Defendant), "no more payments");
        }

        [Test]
        public void ADisputeThatFails_IsDropped_AndTheOthersGoOn()
        {
            var env = new Env();
            env.Core.Choose("a", DisputeModes.LicenseWithdrawal, Plaintiff, Defendant, DisputeActions.License);
            env.Economy.ThrowOnSpend = true;
            env.Month();
            Assert.Equal(0, env.Core.Disputes.Count);
            Assert.True(env.Log.Any(l => l.Contains("failed and is dropped")));
        }

        // ---- the arithmetic and the numbers ----

        [Test]
        public void TheQuote_ShowsWhatEachAnswerCostsTheDefendant()
        {
            var p = new DisputeParams();

            DisputeQuote arbitrate = DisputeMath.Quote(DisputeActions.Arbitrate, p, 4.8e9f, 400e6f);
            Assert.Near(0.002 * 4.8e9, arbitrate.PerMonth, 100);
            Assert.Equal(48, arbitrate.Months);
            Assert.Equal(0.6f, arbitrate.WinChance);

            DisputeQuote license = DisputeMath.Quote(DisputeActions.License, p, 4.8e9f, 400e6f);
            Assert.Near(5e6, license.PerMonth, 1);
            Assert.Equal(36, license.Months);

            Assert.Near(0.03 * 4.8e9, DisputeMath.Quote(DisputeActions.Settle, p, 4.8e9f, 400e6f).Upfront, 100);
            Assert.Equal(1.3f, DisputeMath.Quote(DisputeActions.Independent, p, 0f, 0f).ResearchMultiplier);
            Assert.Equal(1.2f, DisputeMath.Quote(DisputeActions.Redesign, p, 0f, 0f).ResearchMultiplier);
            Assert.Equal(0.55f, DisputeMath.Quote(DisputeActions.Fight, p, 4.8e9f, 0f).WinChance);
            Assert.Equal("hist.fx.dispute.sue", DisputeMath.Quote(DisputeActions.Sue, p, 0f, 0f).TextKey);

            Assert.Near(1.5 * 0.03 * 4.8e9, DisputeMath.LostFightPayment(p, 4.8e9f), 100);
            Assert.Near(5e6, DisputeMath.LicenseFee(p, 400e6f), 1);
        }

        [Test]
        public void TheNumbers_ReadFromTheBlockOfParamsJson_WithTheCodeFillingTheGaps()
        {
            JsonObject shipped = HistFixture.Obj("{'legalCostPct':0.002,'arbitrationMonths':48,'pWinArbitration':0.6,'licenseFeePct':0.0125,'licenseMonths':36,'researchCostMultiplier':1.3," +
                                                  "'pWinInfringement':0.55,'settlePct':0.03,'redesignMultiplier':1.2,'redesignMonths':24,'plaintiffSettlementPct':0.03}");
            var problems = new List<string>();
            DisputeParams p = DisputeParams.Read(shipped, problems);
            Assert.Equal(0, problems.Count);
            Assert.Equal(0.0125f, p.LicenseFeePct);
            Assert.Equal(24, p.FightMonths, "the length of a fight is not in the design file: the code's");
            Assert.Equal(1.5f, p.FightLossFactor);

            foreach (string bad in new[] { "{'pWinArbitration':1.5}", "{'licenseMonths':0}", "{'researchCostMultiplier':0.5}", "{'settlePct':'x'}", "{'legalCostPct':-1}" })
            {
                var lines = new List<string>();
                DisputeParams.Read(HistFixture.Obj(bad), lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }
            Assert.Equal(0.0125f, DisputeParams.Read(null, null).LicenseFeePct, "no block: the code's numbers");
        }

        [Test]
        public void TheEffect_CarriesTheIdTheModeAndTheAction_AndTheActionMustFitTheMode()
        {
            DisputeSpec ok = DisputeSpec.Read(HistFixture.Obj("{'disputeId':'d','mode':'infringement','action':'redesign'}"), null);
            Assert.Equal("d", ok.DisputeId);
            Assert.Equal(DisputeModes.Infringement, ok.Mode);
            Assert.Equal(DisputeActions.Redesign, ok.Action);

            Assert.Equal(DisputeActions.Open, DisputeSpec.Read(new JsonObject(), null).Action, "open when nothing is said");

            foreach (string bad in new[]
            {
                "{'mode':'infringement','action':'arbitrate'}", "{'mode':'licenseWithdrawal','action':'fight'}", "{'mode':'duel'}", "{'action':'bribe'}",
            })
            {
                var lines = new List<string>();
                DisputeSpec.Read(HistFixture.Obj(bad), lines);
                Assert.True(lines.Any(l => !l.EndsWith("unknown member is ignored")), bad + " must be a problem");
            }

            foreach (string fine in new[] { "{'action':'sue'}", "{'action':'decline'}", "{'action':'open','mode':'infringement'}" })
            {
                var lines = new List<string>();
                DisputeSpec.Read(HistFixture.Obj(fine), lines);
                Assert.Equal(0, lines.Count, fine + " is fine");
            }

            Assert.Equal("hist.decide.patent.withdrawal", DisputeKeys.DecisionEvent(DisputeModes.LicenseWithdrawal));
            Assert.Equal("hist.decide.patent.infringement", DisputeKeys.DecisionEvent(DisputeModes.Infringement));
            Assert.True(DisputeActions.IsAnswerIn(DisputeModes.Infringement, DisputeActions.Settle));
            Assert.False(DisputeActions.IsAnswerIn("duel", DisputeActions.Settle));
        }
    }
}
