using System;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// What the controller needs of the game and of the player's settings beyond the world and the output: the two hub settings, the pause of time and
    /// the windows the buttons of the mails open. The game layer implements it (<c>DateController</c>, <c>CreatorManager</c>, <c>ModSettings</c>); the
    /// tests use a fake. Main thread only. The calls must not throw.
    /// </summary>
    public interface IOnboardingHost
    {
        /// <summary>The hub setting "Pause on a new step" (<c>pauseOnStep</c>).</summary>
        bool PauseOnStep { get; }

        /// <summary>The hub setting "Reminder hints" (<c>remindersEnabled</c>).</summary>
        bool RemindersEnabled { get; }

        /// <summary>The game time is running (a speed above 0). False when it is paused or when that cannot be read.</summary>
        bool TimeRunning { get; }

        /// <summary>Puts the game on pause (<c>DateController.ManualPause(false)</c>).</summary>
        void PauseTime();

        /// <summary>
        /// Opens the window of a button of a mail (<see cref="OnboardingMachine.ActionOpenCpuDesigner"/>, <see cref="OnboardingMachine.ActionOpenFactoryExpansion"/>).
        /// False when the id is not a window button or the window could not be opened.
        /// </summary>
        bool OpenWindow(string actionId);
    }

    /// <summary>
    /// The brain of the director (design.md, "OnboardingDirector"; FR-2, FR-8, FR-9, FR-11, FR-12, FR-13, FR-14): everything the director decides, without
    /// Unity. The director only subscribes to the events of the core and the game, guards each entry and calls the method of the same name here.
    /// <list type="bullet">
    /// <item><b>The machine follows the state.</b> The state of the tutorial belongs to the save section, which replaces it on every load, new game and
    /// return to the menu. The controller asks for it at each call and builds a new <see cref="OnboardingMachine"/> over it whenever the object is
    /// another one, so a load needs no event.</item>
    /// <item><b>New game.</b> <see cref="NewGame"/> starts the run when the final mode is "on"; "off" is written as a state of its own (mode
    /// <see cref="OnboardingMode.Off"/> with the source <see cref="SourceOff"/>), so that the hub can tell a game that chose to go without the tutorial
    /// from a save made before the mod (<see cref="IsOldSave"/>).</item>
    /// <item><b>Buttons.</b> The windows are the game layer's (<see cref="IOnboardingHost.OpenWindow"/>), the rest goes to
    /// <see cref="OnboardingMachine.HandleAction"/>; a button of a step the content no longer has is ignored.</item>
    /// <item><b>Pause.</b> The mail of a step that asks for it pauses the game when it reaches the inbox (<see cref="PausePolicy"/>), only for the mail the
    /// state knows as the mail of the step (not for a repeat, not for a stale mail of a skipped run).</item>
    /// <item><b>Bankruptcy.</b> <see cref="OnPlayerBankrupt"/> ends a running tutorial with <see cref="OnboardingMachine.Abort"/>.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class OnboardingController
    {
        /// <summary>The source written into the state of a game that started without the tutorial.</summary>
        public const string SourceOff = "off";

        /// <summary>The source of a run started with the toggle of the new game window.</summary>
        public const string SourceMenu = "menu";

        private readonly StepCatalog _catalog;
        private readonly Func<OnboardingState> _stateSource;
        private readonly IOnboardingWorld _world;
        private readonly IOnboardingOutput _output;
        private readonly IOnboardingHost _host;
        private readonly OnboardingState _spare = new OnboardingState();
        private OnboardingMachine _machine;

        /// <param name="catalog">The checked steps; null is the same as an empty catalog.</param>
        /// <param name="stateSource">Gives the state of the game in play (the save section); asked at every call, because a load replaces it. A null answer gives an empty state.</param>
        /// <param name="world">The game as the machine sees it.</param>
        /// <param name="output">Where the mail and the hints go.</param>
        /// <param name="host">The settings, the pause and the windows.</param>
        public OnboardingController(StepCatalog catalog, Func<OnboardingState> stateSource, IOnboardingWorld world, IOnboardingOutput output, IOnboardingHost host)
        {
            if (stateSource == null) throw new ArgumentNullException("stateSource");
            if (world == null) throw new ArgumentNullException("world");
            if (output == null) throw new ArgumentNullException("output");
            if (host == null) throw new ArgumentNullException("host");

            _catalog = catalog ?? StepCatalog.Build(null, null, null);
            _stateSource = stateSource;
            _world = world;
            _output = output;
            _host = host;
        }

        /// <summary>Receives one English line for the log when the controller or the machine does something worth a record; may be null.</summary>
        public Action<string> Log { get; set; }

        /// <summary>
        /// Times <see cref="Poll"/> and <see cref="OnGameDay"/> (TSK-017, NFR-1) while its flag is on; null for no timing. The entries that do no work (the game
        /// loads, fast-forwards or runs a month in one frame, the tutorial is not running) take no sample.
        /// </summary>
        public PerfMeter Perf { get; set; }

        /// <summary>The state of the game in play. Never null.</summary>
        public OnboardingState State
        {
            get { return _stateSource() ?? _spare; }
        }

        /// <summary>The tutorial is running now (mode <see cref="OnboardingMode.Active"/>): the poll, the days and the buttons have work to do.</summary>
        public bool IsRunning
        {
            get { return State.Mode == OnboardingMode.Active; }
        }

        /// <summary>
        /// The game was made before the mod or without its section: no tutorial was ever decided for it. A game that started with the tutorial switched
        /// off is not an old save.
        /// </summary>
        public bool IsOldSave
        {
            get
            {
                OnboardingState state = State;
                return state.Mode == OnboardingMode.Off && state.Source == null && state.StartDate == null;
            }
        }

        /// <summary>The step is behind the player (done or skipped): the answer of the predicate <c>onb.stepDone</c>.</summary>
        public bool IsStepClosed(string stepId)
        {
            return State.IsStepClosed(stepId);
        }

        // ---- the start ----

        /// <summary>
        /// A new game is ready (<c>GameReady(NewGame)</c>) and its final mode is known. On: the run begins (track, first mail, the notice). Off: the
        /// state is empty and marked as a decision.
        /// </summary>
        /// <param name="tutorialOn">The final mode: the toggle of the new game window, or the mode a scenario asked for.</param>
        /// <param name="source">Who started it (<see cref="SourceMenu"/>, <c>scenario:&lt;id&gt;</c>).</param>
        public void NewGame(bool tutorialOn, string source)
        {
            OnboardingMachine machine = Prepare();
            if (!tutorialOn)
            {
                OnboardingState state = State;
                state.Clear();
                state.Source = SourceOff;
                Note("new game: the tutorial is off (" + (source ?? "no source") + ")");
                return;
            }

            machine.Start(_world, _output, string.IsNullOrEmpty(source) ? SourceMenu : source);
            Note("new game: the tutorial " + State.Mode + ", track " + (State.Track ?? "-"));
        }

        /// <summary>A save was loaded (<c>GameReady(Loaded)</c>): the run goes on from the restored state; nothing is sent here.</summary>
        public void Resume()
        {
            Prepare();
            OnboardingState state = State;
            Note("game loaded: the tutorial is " + state.Mode + (state.Mode == OnboardingMode.Active ? ", step " + (state.CurrentStepId ?? "-") : string.Empty));
        }

        // ---- time ----

        /// <summary>
        /// The poll of the real-time loop (twice a second). Cheap and silent unless the tutorial is running, and skipped while the game loads, fast-forwards
        /// or runs a month in one frame (FR-9, NFR-1): then nothing is read and nothing is timed.
        /// </summary>
        public void Poll()
        {
            if (!IsRunning || _world.IsBlocked || _world.IsBatchRun) return;

            PerfMeter perf = Perf;
            if (perf == null || !perf.Enabled)
            {
                Prepare().Poll(_world, _output);
                return;
            }

            long start = perf.Now();
            try
            {
                Prepare().Poll(_world, _output);
            }
            finally
            {
                EndMeasure(perf, PerfMeter.Poll, start);
            }
        }

        /// <summary>A game day passed (<c>TickPhase.DayPassed</c>). Nothing while the game is blocked.</summary>
        public void OnGameDay()
        {
            if (!IsRunning || _world.IsBlocked) return;

            PerfMeter perf = Perf;
            if (perf == null || !perf.Enabled)
            {
                Prepare().OnGameDay(_world, _output);
                return;
            }

            long start = perf.Now();
            try
            {
                Prepare().OnGameDay(_world, _output);
            }
            finally
            {
                EndMeasure(perf, PerfMeter.OnGameDay, start);
            }
        }

        /// <summary>The month batch run ended (<c>ModTicks.BatchEnded</c>).</summary>
        public void OnBatchEnded()
        {
            if (!IsRunning) return;
            Prepare().OnBatchEnded(_world, _output);
        }

        // ---- mail ----

        /// <summary>
        /// A mail of the feature reached the inbox (<c>ModMail.Delivered</c>). Remembers the day of delivery (the reminders count from it) and pauses
        /// the game when the step asks for it. Mail with a tag that is not a tutorial tag, a repeat and a mail the state does not know are ignored.
        /// </summary>
        public void OnMailDelivered(string tag, int mailId, DateTime deliveredOn)
        {
            string stepId;
            bool isRepeat;
            if (!MailTags.TryParse(tag, out stepId, out isRepeat) || isRepeat) return;

            OnboardingMachine machine = Prepare();
            machine.OnMailDelivered(stepId, mailId, deliveredOn);

            OnboardingState state = State;
            StepState st = state.Find(stepId);
            if (st == null || mailId <= 0 || st.MailId != mailId) return;
            if (!MailStillMatters(state.Mode, stepId)) return;

            StepDefinition def = _catalog.Find(stepId);
            if (!PausePolicy.ShouldPause(def, _host.PauseOnStep, _host.TimeRunning)) return;

            _host.PauseTime();
            Note("the game is paused: the mail of '" + stepId + "' arrived");
        }

        /// <summary>
        /// A button of a mail of the tutorial was pressed. The window buttons open their window; the others go to the machine. A button of a step that
        /// is not in the content, of a mail with another tag and an unknown button are ignored (and noted). True when something was done.
        /// </summary>
        public bool OnMailAction(string tag, string actionId)
        {
            string stepId;
            bool isRepeat;
            if (!MailTags.TryParse(tag, out stepId, out isRepeat))
            {
                Note("the button '" + actionId + "' of a mail with the tag '" + tag + "' is ignored");
                return false;
            }

            if (_catalog.Find(stepId) == null)
            {
                Note("the button '" + actionId + "' of the step '" + stepId + "' is ignored: the content has no such step");
                return false;
            }

            if (actionId == OnboardingMachine.ActionOpenCpuDesigner || actionId == OnboardingMachine.ActionOpenFactoryExpansion)
            {
                bool opened = _host.OpenWindow(actionId);
                if (!opened) Note("the window of the button '" + actionId + "' could not be opened");
                return opened;
            }

            bool handled = Prepare().HandleAction(actionId, stepId, _world, _output);
            if (!handled) Note("the button '" + actionId + "' of the step '" + stepId + "' did nothing");
            return handled;
        }

        // ---- the company ----

        /// <summary>The player's company went bankrupt (<c>BankruptcyHandler.OnCompanyWentBankrupt</c>): a running tutorial ends as aborted.</summary>
        public void OnPlayerBankrupt()
        {
            if (!IsRunning) return;
            Prepare().Abort("bankruptcy", _output);
        }

        // ---- commands (the hub, the API) ----

        /// <summary>Ends the tutorial as skipped by the player. False unless it was running.</summary>
        public bool SkipAll(string reason)
        {
            if (!IsRunning) return false;
            return Prepare().SkipAll(string.IsNullOrEmpty(reason) ? "api" : reason, _output);
        }

        /// <summary>Skips the current step. False unless a step is current.</summary>
        public bool SkipCurrentStep()
        {
            if (!IsRunning) return false;
            return Prepare().SkipCurrentStep(_world, _output);
        }

        /// <summary>Sends the mail of the current step again. False unless a step is current and its mail went out.</summary>
        public bool ResendCurrent()
        {
            if (!IsRunning) return false;
            return Prepare().ResendCurrent(_world, _output);
        }

        /// <summary>
        /// Starts the tutorial over in this game, from every mode (the old save and the "off" game included): the steps the player has done are counted
        /// at once and the mail comes for the first one that is not.
        /// </summary>
        public void Restart()
        {
            OnboardingState state = State;
            if (string.Equals(state.Source, SourceOff, StringComparison.Ordinal)) state.Source = null;   // the run now is the player's wish, not the "off" decision
            Prepare().Restart(_world, _output);
        }

        /// <summary>"Step N of M" for the hub and the status of the API.</summary>
        public OnboardingProgress GetProgress()
        {
            return Prepare().GetProgress();
        }

        // ---- helpers ----

        /// <summary>Takes the sample; a tutorial that has ended with this entry writes the month it was timed in, as nothing will come after it.</summary>
        private void EndMeasure(PerfMeter perf, string probe, long start)
        {
            perf.Record(probe, start, _world.GameDate);
            if (!IsRunning) perf.Flush();
        }

        /// <summary>The machine over the current state, with the player's settings applied.</summary>
        private OnboardingMachine Prepare()
        {
            OnboardingState state = State;
            if (_machine == null || !ReferenceEquals(_machine.State, state))
            {
                _machine = new OnboardingMachine(_catalog, state) { Log = Note };
            }
            _machine.RemindersEnabled = _host.RemindersEnabled;
            return _machine;
        }

        /// <summary>
        /// A mail that arrives after the run ended or was skipped says nothing to the player any more: no pause for it. The closing mail is the one
        /// exception, it is sent as the run completes.
        /// </summary>
        private static bool MailStillMatters(OnboardingMode mode, string stepId)
        {
            if (mode == OnboardingMode.Active) return true;
            return mode == OnboardingMode.Completed && string.Equals(stepId, OnboardingMachine.GraduationStepId, StringComparison.Ordinal);
        }

        private void Note(string message)
        {
            Action<string> log = Log;
            if (log != null) log(message);
        }
    }
}
