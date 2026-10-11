using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using PlayerInitialData = ProcessorTycoon.InitialData.PlayerInitialData;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The "Tutorial" toggle and the line "Set by the scenario: ..." in the new game window (TSK-013, FR-1), with no patch on the window or on the start of
    /// a game: both live on the panel "Mods..." that the core draws (<c>ModNewGame</c>).
    /// <list type="bullet">
    /// <item><b>The toggle</b> is the option <c>first-year-onboarding.tutorial</c> (<c>ModNewGame.RegisterOption</c>, a switch). It starts on until the profile
    /// has seen the tutorial, afterwards as the player left it (<see cref="OnboardingProfile.ToggleStartsOn"/>, read once here: the core reads the profile before
    /// any feature registers). The core shows it only while the feature is enabled, and holds the player's choice while the panel is open.</item>
    /// <item><b>The section</b> (order 20, after the scenarios) shows "Set by the scenario: tutorial on/off" while another feature asked for a mode with
    /// <see cref="OnboardingService.SetPendingMode"/>, and follows <see cref="OnboardingService.PendingModeChanged"/>; with no request it is empty.</item>
    /// <item><b>The start.</b> <c>ModNewGame.BeforeStart</c> records the toggle in the profile (<c>defaultToggle</c>, the next toggle starts there) and in
    /// <see cref="PendingStart"/>; <c>PlayerInitialData</c> is not touched. The final decision (a forced mode wins) is taken when the game is ready, in the director.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class OnboardingNewGame
    {
        /// <summary>The id of the option inside the feature.</summary>
        internal const string OptionId = "tutorial";

        /// <summary>The place of the section on the panel (10 is the scenarios', 20 the tutorial's).</summary>
        internal const int SectionOrder = 20;

        private static bool _registered;
        private static TMP_Text _forcedLine;

        /// <summary>Puts the option and the section on the panel and listens to the start (once per session).</summary>
        internal static void Register()
        {
            if (_registered) return;
            _registered = true;

            ModNewGame.RegisterOption(new NewGameOption
            {
                FeatureId = OnboardingFeature.FeatureId,
                Id = OptionId,
                LabelKey = "onb.toggle.label",
                TooltipKey = "onb.toggle.tooltip",
                Kind = NewGameOptionKind.Toggle,
                DefaultOn = OnboardingProfile.ToggleStartsOn
            });
            ModNewGame.RegisterSection(OnboardingFeature.FeatureId, BuildForcedModeLine, SectionOrder);
            ModNewGame.BeforeStart += OnBeforeStart;
            OnboardingService.PendingModeChanged += OnPendingModeChanged;
        }

        // ---- the start ----

        /// <summary>The player pressed "Start": the toggle goes to the profile and to <see cref="PendingStart"/>. A failure leaves the tutorial off.</summary>
        private static void OnBeforeStart(PlayerInitialData data)
        {
            try
            {
                bool on = ModNewGame.GetToggle(OnboardingFeature.FeatureId, OptionId);
                OnboardingProfile.DefaultToggle = on;
                PendingStart.Set(on);
                ModLog.Debug(OnboardingFeature.FeatureId, "new game: the tutorial toggle is " + (on ? "on" : "off"));
            }
            catch (Exception ex)
            {
                PendingStart.Clear();
                ModHooks.ReportFault(OnboardingFeature.FeatureId, "OnboardingNewGame.OnBeforeStart", ex);
            }
        }

        // ---- the section ----

        /// <summary>Fills the container of the section: one line of text, hidden while no other feature asked for a mode.</summary>
        private static void BuildForcedModeLine(RectTransform container)
        {
            _forcedLine = null;
            TMP_Text line = ModUI.Text(container, string.Empty);
            if (line == null) return;   // the template is missing: the core has said so, the toggle works without the line

            _forcedLine = line;
            RefreshForcedLine();
        }

        private static void OnPendingModeChanged()
        {
            try
            {
                RefreshForcedLine();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, "OnboardingNewGame.OnPendingModeChanged", ex);
            }
        }

        private static void RefreshForcedLine()
        {
            TMP_Text line = _forcedLine;
            if (line == null) return;   // never built, or destroyed with a panel that was drawn again

            PendingMode mode = OnboardingService.GetPendingMode();
            if (mode == PendingMode.Allowed)
            {
                line.gameObject.SetActive(false);
                return;
            }

            string state = ModL10n.L(mode == PendingMode.ForcedOn ? "onb.toggle.forced.on" : "onb.toggle.forced.off");
            line.text = ModL10n.L("onb.toggle.forced", state);
            line.gameObject.SetActive(true);
        }
    }
}
