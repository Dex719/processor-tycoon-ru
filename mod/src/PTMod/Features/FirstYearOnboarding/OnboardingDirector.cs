using System;
using System.Collections;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;
using UnityEngine;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The bridge between the tutorial and the game (TSK-011, TSK-014; design.md, "OnboardingDirector"; FR-2, FR-9, FR-11, NFR-2): it listens to the events of
    /// the core and of the game, keeps the poll of the steps running, and hands every event to <see cref="OnboardingController"/>, which decides. It is an
    /// ordinary object, not a <c>MonoBehaviour</c>, and the implementation behind <see cref="OnboardingService"/> (<see cref="IOnboardingBackend"/>).
    /// <list type="bullet">
    /// <item><b>Subscriptions</b> (<see cref="Install"/>): <c>ModTicks.GameReady</c>, <c>ctx.OnTick(DayPassed)</c>, <c>ModTicks.BatchEnded</c>,
    /// <c>ModMail.Delivered</c> and the handler of the buttons of the mails; on <c>GameReady</c> also <c>BankruptcyHandler.OnCompanyWentBankrupt</c> of the
    /// scene (the event belongs to the instance of the scene and goes with it, so it is subscribed again every time the game is ready).</item>
    /// <item><b>The poll</b> is a coroutine of <c>PTModHost</c> that wakes twice a second of real time (<see cref="PollIntervalSeconds"/>); the controller does
    /// nothing while the game is loading, fast-forwarding or in a month batch run. The coroutine ends when the feature is switched off or the tutorial is
    /// not running, and <see cref="EnsurePolling"/> starts it again at the next start, load, restart or switch-on.</item>
    /// <item><b>The final mode of a new game</b> is computed on <c>GameReady(NewGame)</c>: the mode another feature asked for (<c>PendingMode</c>) wins
    /// over the toggle recorded at "Start" (<see cref="PendingStart"/>); both are used up by it.</item>
    /// <item><b>Guards.</b> Every entry runs in <c>ModHooks.Guard</c>; the director has no counter of errors of its own (the core counts them and switches the
    /// feature off after five).</item>
    /// <item><b>Timing</b> (TSK-017, NFR-1). With the debug flag <see cref="PerfFlag"/> in <c>ptmod_settings.json</c> the controller times <c>Poll</c> and
    /// <c>OnGameDay</c> and the director writes the average and the maximum of each to <c>Player.log</c> once per game month (<see cref="PerfMeter"/>). Without
    /// the flag the cost is one read of the flag per entry.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal sealed class OnboardingDirector : IOnboardingBackend
    {
        /// <summary>The interval of the poll in seconds of real time (FR-4: the answer to the player's action within a second).</summary>
        internal const float PollIntervalSeconds = 0.5f;

        /// <summary>
        /// The debug flag of the timing (<c>"debug": { "first-year-onboarding.perf": true }</c> in <c>ptmod_settings.json</c>; the debug build reads it, the
        /// release build never does).
        /// </summary>
        internal const string PerfFlag = "first-year-onboarding.perf";

        private const string SitePrefix = "OnboardingDirector.";

        private readonly OnboardingController _controller;
        private readonly PerfMeter _perf;
        private readonly Action _pollAction;
        private readonly Action _dayAction;
        private readonly Action _batchAction;
        private BankruptcyWatch _bankruptcy;
        private int _generation;
        private bool _polling;
        private bool _installed;

        /// <param name="catalog">The checked steps (an empty catalog is fine: the tutorial never starts).</param>
        internal OnboardingDirector(StepCatalog catalog)
        {
            catalog = catalog ?? StepCatalog.Build(null, null, null);

            var world = new GameOnboardingWorld();
            var source = new GamePlaceholderSource(catalog.Config.DefaultDaysFallback);
            var mailer = new OnboardingMailer(new GameMailTexts(), source, world.Evaluate, () => catalog);

            _perf = new PerfMeter(
                () => ModSettings.Debug(PerfFlag),
                System.Diagnostics.Stopwatch.GetTimestamp,
                System.Diagnostics.Stopwatch.Frequency,
                WritePerf);

            _controller = new OnboardingController(catalog, () => OnboardingFeature.SaveParticipant.State, world, mailer, new GameOnboardingHost())
            {
                Log = line => ModLog.Info(OnboardingFeature.FeatureId, line),
                Perf = _perf
            };

            // onb.stepDone reads the state of the tutorial of the game in play.
            OnboardingPredicates.Facts.StepDoneSource = _controller.IsStepClosed;

            _pollAction = () => _controller.Poll();
            _dayAction = () =>
            {
                _controller.OnGameDay();
                EnsurePolling();   // a poll that could not start earlier (the host was not up) starts with the first day that passes
            };
            _batchAction = () => _controller.OnBatchEnded();
        }

        /// <summary>The brain: the hub page asks it for the state and sends the commands of its buttons to it.</summary>
        internal OnboardingController Controller
        {
            get { return _controller; }
        }

        // ---- subscriptions ----

        /// <summary>Subscribes to the events of the core and registers the implementation of <see cref="OnboardingService"/>. Once per session.</summary>
        internal void Install(FeatureContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (_installed) return;
            _installed = true;

            ctx.OnTick(TickPhase.DayPassed, OnDayPassed);
            ModTicks.GameReady += OnGameReady;
            ModTicks.BatchEnded += OnBatchEnded;
            ModMail.Delivered += OnMailDelivered;
            ModMail.RegisterActionHandler(OnboardingFeature.FeatureId, OnMailAction);
            OnboardingFeature.ActiveChanged += OnActiveChanged;
            OnboardingService.RegisterBackend(this);
        }

        // ---- the switch of the feature ----

        /// <summary>
        /// The feature was switched on or off (the hub). On: the poll starts and, when the game is up already, the event of the scene is subscribed (the
        /// core does not raise <c>GameReady</c> for a feature switched on in the middle of a scene). Off: the poll stops on its next wake-up and the event
        /// of the scene is let go; the state stays as it is.
        /// </summary>
        private void OnActiveChanged(bool active)
        {
            Guard("OnActiveChanged", () =>
            {
                if (active)
                {
                    if (ModTicks.IsGameReady) AttachBankruptcy();
                    EnsurePolling();
                    return;
                }

                _generation++;   // the loop in flight sees another generation and ends
                _polling = false;
                DetachBankruptcy();
                _perf.Flush();   // the month in progress is written; nothing is held back when the feature stops
            });
        }

        // ---- events of the core ----

        private void OnGameReady(GameReadyReason reason)
        {
            Guard("OnGameReady", () =>
            {
                if (!OnboardingFeature.IsActive) return;

                if (reason == GameReadyReason.NewGame)
                {
                    string requestedBy;
                    PendingMode requested = OnboardingService.TakePendingMode(out requestedBy);
                    bool tutorialOn = PendingModeBook.ResolveTutorial(requested, PendingStart.HasValue, PendingStart.TutorialOn);
                    PendingStart.Clear();

                    string source = requested != PendingMode.Allowed && !string.IsNullOrEmpty(requestedBy) ? requestedBy : OnboardingController.SourceMenu;
                    _controller.NewGame(tutorialOn, source);
                }
                else
                {
                    OnboardingService.ResetPendingMode();
                    PendingStart.Clear();
                    _controller.Resume();
                }

                AttachBankruptcy();
                EnsurePolling();
            });
        }

        private void OnDayPassed()
        {
            Guard("OnDayPassed", _dayAction);
        }

        private void OnBatchEnded()
        {
            Guard("OnBatchEnded", _batchAction);
        }

        private void OnMailDelivered(ModMailRecord record)
        {
            if (record == null || !string.Equals(record.FeatureId, OnboardingFeature.FeatureId, StringComparison.Ordinal)) return;   // somebody else's mail

            Guard("OnMailDelivered", () =>
            {
                if (record.DeliveredOn.HasValue) _controller.OnMailDelivered(record.Tag, record.MailId, record.DeliveredOn.Value);
            });
        }

        private void OnMailAction(ModMailRecord record, string actionId)
        {
            if (record == null) return;

            Guard("OnMailAction", () =>
            {
                _controller.OnMailAction(record.Tag, actionId);
            });
        }

        private void OnPlayerBankrupt()
        {
            Guard("OnPlayerBankrupt", () =>
            {
                if (OnboardingFeature.IsActive) _controller.OnPlayerBankrupt();
            });
        }

        // ---- the poll ----

        /// <summary>
        /// Starts the poll unless it runs, the feature is off or the tutorial is not running. Safe to call whenever something may have started the run
        /// (the game is ready, a restart, the feature switched on).
        /// </summary>
        internal void EnsurePolling()
        {
            if (_polling || !OnboardingFeature.IsActive || !_controller.IsRunning) return;

            PTModHost host = PTModHost.Instance;
            if (host == null) return;   // before the host is up; GameReady comes later and calls this again

            _generation++;
            Coroutine routine = host.Run(PollLoop(_generation));
            _polling = routine != null;
        }

        private IEnumerator PollLoop(int generation)
        {
            try
            {
                while (true)
                {
                    yield return new WaitForSecondsRealtime(PollIntervalSeconds);

                    if (generation != _generation || !OnboardingFeature.IsActive) yield break;
                    if (!_controller.IsRunning) yield break;   // finished, skipped, aborted or no game: nothing to watch until the next start

                    Guard("Poll", _pollAction);
                }
            }
            finally
            {
                if (generation == _generation) _polling = false;
            }
        }

        // ---- the bankruptcy event of the scene ----

        private void AttachBankruptcy()
        {
            try
            {
                if (_bankruptcy == null) _bankruptcy = new BankruptcyWatch();
                if (!_bankruptcy.Attach(OnPlayerBankrupt)) ModLog.Debug(OnboardingFeature.FeatureId, "the bankruptcy handler of the scene is not there yet");
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "AttachBankruptcy", ex);
            }
        }

        private void DetachBankruptcy()
        {
            try
            {
                if (_bankruptcy != null) _bankruptcy.Detach();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "DetachBankruptcy", ex);
            }
        }

        // ---- IOnboardingBackend: the commands of the API and of the hub ----

        public OnboardingStatus GetStatus()
        {
            return OnboardingApiBridge.ToApi(_controller.GetProgress());
        }

        public void SkipAll(string reason)
        {
            _controller.SkipAll(reason);
        }

        public void SkipCurrentStep()
        {
            _controller.SkipCurrentStep();
        }

        public void ResendCurrent()
        {
            _controller.ResendCurrent();
        }

        public void Restart()
        {
            _controller.Restart();
            EnsurePolling();
        }

        // ---- helpers ----

        private static void WritePerf(string line, bool overBudget)
        {
            if (overBudget) ModLog.Warn(OnboardingFeature.FeatureId, line);
            else ModLog.Info(OnboardingFeature.FeatureId, line);
        }

        private static void Guard(string site, Action body)
        {
            ModHooks.Guard(OnboardingFeature.FeatureId, SitePrefix + site, body);
        }

        /// <summary>
        /// The subscription to <c>BankruptcyHandler.OnCompanyWentBankrupt</c> of the scene. The game's classes are touched only inside the methods marked
        /// not to be inlined, so a class that does not match the game fails in a method the director guards.
        /// </summary>
        private sealed class BankruptcyWatch
        {
            private object _handler;
            private Action<ICompany> _callback;

            /// <summary>Subscribes to the handler of the current scene (and lets go of the previous one). False when the scene has no handler.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal bool Attach(Action onPlayerBankrupt)
            {
                Detach();

                GameBankruptcyHandler handler = GameBankruptcyHandler.Instance;
                if (handler == null) return false;

                _callback = company =>
                {
                    if (company != null && ModCompanies.IsPlayer(company)) onPlayerBankrupt();
                };
                handler.OnCompanyWentBankrupt += _callback;
                _handler = handler;
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal void Detach()
            {
                var handler = _handler as GameBankruptcyHandler;
                if (handler != null && _callback != null) handler.OnCompanyWentBankrupt -= _callback;
                _handler = null;
                _callback = null;
            }
        }
    }
}
