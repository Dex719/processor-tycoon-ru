using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The save participant of the feature (FR-10): the section <c>rival-personalities</c> of the side save file. The core calls it when a save
    /// is loaded or written and when the game is reset; the feature never opens the file or the game's save. The work is
    /// <see cref="RivalSaveSection"/>'s (pure, tested); this class only gives it to <c>ctx.SaveParticipant</c> and logs what the reading found.
    /// The core adds and strips <c>_schema</c>; <see cref="RivalState"/> has no version field. The section is read through the migrations and the checks of
    /// the values (TSK-023), and a feature that is switched off and on again in one session gets back the state it left with (<see cref="Freeze"/>).
    /// </summary>
    internal sealed class RivalSaveAdapter : IModSaveParticipant
    {
        private readonly RivalSaveSection _section;

        public RivalSaveAdapter(string featureId)
        {
            _section = new RivalSaveSection(featureId, line => ModLog.Warn(featureId, "save: " + line));

            // a new header object for every side file that is read: the identity of the game in play (null before any file was read)
            _section.UseFreeze(() => ModFeatures.IsActive(featureId), () => ModSave.FileHeader);
        }

        /// <summary>
        /// The feature was switched off (<c>OnDisabled</c>): keeps the state it leaves with for the first hand-over after it is switched on again in the same
        /// game. Without this the core would hand back its copy of the last save, or none.
        /// </summary>
        public void Freeze()
        {
            _section.Freeze();
        }

        /// <summary>A game was loaded or started: a state kept for a switch-on belongs to a game that is gone.</summary>
        public void DropFrozen()
        {
            _section.DropFrozen();
        }

        /// <summary>A state is kept for a switch-on.</summary>
        public bool IsFrozen
        {
            get { return _section.IsFrozen; }
        }

        /// <summary>The state as it was read, or the default one (a new game, a save without the section). Never null; replaced by a load, so read it again after one.</summary>
        public RivalState State
        {
            get { return _section.State; }
        }

        public string SectionId
        {
            get { return _section.SectionId; }
        }

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
