using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The readers and checks of the content of press-reviews (FR-3, FR-10, TSK-002), pure and without the game:
    /// <list type="bullet">
    /// <item><see cref="ParseOutlet"/> and <see cref="ParseNote"/> are the record mappers of <c>ModContent.LoadRecords</c>: a broken record gives
    /// null and the reasons (the record is skipped, the others load); a record that is fine gets only warnings (a clipped value, an ignored member).</item>
    /// <item><see cref="ParseConfig"/> reads <c>review-config.json</c> over the defaults: a value that is wrong keeps its default (an error), one
    /// outside its legal range is clipped (a warning). Weights are cut at 0 and normalized, <c>g</c> is kept in 1…2, <c>tau</c> in 0…1.5, the strictness in
    /// 0.5…2. The segment rule (<c>segmentPriceBand</c>) and the reputation drift belong to the core: the members are ignored with a warning.</item>
    /// <item><see cref="FindCoverageHoles"/> lists the years and segments no outlet reviews (with the fallback outlets counted); <see cref="Validate"/>
    /// puts the checks of a whole content set into one <see cref="ValidationReport"/>.</item>
    /// </list>
    /// </summary>
    public static class PressContentValidator
    {
        /// <summary>The first year the coverage check looks at (the game starts in 1971).</summary>
        public const int FirstYear = 1971;

        /// <summary>The last year the coverage check looks at.</summary>
        public const int LastYear = 2030;

        /// <summary>The quotes every pool (axis × high/low) should have at least (a warning below this).</summary>
        public const int MinQuotesPerPool = 3;

        private const int MinYear = 1800;
        private const int MaxYear = 2200;

        private static readonly string[] SegmentNames = { "HighEnd", "MidRange", "LowEnd", "Industries" };
        private static readonly string[] AxisNames = { "performance", "value", "efficiency", "thermal" };

        private static readonly string[] OutletMembers =
        {
            "id", "name", "years", "segments", "weightShift", "bias", "noiseSigma", "fallback", "disabled",
        };

        private static readonly string[] NoteMembers =
        {
            "id", "condition", "textKey", "args", "cooldownQuarters", "weight", "disabled",
        };

        private static readonly string[] ConfigMembers =
        {
            "schema", "delayDays", "peers", "trend", "curve", "strictness", "thermal", "weights", "cap", "noAnalogPerformance", "verdicts",
            "effects", "awards", "reputationLevels", "digest", "archive", "adjusterLimits", "flawReveal", "quotes",
        };

        // ---- names ----

        /// <summary>The segment from its name in the content ("HighEnd", "MidRange", "LowEnd", "Industries"; the case does not matter); false for another name.</summary>
        public static bool TryParseSegment(string name, out Segment segment)
        {
            segment = Segment.Any;
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < SegmentNames.Length; i++)
            {
                if (string.Equals(SegmentNames[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    segment = (Segment)i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The name of a segment in the content ("HighEnd"…); "Any" for <see cref="Segment.Any"/>.</summary>
        public static string SegmentName(Segment segment)
        {
            int index = (int)segment;
            return index >= 0 && index < SegmentNames.Length ? SegmentNames[index] : "Any";
        }

        // ---- outlets ----

        /// <summary>The mapper of a record of <c>outlets.json</c> (design.md "Контент: outlets.json"). Null with the reasons in <paramref name="messages"/> for a broken record.</summary>
        public static Outlet ParseOutlet(JsonObject json, List<string> messages)
        {
            var report = new ValidationReport();
            Outlet outlet = ReadOutlet(json, report);
            return Finish(outlet, report, messages);
        }

        private static Outlet ReadOutlet(JsonObject json, ValidationReport report)
        {
            if (json == null)
            {
                report.Errors.Add("the record is not an object");
                return null;
            }

            var outlet = new Outlet();
            string id = ContentJson.Str(json, "id", null, null, report);
            if (string.IsNullOrEmpty(id)) report.Errors.Add("id: is required");
            else if (!ContentMerger.IsSafeName(id)) report.Errors.Add("id: '" + id + "' is not a plain name (letters, digits, '-', '_')");
            else outlet.Id = id;

            JsonNode nameNode = ContentJson.Get(json, "name");
            if (nameNode == null)
            {
                report.Errors.Add("name: is required");
            }
            else
            {
                var problems = new List<string>();
                outlet.Name = LocText.FromJson(nameNode, "name", problems);
                report.Errors.AddRange(problems);
            }

            ReadYears(json, outlet, report);
            ReadSegments(json, outlet, report);

            JsonObject shift = ContentJson.Obj(json, "weightShift", null, report);
            if (shift != null)
            {
                for (int i = 0; i < AxisNames.Length; i++) outlet.WeightShift[i] = ContentJson.Float(shift, AxisNames[i], "weightShift", 0f, -1f, 1f, report);
                ContentJson.WarnUnknown(shift, "weightShift", report, AxisNames);
            }

            outlet.Bias = ContentJson.Float(json, "bias", null, 0f, -30f, 30f, report);
            outlet.NoiseSigma = ContentJson.Float(json, "noiseSigma", null, 3f, 0f, 15f, report);
            ReadFallback(json, outlet, report);

            ContentJson.WarnUnknown(json, null, report, OutletMembers);
            return outlet;
        }

        private static void ReadYears(JsonObject json, Outlet outlet, ValidationReport report)
        {
            JsonArray years = ContentJson.Arr(json, "years", null, report);
            if (years == null)
            {
                if (ContentJson.Get(json, "years") == null) report.Errors.Add("years: is required");
                return;
            }
            if (years.Count != 2)
            {
                report.Errors.Add("years: expected [first year, last year], got " + years.Count + " element(s)");
                return;
            }

            int from = ContentJson.IntOf(years[0], "years[0]", 0, MinYear, MaxYear, report);
            int to = ContentJson.IntOf(years[1], "years[1]", 0, MinYear, MaxYear, report);
            if (from > to)
            {
                report.Errors.Add("years: the first year " + from + " is after the last year " + to);
                return;
            }
            outlet.YearFrom = from;
            outlet.YearTo = to;
        }

        private static void ReadSegments(JsonObject json, Outlet outlet, ValidationReport report)
        {
            JsonArray segments = ContentJson.Arr(json, "segments", null, report);
            if (segments == null)
            {
                if (ContentJson.Get(json, "segments") == null) report.Errors.Add("segments: is required");
                return;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var scalar = segments[i] as JsonValue;
                Segment segment;
                if (scalar == null || scalar.Kind != JsonKind.String)
                {
                    report.Errors.Add("segments[" + i + "]: expected a segment name, got " + ContentJson.KindName(segments[i]));
                }
                else if (!TryParseSegment(scalar.StringValue, out segment))
                {
                    report.Errors.Add("segments[" + i + "]: unknown segment '" + scalar.StringValue + "' (HighEnd, MidRange, LowEnd, Industries)");
                }
                else if (!outlet.Segments.Contains(segment))
                {
                    outlet.Segments.Add(segment);
                }
            }
            if (outlet.Segments.Count == 0 && report.Errors.Count == 0) report.Errors.Add("segments: at least one segment is required");
        }

        private static void ReadFallback(JsonObject json, Outlet outlet, ValidationReport report)
        {
            JsonNode node = ContentJson.Get(json, "fallback");
            if (node == null) return;

            var fallback = node as JsonObject;
            if (fallback == null)
            {
                report.Errors.Add("fallback: expected an object { \"from\", \"to\" } or null, got " + ContentJson.KindName(node));
                return;
            }
            if (ContentJson.Get(fallback, "from") == null || ContentJson.Get(fallback, "to") == null)
            {
                report.Errors.Add("fallback: needs both \"from\" and \"to\"");
                return;
            }

            int from = ContentJson.Int(fallback, "from", "fallback", 0, MinYear, MaxYear, report);
            int to = ContentJson.Int(fallback, "to", "fallback", 0, MinYear, MaxYear, report);
            if (from > to)
            {
                report.Errors.Add("fallback: \"from\" " + from + " is after \"to\" " + to);
                return;
            }
            ContentJson.WarnUnknown(fallback, "fallback", report, "from", "to");
            outlet.HasFallback = true;
            outlet.FallbackFrom = from;
            outlet.FallbackTo = to;
        }

        /// <summary>
        /// An outlet that reviews every segment in every year: what the feature uses when <c>outlets.json</c> yields nothing
        /// (design.md "Error Handling": a broken file gives a stub outlet and an error in the log).
        /// </summary>
        public static Outlet StubOutlet()
        {
            var outlet = new Outlet
            {
                Id = "press_stub",
                Name = LocText.Of("Пресса", "The Press"),
                YearFrom = MinYear,
                YearTo = MaxYear,
                NoiseSigma = 3f,
                HasFallback = true,
                FallbackFrom = MinYear,
                FallbackTo = MaxYear,
            };
            for (int i = 0; i < SegmentNames.Length; i++) outlet.Segments.Add((Segment)i);
            return outlet;
        }

        // ---- notes ----

        /// <summary>The mapper of a record of <c>notes.json</c>. A note with an unknown condition type is dropped (null with the reason).</summary>
        public static NoteTemplate ParseNote(JsonObject json, List<string> messages)
        {
            var report = new ValidationReport();
            NoteTemplate note = ReadNote(json, report);
            return Finish(note, report, messages);
        }

        private static NoteTemplate ReadNote(JsonObject json, ValidationReport report)
        {
            if (json == null)
            {
                report.Errors.Add("the record is not an object");
                return null;
            }

            var note = new NoteTemplate();
            string id = ContentJson.Str(json, "id", null, null, report);
            if (string.IsNullOrEmpty(id)) report.Errors.Add("id: is required");
            else if (!ContentMerger.IsSafeName(id)) report.Errors.Add("id: '" + id + "' is not a plain name (letters, digits, '-', '_')");
            else note.Id = id;

            string textKey = ContentJson.Str(json, "textKey", null, null, report);
            if (string.IsNullOrEmpty(textKey)) report.Errors.Add("textKey: is required");
            else note.TextKey = textKey;

            ReadCondition(json, note, report);
            ReadArgs(json, note, report);

            note.CooldownQuarters = ContentJson.Int(json, "cooldownQuarters", null, 4, 0, 400, report);
            note.Weight = ContentJson.Float(json, "weight", null, 1f, 0f, 100f, report);

            ContentJson.WarnUnknown(json, null, report, NoteMembers);
            return note;
        }

        private static void ReadCondition(JsonObject json, NoteTemplate note, ValidationReport report)
        {
            if (ContentJson.Get(json, "condition") == null)
            {
                report.Errors.Add("condition: is required");
                return;
            }
            JsonObject condition = ContentJson.Obj(json, "condition", null, report);
            if (condition == null) return;

            string typeName = ContentJson.Str(condition, "type", "condition", null, report);
            NoteConditionType type;
            if (string.IsNullOrEmpty(typeName))
            {
                report.Errors.Add("condition.type: is required");
                return;
            }
            if (!NoteCondition.TryParseType(typeName, out type))
            {
                report.Errors.Add("condition.type: unknown type '" + typeName + "', the note is dropped");
                return;
            }
            note.Condition.Type = type;

            if (NoteCondition.NeedsSegment(type))
            {
                string segmentName = ContentJson.Str(condition, "segment", "condition", null, report);
                Segment segment;
                if (string.IsNullOrEmpty(segmentName)) report.Errors.Add("condition.segment: is required for " + typeName);
                else if (!TryParseSegment(segmentName, out segment)) report.Errors.Add("condition.segment: unknown segment '" + segmentName + "'");
                else note.Condition.Segment = segment;
            }

            bool hasMin = ContentJson.Get(condition, "min") != null;
            bool hasMax = ContentJson.Get(condition, "max") != null;
            if (hasMin) note.Condition.Min = ContentJson.Float(condition, "min", "condition", 0f, -1000f, 1000f, report);
            if (hasMax) note.Condition.Max = ContentJson.Float(condition, "max", "condition", 0f, -1000f, 1000f, report);
            if (NoteCondition.NeedsBound(type) && !hasMin && !hasMax) report.Errors.Add("condition: " + typeName + " needs \"min\" or \"max\"");
            if (note.Condition.Min.HasValue && note.Condition.Max.HasValue && note.Condition.Min.Value > note.Condition.Max.Value)
                report.Errors.Add("condition: \"min\" is above \"max\"");

            ContentJson.WarnUnknown(condition, "condition", report, "type", "segment", "min", "max");
        }

        private static void ReadArgs(JsonObject json, NoteTemplate note, ValidationReport report)
        {
            JsonArray args = ContentJson.Arr(json, "args", null, report);
            if (args == null) return;

            for (int i = 0; i < args.Count; i++)
            {
                var scalar = args[i] as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.String)
                {
                    report.Errors.Add("args[" + i + "]: expected a name, got " + ContentJson.KindName(args[i]));
                }
                else if (Array.IndexOf(NoteTemplate.KnownArgs, scalar.StringValue) < 0)
                {
                    report.Errors.Add("args[" + i + "]: unknown value name '" + scalar.StringValue + "' (" + string.Join(", ", NoteTemplate.KnownArgs) + ")");
                }
                else
                {
                    note.Args.Add(scalar.StringValue);
                }
            }
        }

        // ---- config ----

        /// <summary>
        /// Reads <c>review-config.json</c> over the defaults of <see cref="ReviewConfig.CreateDefault"/>; never null. Wrong values are errors
        /// (the default stays), clipped and ignored values are warnings, both in <paramref name="report"/> (may be null).
        /// </summary>
        public static ReviewConfig ParseConfig(JsonObject json, ValidationReport report)
        {
            ValidationReport r = report ?? new ValidationReport();
            ReviewConfig cfg = ReviewConfig.CreateDefault();
            if (json == null || json.Count == 0)
            {
                r.Errors.Add("review-config: the file is empty or missing, the built-in defaults are used");
                return cfg;
            }

            if (json.ContainsKey("segmentPriceBand"))
                r.Warnings.Add("segmentPriceBand: ignored, the rule that puts a CPU into a segment belongs to the core (segments.priceBand in core.json)");
            if (json.ContainsKey("drift"))
                r.Warnings.Add("drift: ignored, the monthly drift of the reputation belongs to the core (reputation.driftMonthlyRate in core.json)");
            foreach (KeyValuePair<string, JsonNode> pair in json)
            {
                if (Array.IndexOf(ConfigMembers, pair.Key) < 0 && pair.Key != "segmentPriceBand" && pair.Key != "drift")
                    r.Warnings.Add(pair.Key + ": unknown member is ignored");
            }

            ReadDelay(json, cfg, r);
            ReadPeers(json, cfg, r);
            ReadTrend(json, cfg, r);
            ReadCurve(json, cfg, r);
            ReadStrictness(json, cfg, r);
            ReadThermal(json, cfg, r);
            ReadWeights(json, cfg, r);
            ReadCap(json, cfg, r);
            cfg.NoAnalogPerformance = ContentJson.Float(json, "noAnalogPerformance", null, cfg.NoAnalogPerformance, 0f, 100f, r);
            ReadVerdicts(json, cfg, r);
            ReadEffects(json, cfg, r);
            ReadAwards(json, cfg, r);
            ReadLevels(json, cfg, r);
            ReadDigest(json, cfg, r);
            ReadArchive(json, cfg, r);
            ReadAdjusterLimits(json, cfg, r);

            JsonObject flaw = ContentJson.Obj(json, "flawReveal", null, r);
            if (flaw != null) cfg.FlawReveal.Chance = ContentJson.Float(flaw, "chance", "flawReveal", cfg.FlawReveal.Chance, 0f, 1f, r);

            ReadQuotes(json, cfg, r);
            return cfg;
        }

        private static void ReadDelay(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonArray delay = ContentJson.Arr(json, "delayDays", null, r);
            if (delay == null) return;
            if (delay.Count != 2)
            {
                r.Errors.Add("delayDays: expected [min, max], got " + delay.Count + " element(s)");
                return;
            }

            int min = ContentJson.IntOf(delay[0], "delayDays[0]", cfg.DelayMinDays, 0, 365, r);
            int max = ContentJson.IntOf(delay[1], "delayDays[1]", cfg.DelayMaxDays, 0, 365, r);
            if (min > max)
            {
                r.Errors.Add("delayDays: the minimum " + min + " is above the maximum " + max);
                return;
            }
            cfg.DelayMinDays = min;
            cfg.DelayMaxDays = max;
        }

        private static void ReadPeers(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject peers = ContentJson.Obj(json, "peers", null, r);
            if (peers == null) return;

            cfg.Peers.Count = ContentJson.Int(peers, "count", "peers", cfg.Peers.Count, 1, 10, r);
            cfg.Peers.PeriodMonths = ContentJson.Int(peers, "periodMonths", "peers", cfg.Peers.PeriodMonths, 1, 12, r);
            cfg.Peers.Candidates = ContentJson.Int(peers, "candidates", "peers", cfg.Peers.Candidates, 1, 100, r);
            if (cfg.Peers.Candidates < cfg.Peers.Count)
            {
                r.Warnings.Add("peers.candidates: " + cfg.Peers.Candidates + " is below peers.count " + cfg.Peers.Count + ", raised");
                cfg.Peers.Candidates = cfg.Peers.Count;
            }
        }

        private static void ReadTrend(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject trend = ContentJson.Obj(json, "trend", null, r);
            if (trend == null) return;

            cfg.Trend.Tau = ContentJson.Float(trend, "tau", "trend", cfg.Trend.Tau, 0f, 1.5f, r);

            JsonArray eras = ContentJson.Arr(trend, "eras", "trend", r);
            if (eras == null) return;

            var list = new List<EraRule>();
            for (int i = 0; i < eras.Count; i++)
            {
                string path = "trend.eras[" + i + "]";
                var era = eras[i] as JsonObject;
                if (era == null)
                {
                    r.Errors.Add(path + ": expected an object { \"from\", \"g\" }, got " + ContentJson.KindName(eras[i]));
                    continue;
                }
                if (ContentJson.Get(era, "from") == null || ContentJson.Get(era, "g") == null)
                {
                    r.Errors.Add(path + ": needs both \"from\" and \"g\"");
                    continue;
                }
                var rule = new EraRule
                {
                    From = ContentJson.Int(era, "from", path, 0, MinYear, MaxYear, r),
                    G = ContentJson.Float(era, "g", path, 1f, 1f, 2f, r),
                };
                list.Add(rule);
            }

            if (list.Count == 0)
            {
                r.Errors.Add("trend.eras: no usable era, the defaults are used");
                return;
            }
            list.Sort((a, b) => a.From.CompareTo(b.From));
            cfg.Trend.Eras = list;
        }

        private static void ReadCurve(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject curve = ContentJson.Obj(json, "curve", null, r);
            if (curve == null) return;

            cfg.Curve.Parity = ContentJson.Float(curve, "parity", "curve", cfg.Curve.Parity, 0f, 100f, r);
            cfg.Curve.Span = ContentJson.Float(curve, "span", "curve", cfg.Curve.Span, 1f, 100f, r);
            cfg.Curve.RatioAtTop = ContentJson.Float(curve, "ratioAtTop", "curve", cfg.Curve.RatioAtTop, 1.05f, 5f, r);
        }

        private static void ReadStrictness(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonArray array = ContentJson.Arr(json, "strictness", null, r);
            if (array == null) return;

            var values = new List<float>();
            for (int i = 0; i < array.Count; i++)
            {
                double v;
                if (!ContentJson.TryNumber(array[i], out v))
                {
                    r.Errors.Add("strictness[" + i + "]: expected a number, got " + ContentJson.KindName(array[i]) + ", the defaults are used");
                    return;
                }
                values.Add(ContentJson.ClipNumber(v, "strictness[" + i + "]", 0.5f, 2f, r));
            }
            if (values.Count == 0)
            {
                r.Errors.Add("strictness: at least one value is required, the defaults are used");
                return;
            }
            cfg.Strictness = values.ToArray();
        }

        private static void ReadThermal(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject thermal = ContentJson.Obj(json, "thermal", null, r);
            if (thermal == null) return;

            cfg.Thermal.SafeCap = ContentJson.Float(thermal, "safeCap", "thermal", cfg.Thermal.SafeCap, 40f, 200f, r);
            cfg.Thermal.FullMarksBelowSafe = ContentJson.Float(thermal, "fullMarksBelowSafe", "thermal", cfg.Thermal.FullMarksBelowSafe, 1f, 100f, r);
            cfg.Thermal.ZeroAboveSafe = ContentJson.Float(thermal, "zeroAboveSafe", "thermal", cfg.Thermal.ZeroAboveSafe, 0f, 50f, r);
        }

        private static void ReadWeights(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject weights = ContentJson.Obj(json, "weights", null, r);
            if (weights == null) return;

            for (int s = 0; s < SegmentNames.Length; s++)
            {
                string path = "weights." + SegmentNames[s];
                JsonObject row = ContentJson.Obj(weights, SegmentNames[s], "weights", r);
                if (row == null) continue;

                var raw = new float[ReviewMath.AxisCount];
                for (int a = 0; a < raw.Length; a++) raw[a] = ContentJson.Float(row, AxisNames[a], path, cfg.Weights[s][a], 0f, 100f, r);
                ContentJson.WarnUnknown(row, path, r, AxisNames);

                float[] normalized = ReviewMath.NormalizeWeights(raw);
                if (normalized == null)
                {
                    r.Errors.Add(path + ": the weights add up to 0, the defaults are used");
                    continue;
                }
                double sum = 0.0;
                for (int a = 0; a < raw.Length; a++) sum += raw[a];
                if (Math.Abs(sum - 1.0) > 1e-4) r.Warnings.Add(path + ": the weights add up to " + ContentJson.Text(sum) + ", normalized to 1");
                cfg.Weights[s] = normalized;
            }

            foreach (KeyValuePair<string, JsonNode> pair in weights)
            {
                Segment ignored;
                if (!TryParseSegment(pair.Key, out ignored)) r.Warnings.Add("weights." + pair.Key + ": unknown segment is ignored");
            }
        }

        private static void ReadCap(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject cap = ContentJson.Obj(json, "cap", null, r);
            if (cap == null) return;

            cfg.Cap.Gap = ContentJson.Float(cap, "gap", "cap", cfg.Cap.Gap, 0f, 100f, r);
            cfg.Cap.MinWeight = ContentJson.Float(cap, "minWeight", "cap", cfg.Cap.MinWeight, 0f, 1f, r);
        }

        private static void ReadVerdicts(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            var rules = ReadKeyedLevels(json, "verdicts", r);
            if (rules == null) return;

            var list = new List<VerdictRule>();
            foreach (KeyValuePair<int, string> pair in rules) list.Add(new VerdictRule { Min = pair.Key, Key = pair.Value });
            cfg.Verdicts = list;
        }

        private static void ReadLevels(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            var rules = ReadKeyedLevels(json, "reputationLevels", r);
            if (rules == null) return;

            var list = new List<ReputationLevel>();
            foreach (KeyValuePair<int, string> pair in rules) list.Add(new ReputationLevel { Min = pair.Key, Key = pair.Value });
            cfg.ReputationLevels = list;
        }

        /// <summary>The (min, key) pairs of a list such as <c>verdicts</c>, the highest minimum first; null when the member is absent or yields nothing usable.</summary>
        private static List<KeyValuePair<int, string>> ReadKeyedLevels(JsonObject json, string name, ValidationReport r)
        {
            JsonArray array = ContentJson.Arr(json, name, null, r);
            if (array == null) return null;

            var list = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < array.Count; i++)
            {
                string path = name + "[" + i + "]";
                var item = array[i] as JsonObject;
                if (item == null)
                {
                    r.Errors.Add(path + ": expected an object { \"min\", \"key\" }, got " + ContentJson.KindName(array[i]));
                    continue;
                }
                string key = ContentJson.Str(item, "key", path, null, r);
                if (string.IsNullOrEmpty(key) || ContentJson.Get(item, "min") == null)
                {
                    r.Errors.Add(path + ": needs both \"min\" and \"key\"");
                    continue;
                }
                list.Add(new KeyValuePair<int, string>(ContentJson.Int(item, "min", path, 0, 0, 100, r), key));
            }

            if (list.Count == 0)
            {
                r.Errors.Add(name + ": no usable entry, the defaults are used");
                return null;
            }
            list.Sort((a, b) => b.Key.CompareTo(a.Key));
            return list;
        }

        private static void ReadEffects(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject effects = ContentJson.Obj(json, "effects", null, r);
            if (effects == null) return;

            JsonObject hype = ContentJson.Obj(effects, "hype", "effects", r);
            if (hype != null)
            {
                HypeConfig h = cfg.Hype;
                h.Parity = ContentJson.Float(hype, "parity", "effects.hype", h.Parity, 0f, 100f, r);
                h.Strength = ContentJson.Float(hype, "strength", "effects.hype", h.Strength, 0f, 1f, r);
                h.Min = ContentJson.Float(hype, "min", "effects.hype", h.Min, 0.1f, 1f, r);
                h.Max = ContentJson.Float(hype, "max", "effects.hype", h.Max, 1f, 3f, r);
                h.DurationDays = ContentJson.Int(hype, "durationDays", "effects.hype", h.DurationDays, 1, 3650, r);
            }

            JsonObject delta = ContentJson.Obj(effects, "reputationPerReview", "effects", r);
            if (delta != null)
            {
                RepDeltaConfig d = cfg.ReputationPerReview;
                d.Parity = ContentJson.Float(delta, "parity", "effects.reputationPerReview", d.Parity, 0f, 100f, r);
                d.Divisor = ContentJson.Float(delta, "divisor", "effects.reputationPerReview", d.Divisor, 0.5f, 100f, r);
                d.Min = ContentJson.Int(delta, "min", "effects.reputationPerReview", d.Min, -50, 0, r);
                d.Max = ContentJson.Int(delta, "max", "effects.reputationPerReview", d.Max, 0, 50, r);
            }

            JsonObject effect = ContentJson.Obj(effects, "reputationEffect", "effects", r);
            if (effect != null)
            {
                ReputationEffectConfig e = cfg.ReputationEffect;
                e.Strength = ContentJson.Float(effect, "strength", "effects.reputationEffect", e.Strength, 0f, 1f, r);
                e.Neutral = ContentJson.Float(effect, "neutral", "effects.reputationEffect", e.Neutral, 0f, 100f, r);
                e.AffectsAI = ContentJson.Bool(effect, "affectsAI", "effects.reputationEffect", e.AffectsAI, r);
            }

            ContentJson.WarnUnknown(effects, "effects", r, "hype", "reputationPerReview", "reputationEffect");
        }

        private static void ReadAwards(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject awards = ContentJson.Obj(json, "awards", null, r);
            if (awards == null) return;

            AwardsConfig a = cfg.Awards;
            a.CpuOfYearMin = ContentJson.Int(awards, "cpuOfYearMin", "awards", a.CpuOfYearMin, 0, 100, r);
            a.CpuOfYearRep = ContentJson.Int(awards, "cpuOfYearRep", "awards", a.CpuOfYearRep, -20, 20, r);
            a.CompanyOfYearMinReviews = ContentJson.Int(awards, "companyOfYearMinReviews", "awards", a.CompanyOfYearMinReviews, 1, 100, r);
            a.CompanyOfYearRep = ContentJson.Int(awards, "companyOfYearRep", "awards", a.CompanyOfYearRep, -20, 20, r);
            a.DisappointmentMax = ContentJson.Int(awards, "disappointmentMax", "awards", a.DisappointmentMax, 0, 100, r);
            a.DisappointmentRep = ContentJson.Int(awards, "disappointmentRep", "awards", a.DisappointmentRep, -20, 20, r);
        }

        private static void ReadDigest(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject digest = ContentJson.Obj(json, "digest", null, r);
            if (digest == null) return;

            DigestConfig d = cfg.Digest;
            d.MaxLines = ContentJson.Int(digest, "maxLines", "digest", d.MaxLines, 1, 50, r);
            d.NotableHigh = ContentJson.Int(digest, "notableHigh", "digest", d.NotableHigh, 0, 100, r);
            d.NotableLow = ContentJson.Int(digest, "notableLow", "digest", d.NotableLow, 0, 100, r);
            d.Parity = ContentJson.Int(digest, "parity", "digest", d.Parity, 0, 100, r);
        }

        private static void ReadArchive(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject archive = ContentJson.Obj(json, "archive", null, r);
            if (archive == null) return;

            cfg.Archive.MaxFull = ContentJson.Int(archive, "maxFull", "archive", cfg.Archive.MaxFull, 100, 100000, r);
            cfg.Archive.MaxTotal = ContentJson.Int(archive, "maxTotal", "archive", cfg.Archive.MaxTotal, 100, 1000000, r);
            if (cfg.Archive.MaxTotal < cfg.Archive.MaxFull)
            {
                r.Warnings.Add("archive.maxTotal: " + cfg.Archive.MaxTotal + " is below archive.maxFull " + cfg.Archive.MaxFull + ", raised");
                cfg.Archive.MaxTotal = cfg.Archive.MaxFull;
            }
        }

        private static void ReadAdjusterLimits(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject limits = ContentJson.Obj(json, "adjusterLimits", null, r);
            if (limits == null) return;

            cfg.AdjusterLimits.Axis = ContentJson.Float(limits, "axis", "adjusterLimits", cfg.AdjusterLimits.Axis, 0f, 100f, r);
            cfg.AdjusterLimits.Overall = ContentJson.Float(limits, "overall", "adjusterLimits", cfg.AdjusterLimits.Overall, 0f, 100f, r);
        }

        private static void ReadQuotes(JsonObject json, ReviewConfig cfg, ValidationReport r)
        {
            JsonObject quotes = ContentJson.Obj(json, "quotes", null, r);
            if (quotes == null) return;

            for (int a = 0; a < AxisNames.Length; a++)
            {
                JsonObject axis = ContentJson.Obj(quotes, AxisNames[a], "quotes", r);
                if (axis == null) continue;

                cfg.QuotePools[a * 2] = ReadQuoteList(axis, "high", "quotes." + AxisNames[a], r);
                cfg.QuotePools[a * 2 + 1] = ReadQuoteList(axis, "low", "quotes." + AxisNames[a], r);
                ContentJson.WarnUnknown(axis, "quotes." + AxisNames[a], r, "high", "low");
            }
            ContentJson.WarnUnknown(quotes, "quotes", r, AxisNames);
        }

        private static List<string> ReadQuoteList(JsonObject axis, string name, string path, ValidationReport r)
        {
            var list = new List<string>();
            JsonArray array = ContentJson.Arr(axis, name, path, r);
            if (array == null) return list;

            for (int i = 0; i < array.Count; i++)
            {
                var scalar = array[i] as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.String || string.IsNullOrEmpty(scalar.StringValue))
                {
                    r.Errors.Add(path + "." + name + "[" + i + "]: expected a string key");
                }
                else if (!list.Contains(scalar.StringValue))
                {
                    list.Add(scalar.StringValue);
                }
            }
            return list;
        }

        // ---- the whole set ----

        /// <summary>
        /// The years from <paramref name="firstYear"/> to <paramref name="lastYear"/> and the segments that no outlet reviews, outlets that stand in
        /// through their fallback counted, as "1995 LowEnd" in year order and segment order. Empty when the coverage is complete.
        /// </summary>
        public static List<string> FindCoverageHoles(IReadOnlyList<Outlet> outlets, int firstYear, int lastYear)
        {
            var holes = new List<string>();
            for (int year = firstYear; year <= lastYear; year++)
            {
                for (int s = 0; s < SegmentNames.Length; s++)
                {
                    if (OutletPanel.ActiveFor(year, (Segment)s, outlets).Count == 0) holes.Add(year + " " + SegmentNames[s]);
                }
            }
            return holes;
        }

        /// <summary>The coverage check for the years the game plays (<see cref="FirstYear"/>…<see cref="LastYear"/>).</summary>
        public static List<string> FindCoverageHoles(IReadOnlyList<Outlet> outlets)
        {
            return FindCoverageHoles(outlets, FirstYear, LastYear);
        }

        /// <summary>
        /// The checks of a loaded content set: at least one outlet, unique ids, every segment covered in every year 1971–2030, the verdicts and
        /// levels present, the weights adding up to 1, enough quotes per pool, unique note ids.
        /// </summary>
        public static ValidationReport Validate(IReadOnlyList<Outlet> outlets, ReviewConfig config, IReadOnlyList<NoteTemplate> notes)
        {
            var report = new ValidationReport();

            if (outlets == null || outlets.Count == 0)
            {
                report.Errors.Add("outlets: there is no outlet");
            }
            else
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (Outlet outlet in outlets)
                {
                    if (outlet != null && !ids.Add(outlet.Id)) report.Errors.Add("outlets: the id '" + outlet.Id + "' is used twice");
                }
            }
            report.CoverageHoles.AddRange(FindCoverageHoles(outlets));

            if (config == null)
            {
                report.Errors.Add("config: there is no configuration");
            }
            else
            {
                CheckConfig(config, report);
            }

            if (notes != null)
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (NoteTemplate note in notes)
                {
                    if (note != null && !ids.Add(note.Id)) report.Errors.Add("notes: the id '" + note.Id + "' is used twice");
                }
            }
            return report;
        }

        private static void CheckConfig(ReviewConfig config, ValidationReport report)
        {
            if (config.Verdicts == null || config.Verdicts.Count == 0) report.Errors.Add("config: there is no verdict");
            if (config.ReputationLevels == null || config.ReputationLevels.Count == 0) report.Errors.Add("config: there is no reputation level");

            for (int s = 0; s < SegmentNames.Length; s++)
            {
                double sum = 0.0;
                float[] row = config.Weights != null && s < config.Weights.Length ? config.Weights[s] : null;
                if (row == null || row.Length != ReviewMath.AxisCount)
                {
                    report.Errors.Add("config: the weights of " + SegmentNames[s] + " are missing");
                    continue;
                }
                for (int a = 0; a < row.Length; a++) sum += row[a];
                if (Math.Abs(sum - 1.0) > 0.01) report.Warnings.Add("config: the weights of " + SegmentNames[s] + " add up to " + ContentJson.Text(sum));
            }

            for (int a = 0; a < AxisNames.Length; a++)
            {
                if (config.QuotesFor((Axis)a, true).Count < MinQuotesPerPool)
                    report.Warnings.Add("quotes." + AxisNames[a] + ".high: fewer than " + MinQuotesPerPool + " quotes");
                if (config.QuotesFor((Axis)a, false).Count < MinQuotesPerPool)
                    report.Warnings.Add("quotes." + AxisNames[a] + ".low: fewer than " + MinQuotesPerPool + " quotes");
            }
        }

        // ---- internals ----

        private static T Finish<T>(T record, ValidationReport report, List<string> messages) where T : class
        {
            bool rejected = record == null || report.Errors.Count > 0;
            if (messages != null)
            {
                messages.AddRange(report.Errors);
                if (!rejected) messages.AddRange(report.Warnings);
            }
            return rejected ? null : record;
        }
    }
}
