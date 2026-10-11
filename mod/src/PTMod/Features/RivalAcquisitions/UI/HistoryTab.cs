using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The tab "History" of the window "Deals" (TSK-015; FR-14 item 2; design.md "Окно «Сделки»"): the journal of the deals, the newest first - date, kind of deal,
    /// company, price, the share for a stake - with the mark "partial" for a deal where a step failed (its notes follow in a line of their own). What the rows say is
    /// decided by the pure <see cref="HistoryModel"/>.
    /// </summary>
    internal static class HistoryTab
    {
        // The tab stops of the columns of a row, in per cent of its width: date, kind, company, price, share, mark.
        private static readonly float[] ColumnStops = { 0f, 12f, 38f, 62f, 78f, 88f };

        internal static void Build(RectTransform body, DealsData data)
        {
            if (data.History.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("acq.history.empty"));
                return;
            }

            ViewFormat f = AcqUi.Format;
            string[] header =
            {
                ModL10n.L("acq.col.date"), ModL10n.L("acq.col.deal"), ModL10n.L("acq.col.company"), ModL10n.L("acq.col.price"),
                ModL10n.L("acq.col.share"), string.Empty,
            };
            ModUI.Text(body, AcqUi.Columns(header, ColumnStops), true);

            RectTransform list = ModUI.VerticalList(body, 2f) ?? body;
            foreach (HistoryRow row in data.History)
            {
                string[] cells =
                {
                    row.Date,
                    ModL10n.L(row.TypeKey),
                    AcqUi.CompanyMark(data.Colors, row.TargetSaveId) + AcqUi.Plain(row.TargetName),
                    f.M(row.Price),
                    row.Share > 0.0 ? f.P(row.Share) : ModL10n.L("acq.cell.none"),
                    row.Partial ? AcqUi.Colored(ModL10n.L("acq.history.partial"), ViewTone.Warning) : string.Empty,
                };
                TMP_Text line = ModUI.Text(list, AcqUi.Columns(cells, ColumnStops));
                if (line != null) line.richText = true;

                if (row.Partial && row.Notes.Length > 0)
                {
                    TMP_Text notes = ModUI.Text(list, ModL10n.L("acq.history.notes", AcqUi.Plain(row.Notes)));
                    AcqUi.Tint(notes, ViewTone.Warning);
                }
            }
        }
    }
}
