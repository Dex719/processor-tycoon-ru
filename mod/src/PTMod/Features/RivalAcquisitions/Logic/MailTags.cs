using System;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The tags of the feature's mails (design.md, "Письма и уведомления"; FR-25 of the core: <c>acq:</c> is the prefix of the feature) and the
    /// ids of their actions. The id of a mail is never stored: a mail is found by its tag (<c>ModMail.Find</c>), and an action finds the company
    /// it is about from the tag.
    /// </summary>
    public static class MailTags
    {
        public const string Prefix = "acq:";

        public const string KindDefault = "default";
        public const string KindAuction = "auction";
        public const string KindDeal = "deal";
        public const string KindStakeLost = "stakeLost";
        public const string KindRescueFailed = "rescueFailed";

        /// <summary>The action "Deals" (opens the window on the company); reusable.</summary>
        public const string ActionDeals = "deals";

        /// <summary>The action "Rescue order" (opens the window on the company's stake); reusable.</summary>
        public const string ActionRescue = "rescue";

        public static string Default(int uniqueId)
        {
            return Tag(KindDefault, uniqueId);
        }

        public static string Auction(int uniqueId)
        {
            return Tag(KindAuction, uniqueId);
        }

        public static string Deal(int dealId)
        {
            return Tag(KindDeal, dealId);
        }

        public static string StakeLost(int uniqueId)
        {
            return Tag(KindStakeLost, uniqueId);
        }

        public static string RescueFailed(int uniqueId)
        {
            return Tag(KindRescueFailed, uniqueId);
        }

        /// <summary>
        /// Splits a tag of the feature: <c>acq:&lt;kind&gt;:&lt;number&gt;</c>. The number is a <c>UniqueID</c>, or the deal number for
        /// <see cref="KindDeal"/>. False for anything else.
        /// </summary>
        public static bool TryParse(string tag, out string kind, out int number)
        {
            kind = null;
            number = -1;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            string rest = tag.Substring(Prefix.Length);
            int colon = rest.IndexOf(':');
            if (colon <= 0 || colon == rest.Length - 1) return false;

            int parsed;
            if (!int.TryParse(rest.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out parsed)) return false;

            kind = rest.Substring(0, colon);
            number = parsed;
            return true;
        }

        private static string Tag(string kind, int number)
        {
            return Prefix + kind + ":" + number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
