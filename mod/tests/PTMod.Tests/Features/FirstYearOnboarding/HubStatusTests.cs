using System.Linq;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// What the page "Tutorial" of the hub shows (TSK-015, FR-13): the mode, the track, "Step N of M: name", and which of the four buttons work, for every
    /// state the tutorial can be in, with the texts in the shipped tables.
    /// </summary>
    [Covers(typeof(HubStatus))]
    public class HubStatusTests
    {
        private static HubStatus Of(CtlRig rig)
        {
            return HubStatus.Of(rig.State, rig.Controller.GetProgress(), rig.Controller.IsOldSave, rig.Catalog);
        }

        private static CtlRig Shipped(string track = "idm")
        {
            return new CtlRig(ShippedContext.Load().BuildCatalog(), track);
        }

        [Test]
        public void ASaveFromBeforeTheMod_ShowsOldSave_AndOnlyRestartWorks()
        {
            CtlRig rig = Shipped();

            HubStatus status = Of(rig);

            Assert.True(status.IsOldSave);
            Assert.Equal("onb.hub.status.oldSave", status.ModeKey, "AC-9.3");
            Assert.Null(status.TrackKey);
            Assert.Equal(0, status.StepNumber);
            Assert.False(status.CanRepeat || status.CanSkipStep || status.CanSkipAll);
            Assert.True(status.CanRestart, "the tutorial can be started in an old save");
        }

        [Test]
        public void AGameThatChoseNoTutorial_ShowsOff_NotAnOldSave()
        {
            CtlRig rig = Shipped();
            rig.Controller.NewGame(false, "menu");

            HubStatus status = Of(rig);

            Assert.False(status.IsOldSave);
            Assert.Equal("onb.hub.mode.off", status.ModeKey);
            Assert.True(status.CanRestart);
            Assert.False(status.CanSkipAll);
        }

        [Test]
        public void ARunningTutorial_ShowsTheTrackAndTheStep_AndAllButtonsOfAStepWork()
        {
            CtlRig rig = Shipped();
            rig.Controller.NewGame(true, "menu");
            rig.Days(2);   // the research mail is out
            Assert.Equal(StepStatus.Current, rig.Status("research"));

            HubStatus status = Of(rig);

            Assert.Equal("onb.hub.mode.active", status.ModeKey);
            Assert.Equal("onb.track.idm", status.TrackKey);
            Assert.Equal("research", status.StepId);
            Assert.Equal(2, status.StepNumber, "FR-13 item 1: Step 2 of 10");
            Assert.Equal(10, status.StepTotal);
            Assert.Equal("onb.research.name", status.StepNameKey);
            Assert.True(status.CanRepeat);
            Assert.True(status.CanSkipStep);
            Assert.True(status.CanSkipAll);
            Assert.True(status.CanRestart);
            Assert.False(status.ContentBroken);
        }

        [Test]
        public void WhileTheNextStepWaitsForItsDay_NoStepIsCurrent_SoNeitherRepeatNorSkipStepWorks()
        {
            CtlRig rig = Shipped();
            rig.Controller.NewGame(true, "menu");   // welcome is out and done; research waits for the next game day

            HubStatus status = Of(rig);

            Assert.Equal("research", status.StepId, "the page shows the step that comes next");
            Assert.Equal(2, status.StepNumber);
            Assert.False(status.CanSkipStep, "no mail, nothing to skip");
            Assert.False(status.CanRepeat);
            Assert.True(status.CanSkipAll);
        }

        [Test]
        public void AFinishedTutorial_ShowsItsMode_WithoutAStep_AndOnlyRestartWorks()
        {
            foreach (var pair in new[]
            {
                new { Mode = OnboardingMode.Skipped, Key = "onb.hub.mode.skipped" },
                new { Mode = OnboardingMode.Completed, Key = "onb.hub.mode.completed" },
                new { Mode = OnboardingMode.Aborted, Key = "onb.hub.mode.aborted" }
            })
            {
                CtlRig rig = Shipped();
                rig.Controller.NewGame(true, "menu");
                rig.Days(2);
                rig.State.Mode = pair.Mode;

                HubStatus status = Of(rig);

                Assert.Equal(pair.Key, status.ModeKey);
                Assert.Equal("onb.track.idm", status.TrackKey, "the track is still shown");
                Assert.Equal(0, status.StepNumber, pair.Mode + ": no step line");
                Assert.Null(status.StepNameKey);
                Assert.False(status.CanRepeat || status.CanSkipStep || status.CanSkipAll, pair.Mode.ToString());
                Assert.True(status.CanRestart, "AC-13.1: a finished tutorial can be restarted");
                Assert.False(status.IsOldSave);
            }
        }

        [Test]
        public void TheFoundryTrack_CountsSevenSteps()
        {
            CtlRig rig = Shipped("foundry");
            rig.Controller.NewGame(true, "menu");

            HubStatus status = Of(rig);

            Assert.Equal("onb.track.foundry", status.TrackKey);
            Assert.Equal(7, status.StepTotal);
        }

        [Test]
        public void BrokenContent_IsShownAsBroken_AndNothingWorks()
        {
            var rig = new CtlRig(StepCatalog.Build(null, null, null));

            HubStatus status = HubStatus.Of(rig.State, rig.Controller.GetProgress(), rig.Controller.IsOldSave, rig.Catalog);

            Assert.True(status.ContentBroken);
            Assert.False(status.CanRestart || status.CanRepeat || status.CanSkipStep || status.CanSkipAll);
            Assert.True(HubStatus.Of(rig.State, null, true, null).ContentBroken, "no catalog at all is broken content too");
        }

        [Test]
        public void ATrackWithoutSteps_AddsTheErrorLine_WhileTheRestOfThePageWorks()
        {
            ShippedContext context = ShippedContext.Load();
            // The foundry is taken out of every step: no step is left for that track, the other two are whole.
            var steps = context.MapSteps();
            foreach (StepDefinition step in steps) step.Tracks = step.Tracks.Where(t => t != "foundry").ToArray();
            steps = steps.Where(s => s.Tracks.Length > 0).ToList();
            StepCatalog catalog = StepCatalog.Build(steps, context.MapConfig(), context);
            Assert.False(catalog.HasTrack("foundry"), "the test catalog is damaged for the foundry");
            Assert.True(catalog.HasTrack("idm") && catalog.HasTrack("fabless"));
            var rig = new CtlRig(catalog, "idm");
            rig.Controller.NewGame(true, "menu");

            HubStatus status = Of(rig);

            Assert.True(status.ContentDamaged, "FR-3 item 3: the page says the content is damaged");
            Assert.False(status.ContentBroken, "it is not broken as a whole");
            Assert.True(status.CanSkipAll && status.CanRestart, "the tutorial of the other tracks works");
            Assert.Equal("onb.hub.mode.active", status.ModeKey);
        }

        [Test]
        public void AWholeCatalog_IsNotDamaged_AndBrokenContentIsBrokenNotDamaged()
        {
            Assert.False(Of(Shipped()).ContentDamaged);

            var broken = new CtlRig(StepCatalog.Build(null, null, null));
            HubStatus status = HubStatus.Of(broken.State, broken.Controller.GetProgress(), broken.Controller.IsOldSave, broken.Catalog);
            Assert.True(status.ContentBroken);
            Assert.False(status.ContentDamaged, "the page shows the one error line of broken content, not two");
        }

        [Test]
        public void ANullProgress_IsNoStep()
        {
            var state = new OnboardingState { Mode = OnboardingMode.Active, Track = "idm" };

            HubStatus status = HubStatus.Of(state, null, false, ShippedContext.Load().BuildCatalog());

            Assert.Equal(0, status.StepNumber);
            Assert.Null(status.StepId);
            Assert.True(status.CanSkipAll);
        }

        [Test]
        public void EveryTextTheHubCanAsk_IsInBothTables()
        {
            ShippedContext context = ShippedContext.Load();
            StepCatalog catalog = context.BuildCatalog();
            var keys = new System.Collections.Generic.List<string>
            {
                "onb.hub.title", "onb.hub.mode", "onb.hub.track", "onb.hub.step", "onb.hub.step.none", "onb.hub.status.oldSave",
                "onb.hub.button.repeat", "onb.hub.button.skipStep", "onb.hub.button.skipAll", "onb.hub.button.restart",
                "onb.hub.restart.title", "onb.hub.restart.body", "onb.error.content",
                "onb.track.idm", "onb.track.fabless", "onb.track.foundry"
            };
            foreach (OnboardingMode mode in System.Enum.GetValues(typeof(OnboardingMode))) keys.Add(HubStatus.ModeKeyOf(mode));
            keys.AddRange(catalog.All.Where(s => s.Kind == StepKind.Sequential).Select(s => s.NameKey));

            foreach (string key in keys)
            {
                Assert.True(context.Tables.Contains("ru", key), "no Russian text for " + key);
                Assert.True(context.Tables.Contains("en", key), "no English text for " + key);
            }
        }

        [Test]
        public void TheModeKeys_AreOnePerMode()
        {
            Assert.Equal("onb.hub.mode.off", HubStatus.ModeKeyOf(OnboardingMode.Off));
            Assert.Equal("onb.hub.mode.active", HubStatus.ModeKeyOf(OnboardingMode.Active));
            Assert.Equal("onb.hub.mode.skipped", HubStatus.ModeKeyOf(OnboardingMode.Skipped));
            Assert.Equal("onb.hub.mode.completed", HubStatus.ModeKeyOf(OnboardingMode.Completed));
            Assert.Equal("onb.hub.mode.aborted", HubStatus.ModeKeyOf(OnboardingMode.Aborted));
        }

        [Test]
        public void ANullState_IsRefused()
        {
            Assert.Throws<System.ArgumentNullException>(() => HubStatus.Of(null, null, false, null));
        }
    }
}
