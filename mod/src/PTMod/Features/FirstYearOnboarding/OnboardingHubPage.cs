using System;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The page "Tutorial" of the mods hub (TSK-015, FR-13; design.md, "OnboardingHubPage"), registered with <c>FeatureContext.HubPage</c>. It shows the mode,
    /// the track and "Step N of M: name" (<see cref="HubStatus"/> decides what is shown and which buttons work), and has the four buttons: "Repeat the mail of
    /// the step", "Skip the step", "Skip the tutorial" and "Restart the tutorial". Restarting asks first (<c>ModUI.Confirm</c>, the time stands while the question
    /// is open). The two settings ("Pause on a new step", "Reminder hints") are <c>ParamSpec</c>s: the core draws their switches on the same page and applies
    /// them at once. A save made before the mod shows "Not started (old save)"; broken content shows <c>onb.error.content</c> and nothing else; a track
    /// without steps (the tutorial is off for it) adds that line above the usual status.
    /// The core calls <see cref="Build"/> every time the page is opened, with an empty container, while the feature is on. Main thread only.
    /// </summary>
    internal static class OnboardingHubPage
    {
        /// <summary>The key of the heading of the page (also the title of the settings page the core builds).</summary>
        internal const string TitleKey = "onb.hub.title";

        /// <summary>Fills the page.</summary>
        internal static void Build(RectTransform container)
        {
            OnboardingDirector director = OnboardingFeature.Director;
            if (director == null || OnboardingFeature.ContentBroken)
            {
                // The content is unusable or the feature did not start: one line says so, there is nothing to press.
                ModUI.Text(container, ModL10n.L("onb.error.content"));
                return;
            }

            new View(director).Build(container);
        }

        /// <summary>The widgets of one opening of the page and the redrawing of them after a button.</summary>
        private sealed class View
        {
            private readonly OnboardingDirector _director;
            private InfoText _mode;
            private InfoText _track;
            private TMP_Text _step;
            private ButtonUI _repeat;
            private ButtonUI _skipStep;
            private ButtonUI _skipAll;
            private ButtonUI _restart;

            internal View(OnboardingDirector director)
            {
                _director = director;
            }

            internal void Build(RectTransform container)
            {
                // A track without steps is off for the tutorial (FR-3 item 3): the page keeps working for the other tracks and says what is wrong.
                if (CurrentStatus().ContentDamaged) ModUI.Text(container, ModL10n.L("onb.error.content"));

                _mode = ModUI.Info(container, ModL10n.L("onb.hub.mode"), string.Empty);
                _track = ModUI.Info(container, ModL10n.L("onb.hub.track"), string.Empty);
                _step = ModUI.Text(container, string.Empty);

                _repeat = ModUI.Button(container, ModL10n.L("onb.hub.button.repeat"), OnRepeat);
                _skipStep = ModUI.Button(container, ModL10n.L("onb.hub.button.skipStep"), OnSkipStep);
                _skipAll = ModUI.Button(container, ModL10n.L("onb.hub.button.skipAll"), OnSkipAll);
                _restart = ModUI.Button(container, ModL10n.L("onb.hub.button.restart"), OnRestart);

                Refresh();
            }

            // ---- buttons ----

            private void OnRepeat()
            {
                _director.Controller.ResendCurrent();
                Refresh();
            }

            private void OnSkipStep()
            {
                _director.Controller.SkipCurrentStep();
                Refresh();
            }

            private void OnSkipAll()
            {
                _director.Controller.SkipAll("hub");
                Refresh();
            }

            private void OnRestart()
            {
                // The core's dialog stops the time while it is open; "No" and every other way out leave the tutorial as it is.
                ModUI.Confirm("onb.hub.restart.title", "onb.hub.restart.body", () =>
                {
                    _director.Restart();
                    Refresh();
                });
            }

            // ---- drawing ----

            /// <summary>
            /// Draws the state again. A widget that is gone (the player closed the hub before the answer of the dialog, or the template was missing) is
            /// the Unity null and is left out; with all of them gone nothing is read.
            /// </summary>
            private void Refresh()
            {
                if (_mode == null && _track == null && _step == null && _repeat == null && _skipStep == null && _skipAll == null && _restart == null) return;

                HubStatus status = CurrentStatus();

                if (_mode != null) _mode.SetTexts(ModL10n.L("onb.hub.mode"), ModL10n.L(status.ModeKey));
                if (_track != null) _track.SetTexts(ModL10n.L("onb.hub.track"), status.TrackKey == null ? "-" : ModL10n.L(status.TrackKey));
                if (_step != null) _step.text = StepLine(status);

                Enable(_repeat, status.CanRepeat);
                Enable(_skipStep, status.CanSkipStep);
                Enable(_skipAll, status.CanSkipAll);
                Enable(_restart, status.CanRestart);
            }

            private HubStatus CurrentStatus()
            {
                OnboardingController controller = _director.Controller;
                return HubStatus.Of(controller.State, controller.GetProgress(), controller.IsOldSave, OnboardingFeature.Catalog);
            }

            private static string StepLine(HubStatus status)
            {
                if (status.StepNumber <= 0 || status.StepNameKey == null) return ModL10n.L("onb.hub.step.none");

                string name = ModL10n.Has(status.StepNameKey) ? ModL10n.L(status.StepNameKey) : status.StepId;
                return ModL10n.L("onb.hub.step", status.StepNumber, status.StepTotal, name);
            }

            private static void Enable(ButtonUI button, bool enabled)
            {
                if (button != null) button.Interactable = enabled;
            }
        }
    }
}
