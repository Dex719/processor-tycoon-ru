using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The saved form of <see cref="RecoveryState"/> and the way back (NFR-3, NFR-7; design.md, "Validation Rules"): <see cref="Write"/> is
    /// <c>ModJson.From</c> without the version field, <see cref="Read"/> is <c>ModJson.Bind</c> followed by <see cref="Sanitize"/>, which throws
    /// away every record that cannot be right (a loan with a negative balance, a rate of 500 percent, a date that is not <c>yyyy-MM-dd</c>) and says
    /// why in a line of the problem list; the rest of the section is kept. No game types and no log: the problems are returned as text, the
    /// feature writes them to <c>ModLog</c>. Never throws for bad data.
    /// </summary>
    public static class RecoveryStateCodec
    {
        /// <summary>The version of the section this build writes (<c>_schema</c>, kept by the core). Only version 1 exists.</summary>
        public const int SchemaVersion = 1;

        /// <summary>The size of the ring buffer of the journal; the same number as <c>CapitalLedger.JournalCapacity</c> (FR-10).</summary>
        public const int MaxJournalEntries = 200;

        /// <summary>The longest term of a loan, in months (design.md: <c>monthsTotal ≤ 120</c>).</summary>
        public const int MaxLoanMonths = 120;

        /// <summary>The highest annual rate a stored loan may have (NFR-7: rates 0..2).</summary>
        public const double MaxAnnualRate = 2.0;

        /// <summary>The balance of a loan may exceed its principal by this much (rounding of the payments); design.md: <c>outstanding ≤ principal × 1,01</c>.</summary>
        public const double OutstandingSlack = 1.01;

        private static readonly string[] LoanKinds = { LoanData.KindBridge, LoanData.KindTerm, LoanData.KindRescueLoan };

        // ---- write ----

        /// <summary>The section to store: a fresh object, never null, without <c>_schema</c> (the core adds it). A null state is written as a new game's.</summary>
        public static JsonObject Write(RecoveryState state)
        {
            JsonNode node = ModJson.From(state ?? new RecoveryState());
            var section = node as JsonObject ?? new JsonObject();
            section.Remove(SideFileModel.SchemaKey);
            return section;
        }

        // ---- read ----

        /// <summary>
        /// The state from a stored section (without <c>_schema</c>). Members that are missing keep their defaults, members of the wrong type are
        /// reported and keep them too, invalid records are dropped (<see cref="Sanitize"/>). A null section gives a new game's state without a problem line.
        /// </summary>
        public static RecoveryState Read(JsonObject section, List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            if (section == null) return new RecoveryState();

            var errors = new List<string>();
            RecoveryState state = ModJson.Bind<RecoveryState>(section, errors) ?? new RecoveryState();
            foreach (string error in errors) problems.Add(StripRoot(error));

            Sanitize(state, problems);
            return state;
        }

        // ---- validation ----

        /// <summary>
        /// Brings a state that was just read into the rules of the section (NFR-7): missing members are made, an invalid record is dropped with a line
        /// in <paramref name="problems"/> (<c>loans[1] (bridge-2): outstanding 9e9 is above the principal, the loan is dropped</c>), a number that only
        /// has to be non-negative is raised to 0. Also keeps <c>nextLoanSeq</c> above the numbers of the loan ids and the journal inside its capacity.
        /// </summary>
        public static void Sanitize(RecoveryState state, List<string> problems)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (problems == null) problems = new List<string>();

            state.creditLine = CheckLine(state.creditLine, problems);
            CheckLoans(state, problems);
            state.investorShare = CheckInvestor(state.investorShare, problems);

            if (state.episode == null) state.episode = new EpisodeData();
            if (state.episode.recoveryStreak < 0) state.episode.recoveryStreak = 0;

            if (state.bailoutsUsed < 0)
            {
                problems.Add("bailoutsUsed " + state.bailoutsUsed.ToString(CultureInfo.InvariantCulture) + " is negative, 0 used");
                state.bailoutsUsed = 0;
            }

            if (state.lastBridgeClosedOn == null) state.lastBridgeClosedOn = "";
            DateTime date;
            if (state.lastBridgeClosedOn.Length > 0 && !RecoveryDates.TryParse(state.lastBridgeClosedOn, out date))
            {
                problems.Add("lastBridgeClosedOn '" + state.lastBridgeClosedOn + "' is not a date yyyy-MM-dd, it is dropped");
                state.lastBridgeClosedOn = "";
            }

            CheckJournal(state, problems);
            FixLoanSeq(state);
        }

        // ---- the credit line ----

        private static CreditLineData CheckLine(CreditLineData line, List<string> problems)
        {
            if (line == null) return new CreditLineData();

            string state = line.state ?? "";
            if (state != CreditLineData.StateNone && state != CreditLineData.StateOpen && state != CreditLineData.StateClosed)
            {
                problems.Add("creditLine.state '" + state + "' is not none, open or closed, the line is dropped");
                return new CreditLineData();
            }
            if (!IsMoney(line.drawn))
            {
                problems.Add("creditLine.drawn " + Show(line.drawn) + " is not a finite number of 0 or more, the line is dropped");
                return new CreditLineData();
            }

            line.state = state;
            line.opensOn = line.opensOn ?? "";
            line.closesOn = line.closesOn ?? "";
            line.closedReason = line.closedReason ?? "";
            DateTime date;

            if (state == CreditLineData.StateOpen)
            {
                if (!RecoveryDates.TryParse(line.opensOn, out date) || !RecoveryDates.TryParse(line.closesOn, out date))
                {
                    problems.Add("creditLine: an open line needs opensOn and closesOn as yyyy-MM-dd (got '" + line.opensOn + "' and '" + line.closesOn
                                 + "'), the line is dropped");
                    return new CreditLineData();
                }
            }
            else if (state == CreditLineData.StateClosed)
            {
                if (line.opensOn.Length > 0 && !RecoveryDates.TryParse(line.opensOn, out date))
                {
                    problems.Add("creditLine.opensOn '" + line.opensOn + "' is not a date yyyy-MM-dd, it is dropped");
                    line.opensOn = "";
                }
                if (line.closesOn.Length > 0 && !RecoveryDates.TryParse(line.closesOn, out date))
                {
                    problems.Add("creditLine.closesOn '" + line.closesOn + "' is not a date yyyy-MM-dd, it is dropped");
                    line.closesOn = "";
                }
                if (line.closedReason.Length > 0 && line.closedReason != CreditLineData.ReasonExpired && line.closedReason != CreditLineData.ReasonDisabled)
                {
                    problems.Add("creditLine.closedReason '" + line.closedReason + "' is not expired or disabled, it is dropped");
                    line.closedReason = "";
                }
            }
            else
            {
                // "none" means the line never existed: nothing else can be true of it.
                if (line.drawn != 0) problems.Add("creditLine: a line that was never opened has drawn " + Show(line.drawn) + ", 0 used");
                line.opensOn = "";
                line.closesOn = "";
                line.closedReason = "";
                line.drawn = 0;
            }
            return line;
        }

        // ---- the loans ----

        private static void CheckLoans(RecoveryState state, List<string> problems)
        {
            var kept = new List<LoanData>();
            if (state.loans != null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < state.loans.Count; i++)
                {
                    LoanData loan = state.loans[i];
                    string why = LoanProblem(loan, ids);
                    if (why != null)
                    {
                        string name = loan != null && !string.IsNullOrEmpty(loan.id) ? " (" + loan.id + ")" : "";
                        problems.Add("loans[" + i.ToString(CultureInfo.InvariantCulture) + "]" + name + ": " + why + ", the loan is dropped");
                        continue;
                    }
                    ids.Add(loan.id);
                    kept.Add(loan);
                }
            }
            state.loans = kept;
        }

        // Why a stored loan cannot be right (design.md, "Validation Rules"), or null when it can.
        private static string LoanProblem(LoanData loan, HashSet<string> ids)
        {
            if (loan == null) return "it is empty";
            if (string.IsNullOrEmpty(loan.id)) return "it has no id";
            if (ids.Contains(loan.id)) return "the id is used twice";
            if (Array.IndexOf(LoanKinds, loan.kind) < 0) return "kind '" + loan.kind + "' is not Bridge, Term or RescueLoan";
            if (!IsMoney(loan.principal)) return "principal " + Show(loan.principal) + " is not a finite number of 0 or more";
            if (!IsMoney(loan.outstanding)) return "outstanding " + Show(loan.outstanding) + " is not a finite number of 0 or more";
            if (loan.outstanding > loan.principal * OutstandingSlack) return "outstanding " + Show(loan.outstanding) + " is above the principal " + Show(loan.principal);
            if (!IsMoney(loan.payment)) return "payment " + Show(loan.payment) + " is not a finite number of 0 or more";
            if (double.IsNaN(loan.annualRate) || double.IsInfinity(loan.annualRate) || loan.annualRate < 0 || loan.annualRate > MaxAnnualRate)
                return "annualRate " + Show(loan.annualRate) + " is not between 0 and 2";
            if (loan.monthsTotal < 0 || loan.monthsTotal > MaxLoanMonths) return "monthsTotal " + loan.monthsTotal.ToString(CultureInfo.InvariantCulture) + " is not between 0 and 120";
            if (loan.monthsLeft < 0 || loan.monthsLeft > loan.monthsTotal)
                return "monthsLeft " + loan.monthsLeft.ToString(CultureInfo.InvariantCulture) + " is not between 0 and monthsTotal";
            DateTime date;
            if (!RecoveryDates.TryParse(loan.openedOn, out date)) return "openedOn '" + loan.openedOn + "' is not a date yyyy-MM-dd";
            return null;
        }

        // The next id must not meet a loan that is already there: bridge-7 in the list means the sequence is at least 8.
        private static void FixLoanSeq(RecoveryState state)
        {
            int next = state.nextLoanSeq < 1 ? 1 : state.nextLoanSeq;
            foreach (LoanData loan in state.loans)
            {
                int dash = loan.id.LastIndexOf('-');
                int number;
                if (dash >= 0 && int.TryParse(loan.id.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number < int.MaxValue)
                {
                    if (number + 1 > next) next = number + 1;
                }
            }
            state.nextLoanSeq = next;
        }

        // ---- the investor share ----

        private static InvestorShareData CheckInvestor(InvestorShareData share, List<string> problems)
        {
            if (share == null) return new InvestorShareData();

            string why = null;
            if (!IsMoney(share.amount)) why = "amount " + Show(share.amount) + " is not a finite number of 0 or more";
            else if (!IsMoney(share.paid)) why = "paid " + Show(share.paid) + " is not a finite number of 0 or more";
            else if (!IsMoney(share.cap)) why = "cap " + Show(share.cap) + " is not a finite number of 0 or more";
            else if (double.IsNaN(share.share) || double.IsInfinity(share.share) || share.share < 0 || share.share > 1) why = "share " + Show(share.share) + " is not between 0 and 1";
            else if (share.monthsLeft < 0 || share.monthsLeft > MaxLoanMonths) why = "monthsLeft " + share.monthsLeft.ToString(CultureInfo.InvariantCulture) + " is not between 0 and 120";
            if (why == null) return share;

            problems.Add("investorShare: " + why + ", the contract is dropped");
            return new InvestorShareData();
        }

        // ---- the journal ----

        private static void CheckJournal(RecoveryState state, List<string> problems)
        {
            var kept = new List<JournalEntryData>();
            if (state.journal != null)
            {
                for (int i = 0; i < state.journal.Count; i++)
                {
                    JournalEntryData entry = state.journal[i];
                    string why = JournalProblem(entry);
                    if (why != null)
                    {
                        problems.Add("journal[" + i.ToString(CultureInfo.InvariantCulture) + "]: " + why + ", the record is dropped");
                        continue;
                    }
                    if (entry.details == null) entry.details = "";
                    kept.Add(entry);
                }
            }
            if (kept.Count > MaxJournalEntries) kept.RemoveRange(0, kept.Count - MaxJournalEntries);
            state.journal = kept;
        }

        private static string JournalProblem(JournalEntryData entry)
        {
            if (entry == null) return "it is empty";
            DateTime date;
            if (!RecoveryDates.TryParse(entry.date, out date)) return "date '" + entry.date + "' is not a date yyyy-MM-dd";
            if (string.IsNullOrEmpty(entry.op)) return "it has no operation code";
            if (double.IsNaN(entry.amount) || double.IsInfinity(entry.amount)) return "amount " + Show(entry.amount) + " is not a finite number";
            return null;
        }

        // ---- helpers ----

        private static bool IsMoney(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        private static string Show(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // "$.loans[1].outstanding: expected number, got string" -> "loans[1].outstanding: expected number, got string"
        private static string StripRoot(string error)
        {
            if (error.StartsWith("$.", StringComparison.Ordinal)) return error.Substring(2);
            if (error.StartsWith("$", StringComparison.Ordinal)) return "section" + error.Substring(1);
            return error;
        }
    }
}
