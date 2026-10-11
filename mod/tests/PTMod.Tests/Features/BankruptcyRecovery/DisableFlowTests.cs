using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-022 and TSK-017 (FR-12 items 4 and 6, AC-12.2, AC-12.6, M10): the safe switch-off through the core's real side-file machinery. The core keeps no state of a
    /// feature that is switched off: it hands back, when the feature is on again, the copy of the section of the last save (or none). These tests drive the
    /// feature's section with the core's own <c>SideFileModel</c> (a participant that can be made inactive, restored, read and reset) and check the promise of
    /// the design: switching off and on again gives no money back. Loans that were turned into ordinary debt do not return, the line stays closed, the counter of the
    /// rescues and the pause of the bridge loan do not fall back, and a state kept for a switch-on is never used for another game.
    /// </summary>
    public class DisableFlowTests
    {
        private const string Id = RecoveryTestData.FeatureId;

        // The file a save writes: the text of the side file and the flags of the core's section next to it (the core keeps them in a section of its own).
        private sealed class SaveFile
        {
            public string Text;
            public FakeSettlementFlags Flags;
        }

        // One run of the game around the feature: the core's side-file model with the feature's section, the flags, the finances, the engine.
        private sealed class Game
        {
            public bool Active = true;
            public readonly FakeSettlementFlags Flags = new FakeSettlementFlags();
            public readonly FakePlayerFinance Finance = new FakePlayerFinance();
            public readonly List<string> Notes = new List<string>();
            public readonly SideFileModel Model;
            public readonly RecoverySaveSection Section;
            public readonly RecoveryEnvironment Env;
            public readonly DisableSettlementEngine Engine;
            public readonly RecoveryJournal Journal;

            public Game(bool active = true)
            {
                Active = active;
                Section = new RecoverySaveSection(Id);
                Model = new SideFileModel(id => id != Id || Active);
                string problem;
                Assert.True(Model.TryRegister(Section, out problem), problem);
                Section.UseMarks(Flags, () => Active, Notes.Add, () => Model.Header);

                Journal = new RecoveryJournal(() => Section.State, () => Finance.TodayValue);
                var core = new CapitalLedgerCore(Finance, Journal, CoreMoney.Own, (feature, line, own) => { });
                var money = new CoreMoney(core, () => FakePlayerFinance.RoyaltiesItem);
                Env = new RecoveryEnvironment(Finance, money, Journal, () => Section.State, RecoveryBalance.Defaults, part => Active, null);
                Engine = new DisableSettlementEngine(Env, Flags);

                Finance.MoneyValue = 200000000.0;
                Finance.DebtLimit = 24300000.0;
                Finance.TodayValue = new DateTime(1977, 6, 1);
            }

            public RecoveryState State
            {
                get { return Section.State; }
            }

            // OnDisabled: the feature is no longer active when the core calls it.
            public SettleResult Disable()
            {
                Active = false;
                SettleResult result = Engine.Settle();
                Section.Freeze();
                return result;
            }

            // The core: the state of the feature becomes Enabled, ModSave hands the section to the participant.
            public void Enable()
            {
                Active = true;
                Model.RestoreParticipant(Id);
            }

            public SaveFile Save()
            {
                SideFileBuildReport report = Model.Build(new SideFileHeader { modVersion = "0.1.0", gameDate = RecoveryDates.ToText(Finance.TodayValue) });
                Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
                return new SaveFile { Text = ModJson.Write(report.Document, true), Flags = Flags.Clone() };
            }

            // A save is read: the flags of the core's section first (the core's section leads the file), then the sections.
            public void Load(SaveFile file)
            {
                Flags.Values.Clear();
                foreach (KeyValuePair<string, int> pair in file.Flags.Values) Flags.Values[pair.Key] = pair.Value;
                SideFileReadReport report = Model.Apply(file.Text);
                Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
                if (!Active) return;   // the handlers of GameReady and AfterRead are not called for a feature that is off
                Section.DropFrozen();
                Section.Reconcile();
            }

            // The menu or a new game: the flags of the core's section go first, then every participant is reset, active or not.
            public void NewGame()
            {
                Flags.Values.Clear();
                Model.StartNewGame();
            }

            public bool HasSectionInTheFile(SaveFile file)
            {
                JsonObject features = RecoveryTestData.ParseObject(file.Text).GetObject("features");
                return features != null && features.ContainsKey(Id);
            }
        }

        private static void Populate(Game game)
        {
            game.Section.Read(RecoveryStateCodec.Write(RecoveryTestData.PopulatedState()), 1);
        }

        private static string Loans(RecoveryState state)
        {
            var ids = new List<string>();
            foreach (LoanData loan in state.loans) ids.Add(loan.id);
            return string.Join(",", ids);
        }

        // ---- the same session ----

        [Test]
        public void M10_SwitchOffAndOn_TheDebtIsTakenOnceAndDoesNotComeBack()
        {
            var game = new Game();
            Populate(game);
            double before = game.Finance.MoneyValue;

            SettleResult result = game.Disable();
            double afterDisable = game.Finance.MoneyValue;
            game.Enable();

            Assert.Equal(SettleStatus.Done, result.Status);
            Assert.Near(before - SettlementKit.PopulatedTotal, afterDisable, 0);
            Assert.Near(afterDisable, game.Finance.MoneyValue, 0, "switching it on again costs nothing and gives nothing");
            Assert.Equal(string.Empty, Loans(game.State), "the loans do not come back");
            Assert.Equal(CreditLineData.StateClosed, game.State.creditLine.state, "the line does not open again");
            Assert.False(game.State.investorShare.active);
            Assert.Equal(1, game.State.bailoutsUsed, "the counter of the rescues is the one it was (AC-12.6)");
            Assert.Equal("1977-06-01", game.State.lastBridgeClosedOn, "no new bridge loan until the pause is over (AC-12.6)");
        }

        [Test]
        public void SwitchOffAndOn_TheJournalKeepsTheRecordOfTheConversion()
        {
            var game = new Game();
            Populate(game);

            game.Disable();
            game.Enable();

            int conversions = 0;
            foreach (JournalEntryData entry in game.State.journal)
            {
                if (entry.op == "disable.convert") conversions++;
            }
            Assert.Equal(1, conversions, "TSK-014: the record of the conversion is in the journal of the tab after the feature is back");
            Assert.Equal(4, game.State.journal.Count, "the three records that were there, and the conversion");
        }

        [Test]
        public void SwitchOffAndOn_ThenSavedAndLoaded_StaysSettled()
        {
            var game = new Game();
            Populate(game);
            game.Disable();
            game.Enable();

            SaveFile file = game.Save();
            var next = new Game();
            next.Load(file);

            Assert.Equal(string.Empty, Loans(next.State));
            Assert.Equal(CreditLineData.StateClosed, next.State.creditLine.state);
            Assert.Equal(1, next.State.bailoutsUsed);
            Assert.Equal(1, next.Flags.GetInt(SettlementMarks.Settled), "the marks travel in the flags of the save");
        }

        [Test]
        public void SwitchingOffTwice_ChargesOnce()
        {
            var game = new Game();
            Populate(game);

            game.Disable();
            game.Enable();
            double afterFirst = game.Finance.MoneyValue;
            SettleResult second = game.Disable();
            game.Enable();

            Assert.Equal(SettleStatus.NothingToSettle, second.Status);
            Assert.Near(afterFirst, game.Finance.MoneyValue, 0);
            Assert.Equal(1, game.Finance.Added.Count, "ONE operation in all");
        }

        [Test]
        public void ThePlayerCanBorrowAgain_AfterTheSwitchOn_AndThatLoanIsSettledOnItsOwn()
        {
            var game = new Game();
            Populate(game);
            game.Disable();
            game.Enable();

            var loans = new LoanBook(game.Env);
            LoanData fresh = loans.Open(LoanData.KindRescueLoan, 8000000.0, 0.40, 36);
            Assert.NotNull(fresh);
            game.Finance.AddMoney(8000000.0);
            double before = game.Finance.MoneyValue;
            SaveFile file = game.Save();
            var next = new Game();
            next.Load(file);

            Assert.Equal(fresh.id, Loans(next.State), "a loan taken after the switch-on is a real loan: the marks leave it alone");

            next.Finance.MoneyValue = before;
            SettleResult result = next.Disable();
            Assert.Near(8000000.0, result.Converted, 0, "and only it is charged at the next switch-off");
        }

        // ---- the core hands back an older copy ----

        [Test]
        public void TheCopyOfTheLastSave_IsNotWhatComesBack_LoansOfAnOlderSaveStaySettled()
        {
            var game = new Game();
            Populate(game);
            SaveFile before = game.Save();                  // the core now holds this section for a feature that is switched off
            game.Disable();
            Assert.Equal(string.Empty, Loans(game.State));

            game.Enable();

            Assert.Equal(string.Empty, Loans(game.State), "without the frozen state or the marks the loans of the older copy would be back");
            Assert.True(RecoveryTestData.ParseObject(before.Text).GetObject("features").GetObject(Id).GetArray("loans").Count == 2, "the older copy does hold the loans");
        }

        [Test]
        public void ASaveWrittenWhileTheFeatureIsOff_HoldsTheOldCopy_AndTheMarksSettleIt()
        {
            var game = new Game();
            Populate(game);
            game.Save();                                     // the last save with the feature on: the loans are in it
            game.Disable();
            SaveFile whileOff = game.Save();                 // written while off: the core writes back the copy it kept
            Assert.True(game.HasSectionInTheFile(whileOff), "the older copy is in the file");

            var restarted = new Game(active: false);        // the game is started again; the feature is switched on in the hub later
            restarted.Load(whileOff);
            restarted.Enable();

            Assert.Equal(string.Empty, Loans(restarted.State), "B: the settled loans are not restored from the older copy");
            Assert.Equal(CreditLineData.StateClosed, restarted.State.creditLine.state);
            Assert.False(restarted.State.investorShare.active);
            Assert.Equal(1, restarted.State.bailoutsUsed);
            Assert.Equal("1977-06-01", restarted.State.lastBridgeClosedOn);
        }

        [Test]
        public void ASaveWrittenWhileTheFeatureIsOff_WithoutAnyEarlierSave_HasNoSection_AndTheMarksKeepTheCounters()
        {
            var game = new Game();
            Populate(game);                                  // read from a file: nothing is held back for it
            game.Disable();
            SaveFile whileOff = game.Save();
            Assert.False(game.HasSectionInTheFile(whileOff), "A: the core has no copy to write, so the section is left out");

            var restarted = new Game(active: false);
            restarted.Load(whileOff);
            restarted.Enable();

            Assert.Equal(string.Empty, Loans(restarted.State));
            Assert.Equal(CreditLineData.StateClosed, restarted.State.creditLine.state, "the line does not open again");
            Assert.Equal(1, restarted.State.bailoutsUsed, "the counter of the rescues does not fall back to 0");
            Assert.Equal("1977-06-01", restarted.State.lastBridgeClosedOn, "the pause of the bridge loan is not lost");
            Assert.Equal(3, restarted.State.nextLoanSeq, "the ids are not handed out again");
        }

        [Test]
        public void NothingOwed_TheCountersStillSurviveTheSwitch()
        {
            var game = new Game();
            game.State.bailoutsUsed = 2;
            game.State.lastBridgeClosedOn = "1976-01-15";
            game.State.nextLoanSeq = 6;

            SettleResult result = game.Disable();
            SaveFile whileOff = game.Save();
            var restarted = new Game(active: false);
            restarted.Load(whileOff);
            restarted.Enable();

            Assert.Equal(SettleStatus.NothingToSettle, result.Status);
            Assert.Equal(2, restarted.State.bailoutsUsed, "toggling must not give a player more rescues");
            Assert.Equal("1976-01-15", restarted.State.lastBridgeClosedOn);
            Assert.Equal(6, restarted.State.nextLoanSeq);
            Assert.Equal(CreditLineData.StateNone, restarted.State.creditLine.state, "nothing closed the line, so nothing is changed in it");
        }

        // ---- another game ----

        [Test]
        public void TheSameSaveLoadedAgain_IsTheSaveNotTheStateOfTheEarlierPlay()
        {
            var game = new Game();
            Populate(game);
            SaveFile saved = game.Save();
            game.Disable();                                   // the debt is taken ...

            game.Load(saved);                                 // ... and the player loads the save from before: the world is as it was
            game.Enable();

            Assert.Equal("bridge-1,rescue-2", Loans(game.State), "in that timeline the money was never taken, so the loans are still owed");
            Assert.Equal(CreditLineData.StateOpen, game.State.creditLine.state);
            Assert.True(game.State.investorShare.active);
            Assert.Equal(0, game.Flags.GetInt(SettlementMarks.Settled), "the flags are those of the save");
        }

        [Test]
        public void ANewGame_DropsTheStateKeptForASwitchOn()
        {
            var game = new Game();
            Populate(game);
            game.Disable();

            game.NewGame();
            game.Enable();

            Assert.Equal(string.Empty, Loans(game.State));
            Assert.Equal(CreditLineData.StateNone, game.State.creditLine.state, "the new game starts clean, not from the old game's closed line");
            Assert.Equal(0, game.State.bailoutsUsed);
            Assert.Equal(string.Empty, game.State.lastBridgeClosedOn);
            Assert.False(game.Section.IsFrozen);
        }

        [Test]
        public void ALoadOfAnotherSave_DropsTheStateKeptForASwitchOn()
        {
            var game = new Game();
            Populate(game);
            game.Disable();

            var otherSave = new Game();
            var other = RecoveryTestData.PopulatedState();
            other.loans.RemoveAt(1);
            other.bailoutsUsed = 0;
            otherSave.Section.Read(RecoveryStateCodec.Write(other), 1);
            SaveFile file = otherSave.Save();
            game.Load(file);
            game.Enable();

            Assert.Equal("bridge-1", Loans(game.State), "the other game's own loans, not the settled state of this one");
            Assert.Equal(0, game.State.bailoutsUsed);
        }

        [Test]
        public void TheStateKeptForASwitchOn_IsUsedOnce()
        {
            var game = new Game();
            Populate(game);
            game.Disable();
            Assert.True(game.Section.IsFrozen);

            game.Enable();

            Assert.False(game.Section.IsFrozen, "the first hand-over takes it; the next one gets what the core keeps");
        }

        [Test]
        public void ARestoreWhileTheFeatureIsStillOff_DoesNotUseTheKeptState()
        {
            var game = new Game();
            Populate(game);
            game.Disable();

            game.Model.RestoreParticipant(Id);               // handed over to an inactive participant: a reset of the mod, not a switch-on

            Assert.False(game.Section.IsFrozen);
            Assert.Equal(string.Empty, Loans(game.State));
            Assert.Equal(0, game.State.journal.Count, "the journal of the kept state is not used");
            Assert.Equal(CreditLineData.StateClosed, game.State.creditLine.state, "only the marks of the settlement apply to a reset: the line is closed");
        }

        // ---- when nothing is possible ----

        [Test]
        public void WithoutThePlayersCompany_NothingIsSettled_AndTheLoansComeBackWhenTheFeatureDoes()
        {
            var game = new Game();
            Populate(game);
            game.Finance.Available = false;                  // the main menu

            SettleResult result = game.Disable();
            game.Finance.Available = true;
            game.Enable();

            Assert.Equal(SettleStatus.Unavailable, result.Status);
            Assert.Equal("bridge-1,rescue-2", Loans(game.State), "nothing was charged, so nothing is closed: the obligations are what they were");
            Assert.Near(200000000.0, game.Finance.MoneyValue, 0);
            Assert.Equal(0, game.Flags.GetInt(SettlementMarks.Settled));
        }
    }
}
