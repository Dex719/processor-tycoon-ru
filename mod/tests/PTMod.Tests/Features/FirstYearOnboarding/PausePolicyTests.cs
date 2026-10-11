using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The rule of the pause of time on a new step (TSK-012, FR-12): the step asks for it, the player's setting allows it, and the time is running.
    /// </summary>
    [Covers(typeof(PausePolicy))]
    public class PausePolicyTests
    {
        [Test]
        public void Pauses_OnlyWhenTheStepAsks_TheSettingIsOn_AndTheTimeRuns()
        {
            Assert.True(PausePolicy.ShouldPause(true, true, true), "AC-12.1: all three");
        }

        [Test]
        public void DoesNotPause_WhenTheStepDoesNotAsk()
        {
            Assert.False(PausePolicy.ShouldPause(false, true, true));
        }

        [Test]
        public void DoesNotPause_WhenTheSettingIsOff()
        {
            Assert.False(PausePolicy.ShouldPause(true, false, true), "FR-12 item 3");
        }

        [Test]
        public void DoesNotPause_WhenTheTimeIsPausedAlready()
        {
            Assert.False(PausePolicy.ShouldPause(true, true, false), "FR-12 item 2: the speed is not changed");
        }

        [Test]
        public void AllEightCombinations_PauseOnlyForTheOneWithEveryFact()
        {
            int pauses = 0;
            foreach (bool onShow in new[] { false, true })
            {
                foreach (bool setting in new[] { false, true })
                {
                    foreach (bool running in new[] { false, true })
                    {
                        bool pause = PausePolicy.ShouldPause(onShow, setting, running);
                        if (pause) pauses++;
                        Assert.Equal(onShow && setting && running, pause, "pauseOnShow=" + onShow + ", setting=" + setting + ", running=" + running);
                    }
                }
            }
            Assert.Equal(1, pauses);
        }

        [Test]
        public void ForAStep_UsesItsPauseOnShow()
        {
            StepDefinition asks = OnbSteps.Info("welcome", 10);
            asks.PauseOnShow = true;
            StepDefinition silent = OnbSteps.Info("quiet", 20);
            silent.PauseOnShow = false;

            Assert.True(PausePolicy.ShouldPause(asks, true, true));
            Assert.False(PausePolicy.ShouldPause(silent, true, true));
            Assert.False(PausePolicy.ShouldPause(asks, false, true));
            Assert.False(PausePolicy.ShouldPause(asks, true, false));
        }

        [Test]
        public void ForAMissingStep_NeverPauses()
        {
            Assert.False(PausePolicy.ShouldPause((StepDefinition)null, true, true));
        }
    }
}
