namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// When the game is put on pause because the mail of a new step arrived (FR-12): only if the step asks for it (<c>pauseOnShow</c>), the player's
    /// setting "Pause on a new step" is on, and the time is running. A game that is paused already is left alone: pausing it again would make the game
    /// remember the pause button as the speed to go back to, and the "play" button would then resume nothing. Pure function; the game layer supplies
    /// the three facts and calls <c>DateController.ManualPause</c> on a true answer.
    /// </summary>
    public static class PausePolicy
    {
        /// <summary>True when the game has to be paused now.</summary>
        /// <param name="pauseOnShow">The step wants a pause when its mail arrives (<see cref="StepDefinition.PauseOnShow"/>).</param>
        /// <param name="pauseOnStep">The hub setting "Pause on a new step" (<c>pauseOnStep</c>).</param>
        /// <param name="timeRunning">The game time is running (a speed above 0); false when it is paused or when that cannot be read.</param>
        public static bool ShouldPause(bool pauseOnShow, bool pauseOnStep, bool timeRunning)
        {
            return pauseOnShow && pauseOnStep && timeRunning;
        }

        /// <summary>The same for a step; a null step never pauses.</summary>
        public static bool ShouldPause(StepDefinition step, bool pauseOnStep, bool timeRunning)
        {
            return step != null && ShouldPause(step.PauseOnShow, pauseOnStep, timeRunning);
        }
    }
}
