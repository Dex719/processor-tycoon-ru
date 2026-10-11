namespace PTMod.Features.CpuArchitectures
{
    /// <summary>What switching the feature off did to the player's research (<see cref="LabSwitch.Disable"/>).</summary>
    public sealed class DisableOutcome
    {
        /// <summary>The research of the architecture was put on pause (it was running, or paused in a way that would lift by itself).</summary>
        public bool Paused;

        /// <summary>The lab is empty and a technology was displaced from it: the lab goes back to that technology (the game says whether it still can).</summary>
        public bool GiveBack;
    }

    /// <summary>
    /// The state side of switching the feature off in the middle of a game (FR-9 items 2 and 3, AC-9.1, AC-9.2), pure: the research of the PLAYER's
    /// architecture goes on pause with <see cref="PauseReason.FeatureDisabled"/> - progress, support levels and the family states stay as they are, nothing
    /// is deleted - and the lab goes back to the technology that was displaced. The AI companies are left alone: their state is not touched, and the
    /// arbiter settles what they researched meanwhile when the feature is switched on again. Switching on does not restart the research: a pause of
    /// this kind waits for the button "Продолжить" (<see cref="LabRules.AutoResumes"/> is false for it).
    /// </summary>
    public static class LabSwitch
    {
        /// <summary>
        /// Puts the player's research on pause for the switch-off. A research that is paused because of a default would lift by itself when the
        /// feature is on again, so its reason becomes <see cref="PauseReason.FeatureDisabled"/> as well; a pause that waits for the player anyway
        /// (<see cref="PauseReason.TechnologyChosen"/>, <see cref="PauseReason.NoFunds"/>) keeps its reason. An AI company's state is not touched.
        /// </summary>
        /// <param name="state">The state of the company.</param>
        /// <param name="slotHasTechnology"><c>ResearchSector.CurrentResearch()</c> is not null (a technology of the player's own choice stays in the lab).</param>
        public static DisableOutcome Disable(CompanyArchState state, bool slotHasTechnology)
        {
            var outcome = new DisableOutcome();
            if (state == null || !state.IsPlayer) return outcome;

            if (state.CurrentResearch != null)
            {
                if (!state.Paused)
                {
                    state.Paused = true;
                    state.PauseReason = PauseReason.FeatureDisabled;
                    outcome.Paused = true;
                }
                else if (LabRules.AutoResumes(state.PauseReason))
                {
                    state.PauseReason = PauseReason.FeatureDisabled;
                    outcome.Paused = true;
                }
            }

            outcome.GiveBack = state.ResumeTechnologyId >= 0 && !slotHasTechnology;
            return outcome;
        }
    }
}
