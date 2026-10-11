using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The mails of the feature (design.md "Письма и уведомления"; FR-6, FR-11, FR-13): all with the priority <c>Normal</c> and a tag <c>acq:*</c>
    /// (<see cref="MailTags"/>), texts from the tables (<see cref="AcqKeys"/>) with the arguments formatted when the mail is sent (money with the game's
    /// formatter, dates <c>dd.MM.yyyy</c>). The actions are reusable (<c>ModMailAction</c>): a click opens the "Deals" window on the company and never
    /// closes the mail. The id of a mail is never kept: a mail is found by its tag.
    /// </summary>
    internal static class AcquisitionMail
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        /// <summary>"X is in default" (FR-11 item 1): the actions "Deals" and, when the player has a stake, "Rescue order".</summary>
        internal static void Default(GameAiCompany company, bool withRescue)
        {
            var actions = new List<ModMailAction> { DealsAction() };
            if (withRescue) actions.Add(new ModMailAction { Id = MailTags.ActionRescue, LabelKey = AcqKeys.MailActionRescueOrder, Repeatable = true });

            Send(MailTags.Default(company.UniqueID), AcqKeys.MailDefaultSubject, AcqKeys.MailDefaultBody, actions,
                Name(company), DistressRules.DaysToBankruptcy.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>"X: assets up for auction" with the last day (FR-3 item 1).</summary>
        internal static void Auction(GameAiCompany company, AuctionData auction)
        {
            Send(MailTags.Auction(company.UniqueID), AcqKeys.MailAuctionSubject, AcqKeys.MailAuctionBody, new List<ModMailAction> { DealsAction() },
                Name(company), Date(auction != null ? auction.until : ""));
        }

        /// <summary>The summary of a purchase of a whole company (tag <c>acq:deal:&lt;dealId&gt;</c>).</summary>
        internal static void Deal(DealRecord record)
        {
            if (record == null) return;
            string subject;
            switch (record.type)
            {
                case "BankruptAssets": subject = AcqKeys.MailDealSubjectBankrupt; break;
                case "Distressed": subject = AcqKeys.MailDealSubjectDistressed; break;
                default: subject = AcqKeys.MailDealSubjectHostile; break;
            }

            int lines = 0;
            if (record.lines != null)
            {
                foreach (WaferLines entry in record.lines) lines += entry != null ? entry.lines : 0;
            }

            Send(MailTags.Deal(record.id), subject, record.partial ? AcqKeys.MailDealBodyPartial : AcqKeys.MailDealBody, new List<ModMailAction> { DealsAction() },
                record.targetName ?? "",
                Money(record.price),
                Money(record.assumedCash),
                Count(lines),
                Count(record.techCount),
                Count(record.cpuSaveIds != null ? record.cpuSaveIds.Count : 0),
                Count(record.providerContracts + record.clientContracts));
        }

        /// <summary>The rescue order was carried out (tag <c>acq:deal:&lt;dealId&gt;</c>): the injection and the stake after it.</summary>
        internal static void Rescue(DealRecord record, double shareNow)
        {
            if (record == null) return;
            Send(MailTags.Deal(record.id), AcqKeys.MailRescueSubject, AcqKeys.MailRescueBody, new List<ModMailAction> { DealsAction() },
                record.targetName ?? "", Money(record.price), Percent(shareNow));
        }

        /// <summary>"The stake in X is written off" (FR-6 item 5).</summary>
        internal static void StakeLost(int uniqueId, string name, double share)
        {
            Send(MailTags.StakeLost(uniqueId), AcqKeys.MailStakeLostSubject, AcqKeys.MailStakeLostBody, null, name ?? "", Percent(share));
        }

        /// <summary>"Could not rescue X: reason" (FR-6 item 4); the reason is the text of the key of <see cref="RescueDecision"/>.</summary>
        internal static void RescueFailed(GameAiCompany company, RescueDecision decision)
        {
            string reason = ReasonText(decision.ReasonKey, decision.ReasonArg);
            Send(MailTags.RescueFailed(company.UniqueID), AcqKeys.MailRescueFailedSubject, AcqKeys.StakeRescueFailed, new List<ModMailAction> { DealsAction() },
                Name(company), reason);
        }

        /// <summary>
        /// The handler of the actions of the feature's mails (<c>ModMail.RegisterActionHandler</c>): "Deals" opens the window on the company of the mail (the deal's
        /// target for a deal summary), "Rescue order" opens it on the tab "My stakes" with the company selected. The mail stays open (the actions are reusable); the
        /// letter is found by its tag and its id is never kept, so from the mail to the preview of a deal are two clicks: the action, then the button of the deal.
        /// </summary>
        internal static void OnAction(ModMailRecord record, string actionId)
        {
            if (record == null) return;
            string kind;
            int number;
            if (!MailTags.TryParse(record.Tag, out kind, out number))
            {
                RivalAcquisitionsApi.OpenWindow(-1);
                return;
            }

            int uniqueId = number;
            if (kind == MailTags.KindDeal)
            {
                uniqueId = -1;
                RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
                if (feature != null)
                {
                    foreach (DealRecord deal in feature.State.deals)
                    {
                        if (deal != null && deal.id == number) uniqueId = deal.targetUniqueId;
                    }
                }
            }

            if (actionId == MailTags.ActionRescue && uniqueId >= 0 && RivalAcquisitionsApi.IsAvailable)
            {
                AcquisitionsWindow.OpenRescue(uniqueId);
                return;
            }
            RivalAcquisitionsApi.OpenWindow(uniqueId);
        }

        // ---- formatting ----

        /// <summary>An amount as the game writes money (<c>StringFormatter.FloatToMoney</c>).</summary>
        internal static string Money(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            return GameFormatter.FloatToMoney((float)value, "", true, true, true);
        }

        /// <summary>A share as a number of percent ("35", "10.5"); the table puts the sign.</summary>
        internal static string Percent(double share)
        {
            if (double.IsNaN(share) || double.IsInfinity(share)) share = 0.0;
            return (share * 100.0).ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>A stored date (<c>yyyy-MM-dd</c>) as the deals show dates (<c>dd.MM.yyyy</c>).</summary>
        internal static string Date(string stored)
        {
            DateTime date;
            return DateTime.TryParseExact(stored, AcquisitionsState.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                ? date.ToString(DealRules.DateFormat, CultureInfo.InvariantCulture)
                : stored ?? "";
        }

        /// <summary>The text of a reason key; its argument (a whole number of dollars) as money.</summary>
        internal static string ReasonText(string key, string arg)
        {
            if (string.IsNullOrEmpty(key)) key = AcqKeys.ReasonUnsupported;
            double amount;
            if (arg != null && double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return ModL10n.L(key, Money(amount));
            return arg != null ? ModL10n.L(key, arg) : ModL10n.L(key);
        }

        private static string Count(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Name(GameAiCompany company)
        {
            return company != null ? company.Name ?? "" : "";
        }

        private static ModMailAction DealsAction()
        {
            return new ModMailAction { Id = MailTags.ActionDeals, LabelKey = AcqKeys.MailActionDeals, Repeatable = true };
        }

        private static void Send(string tag, string titleKey, string bodyKey, List<ModMailAction> actions, params string[] args)
        {
            int id = ModMail.Send(new ModMailMessage
            {
                FeatureId = Feature,
                Tag = tag,
                TitleKey = titleKey,
                BodyKey = bodyKey,
                Args = args,
                Priority = MailPriority.Normal,
                Actions = actions,
            });
            ModLog.Debug(Feature, "mail: " + tag + " sent (" + id.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }
}
