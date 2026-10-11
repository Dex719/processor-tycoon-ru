using System;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Stands between the side-file service of the core and the section <c>event-system</c> (<c>EventSaveSection</c>) and keeps the state of the running game from being
    /// replaced by a hand-over the core makes at the wrong moment.
    /// <para>
    /// The core hands a section out in three situations: a save is loaded, a game starts or the menu is shown (all before <c>ModTicks.GameReady</c>, the game is not
    /// ready), and a feature that was switched off comes back (after <c>GameReady</c>). In the last case it gives the participant the text it last remembered - the section
    /// as it was read, or as it was written by the last save - or, with nothing remembered, <c>ResetToDefaults</c>. For a feature that has run since <c>GameReady</c> that
    /// is older than what it holds in memory, or empty: the effects that were frozen when it was switched off (D10, AC-11.1) would be lost. So a hand-over after
    /// <c>GameReady</c> is ignored while the state is <see cref="Bound"/> to the game in play. A feature that was off at <c>GameReady</c> holds nothing of this game; it takes the
    /// hand-over and reports it with <see cref="TakeLate"/>, and the lifecycle then does what <c>GameReady</c> would have done (<see cref="EventRuntime"/>).
    /// </para>
    /// Main thread only.
    /// </summary>
    public sealed class SessionGate : ISideFileParticipant
    {
        private readonly ISideFileParticipant _inner;
        private readonly Func<bool> _gameReady;
        private readonly Action<string> _info;
        private bool _bound;
        private bool _late;

        /// <param name="inner">The section (<c>EventSaveSection</c>).</param>
        /// <param name="gameReady">Whether <c>ModTicks.GameReady</c> has been raised in the current scene (<c>ModTicks.IsGameReady</c>).</param>
        /// <param name="info">A line for the log when a hand-over is ignored; may be null.</param>
        public SessionGate(ISideFileParticipant inner, Func<bool> gameReady, Action<string> info = null)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            if (gameReady == null) throw new ArgumentNullException("gameReady");
            _inner = inner;
            _gameReady = gameReady;
            _info = info ?? (line => { });
        }

        /// <summary>The state in memory belongs to the game in play: it was given its data or reset before <c>GameReady</c> and has run since.</summary>
        public bool Bound
        {
            get { return _bound; }
        }

        /// <summary>The section was handed over after <c>GameReady</c> to a state that was not bound: the lifecycle still has to finish the start (<see cref="TakeLate"/>).</summary>
        public bool HasLateData
        {
            get { return _late; }
        }

        /// <summary><c>ModTicks.GameReady</c> reached the feature: the state is the game's from now on.</summary>
        public void Bind()
        {
            _bound = true;
            _late = false;
        }

        /// <summary>The scene is left (a load, a new game, the menu): the state belongs to no game until the next hand-over.</summary>
        public void Unbind()
        {
            _bound = false;
            _late = false;
        }

        /// <summary>True once after a hand-over that came after <c>GameReady</c>; the state is bound to the game from then on.</summary>
        public bool TakeLate()
        {
            if (!_late) return false;
            _late = false;
            _bound = true;
            return true;
        }

        public string SectionId
        {
            get { return _inner.SectionId; }
        }

        public int SchemaVersion
        {
            get { return _inner.SchemaVersion; }
        }

        public void ResetToDefaults()
        {
            if (Ignore("a reset")) return;
            _inner.ResetToDefaults();
            Taken();
        }

        public void Read(JsonObject section, int schema)
        {
            if (Ignore("a section")) return;
            _inner.Read(section, schema);
            Taken();
        }

        public JsonObject Write()
        {
            return _inner.Write();
        }

        private bool Ignore(string what)
        {
            if (!_bound || !_gameReady()) return false;
            _info("save: " + what + " handed over after the game became ready is ignored, the state in memory is the newer one");
            return true;
        }

        private void Taken()
        {
            if (_gameReady())
            {
                _late = true;
                return;
            }
            _bound = false;
            _late = false;
        }
    }
}
