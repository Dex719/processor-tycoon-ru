using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;
using TMPro;
using UnityEngine;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The "Journal" tab of the rescue menu (FR-10 item 2; design.md "UI"): the last 50 records of the journal of the feature, the newest first, each with the date, the name of
    /// the operation, the signed sum and the note. The records are the feature's own operations (the calls of other features go to the log of the mod, not here); the
    /// text of each row is <see cref="JournalLines"/>'s. The tab only reads the journal.
    /// </summary>
    internal static partial class RecoveryWindow
    {
        private const float DateWidth = 120f;
        private const float AmountWidth = 120f;
        private const float DetailsWidth = 200f;

        private static void BuildJournal(BankruptcyRecoveryFeature feature)
        {
            RecoveryUi.Heading(_body, L("rec.journal.title"));

            IReadOnlyList<JournalRow> rows = JournalLines.Latest(GameMenuText.Instance, feature.Journal.Snapshot());
            if (rows.Count == 0)
            {
                RecoveryUi.Line(_body, L("rec.journal.empty"), MenuTone.Muted);
                return;
            }

            RectTransform head = RecoveryUi.Row(_body, 22f, 6f);
            JournalCell(head, RecoveryUi.Paint(L("rec.journal.col.date"), MenuTone.Muted), DateWidth, 0f, TextAlignmentOptions.MidlineLeft);
            JournalCell(head, RecoveryUi.Paint(L("rec.journal.col.operation"), MenuTone.Muted), 0f, 1f, TextAlignmentOptions.MidlineLeft);
            JournalCell(head, RecoveryUi.Paint(L("rec.journal.col.amount"), MenuTone.Muted), AmountWidth, 0f, TextAlignmentOptions.MidlineRight);
            JournalCell(head, RecoveryUi.Paint(L("rec.journal.col.details"), MenuTone.Muted), DetailsWidth, 0f, TextAlignmentOptions.MidlineLeft);

            foreach (JournalRow row in rows)
            {
                RectTransform line = RecoveryUi.Row(_body, 24f, 6f);
                JournalCell(line, row.Date, DateWidth, 0f, TextAlignmentOptions.MidlineLeft);
                JournalCell(line, RecoveryUi.Safe(row.Operation), 0f, 1f, TextAlignmentOptions.MidlineLeft);
                JournalCell(line, RecoveryUi.Paint(row.AmountText, row.Tone), AmountWidth, 0f, TextAlignmentOptions.MidlineRight);
                JournalCell(line, RecoveryUi.Paint(RecoveryUi.Safe(row.Details), MenuTone.Muted), DetailsWidth, 0f, TextAlignmentOptions.MidlineLeft);
            }

            RecoveryUi.Line(_body, L("rec.journal.footer", RecoveryUi.Num(rows.Count), RecoveryUi.Num(JournalLines.Shown)), MenuTone.Muted);
        }

        private static void JournalCell(Transform row, string richText, float width, float flexible, TextAlignmentOptions align)
        {
            RecoveryUi.Cell(row, richText, width, flexible, align);
        }
    }
}
