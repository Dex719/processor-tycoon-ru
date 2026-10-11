using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>The question before an operation of the rescue menu (FR-2 item 4): the caption, the text, and the balance the operation leaves.</summary>
    internal sealed class Confirmation
    {
        public Confirmation(string title, string message, double balanceAfter, bool belowThreshold)
        {
            Title = title;
            Message = message;
            BalanceAfter = balanceAfter;
            BelowThreshold = belowThreshold;
        }

        /// <summary>The caption of the dialog.</summary>
        public string Title { get; private set; }

        /// <summary>The text: the operation, its sum, its side effects, then the balance after it (one line each).</summary>
        public string Message { get; private set; }

        /// <summary>The balance after the operation.</summary>
        public double BalanceAfter { get; private set; }

        /// <summary>The balance after the operation is below the default threshold (the text says so).</summary>
        public bool BelowThreshold { get; private set; }
    }

    /// <summary>
    /// Writes the question of the second step of every operation of the rescue menu (design.md, "Меню спасения": the player sets an operation up, sees what it does,
    /// then the core's <c>ModUI.Confirm</c> asks): what is done, for what sum, what else it costs, and the balance after it. The balance after is worked out here, from
    /// the balance and the numbers the preview already showed, so the same rule serves every operation. There is no dialog of the feature's own: the game side hands the
    /// text to <c>ModUI.Confirm</c>.
    /// </summary>
    internal sealed class ConfirmPlanner
    {
        private readonly IMenuText _t;
        private readonly double _balance;
        private readonly double _debtLimit;

        /// <param name="text">The words and the numbers.</param>
        /// <param name="balance">The balance now.</param>
        /// <param name="debtLimit">The default threshold without a sign, <c>D</c>; 0 or less when unknown (then no warning about the threshold is written).</param>
        public ConfirmPlanner(IMenuText text, double balance, double debtLimit)
        {
            _t = text;
            _balance = balance;
            _debtLimit = debtLimit;
        }

        /// <summary>Selling production lines (FR-3): the sum, the capacity before and after, and the warning when the contracts need more lines than are left.</summary>
        public Confirmation SellLines(string wafer, int lines, double revenue, int capacityBefore, int capacityAfter, bool contractsAtRisk, int contractedLines)
        {
            var text = new List<string>
            {
                _t.L("rec.lines.sell", Num(lines), wafer ?? string.Empty, _t.Money(revenue)),
                _t.L("rec.confirm.capacity", Num(capacityBefore), Num(capacityAfter)),
            };
            if (contractsAtRisk)
            {
                text.Add(_t.L("rec.lines.contractsWarning"));
                text.Add(_t.L("rec.confirm.contracts", Num(contractedLines)));
            }
            return Make(revenue, text);
        }

        /// <summary>Selling the stock of one CPU (FR-4 item 1).</summary>
        public Confirmation SellStock(string cpu, int stock, double income)
        {
            var text = new List<string> { _t.L("rec.confirm.stock", cpu ?? string.Empty, Num(stock), _t.Money(income)) };
            return Make(income, text);
        }

        /// <summary>Taking a CPU out of production (FR-4 item 2): the fine of its contracts and the income of its stock.</summary>
        public Confirmation Retire(string cpu, double fine, double stockIncome)
        {
            var text = new List<string> { _t.L("rec.confirm.retire", cpu ?? string.Empty) };
            if (fine > 0.0) text.Add(_t.L("rec.confirm.retire.fine", _t.Money(fine)));
            if (stockIncome > 0.0) text.Add(_t.L("rec.confirm.retire.stock", _t.Money(stockIncome)));
            text.Add(_t.L("rec.confirm.retire.note"));
            return Make(stockIncome - fine, text);
        }

        /// <summary>A bridge loan (FR-6 item 2): the amount comes in at once, the fee goes out at once, the payments follow.</summary>
        public Confirmation Bridge(double amount, double fee, int months, double annualRate, double payment)
        {
            var text = new List<string>
            {
                _t.L("rec.bridge.terms", _t.Money(amount), Num(months), _t.Percent(annualRate), _t.Money(payment)),
            };
            if (fee > 0.0) text.Add(_t.L("rec.confirm.fee", _t.Money(fee)));
            return Make(amount - fee, text);
        }

        /// <summary>Drawing from the credit line (FR-7 item 3).</summary>
        public Confirmation Draw(double amount, double annualRate)
        {
            var text = new List<string>
            {
                _t.L("rec.confirm.draw", _t.Money(amount)),
                _t.L("rec.confirm.draw.rate", _t.Percent(annualRate)),
            };
            return Make(amount, text);
        }

        /// <summary>Paying the drawn part of the credit line back.</summary>
        public Confirmation RepayLine(double amount)
        {
            var text = new List<string> { _t.L("rec.confirm.repay", _t.Money(amount)) };
            return Make(-amount, text);
        }

        /// <summary>Paying a loan back early (FR-10 item 3); <paramref name="debtAfter"/> is what is still owed on it afterwards.</summary>
        public Confirmation Prepay(string kindName, double amount, double debtAfter)
        {
            var text = new List<string> { _t.L("rec.confirm.prepay", kindName ?? string.Empty, _t.Money(amount)) };
            text.Add(_t.L("rec.confirm.prepay.left", _t.Money(debtAfter > 0.0 ? debtAfter : 0.0)));
            return Make(-amount, text);
        }

        // The lines the caller wrote, then the balance after the operation, then the warning when that balance is below the threshold.
        private Confirmation Make(double change, List<string> lines)
        {
            double after = _balance + change;
            bool below = _debtLimit > 0.0 && after < -_debtLimit;

            lines.Add(_t.L("rec.confirm.after", _t.MoneyExact(after)));
            if (below) lines.Add(_t.L("rec.confirm.belowThreshold"));

            return new Confirmation(_t.L("rec.confirm.title"), string.Join("\n", lines.ToArray()), after, below);
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
