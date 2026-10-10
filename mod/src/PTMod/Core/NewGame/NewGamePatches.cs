using System;
using GameManagerType = ProcessorTycoon.GameManager;
using MenuGameSetupType = ProcessorTycoon.Menu.MenuGameSetup;
using PlayerInitialData = ProcessorTycoon.InitialData.PlayerInitialData;

namespace PTMod.Core
{
    /// <summary>
    /// P17: <c>GameManager.StartNewGame</c>, prefix. "Start" in the new-game window ends here: the method closes over the very <c>PlayerInitialData</c> it
    /// was given and hands it to <c>Player.InitializeAsNewGame</c> once the scene is loaded, so what a feature changes in <see cref="ModNewGame.BeforeStart"/>
    /// is what the game starts with. The prefix raises <see cref="ModNewGame.BeforeStart"/>, then <c>ModSave.PrepareNewGame</c> resets the participants and picks
    /// the world seed; it never skips the original. A class of its own, so that if another game version changes this method only this patch is off (the core's
    /// unit of rollback). The method is reserved for the core (<c>ReservedTargets</c>, P17): a feature's patch on it is rejected with the hint to use
    /// <see cref="ModNewGame.BeforeStart"/>. Without the patch the new game still starts, features hear no <c>BeforeStart</c> and <c>ModSave</c> warns that the
    /// game became ready without <c>PrepareNewGame</c>.
    /// </summary>
    internal static class NewGameStartPatches
    {
        private const string Site = "P17/prefix";

        // The first argument by position (__0): the name of the parameter does not matter to the patch.
        [ModPatch(typeof(GameManagerType), "StartNewGame", PatchKind.Prefix, ArgTypes = new[] { typeof(PlayerInitialData) })]
        private static void StartNewGamePrefix(PlayerInitialData __0)
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                ModNewGame.StartingNewGame(__0);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }

    /// <summary>
    /// P18: <c>MenuGameSetup.InitializeUI</c>, postfix. The game calls it 0.1 s after the menu scene starts, once its dropdowns and the "Start" button are
    /// wired; from then on <see cref="NewGameButton"/> may add the button "Mods..." next to the colour button, when an Enabled feature has registered an option or
    /// a section (AC-18.1, AC-18.2). A class of its own (the unit of rollback). The method is reserved for the core (<c>ReservedTargets</c>, P18): a feature's
    /// patch on it is rejected with the hint to use <see cref="ModNewGame.RegisterOption"/> and <see cref="ModNewGame.RegisterSection"/>. Without the patch the
    /// new-game window has no button and the options of the features cannot be reached; the game itself is not affected.
    /// </summary>
    internal static class NewGameButtonPatches
    {
        private const string Site = "P18/postfix";

        [ModPatch(typeof(MenuGameSetupType), "InitializeUI", PatchKind.Postfix)]
        private static void InitializeUiPostfix()
        {
            if (!ModHooks.CanRun(ModHooks.CoreId, Site)) return;
            try
            {
                NewGameButton.SetupInitialized();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(ModHooks.CoreId, Site, ex);
            }
        }
    }
}
