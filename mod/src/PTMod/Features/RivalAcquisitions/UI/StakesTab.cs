using System.Globalization;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The tab "My stakes" of the window "Deals" (TSK-015; FR-6; design.md "Окно «Сделки»"): the stakes the player holds and the rescue orders that are on, with
    /// the share, the money invested, the dividends in total and the last one; for the selected row the switch and the limit of the rescue order
    /// (<see cref="RescueOrderControls"/>) and the buttons "Buy more" and "To the company". What the rows say is decided by the pure <see cref="StakesModel"/>.
    /// </summary>
    internal static class StakesTab
    {
        private const float ButtonRowHeight = 38f;
        private const float ButtonWidth = 240f;

        // The tab stops of the columns of a row, in per cent of its width: company, status, share, invested, dividends, last dividend, rescue order.
        private static readonly float[] ColumnStops = { 0f, 22f, 44f, 54f, 66f, 78f, 90f };

        internal static void Build(RectTransform body, DealsData data)
        {
            if (data.Stakes.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("acq.stakes.empty"));
                return;
            }

            ModUI.Text(body, AcqUi.Columns(HeaderCells(), ColumnStops), true);
            RectTransform list = ModUI.VerticalList(body, 2f) ?? body;
            int selected = AcquisitionsWindow.Selected;
            foreach (StakeRow row in data.Stakes)
            {
                bool isSelected = row.UniqueId == selected;
                AddRow(list, row, data, isSelected);
                if (isSelected) AddDetails(list, row, data);
            }
        }

        private static string[] HeaderCells()
        {
            return new[]
            {
                ModL10n.L("acq.col.company"), ModL10n.L("acq.col.status"), ModL10n.L("acq.col.share"), ModL10n.L("acq.col.invested"),
                ModL10n.L("acq.col.dividends"), ModL10n.L("acq.col.lastDividend"), ModL10n.L("acq.col.rescue"),
            };
        }

        private static void AddRow(RectTransform list, StakeRow row, DealsData data, bool selected)
        {
            ViewFormat f = AcqUi.Format;
            CompanyRow company = DealsTable.Find(data.Rows, row.UniqueId);
            string status = company != null ? ModL10n.L(company.StatusKey, company.StatusArg ?? string.Empty) : string.Empty;
            string name = AcqUi.CompanyMark(data.Colors, row.SaveId) + AcqUi.Plain(row.Name);

            string rescue = row.OrderActive ? ModL10n.L("acq.cell.rescueOn", f.M(row.OrderMaxAmount)) : ModL10n.L("acq.cell.none");
            string[] cells =
            {
                name,
                status,
                row.Share > 0.0 ? f.P(row.Share) : ModL10n.L("acq.cell.none"),
                row.Share > 0.0 ? f.M(row.Invested) : ModL10n.L("acq.cell.none"),
                row.Share > 0.0 ? f.M(row.DividendsPaid) : ModL10n.L("acq.cell.none"),
                row.LastDividend > 0.0 ? f.M(row.LastDividend) : ModL10n.L("acq.cell.none"),
                rescue,
            };

            int uniqueId = row.UniqueId;
            ButtonUI button = ModUI.Button(list, AcqUi.Columns(cells, ColumnStops), () => AcquisitionsWindow.Select(uniqueId),
                selected ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null)
            {
                ModLog.Warn(AcqUi.Feature, "ui: a row of the stakes could not be made (the button template is missing)");
                return;
            }
            AcqUi.StyleRowButton(button);
        }

        private static void AddDetails(RectTransform list, StakeRow row, DealsData data)
        {
            RectTransform box = ModUI.VerticalList(list, 4f) ?? list;
            ViewFormat f = AcqUi.Format;

            if (row.Share > 0.0)
            {
                string since = ModL10n.L("acq.stakes.summary", f.P(row.Share), f.M(row.Invested), f.M(row.DividendsPaid));
                ModUI.Text(box, since);
            }

            bool playerOk = data.Player != null && !data.Player.InDefault && !data.Player.IsBankrupt;
            RescueOrderControls.Build(box, row.UniqueId, row.SaveId, row.CreditLimit, row.OrderActive, row.OrderMaxAmount, playerOk,
                () => AcquisitionsWindow.Reload());

            RectTransform buttons = AcqUi.Row(box, "PTAcqStakeButtons", ButtonRowHeight, 8f);
            ButtonUI more = ModUI.Button(buttons, ModL10n.L("acq.stakes.buyMore"), () => StakeDialog.Open(row.UniqueId, row.SaveId), ButtonStyle.Standard,
                ModL10n.L("acq.tip.openStake"));
            AcqUi.SetWidth(more, ButtonWidth, 0f);
            ButtonUI company = ModUI.Button(buttons, ModL10n.L("acq.stakes.toCompany"), () => AcquisitionsWindow.ShowCompany(row.UniqueId), ButtonStyle.Standard);
            AcqUi.SetWidth(company, ButtonWidth, 0f);
        }
    }
}
