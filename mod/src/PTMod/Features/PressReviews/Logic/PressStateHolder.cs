using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The logic of the save participant of the feature (FR-12, TSK-018): it holds the live <see cref="PressState"/> and answers the four calls the core
    /// makes to a holder of a section - <see cref="ResetToDefaults"/> (a new game, the main menu, a save without the section, a section newer than the
    /// mod or unreadable), <see cref="Read"/> (the stored section without <c>_schema</c> and the stored version), <see cref="Write"/> (the data without
    /// <c>_schema</c>) and the section's identity. The binding, the checks and the migrations are <see cref="PressStateCodec"/>'s. It is a plain
    /// <c>ISideFileParticipant</c>, so the unit tests drive it through the real model of the side file (<c>SideFileModel</c>) without the game;
    /// <c>State/PressSaveAdapter</c> is the thin <c>IModSaveParticipant</c> the feature registers with the core and passes every call on to this class.
    /// Nothing here registers a provider or subscribes to an event: a load only replaces the state, and what depends on it (the hype of the reviewed CPUs)
    /// is built again from the archive by the code that uses it. Main thread only.
    /// </summary>
    public sealed class PressStateHolder : ISideFileParticipant
    {
        /// <summary>The id of the section: the feature's slug.</summary>
        public const string Id = "press-reviews";

        private readonly Action<string> warn;

        /// <param name="warn">Receives one English line per problem found while reading (a repaired value, a dropped entry); may be null.</param>
        public PressStateHolder(Action<string> warn)
        {
            this.warn = warn;
            State = new PressState();
        }

        /// <summary>The state of the loaded game (the empty state before a load and after a reset). Never null; a load or a reset gives a new object.</summary>
        public PressState State { get; private set; }

        public string SectionId
        {
            get { return Id; }
        }

        /// <summary>The version of the section this mod writes; the core stores it as <c>_schema</c>, the data has no version field.</summary>
        public int SchemaVersion
        {
            get { return PressStateCodec.SectionVersion; }
        }

        public void ResetToDefaults()
        {
            State = new PressState();
        }

        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            State = PressStateCodec.Read(section, schema, problems);
            if (warn == null) return;

            for (int i = 0; i < problems.Count; i++) warn("save: " + problems[i]);
        }

        public JsonObject Write()
        {
            return PressStateCodec.Write(State);
        }
    }
}
