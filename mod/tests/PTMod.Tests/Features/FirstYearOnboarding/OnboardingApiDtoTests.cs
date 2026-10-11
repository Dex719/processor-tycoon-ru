using PTMod.Features.FirstYearOnboarding;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The plain data types of the public API of the tutorial (<c>Api/Dto</c>), compiled by the test builds together with <c>Logic</c>: the numbers of the
    /// modes (they are written to the save section and compared with the twin enum of <c>Logic</c>), the status snapshot and the result of a run.
    /// </summary>
    [Covers(typeof(OnboardingStatus), typeof(OnboardingMode), typeof(OnboardingResult))]
    public class OnboardingApiDtoTests
    {
        [Test]
        public void OnboardingMode_KeepsTheNumbersOfTheSpec()
        {
            Assert.Equal(0, (int)OnboardingMode.Off);
            Assert.Equal(1, (int)OnboardingMode.Active);
            Assert.Equal(2, (int)OnboardingMode.Skipped);
            Assert.Equal(3, (int)OnboardingMode.Completed);
            Assert.Equal(4, (int)OnboardingMode.Aborted);
        }

        [Test]
        public void Status_Off_IsTheStatusOfAGameWithoutATutorial()
        {
            OnboardingStatus off = OnboardingStatus.Off;

            Assert.Equal(OnboardingMode.Off, off.Mode);
            Assert.False(off.IsActive);
            Assert.Null(off.Track);
            Assert.Null(off.CurrentStepId);
            Assert.Equal(0, off.CurrentStepIndex);
            Assert.Equal(0, off.TotalSteps);
            Assert.Equal("mode=Off, track=-, step=0/0, stepId=-", off.ToString());
        }

        [Test]
        public void Status_IsActive_OnlyForTheRunningTutorial_AndNegativeCountersBecomeZero()
        {
            var running = new OnboardingStatus(OnboardingMode.Active, "idm", 3, 10, "research");
            Assert.True(running.IsActive);
            Assert.Equal("idm", running.Track);
            Assert.Equal(3, running.CurrentStepIndex);
            Assert.Equal(10, running.TotalSteps);
            Assert.Equal("research", running.CurrentStepId);
            Assert.Equal("mode=Active, track=idm, step=3/10, stepId=research", running.ToString());

            foreach (OnboardingMode mode in new[] { OnboardingMode.Off, OnboardingMode.Skipped, OnboardingMode.Completed, OnboardingMode.Aborted })
                Assert.False(new OnboardingStatus(mode, "idm", 1, 10, "x").IsActive, mode + " is not running");

            var clamped = new OnboardingStatus(OnboardingMode.Active, "fabless", -4, -1, null);
            Assert.Equal(0, clamped.CurrentStepIndex);
            Assert.Equal(0, clamped.TotalSteps);
        }

        [Test]
        public void Result_IsClean_UnlessTheWholeTutorialWasSkippedOrCutShort()
        {
            Assert.True(new OnboardingResult { Track = "idm", CompletedSteps = 9 }.IsClean, "a finished run");
            Assert.True(new OnboardingResult { Track = "idm", CompletedSteps = 5, SkippedSteps = 4 }.IsClean, "single steps skipped, the tutorial itself was not");
            Assert.False(new OnboardingResult { WasSkipped = true }.IsClean, "the whole tutorial skipped");
            Assert.False(new OnboardingResult { Aborted = true }.IsClean, "cut short by a bankruptcy");
            Assert.False(new OnboardingResult { WasSkipped = true, Aborted = true }.IsClean);
        }

        [Test]
        public void Result_ToString_IsOneLineForTheLog()
        {
            var result = new OnboardingResult { Track = "foundry", CompletedSteps = 7, SkippedSteps = 1, WasSkipped = false, Aborted = true };
            Assert.Equal("track=foundry, completed=7, skipped=1, wasSkipped=False, aborted=True", result.ToString());
            Assert.Equal("track=-, completed=0, skipped=0, wasSkipped=False, aborted=False", new OnboardingResult().ToString());
        }
    }
}
