using System;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>What <see cref="BridgeLoanEngine.Take"/> did.</summary>
    internal enum BridgeTakeResult
    {
        /// <summary>The loan was taken: the amount is on the balance, the fee is paid, the loan is in the book.</summary>
        Done,

        /// <summary>A bridge loan cannot be taken now (<see cref="BridgeLoanEngine.Check"/> says why).</summary>
        NotEligible,

        /// <summary>The amount is not a number above 0.</summary>
        InvalidAmount,

        /// <summary>The amount is above <see cref="BridgeLoanEngine.MaxAmount"/>.</summary>
        TooMuch,

        /// <summary>The ledger or the player's company is not there.</summary>
        Unavailable,
    }

    /// <summary>
    /// The bridge loan (FR-6, design.md <c>BridgeLoanService</c> without the game): a short expensive loan for a player close to the default threshold. It
    /// says whether a loan may be offered (<see cref="RecoveryRules.CanTakeBridge"/> plus the switch of the part), how much (<c>A_max = β × D_L + γ × R_3</c>),
    /// and takes it: the amount is credited, the fee is paid as an expense of the report (item "Interest"), and an annuity loan is registered. The payments
    /// are <see cref="LoanBook"/>'s.
    /// </summary>
    internal sealed class BridgeLoanEngine
    {
        /// <summary>The key of the reason when the player switched the part off.</summary>
        public const string ReasonOff = "rec.bridge.unavailable.disabled";

        private readonly RecoveryEnvironment _env;
        private readonly LoanBook _loans;

        public BridgeLoanEngine(RecoveryEnvironment env, LoanBook loans)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (loans == null) throw new ArgumentNullException("loans");
            _env = env;
            _loans = loans;
        }

        private BridgeConfig Config
        {
            get { return _env.Balance.bridgeLoan; }
        }

        /// <summary>Whether a bridge loan may be offered now (FR-6 items 1, 5, 6); the reason of a "no" is a key of the string tables.</summary>
        public Eligibility Check()
        {
            if (!_env.IsOn(SubFeature.BridgeLoan)) return Eligibility.No(ReasonOff);
            if (!_env.Finance.IsAvailable) return Eligibility.No(null);

            DateTime? lastClosed = null;
            DateTime parsed;
            if (RecoveryDates.TryParse(_env.State.lastBridgeClosedOn, out parsed)) lastClosed = parsed;

            BankruptcyView view = _env.View();
            return RecoveryRules.CanTakeBridge(view, _loans.HasActive(LoanData.KindBridge), lastClosed, Config);
        }

        /// <summary><c>A_max = β × D_L + γ × R_3</c>: the most the player may take (FR-6 item 1); 0 while the company is not there.</summary>
        public double MaxAmount()
        {
            if (!_env.Finance.IsAvailable) return 0.0;
            double max = Positive(Config.baseDebtLimitShare) * _env.LimitBase() + Positive(Config.revenueShare) * _env.RevenueLast3Months();
            return Positive(max);
        }

        /// <summary>The fee for an amount (a share of it, paid at once).</summary>
        public double Fee(double amount)
        {
            return Positive(Config.fee) * Positive(amount);
        }

        /// <summary>The monthly payment of a loan of that amount at today's rate multiplier (what the confirmation shows).</summary>
        public double Payment(double amount)
        {
            double rate = LoanMath.EffectiveRate(Config.annualRate, _env.Finance.InterestMultiplier);
            return LoanMath.AnnuityPayment(Positive(amount), rate, Config.months);
        }

        /// <summary>The effective yearly rate shown in the terms: the rate of the file times the multiplier of <c>bank.interest</c>.</summary>
        public double EffectiveRate()
        {
            return LoanMath.EffectiveRate(Config.annualRate, _env.Finance.InterestMultiplier);
        }

        /// <summary>
        /// Takes a bridge loan of <paramref name="amount"/> (FR-6 item 2): <c>CapitalIn(amount)</c>, the fee through the report, the annuity loan.
        /// Nothing is done unless <see cref="Check"/> is a yes and the amount is within <see cref="MaxAmount"/>.
        /// </summary>
        public BridgeTakeResult Take(double amount, out LoanData loan)
        {
            loan = null;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || !(amount > 0.0)) return BridgeTakeResult.InvalidAmount;
            if (!_env.Finance.IsAvailable || !_env.Money.IsAvailable) return BridgeTakeResult.Unavailable;
            if (!Check().Ok) return BridgeTakeResult.NotEligible;
            if (amount > MaxAmount() + 0.5) return BridgeTakeResult.TooMuch;

            BridgeConfig c = Config;
            string terms = c.months.ToString(CultureInfo.InvariantCulture) + "m @ " + (c.annualRate * 100.0).ToString("0.##", CultureInfo.InvariantCulture) + "%";

            loan = _loans.Open(LoanData.KindBridge, amount, c.annualRate, c.months);
            if (loan == null) return BridgeTakeResult.InvalidAmount;

            _env.Money.CapitalIn(amount, "bridge.open", terms);
            double fee = Fee(amount);
            if (fee > 0.0) _env.Money.Interest(fee, "bridge.fee", loan.id);
            return BridgeTakeResult.Done;
        }

        private static double Positive(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 ? 0.0 : value;
        }
    }
}
