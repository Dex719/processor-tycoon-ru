using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The suit of a victim of an antitrust case (TSK-010; FR-6, design.md formula 5): twelve months of legal costs, then a win with <c>pWin</c> moves 3 % of the defendant's
    /// revenue to the plaintiff (AC-6.1); a company that is gone ends the suit without a payment.
    /// </summary>
    [Covers(typeof(SettlementActions), typeof(SettlementKeys), typeof(SettlementParams), typeof(SettlementQuote), typeof(SettlementCore))]
    public class SettlementTests
    {
        private const string Plaintiff = "player";
        private const string Defendant = "ai:0";

        private sealed class Env
        {
            public readonly HistSaveData Data = new HistSaveData();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly List<string> Log = new List<string>();
            public FixedRng Rng = new FixedRng(0.1f);
            public SettlementCore Core;
            public readonly SettlementParams Params = SettlementParams.Read(new JsonObject(), null, null);

            public Env()
            {
                Economy.Revenue[Plaintiff] = 5e9f;
                Economy.Revenue[Defendant] = 30e9f;
                Economy.Names[Defendant] = "Intel";
                Core = new SettlementCore(() => Data, Economy, () => Rng, a => a.ToString("0"), Log.Add);
            }

            public void Month()
            {
                Economy.Today += 30;
                Core.OnMonth();
            }
        }

        [Test]
        public void Ac61_AWonSuit_MovesThreePercentOfTheDefendantsRevenue_FromFinesToRoyalties()
        {
            var env = new Env();
            Assert.True(env.Core.File("hist.2009.settlement", Plaintiff, Defendant, env.Params));

            for (int i = 0; i < 14 && env.Core.Suits.Count > 0; i++) env.Month();

            Assert.Equal(0, env.Core.Suits.Count, "the verdict closes the suit");
            Assert.Near(0.03 * 30e9, env.Economy.Earned(Plaintiff, "Royalties"), 1e3, "0.03 x 30 bn = 900 million to the plaintiff");
            Assert.Near(0.03 * 30e9, env.Economy.Spent(Defendant, "Fines"), 1e3, "and the same sum paid as a fine");
            Assert.True(env.Economy.Notes(Plaintiff).Any(n => n.Origin == SettlementKeys.NoteWon), "the player is told");
            Assert.True(env.Economy.Notes(Defendant).Any(n => n.Origin == SettlementKeys.NotePaid), "so is a defendant that is the player");
        }

        [Test]
        public void TheLegalCosts_AreFourTenthsOfAPercentOfTheRevenueEveryMonth_ForTwelveMonths()
        {
            var env = new Env();
            env.Core.File("s", Plaintiff, Defendant, env.Params);

            while (env.Core.Suits.Count > 0 && env.Economy.Today < 14000 + 500) env.Month();

            float perMonth = 0.004f * 5e9f;
            int charges = env.Economy.Flows.Count(f => f.Kind == "spend" && f.Company == Plaintiff);
            Assert.True(charges >= 11 && charges <= 13, "twelve months of costs: " + charges);
            Assert.Near(perMonth * charges, env.Economy.Spent(Plaintiff, "Fines"), 1e3, "each a fine of 0.4 % of the plaintiff's revenue");
        }

        [Test]
        public void ALostSuit_PaysNothing_ButTheLegalCostsAreGone()
        {
            var env = new Env { Rng = new FixedRng(0.95f) };
            env.Core.File("s", Plaintiff, Defendant, env.Params);
            for (int i = 0; i < 14 && env.Core.Suits.Count > 0; i++) env.Month();

            Assert.Equal(0, env.Core.Suits.Count);
            Assert.Equal(0f, env.Economy.Earned(Plaintiff));
            Assert.Equal(0f, env.Economy.Spent(Defendant));
            Assert.True(env.Economy.Spent(Plaintiff) > 0f, "the costs were paid");
            Assert.True(env.Economy.Notes(Plaintiff).Any(n => n.Origin == SettlementKeys.NoteLost));
        }

        [Test]
        public void TheChanceToWinIsPWin_OneDrawPerVerdict()
        {
            var env = new Env { Rng = new FixedRng(0.69f) };
            env.Core.File("won", Plaintiff, Defendant, env.Params);
            while (env.Core.Suits.Count > 0 && env.Economy.Today < 15000) env.Month();
            Assert.True(env.Economy.Earned(Plaintiff) > 0f, "0.69 is below pWin 0.7");
            Assert.Equal(1, env.Rng.Draws, "one number for one verdict");

            var lost = new Env { Rng = new FixedRng(0.70f) };
            lost.Core.File("lost", Plaintiff, Defendant, lost.Params);
            while (lost.Core.Suits.Count > 0 && lost.Economy.Today < 15000) lost.Month();
            Assert.Equal(0f, lost.Economy.Earned(Plaintiff), "0.70 is not below pWin");
        }

        [Test]
        public void ASuitWhosePartyWentBankrupt_EndsWithoutAPayment()
        {
            var env = new Env();
            env.Core.File("s", Plaintiff, Defendant, env.Params);
            env.Month();
            float costs = env.Economy.Spent(Plaintiff);

            env.Economy.Dead.Add(Defendant);
            env.Month();
            Assert.Equal(0, env.Core.Suits.Count);
            Assert.Equal(costs, env.Economy.Spent(Plaintiff), "no more costs");
            Assert.Equal(0f, env.Economy.Earned(Plaintiff));
        }

        [Test]
        public void Filing_NeedsTwoCompaniesOfTheGame_AndAFreeId()
        {
            var env = new Env();
            Assert.False(env.Core.File("", Plaintiff, Defendant, env.Params), "no id");
            Assert.False(env.Core.File("s", Plaintiff, Plaintiff, env.Params), "against itself");
            Assert.False(env.Core.File("s", "nobody", Defendant, env.Params), "not a company key");
            env.Economy.Dead.Add(Defendant);
            Assert.False(env.Core.File("s", Plaintiff, Defendant, env.Params), "a defendant that is gone");
            env.Economy.Dead.Clear();

            Assert.True(env.Core.File("s", Plaintiff, Defendant, env.Params));
            Assert.False(env.Core.File("s", Plaintiff, Defendant, env.Params), "the same id twice");
            Assert.Equal(1, env.Core.Suits.Count);
            Assert.NotNull(env.Core.Find("s"));
        }

        [Test]
        public void TheSuit_KeepsTheNumbersTheWinnerWasShown()
        {
            var env = new Env();
            SettlementParams custom = SettlementParams.Read(HistFixture.Obj("{'pWin':0.5,'settlementPct':0.02,'months':6,'legalCostPct':0.001}"), null, null);
            env.Core.File("s", Plaintiff, Defendant, custom);

            SettlementDto suit = env.Core.Find("s");
            Assert.Equal(0.5f, suit.pWin);
            Assert.Equal(0.02f, suit.settlementPct);
            Assert.Near(0.001 * 5e9, suit.legalPerMonth, 1e3);
            Assert.Equal(14000 + HistTime.MonthsToDays(6), suit.resolveDay);
        }

        [Test]
        public void TheQuote_ShowsTheCostsAndTheAward_ForTheLineUnderTheButton()
        {
            SettlementQuote quote = SettlementCore.Quote(SettlementParams.Read(new JsonObject(), null, null), 5e9f, 30e9f);
            Assert.Near(2e7, quote.LegalPerMonth, 1e3);
            Assert.Equal(12, quote.Months);
            Assert.Near(2.4e8, quote.TotalLegal, 1e4);
            Assert.Near(9e8, quote.Award, 1e3);
        }

        [Test]
        public void TheNumbers_ComeFromTheEffectThenTheDefaultsBlockThenTheCode()
        {
            JsonObject defaults = HistFixture.Obj("{'legalCostPct':0.004,'months':12,'pWin':0.7,'settlementPct':0.03}");
            SettlementParams fromDefaults = SettlementParams.Read(new JsonObject(), defaults, null);
            Assert.Equal(0.7f, fromDefaults.PWin);

            SettlementParams own = SettlementParams.Read(HistFixture.Obj("{'pWin':0.4}"), defaults, null);
            Assert.Equal(0.4f, own.PWin, "the effect's own number wins");
            Assert.Equal(12, own.Months, "the rest from the block");

            SettlementParams code = SettlementParams.Read(new JsonObject(), new JsonObject(), null);
            Assert.Equal(0.03f, code.SettlementPct, "and from the code when both are silent");

            Assert.Equal(SettlementActions.Sue, SettlementParams.ActionOf(null));
            Assert.Equal(SettlementActions.Decline, SettlementParams.ActionOf(HistFixture.Obj("{'action':'decline'}")));

            foreach (string bad in new[] { "{'pWin':1.5}", "{'action':'abandon'}", "{'months':0}", "{'settlementPct':0.9}", "{'legalCostPct':'x'}" })
            {
                var lines = new List<string>();
                SettlementParams.Read(HistFixture.Obj(bad), null, lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }
        }
    }
}
