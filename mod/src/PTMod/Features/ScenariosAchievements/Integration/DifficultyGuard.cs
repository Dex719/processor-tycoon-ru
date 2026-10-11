using System;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using GamePlayer = ProcessorTycoon.Player;
using GameplaySettingsWindow = ProcessorTycoon.Desktop.GameplaySettings.GameplaySettingsWindow;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The prefix on <c>GameplaySettingsWindow.ChangeDifficulty</c> (TSK-018, D14, E16; FR-15 item 3): while a scenario is going its difficulty cannot be
    /// lowered. When the player picks a lower level in the settings window, the list and <c>Player.DifficultyLevel</c> go back to the present level, the
    /// player is told (<c>scn.difficulty.locked</c>) and the original method does not run; raising the difficulty, free play and a scenario that is over (the game
    /// goes on as free play) are not touched, and no assisted flag is set - the change is simply refused. The decision is <see cref="DifficultyRule"/>.
    /// <para>
    /// The private field <c>difficultySettings</c> of the window comes in by Harmony's injection (<c>___difficultySettings</c>). The patch is <c>Optional</c>: when
    /// the game has no such method or field the feature stays available and the difficulty is just not guarded (the self-check says so). While a game is loading
    /// or before the game is ready the window sets its list by itself, which is no choice of the player, so the original runs. Putting the list back raises the
    /// change again, now to the present level, which the rule allows.
    /// </para>
    /// </summary>
    internal static class DifficultyGuard
    {
        private const string Site = "DifficultyGuard.ChangeDifficulty";

        [ModPatch(typeof(GameplaySettingsWindow), "ChangeDifficulty", PatchKind.Prefix, ArgTypes = new Type[0], Optional = true)]
        private static bool Prefix(DropdownUI ___difficultySettings)
        {
            bool original = true;
            ModHooks.Guard(ScenariosFeature.FeatureId, Site, () => { original = Allows(___difficultySettings); });
            return original;
        }

        /// <summary>True lets the original method run; false means the change was refused and undone.</summary>
        private static bool Allows(DropdownUI dropdown)
        {
            if (dropdown == null || ModTicks.IsLoading || !ModTicks.IsGameReady) return true;

            GamePlayer player = GamePlayer.Instance;
            if (player == null) return true;

            RunState run = RunSaveParticipant.Instance.Run;
            bool scenarioActive = run != null && run.IsScenarioActive;
            int current = player.DifficultyLevel;
            if (DifficultyRule.Decide(scenarioActive, dropdown.Value, current) == DifficultyAction.Allow) return true;

            dropdown.SetSelected(current);
            player.DifficultyLevel = current;
            ModNotify.ShowKey(ScenariosFeature.FeatureId, "scn.difficulty.locked");
            ModLog.Info(ScenariosFeature.FeatureId, "difficulty: lowering it below " + current + " is refused while the scenario '" + run.ScenarioId + "' is going");
            return false;
        }
    }
}
