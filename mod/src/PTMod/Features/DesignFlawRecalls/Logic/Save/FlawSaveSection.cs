using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The holder of the section "design-flaw-recalls" of the side save file (FR-16): what the core calls when a save is loaded, written or reset
    /// (design.md, Component 17). <c>SectionId</c> is the feature id and <c>SchemaVersion</c> is 1; the core writes and reads the version as
    /// <c>_schema</c>, so <see cref="Write"/> never returns it and <see cref="FlawSaveData"/> has no version field. <c>Read(section, schema)</c>
    /// migrates the stored section to the current version step by step (<see cref="FlawSaveCodec.Migrate"/>) and then checks it
    /// (<see cref="FlawSaveCodec.Read"/>); the state lives in <see cref="Data"/>.
    /// <para>
    /// This is one class in two files: this one (pure, compiled by the unit tests, implements the logic-side
    /// <see cref="ISideFileParticipant"/>) and <c>Save/FlawSaveSection.cs</c> (the game build), which adds the game-side interface
    /// <c>IModSaveParticipant</c> that <c>FeatureContext.SaveParticipant</c> takes. <c>IModSaveParticipant</c> only extends
    /// <see cref="ISideFileParticipant"/>, so nothing else differs.
    /// </para>
    /// <para>
    /// A state that starts from nothing (<see cref="ResetToDefaults"/>: a new game, a save without the section, a section the core could not use,
    /// leaving to the menu) is empty, except that every CPU that exists at that moment is entered in <c>rolledCpuIds</c>: a CPU that was released
    /// before the mod knew the save must not get a flaw roll afterwards (FR-16 item 3, design.md "Migration").
    /// </para>
    /// </summary>
    public sealed partial class FlawSaveSection : ISideFileParticipant
    {
        /// <summary>The id of the section: the slug of the feature.</summary>
        public const string Id = "design-flaw-recalls";

        private readonly Func<IEnumerable<int>> _existingCpuIds;
        private readonly Action<string> _warn;

        /// <param name="existingCpuIds">
        /// The <c>SaveID</c>s of the CPUs that exist now (the game's <c>CpuDataProvider.GetAllCpus</c>); called by <see cref="ResetToDefaults"/>.
        /// <c>null</c> for none. An exception or <c>null</c> from it means "none known" (in the menu the game's objects are gone).
        /// </param>
        /// <param name="warn">Receives the problems found while loading (a line each); <c>null</c> to drop them.</param>
        public FlawSaveSection(Func<IEnumerable<int>> existingCpuIds = null, Action<string> warn = null)
        {
            _existingCpuIds = existingCpuIds;
            _warn = warn;
            Data = new FlawSaveData();
        }

        /// <summary>The key of the section in <c>features</c> of the side file: the feature id.</summary>
        public string SectionId
        {
            get { return Id; }
        }

        /// <summary>The version of the section; the core writes it as <c>_schema</c> and hands the stored one to <see cref="Read"/>.</summary>
        public int SchemaVersion
        {
            get { return FlawSaveCodec.SchemaVersion; }
        }

        /// <summary>The state of the feature as it was read or started. Never <c>null</c>; replaced by <see cref="ResetToDefaults"/> and <see cref="Read"/>.</summary>
        public FlawSaveData Data { get; private set; }

        /// <summary><see cref="Read"/> ran since the last <see cref="ResetToDefaults"/>: the section was in the file and was used.</summary>
        public bool WasRead { get; private set; }

        /// <summary>
        /// The section was written by a newer mod and is kept by the core as it is (<c>SideFileStatus.Newer</c>): the feature works on an empty state for
        /// this save and must not start flaws it would lose (design.md, "Error Handling": the feature is off for this save). Set by <see cref="LockForNewerSchema"/>.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Marks the section as held by a newer mod. The core calls only <see cref="ResetToDefaults"/> for such a section, so the feature calls this
        /// after the load, when the status of the side file is "newer" and the section was not read. <see cref="ResetToDefaults"/> lifts the lock.
        /// </summary>
        public void LockForNewerSchema()
        {
            IsLocked = true;
        }

        /// <summary>
        /// A new game, no section in the file (an old save, a mod added in mid-game), a section newer than this build or unreadable, leaving to
        /// the menu: the state is empty, and the CPUs that exist now are entered as already rolled.
        /// </summary>
        public void ResetToDefaults()
        {
            Data = new FlawSaveData();
            WasRead = false;
            IsLocked = false;
            MarkExistingCpusRolled();
        }

        /// <summary>
        /// Reads the stored section (without <c>_schema</c>; the object is not modified). <paramref name="schema"/> below the current version is
        /// migrated step by step; above it (the core never does this: it keeps such a section and calls <see cref="ResetToDefaults"/>) the section
        /// is not read and the state is reset. Problems are reported through the <c>warn</c> callback.
        /// </summary>
        public void Read(JsonObject section, int schema)
        {
            if (schema > SchemaVersion)
            {
                Warn("save: the section has the version " + schema + ", newer than " + SchemaVersion + "; it is not read");
                ResetToDefaults();
                IsLocked = true;
                return;
            }

            var problems = new List<string>();
            JsonObject current = FlawSaveCodec.Migrate(section, schema < 1 ? 1 : schema, SchemaVersion, problems);
            Data = FlawSaveCodec.Read(current, problems);
            WasRead = true;
            IsLocked = false;
            foreach (string problem in problems) Warn("save: " + problem);
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it). A fresh object each time.</summary>
        public JsonObject Write()
        {
            return FlawSaveCodec.Write(Data);
        }

        private void MarkExistingCpusRolled()
        {
            if (_existingCpuIds == null) return;

            try
            {
                IEnumerable<int> ids = _existingCpuIds();
                if (ids == null) return;

                var seen = new HashSet<int>(Data.rolledCpuIds);
                var found = new List<int>();
                foreach (int id in ids)
                {
                    if (id >= 0 && seen.Add(id)) found.Add(id);
                }
                found.Sort();
                Data.rolledCpuIds.AddRange(found);
            }
            catch (Exception e)
            {
                // In the menu the game's objects are gone: nothing exists, nothing to enter.
                Warn("save: the CPUs that exist could not be listed (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private void Warn(string text)
        {
            if (_warn == null) return;
            try
            {
                _warn(text);
            }
            catch (Exception)
            {
                // A logger must not break the load.
            }
        }
    }
}
