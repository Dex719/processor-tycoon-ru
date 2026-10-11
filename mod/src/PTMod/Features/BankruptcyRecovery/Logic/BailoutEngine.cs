using System;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>The two ways to accept the "last chance" offer (FR-8); the API enum <c>BailoutKind</c> is the game side's name for the same two.</summary>
    internal enum BailoutChoice
    {
        /// <summary>A rescue loan: the amount over 36 months at a high rate.</summary>
        RescueLoan,

        /// <summary>The investor share: a part of the monthly profit for 60 months, capped.</summary>
        InvestorShare,
    }

    /// <summary>
    /// What the offer window shows (FR-8 item 2): the amount, the balance after it and the terms of both ways. A snapshot taken when the offer opens.
    /// </summary>
    internal sealed class BailoutOffer
    {
        public BailoutOffer(double amount, double balanceAfter, int offersUsed, int offersAllowed, int loanMonths, double loanAnnualRate,
            double loanEffectiveRate, double loanPayment, bool investorAvailable, double investorShare, int investorMonths, double investorCap)
        {
            Amount = amount;
            BalanceAfter = balanceAfter;
            OffersUsed = offersUsed;
            OffersAllowed = offersAllowed;
            LoanMonths = loanMonths;
            LoanAnnualRate = loanAnnualRate;
            LoanEffectiveRate = loanEffectiveRate;
            LoanPayment = loanPayment;
            InvestorAvailable = investorAvailable;
            InvestorShare = investorShare;
            InvestorMonths = investorMonths;
            InvestorCap = investorCap;
        }

        /// <summary>The money the offer gives, <c>B = τ × D − balance</c>.</summary>
        public double Amount { get; private set; }

        /// <summary>The balance after the offer is accepted, <c>+τ × D</c>.</summary>
        public double BalanceAfter { get; private set; }

        /// <summary>How many offers the player accepted so far, and how many the difficulty allows.</summary>
        public int OffersUsed { get; private set; }

        public int OffersAllowed { get; private set; }

        /// <summary>The rescue loan: the term, the rate of the file, the rate with the multiplier of <c>bank.interest</c>, the monthly payment.</summary>
        public int LoanMonths { get; private set; }

        public double LoanAnnualRate { get; private set; }

        public double LoanEffectiveRate { get; private set; }

        public double LoanPayment { get; private set; }

        /// <summary>The investor share can be chosen: there is no investor contract running (a second one is not offered).</summary>
        public bool InvestorAvailable { get; private set; }

        /// <summary>The investor's part of the monthly profit (0..1), the term in months, the ceiling of everything he gets.</summary>
        public double InvestorShare { get; private set; }

        public int InvestorMonths { get; private set; }

        public double InvestorCap { get; private set; }
    }

    /// <summary>
    /// Shows the offer to the player (the modal window of TSK-015). Returns true when the window is up; false (or an exception) means it could
    /// not be built, and the engine lets the bankruptcy go its own way (NFR-2).
    /// </summary>
    internal interface IBailoutPresenter
    {
        bool Open(BailoutOffer offer);
    }

    /// <summary>
    /// The "last chance" (FR-8, design.md <c>BailoutService</c> without the game): on the 120th day of the default the game declares the bankruptcy; the
    /// feature holds the counter and offers a rescue instead, a limited number of times per game. This class decides whether to hold
    /// (<see cref="TryHold"/>), builds the offer, applies the choice (<see cref="Accept"/>), remembers a refusal (<see cref="Decline"/>), and serves the
    /// investor contract every month (<see cref="OnMonth"/>).
    /// <list type="bullet">
    /// <item>The offer opens once per request of the patch; in a batch run (a month in one frame) the counter is held and the window opens after the batch
    /// (<see cref="OnBatchEnded"/>), and closing the window with the cross is not a refusal: the next tick asks again.</item>
    /// <item>Without a presenter (the window is not built) or when it fails, nothing is held: the vanilla bankruptcy goes on, and the offer is not tried
    /// again in this episode.</item>
    /// <item>Accepting credits the amount, registers the loan or the contract, counts the offer, lowers the reputation, and cancels the default with the
    /// game's own method. No assisted flag is set (owner's decision).</item>
    /// </list>
    /// The flags "the offer is open", "waiting for the batch" and "the window failed" belong to the session and are not saved (design.md, EpisodeData).
    /// </summary>
    internal sealed class BailoutEngine
    {
        /// <summary>The key of the reputation change in the history (the string table has it).</summary>
        public const string ReputationReason = "rec.reputation.bailout";

        /// <summary>An offer was accepted (after the money, the loan and the counter are in the state).</summary>
        public event Action<BailoutChoice> Accepted;

        /// <summary>The investor contract came to its end (the term or the ceiling).</summary>
        public event Action InvestorEnded;

        private readonly RecoveryEnvironment _env;
        private readonly LoanBook _loans;
        private readonly Func<bool> _isBatchRun;
        private readonly Action<double, string> _reputation;

        private bool _offerOpen;
        private bool _waitingForBatch;
        private bool _failed;

        /// <param name="env">The environment.</param>
        /// <param name="loans">The loans (the rescue loan is registered there).</param>
        /// <param name="isBatchRun">True while a month plays in one frame (<c>ModTicks.IsBatchRun</c>); null means never.</param>
        /// <param name="reputation">Changes the reputation of the player by (delta, reason key); null means nothing happens.</param>
        public BailoutEngine(RecoveryEnvironment env, LoanBook loans, Func<bool> isBatchRun = null, Action<double, string> reputation = null)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (loans == null) throw new ArgumentNullException("loans");
            _env = env;
            _loans = loans;
            _isBatchRun = isBatchRun;
            _reputation = reputation;
        }

        /// <summary>The window of the offer; null until it is built (then nothing is ever held).</summary>
        public IBailoutPresenter Presenter { get; set; }

        /// <summary>The offer window is open now.</summary>
        public bool IsOfferOpen
        {
            get { return _offerOpen; }
        }

        /// <summary>The window could not be shown in this episode: the player is not asked again before the episode ends.</summary>
        public bool IsSuppressed
        {
            get { return _failed; }
        }

        /// <summary>How many offers the difficulty of the game allows per game.</summary>
        public int OffersAllowed
        {
            get { return BailoutMath.Allowed(_env.Balance.bailout, _env.Finance.Difficulty); }
        }

        // ---- the hold ----

        /// <summary>
        /// The patch on the last day of the default asks: hold the counter? True when the offer is open, will open after the batch, or just opened; false
        /// when there is no window to show, it failed, or the offer cannot be made (the game then goes its own way). Call it only when the rules say the
        /// counter must be held (<see cref="RecoveryRules.MustHoldForBailout"/>).
        /// </summary>
        public bool TryHold()
        {
            if (_failed) return false;
            if (_offerOpen) return true;
            if (Presenter == null) return false;

            if (_isBatchRun != null && _isBatchRun())
            {
                _waitingForBatch = true;
                return true;
            }
            return OpenOffer();
        }

        /// <summary>The batch run is over: opens the offer that waited for it, if the player still needs it.</summary>
        public void OnBatchEnded()
        {
            if (!_waitingForBatch) return;
            _waitingForBatch = false;
            if (_offerOpen || _failed || Presenter == null) return;

            BankruptcyView view = _env.View();
            int allowed = BailoutMath.Allowed(_env.Balance.bailout, _env.Finance.Difficulty);
            if (!_env.IsOn(SubFeature.Bailout)) return;
            if (!RecoveryRules.MustHoldForBailout(view, _env.State.bailoutsUsed, allowed, _env.State.episode.bailoutDeclined)) return;

            OpenOffer();
        }

        /// <summary>The numbers of the offer for the player as they are now; null when there is nothing to offer (the balance is already above the target).</summary>
        public BailoutOffer BuildOffer()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable) return null;

            BailoutConfig c = _env.Balance.bailout;
            double debtLimit = finance.PlayerDebtLimit;
            double amount = BailoutMath.Amount(c, finance.Money, debtLimit);
            if (!(amount > 0.0)) return null;

            double loanRate = LoanMath.EffectiveRate(c.loan.annualRate, finance.InterestMultiplier);
            return new BailoutOffer(
                amount,
                finance.Money + amount,
                _env.State.bailoutsUsed,
                BailoutMath.Allowed(c, finance.Difficulty),
                c.loan.months,
                c.loan.annualRate,
                loanRate,
                LoanMath.AnnuityPayment(amount, loanRate, c.loan.months),
                !_env.State.investorShare.active,
                c.investor.profitShare,
                c.investor.months,
                BailoutMath.InvestorCap(c.investor, amount));
        }

        // ---- the choice ----

        /// <summary>
        /// The player accepted (FR-8 item 3). Credits the amount, registers the rescue loan or the investor contract, counts the offer, lowers the
        /// reputation and cancels the default with the game's method. False when nothing was done: the company is not there, the investor way is taken
        /// while a contract runs, or there is nothing to offer any more.
        /// </summary>
        public bool Accept(BailoutChoice choice)
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || finance.IsBankrupt || !_env.Money.IsAvailable) return false;

            RecoveryState state = _env.State;
            if (choice == BailoutChoice.InvestorShare && state.investorShare.active) return false;

            BailoutOffer offer = BuildOffer();
            if (offer == null)
            {
                Close();
                return false;
            }

            BailoutConfig c = _env.Balance.bailout;
            double amount = offer.Amount;

            if (choice == BailoutChoice.RescueLoan)
            {
                string terms = c.loan.months.ToString(CultureInfo.InvariantCulture) + "m @ "
                               + (c.loan.annualRate * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";
                LoanData loan = _loans.Open(LoanData.KindRescueLoan, amount, c.loan.annualRate, c.loan.months);
                if (loan == null) return false;
                _env.Money.CapitalIn(amount, "bailout.loan", terms);
            }
            else
            {
                string terms = (c.investor.profitShare * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "% x "
                               + c.investor.months.ToString(CultureInfo.InvariantCulture) + "m";
                _env.Money.CapitalIn(amount, "bailout.investor", terms);
                state.investorShare = new InvestorShareData
                {
                    active = true,
                    amount = amount,
                    share = c.investor.profitShare,
                    monthsLeft = c.investor.months,
                    paid = 0.0,
                    cap = BailoutMath.InvestorCap(c.investor, amount),
                };
            }

            state.bailoutsUsed++;
            state.episode.bailoutDeclined = false;
            state.episode.recoveryStreak = 0;
            Close();

            // The rest must not undo what is done: a failing reputation or cancel is reported, the money and the contract stay.
            if (_reputation != null)
            {
                try
                {
                    _reputation(c.reputationDelta, ReputationReason);
                }
                catch (Exception e)
                {
                    _env.Warn("bailout: the reputation could not be lowered: " + e.Message);
                }
            }

            try
            {
                finance.CancelDefault();
            }
            catch (Exception e)
            {
                _env.Warn("bailout: the default could not be cancelled, the game will do it itself: " + e.Message);
            }

            Action<BailoutChoice> handler = Accepted;
            if (handler != null) handler(choice);
            return true;
        }

        /// <summary>The player declined (FR-8 item 4): the counter is not held again in this episode, the bankruptcy follows on the next tick.</summary>
        public void Decline()
        {
            _env.State.episode.bailoutDeclined = true;
            Close();
        }

        /// <summary>The window was closed with the cross: not a refusal. The next tick that must hold the counter asks again (design.md, "UI").</summary>
        public void OfferDismissed()
        {
            _offerOpen = false;
        }

        // ---- the month ----

        /// <summary>
        /// The first of the month: the payment to the investor, <c>min(cap − paid, share × max(0, profit of the last month))</c>, in the item of the
        /// contract. A loss pays 0 and the term goes on all the same (edge case 7). The contract ends with its term or its ceiling.
        /// </summary>
        public void OnMonth()
        {
            InvestorShareData contract = _env.State.investorShare;
            if (!contract.active) return;

            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || !_env.Money.IsAvailable) return;

            int year;
            int month;
            ReportMonths.Back(finance.Today, 1, out year, out month);
            double profit = finance.NetProfitOfMonth(year, month);

            var terms = new InvestorConfig { profitShare = contract.share };
            double pay = BailoutMath.InvestorPayment(terms, profit, contract.paid, contract.cap);
            if (pay > 0.0)
            {
                _env.Money.InvestorPayment(pay, "investor.payment",
                    year.ToString("0000", CultureInfo.InvariantCulture) + "-" + month.ToString("00", CultureInfo.InvariantCulture));
                contract.paid += pay;
            }

            contract.monthsLeft = contract.monthsLeft > 0 ? contract.monthsLeft - 1 : 0;
            if (contract.monthsLeft > 0 && contract.cap - contract.paid > 0.005) return;

            contract.active = false;
            Action ended = InvestorEnded;
            if (ended != null) ended();
        }

        // ---- the session ----

        /// <summary>A save was loaded, a new game started or the feature was switched: the session flags go back to "nothing is open".</summary>
        public void ResetSession()
        {
            _offerOpen = false;
            _waitingForBatch = false;
            _failed = false;
        }

        /// <summary>The player is not in default any more: the next default starts a new episode, with a new try for the window.</summary>
        public void EpisodeEnded()
        {
            ResetSession();
        }

        // ---- internals ----

        private bool OpenOffer()
        {
            BailoutOffer offer = BuildOffer();
            if (offer == null) return false;

            bool shown;
            try
            {
                shown = Presenter.Open(offer);
            }
            catch (Exception e)
            {
                _env.Warn("bailout: the offer window failed, the bankruptcy goes on: " + e.Message);
                shown = false;
            }

            if (!shown)
            {
                _failed = true;
                return false;
            }
            _offerOpen = true;
            return true;
        }

        private void Close()
        {
            _offerOpen = false;
            _waitingForBatch = false;
        }
    }
}
