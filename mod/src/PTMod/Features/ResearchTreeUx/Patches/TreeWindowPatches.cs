using System;
using PTMod.Core;
using GameTreeWindow = ProcessorTycoon.ResearchSystem.ResearchTreeWindow;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The patches of the feature on the research tree window (design: "Патчи Harmony"). The single patch of the project on these methods
    /// (NFR-7): other features reach the window through <c>ResearchTreeUxApi</c>. Nothing here changes a formula or a value of the game.
    /// <list type="bullet">
    /// <item><b><c>ResearchTreeWindow.InitializeUI</c>, postfix.</b> The game builds the buttons and the lines once per scene (0.2 s after
    /// <c>Start</c>); the postfix tells the feature, which takes the picture of the tree (<see cref="TreeIndex"/>) and builds its UI on it.</item>
    /// <item><b><c>ResearchTreeWindow.UpdateButtonStates</c>, postfix.</b> The game calls it every day, when a research is chosen or stopped and
    /// when the theme changes; the postfix redraws the marks "new" and the counters of the tabs (FR-3) while the window is open. It leaves at
    /// the first line during the fast-forward of a new game (NFR-1) and when the feature is not enabled; on a shut window it does no more than
    /// that check and a look at the window.</item>
    /// </list>
    /// Every patch is <c>Optional</c>: when the target is gone the feature still starts, and the self-check switches off the parts that need
    /// it (NFR-2). The body is wrapped in <c>ModHooks.Guard</c> (<c>UpdateButtonStates</c>, a call of every day, in the same try/catch without a
    /// delegate), so an exception is counted against the feature and never reaches the game; the first line leaves at once when the feature
    /// is not enabled.
    /// </summary>
    internal static class TreeWindowPatches
    {
        private const string InitializeSite = "ResearchTreeWindow.InitializeUI/postfix";
        private const string UpdateStatesSite = "ResearchTreeWindow.UpdateButtonStates/postfix";

        /// <summary>
        /// The debug flag of <c>ptmod_settings.json</c> that makes the postfix throw (TSK-003: the test that an exception does not reach the
        /// game). Read only in the debug build, see <c>ModSettings.Debug</c>.
        /// </summary>
        internal const string ThrowFlag = "debug.rtuxThrowOnTreeBuilt";

        [ModPatch(typeof(GameTreeWindow), "InitializeUI", PatchKind.Postfix, Optional = true)]
        private static void InitializeUiPostfix(GameTreeWindow __instance)
        {
            if (!ModFeatures.IsActive(ResearchTreeUxFeature.FeatureId)) return;

            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, InitializeSite, () =>
            {
                if (ModSettings.Debug(ThrowFlag)) throw new InvalidOperationException("research-tree-ux: artificial exception (" + ThrowFlag + ")");

                ResearchTreeUxFeature feature = ResearchTreeUxFeature.Instance;
                if (feature != null) feature.OnTreeBuilt(__instance);
            });
        }

        [ModPatch(typeof(GameTreeWindow), "UpdateButtonStates", PatchKind.Postfix, Optional = true)]
        private static void UpdateButtonStatesPostfix(GameTreeWindow __instance)
        {
            // Before anything else: during the fast-forward of a new game the game calls this thousands of times (NFR-1).
            if (ModTicks.IsFastForwarding) return;

            string id = ResearchTreeUxFeature.FeatureId;
            if (!ModFeatures.IsActive(id) || !ModHooks.CanRun(id, UpdateStatesSite)) return;

            try
            {
                ResearchTreeUxFeature feature = ResearchTreeUxFeature.Instance;
                if (feature != null) feature.OnButtonStatesUpdated(__instance);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(id, UpdateStatesSite, e);
            }
        }
    }
}
