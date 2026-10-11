namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>What the prefix on <c>GameplaySettingsWindow.ChangeDifficulty</c> does with a change of the difficulty.</summary>
    public enum DifficultyAction
    {
        /// <summary>The original method runs: the change goes through.</summary>
        Allow = 0,

        /// <summary>The change is undone: the list and <c>Player.DifficultyLevel</c> go back to the current level, the player is told.</summary>
        Revert = 1,
    }

    /// <summary>
    /// The decision of <c>DifficultyGuard</c> (TSK-018, FR-15 item 3, D14, E16) as a function of its arguments, so it is tested without the game:
    /// while a scenario is going the difficulty cannot be lowered. A rise is allowed, so are free play and a scenario that is over (the game goes on
    /// as free play, D13). The Impossible level (5) is the game's own business: the original method returns at once for it and the list of the
    /// window has no such entry. Lowering the difficulty is no reason for the assisted flag (D9): the change is simply refused.
    /// </summary>
    public static class DifficultyRule
    {
        /// <summary>The Impossible difficulty; the window of the settings does not change it.</summary>
        public const int ImpossibleLevel = 5;

        /// <param name="scenarioActive">A scenario is going (the run has one and it is neither won nor lost).</param>
        /// <param name="selected">The value of the list of the window now (what the player picked).</param>
        /// <param name="current"><c>Player.Instance.DifficultyLevel</c>, the level of the game now.</param>
        public static DifficultyAction Decide(bool scenarioActive, int selected, int current)
        {
            if (!scenarioActive) return DifficultyAction.Allow;
            if (current == ImpossibleLevel) return DifficultyAction.Allow;
            return selected < current ? DifficultyAction.Revert : DifficultyAction.Allow;
        }
    }
}
