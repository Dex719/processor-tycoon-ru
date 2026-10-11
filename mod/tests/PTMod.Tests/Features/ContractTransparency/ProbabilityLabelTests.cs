using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-011 (FR-2 item 8): when the field "Probability" of the contract window gets "(actual N %)" after the game's text, and how the mod finds the game's text again.</summary>
    [Covers(typeof(ProbabilityLabel), typeof(ProbabilityContext))]
    public class ProbabilityLabelTests
    {
        private static ProbabilityContext Everything()
        {
            return new ProbabilityContext
            {
                FeatureActive = true,
                SettingOn = true,
                InspectorAvailable = true,
                HasSelection = true,
                CpuFailsRequirements = false,
                WindowShown = true,
            };
        }

        [Test]
        public void WhenEverythingIsInPlace_TheActualProbabilityIsAppended()
        {
            ProbabilityContext context = Everything();

            Assert.True(ProbabilityLabel.NeedsWork(context));
            Assert.True(ProbabilityLabel.ShouldAppend(context, false));
        }

        [Test]
        public void ACpuThatFailsARequirement_GetsNothing_TheGameSaysZeroPercent()
        {
            ProbabilityContext context = Everything();
            context.CpuFailsRequirements = true;

            Assert.False(ProbabilityLabel.NeedsWork(context));
            Assert.False(ProbabilityLabel.ShouldAppend(context, false));
        }

        [Test]
        public void TheSettingTheFeatureTheMembersAndTheSelection_EachSwitchItOff()
        {
            ProbabilityContext off = Everything();
            off.SettingOn = false;
            Assert.False(ProbabilityLabel.ShouldAppend(off, false));

            off = Everything();
            off.FeatureActive = false;
            Assert.False(ProbabilityLabel.ShouldAppend(off, false));

            off = Everything();
            off.InspectorAvailable = false;
            Assert.False(ProbabilityLabel.ShouldAppend(off, false), "a missing private member keeps the vanilla text (FR-12 item 3)");

            off = Everything();
            off.HasSelection = false;
            Assert.False(ProbabilityLabel.ShouldAppend(off, false));
        }

        [Test]
        public void AContractWithoutOffers_NeedsNoSuffix_TheGameShows100AlreadyAndTheAnswerIsTheSame()
        {
            Assert.False(ProbabilityLabel.ShouldAppend(Everything(), true));
        }

        [Test]
        public void AClosedWindow_IsNotWorkedOutAtEveryDayChange()
        {
            ProbabilityContext context = Everything();
            context.WindowShown = false;

            Assert.False(ProbabilityLabel.NeedsWork(context), "NFR-1: a day costs nothing while the window is shut");
            Assert.True(ProbabilityLabel.ShouldAppend(context, false), "the decision itself does not depend on the window");
        }

        [Test]
        public void TheLine_IsTheKeyWithTheGameTextAndTheActualProbability()
        {
            PanelLine line = ProbabilityLabel.Line("40%", 0.211);

            Assert.Equal("ct.odds.actual", line.Key);
            Assert.Equal("ct.odds.actual", ProbabilityLabel.Key);
            Assert.Equal(new[] { "40%", "21" }, line.Format(new PlainArgFormatter()));
        }

        [Test]
        public void GameTextOf_TakesTheFieldAsItIs_UnlessItStillHoldsTheModsOwnText()
        {
            // The game just wrote the field: it is the game's text.
            Assert.Equal("40%", ProbabilityLabel.GameTextOf("40%", "40% (actual 21 %)", "38%"));

            // The game's method returned early and left the mod's text: the remembered game text is the one to decorate again.
            Assert.Equal("38%", ProbabilityLabel.GameTextOf("40% (actual 21 %)", "40% (actual 21 %)", "38%"));

            Assert.Equal(string.Empty, ProbabilityLabel.GameTextOf(null, null, null));
            Assert.Equal("40%", ProbabilityLabel.GameTextOf("40%", null, null));
            Assert.Equal("x", ProbabilityLabel.GameTextOf("x", "x", null), "nothing remembered: the field stays as it is");
        }
    }
}
