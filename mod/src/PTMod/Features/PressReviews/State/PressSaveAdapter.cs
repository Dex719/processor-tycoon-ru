using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The holder of the section <c>press-reviews</c> of the side save file (FR-12, <c>IModSaveParticipant</c>) and of the live
    /// <see cref="PressState"/>. The core calls <see cref="Read"/> when a save is loaded, <see cref="Write"/> after each save and
    /// <see cref="ResetToDefaults"/> for a new game, the main menu, a save without the section and a section newer than <see cref="SchemaVersion"/>;
    /// the feature never touches the game's save or the file. The version of the section (<c>_schema</c>) is written and read by the core: the data
    /// has no version field, and <see cref="SchemaVersion"/> is the only place the number lives (migrations go inside <see cref="Read"/>).
    /// The work is the pure <see cref="PressStateHolder"/> (binding, checks and migrations: <see cref="PressStateCodec"/>, <see cref="PressStateMigrations"/>);
    /// this class only plugs it into the core's interface and writes the problems it reports to the log. It registers nothing and subscribes to
    /// nothing: the provider of <c>popularity.gain</c> is registered once, in <c>Register</c>, and reads the state the load left behind.
    /// </summary>
    public sealed class PressSaveAdapter : IModSaveParticipant
    {
        /// <summary>The one holder of the session; the feature registers it with <c>ctx.SaveParticipant</c>.</summary>
        public static readonly PressSaveAdapter Instance = new PressSaveAdapter();

        private readonly PressStateHolder holder = new PressStateHolder(line => ModLog.Warn(PressStateHolder.Id, line));

        /// <summary>The state of the loaded game (the empty state before a load and after a reset). Never null.</summary>
        public PressState State
        {
            get { return holder.State; }
        }

        public string SectionId
        {
            get { return holder.SectionId; }
        }

        public int SchemaVersion
        {
            get { return holder.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            holder.ResetToDefaults();
        }

        public void Read(JsonObject section, int schema)
        {
            holder.Read(section, schema);
        }

        public JsonObject Write()
        {
            return holder.Write();
        }
    }
}
