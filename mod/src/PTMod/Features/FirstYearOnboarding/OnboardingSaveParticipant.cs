using System;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The holder of the section <c>first-year-onboarding</c> of the side save file (TSK-009, FR-10, NFR-3): the part of the feature the core
    /// calls to reset, read and write the state of the tutorial. The work is <see cref="OnboardingSaveSection"/>'s (pure logic with the tests); this
    /// class only adds the core's interface and the log. The version of the section (<c>_schema</c>) is the core's business, the state has no such
    /// field. Problems met while reading a section (a bad date, an unknown status) are written to <c>ModLog</c> as warnings and the damaged value is
    /// repaired or dropped. A save without the section, or one from a newer build, gives <see cref="OnboardingMode.Off"/> (the stored text of a newer
    /// section is kept by the core).
    /// </summary>
    internal sealed class OnboardingSaveParticipant : IModSaveParticipant
    {
        private readonly OnboardingSaveSection _section;

        public OnboardingSaveParticipant()
        {
            _section = new OnboardingSaveSection(line => ModLog.Warn(OnboardingFeature.FeatureId, "save: " + line));
        }

        /// <summary>The state of the save in play. Never null; the object is replaced on every load, a new game and a return to the menu, so do not keep it: listen to <see cref="StateReplaced"/>.</summary>
        public OnboardingState State
        {
            get { return _section.State; }
        }

        /// <summary>The state was replaced (a load, a new game, the menu): the machine of the director has to be built over <see cref="State"/> again.</summary>
        public event Action StateReplaced
        {
            add { _section.StateReplaced += value; }
            remove { _section.StateReplaced -= value; }
        }

        public string SectionId
        {
            get { return _section.SectionId; }
        }

        public int SchemaVersion
        {
            get { return _section.SchemaVersion; }
        }

        /// <summary>A new game, a save without the section, a section newer than this build, the menu: no tutorial.</summary>
        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
        }

        /// <summary>The stored section (without <c>_schema</c>).</summary>
        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it). Never null: the state without a tutorial is written too.</summary>
        public JsonObject Write()
        {
            return _section.Write();
        }
    }
}
