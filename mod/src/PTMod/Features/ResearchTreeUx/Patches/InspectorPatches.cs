using PTMod.Core;
using GameInspectorWindow = ProcessorTycoon.Desktop.Inspector.InspectorWindow;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The patch of the feature on the window of the inspector (design: "Патчи Harmony"): a postfix on <c>InspectorWindow.Start</c> that adds the
    /// button "Compare" beside "Detailed / Simplified" (FR-6 item 1). The only patch of the project on this method (NFR-7). It changes no value of
    /// the game; the button is made of the widgets of <c>ModUI</c> (<see cref="InspectorButton"/>).
    /// <para>
    /// The patch is <c>Optional</c>: when the target is gone the feature still starts and the self-check switches the part
    /// <see cref="UxParts.Compare"/> off (NFR-2). The body is wrapped in <c>ModHooks.Guard</c>, so an exception is counted against the feature and
    /// never reaches the game; it leaves at the first line when the feature is not enabled.
    /// </para>
    /// </summary>
    internal static class InspectorPatches
    {
        private const string StartSite = "InspectorWindow.Start/postfix";

        [ModPatch(typeof(GameInspectorWindow), "Start", PatchKind.Postfix, Optional = true)]
        private static void StartPostfix(GameInspectorWindow __instance)
        {
            if (!ModFeatures.IsActive(ResearchTreeUxFeature.FeatureId)) return;

            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, StartSite, () =>
            {
                ResearchTreeUxFeature feature = ResearchTreeUxFeature.Instance;
                if (feature != null) feature.OnInspectorStarted(__instance);
            });
        }
    }
}
