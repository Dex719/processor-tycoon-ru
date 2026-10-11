using System;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The holder of the section <c>gpu-product-line</c> of the side save file, in the shape the core's logic sees (<see cref="ISideFileParticipant"/>):
    /// <c>SectionId</c> is the id of the feature, <c>SchemaVersion</c> is 1, <c>ResetToDefaults</c> / <c>Read</c> / <c>Write</c> go to <see cref="GpuState"/>.
    /// The game-side <c>GpuSaveParticipant</c> adds only the interface <c>IModSaveParticipant</c> (it is the same interface, but the unit tests cannot see
    /// it), so this class is what the tests exercise. Without a version field: the core reads and writes <c>_schema</c>.
    /// </summary>
    public class GpuSectionParticipant : ISideFileParticipant
    {
        private readonly GpuState _state;

        public GpuSectionParticipant(GpuState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            _state = state;
        }

        public string SectionId
        {
            get { return GpuState.SectionId; }
        }

        public int SchemaVersion
        {
            get { return GpuState.SchemaVersion; }
        }

        /// <summary>New game, no section in the file, section newer than <see cref="SchemaVersion"/>: the division is not founded and the market is not seeded.</summary>
        public void ResetToDefaults()
        {
            _state.ResetToDefaults();
        }

        /// <summary>Reads the section; <paramref name="schema"/> below <see cref="SchemaVersion"/> migrates inside (<see cref="GpuState.Read"/>).</summary>
        public void Read(JsonObject section, int schema)
        {
            _state.Read(section, schema);
        }

        /// <summary>The section without <c>_schema</c>.</summary>
        public JsonObject Write()
        {
            return _state.Write();
        }
    }
}
