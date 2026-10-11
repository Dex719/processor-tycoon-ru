using System;
using System.Collections.Generic;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>One line of the "Journal" tab.</summary>
    internal sealed class JournalRow
    {
        public JournalRow(string date, string operation, string amountText, double amount, string details, MenuTone tone)
        {
            Date = date;
            Operation = operation;
            AmountText = amountText;
            Amount = amount;
            Details = details;
            Tone = tone;
        }

        /// <summary>The game date of the operation, written the way the game writes dates (the stored text when it cannot be read).</summary>
        public string Date { get; private set; }

        /// <summary>The name of the operation in the language of the player.</summary>
        public string Operation { get; private set; }

        /// <summary>The signed sum as text, <c>+$1.2M</c> or <c>-$420K</c>; a dash for an operation that moves no money.</summary>
        public string AmountText { get; private set; }

        public double Amount { get; private set; }

        /// <summary>The note of the record (<c>12m @ 30%</c>, a loan id); empty when none.</summary>
        public string Details { get; private set; }

        /// <summary>Green for money that came, red for money that went, muted for the rest.</summary>
        public MenuTone Tone { get; private set; }
    }

    /// <summary>
    /// The "Journal" tab (FR-10 item 2, design.md "UI"): the last <see cref="Shown"/> records of the journal, the newest first, each with the name of its operation. The
    /// journal itself (<see cref="RecoveryJournal"/>) keeps 200 records, oldest first; the tab shows fewer so that it builds quickly. Pure: the words come from
    /// <see cref="IMenuText"/>.
    /// </summary>
    internal static class JournalLines
    {
        /// <summary>How many records the tab shows (design.md: 50).</summary>
        public const int Shown = 50;

        /// <summary>The key of the name of an operation the table does not know (the code goes in as the argument).</summary>
        public const string UnknownKey = "rec.journal.op.unknown";

        private const string OpPrefix = "rec.journal.op.";

        /// <summary>The operation codes the feature writes (and <c>disable.convert</c> of the switch-off; in a debug build also the two codes of the debug actions). Each has a name <c>rec.journal.op.&lt;code&gt;</c> in the string tables.</summary>
        public static readonly IReadOnlyList<string> KnownOps = new[]
        {
            "bridge.open", "bridge.fee",
            "line.open", "line.draw", "line.autodraw", "line.interest", "line.sweep", "line.repay", "line.convert",
            "loan.interest", "loan.principal", "loan.prepay",
            "bailout.loan", "bailout.investor", "investor.payment",
            "lines.sell", "stock.sell", "cpu.retire",
            "disable.convert",
#if PTMOD_DEBUG
            "debug.balance", "debug.days",   // the debug actions of the hub page (TSK-019); only a debug build writes them
#endif
        };

        /// <summary>The key of the name of a known operation; null for a code that is not in <see cref="KnownOps"/>.</summary>
        public static string KeyOf(string op)
        {
            if (string.IsNullOrEmpty(op)) return null;
            for (int i = 0; i < KnownOps.Count; i++)
            {
                if (string.Equals(KnownOps[i], op, StringComparison.Ordinal)) return OpPrefix + op;
            }
            return null;
        }

        /// <summary>
        /// The last <paramref name="max"/> records, newest first. A null list gives an empty one; a null record is skipped. The records are not changed.
        /// </summary>
        public static IReadOnlyList<JournalRow> Latest(IMenuText t, IReadOnlyList<JournalEntryData> entries, int max = Shown)
        {
            var rows = new List<JournalRow>();
            if (entries == null || max < 1) return rows;

            for (int i = entries.Count - 1; i >= 0 && rows.Count < max; i--)
            {
                JournalEntryData entry = entries[i];
                if (entry == null) continue;
                rows.Add(Row(t, entry));
            }
            return rows;
        }

        private static JournalRow Row(IMenuText t, JournalEntryData entry)
        {
            string key = KeyOf(entry.op);
            string name = key != null ? t.L(key) : t.L(UnknownKey, entry.op ?? string.Empty);

            DateTime parsed;
            string date = RecoveryDates.TryParse(entry.date, out parsed) ? t.Date(parsed) : (entry.date ?? string.Empty);

            double amount = double.IsNaN(entry.amount) || double.IsInfinity(entry.amount) ? 0.0 : entry.amount;
            string amountText;
            MenuTone tone;
            if (amount > 0.0)
            {
                amountText = "+" + t.Money(amount);
                tone = MenuTone.Good;
            }
            else if (amount < 0.0)
            {
                amountText = t.Money(amount);
                tone = MenuTone.Bad;
            }
            else
            {
                amountText = "-";
                tone = MenuTone.Muted;
            }

            return new JournalRow(date, name, amountText, amount, entry.details ?? string.Empty, tone);
        }
    }
}
