using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The warnings of the review screen (FR-3 item 3): one line for each risk found in <see cref="CommitmentReport.Risks"/>, with the numbers the player needs to judge it. The order is the order of
    /// the flags in <see cref="RiskFlags"/>; the screens print the lines with the keys <c>ct.risk.*</c>. Pure; the same lines serve the review window and the plain question that stands in for it.
    /// </summary>
    public static class RiskLines
    {
        /// <summary>The key of the short name of a risk (for the details of a log entry that stores the flags as a number).</summary>
        public static string NameKey(RiskFlags flag)
        {
            switch (flag)
            {
                case RiskFlags.CapacityShortNow: return "ct.risk.name.capacity";
                case RiskFlags.Overcommit: return "ct.risk.name.overcommit";
                case RiskFlags.NegativeProfit: return "ct.risk.name.negativeProfit";
                case RiskFlags.BelowAutomationMargin: return "ct.risk.name.belowMargin";
                case RiskFlags.LowActualChance: return "ct.risk.name.lowChance";
                case RiskFlags.BreakFineExceedsCash: return "ct.risk.name.breakFine";
                case RiskFlags.AutoRejectWillWithdraw: return "ct.risk.name.autoReject";
                default: return string.Empty;
            }
        }

        /// <summary>The flags in the order the screen lists them.</summary>
        public static readonly RiskFlags[] Order =
        {
            RiskFlags.CapacityShortNow,
            RiskFlags.Overcommit,
            RiskFlags.NegativeProfit,
            RiskFlags.BelowAutomationMargin,
            RiskFlags.LowActualChance,
            RiskFlags.BreakFineExceedsCash,
            RiskFlags.AutoRejectWillWithdraw,
        };

        /// <summary>
        /// The comma separated keys of the short names of the risks in <paramref name="flags"/> (the number a log entry stores); empty for no risk.
        /// </summary>
        public static string NameKeys(int flags)
        {
            var keys = new List<string>();
            foreach (RiskFlags flag in Order)
            {
                if (((int)flag & flags) != 0) keys.Add(NameKey(flag));
            }
            return string.Join(",", keys.ToArray());
        }

        /// <summary>
        /// The warning lines for the risks of <paramref name="report"/>; empty when none was found. <paramref name="actual"/> is the probability of winning (0..1), <paramref name="companyCash"/>
        /// the money of the offering company, <paramref name="thresholdPercent"/> the margin threshold of the contract automation in percent.
        /// </summary>
        public static List<PanelLine> Build(CommitmentReport report, double actual, double companyCash, int thresholdPercent)
        {
            var lines = new List<PanelLine>();
            RiskFlags risks = report.Risks;
            if (risks == RiskFlags.None) return lines;

            if ((risks & RiskFlags.CapacityShortNow) != 0)
                lines.Add(new PanelLine("ct.risk.capacity", LineTone.Bad, PanelArg.Whole(report.LinesShortNow)));
            if ((risks & RiskFlags.Overcommit) != 0)
                lines.Add(new PanelLine("ct.risk.overcommit", LineTone.Warn));
            if ((risks & RiskFlags.NegativeProfit) != 0)
                lines.Add(new PanelLine("ct.risk.negativeProfit", LineTone.Bad, PanelArg.Money(report.MonthlyProfit)));
            if ((risks & RiskFlags.BelowAutomationMargin) != 0)
                lines.Add(new PanelLine("ct.risk.belowMargin", LineTone.Warn, PanelArg.Fraction(report.Margin), PanelArg.Whole(thresholdPercent)));
            if ((risks & RiskFlags.LowActualChance) != 0)
                lines.Add(new PanelLine("ct.risk.lowChance", LineTone.Warn, PanelArg.Actual(actual)));
            if ((risks & RiskFlags.BreakFineExceedsCash) != 0)
                lines.Add(new PanelLine("ct.risk.breakFine", LineTone.Bad, PanelArg.Money(report.BreakFineAtStart), PanelArg.Money(companyCash)));
            if ((risks & RiskFlags.AutoRejectWillWithdraw) != 0)
                lines.Add(new PanelLine("ct.risk.autoReject", LineTone.Bad));
            return lines;
        }
    }
}
