using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The "last chance" in the game (design.md, <c>BailoutService</c>; FR-8): the offer on the last day of the default, the accepting, the refusal and the
    /// share of the investor. The rules are <see cref="BailoutEngine"/>'s (pure logic with the tests); this class adds what belongs to the game: the reputation,
    /// the event of <see cref="RecoveryApi"/>, the notification when the investor contract ends, and the batch run of a month.
    /// <list type="bullet">
    /// <item><b>The window</b> is shown by whatever is registered in <see cref="Presenter"/> (the modal window of TSK-015). While there is none, nothing is
    /// held and the bankruptcy goes the game's way.</item>
    /// <item><b>The batch run</b> (<c>ModTicks.IsBatchRun</c>, a month in one frame): the counter is held, the window opens on <c>ModTicks.BatchEnded</c>
    /// (the feature subscribes <see cref="OnBatchEnded"/>).</item>
    /// <item><b>The month</b>: the payment to the investor (<see cref="OnMonth"/>, <c>TickPhase.LateMonthPassed</c>).</item>
    /// </list>
    /// No assisted flag is set by a rescue (owner's decision).
    /// </summary>
    internal sealed class BailoutService
    {
        private readonly BailoutEngine _engine;

        public BailoutService(RecoveryEnvironment env, LoanService loans)
        {
            _engine = new BailoutEngine(env, loans.Book, () => ModTicks.IsBatchRun, LowerReputation);
            _engine.Accepted += OnAccepted;
            _engine.InvestorEnded += OnInvestorEnded;
        }

        /// <summary>The rules of the offer.</summary>
        public BailoutEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>
        /// The window that shows the offer (TSK-015 sets it when the window is built). Null: the offer is never made and the counter of the default is
        /// never held. The window calls <see cref="Accept"/>, <see cref="Decline"/> or <see cref="Dismissed"/>.
        /// </summary>
        public IBailoutPresenter Presenter
        {
            get { return _engine.Presenter; }
            set { _engine.Presenter = value; }
        }

        /// <summary>The offer window is open now.</summary>
        public bool IsOfferOpen
        {
            get { return _engine.IsOfferOpen; }
        }

        /// <summary>The numbers of the offer as they are now; null when there is nothing to offer.</summary>
        public BailoutOffer CurrentOffer()
        {
            return _engine.BuildOffer();
        }

        /// <summary>
        /// The patch asks on the last day of the default: hold the counter and show the offer? True when the offer is open, waits for the end of the
        /// batch, or has just opened.
        /// </summary>
        public bool OnHoldRequested()
        {
            return _engine.TryHold();
        }

        /// <summary><c>ModTicks.BatchEnded</c>: opens the offer that waited for the end of the batch run.</summary>
        public void OnBatchEnded()
        {
            _engine.OnBatchEnded();
        }

        /// <summary>The player accepted a way (FR-8 item 3). False when nothing was done.</summary>
        public bool Accept(BailoutKind kind)
        {
            return _engine.Accept(kind == BailoutKind.RescueLoan ? BailoutChoice.RescueLoan : BailoutChoice.InvestorShare);
        }

        /// <summary>The player declined: the counter is not held again, the bankruptcy follows on the next tick (FR-8 item 4).</summary>
        public void Decline()
        {
            _engine.Decline();
        }

        /// <summary>The window was closed with the cross: not a refusal, the offer comes back on the next tick that holds the counter.</summary>
        public void Dismissed()
        {
            _engine.OfferDismissed();
        }

        /// <summary>The first of the month: the payment to the investor.</summary>
        public void OnMonth()
        {
            _engine.OnMonth();
        }

        /// <summary>A save was loaded, a new game started or the feature was switched: nothing is open any more.</summary>
        public void ResetSession()
        {
            _engine.ResetSession();
        }

        // ---- callbacks of the engine ----

        private void LowerReputation(double delta, string reasonKey)
        {
            ModReputation.Add(PlayerFinanceAdapter.PlayerCompany(), (float)delta, reasonKey);
        }

        private static void OnAccepted(BailoutChoice choice)
        {
            RecoveryApi.RaiseBailoutAccepted(choice == BailoutChoice.RescueLoan ? BailoutKind.RescueLoan : BailoutKind.InvestorShare);
        }

        private static void OnInvestorEnded()
        {
            ModNotify.ShowKey(RecoveryApi.FeatureId, "rec.notify.investorEnded");
        }
    }
}
