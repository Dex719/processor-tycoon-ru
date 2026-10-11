namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The kinds of entry of the contract log (<c>LogEntry.type</c>; design.md, "DTO состояния"). The names are what the side save file holds, so they never change;
    /// <see cref="LogFilters"/> sorts them into the tabs of the log window.
    /// </summary>
    public static class LogTypes
    {
        /// <summary>The player sent an offer by hand (after the review screen, or with the confirmation off).</summary>
        public const string OfferSent = "OfferSent";

        /// <summary>The player pressed "Cancel" on the review screen: no offer was sent (FR-3 item 5).</summary>
        public const string OfferCancelledOnReview = "OfferCancelledOnReview";

        /// <summary>A manual offer went through without a screen: no risk was found, or the mode is "never ask" (FR-4 items 2 and 3).</summary>
        public const string OfferAutoConfirmed = "OfferAutoConfirmed";

        /// <summary>The decision of the contract automation about one pair CPU x contract (FR-6 item 1).</summary>
        public const string AutomationDecision = "AutomationDecision";

        /// <summary>The line under a run of the automation: how many offers were sent and skipped (FR-6 item 4).</summary>
        public const string AutomationSummary = "AutomationSummary";

        /// <summary>The game withdrew an offer of the player for lack of production lines (FR-7 item 1).</summary>
        public const string AutoRejected = "AutoRejected";

        /// <summary>The result of a tender the player took part in (FR-5); the details are in the tender list of the save.</summary>
        public const string TenderResult = "TenderResult";

        /// <summary>A deal between companies was signed (FR-10).</summary>
        public const string BusinessSigned = "BusinessSigned";

        /// <summary>The mod and the game disagree (FR-6 item 5, FR-12).</summary>
        public const string Mismatch = "Mismatch";

        /// <summary>True for one of the nine names above.</summary>
        public static bool IsKnown(string type)
        {
            switch (type)
            {
                case OfferSent:
                case OfferCancelledOnReview:
                case OfferAutoConfirmed:
                case AutomationDecision:
                case AutomationSummary:
                case AutoRejected:
                case TenderResult:
                case BusinessSigned:
                case Mismatch:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Who caused an entry (<c>LogEntry.source</c>).</summary>
    public static class LogSources
    {
        /// <summary>The player, by hand.</summary>
        public const string Manual = "Manual";

        /// <summary>The "contract automation" switch of the game (31 December).</summary>
        public const string Automation = "Automation";

        /// <summary>Another feature, through the API of the mod.</summary>
        public const string Api = "Api";

        /// <summary>The game itself (a draw, the withdrawal of an offer).</summary>
        public const string Game = "Game";
    }

    /// <summary>The names of the numbers an entry carries in <c>LogEntry.data</c>. The window of the log (a later task) reads them by these names.</summary>
    public static class LogDataKeys
    {
        public const string Units = "units";
        public const string Payment = "payment";
        public const string UnitCost = "unitCost";
        public const string Margin = "margin";
        public const string Threshold = "threshold";
        public const string Chance = "chance";
        public const string Actual = "actual";
        public const string Risks = "risks";
        public const string Required = "required";
        public const string Contracted = "contracted";
        public const string Capacity = "capacity";
        public const string Maker = "maker";
        public const string Own = "own";
        public const string Outcome = "outcome";
        public const string Winner = "winner";
        public const string WinnerCpu = "winnerCpu";
        public const string Failed = "failed";
        public const string Predicted = "predicted";
        public const string Observed = "observed";
        public const string Sent = "sent";
        public const string Skipped = "skipped";
        public const string SkippedMargin = "skippedMargin";
        public const string SkippedRequirements = "skippedRequirements";
        public const string SkippedOther = "skippedOther";
        public const string AlreadyOffered = "alreadyOffered";
        public const string Mismatches = "mismatches";

        // A deal between companies (FR-10): the terms the player signed.
        public const string Provider = "provider";
        public const string Client = "client";
        public const string Years = "years";
        public const string Share = "share";
        public const string Markup = "markup";
        public const string Exclusive = "exclusive";
        public const string ProviderFines = "providerFines";
        public const string ClientFines = "clientFines";
        public const string Score = "score";
    }

    /// <summary>The tabs of the log window (FR-8 item 2): "All", "My actions", "Automation", "Auto-cancel", "Tender results", "Deals between companies".</summary>
    public enum LogFilter
    {
        All = 0,
        Manual = 1,
        Automation = 2,
        AutoReject = 3,
        Tenders = 4,
        Business = 5,
    }

    /// <summary>Which entries a <see cref="LogFilter"/> lets through.</summary>
    public static class LogFilters
    {
        /// <summary>True when an entry of this type shows under the filter. An unknown filter shows everything.</summary>
        public static bool Matches(LogFilter filter, string entryType)
        {
            switch (filter)
            {
                case LogFilter.Manual:
                    return entryType == LogTypes.OfferSent || entryType == LogTypes.OfferCancelledOnReview || entryType == LogTypes.OfferAutoConfirmed;
                case LogFilter.Automation:
                    return entryType == LogTypes.AutomationDecision || entryType == LogTypes.AutomationSummary || entryType == LogTypes.Mismatch;
                case LogFilter.AutoReject:
                    return entryType == LogTypes.AutoRejected;
                case LogFilter.Tenders:
                    return entryType == LogTypes.TenderResult;
                case LogFilter.Business:
                    return entryType == LogTypes.BusinessSigned;
                default:
                    return true;
            }
        }
    }
}
