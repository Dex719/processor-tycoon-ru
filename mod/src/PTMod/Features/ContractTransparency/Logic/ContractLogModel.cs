using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The contract log as pure data (design.md, Component 10; FR-8, NFR-3, NFR-6, NFR-7): the entries in a ring buffer of <c>log.capacity</c>, the tender results in
    /// a ring of the same size, the two counters of FR-14 and the list of the results that came in today (for the one letter a day). No Unity, no game: the adapter
    /// <c>ContractLog</c> only reads and writes the save and the patches only call <see cref="Add"/>, <see cref="AddTender"/> and the counters.
    /// <list type="bullet">
    /// <item><b>Bounded.</b> More than <see cref="LogCapacity"/> entries push the oldest out (520 become the newest 500, AC-8.1); a result older than
    /// <c>log.tenderHistoryYears</c> is dropped when a save is loaded.</item>
    /// <item><b>Tolerant.</b> <see cref="FromSave"/> takes any DTO: a missing one gives an empty log, a damaged record is refused with a line for the log and the rest
    /// is kept (NFR-3).</item>
    /// <item><b>Not saved:</b> the results waiting for the letter (<see cref="PendingTenderCount"/>): a letter is due the day the draws happened, a load in between loses it (E9).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class ContractLogModel : IContractCounters
    {
        private const int MaxPendingTenders = 200;

        private readonly List<TenderResultDto> _pending = new List<TenderResultDto>();
        private LogRing<LogEntry> _log;
        private LogRing<TenderResultDto> _tenders;
        private ContractRules _rules;

        /// <summary>An empty log with the default rules (500 entries).</summary>
        public ContractLogModel() : this(ContractRules.Default)
        {
        }

        /// <summary>An empty log with the limits of <paramref name="rules"/> (null: the defaults).</summary>
        public ContractLogModel(ContractRules rules)
        {
            _rules = rules ?? ContractRules.Default;
            _log = new LogRing<LogEntry>(_rules.LogCapacity);
            _tenders = new LogRing<TenderResultDto>(_rules.LogCapacity);
        }

        /// <summary>The rules whose limits the log keeps.</summary>
        public ContractRules Rules
        {
            get { return _rules; }
        }

        /// <summary>The most entries (and the most tender results) the log keeps.</summary>
        public int LogCapacity
        {
            get { return _rules.LogCapacity; }
        }

        /// <summary>Entries in the log now.</summary>
        public int Count
        {
            get { return _log.Count; }
        }

        /// <summary>Tender results kept now.</summary>
        public int TenderCount
        {
            get { return _tenders.Count; }
        }

        /// <summary>Results added today that no letter has been made for yet.</summary>
        public int PendingTenderCount
        {
            get { return _pending.Count; }
        }

        /// <summary>Offers the player has sent in this game (FR-14).</summary>
        public int OffersSent { get; private set; }

        /// <summary>Tenders the player has won in this game (FR-14).</summary>
        public int TendersWon { get; private set; }

        // ---- writing ----

        /// <summary>Empties the log and the counters (a new game, a save without a section, a section from a newer version).</summary>
        public void Reset()
        {
            _log.Clear();
            _tenders.Clear();
            _pending.Clear();
            OffersSent = 0;
            TendersWon = 0;
        }

        /// <summary>Takes the limits of other rules; a smaller capacity pushes out the oldest records at once.</summary>
        public void SetRules(ContractRules rules)
        {
            _rules = rules ?? ContractRules.Default;
            _log.Resize(_rules.LogCapacity);
            _tenders.Resize(_rules.LogCapacity);
        }

        /// <summary>
        /// Adds an entry as the newest (the oldest goes when the ring is full). The entry is cleaned first (<see cref="SaveSanitizer"/>); false when it cannot be kept
        /// (an unknown type, a date that is not <c>yyyy-MM-dd</c>).
        /// </summary>
        public bool Add(LogEntry entry)
        {
            if (!SaveSanitizer.Entry(entry, null)) return false;
            _log.Add(entry);
            return true;
        }

        /// <summary>
        /// Adds the result of a tender: into the list of results, into the letter of the day and, as a line, into the log (type <see cref="LogTypes.TenderResult"/>), so
        /// the log shows the draw in its place among the other events. A win raises <see cref="TendersWon"/>. False when the result cannot be kept.
        /// </summary>
        public bool AddTender(TenderResultDto tender)
        {
            if (!SaveSanitizer.Tender(tender, null)) return false;

            _tenders.Add(tender);
            if (_pending.Count < MaxPendingTenders) _pending.Add(tender);
            if (tender.outcome == TenderOutcomeClassifier.OutcomeWon && TendersWon < int.MaxValue) TendersWon++;

            _log.Add(TenderLine(tender));
            return true;
        }

        /// <summary>Counts one more offer sent by the player (FR-14).</summary>
        public void CountOfferSent()
        {
            if (OffersSent < int.MaxValue) OffersSent++;
        }

        /// <summary>The results added since the last call, in the order they came; the list of waiting results is emptied. Empty (not null) when none.</summary>
        public IReadOnlyList<TenderResultDto> DrainTodayTenders()
        {
            if (_pending.Count == 0) return NoTenders;
            var copy = new List<TenderResultDto>(_pending);
            _pending.Clear();
            return copy;
        }

        private static readonly TenderResultDto[] NoTenders = new TenderResultDto[0];

        // ---- reading ----

        /// <summary>The entries the filter lets through, the newest first (FR-8 item 1). Never null.</summary>
        public IReadOnlyList<LogEntry> Query(LogFilter filter)
        {
            var result = new List<LogEntry>();
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                LogEntry entry = _log[i];
                if (LogFilters.Matches(filter, entry.type)) result.Add(entry);
            }
            return result;
        }

        /// <summary>The newest tender result of this contract on this day (the details behind a line of the log); null when there is none.</summary>
        public TenderResultDto FindTender(string contractKey, string date)
        {
            for (int i = _tenders.Count - 1; i >= 0; i--)
            {
                TenderResultDto tender = _tenders[i];
                if (tender.contractKey == contractKey && tender.date == date) return tender;
            }
            return null;
        }

        /// <summary>The tender results, the newest first. Never null.</summary>
        public IReadOnlyList<TenderResultDto> Tenders()
        {
            var result = new List<TenderResultDto>(_tenders.Count);
            for (int i = _tenders.Count - 1; i >= 0; i--) result.Add(_tenders[i]);
            return result;
        }

        // ---- the save ----

        /// <summary>The data for the save (the records themselves, not copies; the oldest first). The section has no version field: the core adds it.</summary>
        public ContractTransparencySave ToSave()
        {
            return new ContractTransparencySave
            {
                log = _log.ToList(),
                tenders = _tenders.ToList(),
                offersSent = OffersSent,
                tendersWon = TendersWon,
            };
        }

        /// <summary>
        /// Takes the contents from the save, with the limits of <paramref name="rules"/> and without a date to measure the age of results against: the newest date in the
        /// data stands in for today. See the overload with <c>today</c>.
        /// </summary>
        public int FromSave(ContractTransparencySave save, int schema, ContractRules rules)
        {
            return FromSave(save, schema, rules, default(DateTime), null);
        }

        /// <summary>
        /// Replaces the contents with those of <paramref name="save"/> (NFR-3). <c>null</c> gives an empty log. A record that is damaged is refused (a line in
        /// <paramref name="problems"/> says why), a tender result older than <c>log.tenderHistoryYears</c> before <paramref name="today"/> is dropped together with its line
        /// in the log, and more records than the capacity keep only the newest. <paramref name="today"/> equal to <c>default(DateTime)</c> means unknown: the newest date in the
        /// data is used. <paramref name="schema"/> is the version the core read from the section (migrations by it go here; the only one is 1). Returns how many records
        /// were not kept. Never throws for any content of <paramref name="save"/>.
        /// </summary>
        public int FromSave(ContractTransparencySave save, int schema, ContractRules rules, DateTime today, List<string> problems)
        {
            SetRules(rules);
            Reset();
            if (save == null) return 0;

            OffersSent = save.offersSent < 0 ? 0 : save.offersSent;
            TendersWon = save.tendersWon < 0 ? 0 : save.tendersWon;

            var entries = new List<LogEntry>();
            var tenders = new List<TenderResultDto>();
            int invalid = 0;
            var lines = new List<string>();

            if (save.log != null)
            {
                foreach (LogEntry entry in save.log)
                {
                    lines.Clear();
                    if (SaveSanitizer.Entry(entry, lines)) entries.Add(entry);
                    else Refused(ref invalid, lines, problems);
                }
            }
            if (save.tenders != null)
            {
                foreach (TenderResultDto tender in save.tenders)
                {
                    lines.Clear();
                    if (SaveSanitizer.Tender(tender, lines)) tenders.Add(tender);
                    else Refused(ref invalid, lines, problems);
                }
            }

            // The age of a tender result is measured from today; when the caller does not know it, from the newest date in the data.
            DateTime reference = today;
            if (reference == default(DateTime)) reference = Newest(entries, tenders);
            int old = 0;
            if (reference != default(DateTime))
            {
                DateTime cutoff;
                try
                {
                    cutoff = reference.AddYears(-_rules.TenderHistoryYears);
                }
                catch (ArgumentOutOfRangeException)
                {
                    cutoff = DateTime.MinValue;   // a date in the first years of the calendar: nothing is old enough to drop
                }
                old += Keep(entries, e => e.type != LogTypes.TenderResult || !Older(e.date, cutoff));
                old += Keep(tenders, t => !Older(t.date, cutoff));
            }

            int before = entries.Count + tenders.Count;
            foreach (LogEntry entry in entries) _log.Add(entry);
            foreach (TenderResultDto tender in tenders) _tenders.Add(tender);
            int overflow = before - (_log.Count + _tenders.Count);

            int notKept = invalid + old + overflow;
            if (problems != null)
            {
                if (old > 0) problems.Add(old.ToString(CultureInfo.InvariantCulture) + " tender record(s) older than " + _rules.TenderHistoryYears.ToString(CultureInfo.InvariantCulture) + " years dropped");
                if (overflow > 0) problems.Add(overflow.ToString(CultureInfo.InvariantCulture) + " record(s) over the limit of " + _rules.LogCapacity.ToString(CultureInfo.InvariantCulture) + " dropped (the oldest)");
            }
            return notKept;
        }

        // ---- private ----

        private static LogEntry TenderLine(TenderResultDto tender)
        {
            LogEntry line = LogEntry.Create(tender.date, LogTypes.TenderResult, LogSources.Game);
            line.contractKey = tender.contractKey;
            line.contractLabel = tender.contractLabel;
            line.cpuId = tender.playerCpuId;
            line.cpuName = tender.playerCpuName;
            line.reasonKey = tender.reasonKey;
            line.Put(LogDataKeys.Outcome, tender.outcome);
            line.Put(LogDataKeys.Chance, tender.playerGameChance);
            line.Put(LogDataKeys.Actual, tender.playerActual);
            if (!string.IsNullOrEmpty(tender.winnerCompany)) line.Put(LogDataKeys.Winner, tender.winnerCompany);
            if (!string.IsNullOrEmpty(tender.winnerCpuName)) line.Put(LogDataKeys.WinnerCpu, tender.winnerCpuName);
            return line;
        }

        private static void Refused(ref int count, List<string> lines, List<string> problems)
        {
            count++;
            if (problems == null) return;
            foreach (string line in lines) problems.Add(line);
        }

        private static bool Older(string date, DateTime cutoff)
        {
            DateTime parsed;
            return LogDates.TryParse(date, out parsed) && parsed < cutoff;
        }

        private static int Keep<T>(List<T> items, Func<T, bool> keep)
        {
            int removed = items.RemoveAll(item => !keep(item));
            return removed;
        }

        private static DateTime Newest(List<LogEntry> entries, List<TenderResultDto> tenders)
        {
            DateTime newest = default(DateTime);
            DateTime parsed;
            foreach (LogEntry entry in entries)
            {
                if (LogDates.TryParse(entry.date, out parsed) && parsed > newest) newest = parsed;
            }
            foreach (TenderResultDto tender in tenders)
            {
                if (LogDates.TryParse(tender.date, out parsed) && parsed > newest) newest = parsed;
            }
            return newest;
        }
    }
}
