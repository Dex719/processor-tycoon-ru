using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Reads and writes the section "historical-events" of the side save file (FR-18, NFR-3, NFR-8): the DTOs of <see cref="HistSaveData"/> to and from
    /// the JSON object the core hands over. The version of the section (<c>_schema</c>) is the core's business: <see cref="SchemaVersion"/> is what the
    /// participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member. Never throws.
    /// <para>
    /// <see cref="Read"/> normalizes what a hand-edited or damaged file can hold (Security, "Валидация сейва"): sums are finite and not negative, days are
    /// not negative, statuses are inside <see cref="EntryStatus"/>, companies are written as <see cref="CompanyKey"/>s. A value that can be repaired is
    /// repaired (a negative day becomes 0, an unknown status becomes <see cref="EntryStatus.Pending"/>); an object that is useless without the value (a
    /// case without a valid target, a restore without a price) is dropped. Every change is one line in the problems. That a key names a company that is
    /// still in the game, and that a CPU still exists, is checked when the object is used, not here.
    /// </para>
    /// </summary>
    public static class HistSaveCodec
    {
        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The name of the version member, which the core adds and strips; the codec removes it if it ever gets in.</summary>
        private const string SchemaMember = "_schema";

        /// <summary>The market segments are numbered 0..3 (<c>Segment.HighEnd</c> .. <c>Industries</c>).</summary>
        private const int SegmentCount = 4;

        /// <summary>
        /// The section as a DTO. A null section (nothing saved) gives an empty one. A member of the wrong type keeps its default and adds a line to
        /// <paramref name="problems"/> (may be null). The result is normalized, never null, and none of its lists is null.
        /// </summary>
        public static HistSaveData Read(JsonObject section, List<string> problems)
        {
            if (section == null) return new HistSaveData();

            var errors = new List<string>();
            HistSaveData data = ModJson.Bind<HistSaveData>(section, errors) ?? new HistSaveData();
            if (problems != null) problems.AddRange(errors);

            new Normalizer(problems).Run(data);
            return data;
        }

        /// <summary>The section as JSON, without <c>_schema</c> (the core adds it). A null DTO is written as an empty section.</summary>
        public static JsonObject Write(HistSaveData data)
        {
            JsonNode node = ModJson.From(data ?? new HistSaveData());
            var section = node as JsonObject ?? new JsonObject();
            section.Remove(SchemaMember);
            return section;
        }

        /// <summary>
        /// The state of a game that has no section (FR-18 item 3): every entry whose anchor is before <paramref name="todayDay"/> is
        /// <see cref="EntryStatus.SkippedBeforeStart"/> (it will not fire and is not caught up), every other is <see cref="EntryStatus.Pending"/>, and
        /// <c>initialized</c> is set. The jitter stays 0: this game never rolled one. A repeated id is taken once.
        /// </summary>
        /// <param name="todayDay">The current day as <see cref="SimpleDate.Days"/>.</param>
        public static HistSaveData CreateDefaults(IEnumerable<CalendarEntry> entries, int todayDay)
        {
            var data = new HistSaveData { initialized = true };
            if (entries == null) return data;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CalendarEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id) || !seen.Add(entry.Id)) continue;

                bool past = entry.Anchor.IsSet && entry.Anchor.Days < todayDay;
                data.entries.Add(new EntryStateDto { id = entry.Id, status = (int)(past ? EntryStatus.SkippedBeforeStart : EntryStatus.Pending) });
            }
            return data;
        }

        /// <summary>The state of an entry by its id, or null.</summary>
        public static EntryStateDto FindEntry(HistSaveData data, string id)
        {
            if (data == null || id == null) return null;
            foreach (EntryStateDto entry in data.entries)
            {
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        /// <summary>The numbers of <see cref="EntryStatus"/> that exist.</summary>
        public static bool IsKnownStatus(int status)
        {
            return status >= (int)EntryStatus.Pending && status <= (int)EntryStatus.Error;
        }

        /// <summary>One pass over a section that was just bound.</summary>
        private sealed class Normalizer
        {
            private readonly List<string> _problems;

            public Normalizer(List<string> problems)
            {
                _problems = problems;
            }

            public void Run(HistSaveData data)
            {
                data.lastMajorDay = Day(data.lastMajorDay, "lastMajorDay");
                data.catchUpEntryIds = Strings(data.catchUpEntryIds, "catchUpEntryIds");

                data.entries = Keep(data.entries, "entries", Entry);
                data.cases = Keep(data.cases, "cases", Case);
                data.settlements = Keep(data.settlements, "settlements", Settlement);
                data.exportRules = Keep(data.exportRules, "exportRules", ExportRule);
                data.priceWars = Keep(data.priceWars, "priceWars", PriceWar);
                data.disputes = Keep(data.disputes, "disputes", Dispute);
                data.races = Keep(data.races, "races", Race);

                DropRepeats(data.entries, "entries", e => e.id);
                DropRepeats(data.cases, "cases", e => e.caseId);
                DropRepeats(data.settlements, "settlements", e => e.id);
                DropRepeats(data.exportRules, "exportRules", e => e.ruleId);
                DropRepeats(data.priceWars, "priceWars", e => e.warId);
                DropRepeats(data.disputes, "disputes", e => e.disputeId);
                DropRepeats(data.races, "races", e => e.raceId);
            }

            // ---- the objects: true keeps the object (repaired), false drops it ----

            private bool Entry(EntryStateDto e, string path)
            {
                if (string.IsNullOrEmpty(e.id)) return Drop(path, "the entry has no id");

                if (!IsKnownStatus(e.status))
                {
                    Note(path + ".status", "status " + e.status + " is not an EntryStatus; taken as Pending");
                    e.status = (int)EntryStatus.Pending;
                }

                int jitter = Math.Max(-CalendarValidator.MaxJitterDays, Math.Min(CalendarValidator.MaxJitterDays, e.jitter));
                if (jitter != e.jitter) Note(path + ".jitter", e.jitter + " is outside " + -CalendarValidator.MaxJitterDays + ".." + CalendarValidator.MaxJitterDays + "; clamped");
                e.jitter = jitter;

                e.firedDay = Day(e.firedDay, path + ".firedDay");
                e.stagesFired = Count(e.stagesFired, path + ".stagesFired");
                e.flags = Strings(e.flags, path + ".flags");
                e.targets = Keep(e.targets, path + ".targets", Targets);
                return true;
            }

            private bool Targets(RoleTargetsDto t, string path)
            {
                if (string.IsNullOrEmpty(t.role)) return Drop(path, "the target list has no role name");
                t.companyKeys = CompanyKeys(t.companyKeys, path + ".companyKeys");
                return true;
            }

            private bool Case(CaseDto c, string path)
            {
                if (string.IsNullOrEmpty(c.caseId)) return Drop(path, "the case has no id");
                if (!CompanyKey.IsValid(c.targetKey)) return Drop(path, "the case targets \"" + c.targetKey + "\", which is not a company key");

                c.mode = c.mode ?? string.Empty;
                c.stage = Count(c.stage, path + ".stage");
                c.fine = Amount(c.fine, path + ".fine");
                c.paid = Amount(c.paid, path + ".paid");
                c.provisioned = Amount(c.provisioned, path + ".provisioned");
                c.guaranteeFeePerMonth = Amount(c.guaranteeFeePerMonth, path + ".guaranteeFeePerMonth");
                c.rulingDay = Day(c.rulingDay, path + ".rulingDay");
                c.reimposeDay = Day(c.reimposeDay, path + ".reimposeDay");
                c.openedDay = Day(c.openedDay, path + ".openedDay");
                c.@params = c.@params ?? new JsonObject();
                return true;
            }

            private bool Settlement(SettlementDto s, string path)
            {
                if (string.IsNullOrEmpty(s.id)) return Drop(path, "the settlement has no id");
                if (!CompanyKey.IsValid(s.plaintiffKey) || !CompanyKey.IsValid(s.defendantKey)) return Drop(path, "the plaintiff or the defendant is not a company key");

                s.resolveDay = Day(s.resolveDay, path + ".resolveDay");
                s.legalPerMonth = Amount(s.legalPerMonth, path + ".legalPerMonth");
                s.pWin = Share(s.pWin, path + ".pWin");
                s.settlementPct = Share(s.settlementPct, path + ".settlementPct");
                return true;
            }

            private bool ExportRule(ExportRuleDto r, string path)
            {
                if (string.IsNullOrEmpty(r.ruleId)) return Drop(path, "the export rule has no id");

                r.threshold0 = Amount(r.threshold0, path + ".threshold0");
                r.avgIps0 = Amount(r.avgIps0, path + ".avgIps0");
                r.threshold = Amount(r.threshold, path + ".threshold");
                r.percentile = Share(r.percentile, path + ".percentile");
                r.blockedShare = Share(r.blockedShare, path + ".blockedShare");
                r.indexing = Amount(r.indexing, path + ".indexing");
                r.endDay = Day(r.endDay, path + ".endDay");
                r.segments = Segments(r.segments, path + ".segments");
                r.exemptions = Keep(r.exemptions, path + ".exemptions", Exemption);
                return true;
            }

            private bool Exemption(ExemptionDto x, string path)
            {
                if (!CompanyKey.IsValid(x.companyKey)) return Drop(path, "the exemption is for \"" + x.companyKey + "\", which is not a company key");

                x.kind = x.kind ?? string.Empty;
                x.factor = Amount(x.factor, path + ".factor");
                x.endDay = Day(x.endDay, path + ".endDay");
                return true;
            }

            private bool PriceWar(PriceWarDto w, string path)
            {
                if (string.IsNullOrEmpty(w.warId)) return Drop(path, "the price war has no id");

                w.endDay = Day(w.endDay, path + ".endDay");
                w.restore = Keep(w.restore, path + ".restore", Restore);
                return true;
            }

            private bool Restore(PriceRestoreDto p, string path)
            {
                // A price of zero or less would set the CPU's price to nonsense when the war ends: better to leave that CPU alone.
                if (p.cpuSaveId < 0) return Drop(path, "the CPU id " + p.cpuSaveId + " is negative");
                if (IsBad(p.originalTargetPrice) || p.originalTargetPrice <= 0) return Drop(path, "the price to restore (" + Show(p.originalTargetPrice) + ") is not a price");
                return true;
            }

            private bool Dispute(DisputeDto d, string path)
            {
                if (string.IsNullOrEmpty(d.disputeId)) return Drop(path, "the dispute has no id");
                if (!CompanyKey.IsValid(d.plaintiffKey) || !CompanyKey.IsValid(d.defendantKey)) return Drop(path, "the plaintiff or the defendant is not a company key");

                d.mode = d.mode ?? string.Empty;
                d.choice = d.choice ?? string.Empty;
                d.endDay = Day(d.endDay, path + ".endDay");
                d.resolveDay = Day(d.resolveDay, path + ".resolveDay");
                d.monthlyPaymentPct = Amount(d.monthlyPaymentPct, path + ".monthlyPaymentPct");
                return true;
            }

            private bool Race(RaceDto r, string path)
            {
                if (string.IsNullOrEmpty(r.raceId)) return Drop(path, "the race has no id");

                r.attribute = r.attribute ?? string.Empty;
                r.topN = Count(r.topN, path + ".topN");
                r.bonus = Amount(r.bonus, path + ".bonus");
                r.endDay = Day(r.endDay, path + ".endDay");
                r.segments = Segments(r.segments, path + ".segments");
                r.topCpuIds = Ids(r.topCpuIds, path + ".topCpuIds");
                return true;
            }

            // ---- the values ----

            private int Day(int value, string path)
            {
                if (value >= 0) return value;
                Note(path, "the day " + value + " is negative; taken as 0");
                return 0;
            }

            private int Count(int value, string path)
            {
                if (value >= 0) return value;
                Note(path, "the count " + value + " is negative; taken as 0");
                return 0;
            }

            /// <summary>A sum, a rate, a threshold: finite and not negative.</summary>
            private float Amount(float value, string path)
            {
                if (!IsBad(value) && value >= 0) return value;
                Note(path, "the value " + Show(value) + " is not a finite number of at least 0; taken as 0");
                return 0;
            }

            /// <summary>A share or a probability: 0 to 1.</summary>
            private float Share(float value, string path)
            {
                if (!IsBad(value) && value >= 0 && value <= 1) return value;
                float fixedValue = IsBad(value) ? 0 : Math.Max(0, Math.Min(1, value));
                Note(path, "the value " + Show(value) + " is outside 0..1; taken as " + Show(fixedValue));
                return fixedValue;
            }

            private List<string> Strings(List<string> list, string path)
            {
                var result = new List<string>();
                if (list == null) return result;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.IsNullOrEmpty(list[i])) Note(path + "[" + i + "]", "an empty name is dropped");
                    else if (seen.Add(list[i])) result.Add(list[i]);
                }
                return result;
            }

            private List<string> CompanyKeys(List<string> list, string path)
            {
                var result = new List<string>();
                if (list == null) return result;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    if (!CompanyKey.IsValid(list[i])) Note(path + "[" + i + "]", "\"" + list[i] + "\" is not a company key; dropped");
                    else if (seen.Add(list[i])) result.Add(list[i]);
                }
                return result;
            }

            private List<int> Segments(List<int> list, string path)
            {
                var result = new List<int>();
                if (list == null) return result;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] < 0 || list[i] >= SegmentCount) Note(path + "[" + i + "]", "the segment " + list[i] + " does not exist; dropped");
                    else if (!result.Contains(list[i])) result.Add(list[i]);
                }
                return result;
            }

            private List<int> Ids(List<int> list, string path)
            {
                var result = new List<int>();
                if (list == null) return result;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] < 0) Note(path + "[" + i + "]", "the id " + list[i] + " is negative; dropped");
                    else result.Add(list[i]);
                }
                return result;
            }

            // ---- lists ----

            private List<T> Keep<T>(List<T> list, string path, Func<T, string, bool> normalize) where T : class
            {
                var result = new List<T>();
                if (list == null) return result;

                for (int i = 0; i < list.Count; i++)
                {
                    string where = path + "[" + i + "]";
                    if (list[i] == null)
                    {
                        Note(where, "an empty item is dropped");
                        continue;
                    }
                    if (normalize(list[i], where)) result.Add(list[i]);
                }
                return result;
            }

            private void DropRepeats<T>(List<T> list, string path, Func<T, string> idOf)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    if (seen.Add(idOf(list[i]))) continue;
                    Note(path + "[" + i + "]", "\"" + idOf(list[i]) + "\" appears twice; the first one stays");
                    list.RemoveAt(i);
                    i--;
                }
            }

            private bool Drop(string path, string why)
            {
                Note(path, why + "; dropped");
                return false;
            }

            private void Note(string path, string message)
            {
                if (_problems != null) _problems.Add("save " + path + ": " + message);
            }

            private static bool IsBad(float value)
            {
                return float.IsNaN(value) || float.IsInfinity(value);
            }

            private static string Show(float value)
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }
}
