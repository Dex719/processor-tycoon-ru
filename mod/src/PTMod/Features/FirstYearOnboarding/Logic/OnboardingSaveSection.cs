using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The holder of the section <c>first-year-onboarding</c> of the side save file as the core's save model sees it
    /// (<see cref="ISideFileParticipant"/>, TSK-009): what the feature's <c>IModSaveParticipant</c> delegates to. It owns the
    /// <see cref="State"/> of the tutorial and has no game dependencies, so the real <c>SideFileModel</c> can drive it in a test: no section,
    /// a section from a newer build (the core resets the participant and keeps the stored text), a stored section. The core keeps the version of
    /// the section (<c>_schema</c>): it strips it before <see cref="Read"/> and adds it to what <see cref="Write"/> returns (FR-5, NFR-3).
    /// <para>
    /// The state object is replaced on every <see cref="Read"/> and <see cref="ResetToDefaults"/>; <see cref="StateReplaced"/> tells the director
    /// to build its machine over the new one. A game without the section, a new game and a section the build cannot read all mean "no tutorial":
    /// <see cref="OnboardingMode.Off"/> (FR-9 item 7).
    /// </para>
    /// </summary>
    public sealed class OnboardingSaveSection : ISideFileParticipant
    {
        private readonly Action<string> _warn;

        /// <param name="warn">Receives one English line per problem met while reading (the feature logs them); may be null.</param>
        public OnboardingSaveSection(Action<string> warn = null)
        {
            _warn = warn;
            State = new OnboardingState();
        }

        public string SectionId
        {
            get { return OnboardingSaveCodec.SectionId; }
        }

        public int SchemaVersion
        {
            get { return OnboardingSaveCodec.SchemaVersion; }
        }

        /// <summary>The state as it was read, or the state of a game without a tutorial after a reset. Never null; do not keep the reference across a load.</summary>
        public OnboardingState State { get; private set; }

        /// <summary>Raised after <see cref="State"/> was replaced (a load, a reset). The machine built over the old state is out of date then.</summary>
        public event Action StateReplaced;

        /// <summary>A new game, a save without the section, a section newer than this build, leaving to the menu: no tutorial.</summary>
        public void ResetToDefaults()
        {
            State = new OnboardingState();
            RaiseReplaced();
        }

        /// <summary>The stored section (without <c>_schema</c>). <paramref name="schema"/> below <see cref="SchemaVersion"/> would mean "migrate"; there is only version 1.</summary>
        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            State = OnboardingSaveCodec.Read(section, problems);

            if (_warn != null)
            {
                foreach (string problem in problems) _warn(problem);
            }
            RaiseReplaced();
        }

        /// <summary>The data to store, without <c>_schema</c> (the core adds it): a fresh object each time, never null.</summary>
        public JsonObject Write()
        {
            return OnboardingSaveCodec.Write(State);
        }

        private void RaiseReplaced()
        {
            Action handlers = StateReplaced;
            if (handlers == null) return;

            try
            {
                handlers();
            }
            catch (Exception e)
            {
                // A subscriber's fault must not fail the load of the whole side file.
                if (_warn != null) _warn("state replaced: a subscriber failed (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }
    }
}
