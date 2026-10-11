using System;
using System.Globalization;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The tab "Companies" of the window "Deals" (TSK-015; FR-2; design.md "Окно «Сделки»"): a table of the companies of the game - colour and name, status, money, lines,
    /// active CPUs, market share, valuation E and the player's share - and, for the selected row, its details and the buttons of the deals with their price. A deal
    /// that is closed has its button inactive and the reason in the tooltip (<c>ModUI.Tooltip</c>); when the player is in default every button is inactive with
    /// that reason (FR-2 item 4). A click on a row opens it, a second click closes it. What the rows say is decided by the pure <see cref="DealsTable"/>.
    /// </summary>
    internal static class CompaniesTab
    {
        private const float OfferRowHeight = 38f;
        private const float OfferWidth = 330f;
        private const float RowStep = 36f;
        private const float HeaderHeight = 40f;

        // The tab stops of the columns of a row, in per cent of its width: company, status, money, lines, CPUs, market share, valuation, the player's share.
        private static readonly float[] ColumnStops = { 0f, 19f, 44f, 56f, 62f, 68f, 75f, 88f };

        /// <summary>How far the body is scrolled so that the row with this index (from 0) is near the top.</summary>
        internal static float OffsetOfRow(int index)
        {
            return Mathf.Max(0f, HeaderHeight + (index - 1) * RowStep);
        }

        /// <summary>Builds the tab into the emptied body of the window.</summary>
        internal static void Build(RectTransform body, DealsData data, int selected)
        {
            if (data.Player != null && (data.Player.InDefault || data.Player.IsBankrupt))
            {
                TMP_Text warning = ModUI.Text(body, ModL10n.L(AcqKeys.ReasonPlayerDefault), true);
                AcqUi.Tint(warning, ViewTone.Negative);
            }

            if (data.Rows.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("acq.companies.empty"));
                return;
            }

            ModUI.Text(body, AcqUi.Columns(HeaderCells(), ColumnStops), true);
            RectTransform list = ModUI.VerticalList(body, 2f) ?? body;
            foreach (CompanyRow row in data.Rows)
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
                ModL10n.L("acq.col.company"), ModL10n.L("acq.col.status"), ModL10n.L("acq.col.money"), ModL10n.L("acq.col.lines"),
                ModL10n.L("acq.col.cpus"), ModL10n.L("acq.col.marketShare"), ModL10n.L("acq.col.valuation"), ModL10n.L("acq.col.mine"),
            };
        }

        // ---- a row ----

        private static void AddRow(RectTransform list, CompanyRow row, DealsData data, bool selected)
        {
            ViewFormat f = AcqUi.Format;
            bool gone = row.Status == CompanyStatus.Bankrupt || row.Status == CompanyStatus.BankruptAuction || row.Status == CompanyStatus.Absorbed;

            string name = AcqUi.CompanyMark(data.Colors, row.SaveId) + AcqUi.Plain(row.Name);
            string status = AcqUi.Colored(ModL10n.L(row.StatusKey, row.StatusArg ?? string.Empty), ToneOf(row.Status));
            string money = gone ? ModL10n.L("acq.cell.none") : AcqUi.Colored(f.M(row.Money), row.Money < 0.0 ? ViewTone.Negative : ViewTone.Neutral);
            string lines = row.Lines.ToString(CultureInfo.InvariantCulture);
            string cpus = row.ActiveCpus.ToString(CultureInfo.InvariantCulture);
            string share = f.P(row.MarketShare);
            string value = f.M(row.Valuation.E);
            string mine = row.OwnedShare > 0.0 ? f.P(row.OwnedShare) : ModL10n.L("acq.cell.none");

            string caption = AcqUi.Columns(new[] { name, status, money, lines, cpus, share, value, mine }, ColumnStops);
            int uniqueId = row.UniqueId;
            ButtonUI button = ModUI.Button(list, caption, () => AcquisitionsWindow.Select(uniqueId), selected ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null)
            {
                ModLog.Warn(AcqUi.Feature, "ui: a row of the table could not be made (the button template is missing)");
                return;
            }
            AcqUi.StyleRowButton(button);
        }

        private static ViewTone ToneOf(CompanyStatus status)
        {
            switch (status)
            {
                case CompanyStatus.InDefault: return ViewTone.Warning;
                case CompanyStatus.BankruptAuction: return ViewTone.Warning;
                case CompanyStatus.Bankrupt: return ViewTone.Negative;
                case CompanyStatus.Absorbed: return ViewTone.Positive;
                default: return ViewTone.Neutral;
            }
        }

        // ---- the details of the selected row ----

        private static void AddDetails(RectTransform list, CompanyRow row, DealsData data)
        {
            ViewFormat f = AcqUi.Format;
            RectTransform box = ModUI.VerticalList(list, 4f) ?? list;

            bool gone = row.Status == CompanyStatus.Bankrupt || row.Status == CompanyStatus.BankruptAuction || row.Status == CompanyStatus.Absorbed;
            if (row.Status != CompanyStatus.Absorbed)
            {
                string facts = ModL10n.L("acq.details.facts", gone ? ModL10n.L("acq.cell.none") : f.M(row.Money), f.M(row.CreditLimit),
                    row.Lines.ToString(CultureInfo.InvariantCulture), row.ActiveCpus.ToString(CultureInfo.InvariantCulture), f.P(row.MarketShare));
                ModUI.Text(box, facts);

                Valuation v = row.Valuation;
                ModUI.Text(box, ModL10n.L("acq.details.valuation", f.M(v.E), f.M(v.AFab), f.M(v.ATech), f.M(v.ACpu), f.M(v.SharePremium)));
            }

            if (row.OwnedShare > 0.0) ModUI.Text(box, ModL10n.L("acq.details.owned", f.P(row.OwnedShare)));
            if (row.RescueOrderActive) ModUI.Text(box, ModL10n.L("acq.details.rescueOn"));

            if (!row.HasOffers)
            {
                TMP_Text none = ModUI.Text(box, ModL10n.L(row.Status == CompanyStatus.Absorbed ? AcqKeys.ReasonAbsorbed : AcqKeys.ReasonUnsupported));
                AcqUi.Tint(none, ViewTone.Warning);
                return;
            }

            RectTransform buttons = AcqUi.Row(box, "PTAcqOffers", OfferRowHeight, 8f);
            bool busy = AcquisitionExecutor.IsBusy;
            foreach (DealOffer offer in row.Offers) AddOffer(buttons, row, offer, busy);

            if (!row.AnyAvailable)
            {
                // Every deal is closed: say why in the text as well (the first reason), the tooltips of the buttons have each one's own.
                DealOffer first = row.Offers[0];
                TMP_Text closed = ModUI.Text(box, ModL10n.L("acq.details.allClosed", ReasonText.Of(f, first.ReasonKey, first.ReasonArg)));
                AcqUi.Tint(closed, ViewTone.Warning);
            }
        }

        private static void AddOffer(RectTransform buttons, CompanyRow row, DealOffer offer, bool busy)
        {
            ViewFormat f = AcqUi.Format;
            bool open = offer.Available && !busy;
            bool purchase = !offer.IsStake;
            ButtonUI button = ModUI.Button(buttons, Caption(offer), () => OnOffer(row, offer), open && purchase ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null)
            {
                ModLog.Warn(AcqUi.Feature, "ui: a deal button could not be made (the button template is missing)");
                return;
            }

            AcqUi.SetWidth(button, OfferWidth, 0f);
            AcqUi.StyleDealButton(button);
            button.Interactable = open;

            if (offer.Available)
            {
                AcqUi.Tip(button, Caption(offer), ModL10n.L(purchase ? "acq.tip.openPreview" : "acq.tip.openStake"));
            }
            else
            {
                AcqUi.Tip(button, ModL10n.L("acq.tip.closed"), ReasonText.Of(f, offer.ReasonKey, offer.ReasonArg));
            }
        }

        /// <summary>The caption of a deal button: the deal and its price (design.md keys <c>deal.*</c>); without a price when the company is not of the kind the deal is for.</summary>
        internal static string Caption(DealOffer offer)
        {
            bool priced = offer.Price > 0.0;
            string price = AcquisitionMail.Money(offer.Price);
            switch (offer.Type)
            {
                case DealType.BankruptAssets:
                    return priced ? ModL10n.L(UiKeys.DealBankrupt, price) : ModL10n.L(UiKeys.DealBankruptPlain);
                case DealType.Distressed:
                    return priced ? ModL10n.L(UiKeys.DealDistressed, price) : ModL10n.L(UiKeys.DealDistressedPlain);
                case DealType.Hostile:
                    return priced ? ModL10n.L(UiKeys.DealHostile, price) : ModL10n.L(UiKeys.DealHostilePlain);
                default:
                    return priced ? ModL10n.L(UiKeys.DealStakeFrom, price) : ModL10n.L(UiKeys.DealStake);
            }
        }

        private static void OnOffer(CompanyRow row, DealOffer offer)
        {
            if (!offer.Available || AcquisitionExecutor.IsBusy) return;

            if (offer.IsStake) StakeDialog.Open(row.UniqueId, row.SaveId);
            else DealPreviewWindow.Open(offer.Type, row.UniqueId, row.SaveId);
        }
    }
}
