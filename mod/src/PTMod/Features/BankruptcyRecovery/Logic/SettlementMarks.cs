using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// Named small whole numbers kept beside the section of the feature in the same save (on the game side: <c>ModSave.Flags</c> of the feature id, which the core
    /// keeps in its own section, whether the feature is on or off). A flag that was never set reads 0. In a test: a table.
    /// </summary>
    internal interface ISettlementFlags
    {
        int GetInt(string key);

        void SetInt(string key, int value);
    }

    /// <summary>
    /// The marks of the safe switch-off (FR-12, design.md D9): what a settlement leaves in the flags of the save so that switching the feature off and on gives no
    /// money back. The core hands a feature that is switched on again the section it kept (the one of the last save, or none, never the state of the moment
    /// the feature was switched off), so the loans that were turned into ordinary debt could come back, and the counter of the rescues and the pause of the bridge
    /// loan could fall to their start. The marks stand in the way:
    /// <list type="bullet">
    /// <item><see cref="LoanSeq"/>: every loan whose number is below it was settled (the ids are <c>bridge-N</c>, <c>term-N</c>, <c>rescue-N</c> with one rising number);</item>
    /// <item><see cref="LineClosed"/>: the credit line was closed by the settlement and never opens again;</item>
    /// <item><see cref="Bailouts"/>: the counter of the rescues at that moment; an investor contract of that or an earlier rescue was settled;</item>
    /// <item><see cref="BridgeDay"/>: the day the last bridge loan was closed (the pause of the bridge loan counts from it).</item>
    /// </list>
    /// All of them only grow: a later settlement may raise a mark and never lowers it. <see cref="Reconcile"/> applies the marks to a state that was read and is
    /// harmless to repeat. No game, no log: the notes come back as text.
    /// </summary>
    internal static class SettlementMarks
    {
        /// <summary>1 once any settlement was made in this game; without it nothing below is looked at.</summary>
        public const string Settled = "settled";

        /// <summary>The next loan number at the settlement: loans with a number below it are settled.</summary>
        public const string LoanSeq = "settledLoanSeq";

        /// <summary>1: the credit line was closed by a settlement.</summary>
        public const string LineClosed = "settledLineClosed";

        /// <summary>How many rescues were accepted at the settlement.</summary>
        public const string Bailouts = "settledBailouts";

        /// <summary>The day number (days since 0001-01-01) the last bridge loan was closed, 0 for never.</summary>
        public const string BridgeDay = "settledBridgeDay";

        /// <summary>
        /// Writes the marks for a state that was just settled (or that had nothing to settle: the counter and the pause must outlive the switch all the same).
        /// Every number only grows. <paramref name="lineClosed"/> is true when the settlement closed the credit line.
        /// </summary>
        public static void Record(ISettlementFlags flags, RecoveryState state, bool lineClosed)
        {
            if (flags == null || state == null) return;

            flags.SetInt(Settled, 1);
            Raise(flags, LoanSeq, state.nextLoanSeq);
            Raise(flags, Bailouts, state.bailoutsUsed);
            Raise(flags, BridgeDay, DayNumber(state.lastBridgeClosedOn));
            if (lineClosed) flags.SetInt(LineClosed, 1);
        }

        /// <summary>
        /// Brings a state that was read from the save in line with the marks: the loans that were settled are removed, a credit line that was closed stays closed,
        /// an investor contract of a settled rescue is closed, and the counters never fall below the marks. Returns true when the state changed; a line of text per
        /// change goes to <paramref name="note"/> (may be null). Repeating the call changes nothing more. Without the mark <see cref="Settled"/> it does nothing.
        /// </summary>
        public static bool Reconcile(RecoveryState state, ISettlementFlags flags, Action<string> note)
        {
            if (state == null || flags == null || flags.GetInt(Settled) == 0) return false;

            bool changed = false;
            int seq = flags.GetInt(LoanSeq);
            int bailouts = flags.GetInt(Bailouts);

            if (state.loans != null)
            {
                for (int i = state.loans.Count - 1; i >= 0; i--)
                {
                    LoanData loan = state.loans[i];
                    if (loan != null && NumberOf(loan.id) >= seq) continue;   // taken after the settlement
                    state.loans.RemoveAt(i);
                    changed = true;
                    Say(note, "the loan " + (loan == null ? "(empty)" : loan.id) + " was settled when the feature was switched off, it is not restored");
                }
            }

            CreditLineData line = state.creditLine;
            if (flags.GetInt(LineClosed) != 0 && line != null && line.state != CreditLineData.StateClosed)
            {
                line.state = CreditLineData.StateClosed;
                line.closedReason = CreditLineData.ReasonDisabled;
                line.drawn = 0.0;
                changed = true;
                Say(note, "the credit line was closed when the feature was switched off, it stays closed");
            }

            // judged before the counter is raised: a contract of a later rescue has a counter above the mark
            InvestorShareData investor = state.investorShare;
            if (investor != null && investor.active && state.bailoutsUsed <= bailouts)
            {
                investor.active = false;
                changed = true;
                Say(note, "the investor contract was settled when the feature was switched off, it is not restored");
            }

            if (state.bailoutsUsed < bailouts)
            {
                state.bailoutsUsed = bailouts;
                changed = true;
                Say(note, "the counter of the rescues is kept at " + bailouts.ToString(CultureInfo.InvariantCulture));
            }

            if (state.nextLoanSeq < seq)
            {
                state.nextLoanSeq = seq;
                changed = true;
            }

            DateTime marked;
            if (TryDate(flags.GetInt(BridgeDay), out marked))
            {
                DateTime own;
                if (!RecoveryDates.TryParse(state.lastBridgeClosedOn, out own) || own < marked)
                {
                    state.lastBridgeClosedOn = RecoveryDates.ToText(marked);
                    changed = true;
                    Say(note, "the pause of the bridge loan counts from " + state.lastBridgeClosedOn);
                }
            }
            return changed;
        }

        /// <summary>The number in the id of a loan (<c>bridge-7</c> gives 7); -1 for an id without one.</summary>
        internal static int NumberOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int dash = id.LastIndexOf('-');
            int number;
            if (dash < 0 || !int.TryParse(id.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out number)) return -1;
            return number;
        }

        /// <summary>The day number of a date as text (<c>yyyy-MM-dd</c>): whole days since 0001-01-01, 0 for an empty or bad text.</summary>
        internal static int DayNumber(string dateText)
        {
            DateTime date;
            if (!RecoveryDates.TryParse(dateText, out date)) return 0;
            return (int)(date.Date.Ticks / TimeSpan.TicksPerDay);
        }

        // False for 0 and for a number that is no day of the calendar.
        private static bool TryDate(int dayNumber, out DateTime date)
        {
            date = default(DateTime);
            if (dayNumber < 1 || dayNumber > 3652058) return false;
            date = new DateTime(dayNumber * TimeSpan.TicksPerDay);
            return true;
        }

        private static void Raise(ISettlementFlags flags, string key, int value)
        {
            if (value > flags.GetInt(key)) flags.SetInt(key, value);
        }

        private static void Say(Action<string> note, string text)
        {
            if (note != null) note(text);
        }
    }
}
