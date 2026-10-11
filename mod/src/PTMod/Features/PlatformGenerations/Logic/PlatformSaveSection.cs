using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The holder of the section "platform-generations" of the side save file, as the core's save model sees it (<see cref="ISideFileParticipant"/>):
    /// what the feature's <c>IModSaveParticipant</c> delegates to. It owns the DTO <see cref="Data"/> and the flag that the chronology has to be
    /// reconstructed, and has no game dependencies, so the real <c>SideFileModel</c> can drive it in a test (reset on a new game, read, write).
    /// The core writes <c>_schema</c> (<see cref="SchemaVersion"/>) and strips it before <see cref="Read"/>; <see cref="Write"/> returns the data without it.
    /// </summary>
    public sealed class PlatformSaveSection : ISideFileParticipant
    {
        private readonly string _sectionId;
        private readonly Action<string> _warn;

        /// <param name="sectionId">The id of the section: the feature's id.</param>
        /// <param name="warn">Receives one English line per problem met while reading (the feature logs them); may be null.</param>
        public PlatformSaveSection(string sectionId, Action<string> warn = null)
        {
            _sectionId = sectionId;
            _warn = warn;
            Data = new PlatformGenerationsSave();
            NeedsReconstruction = true;
        }

        public string SectionId
        {
            get { return _sectionId; }
        }

        public int SchemaVersion
        {
            get { return PlatformSaveCodec.SchemaVersion; }
        }

        /// <summary>The section as it was read, or empty after a reset. Never null.</summary>
        public PlatformGenerationsSave Data { get; private set; }

        /// <summary>
        /// Nothing was read: the state started from nothing (a new game, a save without the section, a section newer than this build, leaving to the
        /// menu), so the chronology has to be reconstructed from the CPUs of the market (FR-12 items 3-4). Whoever reconstructs it lowers the flag.
        /// A fresh holder has it raised.
        /// </summary>
        public bool NeedsReconstruction { get; set; }

        /// <summary>A new game (before the fast-forward starts), no section in the file, a section newer than this build, the menu: the empty state (FR-12 item 1).</summary>
        public void ResetToDefaults()
        {
            Data = new PlatformGenerationsSave();
            NeedsReconstruction = true;
        }

        /// <summary>The stored section, without <c>_schema</c>. <paramref name="schema"/> below <see cref="SchemaVersion"/> would mean "migrate"; there is only version 1.</summary>
        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            Data = PlatformSaveCodec.Read(section, problems);
            NeedsReconstruction = false;

            if (_warn == null) return;
            foreach (string problem in problems) _warn(problem);
        }

        /// <summary>The data to store, without <c>_schema</c> (the core adds it): a fresh object each time.</summary>
        public JsonObject Write()
        {
            return PlatformSaveCodec.Write(Data);
        }
    }
}
