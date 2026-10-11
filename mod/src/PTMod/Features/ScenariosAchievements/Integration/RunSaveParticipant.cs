using System;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The holder of the section <c>scenarios-achievements</c> of the side save file (TSK-014, FR-16, NFR-3): the part of the feature the core
    /// calls to reset, read and write the run in play. The work is <see cref="RunSaveSection"/>'s (pure logic with the tests, which uses
    /// <see cref="RunSaveCodec"/>); this class only adds the core's interface and the log. The version of the section (<c>_schema</c>) is the
    /// core's business, the run has no such field: <c>SchemaVersion</c> is 1. Problems met while reading a section (a bad date, an unknown
    /// status) are written to <c>ModLog</c> as warnings and the damaged value is repaired or dropped. A save without the section, or one from a newer
    /// build, gives no run (<see cref="Run"/> is null): <c>RunDirector</c> makes a <c>legacy</c> run on <c>GameReady(Loaded)</c>; the stored text of
    /// a newer section is kept by the core. One instance for the session, registered by <c>ScenariosFeature.Register</c>.
    /// </summary>
    internal sealed class RunSaveParticipant : IModSaveParticipant
    {
        /// <summary>The participant of the session.</summary>
        internal static readonly RunSaveParticipant Instance = new RunSaveParticipant();

        private readonly RunSaveSection _section;

        private RunSaveParticipant()
        {
            _section = new RunSaveSection(line => ModLog.Warn(ScenariosFeature.FeatureId, "save: " + line));
        }

        /// <summary>The run in play, or null when there is none. The director sets it when a run starts or a legacy run is born.</summary>
        internal RunState Run
        {
            get { return _section.Run; }
            set { _section.Run = value; }
        }

        /// <summary>The run was replaced by a load or a reset: whatever was built over the old run is out of date.</summary>
        internal event Action RunReplaced
        {
            add { _section.RunReplaced += value; }
            remove { _section.RunReplaced -= value; }
        }

        public string SectionId
        {
            get { return _section.SectionId; }
        }

        public int SchemaVersion
        {
            get { return _section.SchemaVersion; }
        }

        /// <summary>A new game, a save without the section, a section newer than this build, the menu: no run.</summary>
        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
        }

        /// <summary>The stored section (without <c>_schema</c>).</summary>
        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it); null (the section is left out) when there is no run.</summary>
        public JsonObject Write()
        {
            return _section.Write();
        }
    }
}
