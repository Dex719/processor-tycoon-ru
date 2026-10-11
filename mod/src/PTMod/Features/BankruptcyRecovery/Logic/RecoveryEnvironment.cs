using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// What the engines of the feature stand on, in one object (so that a constructor does not take eight arguments): the player's finances, the
    /// money operations, the journal, the state of the save in play, the numbers of <c>balance.json</c>, the switches of the five parts, and a
    /// place to say a warning. The state and the balance are functions because the state is replaced when a save is loaded or a new game starts and the
    /// balance is read lazily. The game side builds one object for the session; a test builds one around fakes.
    /// <para>
    /// It also keeps the revenue of the last three full months for the month (design.md, "Performance Considerations": <c>R_3</c> is recomputed
    /// once a month, not every day), because both the credit line and the bridge loan need it.
    /// </para>
    /// </summary>
    internal sealed class RecoveryEnvironment
    {
        /// <summary>How many full months make <c>R_3</c>.</summary>
        public const int RevenueMonths = 3;

        private readonly Func<RecoveryState> _state;
        private readonly Func<RecoveryBalance> _balance;
        private readonly Func<SubFeature, bool> _isOn;
        private readonly Action<string> _warn;

        private bool _revenueKnown;
        private int _revenueMonthKey;
        private double _revenue;

        public RecoveryEnvironment(IPlayerFinance finance, IRecoveryMoney money, RecoveryJournal journal, Func<RecoveryState> state,
            Func<RecoveryBalance> balance, Func<SubFeature, bool> isOn = null, Action<string> warn = null)
        {
            if (finance == null) throw new ArgumentNullException("finance");
            if (money == null) throw new ArgumentNullException("money");
            if (journal == null) throw new ArgumentNullException("journal");
            if (state == null) throw new ArgumentNullException("state");
            if (balance == null) throw new ArgumentNullException("balance");
            Finance = finance;
            Money = money;
            Journal = journal;
            _state = state;
            _balance = balance;
            _isOn = isOn;
            _warn = warn;
        }

        public IPlayerFinance Finance { get; private set; }

        public IRecoveryMoney Money { get; private set; }

        public RecoveryJournal Journal { get; private set; }

        /// <summary>The state of the save in play. Read it through the environment every time; do not keep the object.</summary>
        public RecoveryState State
        {
            get { return _state(); }
        }

        /// <summary>The numbers of the balance file (after the range checks).</summary>
        public RecoveryBalance Balance
        {
            get { return _balance(); }
        }

        /// <summary>
        /// The part is on (<c>IsEnabled(SubFeature)</c> of the design): the feature is active and the player left the switch of the part on. It decides
        /// about NEW operations of the part; what was taken before keeps being served. Without a function every part is on.
        /// </summary>
        public bool IsOn(SubFeature part)
        {
            return _isOn == null || _isOn(part);
        }

        /// <summary>Writes a warning (to the log on the game side). Never throws.</summary>
        public void Warn(string message)
        {
            if (_warn == null) return;
            try
            {
                _warn(message);
            }
            catch (Exception)
            {
                // a failing logger must not break a money operation
            }
        }

        /// <summary>The facts of the player's finances the rules of <see cref="RecoveryRules"/> read.</summary>
        public BankruptcyView View()
        {
            return Finance.View();
        }

        /// <summary><c>D_L = max(D, floorBase × I)</c>, the base of the limits of the feature (FR-7 item 2).</summary>
        public double LimitBase()
        {
            return CreditLineMath.LimitBase(Finance.PlayerDebtLimit, Finance.Inflation, Balance.limits.floorBase);
        }

        /// <summary>
        /// <c>R_3</c>: the revenue from sales and contracts of the last three full months, read once per game month and kept. A new game, a load and a
        /// change of the date to another month recompute it; <see cref="ResetCache"/> forgets it by hand.
        /// </summary>
        public double RevenueLast3Months()
        {
            int key = ReportMonths.KeyOf(Finance.Today);
            if (!_revenueKnown || key != _revenueMonthKey)
            {
                double value = Finance.SalesRevenueLastFullMonths(RevenueMonths);
                _revenue = double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 ? 0.0 : value;
                _revenueMonthKey = key;
                _revenueKnown = true;
            }
            return _revenue;
        }

        /// <summary>Forgets <see cref="RevenueLast3Months"/> (a save was loaded, a new game started).</summary>
        public void ResetCache()
        {
            _revenueKnown = false;
        }
    }
}
