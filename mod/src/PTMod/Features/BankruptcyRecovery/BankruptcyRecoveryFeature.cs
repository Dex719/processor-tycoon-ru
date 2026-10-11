using System;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The entry point of bankruptcy-recovery (design.md, "BankruptcyRecoveryFeature"): the recovery menu, the startup credit line, the bridge loan and
    /// the "last chance" offer for the player's company. The feature changes no vanilla threshold or formula; it moves the player's money through
    /// <c>CapitalLedger</c> and holds one moment of the game's bankruptcy cycle (patches P1-P4).
    /// <para>
    /// Built so far: the identity for the hub, the five switches of the parts, the section of the side save file, the numbers of <c>balance.json</c>, the
    /// <see cref="RecoveryApi"/> service (TSK-001, TSK-002); the finances of the player and the capital ledger (TSK-007); the loans, the bridge loan and the
    /// credit line with their ticks (TSK-008, TSK-009); the patches and the self-check (TSK-010); the last chance (TSK-011); the asset sale (TSK-012); the rescue
    /// menu with its four tabs (<see cref="RecoveryWindow"/>, TSK-013, TSK-014); the window of the offer, the letters, the page in the hub (<see cref="BailoutOfferWindow"/>,
    /// <see cref="RecoveryMonitor"/>, <see cref="RecoveryHubPage"/>, TSK-015); the safe switch-off (the question of the hub and the settlement of what is owed,
    /// <see cref="DisableSettlementService"/>, TSK-022). There are no error counters or <c>Enabled</c> flags of its own: the core keeps them.
    /// </para>
    /// <para>
    /// <b>The constructor</b> runs for every discovered feature, also for one the player switched off, while <c>Register</c> runs for the enabled ones only.
    /// So the ledger backend is registered here: <c>CapitalLedger</c> must work with this feature off, other features pay through it. The constructor touches
    /// no game object. The services are made here too (they read the game only when a tick or a window asks).
    /// </para>
    /// </summary>
    [ModFeature]
    public sealed class BankruptcyRecoveryFeature : IModFeature
    {
        /// <summary>The slug of the feature: the key of its content, strings, settings and save section.</summary>
        public const string FeatureId = RecoveryApi.FeatureId;

        private readonly RecoverySaveParticipant _participant = new RecoverySaveParticipant();
        private readonly PlayerFinanceAdapter _finance = new PlayerFinanceAdapter();
        private readonly RecoveryJournal _journal;
        private readonly RecoveryEnvironment _env;
        private readonly LoanService _loans;
        private readonly BridgeLoanService _bridgeLoans;
        private readonly StartupCreditLineService _creditLine;
        private readonly BailoutService _bailout;
        private readonly AssetSaleService _assets;
        private readonly DefaultCycleEngine _cycle;
        private readonly RecoveryMonitor _monitor;
        private readonly BailoutOfferWindow _offerWindow;
        private readonly DisableSettlementService _settlement;

        private RecoveryBalance _balance = RecoveryBalance.Defaults();
        private bool _balanceLoaded;

        public BankruptcyRecoveryFeature()
        {
            _journal = new RecoveryJournal(() => _participant.State, () => _finance.Today);
            CapitalLedger.SetBackend(new CapitalLedgerBackend(_finance, _journal));

            _env = new RecoveryEnvironment(
                _finance,
                new LedgerMoney(_finance, () => Balance.bailout.investor.expenseOrigin),
                _journal,
                () => _participant.State,
                () => Balance,
                RecoverySettings.IsEnabled,
                message => ModLog.Warn(FeatureId, message));

            _loans = new LoanService(_env);
            _bridgeLoans = new BridgeLoanService(_env, _loans);
            _creditLine = new StartupCreditLineService(_env, _loans);
            _bailout = new BailoutService(_env, _loans);
            _assets = new AssetSaleService(_env);
            _cycle = new DefaultCycleEngine(_env, _bailout.Engine);
            _monitor = new RecoveryMonitor(_env);
            _offerWindow = new BailoutOfferWindow(_bailout);
            _settlement = new DisableSettlementService(_env);
        }

        /// <summary>The running feature; set in <see cref="Register"/>. Check <c>ModFeatures.IsActive(FeatureId)</c> before using what it holds.</summary>
        internal static BankruptcyRecoveryFeature Instance { get; private set; }

        /// <summary>
        /// The numbers of the feature (<c>balance.json</c> after the range checks). Read on the first use or in <see cref="OnEnabled"/>, whichever
        /// comes first; a broken file or field gives the defaults of the code. Never null.
        /// </summary>
        internal RecoveryBalance Balance
        {
            get
            {
                EnsureBalance();
                return _balance;
            }
        }

        /// <summary>
        /// The state of the save in play (the section "bankruptcy-recovery"). Never null. The object is replaced when a save is loaded, a new game
        /// starts or the game goes back to the menu: read it through the feature every time, do not keep the reference.
        /// </summary>
        internal RecoveryState State
        {
            get { return _participant.State; }
        }

        /// <summary>The player's finances (design: <c>IPlayerFinance</c>).</summary>
        internal IPlayerFinance Finance
        {
            get { return _finance; }
        }

        /// <summary>What the engines stand on: the finances, the money, the journal, the state, the balance and the switches.</summary>
        internal RecoveryEnvironment Environment
        {
            get { return _env; }
        }

        /// <summary>The journal of the money operations of the feature (the last 200).</summary>
        internal RecoveryJournal Journal
        {
            get { return _journal; }
        }

        /// <summary>The loans (bridge, term, rescue): the monthly payments and the early repayment.</summary>
        internal LoanService Loans
        {
            get { return _loans; }
        }

        internal BridgeLoanService BridgeLoans
        {
            get { return _bridgeLoans; }
        }

        internal StartupCreditLineService CreditLine
        {
            get { return _creditLine; }
        }

        /// <summary>The last chance: the offer, its acceptance and refusal, the share of the investor. Its presenter is the window of the offer.</summary>
        internal BailoutService Bailout
        {
            get { return _bailout; }
        }

        internal AssetSaleService Assets
        {
            get { return _assets; }
        }

        /// <summary>The moments of the bankruptcy cycle the patches P1 and P2 take part in.</summary>
        internal DefaultCycleEngine Cycle
        {
            get { return _cycle; }
        }

        /// <summary>The letters of the warning and of the default.</summary>
        internal RecoveryMonitor Monitor
        {
            get { return _monitor; }
        }

        /// <summary>The safe switch-off: the question before it and the settlement of what is owed (FR-12).</summary>
        internal DisableSettlementService Settlement
        {
            get { return _settlement; }
        }

        // ---- IModFeature ----

        public string Id
        {
            get { return FeatureId; }
        }

        public string NameKey
        {
            get { return "rec.feature.name"; }
        }

        public string DescriptionKey
        {
            get { return "rec.feature.description"; }
        }

        /// <summary>No hard dependencies: the feature reads the core only.</summary>
        public string[] DependsOn
        {
            get { return new string[0]; }
        }

        public bool EnabledByDefault
        {
            get { return true; }
        }

        /// <summary>The feature can be switched in the middle of a game: its patches go out at once and its handlers are skipped (FR-3 item 5).</summary>
        public bool SupportsHotToggle
        {
            get { return true; }
        }

        /// <summary>
        /// Declares the five switches, the save participant, the service of <see cref="RecoveryApi"/>, the patches, the ticks and the handlers of the
        /// game events, and last runs the self-check: a member of the game that is missing makes the feature unavailable (the exception is the core's
        /// signal, see <see cref="RecoverySelfCheck"/>). Runs once per session. The question before the switch-off is declared here too
        /// (<c>ctx.ConfirmBeforeDisable</c>).
        /// </summary>
        public void Register(FeatureContext ctx)
        {
            Instance = this;
            RecoverySettings.Declare(ctx);
            ctx.SaveParticipant(_participant);
            RecoveryApi.SetService(new RecoveryService(this));

            ctx.Patch(typeof(BankruptcyPatches));                                  // P1-P4

            ctx.OnTick(TickPhase.LateTick, _creditLine.OnDay);                     // the auto draw, before the game checks for bankruptcy on DayPassed
            ctx.OnTick(TickPhase.LateTick, _monitor.OnDay);                        // the letter "default risk"
            ctx.OnTick(TickPhase.LateMonthPassed, _loans.OnMonth);                 // the payment of every loan
            ctx.OnTick(TickPhase.LateMonthPassed, _creditLine.OnMonth);            // the interest, the repayment, the end of the term
            ctx.OnTick(TickPhase.LateMonthPassed, _bailout.OnMonth);               // the share of the investor

            _bailout.Presenter = _offerWindow;                                     // the offer window: without it the bankruptcy is the game's own
            ModMail.RegisterActionHandler(FeatureId, RecoveryMonitor.OnMailAction); // the action "Recovery options" of both letters
            ctx.HubPage(RecoveryHubPage.TitleKey, RecoveryHubPage.Build);          // the page in the hub: the way to the menu at any moment
            ctx.ConfirmBeforeDisable(_settlement.BuildConfirmation);               // the hub asks before the switch-off when something is owed (FR-12)

            ModTicks.GameReady += OnGameReady;                                     // NewGame: open the line; Loaded: the same for an old save
            ModTicks.BatchEnded += OnBatchEnded;                                   // the offer that waited for the end of a month in one frame
            ModSettings.Changed += OnSettingChanged;                               // the part "startup credit" switched on in the middle of a game
            ModSave.AfterRead += OnSaveRead;                                       // the marks of an earlier switch-off are applied to the state that was read
            ModFeatures.StateChanged += OnFeatureStateChanged;                     // after a switch-on the core hands the section over: only then is the state the real one

            TargetReport report = RecoverySelfCheck.Run();
            if (!report.AllFound) throw new InvalidOperationException(report.FailureText);   // the core's signal: the feature is unavailable until the game restarts
        }

        /// <summary>
        /// The feature became active: reads the numbers of <c>balance.json</c> and, when it is switched on in the middle of a game, opens the credit line
        /// if the first years of the company are not over (FR-7 item 8). At the start of the game the line is opened by <c>GameReady</c>.
        /// </summary>
        public void OnEnabled()
        {
            ModHooks.Guard(FeatureId, "feature/enabled", Enabled);
        }

        /// <summary>
        /// The feature stopped, by the player (the hub after the question, or <c>ModFeatures.SetEnabled</c> from code, which asks nothing). What the player still owes
        /// through the feature turns into ordinary debt of the balance (<see cref="DisableSettlementService.Settle"/>, FR-12, D9): the loans, the drawn part of the
        /// credit line and what the investor may still take are closed and taken from the balance in one operation. The counter of the rescues and the date of the last
        /// bridge loan stay, the credit line that was closed never opens again, so switching off and on gives no new money. Then the state is kept for the moment the
        /// feature is switched on again (the core would hand back its older copy), and what belongs to the session (an offer that was open) is forgotten. The
        /// registrations stay, the core skips an inactive feature and <see cref="RecoveryApi"/> answers like an empty game.
        /// <para>
        /// Runs under the guard of the feature, which lets a switched-off feature run its own cleanup but not a faulted or unavailable one: a feature that the circuit
        /// breaker stopped settles nothing and keeps its obligations in the save as they are.
        /// </para>
        /// </summary>
        public void OnDisabled()
        {
            ModHooks.Guard(FeatureId, "feature/disabled", () =>
            {
                RecoveryWindow.Close();      // the core closes the windows of a stopped feature as well; this is for the state of the offer
                _offerWindow.Close();
                _bailout.ResetSession();
                _settlement.Settle("disabled");
                _participant.Freeze();
            });
        }

        // ---- handlers ----

        private void Enabled()
        {
            EnsureBalance();
            RecoverySelfCheck.VerifyPatches();
            if (!ModTicks.IsGameReady) return;
            _env.ResetCache();
            _creditLine.EnsureOpened();
        }

        // A save was loaded or a new game started (after its fast-forward): the session flags and the month cache start again, and the line opens
        // if it never did and the first years of the company are not over.
        private void OnGameReady(GameReadyReason reason)
        {
            _participant.DropFrozen();   // a state kept for a switch-on belongs to a game that is gone
            _participant.Reconcile();
            _env.ResetCache();
            _bailout.ResetSession();
            _creditLine.EnsureOpened();
#if PTMOD_DEBUG
            RecoveryDebugActions.LogThreshold(this, reason);   // TSK-003 (the spike): the threshold on the first day of the game, only with the flag debug.recovery
#endif
        }

        private void OnBatchEnded()
        {
            _bailout.OnBatchEnded();
        }

        // The feature is Enabled again. The core raises this after OnEnabled, and the section is handed over to the participant in between (a game that was loaded while
        // the feature was off gives it the section of that save only now), so the line is looked at once more on the state that is really there.
        private void OnFeatureStateChanged(string featureId, FeatureState state)
        {
            if (featureId != FeatureId || state != FeatureState.Enabled || !ModTicks.IsGameReady) return;
            _env.ResetCache();
            _creditLine.EnsureOpened();
        }

        // Every participant has its data after a load: the marks of a settlement (the flags of the save are read by now) are applied once more, in case the flags
        // came after the section.
        private void OnSaveRead()
        {
            _participant.Reconcile();
        }

        private void OnSettingChanged(string featureId, string key)
        {
            if (featureId != FeatureId || key != RecoveryParams.StartupCredit) return;
            if (!ModTicks.IsGameReady || !ModFeatures.IsActive(FeatureId)) return;
            ModHooks.Guard(FeatureId, "settings/startupCredit", () => _creditLine.EnsureOpened());
        }

        // ---- internals ----

        private void EnsureBalance()
        {
            if (_balanceLoaded) return;
            _balanceLoaded = true;   // first: a failure must not be retried on every read
            _balance = RecoveryContent.LoadBalance();
        }
    }
}
