using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The two texts of one tooltip (the header line and the body, TextMeshPro rich text); what <c>ModUI.Tooltip</c> shows.</summary>
    public readonly struct TipText
    {
        public readonly string Header;
        public readonly string Content;

        public TipText(string header, string content)
        {
            Header = header ?? string.Empty;
            Content = content ?? string.Empty;
        }

        /// <summary>Both texts are empty: nothing to show.</summary>
        public bool IsEmpty
        {
            get { return Header.Length == 0 && Content.Length == 0; }
        }
    }

    /// <summary>
    /// What the tooltip builder gets from the outside to write text: the language lookup and the two formatters of the game (so a time or a
    /// sum reads exactly as on the button of the tree, NFR-6). The adapter fills it with <c>ModL10n.L</c>, <c>StringFormatter.DaysToDate</c>
    /// and <c>StringFormatter.FloatToMoney</c>; a test passes its own.
    /// </summary>
    public sealed class TipFormat
    {
        /// <summary>The text of a key with its arguments (<c>ModL10n.L</c>). Required.</summary>
        public Func<string, object[], string> L;

        /// <summary>A number of days as the game writes a duration (<c>StringFormatter.DaysToDate</c>); when null, "N days" by the text of <c>rtux.tip.days</c>.</summary>
        public Func<int, string> Duration;

        /// <summary>An amount of money as the game writes it (<c>StringFormatter.FloatToMoney</c>); when null, the rounded number.</summary>
        public Func<float, string> Money;

        /// <summary>A frequency as the game writes it (<c>StringFormatter.FloatToFrequency</c>); used by the modifiers panel. When null, the rounded number.</summary>
        public Func<float, string> Frequency;

        /// <summary>
        /// Whether a key is known to the string tables (<c>ModL10n.Has</c>); used by the modifiers panel to leave out the reason of a source whose
        /// key no table has instead of printing a raw key. When null, every key counts as known.
        /// </summary>
        public Func<string, bool> Has;
    }

    /// <summary>
    /// The text of the technology tooltip (FR-4), without the game: name, kind and year; status; the base and the present research time with the
    /// penalty for being ahead of the year or the bonus for being late; the time left at the present funding and at the fundings of
    /// <c>ui.json</c>; the cost per month and what is left to spend; what the technology requires and opens; why a technology is unnecessary for the
    /// company; and the lines other features add. Everything it needs comes in as arguments (the snapshots, <see cref="ResearchContext"/>, the
    /// function that gives the time left at another funding, <see cref="TipFormat"/>), so the cases of the spec are tests.
    /// <list type="bullet">
    /// <item>A researched technology gets name, kind, status, requires and opens only: its time and cost say nothing any more.</item>
    /// <item>A locked technology gets the times and the cost per month, but not the time left (it cannot be started).</item>
    /// <item><c>BaseTime = 0</c> writes "instant" and never divides by the time (FR-4 item 5, AC-4.3). A funding of nearly zero writes "∞" for the
    /// time left and the money left (the game would divide by zero).</item>
    /// </list>
    /// </summary>
    public static class TechTooltipBuilder
    {
        /// <summary>At most this many names are listed for "requires" and "opens"; the rest are counted.</summary>
        public const int MaxListedNames = 8;

        /// <summary>A funding above this one (200 %) is shown only while the innovation mode is on (FR-4 item 4).</summary>
        public const float InnovationOnlyAbove = 2.0f;

        // The key of the name of each value of the game's TechnologyType.
        private static readonly string[] TypeKeys =
        {
            "rtux.type.package", "rtux.type.lithography", "rtux.type.memory", "rtux.type.frequency",
            "rtux.type.cache", "rtux.type.wafer", "rtux.type.multicore",
        };

        /// <summary>
        /// Builds the tooltip of <paramref name="tech"/>. <paramref name="byId"/> gives the snapshot of a required or opened technology (a missing one
        /// is written as its number), <paramref name="etaAtFunding"/> the days left at another funding (below zero: never; may be null),
        /// <paramref name="extraLines"/> the lines of other features (put at the end as a block of their own; may be null). A null technology
        /// gives an empty tooltip; a null <paramref name="ctx"/> or <paramref name="format"/> uses the defaults (a test of the shape of the text).
        /// </summary>
        public static TipText Build(TechSnapshot tech, ResearchContext ctx, IReadOnlyDictionary<int, TechSnapshot> byId,
            Func<float, int> etaAtFunding, IReadOnlyList<string> extraLines, TipFormat format)
        {
            if (tech == null) return new TipText(string.Empty, string.Empty);

            if (ctx == null) ctx = new ResearchContext();
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");

            var lines = new List<string>();
            AddKindAndStatus(lines, tech, format);

            if (!tech.IsResearched)
            {
                AddTimeLines(lines, tech, ctx, format);
                if (tech.IsAvailable && tech.BaseTime > 0f) AddEtaLines(lines, tech, ctx, etaAtFunding, format);
                AddCostLine(lines, tech, ctx, format);
            }

            AddRelations(lines, tech, ctx, byId, format);
            AddUnnecessary(lines, tech, ctx, format);
            AddExtraLines(lines, extraLines);

            return new TipText(NameOf(tech), string.Join("\n", lines.ToArray()));
        }

        // ---- the parts ----

        private static void AddKindAndStatus(List<string> lines, TechSnapshot tech, TipFormat format)
        {
            string kind = KindName(tech.Type, format);
            lines.Add(kind.Length == 0
                ? tech.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : format.L("rtux.tip.kind", new object[] { kind, tech.Year }));

            string status;
            if (tech.IsResearched) status = format.L("rtux.status.researched", new object[0]);
            else if (tech.IsCurrent) status = format.L("rtux.status.current", new object[] { TipNumberFormat.PercentValue(tech.Progress) });
            else if (tech.IsLocked) status = format.L("rtux.status.locked", new object[0]);
            else status = format.L("rtux.status.available", new object[0]);
            lines.Add(status);
        }

        private static void AddTimeLines(List<string> lines, TechSnapshot tech, ResearchContext ctx, TipFormat format)
        {
            if (tech.BaseTime <= 0f)
            {
                lines.Add(format.L("rtux.tip.timeOnly", new object[] { format.L("rtux.tip.instant", new object[0]) }));
                return;
            }

            bool changed = Math.Abs(tech.Time - tech.BaseTime) >= 0.5f;
            string baseText = DaysText(tech.BaseTime, format);
            string nowText = (changed ? "≈" : string.Empty) + DaysText(tech.Time, format);
            string line = format.L("rtux.tip.time", new object[] { baseText, nowText });

            string adjustment = YearAdjustment(tech, ctx, format);
            lines.Add(adjustment.Length == 0 ? line : line + ", " + adjustment);
        }

        // "ahead-of-time penalty +13 300 % (+350 % per year)" or "catch-up bonus -45 %", from the times the game already worked out.
        private static string YearAdjustment(TechSnapshot tech, ResearchContext ctx, TipFormat format)
        {
            if (ctx.CurrentYear <= 0 || tech.BaseTime <= 0f) return string.Empty;

            if (tech.Year > ctx.CurrentYear)
            {
                long percent = TipNumberFormat.RoundToLong((tech.Time / (double)tech.BaseTime - 1.0) * 100.0);
                if (percent < 1) return string.Empty;
                string text = format.L("rtux.tip.penalty", new object[]
                {
                    TipNumberFormat.Group(percent), TipNumberFormat.Round(ctx.PenaltyPerYear * 100.0),
                });
                return Colored(text, ctx.NegativeColor);
            }

            if (tech.Year < ctx.CurrentYear && tech.Time < tech.BaseTime)
            {
                long percent = TipNumberFormat.RoundToLong((1.0 - tech.Time / (double)tech.BaseTime) * 100.0);
                if (percent < 1) return string.Empty;
                return Colored(format.L("rtux.tip.bonus", new object[] { TipNumberFormat.Group(percent) }), ctx.PositiveColor);
            }

            return string.Empty;
        }

        // The time left at the present funding and, one line each, at the other fundings of ui.json (FR-4 item 4).
        private static void AddEtaLines(List<string> lines, TechSnapshot tech, ResearchContext ctx, Func<float, int> etaAtFunding, TipFormat format)
        {
            string present = ctx.HasNoFunding || tech.EtaDays < 0 ? TipNumberFormat.Infinity : DurationText(tech.EtaDays, format);
            lines.Add(format.L("rtux.tip.eta", new object[] { TipNumberFormat.PercentValue(ctx.Funding), present }));

            float[] fundings = ctx.TooltipFundings ?? new float[0];
            foreach (float funding in fundings)
            {
                if (float.IsNaN(funding) || funding < ResearchContext.MinFunding) continue;
                if (funding > InnovationOnlyAbove && !ctx.InnovationEnabled) continue;
                if (Math.Abs(funding - ctx.Funding) < 0.005f) continue;   // the line of the present funding is there already

                int days = etaAtFunding == null ? -1 : etaAtFunding(funding);
                string text = days < 0 ? TipNumberFormat.Infinity : DurationText(days, format);
                lines.Add(format.L("rtux.tip.eta", new object[] { TipNumberFormat.PercentValue(funding), text }));
            }
        }

        // Cost per month at the present funding; for a technology that can be started also what is left to pay at that funding.
        private static void AddCostLine(List<string> lines, TechSnapshot tech, ResearchContext ctx, TipFormat format)
        {
            if (tech.MonthlyCost <= 0f) return;

            double funding = ctx.HasNoFunding ? 0.0 : ctx.Funding;
            double perMonth = tech.MonthlyCost * (double)ctx.CostMultiplierTotal * funding;
            string perMonthText = MoneyText((float)perMonth, format);

            if (!tech.IsAvailable || tech.BaseTime <= 0f)
            {
                lines.Add(format.L("rtux.tip.costOnly", new object[] { perMonthText }));
                return;
            }

            string left = TipNumberFormat.Infinity;
            if (!ctx.HasNoFunding && tech.EtaDays >= 0) left = MoneyText((float)(perMonth / 30.0 * tech.EtaDays), format);
            lines.Add(format.L("rtux.tip.cost", new object[] { perMonthText, left }));
        }

        private static void AddRelations(List<string> lines, TechSnapshot tech, ResearchContext ctx, IReadOnlyDictionary<int, TechSnapshot> byId, TipFormat format)
        {
            if (tech.DependencyIds != null && tech.DependencyIds.Length > 0)
            {
                lines.Add(format.L("rtux.tip.deps", new object[] { NameList(tech.DependencyIds, byId, ctx, true, format) }));
            }
            if (tech.UnlocksIds != null && tech.UnlocksIds.Length > 0)
            {
                lines.Add(format.L("rtux.tip.unlocks", new object[] { NameList(tech.UnlocksIds, byId, ctx, false, format) }));
            }
        }

        private static void AddUnnecessary(List<string> lines, TechSnapshot tech, ResearchContext ctx, TipFormat format)
        {
            if (!tech.IsUnnecessary || tech.IsResearched) return;

            string key = ctx.IsFabless ? "rtux.tip.unnecessary.fabless" : ctx.IsFoundry ? "rtux.tip.unnecessary.foundry" : null;
            if (key == null) return;
            lines.Add(Colored(format.L(key, new object[0]), ctx.WarningColor));
        }

        private static void AddExtraLines(List<string> lines, IReadOnlyList<string> extraLines)
        {
            if (extraLines == null) return;

            bool separated = false;
            for (int i = 0; i < extraLines.Count; i++)
            {
                string line = extraLines[i];
                if (string.IsNullOrEmpty(line)) continue;
                if (!separated)
                {
                    lines.Add(string.Empty);   // a block of its own
                    separated = true;
                }
                lines.Add(line);
            }
        }

        // ---- small helpers ----

        private static string NameList(int[] ids, IReadOnlyDictionary<int, TechSnapshot> byId, ResearchContext ctx, bool markResearched, TipFormat format)
        {
            var text = new StringBuilder();
            int listed = Math.Min(ids.Length, MaxListedNames);
            for (int i = 0; i < listed; i++)
            {
                TechSnapshot other = null;
                bool known = byId != null && byId.TryGetValue(ids[i], out other) && other != null;

                string name = known ? NameOf(other) : "#" + ids[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (markResearched && known && other.IsResearched) name = Colored(name, ctx.PositiveColor);

                if (i > 0) text.Append(", ");
                text.Append(name);
            }
            if (ids.Length > listed) text.Append(", ").Append(format.L("rtux.tip.more", new object[] { ids.Length - listed }));
            return text.ToString();
        }

        /// <summary>The name of a technology type ("Memory"); empty for a type this version does not know.</summary>
        public static string KindName(int technologyType, TipFormat format)
        {
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");
            if (technologyType < 0 || technologyType >= TypeKeys.Length) return string.Empty;
            return format.L(TypeKeys[technologyType], new object[0]);
        }

        private static string NameOf(TechSnapshot tech)
        {
            return string.IsNullOrEmpty(tech.Name) ? "#" + tech.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : tech.Name;
        }

        private static string DaysText(float days, TipFormat format)
        {
            return format.L("rtux.tip.days", new object[] { TipNumberFormat.Round(days) });
        }

        private static string DurationText(int days, TipFormat format)
        {
            return format.Duration != null ? format.Duration(days) : format.L("rtux.tip.days", new object[] { TipNumberFormat.Group(days) });
        }

        private static string MoneyText(float amount, TipFormat format)
        {
            return format.Money != null ? format.Money(amount) : TipNumberFormat.Round(amount);
        }

        private static string Colored(string text, string color)
        {
            return string.IsNullOrEmpty(color) ? text : "<color=" + color + ">" + text + "</color>";
        }
    }
}
