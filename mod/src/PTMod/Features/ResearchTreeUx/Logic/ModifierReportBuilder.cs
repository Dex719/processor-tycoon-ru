using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>One line of the modifiers report: the finished text (rich text of TextMeshPro) and how deep it sits (0 a line of its own, 1 a source under a line).</summary>
    public sealed class ReportLine
    {
        public string Text = string.Empty;
        public int Indent;
    }

    /// <summary>A titled block of the report: "Research speed", "Research cost", "Accumulated project bonuses", "Projects in progress", "Other modifiers", or a block of another feature.</summary>
    public sealed class ReportGroup
    {
        public string Title = string.Empty;
        public List<ReportLine> Lines = new List<ReportLine>();
    }

    /// <summary>
    /// A line another feature adds to the report (a pure copy of <c>ModifierLine</c> with the label already translated): the text, an optional
    /// factor and whether the value was limited. The adapter turns <c>ModifierLineGroup</c> into <see cref="ExtraModifierGroup"/>.
    /// </summary>
    public sealed class ExtraModifierLine
    {
        public string Label = string.Empty;
        public float? Multiplier;
        public bool Capped;
        public string SourceFeatureId;
    }

    /// <summary>A block of another feature for the report: the heading (already translated) and the lines.</summary>
    public sealed class ExtraModifierGroup
    {
        public string Title = string.Empty;
        public List<ExtraModifierLine> Lines = new List<ExtraModifierLine>();
    }

    /// <summary>
    /// The panel "All modifiers" and its tooltip as data (FR-5): the blocks and the lines, finished texts. The panel writes each line into a
    /// text widget, the tooltip joins them (<see cref="ToTooltip"/>), so the two show the same.
    /// </summary>
    public sealed class ModifierReport
    {
        public List<ReportGroup> Groups = new List<ReportGroup>();

        /// <summary>The lines of all blocks, no headings, top to bottom (a convenience for tests and the log).</summary>
        public IEnumerable<string> AllTexts()
        {
            foreach (ReportGroup group in Groups)
            {
                foreach (ReportLine line in group.Lines) yield return line.Text;
            }
        }

        /// <summary>
        /// The tooltip of the button: the header "All modifiers" and the blocks, a bold heading over its lines, a source line set in by two
        /// spaces. A tooltip of an empty report has the header only.
        /// </summary>
        public TipText ToTooltip(TipFormat format)
        {
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");

            var text = new StringBuilder();
            foreach (ReportGroup group in Groups)
            {
                if (text.Length > 0) text.Append('\n');
                if (!string.IsNullOrEmpty(group.Title)) text.Append("<b>").Append(group.Title).Append("</b>");

                foreach (ReportLine line in group.Lines)
                {
                    if (text.Length > 0) text.Append('\n');
                    if (line.Indent > 0) text.Append("  ");
                    text.Append(line.Text);
                }
            }
            return new TipText(format.L("rtux.mod.title", new object[0]), text.ToString());
        }
    }

    /// <summary>
    /// Builds the report of the panel "All modifiers" (FR-5) from a <see cref="ModifierSnapshot"/>, the lines other features add and the words
    /// of the language; no game, no engine, so the cases of the spec are tests. The blocks, top to bottom:
    /// <list type="bullet">
    /// <item><b>Research speed</b> (items 2 and 7): the formula "Speed ×1.29 = √1.5 (×1.22) × IDM bonus (×1.05) × mods (×1.00)" and, under it,
    /// one line per multiplier the mods put on the channel (feature, factor, reason, days left). "(capped)" follows the line when the balance
    /// range or the fuse cut the value. When <c>Explain</c> was not available the mods' share is worked out from the totals.</item>
    /// <item><b>Research cost</b> (item 3): the factor of type 4 and what the technology in the lab costs per day and per month
    /// (<c>ResearchSector.Team.GetTotalCost</c>: <c>Cost × factor / 30 × funding</c> a day).</item>
    /// <item><b>Accumulated project bonuses</b> (item 4): the maximum frequency, the consumption divisor and the transistor density from
    /// <c>Company.ResearchModifiers</c>, with what each does to a processor.</item>
    /// <item><b>Projects in progress</b> (item 5): what each running project adds and when, only when there are some.</item>
    /// <item><b>Other modifiers</b> (item 6): types 1, 2, 3, 5 and 6 that are not 1, each with the reason of the game, and the sources of the mods.</item>
    /// <item>The blocks of other features (item 8), as they came.</item>
    /// </list>
    /// </summary>
    public static class ModifierReportBuilder
    {
        /// <summary>The types of the block "Other modifiers", in the order of the spec (item 6).</summary>
        public static readonly int[] OtherTypes =
        {
            VanillaModifierTable.MaintenanceCost, VanillaModifierTable.ConstructionCost, VanillaModifierTable.DevelopmentCost,
            VanillaModifierTable.Taxes, VanillaModifierTable.Interest,
        };

        // The key of the name of each type; the research speed has the formula line instead.
        private static readonly string[] TypeKeys =
        {
            "rtux.mod.group.speed", "rtux.mod.type.maintenance", "rtux.mod.type.construction", "rtux.mod.type.development",
            "rtux.mod.type.researchCost", "rtux.mod.type.taxes", "rtux.mod.type.interest",
        };

        // A factor within this of 1 is "no change" for the lines that show only what differs (two decimals are all the player sees).
        private const float Epsilon = 0.005f;

        // A project bonus smaller than this (a divisor of 1.0004) is rounding noise, not a bonus.
        private const float MinBonus = 0.0005f;

        /// <summary>
        /// Builds the report. <paramref name="extra"/> are the blocks of other features (may be null); <paramref name="format"/> brings the words
        /// (<c>L</c> is required), the game's formatters of time, money and frequency (optional) and the test whether a key exists (optional).
        /// A null snapshot gives a report with the extra blocks only.
        /// </summary>
        public static ModifierReport Build(ModifierSnapshot snapshot, IReadOnlyList<ExtraModifierGroup> extra, TipFormat format)
        {
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");

            var report = new ModifierReport();
            if (snapshot != null)
            {
                string separator = Decimal(format);
                AddSpeed(report, snapshot, format, separator);
                AddCost(report, snapshot, format, separator);
                AddAccumulated(report, snapshot, format, separator);
                AddPending(report, snapshot, format);
                AddOther(report, snapshot, format, separator);
            }
            AddExtra(report, extra, format);
            return report;
        }

        // ---- research speed ----

        private static void AddSpeed(ModifierReport report, ModifierSnapshot s, TipFormat format, string separator)
        {
            ReportGroup group = NewGroup(report, format.L("rtux.mod.group.speed", new object[0]));

            float funding = s.Funding > 0f && !float.IsNaN(s.Funding) ? s.Funding : 0f;
            float root = (float)Math.Sqrt(funding);
            float vanilla = VanillaModifierTable.Get(VanillaModifierTable.ResearchSpeed, s.Facts);
            ExplainData explain = s.ExplainFor(VanillaModifierTable.ResearchSpeed);
            float mods = explain != null ? explain.Total : ModsFromTotal(s.SpeedTotal, root * vanilla);

            string[] reasons = VanillaModifierTable.Reasons(VanillaModifierTable.ResearchSpeed, s.Facts);
            string line;
            if (reasons.Length > 0 && vanilla != 1f)
            {
                line = format.L("rtux.mod.speed.formula", new object[]
                {
                    Factor(s.SpeedTotal, separator), Trimmed(funding, separator), Factor(root, separator),
                    ReasonNames(reasons, format), Factor(vanilla, separator), Factor(mods, separator),
                });
            }
            else
            {
                line = format.L("rtux.mod.speed.formulaPlain", new object[]
                {
                    Factor(s.SpeedTotal, separator), Trimmed(funding, separator), Factor(root, separator), Factor(mods, separator),
                });
            }
            if (explain != null && explain.IsCapped) line += " " + format.L("core.explain.capped", new object[0]);
            group.Lines.Add(new ReportLine { Text = line });

            AddSources(group, explain, format, separator);
        }

        // ---- research cost ----

        private static void AddCost(ModifierReport report, ModifierSnapshot s, TipFormat format, string separator)
        {
            ReportGroup group = NewGroup(report, format.L("rtux.mod.group.cost", new object[0]));
            int type = VanillaModifierTable.ResearchCost;

            AddTypeLine(group, s, type, format, separator);

            if (string.IsNullOrEmpty(s.CurrentTechName))
            {
                group.Lines.Add(new ReportLine { Text = format.L("rtux.mod.cost.none", new object[0]) });
                return;
            }

            // ResearchSector.Team.GetTotalCost: (Cost * factor) / 30 * funding a day; a month is thirty of those.
            double funding = s.Funding > 0f && !float.IsNaN(s.Funding) ? s.Funding : 0.0;
            double perMonth = s.CurrentTechMonthlyCost * (double)s.TotalFor(type) * funding;
            double perDay = perMonth / 30.0;
            group.Lines.Add(new ReportLine
            {
                Text = format.L("rtux.mod.cost.current", new object[] { s.CurrentTechName, Money((float)perDay, format), Money((float)perMonth, format) }),
            });
        }

        // ---- accumulated bonuses of the projects ----

        private static void AddAccumulated(ModifierReport report, ModifierSnapshot s, TipFormat format, string separator)
        {
            ReportGroup group = NewGroup(report, format.L("rtux.mod.group.projects", new object[0]));

            if (s.FrequencyIncrease > 0f)
            {
                group.Lines.Add(new ReportLine { Text = format.L("rtux.mod.proj.frequency", new object[] { FrequencyText(s.FrequencyIncrease, format) }) });
            }

            float divisor = s.ConsumptionReduction;
            if (divisor > 1f + MinBonus)
            {
                // CpuMath.Consumption raises (frequency / (node reduction * divisor)) to the power 2 (at or under the maximum frequency): the power goes with 1/divisor squared.
                double powerCut = (1.0 - 1.0 / ((double)divisor * divisor)) * 100.0;
                group.Lines.Add(new ReportLine
                {
                    Text = format.L("rtux.mod.proj.consumption", new object[] { Factor(divisor, separator), TipNumberFormat.Round(powerCut) }),
                });
            }

            float density = s.DensityIncrease;
            if (density > 1f + MinBonus)
            {
                group.Lines.Add(new ReportLine
                {
                    Text = format.L("rtux.mod.proj.density", new object[] { Factor(density, separator), TipNumberFormat.Round((density - 1.0) * 100.0) }),
                });
            }

            if (group.Lines.Count == 0) group.Lines.Add(new ReportLine { Text = format.L("rtux.mod.proj.none", new object[0]) });
        }

        // ---- projects in progress ----

        private static void AddPending(ModifierReport report, ModifierSnapshot s, TipFormat format)
        {
            if (s.Pending == null || s.Pending.Count == 0) return;

            var lines = new List<ReportLine>();
            foreach (PendingProject project in s.Pending)
            {
                if (project == null) continue;

                string when = project.IsPaused || project.EtaDays < 0
                    ? format.L("rtux.mod.pending.paused", new object[0])
                    : format.L("rtux.mod.pending.eta", new object[] { DurationText(project.EtaDays, format) });

                if (project.Frequency > 0f)
                    lines.Add(new ReportLine { Text = format.L("rtux.mod.pending.frequency", new object[] { FrequencyText(project.Frequency, format), when }) });
                if (project.Consumption > 0f)
                    lines.Add(new ReportLine { Text = format.L("rtux.mod.pending.consumption", new object[] { TipNumberFormat.Round(project.Consumption * 100.0), when }) });
                if (project.Density > 0f)
                    lines.Add(new ReportLine { Text = format.L("rtux.mod.pending.density", new object[] { TipNumberFormat.Round(project.Density * 100.0), when }) });
            }
            if (lines.Count == 0) return;

            ReportGroup group = NewGroup(report, format.L("rtux.mod.group.pending", new object[0]));
            group.Lines.AddRange(lines);
        }

        // ---- the other perks of the company ----

        private static void AddOther(ModifierReport report, ModifierSnapshot s, TipFormat format, string separator)
        {
            ReportGroup group = NewGroup(report, format.L("rtux.mod.group.other", new object[0]));

            foreach (int type in OtherTypes)
            {
                float vanilla = VanillaModifierTable.Get(type, s.Facts);
                float total = s.TotalFor(type);
                ExplainData explain = s.ExplainFor(type);
                bool hasRows = explain != null && explain.Rows != null && explain.Rows.Count > 0;

                if (Math.Abs(total - 1f) < Epsilon && vanilla == 1f && !hasRows) continue;

                AddTypeLine(group, s, type, format, separator);
            }

            if (group.Lines.Count == 0) group.Lines.Add(new ReportLine { Text = format.L("rtux.mod.other.none", new object[0]) });
        }

        // "Development ×0.67 (fabless)" and, under it, the sources of the mods (or "Mods: ×k" when Explain was not there).
        private static void AddTypeLine(ReportGroup group, ModifierSnapshot s, int type, TipFormat format, string separator)
        {
            float vanilla = VanillaModifierTable.Get(type, s.Facts);
            float total = s.TotalFor(type);
            ExplainData explain = s.ExplainFor(type);
            string label = format.L(TypeKeys[type], new object[0]);
            string[] reasons = VanillaModifierTable.Reasons(type, s.Facts);

            string line = reasons.Length > 0
                ? format.L("rtux.mod.line.reason", new object[] { label, Factor(total, separator), ReasonNames(reasons, format) })
                : format.L("rtux.mod.line", new object[] { label, Factor(total, separator) });
            if (explain != null && explain.IsCapped) line += " " + format.L("core.explain.capped", new object[0]);
            group.Lines.Add(new ReportLine { Text = line });

            if (explain != null)
            {
                AddSources(group, explain, format, separator);
                return;
            }

            // FR-5 item 7: no breakdown, so one line "Mods: ×k", with k = the total over the game's own value.
            float mods = ModsFromTotal(total, vanilla);
            if (Math.Abs(mods - 1f) >= Epsilon)
                group.Lines.Add(new ReportLine { Text = format.L("rtux.mod.mods", new object[] { Factor(mods, separator) }), Indent = 1 });
        }

        // ---- the sources of the mods ----

        private static void AddSources(ReportGroup group, ExplainData explain, TipFormat format, string separator)
        {
            if (explain == null || explain.Rows == null) return;

            foreach (ExplainRow row in explain.Rows)
            {
                if (row == null) continue;
                group.Lines.Add(new ReportLine { Text = SourceText(row, format, separator), Indent = 1 });
            }
        }

        // "event-system: ×1.25 - the reason (20 days left)"; the reason is left out when the source gave none or its key is in no table.
        private static string SourceText(ExplainRow row, TipFormat format, string separator)
        {
            string who = string.IsNullOrEmpty(row.FeatureId) ? "?" : row.FeatureId;
            string factor = Factor(row.Multiplier, separator);

            string reason = null;
            if (!string.IsNullOrEmpty(row.ReasonKey) && (format.Has == null || format.Has(row.ReasonKey)))
            {
                object[] args = row.ReasonArgs == null ? new object[0] : Array.ConvertAll(row.ReasonArgs, a => (object)a);
                reason = format.L(row.ReasonKey, args);
            }

            string text = string.IsNullOrEmpty(reason)
                ? format.L("rtux.mod.sourceNoReason", new object[] { who, factor })
                : format.L("rtux.mod.source", new object[] { who, factor, reason });

            if (row.ExpiresInDays.HasValue && row.ExpiresInDays.Value >= 0)
                text += " " + format.L("rtux.mod.expires", new object[] { DurationText(row.ExpiresInDays.Value, format) });
            return text;
        }

        // ---- the blocks of other features ----

        private static void AddExtra(ModifierReport report, IReadOnlyList<ExtraModifierGroup> extra, TipFormat format)
        {
            if (extra == null) return;

            string separator = Decimal(format);
            foreach (ExtraModifierGroup source in extra)
            {
                if (source == null || source.Lines == null || source.Lines.Count == 0) continue;

                var lines = new List<ReportLine>();
                foreach (ExtraModifierLine line in source.Lines)
                {
                    if (line == null || string.IsNullOrEmpty(line.Label)) continue;

                    string text = line.Label;
                    if (line.Multiplier.HasValue) text += " ×" + Factor(line.Multiplier.Value, separator);
                    if (line.Capped) text += " " + format.L("core.explain.capped", new object[0]);
                    lines.Add(new ReportLine { Text = text });
                }
                if (lines.Count == 0) continue;

                ReportGroup group = NewGroup(report, source.Title ?? string.Empty);
                group.Lines.AddRange(lines);
            }
        }

        // ---- small helpers ----

        private static ReportGroup NewGroup(ModifierReport report, string title)
        {
            var group = new ReportGroup { Title = title };
            report.Groups.Add(group);
            return group;
        }

        // The share of the mods when the breakdown is not available: the total over the game's own value (1 when that is zero or no number).
        private static float ModsFromTotal(float total, float vanillaPart)
        {
            if (float.IsNaN(total) || float.IsInfinity(total) || vanillaPart <= 0f || float.IsNaN(vanillaPart)) return 1f;
            return total / vanillaPart;
        }

        private static string ReasonNames(string[] reasonKeys, TipFormat format)
        {
            var names = new StringBuilder();
            foreach (string key in reasonKeys)
            {
                if (names.Length > 0) names.Append(", ");
                names.Append(format.L(key, new object[0]));
            }
            return names.ToString();
        }

        private static string FrequencyText(float frequency, TipFormat format)
        {
            return format.Frequency != null ? format.Frequency(frequency) : TipNumberFormat.Round(frequency);
        }

        private static string Money(float amount, TipFormat format)
        {
            return format.Money != null ? format.Money(amount) : TipNumberFormat.Round(amount);
        }

        private static string DurationText(int days, TipFormat format)
        {
            return format.Duration != null ? format.Duration(days) : format.L("rtux.tip.days", new object[] { TipNumberFormat.Group(days) });
        }

        // The decimal separator of the language, from the string table (",", "."), so a number reads the way the player's language writes it.
        private static string Decimal(TipFormat format)
        {
            string separator = format.L("rtux.decimal", new object[0]);
            return string.IsNullOrEmpty(separator) || separator.Length > 2 ? "." : separator;
        }

        /// <summary>A factor with two decimals: 1.2859 gives "1,29" (with the separator of the language); a value that is no number gives the sign of infinity.</summary>
        public static string Factor(double value, string separator)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return TipNumberFormat.Infinity;
            return Replace(value.ToString("0.00", CultureInfo.InvariantCulture), separator);
        }

        /// <summary>A number with at most two decimals and no trailing zeros: 1.5 gives "1,5", 2 gives "2", 1.2247 gives "1,22".</summary>
        public static string Trimmed(double value, string separator)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return TipNumberFormat.Infinity;

            string text = value.ToString("0.##", CultureInfo.InvariantCulture);
            return Replace(text, separator);
        }

        private static string Replace(string invariant, string separator)
        {
            return string.IsNullOrEmpty(separator) || separator == "." ? invariant : invariant.Replace(".", separator);
        }
    }
}
