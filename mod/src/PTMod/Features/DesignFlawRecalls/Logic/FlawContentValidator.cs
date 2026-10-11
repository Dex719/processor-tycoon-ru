using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>What <see cref="FlawContentValidator"/> found: a line per problem, in the order the file was read. Plain lists, no logging here.</summary>
    public sealed class FlawContentReport
    {
        /// <summary>Values that were rejected (wrong type, out of range, a skipped type, a broken file): each one means a default was used instead.</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>Things that did not change the numbers: an unknown member, a name key that no language table has.</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>True when the file was read without a single note.</summary>
        public bool IsClean
        {
            get { return Errors.Count == 0 && Warnings.Count == 0; }
        }
    }

    /// <summary>
    /// Reads and checks <c>Content/design-flaw-recalls/flaws.json</c> (FR-15, NFR-7; design.md, "Security"). The input is the merged config of
    /// <c>ModContent.LoadConfig</c> (the built-in file with the player's override on top), so an override goes through the same checks as the
    /// shipped numbers. The rules:
    /// <list type="bullet">
    /// <item>probabilities and shares are in [0, 1]; multipliers and coefficients in [0, 10]; days in [1, 3650]; months in [1, 120]; years in
    /// [1971, 2030]; every array by severity has exactly 3 numbers, <c>difficultyMultiplier</c> has 6, the weights of the AI policy are four numbers in [0, 1];</item>
    /// <item><c>ipsLoss</c> is at most 0.5, and the multipliers of <c>contract.signChance</c> (waiting, ignore) are at least 0.5, so the channels of
    /// <c>ModModifiers</c> never get a factor below 0.5 from this feature (NFR-8);</item>
    /// <item><c>risk.kRush</c> in [0, 1], <c>rush.timeCut</c> in [0, 0.5], <c>rush.costMultiplier</c> in [1, 2], <c>responses.pendingGraceDays</c> in [0, 365];</item>
    /// <item>a flaw type needs an unique <c>id</c> and a <c>nameKey</c> with the prefix of the feature (<c>dfr.</c>); a key that no language table has is only
    /// a warning; a type with any error is skipped, and with no valid type left the built-in <c>arith</c> stands in.</item>
    /// </list>
    /// A rejected value leaves the default of <see cref="FlawBalance"/> in place and adds a line to <see cref="FlawContentReport.Errors"/>, for example
    /// <c>risk.pMax out of range [0,1]: 3; the default 0.5 is used</c> (AC-15.1). A missing member keeps its default without a note (an override
    /// may be partial). The reader marks every member it consumed, so a member nobody reads (a typo) is reported as a warning. Never throws.
    /// </summary>
    public static class FlawContentValidator
    {
        /// <summary>The <c>schema</c> of <c>flaws.json</c> this version reads (the loader passes it to <c>ModContent.LoadConfig</c>).</summary>
        public const int SupportedSchema = 1;

        /// <summary>The prefix every L10n key and event id of the feature carries (<c>ModIds</c>: the row of <c>design-flaw-recalls</c> in <c>core.json</c>).</summary>
        public const string KeyPrefix = "dfr.";

        private static readonly Regex TypeIdPattern = new Regex("^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.Compiled);

        /// <summary>
        /// Reads the JSON text of a config (a test or a tool; the game goes through <see cref="Validate"/>). A text that is not JSON, or not
        /// an object, gives the defaults and an error with the place of the syntax problem.
        /// </summary>
        public static FlawBalance Parse(string json, FlawContentReport report, Func<string, bool> nameKeyAllowed = null, Func<string, bool> nameKeyExists = null)
        {
            report = report ?? new FlawContentReport();
            JsonNode node = null;
            JsonError error = null;
            if (json == null || !ModJson.TryParse(json, out node, out error))
            {
                string where = error == null ? "no text" : error.Message + " (line " + error.Line + ", column " + error.Column + ")";
                report.Errors.Add("flaws.json is not valid JSON: " + where + "; the built-in values are used");
                return FlawBalance.Defaults();
            }
            if (!(node is JsonObject))
            {
                report.Errors.Add("flaws.json must be a JSON object; the built-in values are used");
                return FlawBalance.Defaults();
            }
            return Validate((JsonObject)node, report, nameKeyAllowed, nameKeyExists);
        }

        /// <summary>
        /// Reads a config object. <c>null</c> (no file, not an object) gives the defaults without a note: whoever loaded the file has said why.
        /// </summary>
        /// <param name="config">The merged config.</param>
        /// <param name="report">Receives the problems; <c>null</c> to ignore them.</param>
        /// <param name="nameKeyAllowed">Does the feature own this L10n key (<c>ModIds.IsL10nKeyAllowed</c>)? <c>null</c>: the key must start with <see cref="KeyPrefix"/>.</param>
        /// <param name="nameKeyExists">Does the current language have the key (<c>ModL10n.Has</c>)? <c>null</c>: not checked. A missing key is a warning.</param>
        public static FlawBalance Validate(JsonObject config, FlawContentReport report, Func<string, bool> nameKeyAllowed = null, Func<string, bool> nameKeyExists = null)
        {
            var result = FlawBalance.Defaults();
            if (config == null) return result;

            var context = new Context
            {
                Report = report ?? new FlawContentReport(),
                NameKeyAllowed = nameKeyAllowed ?? (key => key.StartsWith(KeyPrefix, StringComparison.Ordinal)),
                NameKeyExists = nameKeyExists,
            };

            try
            {
                Read(config, context, result);
            }
            catch (Exception e)
            {
                // The reader is written not to throw; if it does anyway, nothing half-read is used.
                context.Report.Errors.Add("flaws.json could not be read (" + e.GetType().Name + ": " + e.Message + "); the built-in values are used");
                return FlawBalance.Defaults();
            }
            return result;
        }

        // ---- the sections ----

        private static void Read(JsonObject config, Context context, FlawBalance result)
        {
            var root = new Section(config, string.Empty, context);
            root.Schema();

            Section risk = root.Child("risk");
            if (risk != null)
            {
                FlawBalance.RiskBalance r = result.Risk;
                risk.Float("base", 0, 1, ref r.Base);
                risk.Float("kFailureRate", 0, 10, ref r.KFailureRate);
                risk.Float("kOverclock", 0, 10, ref r.KOverclock);
                risk.Float("kThermal", 0, 10, ref r.KThermal);
                risk.Float("kNodeImmaturity", 0, 10, ref r.KNodeImmaturity);
                risk.Float("kRush", 0, 1, ref r.KRush);
                risk.Float("overclockCap", 0, 1, ref r.OverclockCap);
                risk.Float("unknownNodeImmaturity", 0, 1, ref r.UnknownNodeImmaturity);
                risk.Float("pMax", 0, 1, ref r.PMax);
                risk.Float("aiRiskMultiplier", 0, 10, ref r.AiRiskMultiplier);
                risk.FloatArray("difficultyMultiplier", FlawBalance.DifficultyCount, 0, 10, ref r.DifficultyMultiplier);
                ReadEra(risk, result);
                risk.Finish();
            }

            Section severity = root.Child("severity");
            if (severity != null)
            {
                FlawBalance.SeverityBalance s = result.Severity;
                severity.FloatArray("baseWeights", FlawBalance.SeverityCount, 0, 10, ref s.BaseWeights, requirePositiveSum: true);
                severity.Float("shiftMajor", 0, 10, ref s.ShiftMajor);
                severity.Float("shiftCritical", 0, 10, ref s.ShiftCritical);
                severity.Finish();
            }

            Section discovery = root.Child("discovery");
            if (discovery != null)
            {
                FlawBalance.DiscoveryBalance d = result.Discovery;
                discovery.FloatArray("hBase", FlawBalance.SeverityCount, 0, 1, ref d.HBase);
                discovery.Float("kUnits", 0, 10, ref d.KUnits);
                discovery.Float("unitsRef", 1, 1e9, ref d.UnitsRef);
                discovery.Float("hMax", 0, 1, ref d.HMax);
                discovery.IntArray("maxHiddenMonths", FlawBalance.SeverityCount, 1, 120, ref d.MaxHiddenMonths);
                discovery.FloatArray("qInternal", FlawBalance.SeverityCount, 0, 1, ref d.QInternal);
                discovery.Int("expireAfterRetiredMonths", 1, 120, ref d.ExpireAfterRetiredMonths);
                discovery.Int("expireNoSalesMonths", 1, 120, ref d.ExpireNoSalesMonths);
                Section leak = discovery.Child("leakRate");
                if (leak != null)
                {
                    leak.Float("ignore", 0, 1, ref d.LeakRate.Ignore);
                    leak.Float("quietStepping", 0, 1, ref d.LeakRate.QuietStepping);
                    leak.Float("tail", 0, 1, ref d.LeakRate.Tail);
                    leak.Finish();
                }
                discovery.Int("leakTailMonths", 1, 120, ref d.LeakTailMonths);
                discovery.Finish();
            }

            Section responses = root.Child("responses");
            if (responses != null)
            {
                ReadResponses(responses, result.Responses);
                responses.Finish();
            }

            Section reputation = root.Child("reputation");
            if (reputation != null)
            {
                FlawBalance.ReputationBalance rep = result.Reputation;
                reputation.IntArray("publicBase", FlawBalance.SeverityCount, -100, 100, ref rep.PublicBase);
                reputation.IntArray("recall", FlawBalance.SeverityCount, -100, 100, ref rep.Recall);
                reputation.IntArray("steppingPublic", FlawBalance.SeverityCount, -100, 100, ref rep.SteppingPublic);
                reputation.IntArray("steppingQuiet", FlawBalance.SeverityCount, -100, 100, ref rep.SteppingQuiet);
                reputation.IntArray("patch", FlawBalance.SeverityCount, -100, 100, ref rep.Patch);
                reputation.IntArray("ignorePublic", FlawBalance.SeverityCount, -100, 100, ref rep.IgnorePublic);
                reputation.Float("coverUpMultiplier", 0, 10, ref rep.CoverUpMultiplier);
                reputation.Finish();
            }

            Section press = root.Child("press");
            if (press != null)
            {
                press.IntArray("overallAdjustment", FlawBalance.SeverityCount, -20, 20, ref result.Press.OverallAdjustment);
                press.Finish();
            }

            ReadTypes(root, result);

            Section ai = root.Child("aiPolicy");
            if (ai != null)
            {
                AiPolicyDef p = result.AiPolicy;
                ReadAiWeights(ai, "public", p.Public);
                ReadAiWeights(ai, "internal", p.Internal);
                ai.Float("recallRequiresHeadroomFactor", 0, 10, ref p.RecallRequiresHeadroomFactor);
                ai.Finish();
            }

            Section rush = root.Child("rush");
            if (rush != null)
            {
                rush.Float("timeCut", 0, 0.5, ref result.Rush.TimeCut);
                rush.Float("costMultiplier", 1, 2, ref result.Rush.CostMultiplier);
                rush.Finish();
            }

            root.Finish();
        }

        /// <summary>The weights of one situation of the AI policy: an object with the four reactions, each in [0, 1].</summary>
        private static void ReadAiWeights(Section ai, string key, float[] weights)
        {
            Section section = ai.Child(key);
            if (section == null) return;

            section.Float("recall", 0, 1, ref weights[FlawResponseBiasOrder.Recall]);
            section.Float("stepping", 0, 1, ref weights[FlawResponseBiasOrder.Stepping]);
            section.Float("patch", 0, 1, ref weights[FlawResponseBiasOrder.Patch]);
            section.Float("ignore", 0, 1, ref weights[FlawResponseBiasOrder.Ignore]);
            section.Finish();
        }

        private static void ReadResponses(Section responses, FlawBalance.ResponsesBalance b)
        {
            responses.Int("timerDays", 1, 3650, ref b.TimerDays);
            responses.Float("pendingSignChanceMultiplier", 0.5, 1, ref b.PendingSignChanceMultiplier);
            responses.Int("pendingGraceDays", 0, 365, ref b.PendingGraceDays);

            Section recall = responses.Child("recall");
            if (recall != null)
            {
                FlawBalance.RecallBalance r = b.Recall;
                recall.FloatArray("returnRate", FlawBalance.SeverityCount, 0, 1, ref r.ReturnRate);
                recall.Float("logisticsShareOfPrice", 0, 1, ref r.LogisticsShareOfPrice);
                recall.Float("fixedCostBase", 0, 1e9, ref r.FixedCostBase);
                recall.Float("stockReworkShare", 0, 1, ref r.StockReworkShare);
                recall.Float("capShareOfRevenue12m", 0, 1, ref r.CapShareOfRevenue12m);
                recall.Finish();
            }

            Section stepping = responses.Child("stepping");
            if (stepping != null)
            {
                FlawBalance.SteppingBalance s = b.Stepping;
                stepping.FloatArray("costShareOfDevCost", FlawBalance.SeverityCount, 0, 1, ref s.CostShareOfDevCost);
                stepping.IntArray("days", FlawBalance.SeverityCount, 1, 3650, ref s.Days);
                stepping.Float("residualShareOfRecall", 0, 1, ref s.ResidualShareOfRecall);
                stepping.Finish();
            }

            Section patch = responses.Child("patch");
            if (patch != null)
            {
                FlawBalance.PatchBalance p = b.Patch;
                patch.Float("costShareOfDevCost", 0, 1, ref p.CostShareOfDevCost);
                patch.Float("fixedCostBase", 0, 1e9, ref p.FixedCostBase);
                patch.IntArray("months", FlawBalance.SeverityCount, 1, 120, ref p.Months);
                patch.Finish();
            }

            Section ignore = responses.Child("ignore");
            if (ignore != null)
            {
                FlawBalance.IgnoreBalance g = b.Ignore;
                ignore.FloatArray("oemCancel", FlawBalance.SeverityCount, 0, 1, ref g.OemCancel);
                ignore.FloatArray("signChance", FlawBalance.SeverityCount, 0.5, 1, ref g.SignChance);
                ignore.Int("signChanceMonths", 1, 120, ref g.SignChanceMonths);
                ignore.FloatArray("classAction", FlawBalance.SeverityCount, 0, 1, ref g.ClassAction);
                ignore.Float("suitShareOfRevenue", 0, 1, ref g.SuitShareOfRevenue);
                ignore.Int("drainMonths", 1, 120, ref g.DrainMonths);
                ignore.Int("drainPerMonth", -100, 0, ref g.DrainPerMonth);
                ignore.Finish();
            }

            Section popHit = responses.Child("popHit");
            if (popHit != null)
            {
                FlawBalance.PopHitBalance h = b.PopHit;
                popHit.FloatArray("public", FlawBalance.SeverityCount, 0, 1, ref h.Public);
                popHit.FloatArray("recall", FlawBalance.SeverityCount, 0, 1, ref h.Recall);
                popHit.FloatArray("stepping", FlawBalance.SeverityCount, 0, 1, ref h.Stepping);
                popHit.FloatArray("patch", FlawBalance.SeverityCount, 0, 1, ref h.Patch);
                popHit.FloatArray("ignore", FlawBalance.SeverityCount, 0, 1, ref h.Ignore);
                popHit.Finish();
            }
        }

        /// <summary>The era table: rows with an error are skipped; a table that is not an array keeps the default one.</summary>
        private static void ReadEra(Section risk, FlawBalance result)
        {
            JsonNode node = risk.Take("era");
            if (node == null) return;

            var array = node as JsonArray;
            if (array == null)
            {
                risk.Error("era", "must be an array of rows, got " + KindName(node) + "; the default table is used");
                return;
            }

            var rows = new List<FlawBalance.EraRow>();
            int index = 0;
            foreach (JsonNode item in array)
            {
                string path = risk.Path + ".era[" + index + "]";
                index++;

                var obj = item as JsonObject;
                if (obj == null)
                {
                    risk.Context.Report.Errors.Add(path + " must be an object, got " + KindName(item) + "; the row is skipped");
                    continue;
                }

                var row = new Section(obj, path, risk.Context);
                int errorsBefore = row.Context.Report.Errors.Count;
                var era = new FlawBalance.EraRow();
                row.RequiredInt("fromYear", FlawBalance.MinYear, FlawBalance.MaxYear, out era.FromYear);
                row.RequiredInt("toYear", FlawBalance.MinYear, FlawBalance.MaxYear, out era.ToYear);
                row.RequiredFloat("multiplier", 0, 10, out era.Multiplier);
                row.Finish();

                if (row.Context.Report.Errors.Count == errorsBefore && era.FromYear > era.ToYear)
                {
                    row.Context.Report.Errors.Add(path + ": fromYear " + era.FromYear + " is after toYear " + era.ToYear);
                }
                if (row.Context.Report.Errors.Count != errorsBefore)
                {
                    row.Context.Report.Errors.Add(path + " is skipped");
                    continue;
                }
                rows.Add(era);
            }
            result.Risk.Era = rows;
        }

        /// <summary>The kinds of flaw: a type with any error is skipped; none valid left means the built-in <c>arith</c>.</summary>
        private static void ReadTypes(Section root, FlawBalance result)
        {
            JsonNode node = root.Take("types");
            if (node == null) return;

            var array = node as JsonArray;
            if (array == null)
            {
                root.Error("types", "must be an array of types, got " + KindName(node) + "; the built-in types are used");
                return;
            }

            Context context = root.Context;
            var types = new List<FlawTypeDef>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonNode item in array)
            {
                string path = "types[" + index + "]";
                index++;

                var obj = item as JsonObject;
                if (obj == null)
                {
                    context.Report.Errors.Add(path + " must be an object, got " + KindName(item) + "; the type is skipped");
                    continue;
                }

                FlawTypeDef type = ReadType(obj, path, context);
                if (type == null) continue;

                if (!ids.Add(type.Id))
                {
                    context.Report.Errors.Add(path + ": the id '" + type.Id + "' is used by an earlier type; the type is skipped");
                    continue;
                }
                types.Add(type);
            }

            if (types.Count == 0)
            {
                context.Report.Errors.Add("types has no valid entry; the built-in '" + FlawTypeDef.FallbackId + "' type is used");
                types.Add(FlawTypeDef.FallbackArithmetic());
            }
            result.Types = types;
        }

        private static FlawTypeDef ReadType(JsonObject obj, string path, Context context)
        {
            var section = new Section(obj, path, context);
            int errorsBefore = context.Report.Errors.Count;
            var type = new FlawTypeDef { IpsLoss = new float[FlawBalance.SeverityCount] };

            section.RequiredString("id", out type.Id);
            if (type.Id != null && !TypeIdPattern.IsMatch(type.Id))
            {
                section.Error("id", "'" + type.Id + "' is not a valid id (a letter, then letters, digits, '_' or '-', up to 40 characters)");
                type.Id = null;
            }
            if (type.Id != null) section.Path = path + " (" + type.Id + ")";

            section.RequiredString("nameKey", out type.NameKey);
            if (type.NameKey != null)
            {
                if (!context.NameKeyAllowed(type.NameKey))
                {
                    section.Error("nameKey", "'" + type.NameKey + "' does not belong to the feature (a key of the strings of design-flaw-recalls starts with '" + KeyPrefix + "')");
                }
                else if (context.NameKeyExists != null && !context.NameKeyExists(type.NameKey))
                {
                    context.Report.Warnings.Add(section.Path + ".nameKey: no language table has '" + type.NameKey + "'");
                }
            }

            section.RequiredInt("fromYear", FlawBalance.MinYear, FlawBalance.MaxYear, out type.FromYear);
            section.RequiredInt("toYear", FlawBalance.MinYear, FlawBalance.MaxYear, out type.ToYear);
            section.RequiredFloat("weight", 0, 10, out type.Weight);
            section.OptionalFloat("weightByOverclock", 0, 10, 0f, out type.WeightByOverclock);
            section.OptionalFloat("weightByThermal", 0, 10, 0f, out type.WeightByThermal);
            section.OptionalBool("patchable", true, out type.Patchable);
            section.OptionalInt("patchFromYear", FlawBalance.MinYear, FlawBalance.MaxYear, type.FromYear, out type.PatchFromYear);
            section.RequiredFloatArray("ipsLoss", FlawBalance.SeverityCount, 0, 0.5, out type.IpsLoss);
            section.OptionalBool("recallable", true, out type.Recallable);
            section.Finish();

            if (context.Report.Errors.Count == errorsBefore && type.FromYear > type.ToYear)
            {
                context.Report.Errors.Add(section.Path + ": fromYear " + type.FromYear + " is after toYear " + type.ToYear);
            }

            if (context.Report.Errors.Count != errorsBefore)
            {
                context.Report.Errors.Add(section.Path + " is skipped");
                return null;
            }
            return type;
        }

        // ---- helpers ----

        private static string KindName(JsonNode node)
        {
            if (node == null) return "nothing";
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "a boolean";
                case JsonKind.Number: return "a number";
                case JsonKind.String: return "a string";
                case JsonKind.Object: return "an object";
                default: return "an array";
            }
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Num(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Range(double min, double max)
        {
            return "[" + Num(min) + "," + Num(max) + "]";
        }

        private static string ListText(float[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = Num(values[i]);
            return "[" + string.Join(", ", parts) + "]";
        }

        private static string ListText(int[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
            return "[" + string.Join(", ", parts) + "]";
        }

        /// <summary>What the readers share: the report and the two questions about name keys.</summary>
        private sealed class Context
        {
            public FlawContentReport Report;
            public Func<string, bool> NameKeyAllowed;
            public Func<string, bool> NameKeyExists;
        }

        /// <summary>
        /// One JSON object being read: typed accessors that check the range, keep the default on a problem and remember the keys they used,
        /// so that <see cref="Finish"/> can name the members nobody read.
        /// </summary>
        private sealed class Section
        {
            private readonly JsonObject _node;
            private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

            public Section(JsonObject node, string path, Context context)
            {
                _node = node;
                Path = path;
                Context = context;
            }

            public string Path { get; set; }

            public Context Context { get; private set; }

            private string Of(string key)
            {
                return Path.Length == 0 ? key : Path + "." + key;
            }

            public void Error(string key, string message)
            {
                Context.Report.Errors.Add(Of(key) + " " + message);
            }

            /// <summary>The member as it is (or null when there is none); marks it as read.</summary>
            public JsonNode Take(string key)
            {
                _used.Add(key);
                return _node[key];
            }

            /// <summary>The member as a section; null when it is missing (the defaults stay) or not an object (an error).</summary>
            public Section Child(string key)
            {
                JsonNode node = Take(key);
                if (node == null) return null;

                var obj = node as JsonObject;
                if (obj == null)
                {
                    Error(key, "must be an object, got " + KindName(node) + "; the defaults of the section are used");
                    return null;
                }
                return new Section(obj, Of(key), Context);
            }

            /// <summary><c>schema</c> is read by <c>ModContent</c>; a different number here is only worth a note.</summary>
            public void Schema()
            {
                JsonNode node = Take("schema");
                var value = node as JsonValue;
                if (value != null && value.Kind == JsonKind.Number && Math.Abs(value.NumberValue - SupportedSchema) > 0.5)
                {
                    Context.Report.Warnings.Add("schema " + Num(value.NumberValue) + " is not the schema " + SupportedSchema + " this version knows");
                }
            }

            /// <summary>Reports the members no accessor asked for.</summary>
            public void Finish()
            {
                foreach (KeyValuePair<string, JsonNode> pair in _node)
                {
                    if (!_used.Contains(pair.Key)) Context.Report.Warnings.Add(Of(pair.Key) + " is not a known member and is ignored");
                }
            }

            // ---- numbers with a default (the target keeps its value on a problem) ----

            public void Float(string key, double min, double max, ref float target)
            {
                double value;
                if (!TryNumber(key, min, max, Num(target), out value)) return;
                target = (float)value;
            }

            public void Int(string key, int min, int max, ref int target)
            {
                int value;
                if (!TryWhole(key, min, max, target.ToString(CultureInfo.InvariantCulture), out value)) return;
                target = value;
            }

            public void FloatArray(string key, int length, double min, double max, ref float[] target, bool requirePositiveSum = false)
            {
                float[] parsed = ReadFloats(key, length, min, max, "the default " + ListText(target) + " is used", requirePositiveSum);
                if (parsed != null) target = parsed;
            }

            public void IntArray(string key, int length, int min, int max, ref int[] target)
            {
                int[] parsed = ReadInts(key, length, min, max, "the default " + ListText(target) + " is used");
                if (parsed != null) target = parsed;
            }

            // ---- members of a record: required ones add an error when missing ----

            public void RequiredString(string key, out string value)
            {
                value = null;
                JsonNode node = Take(key);
                if (node == null)
                {
                    Error(key, "is required");
                    return;
                }
                var text = node as JsonValue;
                if (text == null || text.Kind != JsonKind.String || string.IsNullOrEmpty(text.StringValue))
                {
                    Error(key, "must be a non-empty string, got " + KindName(node));
                    return;
                }
                value = text.StringValue;
            }

            public void RequiredInt(string key, int min, int max, out int value)
            {
                value = 0;
                if (!Has(key))
                {
                    Error(key, "is required");
                    return;
                }
                TryWhole(key, min, max, null, out value);
            }

            public void RequiredFloat(string key, double min, double max, out float value)
            {
                value = 0f;
                if (!Has(key))
                {
                    Error(key, "is required");
                    return;
                }
                double number;
                if (TryNumber(key, min, max, null, out number)) value = (float)number;
            }

            public void OptionalFloat(string key, double min, double max, float fallback, out float value)
            {
                value = fallback;
                if (!Has(key)) return;
                double number;
                if (TryNumber(key, min, max, null, out number)) value = (float)number;
            }

            public void OptionalInt(string key, int min, int max, int fallback, out int value)
            {
                value = fallback;
                if (!Has(key)) return;
                TryWhole(key, min, max, null, out value);
            }

            public void OptionalBool(string key, bool fallback, out bool value)
            {
                value = fallback;
                JsonNode node = Take(key);
                if (node == null) return;
                var flag = node as JsonValue;
                if (flag == null || flag.Kind != JsonKind.Bool)
                {
                    Error(key, "must be true or false, got " + KindName(node));
                    return;
                }
                value = flag.BoolValue;
            }

            public void RequiredFloatArray(string key, int length, double min, double max, out float[] value)
            {
                value = new float[length];
                if (!Has(key))
                {
                    Error(key, "is required");
                    return;
                }
                float[] parsed = ReadFloats(key, length, min, max, null, false);
                if (parsed != null) value = parsed;
            }

            // ---- the readers behind them ----

            private bool Has(string key)
            {
                return _node[key] != null;
            }

            /// <summary>A finite number in [min, max]. <paramref name="defaultText"/> (may be null) is named in the message when the value is rejected.</summary>
            private bool TryNumber(string key, double min, double max, string defaultText, out double value)
            {
                value = 0;
                JsonNode node = Take(key);
                if (node == null) return false;

                string fallback = defaultText == null ? "the value is rejected" : "the default " + defaultText + " is used";
                var number = node as JsonValue;
                if (number == null || number.Kind != JsonKind.Number)
                {
                    Error(key, "must be a number, got " + KindName(node) + "; " + fallback);
                    return false;
                }
                if (double.IsNaN(number.NumberValue) || double.IsInfinity(number.NumberValue) || number.NumberValue < min || number.NumberValue > max)
                {
                    Error(key, "out of range " + Range(min, max) + ": " + Num(number.NumberValue) + "; " + fallback);
                    return false;
                }
                value = number.NumberValue;
                return true;
            }

            private bool TryWhole(string key, int min, int max, string defaultText, out int value)
            {
                value = 0;
                JsonNode node = Take(key);
                if (node == null) return false;

                string fallback = defaultText == null ? "the value is rejected" : "the default " + defaultText + " is used";
                var number = node as JsonValue;
                if (number == null || number.Kind != JsonKind.Number)
                {
                    Error(key, "must be a whole number, got " + KindName(node) + "; " + fallback);
                    return false;
                }
                int whole;
                if (!number.TryGetInt32(out whole))
                {
                    Error(key, number.NumberValue == Math.Floor(number.NumberValue) && !double.IsInfinity(number.NumberValue)
                        ? "out of range " + Range(min, max) + ": " + Num(number.NumberValue) + "; " + fallback
                        : "must be a whole number, got " + Num(number.NumberValue) + "; " + fallback);
                    return false;
                }
                if (whole < min || whole > max)
                {
                    Error(key, "out of range " + Range(min, max) + ": " + whole.ToString(CultureInfo.InvariantCulture) + "; " + fallback);
                    return false;
                }
                value = whole;
                return true;
            }

            private float[] ReadFloats(string key, int length, double min, double max, string fallback, bool requirePositiveSum)
            {
                JsonNode node = Take(key);
                if (node == null) return null;

                string tail = fallback ?? "the value is rejected";
                var array = node as JsonArray;
                if (array == null)
                {
                    Error(key, "must be an array of " + length + " numbers, got " + KindName(node) + "; " + tail);
                    return null;
                }
                if (array.Count != length)
                {
                    Error(key, "must have exactly " + length + " numbers, got " + array.Count + "; " + tail);
                    return null;
                }

                var result = new float[length];
                bool ok = true;
                double sum = 0;
                for (int i = 0; i < length; i++)
                {
                    JsonNode item;
                    array.TryGetValue(i, out item);
                    var number = item as JsonValue;
                    if (number == null || number.Kind != JsonKind.Number)
                    {
                        Error(key + "[" + i + "]", "must be a number, got " + KindName(item) + "; " + tail);
                        ok = false;
                    }
                    else if (double.IsNaN(number.NumberValue) || double.IsInfinity(number.NumberValue) || number.NumberValue < min || number.NumberValue > max)
                    {
                        Error(key + "[" + i + "]", "out of range " + Range(min, max) + ": " + Num(number.NumberValue) + "; " + tail);
                        ok = false;
                    }
                    else
                    {
                        result[i] = (float)number.NumberValue;
                        sum += number.NumberValue;
                    }
                }
                if (ok && requirePositiveSum && !(sum > 0))
                {
                    Error(key, "must not be all zero; " + tail);
                    ok = false;
                }
                return ok ? result : null;
            }

            private int[] ReadInts(string key, int length, int min, int max, string fallback)
            {
                JsonNode node = Take(key);
                if (node == null) return null;

                string tail = fallback ?? "the value is rejected";
                var array = node as JsonArray;
                if (array == null)
                {
                    Error(key, "must be an array of " + length + " whole numbers, got " + KindName(node) + "; " + tail);
                    return null;
                }
                if (array.Count != length)
                {
                    Error(key, "must have exactly " + length + " numbers, got " + array.Count + "; " + tail);
                    return null;
                }

                var result = new int[length];
                bool ok = true;
                for (int i = 0; i < length; i++)
                {
                    JsonNode item;
                    array.TryGetValue(i, out item);
                    var number = item as JsonValue;
                    int whole;
                    if (number == null || number.Kind != JsonKind.Number || !number.TryGetInt32(out whole))
                    {
                        Error(key + "[" + i + "]", "must be a whole number, got " + (number != null && number.Kind == JsonKind.Number ? Num(number.NumberValue) : KindName(item)) + "; " + tail);
                        ok = false;
                    }
                    else if (whole < min || whole > max)
                    {
                        Error(key + "[" + i + "]", "out of range " + Range(min, max) + ": " + whole.ToString(CultureInfo.InvariantCulture) + "; " + tail);
                        ok = false;
                    }
                    else
                    {
                        result[i] = whole;
                    }
                }
                return ok ? result : null;
            }
        }
    }
}
