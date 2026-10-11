using System;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The startup credit line (FR-7, design.md <c>StartupCreditLineService</c> without the game): it opens for the first years of the company, grows with the
    /// sales of the first CPUs, lends by itself when the balance gets close to the default threshold, charges its interest once a month, takes a part of a
    /// positive balance back, and at the end of its term turns what is still owed into a term loan. The state is the <see cref="CreditLineData"/> of the
    /// save; the money goes through <see cref="IRecoveryMoney"/>. A closed line never opens again.
    /// <para>
    /// The part <see cref="SubFeature.StartupCredit"/> decides about NEW things only (opening the line, drawing, the auto draw). A line that is open
    /// is served all the same: the interest, the repayment out of a positive balance, the closing at the end of the term (FR-12 item 2).
    /// </para>
    /// </summary>
    internal sealed class CreditLineEngine
    {
        /// <summary>The reasons a draw is not possible now (keys of the string tables; shown by the Loans tab).</summary>
        public const string ReasonOff = "rec.credit.unavailable.off";
        public const string ReasonNoLine = "rec.credit.unavailable.noLine";
        public const string ReasonExpired = "rec.credit.unavailable.expired";
        public const string ReasonFull = "rec.credit.unavailable.full";
        public const string ReasonBankrupt = "rec.credit.unavailable.bankrupt";

        /// <summary>The auto draw took this amount (positive).</summary>
        public event Action<double> AutoDrawn;

        /// <summary>The line ran out of time and what was owed became this term loan.</summary>
        public event Action<LoanData> Converted;

        private readonly RecoveryEnvironment _env;
        private readonly LoanBook _loans;

        // The parsed closing date of the line, so that the daily check does not parse a text 365 times a year.
        private string _closesText;
        private DateTime _closes = DateTime.MaxValue;

        public CreditLineEngine(RecoveryEnvironment env, LoanBook loans)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (loans == null) throw new ArgumentNullException("loans");
            _env = env;
            _loans = loans;
        }

        private CreditLineData Line
        {
            get { return _env.State.creditLine; }
        }

        private StartupCreditConfig Config
        {
            get { return _env.Balance.startupCredit; }
        }

        /// <summary>The line is open (not "none", not "closed").</summary>
        public bool IsOpen
        {
            get { return Line.state == CreditLineData.StateOpen; }
        }

        /// <summary>The line is open and its term is over: no more draws, the debt turns into a term loan on the next first of the month.</summary>
        public bool IsExpired
        {
            get { return IsOpen && HasExpired(Line, _env.Finance.Today); }
        }

        /// <summary>What the line owes now: the drawn part. 0 for a line that is not open.</summary>
        public double Drawn
        {
            get { return IsOpen ? Line.drawn : 0.0; }
        }

        /// <summary>The auto draw switch of the menu.</summary>
        public bool AutoDraw
        {
            get { return Line.autoDraw; }
        }

        /// <summary>Switches the auto draw (the menu's toggle).</summary>
        public void SetAutoDraw(bool on)
        {
            Line.autoDraw = on;
        }

        /// <summary>
        /// The limit of the line today, <c>L = clamp(a_d × D_L + b × R_3, 0, c × D_L)</c> (FR-7 item 2); 0 for a line that is not open or while the
        /// company is not there. After the end of the term the number is still the limit; <see cref="Available"/> is 0 then.
        /// </summary>
        public double Limit()
        {
            if (!IsOpen || !_env.Finance.IsAvailable) return 0.0;
            return CreditLineMath.Limit(Config, _env.Finance.Difficulty, _env.LimitBase(), _env.RevenueLast3Months());
        }

        /// <summary>What can still be drawn: the limit minus the drawn part, not below 0; 0 when the term is over.</summary>
        public double Available()
        {
            if (!IsOpen || IsExpired) return 0.0;
            return CreditLineMath.Available(Limit(), Line.drawn);
        }

        /// <summary>What the next first of the month charges as interest on the drawn part (FR-7 item 5), at today's rate multiplier; 0 without a debt.</summary>
        public double NextInterest()
        {
            if (!IsOpen) return 0.0;
            return LoanMath.MonthlyInterest(Line.drawn, LoanMath.EffectiveRate(Config.annualRate, _env.Finance.InterestMultiplier));
        }

        /// <summary>Null when a draw is possible now, otherwise the key of the reason.</summary>
        public string DrawBlock()
        {
            if (!_env.IsOn(SubFeature.StartupCredit)) return ReasonOff;
            if (!IsOpen) return ReasonNoLine;
            if (_env.Finance.IsBankrupt) return ReasonBankrupt;
            if (IsExpired) return ReasonExpired;
            if (!(Available() > 0.0)) return ReasonFull;
            return null;
        }

        // ---- opening ----

        /// <summary>
        /// Opens the line when it was never opened, the part is on, the company is not bankrupt and the first years of the company are not over (FR-7
        /// items 1 and 8): from today until the foundation date plus <c>years</c>. Returns true when it opened now. A line that is open or closed is
        /// left alone.
        /// </summary>
        public bool EnsureOpened()
        {
            CreditLineData line = Line;
            if (line.state != CreditLineData.StateNone) return false;
            if (!_env.IsOn(SubFeature.StartupCredit)) return false;

            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || finance.IsBankrupt) return false;

            DateTime foundation = finance.FoundationDate;
            DateTime today = finance.Today;
            if (!CreditLineMath.IsWindowOpen(Config, foundation, today)) return false;

            DateTime closes = CreditLineMath.ClosesOn(Config, foundation);
            line.state = CreditLineData.StateOpen;
            line.opensOn = RecoveryDates.ToText(today);
            line.closesOn = RecoveryDates.ToText(closes);
            line.drawn = 0.0;
            line.closedReason = string.Empty;

            _env.Journal.Record("line.open", 0.0, "until " + line.closesOn);
            return true;
        }

        // ---- the day ----

        /// <summary>
        /// The auto draw (FR-7 item 4), before the game checks for bankruptcy: when the balance is below <c>−0.9 × D</c> the line lends what brings the
        /// balance to <c>−0.6 × D</c>, within what is free. Cheap when the balance is far from the threshold (the limit is not even computed).
        /// </summary>
        public void OnDay()
        {
            CreditLineData line = Line;
            if (line.state != CreditLineData.StateOpen || !line.autoDraw) return;
            if (!_env.IsOn(SubFeature.StartupCredit)) return;

            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || finance.IsBankrupt) return;

            double d = finance.PlayerDebtLimit;
            if (!(d > 0.0) || !(finance.Money < -Config.autoDrawTriggerShare * d)) return;
            if (HasExpired(line, finance.Today) || !_env.Money.IsAvailable) return;

            double amount = CreditLineMath.AutoDraw(Config, finance.Money, d, Available());
            if (!(amount > 0.0)) return;

            _env.Money.CapitalIn(amount, "line.autodraw", string.Empty);
            line.drawn += amount;

            Action<double> handler = AutoDrawn;
            if (handler != null) handler(amount);
        }

        // ---- the month ----

        /// <summary>
        /// The first of the month (FR-7 items 5 to 7), in this order: the interest on what is drawn, in one line of the report; a quarter of a positive
        /// balance pays the debt back; and, if the term is over, what is still owed turns into a term loan and the line closes for good.
        /// </summary>
        public void OnMonth()
        {
            CreditLineData line = Line;
            if (line.state != CreditLineData.StateOpen) return;

            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || !_env.Money.IsAvailable) return;

            double interest = LoanMath.MonthlyInterest(line.drawn, LoanMath.EffectiveRate(Config.annualRate, finance.InterestMultiplier));
            if (interest > 0.0) _env.Money.Interest(interest, "line.interest", string.Empty);

            double repay = CreditLineMath.Sweep(Config, finance.Money, line.drawn);
            if (repay > 0.0)
            {
                _env.Money.CapitalOut(repay, "line.sweep", string.Empty);
                line.drawn = line.drawn > repay ? line.drawn - repay : 0.0;
            }

            if (HasExpired(line, finance.Today)) Expire(line);
        }

        // ---- the player's actions ----

        /// <summary>
        /// Draws money from the line by hand (FR-7 item 3). The amount is cut to what is free. Returns what was drawn, 0 when nothing was
        /// (<see cref="DrawBlock"/> says why).
        /// </summary>
        public double Draw(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount) || !(amount > 0.0)) return 0.0;
            if (DrawBlock() != null || !_env.Money.IsAvailable) return 0.0;

            double take = Math.Min(amount, Available());
            if (!(take > 0.0)) return 0.0;

            _env.Money.CapitalIn(take, "line.draw", string.Empty);
            Line.drawn += take;
            return take;
        }

        /// <summary>
        /// Pays the drawn part back by hand, without a penalty. The amount is cut to what is owed and the balance has to cover it. Returns what was repaid,
        /// 0 when nothing was. Works while the line is open, also when the part is switched off.
        /// </summary>
        public double Repay(double amount)
        {
            CreditLineData line = Line;
            if (line.state != CreditLineData.StateOpen) return 0.0;
            if (double.IsNaN(amount) || double.IsInfinity(amount) || !(amount > 0.0)) return 0.0;

            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || !_env.Money.IsAvailable) return 0.0;

            double pay = Math.Min(amount, line.drawn);
            if (!(pay > 0.0) || finance.Money < pay) return 0.0;

            _env.Money.CapitalOut(pay, "line.repay", string.Empty);
            line.drawn = line.drawn > pay ? line.drawn - pay : 0.0;
            return pay;
        }

        // ---- internals ----

        // The end of the term: the debt becomes a term loan (no money moves: it is the same debt), the line closes for good.
        private void Expire(CreditLineData line)
        {
            double owed = line.drawn;
            LoanData loan = null;
            if (owed > 0.005)
            {
                loan = _loans.Open(LoanData.KindTerm, owed, Config.termAnnualRate, Config.termMonths);
                if (loan == null)
                {
                    _env.Warn("the credit line could not turn " + owed.ToString("0.##", CultureInfo.InvariantCulture) + " into a term loan, it stays open");
                    return;
                }
            }

            line.drawn = 0.0;
            line.state = CreditLineData.StateClosed;
            line.closedReason = CreditLineData.ReasonExpired;
            _env.Journal.Record("line.convert", 0.0, loan == null ? string.Empty : loan.id);

            if (loan == null) return;
            Action<LoanData> handler = Converted;
            if (handler != null) handler(loan);
        }

        private bool HasExpired(CreditLineData line, DateTime today)
        {
            if (!ReferenceEquals(line.closesOn, _closesText))
            {
                DateTime parsed;
                _closes = RecoveryDates.TryParse(line.closesOn, out parsed) ? parsed : DateTime.MaxValue;
                _closesText = line.closesOn;
            }
            return CreditLineMath.HasExpired(_closes, today);
        }
    }
}
