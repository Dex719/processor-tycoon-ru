using System;

namespace PTMod.Features.EventSystem
{
    /// <summary>Why the game state became ready: a new game after its fast-forward, or a loaded save (the logic's copy of the core's <c>GameReadyReason</c>).</summary>
    public enum ReadyKind
    {
        NewGame = 0,
        Loaded = 1,
    }

    /// <summary>
    /// The life of the engine around the moments the core announces (design.md "Подписки на события", FR-10, FR-11, FR-13): the game becomes ready after a load or a new
    /// game, the feature is switched off in the middle of a game and on again. The work is done by the parts it is given, in the order the design lays down; the engine
    /// calls it from its handlers of <c>ModTicks.GameReady</c>, <c>IModFeature.OnEnabled</c> / <c>OnDisabled</c> and <c>ModFeatures.StateChanged</c>.
    /// <list type="bullet">
    /// <item><b>A new game</b> (<see cref="OnGameReady"/>, <see cref="ReadyKind.NewGame"/>): whatever the fast-forward left is dropped - no instance, no contribution, an empty
    /// journal (AC-13.1) - the windows are forgotten and the count of days starts from today (FR-10 item 5). Nothing is simulated for the years that were skipped.</item>
    /// <item><b>A load</b> (<see cref="ReadyKind.Loaded"/>): the section has been read already (it checked the numbers and dropped the contributions of the last save). Now the
    /// events that are no longer in the registry are cancelled (their letters closed, the journal written), the running effects are put back with their handlers' <c>Restore</c>,
    /// every waiting choice finds its letter by the tag and the buttons and the lines of the letters are recalculated (AC-10.1, AC-10.3). No day is simulated for the time
    /// since the last processed one (FR-13 item 3).</item>
    /// <item><b>Time the feature stood still</b> is not charged to the player's choices: the deadlines move by the number of days the engine did not process (FR-11 item 4,
    /// AC-11.3). In the middle of a game that is the time since <see cref="OnDisabled"/>; after a load it is the time since the last day the saved state was processed.</item>
    /// <item><b>Switched on again</b> (<see cref="OnEnabled"/>): the effects that did not end meanwhile contribute again (the providers and contributions were never removed,
    /// AC-11.1), those that ended are closed with a journal entry at once, the letters are linked and recalculated.</item>
    /// </list>
    /// Every step runs on its own, so one that fails is reported and the rest still run (NFR-2). Main thread only.
    /// </summary>
    public sealed class EventRuntime
    {
        private readonly EffectRuntime _effects;
        private readonly EventScheduler _scheduler;
        private readonly ChoiceController _choices;
        private readonly EventUiController _ui;
        private readonly SessionGate _gate;
        private readonly Func<int> _today;
        private readonly Action _loadPacks;
        private readonly Action _syncPacks;
        private readonly Action<string, Exception> _onFault;
        private readonly Action<string> _onInfo;

        private int _disabledOn;
        private bool _frozen;

        /// <param name="gate">The gate in front of the section; <see cref="OnGameReady"/> binds it.</param>
        /// <param name="today">Today in game days; 0 or less - there is no game date (the menu).</param>
        /// <param name="loadPacks">Reads the packs that are registered and not read yet (once per session; <c>PackLoader.EnsureLoaded</c>).</param>
        /// <param name="syncPacks">Freezes and thaws the packs by the switches of their features (<c>EffectRuntime.SyncPacks</c>).</param>
        public EventRuntime(EffectRuntime effects, EventScheduler scheduler, ChoiceController choices, EventUiController ui, SessionGate gate, Func<int> today,
            Action loadPacks, Action syncPacks, Action<string, Exception> onFault, Action<string> onInfo)
        {
            if (effects == null) throw new ArgumentNullException("effects");
            if (scheduler == null) throw new ArgumentNullException("scheduler");
            if (choices == null) throw new ArgumentNullException("choices");
            if (ui == null) throw new ArgumentNullException("ui");
            if (gate == null) throw new ArgumentNullException("gate");
            if (today == null) throw new ArgumentNullException("today");
            _effects = effects;
            _scheduler = scheduler;
            _choices = choices;
            _ui = ui;
            _gate = gate;
            _today = today;
            _loadPacks = loadPacks ?? (() => { });
            _syncPacks = syncPacks ?? (() => { });
            _onFault = onFault ?? ((site, error) => { });
            _onInfo = onInfo ?? (line => { });
        }

        /// <summary>The gate in front of the section.</summary>
        public SessionGate Gate
        {
            get { return _gate; }
        }

        /// <summary>The feature is switched off now (it stood still since <see cref="OnDisabled"/> and has not been switched on yet).</summary>
        public bool IsFrozen
        {
            get { return _frozen; }
        }

