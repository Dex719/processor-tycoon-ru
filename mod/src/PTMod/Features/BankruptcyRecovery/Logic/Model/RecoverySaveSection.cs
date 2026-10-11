using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The holder of the section "bankruptcy-recovery" of the side save file as the core's save model sees it (<see cref="ISideFileParticipant"/>):
    /// what the feature's <c>IModSaveParticipant</c> delegates to. It owns the <see cref="State"/> and has no game dependencies, so the real
    /// <c>SideFileModel</c> can drive it in a test (reset on a new game, read, write). The core keeps the version of the section (<c>_schema</c>,
    /// <see cref="SchemaVersion"/>): it strips it before <see cref="Read"/> and adds it to what <see cref="Write"/> returns (FR-5, NFR-3).
    /// </summary>
    public sealed class RecoverySaveSection : ISideFileParticipant
    {
        private readonly string _sectionId;
        private readonly Action<string> _warn;

        /// <param name="sectionId">The id of the section: the feature's id.</param>
        /// <param name="warn">Receives one English line per problem met while reading (the feature logs them); may be null.</param>
        public RecoverySaveSection(string sectionId, Action<string> warn = null)
        {
            _sectionId = sectionId;
            _warn = warn;
            State = new RecoveryState();
        }

        // The safe switch-off (FR-12, TSK-022). The core hands a feature that is switched on again the section it kept: the one of the last save, or none, never the
        // state of the moment the feature went off (a switched-off participant is not asked to write). So the section remembers two things: the state as the settlement
        // left it ("frozen", used once, for the very next hand-over, and only when the feature is active again) and the marks in the flags of the save (always applied,
        // they also hold across a restart of the game).
        private ISettlementFlags _marks;
        private Func<bool> _isActive;
        private Action<string> _note;
        private Func<object> _game;
        private RecoveryState _frozen;
        private object _frozenGame;

        public string SectionId
        {
            get { return _sectionId; }
        }

        public int SchemaVersion
        {
            get { return RecoveryStateCodec.SchemaVersion; }
        }

        /// <summary>
        /// The state as it was read, or the state of a new game after a reset. Never null. The object is replaced by every <see cref="Read"/> and
        /// <see cref="ResetToDefaults"/>, so read it through the holder each time instead of keeping the reference.
        /// </summary>
        public RecoveryState State { get; private set; }

        /// <summary>A new game, a save without the section, a section newer than this build, leaving to the menu: the state of a game that never met the feature.</summary>
        public void ResetToDefaults()
        {
            RecoveryState frozen = TakeFrozen();
            State = frozen ?? new RecoveryState();
            Reconcile();
        }

        /// <summary>The stored section, without <c>_schema</c>. <paramref name="schema"/> below <see cref="SchemaVersion"/> would mean "migrate"; there is only version 1.</summary>
        public void Read(JsonObject section, int schema)
        {
            RecoveryState frozen = TakeFrozen();
            if (frozen != null)
            {
                State = frozen;   // the feature came back in the same session: the state it left with is newer than the copy the core kept
                Reconcile();
                return;
            }

            var problems = new List<string>();
            State = RecoveryStateCodec.Read(section, problems);

            if (_warn != null)
            {
                foreach (string problem in problems) _warn(problem);
            }
            Reconcile();
        }

        /// <summary>The data to store, without <c>_schema</c> (the core adds it): a fresh object each time, never null (a section with the defaults is written too).</summary>
        public JsonObject Write()
        {
            return RecoveryStateCodec.Write(State);
        }

        // ---- the safe switch-off ----

        /// <summary>
        /// Connects the marks of a settlement: the flags of the save they live in, whether the feature is active now (decides whether a frozen state is used or
        /// dropped), a place for the notes of what a reconcile changed (may be null), and the identity of the game in play (an object that changes whenever a save
        /// is read: the header of the side file; may be null for "never changes"). A frozen state is used only in the game it was frozen in.
        /// </summary>
        internal void UseMarks(ISettlementFlags marks, Func<bool> isActive, Action<string> note = null, Func<object> game = null)
        {
            _marks = marks;
            _isActive = isActive;
            _note = note;
            _game = game;
        }

        /// <summary>
        /// The feature was switched off and its obligations settled: remembers the state as it is now, for the first hand-over after the feature is active again.
        /// The core keeps nothing newer than its last copy of the section, so without this the loans of the last save would come back.
        /// </summary>
        internal void Freeze()
        {
            _frozen = State;
            _frozenGame = _game == null ? null : _game();
        }

        /// <summary>Forgets the frozen state (a game was loaded or started: it belongs to a game that is gone).</summary>
        internal void DropFrozen()
        {
            _frozen = null;
            _frozenGame = null;
        }

        /// <summary>A frozen state waits for the feature to come back.</summary>
        internal bool IsFrozen
        {
            get { return _frozen != null; }
        }

        /// <summary>
        /// Applies the marks of <see cref="SettlementMarks"/> to the state. Done after every hand-over of the core and after a load, and harmless to repeat; without
        /// connected marks it does nothing. Returns true when the state changed.
        /// </summary>
        internal bool Reconcile()
        {
            if (_marks == null) return false;
            return SettlementMarks.Reconcile(State, _marks, _note);
        }

        // The frozen state goes to whoever asks first, once, and only if two things hold. The feature is active: a hand-over to an inactive participant is a reset of
        // the whole mod (the menu, a new game) and the frozen state belongs to a game that is over. And the game is the same one: a save that was read since (even
        // the same file) is a different timeline, in which the money of the settlement was never taken, so its section and not the frozen state is the truth.
        private RecoveryState TakeFrozen()
        {
            RecoveryState frozen = _frozen;
            object game = _frozenGame;
            _frozen = null;
            _frozenGame = null;
            if (frozen == null) return null;
            if (_isActive == null || !_isActive()) return null;
            return ReferenceEquals(game, _game == null ? null : _game()) ? frozen : null;
        }
    }
}
