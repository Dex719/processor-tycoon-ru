#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>What a debug action did.</summary>
    internal enum DebugStatus
    {
        /// <summary>Applied; the journal has a record with the mark <c>debug</c>.</summary>
        Done,

        /// <summary>The player's company or the ledger is not there (the menu, a scene change): nothing was changed.</summary>
        Unavailable,

        /// <summary>The request makes no sense now (a number that is not one, no threshold, a bankrupt company): nothing was changed. The reason is in <see cref="DebugOutcome.Reason"/>.</summary>
        Rejected,
    }

    /// <summary>Which debug action an outcome is of.</summary>
    internal enum DebugKind
    {
        /// <summary>"Balance = k × D".</summary>
        Balance,

        /// <summary>"DaysInDefault = N".</summary>
        Days,
    }

    /// <summary>The outcome of one debug action: the status and the numbers the line under the buttons shows.</summary>
    internal sealed class DebugOutcome
    {
        public DebugOutcome(DebugKind kind, DebugStatus status, double before, double after, string reason)
        {
            Kind = kind;
            Status = status;
            Before = before;
            After = after;
            Reason = reason ?? string.Empty;
        }

        public DebugKind Kind { get; private set; }

        public DebugStatus Status { get; private set; }

        /// <summary>The balance (or the count of days in default) before the action; 0 when nothing was done.</summary>
        public double Before { get; private set; }

        /// <summary>The balance (or the count of days) after the action, read back from the finances; 0 when nothing was done.</summary>
        public double After { get; private set; }

        /// <summary>A short ASCII code of why the request was rejected (<c>no-limit</c>, <c>bankrupt</c>, <c>bad-number</c>); empty otherwise.</summary>
        public string Reason { get; private set; }
    }

    /// <summary>
    /// The debug actions of the manual scenarios M1-M10 without the game (TSK-019; design.md "Ручные сценарии в игре"): put the balance of the player on <c>k × D</c> and set the
    /// count of the days in default. The whole file is the debug build's (<c>PTMOD_DEBUG</c>): a release build has neither this class nor the page that calls it, so a player cannot reach
    /// these actions at all. The page with the buttons is <c>Debug/RecoveryDebugActions.cs</c>.
    /// <list type="bullet">
    /// <item><b>The balance.</b> The difference to <c>k × D</c> goes through the ledger as one capital operation (<see cref="IRecoveryMoney.CapitalIn"/> or
    /// <see cref="IRecoveryMoney.CapitalOut"/>, the code <see cref="BalanceOp"/>), so the money rules, the journal and the log are those of every other operation. A balance that is
    /// already on the mark gets a record of 0, so that every press is in the journal.</item>
    /// <item><b>The days.</b> The count is written through the finances of the player (<see cref="IPlayerFinance.DaysInDefault"/>) and never reaches the limit of the game: the action
    /// stops one day short of the bankruptcy (the game would declare it at the next day), it is a tool for setting up a scenario, not for ending the game. The record has the code
    /// <see cref="DaysOp"/> and no money.</item>
    /// <item><b>The mark.</b> Both records have a code that starts with <c>debug.</c> and a note that starts with <see cref="Mark"/>, so a record of a test is told from a real one in the journal tab.</item>
    /// </list>
    /// Nothing else of the state changes: not the counter of the rescues, not the loans, not the line. Player only: the engine works on the player's finances.
    /// </summary>
    internal sealed class RecoveryDebugEngine
    {
        /// <summary>The code of the journal record of "Balance = k × D".</summary>
        public const string BalanceOp = "debug.balance";

        /// <summary>The code of the journal record of "DaysInDefault = N".</summary>
        public const string DaysOp = "debug.days";

        /// <summary>The note of every record of a debug action starts with this word and a colon.</summary>
        public const string Mark = "debug";

        /// <summary>The balance counts as put on the mark when it is within this many dollars of it (the game keeps money in a <c>float</c>).</summary>
        public const double Tolerance = 0.5;

        /// <summary>The factors k of the buttons "Balance = k × D" (Acceptance of TSK-019): the warning, the default, the default deeper, the last-chance case, the recovery.</summary>
        public static readonly IReadOnlyList<double> BalanceShares = new[] { -0.8, -0.95, -1.1, -1.2, 0.1 };

        /// <summary>The counts N of the buttons "DaysInDefault = N" (Acceptance of TSK-019): the first day and two days short of the limit.</summary>
        public static readonly IReadOnlyList<int> DefaultDays = new[] { 1, 118 };

        private readonly RecoveryEnvironment _env;

        public RecoveryDebugEngine(RecoveryEnvironment env)
        {
            if (env == null) throw new ArgumentNullException("env");
            _env = env;
        }

        /// <summary>Puts the balance of the player on <paramref name="share"/> × D (D is the default threshold without its sign, <c>ModCredit.PlayerDebtLimit</c>).</summary>
        public DebugOutcome SetBalanceShare(double share)
        {
            IPlayerFinance finance = _env.Finance;
            if (double.IsNaN(share) || double.IsInfinity(share)) return Rejected(DebugKind.Balance, "bad-number");
            if (!finance.IsAvailable || !_env.Money.IsAvailable) return new DebugOutcome(DebugKind.Balance, DebugStatus.Unavailable, 0.0, 0.0, null);
            if (finance.IsBankrupt) return Rejected(DebugKind.Balance, "bankrupt");

            double limit = finance.PlayerDebtLimit;
            if (double.IsNaN(limit) || double.IsInfinity(limit) || !(limit > 0.0)) return Rejected(DebugKind.Balance, "no-limit");

            double before = finance.Money;
            double target = share * limit;
            double delta = target - before;
            string details = Mark + ": balance " + Number(before) + " -> " + Number(target) + " (" + ShareText(share, false) + " x D, D = " + Number(limit) + ")";

            if (Math.Abs(delta) < Tolerance)
            {
                _env.Journal.Record(BalanceOp, 0.0, details);
                return new DebugOutcome(DebugKind.Balance, DebugStatus.Done, before, before, null);
            }

            if (delta > 0.0) _env.Money.CapitalIn(delta, BalanceOp, details);
            else _env.Money.CapitalOut(-delta, BalanceOp, details);

            double after = finance.Money;
            if (Math.Abs(after - before) < Tolerance) return Rejected(DebugKind.Balance, "ledger");   // the ledger refused: it has said why in the log
            return new DebugOutcome(DebugKind.Balance, DebugStatus.Done, before, after, null);
        }

        /// <summary>
        /// Sets the count of the days in default of the player. A count at or above the limit of the game (<see cref="IPlayerFinance.DefaultTimeLimit"/>) is held one day below it; a
        /// negative count is rejected. The state of the default (the banner, the warning window) is not touched: the counter alone is written.
        /// </summary>
        public DebugOutcome SetDaysInDefault(int days)
        {
            IPlayerFinance finance = _env.Finance;
            if (days < 0) return Rejected(DebugKind.Days, "bad-number");
            if (!finance.IsAvailable) return new DebugOutcome(DebugKind.Days, DebugStatus.Unavailable, 0.0, 0.0, null);
            if (finance.IsBankrupt) return Rejected(DebugKind.Days, "bankrupt");

            int limit = finance.DefaultTimeLimit;
            int value = limit > 0 && days >= limit ? limit - 1 : days;
            int before = finance.DaysInDefault;
            finance.DaysInDefault = value;

            string details = Mark + ": DaysInDefault " + before.ToString(CultureInfo.InvariantCulture) + " -> " + value.ToString(CultureInfo.InvariantCulture)
                             + (value != days ? " (asked " + days.ToString(CultureInfo.InvariantCulture) + ", the limit of the game is " + limit.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
            _env.Journal.Record(DaysOp, 0.0, details);
            return new DebugOutcome(DebugKind.Days, DebugStatus.Done, before, value, null);
        }

        /// <summary>
        /// The threshold at this moment as one line for <c>Player.log</c> (TSK-003, the runtime part of the spike: "the threshold on 1975-01-01, 1980-01-01 and 1985-01-01 of a new
        /// game"): the date, <c>D</c> (<c>ModCredit.PlayerDebtLimit</c>: the bank's limit with the multipliers of the core, which are 1 in a plain game), the base of the limits
        /// <c>D_L</c> and the inflation. Empty without the player's company. Reads nothing from the bank itself: the rule of the feature (FR-11 item 2) holds for the debug build too.
        /// </summary>
        public string ThresholdLine()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable) return string.Empty;
            return Mark + ": threshold on " + RecoveryDates.ToText(finance.Today) + ": D = " + Number(finance.PlayerDebtLimit) + ", D_L = " + Number(_env.LimitBase())
                   + ", inflation = " + finance.Inflation.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The factor k as the caption of a button shows it: with its sign and up to two decimals (<c>-0.8</c>, <c>+0.1</c>), the decimal comma for Russian when
        /// <paramref name="decimalComma"/> is true. A plain minus: the font of the game may lack the typographic one.
        /// </summary>
        public static string ShareText(double share, bool decimalComma)
        {
            if (double.IsNaN(share) || double.IsInfinity(share)) return "0";
            string text = share.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
            return decimalComma ? text.Replace('.', ',') : text;
        }

        private static DebugOutcome Rejected(DebugKind kind, string reason)
        {
            return new DebugOutcome(kind, DebugStatus.Rejected, 0.0, 0.0, reason);
        }

        // Plain digits for the note of the journal: the formatter of the game may not be ready, and the note is read by people who compare it with the log.
        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
#endif
