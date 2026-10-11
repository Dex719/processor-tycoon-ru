using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The terms of a deal between companies as the negotiation window shows them (FR-10 item 2); the gate fills it from the fields of <c>ContractNegotiationWindow</c>.</summary>
    public struct DealInput
    {
        /// <summary>The company that provides the production lines (the foundry).</summary>
        public string Provider;

        /// <summary>The company that buys them.</summary>
        public string Client;

        /// <summary>The length of the deal in years.</summary>
        public int Years;

        /// <summary>The share of the provider's lines, percent.</summary>
        public int CapacityPercent;

        /// <summary>The mark-up on the price, percent.</summary>
        public int MarkupPercent;

        public bool Exclusive;

        /// <summary>What the provider pays for each month it falls short (the window's <c>providerFines</c>).</summary>
        public double ProviderFines;

        /// <summary>What the client pays for breaking the exclusivity (the window's <c>clientFines</c>; 0 without it).</summary>
        public double ClientFines;

        /// <summary>The window's verdict, the sum of the scores of the other side (positive: they will accept).</summary>
        public int AcceptanceScore;
    }

    /// <summary>
    /// The question before a deal is signed (FR-10 item 2): the provider, the client, the length in years, the share of lines, the mark-up, the exclusivity, the fines of both sides and the other side's
    /// verdict, as lines of the text of <c>ModUI.Confirm</c>; and the same terms as the numbers of the log entry "deal signed".
    /// </summary>
    public static class DealLines
    {
        public static List<PanelLine> Build(DealInput deal)
        {
            var lines = new List<PanelLine>();
            lines.Add(new PanelLine("ct.business.provider", LineTone.Normal, PanelArg.OfText(deal.Provider)));
            lines.Add(new PanelLine("ct.business.client", LineTone.Normal, PanelArg.OfText(deal.Client)));
            lines.Add(new PanelLine("ct.business.term", LineTone.Normal, PanelArg.Whole(deal.Years)));
            lines.Add(new PanelLine("ct.business.share", LineTone.Normal, PanelArg.Percent(deal.CapacityPercent)));
            lines.Add(new PanelLine("ct.business.markup", LineTone.Normal, PanelArg.Percent(deal.MarkupPercent)));
            lines.Add(new PanelLine("ct.business.exclusive", LineTone.Normal, PanelArg.OfKey(deal.Exclusive ? "ct.log.yes" : "ct.log.no")));
            lines.Add(new PanelLine("ct.business.providerFines", LineTone.Normal, PanelArg.Money(deal.ProviderFines)));
            if (deal.Exclusive) lines.Add(new PanelLine("ct.business.clientFines", LineTone.Normal, PanelArg.Money(deal.ClientFines)));
            lines.Add(new PanelLine("ct.business.score", deal.AcceptanceScore > 0 ? LineTone.Good : LineTone.Bad, PanelArg.Whole(deal.AcceptanceScore)));
            return lines;
        }

        /// <summary>The log entry "deal signed" with the terms as numbers (FR-10 item 3).</summary>
        public static LogEntry LogEntryOf(DealInput deal, string date)
        {
            LogEntry entry = LogEntry.Create(date, LogTypes.BusinessSigned, LogSources.Manual);
            entry.reasonKey = "ct.reason.businessSigned";
            entry.contractLabel = TextSafe.Clean(deal.Provider + " > " + deal.Client);
            entry.Put(LogDataKeys.Provider, deal.Provider ?? string.Empty);
            entry.Put(LogDataKeys.Client, deal.Client ?? string.Empty);
            entry.Put(LogDataKeys.Years, deal.Years);
            entry.Put(LogDataKeys.Share, deal.CapacityPercent);
            entry.Put(LogDataKeys.Markup, deal.MarkupPercent);
            entry.Put(LogDataKeys.Exclusive, deal.Exclusive ? 1 : 0);
            entry.Put(LogDataKeys.ProviderFines, deal.ProviderFines);
            entry.Put(LogDataKeys.ClientFines, deal.ClientFines);
            entry.Put(LogDataKeys.Score, deal.AcceptanceScore);
            return entry;
        }
    }
}