        /// <summary>The day the feature was switched off; 0 when it is not frozen.</summary>
        public int FrozenSince
        {
            get { return _frozen ? _disabledOn : 0; }
        }

        // ---- the game becomes ready ----

        /// <summary><c>ModTicks.GameReady</c> (the core calls the handler of a feature that is Enabled): the start of a new game, or the state of a load put back to work.</summary>
        public void OnGameReady(ReadyKind kind)
        {
            _gate.Bind();
            _frozen = false;
            int today = _today();

            Step("ready/packs", _loadPacks);
            if (kind == ReadyKind.NewGame) StartNewGame(today);
            else AfterLoad(today);
            Step("ready/packs-sync", _syncPacks);
        }

        private void StartNewGame(int today)
        {
            Step("ready/clear", () =>
            {
                _scheduler.State.Clear();
                _scheduler.History.Clear();
                _effects.Clear();
            });
            Step("ready/ui", _ui.Reset);
            Step("ready/start", () => _scheduler.StartNewGame(today));
        }

        private void AfterLoad(int today)
        {
            int stopped = StoppedDays(today);   // read before anything may touch the saved state

            Step("ready/ui-reset", _ui.Reset);
            Step("ready/restore", () => _effects.AfterLoad(today));
            Step("ready/expire", () => _effects.ExpireEffects(today));   // an old section: what ended while nobody ran the engine does not contribute for one more day
            if (stopped > 0)
            {
                int moved = 0;
                Step("ready/shift", () => moved = _choices.ShiftDeadlines(stopped));
                if (moved > 0) _onInfo("load: the engine stood still for " + stopped + " day(s); the deadlines of " + moved + " choice(s) of the player moved by as much");
            }
            Step("ready/link", _ui.AfterLoad);
        }

        /// <summary>The days between the last day the saved state was processed and today: the time nobody ran the engine (0 right after an ordinary save).</summary>
        private int StoppedDays(int today)
        {
            int last = _scheduler.State.LastProcessedDay;
            if (last == SchedulerState.NoDay || today <= 0 || today <= last) return 0;
            return today - last;
        }

        // ---- the feature is switched off and on ----

        /// <summary>
        /// <c>IModFeature.OnDisabled</c>: the day is remembered. Nothing is dropped and nothing is created again (D10): the providers of the channels, the contributions and the
        /// state stay, the core stops the day tick and passes by the providers of a feature that is off.
        /// </summary>
        public void OnDisabled()
        {
            if (_frozen) return;   // already off: the first day is the one that counts
            int today = _today();
            if (!_gate.Bound || today <= 0)
            {
                _frozen = false;   // nothing of a game is held (the menu, or the game was not ready yet)
                return;
            }
            _frozen = true;
            _disabledOn = today;
        }

        /// <summary>
        /// <c>IModFeature.OnEnabled</c>: on again after <see cref="OnDisabled"/>. The deadlines of the player's choices move by the days the feature stood still (AC-11.3), the
        /// effects whose end date passed meanwhile are closed (AC-11.1: the others keep their date), the letters are linked and recalculated, the packs follow their switches.
        /// Does nothing at the start of a session or for a feature that held no game.
        /// </summary>
        public void OnEnabled()
        {
            if (!_frozen) return;
            _frozen = false;

            int today = _today();
            int days = today > _disabledOn ? today - _disabledOn : 0;
            if (!_gate.Bound || today <= 0) return;

            int moved = 0;
            if (days > 0) Step("enable/shift", () => moved = _choices.ShiftDeadlines(days));
            Step("enable/packs-sync", _syncPacks);   // the packs follow the switches they have now, before the lines under the letters are written
            Step("enable/expire", () => _effects.ExpireEffects(today));
            Step("enable/ui", _ui.AfterEnable);
            _onInfo("switched on again after " + days + " day(s); the deadlines of " + moved + " choice(s) of the player moved by as much");
        }

        /// <summary>
        /// The state of the feature changed (<c>ModFeatures.StateChanged</c> for this feature, or the first day): when the core handed the section over after
        /// <c>GameReady</c> (the feature was off at that moment) the start that <c>GameReady</c> would have made is made now. True if it was.
        /// </summary>
        public bool CompleteLateStart(ReadyKind kind)
        {
            if (!_gate.TakeLate()) return false;
            _onInfo("the section was handed over after the game became ready (the feature was off); the start is made now");
            OnGameReady(kind);
            return true;
        }

        /// <summary>The scene is about to go (a load, a new game, the menu): the state belongs to no game until the next hand-over.</summary>
        public void OnSceneLeaving()
        {
            _gate.Unbind();
            _frozen = false;
        }

        private void Step(string site, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                _onFault(site, ex);
            }
        }
    }
}
