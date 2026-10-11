using System;
using System.Globalization;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The texts of one row of the research list (FR-2 item 2); the view puts each into its column.</summary>
    public sealed class ListRowText
    {
        public string Name = string.Empty;
        public string Year = string.Empty;

        /// <summary>"Researched", "Available", "In progress 43 %" or "Locked".</summary>
        public string Status = string.Empty;

        /// <summary>The time left, "instant", "∞" for a time that never ends at this funding, or a dash for a technology that is done.</summary>
        public string Time = string.Empty;

        /// <summary>The cost per month with the sign of the game's money, or a dash for a technology that is done.</summary>
        public string Cost = string.Empty;

        /// <summary>The mark "not needed" (empty when the technology is needed or done).</summary>
        public string Mark = string.Empty;

        public override string ToString()
        {
            return Name + " | " + Year + " | " + Status + " | " + Time + " | " + Cost + (Mark.Length == 0 ? string.Empty : " | " + Mark);
        }
    }

    /// <summary>
    /// Writes the texts of a row from a snapshot (FR-2 item 2): the name, the year, the status with the percentage of the work in progress,
    /// the time left and the cost per month. The times and the sums come from the formatters of the game (<see cref="TipFormat"/>), the same
    /// ones the button of the tree and the tooltip use, so a time reads the same in all three places (NFR-6).
    /// </summary>
    public static class ListRowTextBuilder
    {
        /// <summary>The text of a value that does not apply (the time and the cost of a technology that is done).</summary>
        public const string NoValue = "—";

        /// <summary>The key of the cost with its unit: "{0}/mo".</summary>
        public const string CostKey = "rtux.list.cost";

        /// <summary>The key of the mark "not needed".</summary>
        public const string MarkKey = "rtux.list.unnecessary";

        /// <summary>The key of the caption of a status.</summary>
        public static string StatusKey(RowStatus status)
        {
            switch (status)
            {
                case RowStatus.Researched: return "rtux.status.researched";
                case RowStatus.Current: return "rtux.status.current";
                case RowStatus.Locked: return "rtux.status.locked";
                default: return "rtux.status.available";
            }
        }

        /// <summary>The texts of a technology. <paramref name="format"/> needs its <c>L</c>. A null technology gives empty texts.</summary>
        public static ListRowText Build(TechSnapshot tech, TipFormat format)
        {
            var text = new ListRowText();
            if (tech == null) return text;
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");

            RowStatus status = ListRowRules.StatusOf(tech);
            text.Name = string.IsNullOrEmpty(tech.Name) ? "#" + tech.Id.ToString(CultureInfo.InvariantCulture) : tech.Name;
            text.Year = tech.Year.ToString(CultureInfo.InvariantCulture);
            text.Status = status == RowStatus.Current
                ? format.L(StatusKey(status), new object[] { TipNumberFormat.PercentValue(tech.Progress) })
                : format.L(StatusKey(status), new object[0]);

            if (status == RowStatus.Researched)
            {
                text.Time = NoValue;
                text.Cost = NoValue;
            }
            else
            {
                text.Time = TimeText(tech, format);
                text.Cost = tech.MonthlyCost > 0f
                    ? format.L(CostKey, new object[] { MoneyText(tech.MonthlyCost, format) })
                    : NoValue;
            }

            if (ListRowRules.IsMarkedUnnecessary(tech)) text.Mark = format.L(MarkKey, new object[0]);
            return text;
        }

        /// <summary>The key of the header of a group: "{0} ({1}/{2})", the name of the type and the count researched out of all.</summary>
        public const string GroupKey = "rtux.list.group";

        /// <summary>The header of a group of the list (FR-2 item 1): "Memory (3/9)". A type this version does not know is written by its number.</summary>
        public static string GroupText(ListGroup group, TipFormat format)
        {
            if (group == null) return string.Empty;
            if (format == null || format.L == null) throw new ArgumentException("format.L is required", "format");

            string name = TechTooltipBuilder.KindName(group.Type, format);
            if (name.Length == 0) name = "#" + group.Type.ToString(CultureInfo.InvariantCulture);
            return format.L(GroupKey, new object[] { name, group.Researched, group.Total });
        }

        // A technology with no base time takes no time at all (the tooltip says "instant" too); the days left of one that never ends are "∞".
        private static string TimeText(TechSnapshot tech, TipFormat format)
        {
            if (tech.BaseTime <= 0f) return format.L("rtux.tip.instant", new object[0]);
            if (tech.EtaDays < 0) return TipNumberFormat.Infinity;
            return format.Duration != null
                ? format.Duration(tech.EtaDays)
                : format.L("rtux.tip.days", new object[] { TipNumberFormat.Group(tech.EtaDays) });
        }

        private static string MoneyText(float amount, TipFormat format)
        {
            return format.Money != null ? format.Money(amount) : TipNumberFormat.Round(amount);
        }
    }
}
