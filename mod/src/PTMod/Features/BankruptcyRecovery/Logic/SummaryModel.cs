using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>The numbers of the "Summary" tab, taken from <c>RecoveryApi.GetSummary</c> (plain values: the model needs no game).</summary>
    internal struct SummaryInput
    {
        /// <summary>The balance.</summary>
        public double Money;

        /// <summary>The default threshold without a sign, <c>D</c>; 0 or less when it is unknown. (Not named after the bank's member: a test of the core forbids a dotted read of that name.)</summary>
        public double Limit;

        /// <summary>How far the balance is below the threshold, 0 when it is not.</summary>
        public double Deficit;

        /// <summary>What the player owes on the loans and the line of the feature.</summary>
        public double LoansOutstanding;

        /// <summary>The next monthly payment of all loans together, 0 when there is none.</summary>
        public double NextPayment;

        /// <summary>The date of that payment; null when unknown.</summary>
        public DateTime? NextPaymentDate;

        public bool InDefault;

        /// <summary>Days before the bankruptcy; meaningful when <see cref="InDefault"/>.</summary>
        public int DaysToBankruptcy;
    }

    /// <summary>One row of the summary: the title, the value as text and how to paint it.</summary>
    internal sealed class SummaryRow
    {
        public SummaryRow(string id, string title, string value, MenuTone tone)
        {
            Id = id;
            Title = title;
            Value = value;
            Tone = tone;
        }

        /// <summary><c>balance</c>, <c>threshold</c>, <c>deficit</c>, <c>bankruptcy</c>, <c>debt</c> or <c>payment</c> (the place of the row; the title may differ).</summary>
        public string Id { get; private set; }

        public string Title { get; private set; }

        public string Value { get; private set; }

        public MenuTone Tone { get; private set; }
    }

    /// <summary>
    /// The six rows of the "Summary" tab (FR-2 item 3, design.md "UI"): the balance, the default threshold, the deficit (or the room left above the threshold),
    /// the days to the bankruptcy (or "not in default"), the debt on the loans of the feature, and the next payment with its date. Pure: the texts come from
    /// <see cref="IMenuText"/>.
    /// </summary>
    internal static class SummaryModel
    {
        /// <summary>The number of rows.</summary>
        public const int RowCount = 6;

        /// <summary>Days to the bankruptcy at or below which the row is painted as a danger.</summary>
        public const int UrgentDays = 30;

        private const string Dash = "-";

        public static IReadOnlyList<SummaryRow> Rows(IMenuText t, SummaryInput s)
        {
            var rows = new List<SummaryRow>(RowCount);
            bool hasLimit = s.Limit > 0.0 && !double.IsNaN(s.Limit) && !double.IsInfinity(s.Limit);

            rows.Add(new SummaryRow("balance", t.L("rec.summary.label.balance"), t.Money(s.Money), s.Money < 0.0 ? MenuTone.Bad : MenuTone.Normal));

            rows.Add(new SummaryRow("threshold", t.L("rec.summary.label.threshold"), hasLimit ? t.Money(-s.Limit) : Dash, MenuTone.Normal));

            if (s.Deficit > 0.0)
            {
                rows.Add(new SummaryRow("deficit", t.L("rec.summary.label.deficit"), t.Money(s.Deficit), MenuTone.Bad));
            }
            else
            {
                // above the threshold: how much room is left before it
                rows.Add(new SummaryRow("deficit", t.L("rec.summary.label.headroom"), hasLimit ? t.Money(s.Money + s.Limit) : Dash, hasLimit ? MenuTone.Good : MenuTone.Normal));
            }

            if (s.InDefault)
            {
                int days = s.DaysToBankruptcy < 0 ? 0 : s.DaysToBankruptcy;
                rows.Add(new SummaryRow("bankruptcy", t.L("rec.summary.label.bankruptcy"),
                    t.L("rec.summary.daysLeft", days.ToString(CultureInfo.InvariantCulture)), days <= UrgentDays ? MenuTone.Bad : MenuTone.Warn));
            }
            else
            {
                rows.Add(new SummaryRow("bankruptcy", t.L("rec.summary.label.bankruptcy"), t.L("rec.summary.notInDefault"), MenuTone.Good));
            }

            rows.Add(new SummaryRow("debt", t.L("rec.summary.label.debt"), t.Money(s.LoansOutstanding), s.LoansOutstanding > 0.0 ? MenuTone.Normal : MenuTone.Muted));

            if (s.NextPayment > 0.0)
            {
                string when = s.NextPaymentDate.HasValue ? t.Date(s.NextPaymentDate.Value) : Dash;
                rows.Add(new SummaryRow("payment", t.L("rec.summary.label.payment"), t.L("rec.summary.payment", t.Money(s.NextPayment), when), MenuTone.Normal));
            }
            else
            {
                rows.Add(new SummaryRow("payment", t.L("rec.summary.label.payment"), t.L("rec.summary.noPayment"), MenuTone.Muted));
            }

            return rows;
        }
    }
}
