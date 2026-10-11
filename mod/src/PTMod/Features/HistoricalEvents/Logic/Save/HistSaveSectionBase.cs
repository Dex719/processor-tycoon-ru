using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The section "historical-events" of the side save file, without the game (FR-18, NFR-3): what the core asks of a holder of a section. The class
    /// the feature registers is <c>HistSaveSection : HistSaveSectionBase, IModSaveParticipant</c> (folder <c>Save/</c>); the part that is pure lives
    /// here so that the tests can compile it (the build of the tests takes <c>Logic/</c> only).
    /// <list type="bullet">
    /// <item><b>Version.</b> <see cref="SchemaVersion"/> is 1; the core writes it as <c>_schema</c> and reads it back. <see cref="Write"/> returns the data without
    /// it; <see cref="HistSaveData"/> has no field for it. A section newer than this build is not passed to <see cref="Read"/> at all: the core calls
    /// <see cref="ResetToDefaults"/> and keeps the stored section as it was (the feature then stays off for that save).</item>
    /// <item><b>No section</b> (an old save, FR-18 item 3): <see cref="ResetToDefaults"/> builds <see cref="HistSaveCodec.CreateDefaults"/> from the calendar and
    /// the current day as soon as it knows both. The calendar is loaded when the game is ready, which is after the core has read the file, and a reset
    /// in the menu has no date; then <see cref="NeedsDefaults"/> stays set and whoever loads the calendar calls <see cref="TryApplyDefaults"/> once the
    /// game of the loaded save is ready. A new game does the same and is overwritten by the scheduler after the fast-forward (<c>initialized = false</c>).</item>
    /// </list>
    /// </summary>
    public class HistSaveSectionBase : ISideFileParticipant
    {
        private readonly Func<IReadOnlyList<CalendarEntry>> _calendar;
        private readonly Func<int?> _today;
        private readonly Action<string> _warn;

        /// <param name="calendar">The valid entries of the calendar; null or empty while it is not loaded.</param>
        /// <param name="today">The current day (<see cref="SimpleDate.Days"/>) of the game that is running, null when there is none (the menu, a scene being loaded).</param>
        /// <param name="warn">Where the lines about repaired values go (the log); may be null.</param>
        public HistSaveSectionBase(Func<IReadOnlyList<CalendarEntry>> calendar, Func<int?> today, Action<string> warn)
        {
            _calendar = calendar;
            _today = today;
            _warn = warn;
        }

        /// <summary>The key in <c>features</c>: the feature's id.</summary>
        public string SectionId
        {
            get { return HistIds.SaveSection; }
        }

        /// <summary>The version of the section; the core writes it as <c>_schema</c>.</summary>
        public int SchemaVersion
        {
            get { return HistSaveCodec.SchemaVersion; }
        }

        /// <summary>
        /// The state of the feature in the game that runs. Never null. <see cref="Read"/> and <see cref="ResetToDefaults"/> replace the object and its
        /// lists, so a service must take <c>Data</c> afresh whenever it works and not keep the lists from an earlier look.
        /// </summary>
        public HistSaveData Data { get; private set; } = new HistSaveData();

        /// <summary>The state was reset and the statuses by the current date are still to be set (see the class summary).</summary>
        public bool NeedsDefaults { get; private set; }

        /// <summary>A new game, a save without the section, a section newer than this build, leaving to the menu: the state starts empty.</summary>
        public void ResetToDefaults()
        {
            Data = new HistSaveData();
            NeedsDefaults = true;
            TryApplyDefaults();
        }

        /// <summary>The stored section (without <c>_schema</c>). There is only version 1, so <paramref name="schema"/> needs no migration.</summary>
        public void Read(JsonObject section, int schema)
        {
            var problems = new List<string>();
            Data = HistSaveCodec.Read(section, problems);
            NeedsDefaults = false;
            Warn(problems);
        }

        /// <summary>The section to store, without <c>_schema</c> (the core adds it). A fresh object each time.</summary>
        public JsonObject Write()
        {
            return HistSaveCodec.Write(Data);
        }

        /// <summary>
        /// Sets the statuses by the current date if a reset left them open and both the calendar and the date are known now. True when it did.
        /// A section that was read is never touched (<see cref="NeedsDefaults"/> is false then).
        /// </summary>
        public bool TryApplyDefaults()
        {
            if (!NeedsDefaults) return false;

            IReadOnlyList<CalendarEntry> entries = _calendar == null ? null : _calendar();
            int? today = _today == null ? null : _today();
            if (entries == null || entries.Count == 0 || !today.HasValue) return false;

            Data = HistSaveCodec.CreateDefaults(entries, today.Value);
            NeedsDefaults = false;
            return true;
        }

        /// <summary>
        /// Whoever set the state up by itself (the scheduler of a new game, after the fast-forward) says so: the statuses by the current date are not
        /// needed any more and <see cref="TryApplyDefaults"/> will not overwrite what was set.
        /// </summary>
        public void SetUpByOwner()
        {
            NeedsDefaults = false;
        }

        private void Warn(List<string> problems)
        {
            if (_warn == null) return;
            foreach (string problem in problems) _warn(problem);
        }
    }
}
