using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The holder of the section <c>rival-acquisitions</c> of the side save file (FR-5 of the core, NFR-3): it hands <see cref="AcquisitionsState"/> to
    /// <see cref="AcquisitionsSection"/>, which turns it into a <c>JsonObject</c> and back with <c>ModJson</c>, and writes the problems to the log. The version of
    /// the section is the core's business: <see cref="SchemaVersion"/> is written by the core as <c>_schema</c> and handed back to <see cref="Read"/>, the DTO has no
    /// version field. A save made before the feature, or a section newer than this version, reaches <see cref="ResetToDefaults"/> and the feature starts with an
    /// empty book (for a newer section the feature then offers no deal: <see cref="RivalAcquisitionsFeature"/> closes the offers once the save is read).
    /// <para>
    /// A section that is damaged field by field is read as far as it goes: the fields that bind keep their values, the records that cannot be right
    /// are dropped by <see cref="AcquisitionsState.Sanitize"/>, and every problem is a line in the log. Whether a record still points at a living
    /// company is checked where the game is at hand, not here.
    /// </para>
    /// </summary>
    internal sealed class AcquisitionsSaveParticipant : IModSaveParticipant
    {
        private readonly RivalAcquisitionsFeature _feature;

        public AcquisitionsSaveParticipant(RivalAcquisitionsFeature feature)
        {
            _feature = feature;
        }

        public string SectionId
        {
            get { return AcquisitionsSection.SectionId; }
        }

        public int SchemaVersion
        {
            get { return AcquisitionsSection.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            _feature.State = new AcquisitionsState();
        }

        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            _feature.State = AcquisitionsSection.Read(section, problems);
            foreach (string line in problems) ModLog.Warn(RivalAcquisitionsFeature.FeatureId, "save: " + line);
        }

        public JsonObject Write()
        {
            var problems = new List<string>();
            JsonObject section = AcquisitionsSection.Write(_feature.State, problems);
            foreach (string line in problems) ModLog.Warn(RivalAcquisitionsFeature.FeatureId, "save: " + line);
            return section;
        }
    }
}
