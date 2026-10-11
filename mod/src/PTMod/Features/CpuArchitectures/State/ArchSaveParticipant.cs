using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The holder of the section <c>cpu-architectures</c> of the side save file (NFR-3): what <c>ctx.SaveParticipant</c> registers. The reading and
    /// writing are <see cref="ArchSaveSection"/>'s (under the unit tests); this class only adds the core's interface and sends the notes about unreadable
    /// records to the log. The core guards every call, counts a failure against the feature and keeps the previous text of a section it could not read or write.
    /// </summary>
    internal sealed class ArchSaveParticipant : IModSaveParticipant
    {
        private readonly ArchSaveSection _section;

        public ArchSaveParticipant(ArchRegistry registry)
        {
            _section = new ArchSaveSection(registry, line => ModLog.Warn(ArchitecturesFeature.FeatureId, "save: " + line));
        }

        public string SectionId
        {
            get { return _section.SectionId; }
        }

        /// <summary>Version 1; <c>_schema</c> is written and read by the core, the data has no version field of its own.</summary>
        public int SchemaVersion
        {
            get { return _section.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
        }

        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
        }

        public JsonObject Write()
        {
            return _section.Write();
        }
    }
}
