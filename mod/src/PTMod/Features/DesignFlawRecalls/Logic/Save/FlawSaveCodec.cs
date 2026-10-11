using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// Reads and writes the section "design-flaw-recalls" of the side save file (FR-16, NFR-3, NFR-7): <see cref="FlawSaveData"/> to and from the
    /// JSON object the core hands over, by <c>ModJson.Bind</c> and <c>ModJson.From</c>. The version of the section (<c>_schema</c>) belongs to the
    /// core: <see cref="SchemaVersion"/> is what the participant reports, and <see cref="Write"/> never returns a <c>_schema</c> member.
    /// <para>
    /// What is checked on load (design.md, "Security"): a record whose <c>state</c>, <c>severity</c>, <c>source</c> or <c>response</c> is not a value of its
    /// enum, whose days are negative, whose sums are NaN or infinite, whose company key is not <c>player</c> / <c>ai:N</c>, or one of whose members has the
    /// wrong JSON type is <b>closed</b> (<see cref="FlawState.Closed"/>) with the offending value reset, so nothing downstream ever casts a bad
    /// number to an enum. A rush project with a bad key or a negative day is dropped; ids that point nowhere are dropped from the lists. Every
    /// repair is a line in the problem list (the caller logs it). Never throws, never returns <c>null</c>.
    /// </para>
    /// </summary>
    public static class FlawSaveCodec
    {
        /// <summary>The version of the section this build writes (<c>IModSaveParticipant.SchemaVersion</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The name of the version member, which the core adds and strips; the codec removes it if it ever gets in.</summary>
        private const string SchemaMember = "_schema";

        private static readonly Regex RecordError = new Regex(@"^\$\.records\[(\d+)\]", RegexOptions.Compiled);

        /// <summary>
        /// The section as a DTO. A null section (nothing saved) gives an empty state. A member of the wrong type keeps its default and adds a line to
        /// <paramref name="problems"/> (may be null); unknown members are ignored; the values are then checked by <see cref="Sanitize"/>.
        /// </summary>
        public static FlawSaveData Read(JsonObject section, List<string> problems)
        {
            if (section == null) return new FlawSaveData();

            var errors = new List<string>();
            FlawSaveData data = ModJson.Bind<FlawSaveData>(section, errors);

            // A wrong-typed member inside a record spoils that record only: it is closed below. The other errors are reported as they are.
            var damaged = new HashSet<int>();
            foreach (string error in errors)
            {
                Match match = RecordError.Match(error);
                int index;
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index)) damaged.Add(index);
                if (problems != null) problems.Add("section" + error.Substring(1));
            }

            return Repair(data, problems, damaged);
        }

        /// <summary>The section as JSON, without <c>_schema</c> (the core adds it). A null DTO is written as the empty state.</summary>
        public static JsonObject Write(FlawSaveData data)
        {
            JsonNode node = ModJson.From(data ?? new FlawSaveData());
            var section = node as JsonObject ?? new JsonObject();
            section.Remove(SchemaMember);
            return section;
        }

        /// <summary>
        /// The migration step of <c>Read(section, schema)</c>: brings a section stored with version <paramref name="fromSchema"/> to
        /// <paramref name="toSchema"/> one version at a time (1 to 2, 2 to 3, ...) and returns the result without touching the input (a step works on a
        /// copy). There is only version 1 so far, so there is nothing to do; the first schema change adds its <c>case</c> here and a test for it
        /// (NFR-3: migrations are described in the design and live in this one place).
        /// </summary>
        public static JsonObject Migrate(JsonObject section, int fromSchema, int toSchema, List<string> problems)
        {
            JsonObject current = section;
            for (int version = Math.Max(fromSchema, 1); version < toSchema; version++)
            {
                if (TryMigrateStep(version, ref current)) continue;

                if (problems != null) problems.Add("no migration from schema " + version.ToString(CultureInfo.InvariantCulture) + " to " + (version + 1).ToString(CultureInfo.InvariantCulture));
                return current;
            }
            return current;
        }

        /// <summary>One step of <see cref="Migrate"/>: version N to N+1. False when there is no such step.</summary>
        private static bool TryMigrateStep(int fromVersion, ref JsonObject section)
        {
            switch (fromVersion)
            {
                // case 1: section = ToVersion2((JsonObject)section.DeepClone()); return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks and repairs a DTO in place (see the class summary) and returns it; a null DTO gives an empty one. Public so that a state built in
        /// memory can be checked the same way as a loaded one (a NaN cannot come out of JSON, but can come out of code).
        /// </summary>
        public static FlawSaveData Sanitize(FlawSaveData data, List<string> problems)
        {
            return Repair(data, problems, null);
        }

        // ---- the checks ----

        private static FlawSaveData Repair(FlawSaveData data, List<string> problems, HashSet<int> damaged)
        {
            if (data == null) data = new FlawSaveData();

            if (data.lastActiveDay < 0)
            {
                Note(problems, "lastActiveDay " + data.lastActiveDay + " is negative; 0 is used");
                data.lastActiveDay = 0;
            }

            data.rolledCpuIds = CleanIds(data.rolledCpuIds, "rolledCpuIds", problems);
            data.rushCpuIds = CleanIds(data.rushCpuIds, "rushCpuIds", problems);
            data.rushProjects = CleanProjects(data.rushProjects, problems);

            var records = new List<FlawRecordDto>();
            var ids = new HashSet<int>();
            int maxId = 0;
            List<FlawRecordDto> source = data.records ?? new List<FlawRecordDto>();
            for (int i = 0; i < source.Count; i++)
            {
                FlawRecordDto record = source[i];
                if (record == null)
                {
                    Note(problems, "records[" + i + "] is empty and is dropped");
                    continue;
                }
                if (record.id < 1)
                {
                    Note(problems, "records[" + i + "] has the id " + record.id + " (ids start at 1) and is dropped");
                    continue;
                }
                if (!ids.Add(record.id))
                {
                    Note(problems, "records[" + i + "] repeats the id " + record.id + " and is dropped");
                    continue;
                }

                CheckRecord(record, i, damaged != null && damaged.Contains(i), problems);
                records.Add(record);
                if (record.id > maxId) maxId = record.id;
            }
            data.records = records;

            int minimumNextId = maxId + 1;
            if (data.nextRecordId < minimumNextId)
            {
                Note(problems, "nextRecordId " + data.nextRecordId + " is not above the highest record id " + maxId + "; " + minimumNextId + " is used");
                data.nextRecordId = minimumNextId;
            }

            data.pendingDecisionQueue = CleanQueue(data.pendingDecisionQueue, records, problems);
            return data;
        }

        /// <summary>Closes the record when anything in it is not usable and resets the offending values.</summary>
        private static void CheckRecord(FlawRecordDto r, int index, bool damaged, List<string> problems)
        {
            var reasons = new List<string>();
            if (damaged) reasons.Add("a member has the wrong type");

            if (r.cpuIds == null) r.cpuIds = new List<int>();
            if (r.modifierEntryIds == null) r.modifierEntryIds = new List<string>();
            r.modifierEntryIds.RemoveAll(string.IsNullOrEmpty);

            if (!CompanyKey.IsValid(r.companyKey))
            {
                reasons.Add("the company key '" + r.companyKey + "' is not player or ai:N");
                r.companyKey = r.companyKey ?? string.Empty;
            }
            if (string.IsNullOrEmpty(r.typeId))
            {
                reasons.Add("the type id is empty");
                r.typeId = r.typeId ?? string.Empty;
            }
            if (r.baseCpuId < 0)
            {
                reasons.Add("baseCpuId " + r.baseCpuId + " is negative");
                r.baseCpuId = 0;
            }

            r.cpuIds.RemoveAll(id => id < 0);
            if (!r.cpuIds.Contains(r.baseCpuId)) r.cpuIds.Insert(0, r.baseCpuId);

            if (!Enum.IsDefined(typeof(FlawSeverity), r.severity))
            {
                reasons.Add("severity " + r.severity + " is not a FlawSeverity");
                r.severity = (int)FlawSeverity.Minor;
            }
            if (!Enum.IsDefined(typeof(FlawState), r.state))
            {
                reasons.Add("state " + r.state + " is not a FlawState");
                r.state = (int)FlawState.Closed;
            }
            if (!Enum.IsDefined(typeof(DiscoverySource), r.source))
            {
                reasons.Add("source " + r.source + " is not a DiscoverySource");
                r.source = (int)DiscoverySource.None;
            }
            if (!Enum.IsDefined(typeof(FlawResponse), r.response))
            {
                reasons.Add("response " + r.response + " is not a FlawResponse");
                r.response = (int)FlawResponse.None;
            }

            Day(ref r.releaseDay, "releaseDay", reasons);
            Day(ref r.discoveredDay, "discoveredDay", reasons);
            Day(ref r.publicSinceDay, "publicSinceDay", reasons);
            Day(ref r.steppingEndDay, "steppingEndDay", reasons);
            Day(ref r.lastChargedDay, "lastChargedDay", reasons);
            Day(ref r.mitigationEndDay, "mitigationEndDay", reasons);
            Day(ref r.ignoreDrainUntilDay, "ignoreDrainUntilDay", reasons);
            Day(ref r.leakTailUntilDay, "leakTailUntilDay", reasons);
            if (r.unitsAtDiscovery < 0)
            {
                reasons.Add("unitsAtDiscovery " + r.unitsAtDiscovery + " is negative");
                r.unitsAtDiscovery = 0;
            }

            Sum(ref r.riskAtRelease, "riskAtRelease", reasons);
            Sum(ref r.inflationAtRelease, "inflationAtRelease", reasons);
            Sum(ref r.steppingDailyCost, "steppingDailyCost", reasons);
            Sum(ref r.ipsLoss, "ipsLoss", reasons);
            Sum(ref r.spentTotal, "spentTotal", reasons);

            FlawQuoteDto q = r.quote;
            if (q != null)
            {
                Sum(ref q.RecallCost, "quote.RecallCost", reasons);
                Sum(ref q.SteppingCost, "quote.SteppingCost", reasons);
                Sum(ref q.SteppingDailyCost, "quote.SteppingDailyCost", reasons);
                Sum(ref q.ResidualClaimsCost, "quote.ResidualClaimsCost", reasons);
                Sum(ref q.PatchCost, "quote.PatchCost", reasons);
                Sum(ref q.PatchIpsLoss, "quote.PatchIpsLoss", reasons);
                Sum(ref q.ClassActionCost, "quote.ClassActionCost", reasons);
                Sum(ref q.OemCancelChance, "quote.OemCancelChance", reasons);
                Sum(ref q.IgnoreSignChanceMultiplier, "quote.IgnoreSignChanceMultiplier", reasons);
                if (q.SteppingDays < 0 || q.PatchMonths < 0 || q.AffectedUnits < 0)
                {
                    reasons.Add("the quote has a negative term or unit count");
                    q.SteppingDays = Math.Max(0, q.SteppingDays);
                    q.PatchMonths = Math.Max(0, q.PatchMonths);
                    q.AffectedUnits = Math.Max(0, q.AffectedUnits);
                }
            }

            if (reasons.Count == 0) return;

            r.state = (int)FlawState.Closed;
            Note(problems, "records[" + index + "] (id " + r.id + ") is closed: " + string.Join("; ", reasons));
        }

        private static void Day(ref int day, string name, List<string> reasons)
        {
            if (day >= 0) return;
            reasons.Add(name + " " + day + " is negative");
            day = 0;
        }

        private static void Sum(ref float value, string name, List<string> reasons)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value)) return;
            reasons.Add(name + " is not a finite number");
            value = 0f;
        }

        private static List<int> CleanIds(List<int> ids, string name, List<string> problems)
        {
            var result = new List<int>();
            if (ids == null) return result;

            var seen = new HashSet<int>();
            int dropped = 0;
            foreach (int id in ids)
            {
                if (id < 0 || !seen.Add(id)) dropped++;
                else result.Add(id);
            }
            if (dropped > 0) Note(problems, name + ": " + dropped + " negative or repeated id(s) dropped");
            return result;
        }

        private static List<RushProjectDto> CleanProjects(List<RushProjectDto> projects, List<string> problems)
        {
            var result = new List<RushProjectDto>();
            if (projects == null) return result;

            for (int i = 0; i < projects.Count; i++)
            {
                RushProjectDto project = projects[i];
                if (project == null)
                {
                    Note(problems, "rushProjects[" + i + "] is empty and is dropped");
                    continue;
                }
                if (!CompanyKey.IsValid(project.companyKey))
                {
                    Note(problems, "rushProjects[" + i + "] has the company key '" + project.companyKey + "' (player or ai:N expected) and is dropped");
                    continue;
                }
                if (project.plannedEndDay < 0)
                {
                    Note(problems, "rushProjects[" + i + "] has a negative plannedEndDay and is dropped");
                    continue;
                }
                if (project.cpuName == null) project.cpuName = string.Empty;
                result.Add(project);
            }
            return result;
        }

        /// <summary>The queue keeps the ids of records that exist and are not finished; each once, in order.</summary>
        private static List<int> CleanQueue(List<int> queue, List<FlawRecordDto> records, List<string> problems)
        {
            var result = new List<int>();
            if (queue == null) return result;

            var open = new HashSet<int>();
            foreach (FlawRecordDto record in records)
            {
                var state = (FlawState)record.state;
                if (state != FlawState.Resolved && state != FlawState.Expired && state != FlawState.Closed) open.Add(record.id);
            }

            var seen = new HashSet<int>();
            int dropped = 0;
            foreach (int id in queue)
            {
                if (open.Contains(id) && seen.Add(id)) result.Add(id);
                else dropped++;
            }
            if (dropped > 0) Note(problems, "pendingDecisionQueue: " + dropped + " id(s) of missing, finished or repeated records dropped");
            return result;
        }

        private static void Note(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
