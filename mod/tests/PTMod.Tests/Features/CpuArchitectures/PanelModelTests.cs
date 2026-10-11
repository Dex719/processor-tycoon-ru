using System.Collections.Generic;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// What the panel "Архитектуры" shows (FR-3; TSK-012, TSK-013), without the game: the seven cards with their status and button (AC-3.1), the reasons of
    /// a lock and a pause, the counter of the tab that grows when a family becomes available and goes when the tab was looked at (FR-3 item 7), and the
    /// arithmetic of the funding slider and the lines of the lab block (FR-3 items 4-5).
    /// </summary>
    [Covers(typeof(FamilyCardModel), typeof(FamilyCardInfo), typeof(CardStatus), typeof(CardAction), typeof(NewFamilyTracker), typeof(LabBlockModel))]
    public class PanelModelTests
    {
        private static ArchCatalog Catalog()
        {
            return ArchTestKit.BuiltInCatalog();
        }

        private static FamilyCardInfo CardOf(string id, CompanyArchState state, bool usable = true)
        {
            ArchCatalog catalog = Catalog();
            return FamilyCardModel.Of(catalog.Get(id), state, usable);
        }

        private static CompanyArchState PlayerWith(params string[] researched)
        {
            var state = new CompanyArchState(1, true);
            foreach (string id in researched)
            {
                FamilyState family = state.GetOrAddFamily(id);
                family.Researched = true;
                family.Progress = 1f;
            }
            return state;
        }

        // ---- the cards ----

        [Test]
        public void TheStartOfTheGame_SevenCards_ProprietaryResearched_AC31()
        {
            ArchCatalog catalog = Catalog();
            List<FamilyCardInfo> cards = FamilyCardModel.All(catalog, PlayerWith(), true);

            Assert.Equal(7, cards.Count, "one card per family");
            Assert.Equal(string.Join(",", ArchTestKit.Ids(catalog.Families)), string.Join(",", cards.ConvertAll(c => c.FamilyId).ToArray()), "in the order of the catalog");

            Assert.Equal(CardStatus.Researched, cards[0].Status);
            Assert.False(cards[0].ActionEnabled, "nothing to press on a researched family");
            Assert.Equal(100, cards[0].ProgressPercent);
        }

        [Test]
        public void AvailableAndLocked_FollowTheRequirements()
        {
            CompanyArchState state = PlayerWith();

            FamilyCardInfo x86 = CardOf("x86", state);
            Assert.Equal(CardStatus.Available, x86.Status);
            Assert.Equal(CardAction.Research, x86.Action);
            Assert.True(x86.ActionEnabled);
            Assert.True(x86.ShowsResearchNumbers);

            FamilyCardInfo x8664 = CardOf("x86_64", state);
            Assert.Equal(CardStatus.Locked, x8664.Status, "x86-64 needs x86");
            Assert.False(x8664.ActionEnabled, "a locked family cannot be started");
            Assert.Equal("x86", string.Join(",", x8664.Missing.ToArray()));

            Assert.Equal("arm", string.Join(",", CardOf("arm64", state).Missing.ToArray()));

            // With x86 researched the lock is gone.
            Assert.Equal(CardStatus.Available, CardOf("x86_64", PlayerWith("x86")).Status);
            Assert.Equal(0, CardOf("x86_64", PlayerWith("x86")).Missing.Count);
        }

        [Test]
        public void AResearchedFamily_IsDoneAndItsButtonIsOff()
        {
            FamilyCardInfo card = CardOf("x86", PlayerWith("x86"));
            Assert.Equal(CardStatus.Researched, card.Status);
            Assert.False(card.ActionEnabled);
            Assert.Equal(100, card.ProgressPercent);
            Assert.False(card.ShowsResearchNumbers);
        }

        [Test]
        public void TheRunningResearch_ShowsItsPercent_AndItsButtonIsOff()
        {
            CompanyArchState state = PlayerWith();
            state.CurrentResearch = "x86";
            state.GetOrAddFamily("x86").Progress = 0.437f;

            FamilyCardInfo card = CardOf("x86", state);
            Assert.Equal(CardStatus.InProgress, card.Status);
            Assert.Equal(43, card.ProgressPercent, "rounded down");
            Assert.False(card.ActionEnabled, "it is already researched in the lab");
            Assert.Equal(CardStatus.Available, CardOf("risc", state).Status, "the others can be started (the lab is taken over)");
        }

        [Test]
        public void APausedResearch_OffersResume_WithTheReason()
        {
            CompanyArchState state = PlayerWith();
            state.CurrentResearch = "x86";
            state.Paused = true;
            state.PauseReason = PauseReason.TechnologyChosen;
            state.GetOrAddFamily("x86").Progress = 0.6f;

            FamilyCardInfo card = CardOf("x86", state);
            Assert.Equal(CardStatus.Paused, card.Status);
            Assert.Equal(CardAction.Resume, card.Action);
            Assert.True(card.ActionEnabled);
            Assert.Equal(PauseReason.TechnologyChosen, card.PauseReason);
            Assert.Equal(60, card.ProgressPercent);

            Assert.False(CardOf("x86", state, usable: false).ActionEnabled, "the research part does not work");
        }

        [Test]
        public void SwitchingToAnotherFamily_KeepsTheProgressOfTheFirst_AC22()
        {
            CompanyArchState state = PlayerWith();
            state.GetOrAddFamily("x86").Progress = 0.5f;
            state.CurrentResearch = "risc";

            FamilyCardInfo x86 = CardOf("x86", state);
            Assert.Equal(CardStatus.Available, x86.Status, "not the current research any more");
            Assert.Equal(50, x86.ProgressPercent, "but the progress is on the card");
            Assert.True(x86.ActionEnabled);
        }

        [Test]
        public void WhenTheResearchPartDoesNotWork_NoButtonIsEnabled()
        {
            foreach (FamilyCardInfo card in FamilyCardModel.All(Catalog(), PlayerWith(), false))
            {
                Assert.False(card.ActionEnabled, card.FamilyId);
            }
        }

        [Test]
        public void ANullState_IsTheStartOfTheGame()
        {
            Assert.Equal(CardStatus.Researched, CardOf("proprietary", null).Status);
            Assert.Equal(CardStatus.Available, CardOf("x86", null).Status);
            Assert.Equal(CardStatus.Locked, CardOf("x86_64", null).Status);
            Assert.Equal(0, FamilyCardModel.All(null, null, true).Count);
            Assert.Throws<System.ArgumentNullException>(() => FamilyCardModel.Of(null, null, true));
        }

        [Test]
        public void Percent_RoundsDown_AndToleratesBadNumbers()
        {
            Assert.Equal(0, FamilyCardModel.Percent(0f));
            Assert.Equal(43, FamilyCardModel.Percent(0.435f));
            Assert.Equal(99, FamilyCardModel.Percent(0.999f));
            Assert.Equal(100, FamilyCardModel.Percent(1f));
            Assert.Equal(100, FamilyCardModel.Percent(7f));
            Assert.Equal(0, FamilyCardModel.Percent(-0.2f));
            Assert.Equal(0, FamilyCardModel.Percent(float.NaN));
            Assert.Equal(0, FamilyCardModel.Percent(float.PositiveInfinity));
        }

        // ---- the counter of the tab ----

        [Test]
        public void Newcomers_AreTheFamiliesWhoseYearHasCome_AndWhoseRequirementsAreResearched()
        {
            ArchCatalog catalog = Catalog();
            CompanyArchState state = PlayerWith();

            Assert.Equal(0, FamilyCardModel.Newcomers(catalog, state, 1977).Count);
            Assert.Equal("x86", string.Join(",", FamilyCardModel.Newcomers(catalog, state, 1978).ToArray()));
            Assert.Equal("x86,risc,arm", string.Join(",", FamilyCardModel.Newcomers(catalog, state, 1990).ToArray()));
            Assert.Equal("x86,risc,arm", string.Join(",", FamilyCardModel.Newcomers(catalog, state, 2010).ToArray()), "x86-64 needs x86, ARM64 needs ARM, RISC-V is from 2014");

            CompanyArchState later = PlayerWith("x86", "arm");
            Assert.Equal("risc,x86_64,arm64", string.Join(",", FamilyCardModel.Newcomers(catalog, later, 2012).ToArray()));
            Assert.Equal(0, FamilyCardModel.Newcomers(null, state, 2000).Count);
        }

        [Test]
        public void Tracker_TheFirstSetIsTheBaseline_ANewFamilyCountsUntilTheTabIsLookedAt()
        {
            var tracker = new NewFamilyTracker();

            Assert.Equal(0, tracker.Update(new[] { "x86", "arm" }, false), "what was available at the start is not news");
            Assert.Equal(0, tracker.Update(new[] { "x86", "arm" }, false));
            Assert.Equal(1, tracker.Update(new[] { "x86", "arm", "risc" }, false), "risc appeared");
            Assert.Equal(1, tracker.Count);
            Assert.Equal(2, tracker.Update(new[] { "x86", "arm", "risc", "x86_64" }, false));

            tracker.MarkSeen();
            Assert.Equal(0, tracker.Count, "FR-3 item 7: the counter goes when the tab was looked at");
            Assert.Equal(0, tracker.Update(new[] { "x86", "arm", "risc", "x86_64" }, false), "and stays gone");
            Assert.Equal(1, tracker.Update(new[] { "x86", "arm", "risc", "x86_64", "riscv" }, false));
        }

        [Test]
        public void Tracker_WhileTheTabIsOnTheScreen_NothingCounts()
        {
            var tracker = new NewFamilyTracker();
            tracker.Update(new[] { "x86" }, false);
            Assert.Equal(0, tracker.Update(new[] { "x86", "risc" }, true), "seen as it appeared");
            Assert.Equal(0, tracker.Update(new[] { "x86", "risc" }, false));
        }

        [Test]
        public void Tracker_AFamilyThatIsResearched_NoLongerCounts()
        {
            var tracker = new NewFamilyTracker();
            tracker.Update(new string[0], false);
            Assert.Equal(2, tracker.Update(new[] { "x86", "arm" }, false));
            Assert.Equal(1, tracker.Update(new[] { "arm" }, false), "x86 was researched: it is gone from the list");
            Assert.Equal(0, tracker.Update(null, false));
        }

        [Test]
        public void Tracker_Reset_StartsTheBaselineOver()
        {
            var tracker = new NewFamilyTracker();
            tracker.Update(new string[0], false);
            Assert.Equal(1, tracker.Update(new[] { "x86" }, false));

            tracker.Reset();
            Assert.Equal(0, tracker.Count);
            Assert.Equal(0, tracker.Update(new[] { "x86" }, false), "a new game: the first set is the baseline again");
        }

        [Test]
        public void Tracker_MarkSeenBeforeAnyUpdate_DoesNotSpoilTheBaseline()
        {
            var tracker = new NewFamilyTracker();
            tracker.MarkSeen();
            Assert.Equal(0, tracker.Update(new[] { "x86", "arm" }, false), "still the baseline");
        }

        // ---- the lab block ----

        [Test]
        public void TheStatusLine_NamesWhatTheLabIsBusyWith()
        {
            Assert.Equal("arch.lab.free", LabBlockModel.StatusKey(LabStatus.Free));
            Assert.Equal("arch.lab.tech", LabBlockModel.StatusKey(LabStatus.Technology));
            Assert.Equal("arch.lab.arch", LabBlockModel.StatusKey(LabStatus.Architecture));
            Assert.Equal("arch.lab.paused", LabBlockModel.StatusKey(LabStatus.ArchitecturePaused));
        }

        [Test]
        public void TheCurrentResearchWidgets_AreThereOnlyForAnArchitecture()
        {
            Assert.False(LabBlockModel.HasArchitecture(LabStatus.Free));
            Assert.False(LabBlockModel.HasArchitecture(LabStatus.Technology));
            Assert.True(LabBlockModel.HasArchitecture(LabStatus.Architecture));
            Assert.True(LabBlockModel.HasArchitecture(LabStatus.ArchitecturePaused));
        }

        [Test]
        public void StopAndResume_WorkInTheirOwnStates()
        {
            Assert.True(LabBlockModel.CanStop(LabStatus.Architecture, true));
            Assert.True(LabBlockModel.CanStop(LabStatus.ArchitecturePaused, true), "a paused research can be dropped");
            Assert.False(LabBlockModel.CanStop(LabStatus.Technology, true));
            Assert.False(LabBlockModel.CanStop(LabStatus.Free, true));
            Assert.False(LabBlockModel.CanStop(LabStatus.Architecture, false), "the research part does not work");

            Assert.True(LabBlockModel.CanResume(LabStatus.ArchitecturePaused, true));
            Assert.False(LabBlockModel.CanResume(LabStatus.Architecture, true), "it is running");
            Assert.False(LabBlockModel.CanResume(LabStatus.ArchitecturePaused, false));
        }

        [Test]
        public void ThePauseReasons_HaveATextEach()
        {
            Assert.Equal("arch.pause.technology", LabBlockModel.PauseKey(PauseReason.TechnologyChosen));
            Assert.Equal("arch.pause.default", LabBlockModel.PauseKey(PauseReason.Default));
            Assert.Equal("arch.pause.noFunds", LabBlockModel.PauseKey(PauseReason.NoFunds));
            Assert.Equal("arch.pause.disabled", LabBlockModel.PauseKey(PauseReason.FeatureDisabled));
            Assert.Null(LabBlockModel.PauseKey(PauseReason.None));
        }

        [Test]
        public void TheSlider_CountsWholePercent_FromOneToHundred_ToFiveHundredWithInnovation()
        {
            Assert.Equal(100, LabBlockModel.MaxPercent(false));
            Assert.Equal(500, LabBlockModel.MaxPercent(true));

            Assert.Equal(35, LabBlockModel.FundingToPercent(0.35f, false));
            Assert.Equal(1, LabBlockModel.FundingToPercent(0.001f, false), "never below 1 %");
            Assert.Equal(1, LabBlockModel.FundingToPercent(0f, true));
            Assert.Equal(100, LabBlockModel.FundingToPercent(1f, false));
            Assert.Equal(100, LabBlockModel.FundingToPercent(2.5f, false), "above 100 % only with the innovation effort");
            Assert.Equal(250, LabBlockModel.FundingToPercent(2.5f, true));
            Assert.Equal(500, LabBlockModel.FundingToPercent(9f, true));
            Assert.Equal(100, LabBlockModel.FundingToPercent(float.NaN, false));
            Assert.Equal(100, LabBlockModel.FundingToPercent(float.PositiveInfinity, true));
        }

        [Test]
        public void TheSlider_WritesAFractionTheGameUnderstands()
        {
            Assert.Near(0.5, LabBlockModel.PercentToFunding(50f, false), 1e-6);
            Assert.Near(1.0, LabBlockModel.PercentToFunding(100f, false), 1e-6);
            Assert.Near(1.0, LabBlockModel.PercentToFunding(150f, false), 1e-6, "the limit of the normal slider");
            Assert.Near(1.5, LabBlockModel.PercentToFunding(150f, true), 1e-6);
            Assert.Near(5.0, LabBlockModel.PercentToFunding(900f, true), 1e-6);
            Assert.Near(0.01, LabBlockModel.PercentToFunding(0f, false), 1e-6, "1 % is the lowest");
        }

        [Test]
        public void TheSlider_AndTheGame_RoundTrip()
        {
            for (int percent = 1; percent <= 500; percent++)
            {
                float funding = LabBlockModel.PercentToFunding(percent, true);
                Assert.Equal(percent, LabBlockModel.FundingToPercent(funding, true), "percent " + percent);
            }
        }
    }
}
