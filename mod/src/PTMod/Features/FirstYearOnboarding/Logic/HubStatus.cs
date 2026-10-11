using System;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// What the page "Tutorial" of the mods hub shows and which of its buttons work (FR-13 item 1; design.md, "OnboardingHubPage"): the mode, the track,
    /// "Step N of M: name", and the four buttons. Made from the state and the progress of the machine; the page itself only draws it. Plain fields; pure logic.
    /// </summary>
    public sealed class HubStatus
    {
        /// <summary>String key of the mode line: <c>onb.hub.mode.&lt;mode&gt;</c>, or <c>onb.hub.status.oldSave</c> for a save made before the mod.</summary>
        public string ModeKey;

        /// <summary>String key of the track (<c>onb.track.&lt;track&gt;</c>); null while the track is not known.</summary>
        public string TrackKey;

        /// <summary>The place of the current step among the sequential steps ("Step N"); 0 when there is no step to show.</summary>
        public int StepNumber;

        /// <summary>The number of sequential steps of the track ("of M").</summary>
        public int StepTotal;

        /// <summary>String key of the short name of the step; null when there is no step to show.</summary>
        public string StepNameKey;

        /// <summary>The id of the step on the line; null when there is none.</summary>
        public string StepId;

        /// <summary>"Repeat the mail of the step": a step is current and its mail went out.</summary>
        public bool CanRepeat;

        /// <summary>"Skip the step": a step is current.</summary>
        public bool CanSkipStep;

        /// <summary>"Skip the tutorial": the tutorial is running.</summary>
        public bool CanSkipAll;

        /// <summary>"Restart the tutorial": the content has steps (works from every mode, an old save included).</summary>
        public bool CanRestart;

        /// <summary>The content has no usable step: the page shows the error line instead.</summary>
        public bool ContentBroken;

        /// <summary>
        /// Some track has no step left (FR-3 item 3): the tutorial is off for that track and the page adds the error line to the usual status. False when
        /// the content is broken as a whole (<see cref="ContentBroken"/> says that) and when every track has steps.
        /// </summary>
        public bool ContentDamaged;

        /// <summary>The save was made before the mod (no decision about the tutorial was ever made for it).</summary>
        public bool IsOldSave;

        /// <summary>The mode line key for a mode.</summary>
        public static string ModeKeyOf(OnboardingMode mode)
        {
            switch (mode)
            {
                case OnboardingMode.Active: return "onb.hub.mode.active";
                case OnboardingMode.Skipped: return "onb.hub.mode.skipped";
                case OnboardingMode.Completed: return "onb.hub.mode.completed";
                case OnboardingMode.Aborted: return "onb.hub.mode.aborted";
                default: return "onb.hub.mode.off";
            }
        }

        /// <summary>The state of the page for the tutorial of the game in play.</summary>
        /// <param name="state">The state of the tutorial.</param>
        /// <param name="progress">The progress ("Step N of M") of the machine over that state.</param>
        /// <param name="isOldSave">No decision about the tutorial was made for this game.</param>
        /// <param name="catalog">The steps (for the name of the current step and for the broken-content check).</param>
        public static HubStatus Of(OnboardingState state, OnboardingProgress progress, bool isOldSave, StepCatalog catalog)
        {
            if (state == null) throw new ArgumentNullException("state");

            var status = new HubStatus
            {
                ContentBroken = catalog == null || catalog.IsEmpty,
                IsOldSave = isOldSave,
                ModeKey = isOldSave ? "onb.hub.status.oldSave" : ModeKeyOf(state.Mode),
                TrackKey = string.IsNullOrEmpty(state.Track) ? null : "onb.track." + state.Track
            };
            status.CanRestart = !status.ContentBroken;
            if (status.ContentBroken) return status;
            status.ContentDamaged = catalog.MissingTracks.Count > 0;

            bool active = state.Mode == OnboardingMode.Active;
            status.CanSkipAll = active;

            if (progress != null) status.StepTotal = progress.Total;
            if (!active || progress == null || string.IsNullOrEmpty(progress.StepId)) return status;

            StepDefinition def = catalog.Find(progress.StepId);
            status.StepId = progress.StepId;
            status.StepNumber = progress.Number;
            status.StepNameKey = def == null ? null : def.NameKey;

            StepState step = state.Find(progress.StepId);
            bool current = step != null && step.Status == StepStatus.Current;
            status.CanSkipStep = current;
            status.CanRepeat = current && step.MailId > 0;
            return status;
        }
    }
}
