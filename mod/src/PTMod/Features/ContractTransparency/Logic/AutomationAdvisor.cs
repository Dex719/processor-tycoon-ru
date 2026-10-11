using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the contract automation of the game does with one pair "CPU of the player x available contract" (FR-6 item 1).</summary>
    public enum AutomationDecision
    {
        /// <summary>The offer is sent.</summary>
        WillOffer = 0,

        /// <summary>The CPU is already among the offers of the contract; nothing happens.</summary>
        AlreadyOffered = 1,

        /// <summary>The CPU is retired: the automation skips it (for every contract).</summary>
        SkippedRetired = 2,

        /// <summary>The CPU is not for sale on contracts (<c>SellOnContracts</c> is off): skipped for every contract.</summary>
        SkippedNotForContracts = 3,

        /// <summary>The margin at the contract's price is below the player's threshold (<c>ContractProfitMargin</c>).</summary>
        SkippedMargin = 4,

        /// <summary>The automation tries to send, but <c>Contract.AddOffer</c> turns the offer down silently: the CPU fails IPS, power or package.</summary>
        RejectedByRequirements = 5,

        /// <summary>The "contract automation" switch is off: nothing is sent at all.</summary>
        AutomationOff = 6,
    }

    /// <summary>What <see cref="AutomationAdvisor.Decide"/> needs to know about one pair; the patch fills it from the player's CPU and the contract.</summary>
    public struct AutomationInput
    {
        /// <summary><c>Player.AutomateContracts</c>.</summary>
        public bool AutomateContracts;

        /// <summary><c>Cpu.IsRetired</c>.</summary>
        public bool CpuRetired;

        /// <summary><c>Cpu.SellOnContracts</c>.</summary>
        public bool CpuSellOnContracts;

        /// <summary><c>Contract.HasReceivedOffer(cpu.SaveID)</c>.</summary>
        public bool AlreadyOffered;

        /// <summary><c>Contract.FailsAtAnyRequirement(cpu)</c>.</summary>
        public bool FailsRequirements;

        /// <summary><c>Contract.PaymentPerUnit</c>.</summary>
        public float PaymentPerUnit;

        /// <summary><c>Cpu.RealUnitCostWithNewPrice(PaymentPerUnit)</c>.</summary>
        public float RealUnitCostAtPayment;

        /// <summary><c>Player.ContractProfitMargin</c>: the threshold in percent (the slider of the contract window, 1..100).</summary>
        public int MarginPercent;
    }

    /// <summary>
    /// The decision rule of the contract automation (design.md, Component 5; FR-6, FR-9), copied from <c>ContractAutomation.SendOffers</c> and the silent checks of
    /// <c>Contract.AddOffer</c>, in the order the game applies them: the switch, then per CPU "retired" and "for sale on contracts", then per contract the margin
    /// (<c>(payment - cost) / payment &lt; threshold / 100</c>, in float like the game, a margin equal to the threshold passes), and only then <c>AddOffer</c>, which
    /// drops a CPU that is already in or fails the requirements. The same rule serves the log (what the run did) and the preview (what it will do).
    /// </summary>
    public static class AutomationAdvisor
    {
        /// <summary>
        /// The decision for a pair. <paramref name="margin"/> is the margin the game computes (<c>(payment - cost) / payment</c>) for every decision made at or after the
        /// margin check, and NaN for the decisions made before it (the switch, a retired CPU, a CPU not for contracts), when no cost is needed.
        /// </summary>
        public static AutomationDecision Decide(AutomationInput input, out float margin)
        {
            margin = float.NaN;
            if (!input.AutomateContracts) return AutomationDecision.AutomationOff;
            if (input.CpuRetired) return AutomationDecision.SkippedRetired;
            if (!input.CpuSellOnContracts) return AutomationDecision.SkippedNotForContracts;

            margin = Margin(input.PaymentPerUnit, input.RealUnitCostAtPayment);
            if (margin < Threshold(input.MarginPercent)) return AutomationDecision.SkippedMargin;

            if (input.AlreadyOffered) return AutomationDecision.AlreadyOffered;
            if (input.FailsRequirements) return AutomationDecision.RejectedByRequirements;
            return AutomationDecision.WillOffer;
        }

        /// <summary><c>(payment - cost) / payment</c> in float, as <c>ContractAutomation.SendOffers</c> computes it.</summary>
        public static float Margin(float paymentPerUnit, float realUnitCost)
        {
            return (paymentPerUnit - realUnitCost) / paymentPerUnit;
        }

        /// <summary>The threshold as a fraction: <c>ContractProfitMargin / 100f</c>.</summary>
        public static float Threshold(int marginPercent)
        {
            return marginPercent / 100f;
        }

        /// <summary>
        /// The day of the next run (FR-9 item 4): the game's automation runs when the date becomes 31 December (<c>ContractAutomation</c> checks the day and the month on every
        /// <c>OnDayPassed</c>). On that day itself the run is over, so the next one is the next 31 December. <c>default(DateTime)</c> for no date.
        /// </summary>
        public static DateTime NextRun(DateTime today)
        {
            if (today == default(DateTime)) return default(DateTime);
            var thisYear = new DateTime(today.Year, 12, 31);
            if (today.Date < thisYear) return thisYear;
            return today.Year >= DateTime.MaxValue.Year ? thisYear : new DateTime(today.Year + 1, 12, 31);
        }
    }

    /// <summary>One line of a run (or of the preview) of the automation: the decision for a pair, or for a CPU the automation skips whatever the contract.</summary>
    public sealed class AutomationRow
    {
        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int CpuId;

        /// <summary><c>Cpu.Name</c>.</summary>
        public string CpuName = string.Empty;

        /// <summary>The contract key; empty for the rows about a CPU alone (retired, not for contracts) and for "automation off".</summary>
        public string ContractKey = string.Empty;

        /// <summary>The contract for people; empty with <see cref="ContractKey"/>.</summary>
        public string ContractLabel = string.Empty;

        /// <summary>What the rule says.</summary>
        public AutomationDecision Decision;

        /// <summary>The margin the game computes; NaN when it was not (see <see cref="AutomationAdvisor.Decide"/>).</summary>
        public float Margin = float.NaN;

        /// <summary>The threshold as a fraction (<c>ContractProfitMargin / 100</c>).</summary>
        public float Threshold;

        /// <summary><c>Contract.PaymentPerUnit</c>; 0 for a row without a contract.</summary>
        public float Payment;

        /// <summary>The unit cost at that price; 0 when it was not needed.</summary>
        public float UnitCost;

        /// <summary>For <see cref="AutomationDecision.RejectedByRequirements"/>: which requirements failed, <c>ips</c>, <c>power</c>, <c>package</c>, comma separated.</summary>
        public string Failed = string.Empty;

        /// <summary>The CPU was among the offers of the contract before the run (read when the row was made).</summary>
        public bool OfferedBefore;

        /// <summary>Filled after a run: the offer is in the contract now and was not before. The preview leaves it false.</summary>
        public bool Sent;

        /// <summary>The row is about a pair of a CPU and a contract (not a CPU alone, not "automation off").</summary>
        public bool IsPair
        {
            get { return !string.IsNullOrEmpty(ContractKey); }
        }
    }

    /// <summary>The log lines of one run of the automation and the counts they add up to (FR-6 items 1-5).</summary>
    public sealed class AutomationReport
    {
        /// <summary>The lines in the order to write them: a decision for each row, a <see cref="LogTypes.Mismatch"/> after each decision that differs from what happened, then the summary.</summary>
        public List<LogEntry> Entries = new List<LogEntry>();

        /// <summary>Offers that were really sent.</summary>
        public int Sent;

        /// <summary>Pairs and CPUs skipped: by margin, by requirements, retired CPUs and CPUs not for contracts.</summary>
        public int Skipped;

        public int SkippedMargin;
        public int SkippedRequirements;

        /// <summary>Retired CPUs and CPUs not for contracts (each counted once).</summary>
        public int SkippedOther;

        /// <summary>Pairs where the CPU was already among the offers.</summary>
        public int AlreadyOffered;

        /// <summary>Decisions that differ from what the game did: for the log (<c>ModLog</c>) of the mod, one line each.</summary>
        public List<string> MismatchLines = new List<string>();
    }

    /// <summary>
    /// Turns the rows of a finished run into log lines (FR-6): one line per decision, the reason and the numbers it rests on (the margin line carries the formula's
    /// figures: margin, threshold, price, cost), a line "automation is off" when the switch is off and nothing else, and the summary "sent N, skipped M". Where the rule
    /// and the game disagree, the line says what the game did and a <see cref="LogTypes.Mismatch"/> line follows (FR-6 item 5).
    /// </summary>
    public static class AutomationReportBuilder
    {
        /// <summary>
        /// The lines for <paramref name="rows"/> on the day <paramref name="date"/>. The rows carry the verdict of the rule and, in <see cref="AutomationRow.Sent"/>, what the
        /// game did. An empty list (the switch is on but nothing to decide) still gives the summary; one "automation off" row gives that single line and no summary.
        /// </summary>
        public static AutomationReport Build(IList<AutomationRow> rows, DateTime date)
        {
            var report = new AutomationReport();
            string day = LogDates.Write(date);

            if (rows != null && rows.Count == 1 && rows[0].Decision == AutomationDecision.AutomationOff)
            {
                report.Entries.Add(Line(day, rows[0], "ct.reason.automationOff"));
                return report;
            }

            int mismatches = 0;
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    AutomationRow row = rows[i];
                    bool differs = row.IsPair && (row.Decision == AutomationDecision.WillOffer) != row.Sent;

                    report.Entries.Add(Line(day, row, ReasonOf(row, differs)));
                    Count(report, row);

                    if (!differs) continue;
                    mismatches++;
                    string predicted = row.Decision.ToString();
                    string observed = row.Sent ? "sent" : "notSent";
                    LogEntry mismatch = Base(day, LogTypes.Mismatch, row);
                    mismatch.reasonKey = "ct.reason.mismatch";
                    mismatch.Put(LogDataKeys.Predicted, predicted);
                    mismatch.Put(LogDataKeys.Observed, observed);
                    report.Entries.Add(mismatch);
                    report.MismatchLines.Add("automation: " + row.ContractKey + " CPU " + row.CpuId.ToString(CultureInfo.InvariantCulture) +
                                             ": the model says " + predicted + ", the game " + (row.Sent ? "sent the offer" : "did not send the offer"));
                }
            }

            report.Skipped = report.SkippedMargin + report.SkippedRequirements + report.SkippedOther;

            LogEntry summary = LogEntry.Create(day, LogTypes.AutomationSummary, LogSources.Automation);
            summary.reasonKey = "ct.reason.automationSummary";
            summary.Put(LogDataKeys.Sent, report.Sent);
            summary.Put(LogDataKeys.Skipped, report.Skipped);
            summary.Put(LogDataKeys.SkippedMargin, report.SkippedMargin);
            summary.Put(LogDataKeys.SkippedRequirements, report.SkippedRequirements);
            summary.Put(LogDataKeys.SkippedOther, report.SkippedOther);
            summary.Put(LogDataKeys.AlreadyOffered, report.AlreadyOffered);
            if (mismatches > 0) summary.Put(LogDataKeys.Mismatches, mismatches);
            report.Entries.Add(summary);
            return report;
        }

        private static void Count(AutomationReport report, AutomationRow row)
        {
            if (row.Sent)
            {
                report.Sent++;
                return;
            }
            switch (row.Decision)
            {
                case AutomationDecision.SkippedMargin: report.SkippedMargin++; break;
                case AutomationDecision.RejectedByRequirements: report.SkippedRequirements++; break;
                case AutomationDecision.SkippedRetired:
                case AutomationDecision.SkippedNotForContracts: report.SkippedOther++; break;
                case AutomationDecision.AlreadyOffered: report.AlreadyOffered++; break;
            }
        }

        /// <summary>The reason of the line: what the game did when it differs from the rule, otherwise what the rule says.</summary>
        private static string ReasonOf(AutomationRow row, bool differs)
        {
            if (differs) return row.Sent ? "ct.reason.sent" : "ct.reason.notSent";
            switch (row.Decision)
            {
                case AutomationDecision.WillOffer: return "ct.reason.sent";
                case AutomationDecision.AlreadyOffered: return "ct.reason.alreadyOffered";
                case AutomationDecision.SkippedRetired: return "ct.reason.retired";
                case AutomationDecision.SkippedNotForContracts: return "ct.reason.notForContracts";
                case AutomationDecision.SkippedMargin: return "ct.reason.margin";
                case AutomationDecision.RejectedByRequirements: return "ct.reason.requirements";
                default: return "ct.reason.automationOff";
            }
        }

        private static LogEntry Base(string day, string type, AutomationRow row)
        {
            LogEntry entry = LogEntry.Create(day, type, LogSources.Automation);
            entry.contractKey = row.ContractKey;
            entry.contractLabel = row.ContractLabel;
            entry.cpuId = row.CpuId;
            entry.cpuName = row.CpuName;
            return entry;
        }

        private static LogEntry Line(string day, AutomationRow row, string reasonKey)
        {
            LogEntry entry = Base(day, LogTypes.AutomationDecision, row);
            entry.reasonKey = reasonKey;
            if (!float.IsNaN(row.Margin))
            {
                entry.Put(LogDataKeys.Margin, row.Margin);
                entry.Put(LogDataKeys.Threshold, row.Threshold);
                entry.Put(LogDataKeys.Payment, row.Payment);
                entry.Put(LogDataKeys.UnitCost, row.UnitCost);
            }
            if (row.Decision == AutomationDecision.RejectedByRequirements && !string.IsNullOrEmpty(row.Failed)) entry.Put(LogDataKeys.Failed, row.Failed);
            return entry;
        }
    }
}
