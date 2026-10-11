using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-018 (FR-11, NFR-2, AC-11.1): the feature changes the player's company and nothing else. The rules of the default cycle say no for a company that is not the
    /// player's; every engine and the ledger work through <see cref="IPlayerFinance"/>, which is the player's finances or nothing (the adapter hands out no company that
    /// does not say it is the player's), so with no player's company they do nothing at all: no money moves, no counter changes, no window or letter, no state changes;
    /// and an AI company in default is never seen by any of them. The seams of the game side that cannot be loaded here (the adapter, P3, the reputation) are read from the
    /// sources.
    /// </summary>
    public class AiIsolationTests
    {
        private static readonly DateTime Today = new DateTime(1977, 5, 1);

        // The view of the most tempting company: the 119th day of a default, 30 million under a threshold of 24.3 million, nothing used yet.
        private static BankruptcyView Tempting(bool isPlayer, double money = -30000000.0, bool inDefault = true, int days = 119)
        {
            return new BankruptcyView(money, 24300000.0, inDefault, false, days, 120, Today, isPlayer);
        }

        // ---- the rules ----

        [Test]
        public void TheLastChance_IsNeverHeld_ForACompanyThatIsNotThePlayers()
        {
            Assert.True(RecoveryRules.MustHoldForBailout(Tempting(true), 0, 2, false), "the control: the same numbers for the player hold the counter");
            Assert.False(RecoveryRules.MustHoldForBailout(Tempting(false), 0, 2, false), "AC-11.1: the AI goes bankrupt by the game's own rule");
            Assert.False(RecoveryRules.MustHoldForBailout(Tempting(false), 0, 100, false));
        }

        [Test]
        public void TheWarning_IsNeverSent_ForACompanyThatIsNotThePlayers()
        {
            var config = new PreWarningConfig();
            BankruptcyView falling = Tempting(true, money: -20000000.0, inDefault: false, days: 0);
            BankruptcyView fallingAi = Tempting(false, money: -20000000.0, inDefault: false, days: 0);
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(falling, false, config), "the control");
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(fallingAi, false, config));

            BankruptcyView back = Tempting(true, money: -1000000.0, inDefault: false, days: 0);
            BankruptcyView backAi = Tempting(false, money: -1000000.0, inDefault: false, days: 0);
            Assert.Equal(PreWarningDecision.Reset, RecoveryRules.PreWarning(back, true, config), "the control");
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(backAi, true, config), "an AI company has no episode to forget");
        }

        [Test]
        public void TheEarlyExit_NeverCounts_ForACompanyThatIsNotThePlayers()
        {
            BankruptcyView above = Tempting(true, money: 1000000.0);
            BankruptcyView aboveAi = Tempting(false, money: 1000000.0);
            Assert.Equal(4, RecoveryRules.NextRecoveryStreak(above, 3), "the control");
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(aboveAi, 3), "a streak is never counted for the AI, so the default is never cancelled for it");
        }

        [Test]
        public void TheBridgeLoan_IsNeverOffered_ToACompanyThatIsNotThePlayer()
        {
            var config = new BridgeConfig();
            Eligibility player = RecoveryRules.CanTakeBridge(Tempting(true), false, null, config);
            Eligibility ai = RecoveryRules.CanTakeBridge(Tempting(false), false, null, config);

            Assert.True(player.Ok, "the control");
            Assert.False(ai.Ok);
            Assert.Null(ai.ReasonKey, "no reason is shown for a company the window does not serve");
        }

        [Test]
        public void TheView_OfTheFinancesOfTheFeature_IsTheViewOfThePlayer()
        {
            var finance = new FakePlayerFinance();
            Assert.True(finance.View().IsPlayer, "IPlayerFinance is the player's finances: the view says so, and only it does");
        }

        // ---- the engines without the player's company ----

        private sealed class Window : IBailoutPresenter
        {
            public int Opened;

            public bool Open(BailoutOffer offer)
            {
                Opened++;
                return true;
            }
        }

        private sealed class Letters : IRecoveryLetters
        {
            public int Sent;

            public bool SendPreWarning(double balance, double threshold)
            {
                Sent++;
                return true;
            }

            public bool SendDefault(double balance, double threshold, int daysToBankruptcy)
            {
                Sent++;
                return true;
            }
        }

        // Everything the feature has, around a finance that is not the player's (the real adapter says "not available" for a company that is not the player's), with
        // obligations in the save and the 119th day of a default on the counter: the most tempting state there is.
        private sealed class World
        {
            public readonly RecoveryRig Rig = SettlementKit.PopulatedRig();
            public readonly Window Offer = new Window();
            public readonly Letters Mail = new Letters();
            public readonly LoanBook Loans;
            public readonly CreditLineEngine Line;
            public readonly BridgeLoanEngine Bridge;
            public readonly BailoutEngine Bailout;
            public readonly DefaultCycleEngine Cycle;
            public readonly RecoveryMonitorEngine Monitor;
            public readonly DisableSettlementEngine Disable;
            public int Reputation;

            public World(bool playerIsThere)
            {
                Rig.Finance.MoneyValue = -30000000.0;
                Rig.Finance.InDefault = true;
                Rig.Finance.Days = 119;
                Rig.Finance.TodayValue = Today;
                Rig.State.episode.bailoutDeclined = false;
                Loans = new LoanBook(Rig.Env);
                Line = new CreditLineEngine(Rig.Env, Loans);
                Bridge = new BridgeLoanEngine(Rig.Env, Loans);
                Bailout = new BailoutEngine(Rig.Env, Loans, null, (delta, reason) => Reputation++);
                Bailout.Presenter = Offer;
                Cycle = new DefaultCycleEngine(Rig.Env, Bailout);
                Monitor = new RecoveryMonitorEngine(Rig.Env, Mail);
                Disable = new DisableSettlementEngine(Rig.Env, new FakeSettlementFlags());
                Rig.Finance.Available = playerIsThere;
            }
        }

        // Every way into the feature that could do something, once.
        private static void DoEverything(World w)
        {
            w.Cycle.BeforeTick();
            w.Cycle.AfterTick();
            w.Monitor.OnDay();
            w.Monitor.OnPlayerDefault();
            w.Bailout.TryHold();
            w.Bailout.OnBatchEnded();
            w.Bailout.Accept(BailoutChoice.RescueLoan);
            w.Bailout.Accept(BailoutChoice.InvestorShare);
            w.Bailout.OnMonth();
            w.Loans.OnMonth();
            w.Loans.Prepay("bridge-1", 1000.0);
            w.Line.EnsureOpened();
            w.Line.OnDay();
            w.Line.OnMonth();
            w.Line.Draw(1000000.0);
            w.Line.Repay(1000.0);
            LoanData bridge;
            w.Bridge.Take(1000000.0, out bridge);
            w.Disable.Settle();
        }

        [Test]
        public void WithoutThePlayersCompany_NoEngineDoesAnything()
        {
            var w = new World(playerIsThere: false);
            string state = SettlementKit.Describe(w.Rig.State);

            DoEverything(w);

            Assert.Equal(0, w.Rig.Finance.Added.Count + w.Rig.Finance.Spent.Count + w.Rig.Finance.Earned.Count, "no money moves");
            Assert.Equal(119, w.Rig.Finance.Days, "the counter of the default is not touched");
            Assert.Equal(0, w.Rig.Finance.CancelCalls, "the default is not cancelled");
            Assert.Equal(0, w.Offer.Opened, "AC-11.1: no window");
            Assert.Equal(0, w.Mail.Sent, "no letter");
            Assert.Equal(0, w.Reputation, "no change of reputation");
            Assert.Equal(state, SettlementKit.Describe(w.Rig.State), "no loan, no line, no contract, no journal record is touched");
            Assert.Equal(0, w.Rig.OtherFeatureLines.Count);
        }

        [Test]
        public void WithThePlayer_TheSameEnginesDoAct_SoTheTestAbove_IsNotEmpty()
        {
            // the control: with the player's company the same calls do move money and open the window
            var w = new World(playerIsThere: true);

            DoEverything(w);

            Assert.True(w.Rig.Finance.Added.Count > 0, "money moved");
            Assert.True(w.Offer.Opened > 0 || w.Rig.Finance.CancelCalls > 0, "the last chance was shown or accepted");
        }

        [Test]
        public void AnAiCompanyInDefault_IsNeverSeen_WhileThePlayerIsHealthy()
        {
            // AC-11.1: an AI company is on its 119th day with a balance below the threshold. The engines hold one set of finances, the player's; nothing in them can
            // name another company, so the AI's counter is the game's alone. The player here is healthy and the engines leave the day to the game.
            var w = new World(playerIsThere: true);
            w.Rig.Finance.InDefault = false;
            w.Rig.Finance.Days = 0;
            w.Rig.Finance.MoneyValue = 50000000.0;
            var ai = new FakePlayerFinance { MoneyValue = -30000000.0, InDefault = true, Days = 119 };

            Assert.False(w.Cycle.BeforeTick(), "no counter is held");
            Assert.False(w.Cycle.AfterTick());
            Assert.False(w.Bailout.TryHold());

            Assert.Equal(0, w.Offer.Opened, "the mod opens no window");
            Assert.Equal(0, w.Rig.Finance.Days);
            Assert.Equal(119, ai.Days, "the AI company's counter is where the game left it");
            Assert.Equal(0, ai.Added.Count + ai.Spent.Count + ai.Earned.Count + ai.CancelCalls);
        }

        // ---- the ledger ----

        [Test]
        public void TheLedger_RefusesACompanyThatIsNotThePlayers()
        {
            var rig = new RecoveryRig();
            rig.Finance.Available = false;   // the finances of the feature are the player's; for anything else they are not there

            Assert.False(rig.Core.CapitalIn(CoreMoney.Own, 1000.0, "bridge.open", ""));
            Assert.False(rig.Core.CapitalOut(CoreMoney.Own, 1000.0, "loan.principal", ""));
            Assert.False(rig.Core.Expense(CoreMoney.Own, 1000.0, FakePlayerFinance.InterestItem, "loan.interest", ""));
            Assert.False(rig.Core.Revenue(CoreMoney.Own, 1000.0, FakePlayerFinance.SalesItem, "lines.sell", ""));
            Assert.False(rig.Core.CapitalOut("rival-acquisitions", 1000.0, "acq.price", ""), "also for the calls of another feature");

            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count + rig.Finance.Earned.Count);
            Assert.Equal(0, rig.Journal.Count, "a refused operation is not journaled");
            Assert.Equal(0, rig.OtherFeatureLines.Count);
        }

        // ---- the game side, from the sources ----

        private static string FeatureDir()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            Assert.True(Directory.Exists(dir), "the folder of the feature is missing: " + dir);
            return dir;
        }

        private static List<KeyValuePair<string, string>> AllCode()
        {
            var found = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                found.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), true)));
            }
            Assert.True(found.Count > 20, "too few sources found: " + found.Count);
            return found;
        }

        [Test]
        public void TheAdapter_HandsOutOnlyACompanyThatSaysItIsThePlayers()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PlayerFinanceAdapter.cs")), false);
            int start = code.IndexOf("internal static ICompany PlayerCompany()", StringComparison.Ordinal);
            Assert.True(start >= 0, "the one way to the company");
            int end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
            string body = code.Substring(start, end - start);

            Assert.True(body.Contains("GamePlayer.Instance"), "the company of the player of the game");
            Assert.True(Regex.IsMatch(body, @"company\.IsPlayer\s*\?\s*company\s*:\s*null"), "a company that is not the player's is not handed out (FR-11): " + body);
        }

        [Test]
        public void TheOnlyWayToACompany_IsTheAdapter()
        {
            // `.Company` is the property of the player object; the adapter reads it once, in PlayerCompany(). Nobody else reaches a company.
            var company = new Regex(@"\.\s*Company\b");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode())
            {
                if (file.Key == "PlayerFinanceAdapter.cs") continue;
                foreach (Match m in company.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index));
            }
            Assert.True(offenders.Count == 0, "a company reached some other way than PlayerFinanceAdapter.PlayerCompany():\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheCounterOfTheDefault_IsWrittenOnlyThroughTheFinancesOfThePlayer()
        {
            var write = new Regex(@"(\w+)\s*\.\s*DaysInDefault\s*=(?!=)");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode())
            {
                foreach (Match m in write.Matches(file.Value))
                {
                    string owner = m.Groups[1].Value;
                    bool adapter = file.Key == "PlayerFinanceAdapter.cs" && owner == "company";
                    if (owner != "finance" && !adapter) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": " + m.Value);
                }
            }
            Assert.True(offenders.Count == 0, "the counter of a default is set on the player's finances only:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheReputation_IsChangedForThePlayerOnly()
        {
            var add = new Regex(@"ModReputation\s*\.\s*Add\s*\(([^;]*)\)\s*;");
            int calls = 0;
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode())
            {
                foreach (Match m in add.Matches(file.Value))
                {
                    calls++;
                    if (!m.Groups[1].Value.TrimStart().StartsWith("PlayerFinanceAdapter.PlayerCompany()", StringComparison.Ordinal))
                        offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index));
                }
            }
            Assert.True(calls >= 1, "the rescue lowers the reputation");
            Assert.True(offenders.Count == 0, "ModReputation.Add on the player's company only:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheLetterOfTheDefault_IsSentByThePatchForThePlayerOnly()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Patches", "BankruptcyPatches.cs")), false);
            int start = code.IndexOf("private static void TriggerPostfix(ICompany company)", StringComparison.Ordinal);
            int end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
            string body = code.Substring(start, end - start);

            Assert.True(Regex.IsMatch(body, @"company\s*==\s*null\s*\|\|\s*!company\.IsPlayer"), "P3 returns at once for an AI company: " + body);
            Assert.True(body.IndexOf("company.IsPlayer", StringComparison.Ordinal) < body.IndexOf("ModHooks.Guard", StringComparison.Ordinal), "the filter comes before any work");
        }

        [Test]
        public void TheTwoPatchesOnTheTick_AreMadeOfNoCompany()
        {
            // P1 and P2 are called once per day for the whole game, with the AI companies handled inside the game's method. They take no company, loop over none, and
            // pass the day on to the engine of the player's finances.
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Patches", "BankruptcyPatches.cs")), true);
            Assert.True(code.Contains("private static bool TickPrefix()") && code.Contains("private static void TickPostfix()"));
            Assert.False(Regex.IsMatch(code, @"\bforeach\b|\bfor\s*\("), "no loop over companies in the patches");
            Assert.True(code.Contains("feature.Cycle.BeforeTick()") && code.Contains("feature.Cycle.AfterTick()"));
        }
    }
}
