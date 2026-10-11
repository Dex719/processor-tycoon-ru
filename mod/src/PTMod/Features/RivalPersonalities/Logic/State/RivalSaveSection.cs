using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The holder of the section "rival-personalities" of the side save file, as the core's save model sees it (<see cref="ISideFileParticipant"/>):
    /// what the feature's <c>IModSaveParticipant</c> (<c>RivalSaveAdapter</c>) delegates to. It owns the DTO <see cref="State"/> and has no game
    /// dependencies, so the real <c>SideFileModel</c> can drive it in a test (reset on a new game, read, write, hand over again when the feature is switched
    /// on). The core writes <c>_schema</c> (<see cref="SchemaVersion"/>) and strips it before <see cref="Read"/>; <see cref="Write"/> returns the data
    /// without it, and the state itself has no version field.
    /// <para>
    /// <b>Reading</b> (FR-10, NFR-3, TSK-023) goes through <see cref="RivalSaveCodec.Read(JsonObject, int, List{string})"/>: the older section is migrated, the
    /// values are checked and clipped, the lists are cut to their limits. <b>A section newer than the mod</b> never reaches <see cref="Read"/> (the core calls
    /// <see cref="ResetToDefaults"/> and writes the stored section back as it is); if it did, the default state would be used.
    /// </para>
    /// <para>
    /// <b>Switching the feature off and on in one session.</b> The core hands a feature that is switched on again the copy of the section that it holds from
    /// the last save, or none (a reset): never the state of the moment the feature went off. For this feature that would lose the attitudes, the history,
    /// the planned and active reactions and the price book of the whole session. So <see cref="Freeze"/> (from <c>OnDisabled</c>) remembers the state, and the first
    /// hand-over after the feature is active again (<see cref="Read"/> or <see cref="ResetToDefaults"/>) takes it instead of the core's copy, provided the
    /// feature is active and the game is the same one (<see cref="UseFreeze"/>). A game that was loaded or started in between, and the menu, drop it
    /// (<see cref="DropFrozen"/>; a hand-over to a feature that is still off is a reset of the whole mod).
    /// </para>
    /// </summary>
    public sealed class RivalSaveSection : ISideFileParticipant
    {
        private readonly string _sectionId;
        private readonly Action<string> _warn;

        // the switch-off: the state of the moment, used once, only when the feature is active again and the game is the same
        private RivalState _frozen;
        private object _frozenGame;
        private Func<bool> _isActive;
        private Func<object> _game;

        /// <param name="sectionId">The id of the section: the feature's id (<c>rival-personalities</c>).</param>
        /// <param name="warn">Receives one English line per problem met while reading (the feature logs them); may be null.</param>
        public RivalSaveSection(string sectionId, Action<string> warn = null)
        {
            _sectionId = sectionId;
            _warn = warn;
            State = new RivalState();
        }

        public string SectionId
        {
            get { return _sectionId; }
        }

        public int SchemaVersion
        {
            get { return RivalSaveCodec.SchemaVersion; }
        }

        /// <summary>The state as it was read, or the default one after a reset. Never null; the object is replaced by <see cref="Read"/> and <see cref="ResetToDefaults"/>, so do not keep it across them.</summary>
        public RivalState State { get; private set; }

        /// <summary>
        /// A new game, no section in the file, a section newer than this build, leaving to the menu: the default state (FR-10 item 3). The first hand-over
        /// after the feature was switched off and on again gives back the state it left with instead (see the class).
        /// </summary>
        public void ResetToDefaults()
        {
            RivalState frozen = TakeFrozen();
            State = frozen ?? new RivalState();
        }

        /// <summary>
        /// The stored section, without <c>_schema</c>. <paramref name="schema"/> below <see cref="SchemaVersion"/> means "migrate" (inside, by
        /// <see cref="RivalStateMigrations"/>); above it, the default state is used. The object passed in is never changed.
        /// </summary>
        public void Read(JsonObject section, int schema)
        {
            RivalState frozen = TakeFrozen();
            if (frozen != null)
            {
                State = frozen;   // the feature came back in the same game: the state it left with is newer than the copy the core kept
                return;
            }

            var problems = new List<string>();
            State = RivalSaveCodec.Read(section, schema, problems);

            if (_warn == null) return;
            foreach (string problem in problems) _warn(problem);
        }

        /// <summary>The data to store, without <c>_schema</c> (the core adds it): a fresh object each time.</summary>
        public JsonObject Write()
        {
            return RivalSaveCodec.Write(State);
        }

        // ---- the switch-off ----

        /// <summary>
        /// Connects the two facts that decide whether a frozen state is still good: whether the feature is active now, and the identity of the game in play
        /// (an object that changes whenever a side file is read, <c>ModSave.FileHeader</c>; null for "never changes"). Without it nothing is ever used.
        /// </summary>
        public void UseFreeze(Func<bool> isActive, Func<object> game)
        {
            _isActive = isActive;
            _game = game;
        }

        /// <summary>The feature was switched off (<c>OnDisabled</c>): remembers the state as it is, for the first hand-over after the feature is active again.</summary>
        public void Freeze()
        {
            _frozen = State;
            _frozenGame = _game == null ? null : _game();
        }

        /// <summary>Forgets the frozen state (a game was loaded or started: it belongs to a game that is gone).</summary>
        public void DropFrozen()
        {
            _frozen = null;
            _frozenGame = null;
        }

        /// <summary>A frozen state waits for the feature to come back.</summary>
        public bool IsFrozen
        {
            get { return _frozen != null; }
        }

        // The frozen state goes to whoever asks first, once, and only if two things hold. The feature is active: a hand-over to an inactive participant is a
        // reset of the whole mod (the menu, a new game) and the frozen state belongs to a game that is over. And the game is the same one: a side file that was read
        // since (even the same save) is a different timeline, and its section, not the frozen state, is the truth.
        private RivalState TakeFrozen()
        {
            RivalState frozen = _frozen;
            object game = _frozenGame;
            _frozen = null;
            _frozenGame = null;
            if (frozen == null) return null;
            if (_isActive == null || !_isActive()) return null;
            return ReferenceEquals(game, _game == null ? null : _game()) ? frozen : null;
        }
    }
}
