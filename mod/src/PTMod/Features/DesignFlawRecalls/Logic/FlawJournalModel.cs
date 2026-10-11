using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The two tabs of the page "Flaws and recalls" (FR-12 item 1).</summary>
    public enum FlawJournalTab
    {
        /// <summary>"My CPUs": the flaws of the player's CPUs that the player knows about.</summary>
        Mine = 0,

        /// <summary>"Rivals": the public incidents of the AI companies.</summary>
        Rivals = 1,
    }

    /// <summary>
    /// One line of the page "Flaws and recalls", as plain data (the columns of FR-12 item 1 without the words: the page writes them in the language of the player).
    /// A row is a snapshot: it does not change when the record does.
    /// </summary>
    public sealed class FlawJournalRow
    {
        public int RecordId;

        /// <summary>The owner (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>).</summary>
        public string CompanyKey;

        /// <summary><c>SaveID</c> of the base CPU (the page asks the game for its name).</summary>
        public int BaseCpuId;

        public string TypeId;
        public FlawSeverity Severity;
        public FlawState State;
        public FlawResponse Response;

        /// <summary>The flaw was ignored or revised in silence and leaked.</summary>
        public bool CoverUp;

        /// <summary>The record is over (<see cref="FlawState.Resolved"/>, <see cref="FlawState.Expired"/> or <see cref="FlawState.Closed"/>).</summary>
        public bool IsFinal;

        /// <summary>The owner has to answer: the flaw is known and nothing is chosen yet.</summary>
        public bool Waiting;

        /// <summary>The revision is quiet (nobody outside the company knows).</summary>
        public bool QuietStepping;

        /// <summary>Days to the end of the running revision or patch; 0 when none runs.</summary>
        public int DaysLeft;

        /// <summary>The share of the performance a running patch costs, 0..1.</summary>
        public float IpsLoss;

        /// <summary>Money spent on the flaw so far.</summary>
        public float SpentTotal;

        /// <summary>The line has the button "Fund a new stepping" (FR-8 item 5, FR-12 item 2): a patched flaw of the player that has a quote.</summary>
        public bool CanFundStepping;

        /// <summary>The price of the revision the button offers (the saved quote).</summary>
        public float FundCost;

        /// <summary>The length of the revision the button offers, days.</summary>
        public int FundDays;
    }

    /// <summary>
    /// The rows of the page "Flaws and recalls" (FR-12, design.md Component 14): which records each tab shows, in which order, and what the line of a record says.
    /// Pure: the page (<c>FlawJournalPage</c>) turns the rows into widgets and the words. The rules:
    /// <list type="bullet">
    /// <item>A <see cref="FlawState.Hidden"/> record is never shown and never hinted at (FR-12 item 4).</item>
    /// <item><b>My CPUs</b> shows every record of the player that is not hidden, final ones included (the history).</item>
    /// <item><b>Rivals</b> shows an incident of an AI company only if it is public knowledge: the flaw was public (<c>publicSinceDay</c>), or the company recalled it. What a
    /// rival knows and keeps quiet (a flaw only the company knows, a quiet revision, a quiet patch) is not shown until it leaks (FR-11 item 5: no rumours).</item>
    /// <item>The records that are not over come first, then the finished ones; newest first. At most <see cref="MaxFinalRows"/> finished ones are shown.</item>
    /// </list>
    /// </summary>
    public static class FlawJournalModel
    {
        /// <summary>The most finished records one tab shows (the page is a list of what matters now, not an archive).</summary>
        public const int MaxFinalRows = 20;

        /// <summary>The record belongs on the tab.</summary>
        public static bool IsShown(FlawRecord record, FlawJournalTab tab)
        {
            if (record == null || record.State == FlawState.Hidden) return false;

            bool mine = CompanyKey.IsPlayer(record.CompanyKey);
            if (tab == FlawJournalTab.Mine) return mine;
            return !mine && IsPublicKnowledge(record);
        }

        /// <summary>
        /// The public knows about the flaw: it was public at some time, or its owner recalled it (a recall is never done in secret,
        /// <see cref="FlawNewsPlanner.IsPublicIncident"/>).
        /// </summary>
        public static bool IsPublicKnowledge(FlawRecord record)
        {
            if (record == null) return false;
            return record.Data.publicSinceDay > 0 || (record.State == FlawState.Resolved && record.Response == FlawResponse.Recall);
        }

        /// <summary>The rows of one tab as of day <paramref name="today"/>, in the order described in the remarks of the class.</summary>
        public static List<FlawJournalRow> Rows(IEnumerable<FlawRecord> records, FlawJournalTab tab, int today)
        {
            var active = new List<FlawRecord>();
            var finished = new List<FlawRecord>();
            if (records != null)
            {
                foreach (FlawRecord record in records)
                {
                    if (!IsShown(record, tab)) continue;
                    (record.IsFinal ? finished : active).Add(record);
                }
            }

            Comparison<FlawRecord> newestFirst = (a, b) => b.Id.CompareTo(a.Id);
            active.Sort(newestFirst);
            finished.Sort(newestFirst);
            if (finished.Count > MaxFinalRows) finished.RemoveRange(MaxFinalRows, finished.Count - MaxFinalRows);

            var rows = new List<FlawJournalRow>(active.Count + finished.Count);
            foreach (FlawRecord record in active) rows.Add(RowOf(record, today));
            foreach (FlawRecord record in finished) rows.Add(RowOf(record, today));
            return rows;
        }

        /// <summary>The row of one record as of day <paramref name="today"/>.</summary>
        public static FlawJournalRow RowOf(FlawRecord record, int today)
        {
            if (record == null) throw new ArgumentNullException("record");
            FlawRecordDto d = record.Data;
            FlawQuote quote = record.Quote;

            int daysLeft = 0;
            if (record.State == FlawState.Stepping) daysLeft = d.steppingEndDay - today;
            else if (record.State == FlawState.Mitigated) daysLeft = d.mitigationEndDay - today;

            bool fund = record.State == FlawState.Mitigated && CompanyKey.IsPlayer(record.CompanyKey) && quote != null;
            return new FlawJournalRow
            {
                RecordId = record.Id,
                CompanyKey = record.CompanyKey,
                BaseCpuId = record.BaseCpuId,
                TypeId = record.TypeId,
                Severity = record.Severity,
                State = record.State,
                Response = record.Response,
                CoverUp = d.coverUp,
                IsFinal = record.IsFinal,
                Waiting = FlawResponseEngine.AwaitsDecision(record),
                QuietStepping = d.quietStepping,
                DaysLeft = Math.Max(0, daysLeft),
                IpsLoss = d.ipsLoss,
                SpentTotal = d.spentTotal,
                CanFundStepping = fund,
                FundCost = fund ? quote.SteppingCost : 0f,
                FundDays = fund ? quote.SteppingDays : 0,
            };
        }

        /// <summary>
        /// The key of the words for the status of a row, without the prefix <c>dfr.journal.status.</c> (the page adds it): what the owner is doing about the flaw.
        /// Every value this method returns has a text in both languages (a test checks).
        /// </summary>
        public static string StatusKey(FlawJournalRow row)
        {
            if (row == null) throw new ArgumentNullException("row");

            switch (row.State)
            {
                case FlawState.InternalKnown:
                    return row.Response == FlawResponse.Ignore ? "ignoredInternal" : "waitingInternal";
                case FlawState.Public:
                    return row.Response == FlawResponse.Ignore ? "ignoredPublic" : "waitingPublic";
                case FlawState.Stepping:
                    return row.QuietStepping ? "steppingQuiet" : "stepping";
                case FlawState.Mitigated:
                    return "mitigated";
                case FlawState.Resolved:
                    switch (row.Response)
                    {
                        case FlawResponse.Recall: return "resolvedRecall";
                        case FlawResponse.Stepping: return "resolvedStepping";
                        case FlawResponse.Patch: return "resolvedPatch";
                        default: return "resolved";
                    }
                case FlawState.Expired:
                    return "expired";
                case FlawState.Closed:
                    return "closed";
                default:
                    return "resolved";
            }
        }

        /// <summary>Every key <see cref="StatusKey"/> can return, for the check that the string tables have them all.</summary>
        public static readonly string[] AllStatusKeys =
        {
            "waitingInternal", "waitingPublic", "ignoredInternal", "ignoredPublic", "stepping", "steppingQuiet", "mitigated",
            "resolvedRecall", "resolvedStepping", "resolvedPatch", "resolved", "expired", "closed",
        };
    }
}
