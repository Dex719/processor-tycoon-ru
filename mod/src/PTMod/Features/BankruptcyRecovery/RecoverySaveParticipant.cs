using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The holder of the section "bankruptcy-recovery" of the side save file (FR-5, NFR-3): the part of the feature the core calls to reset, read and
    /// write the state. The work is <see cref="RecoverySaveSection"/>'s (pure logic with the tests); this class only adds the core's interface and the
    /// log. The version of the section (<c>_schema</c>) is the core's business, the DTO has no such field. Problems met while reading a section
    /// (a damaged loan, a bad date) are written to <c>ModLog</c> as warnings and the damaged record is dropped.
    /// </summary>
    internal sealed class RecoverySaveParticipant : IModSaveParticipant
    {
        private readonly RecoverySaveSection _section;

        public RecoverySaveParticipant()
        {
            _section = new RecoverySaveSection(RecoveryApi.FeatureId, line => ModLog.Warn(RecoveryApi.FeatureId, "save: " + line));
            _section.UseMarks(new SaveMarks(), () => ModFeatures.IsActive(RecoveryApi.FeatureId), note => ModLog.Info(RecoveryApi.FeatureId, "save: " + note),
                () => ModSave.FileHeader);   // a new header object for every side file that is read: the identity of the game in play
        }

        /// <summary>
        /// The feature was switched off and settled: keeps the state it left with for the first hand-over after it comes back (see <see cref="RecoverySaveSection"/>).
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

        /// <summary>Applies the marks of a settlement to the state in play (after the core has read the whole file).</summary>
        public void Reconcile()
        {
            _section.Reconcile();
        }

        /// <summary>The state of the save in play. Never null; the object is replaced on every load, a new game and a return to the menu, so do not keep it.</summary>
        public RecoveryState State
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

        /// <summary>A new game, a save without the section, a section newer than this build, the menu: the state of a game that never met the feature.</summary>
        public void ResetToDefaults()
        {
            _section.ResetToDefaults();
        }

        /// <summary>The stored section (without <c>_schema</c>).</summary>
        public void Read(JsonObject section, int schema)
        {
            _section.Read(section, schema);
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it). Never null: the defaults are written too.</summary>
        public JsonObject Write()
        {
            return _section.Write();
        }
    }
}
