using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The outcome of <see cref="ArchCatalogValidator.Validate"/>: the families that passed, why the others did not, and whether the default
    /// family <c>proprietary</c> is among them (without it the feature cannot work, FR-1 item 4).
    /// </summary>
    public sealed class ValidationResult
    {
        private readonly List<ArchFamily> _families = new List<ArchFamily>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>The accepted families in the order they came in; each one requires only accepted families, and there is no cycle among them.</summary>
        public IReadOnlyList<ArchFamily> Families
        {
            get { return _families; }
        }

        /// <summary>One English line per skipped family (or per problem of the list), each starting with the family id.</summary>
        public IReadOnlyList<string> Errors
        {
            get { return _errors; }
        }

        /// <summary>Notes about families that were accepted.</summary>
        public IReadOnlyList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>The default family <see cref="ArchCatalog.DefaultId"/> is among the accepted families.</summary>
        public bool HasDefault { get; internal set; }

        internal List<ArchFamily> FamilyList
        {
            get { return _families; }
        }

        internal List<string> ErrorList
        {
            get { return _errors; }
        }

        internal List<string> WarningList
        {
            get { return _warnings; }
        }
    }

    /// <summary>
    /// The rules of the family catalog (FR-1 item 3, design.md "Security"): the values of one record, and the rules across records - duplicate ids,
    /// unknown dependencies, cycles. A record that breaks a rule is skipped with the reason and the others go on; a family whose dependency was skipped
    /// is skipped too. Pure logic: the same code checks the built-in file, the player's override and the lists the tests build by hand.
    /// </summary>
    public static class ArchCatalogValidator
    {
        public const int MaxFamilies = 32;
        public const int MinYear = 1971;
        public const int MaxYear = 2050;
        public const float MinMultiplier = 0.5f;
        public const float MaxMultiplier = 2.0f;
        public const float MinAffinity = 0.7f;
        public const float MaxAffinity = 1.3f;
        public const float MaxBaseDays = 3650f;
        public const float MaxMonthlyCost = 1e12f;
        public const int MaxRequires = 8;
        public const int SegmentCount = 4;

        /// <summary>
        /// Checks the values of one family. False when the record must be skipped; the messages (English, without the id) explain every broken rule.
        /// Soft remarks about an accepted record are added too (the content loader logs them as warnings).
        /// </summary>
        public static bool ValidateRecord(ArchFamily family, List<string> messages)
        {
            if (messages == null) messages = new List<string>();
            if (family == null)
            {
                messages.Add("the record is null");
                return false;
            }

            int before = messages.Count;
            if (!ArchIds.IsFamilyId(family.Id)) messages.Add("id: must be 1-32 characters out of a-z, 0-9 and _ (got '" + family.Id + "')");
            if (!ArchIds.IsIsaId(family.Isa)) messages.Add("isa: must be 1-16 characters out of a-z, 0-9 and _ (got '" + family.Isa + "')");
            CheckKey(family.NameKey, "nameKey", messages);
            CheckKey(family.DescKey, "descKey", messages);
            if (family.Year < MinYear || family.Year > MaxYear) messages.Add("year: " + family.Year.ToString(CultureInfo.InvariantCulture) + " is outside " + MinYear + ".." + MaxYear);

            CheckRequires(family, messages);
            CheckResearch(family.Research, messages);
            CheckDesign(family.Design, messages);
            CheckAffinity(family.Affinity, messages);
            CheckFade(family.Fade, messages);
            CheckSupport(family.Support, messages);

            if (family.Id == ArchCatalog.DefaultId && family.Requires != null && family.Requires.Length > 0)
                messages.Add("requires: the default family '" + ArchCatalog.DefaultId + "' must not require another family");

            // Only the messages added here count: the list may arrive with remarks of the mapper.
            return messages.Count == before;
        }

        /// <summary>
        /// Checks a whole list of families: every record by <see cref="ValidateRecord"/>, then the rules across records (duplicate ids, at most
        /// <see cref="MaxFamilies"/> families, unknown or skipped dependencies, dependency cycles), until nothing changes. Never throws; a null list is empty.
        /// </summary>
        public static ValidationResult Validate(IReadOnlyList<ArchFamily> raw)
        {
            var result = new ValidationResult();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var accepted = result.FamilyList;

            if (raw != null)
            {
                for (int i = 0; i < raw.Count; i++)
                {
                    ArchFamily family = raw[i];
                    if (family == null)
                    {
                        result.ErrorList.Add("record #" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": the record is null");
                        continue;
                    }

                    string id = family.Id ?? "#" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    if (!seen.Add(id))
                    {
                        result.ErrorList.Add(id + ": duplicate id; the first record with it is kept");
                        continue;
                    }

                    var messages = new List<string>();
                    if (!ValidateRecord(family, messages))
                    {
                        foreach (string message in messages) result.ErrorList.Add(id + ": " + message);
                        continue;
                    }

                    if (accepted.Count >= MaxFamilies)
                    {
                        result.ErrorList.Add(id + ": more than " + MaxFamilies.ToString(CultureInfo.InvariantCulture) + " families; this one is skipped");
                        continue;
                    }

                    foreach (string message in messages) result.WarningList.Add(id + ": " + message);
                    accepted.Add(family);
                }
            }

            RemoveBrokenDependencies(accepted, seen, result.ErrorList);

            foreach (ArchFamily family in accepted)
            {
                if (family.Id == ArchCatalog.DefaultId) result.HasDefault = true;
            }
            if (!result.HasDefault)
            {
                result.ErrorList.Add(ArchCatalog.DefaultId + ": the default family is missing or invalid; the feature cannot work without it");
            }
            return result;
        }

        // ---- the rules across records ----

        /// <summary>Skips the families that require an unknown or skipped family and the members of dependency cycles, until the list is stable.</summary>
        private static void RemoveBrokenDependencies(List<ArchFamily> accepted, HashSet<string> everSeen, List<string> errors)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;

                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ArchFamily family in accepted) ids.Add(family.Id);

                for (int i = accepted.Count - 1; i >= 0; i--)
                {
                    ArchFamily family = accepted[i];
                    string missing = null;
                    foreach (string required in family.Requires)
                    {
                        if (!ids.Contains(required))
                        {
                            missing = required;
                            break;
                        }
                    }
                    if (missing == null) continue;

                    errors.Add(family.Id + ": requires "
                               + (everSeen.Contains(missing) ? "'" + missing + "', which was skipped" : "an unknown family '" + missing + "'")
                               + "; this family is skipped");
                    accepted.RemoveAt(i);
                    changed = true;
                }
                if (changed) continue;

                List<string> cycle = FindCycle(accepted);
                if (cycle == null) continue;

                errors.Add(string.Join(", ", cycle.ToArray()) + ": the families require each other in a cycle (" + string.Join(" -> ", cycle.ToArray())
                           + " -> " + cycle[0] + "); they are skipped");
                accepted.RemoveAll(f => cycle.Contains(f.Id));
                changed = true;
            }
        }

        /// <summary>The ids of one dependency cycle (in the order of the cycle), or null when there is none.</summary>
        private static List<string> FindCycle(List<ArchFamily> families)
        {
            var byId = new Dictionary<string, ArchFamily>(StringComparer.Ordinal);
            foreach (ArchFamily family in families) byId[family.Id] = family;

            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArchFamily start in families)
            {
                if (done.Contains(start.Id)) continue;
                var path = new List<string>();
                List<string> cycle = Walk(start.Id, byId, done, path);
                if (cycle != null) return cycle;
            }
            return null;
        }

        private static List<string> Walk(string id, Dictionary<string, ArchFamily> byId, HashSet<string> done, List<string> path)
        {
            int at = path.IndexOf(id);
            if (at >= 0) return path.GetRange(at, path.Count - at);
            if (done.Contains(id)) return null;

            path.Add(id);
            ArchFamily family;
            if (byId.TryGetValue(id, out family))
            {
                foreach (string required in family.Requires)
                {
                    List<string> cycle = Walk(required, byId, done, path);
                    if (cycle != null) return cycle;
                }
            }
            path.RemoveAt(path.Count - 1);
            done.Add(id);
            return null;
        }

        // ---- the values of one record ----

        private static void CheckKey(string key, string name, List<string> messages)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 100) messages.Add(name + ": must be a L10n key of 1-100 characters");
        }

        private static void CheckRequires(ArchFamily family, List<string> messages)
        {
            if (family.Requires == null)
            {
                messages.Add("requires: must be a list");
                return;
            }
            if (family.Requires.Length > MaxRequires) messages.Add("requires: more than " + MaxRequires.ToString(CultureInfo.InvariantCulture) + " entries");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string required in family.Requires)
            {
                if (!ArchIds.IsFamilyId(required)) messages.Add("requires: '" + required + "' is not a family id");
                else if (required == family.Id) messages.Add("requires: the family requires itself");
                else if (!seen.Add(required)) messages.Add("requires: '" + required + "' is listed twice");
            }
        }

        private static void CheckResearch(ResearchDef research, List<string> messages)
        {
            if (research == null)
            {
                messages.Add("research: is required");
                return;
            }
            CheckRange(research.BaseDays, 0f, MaxBaseDays, "research.baseDays", messages);
            CheckRange(research.MonthlyCost, 0f, MaxMonthlyCost, "research.monthlyCost", messages);
        }

        private static void CheckDesign(DesignDef design, List<string> messages)
        {
            if (design == null)
            {
                messages.Add("design: is required");
                return;
            }
            CheckRange(design.Ipc, MinMultiplier, MaxMultiplier, "design.ipc", messages);
            CheckRange(design.Power, MinMultiplier, MaxMultiplier, "design.power", messages);
            CheckRange(design.DevCost, MinMultiplier, MaxMultiplier, "design.devCost", messages);
        }

        private static void CheckAffinity(float[] affinity, List<string> messages)
        {
            if (affinity == null || affinity.Length != SegmentCount)
            {
                messages.Add("affinity: must have exactly " + SegmentCount.ToString(CultureInfo.InvariantCulture) + " numbers (HighEnd, MidRange, LowEnd, Industries)");
                return;
            }
            for (int i = 0; i < affinity.Length; i++)
            {
                CheckRange(affinity[i], MinAffinity, MaxAffinity, "affinity[" + i.ToString(CultureInfo.InvariantCulture) + "]", messages);
            }
        }

        private static void CheckFade(FadeDef fade, List<string> messages)
        {
            if (fade == null) return;

            if (fade.FromYear < MinYear || fade.FromYear > MaxYear) messages.Add("fade.fromYear: " + fade.FromYear.ToString(CultureInfo.InvariantCulture) + " is outside " + MinYear + ".." + MaxYear);
            if (fade.ToYear < MinYear || fade.ToYear > MaxYear) messages.Add("fade.toYear: " + fade.ToYear.ToString(CultureInfo.InvariantCulture) + " is outside " + MinYear + ".." + MaxYear);
            if (fade.ToYear < fade.FromYear) messages.Add("fade.toYear: must not be earlier than fade.fromYear");
            CheckRange(fade.Min, 0.5f, 1f, "fade.min", messages);

            if (fade.Segments == null || fade.Segments.Length == 0 || fade.Segments.Length > SegmentCount)
            {
                messages.Add("fade.segments: must list 1-4 segments (0 HighEnd, 1 MidRange, 2 LowEnd, 3 Industries)");
                return;
            }
            var seen = new HashSet<int>();
            foreach (int segment in fade.Segments)
            {
                if (segment < 0 || segment >= SegmentCount) messages.Add("fade.segments: " + segment.ToString(CultureInfo.InvariantCulture) + " is not a segment (0..3)");
                else if (!seen.Add(segment)) messages.Add("fade.segments: " + segment.ToString(CultureInfo.InvariantCulture) + " is listed twice");
            }
        }

        private static void CheckSupport(SupportDef support, List<string> messages)
        {
            if (support == null)
            {
                messages.Add("support: is required");
                return;
            }
            CheckRange(support.UpkeepPerMonth, 0f, 1e10f, "support.upkeepPerMonth", messages);
            CheckRange(support.DecayRate, 0f, 1f, "support.decayRate", messages);
            CheckRange(support.TargetCap, 0f, 100f, "support.targetCap", messages);

            float[][] era = support.EraCost;
            if (era == null) return;
            if (era.Length > 16) messages.Add("support.eraCost: more than 16 points");
            float previousYear = float.NegativeInfinity;
            for (int i = 0; i < era.Length; i++)
            {
                string name = "support.eraCost[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (era[i] == null || era[i].Length != 2)
                {
                    messages.Add(name + ": must be a pair [year, factor]");
                    continue;
                }
                CheckRange(era[i][0], MinYear, MaxYear, name + " year", messages);
                CheckRange(era[i][1], 0.1f, 5f, name + " factor", messages);
                if (era[i][0] <= previousYear) messages.Add(name + ": years must ascend");
                previousYear = era[i][0];
            }
        }

        private static void CheckRange(float value, float min, float max, string name, List<string> messages)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            {
                messages.Add(name + ": " + value.ToString("R", CultureInfo.InvariantCulture) + " is outside " + min.ToString("R", CultureInfo.InvariantCulture)
                             + ".." + max.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
