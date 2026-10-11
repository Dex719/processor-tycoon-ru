using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// One finished row of the compare table (FR-6): the label, the two values as the player reads them, the numbers behind them and the
    /// verdict. The window writes the texts and paints the better side. A pure copy of the public <c>CompareRow</c> of the API (the pure logic
    /// cannot see the API folder); the window turns the rows other features return into this type at the border.
    /// </summary>
    public sealed class CompareLine
    {
        /// <summary>Stable identifier, unique in the table.</summary>
        public string Id = string.Empty;

        /// <summary>Key of the label in the string tables.</summary>
        public string LabelKey = string.Empty;

        public string ValueA = CompareModel.Dash;
        public string ValueB = CompareModel.Dash;

        /// <summary>The number behind <see cref="ValueA"/>; null for a text row and for a side that is not chosen.</summary>
        public float? NumA;

        public float? NumB;

        public CompareDirection Better;

        /// <summary>Sort key, ascending.</summary>
        public int Order;

        /// <summary>The difference column: "+18 %", "0 %" or "—".</summary>
        public string DeltaText = CompareModel.Dash;

        /// <summary>The highlighted side: -1 is A, 0 none, +1 is B.</summary>
        public int Winner;
    }

    /// <summary>
    /// The compare table of two processors (FR-6, design: Component 8) as pure logic: the 26 built-in rows from the facts of the two CPUs, the
    /// rows other features add, the column of differences and the verdict of each row.
    /// <list type="bullet">
    /// <item><b>The difference</b> is <c>(B − A) / |A| × 100</c> (<see cref="DeltaPercent"/>); "—" when A is 0, when a side has no number
    /// (a text row, a side that is not chosen) and when the number is no number at all (item 5). A = B gives "0 %" (AC-6.2).</item>
    /// <item><b>The highlight</b> goes to the better side when the difference is at least the threshold (2.5 % by default) and the row has a
    /// direction (item 4); a row without direction is never highlighted.</item>
    /// <item><b>The order</b> is the <c>Order</c> of the row, rows with the same one keep the order in which they were added: the built-in rows
    /// first, then those of other features.</item>
    /// </list>
    /// Nothing here reads the game; the facts come in as <see cref="CpuFacts"/> and the words and numbers go out through <see cref="IFormatter"/>.
    /// </summary>
    public static class CompareModel
    {
        /// <summary>What a value that cannot be given is written as.</summary>
        public const string Dash = "—";

        /// <summary>At most this many rows are built (built-in and of other features together; design: "Security").</summary>
        public const int MaxRows = 64;

        /// <summary>The prefix of the keys of the labels of the built-in rows: <c>rtux.compare.row.&lt;id&gt;</c>.</summary>
        public const string LabelKeyPrefix = "rtux.compare.row.";

        // The unit of the power and the temperature, as the inspector writes them (CpuUI.SetCpu).
        private const string PowerUnit = " W";
        private const string TemperatureUnit = "°C";
        private const string DieSizeUnit = "mm²";

        // IPS per watt divides by the power clamped as Market.CalculateRatings does (design: "Формулы и баланс").
        private const float MinPowerForRatio = 0.5f;
        private const float MaxPowerForRatio = 9999f;

        // Rounding of the highlight: a difference of 2.4999 that is 2.5 up to the float of the input counts.
        private const double ThresholdSlack = 1e-4;

        private sealed class RowDef
        {
            public string Id;
            public Func<CpuFacts, IFormatter, string> Text;
            public Func<CpuFacts, float?> Number;
        }

        private static readonly RowDef[] Rows =
        {
            Text("company", c => c.Company),
            Text("manufacturer", c => c.Manufacturer),
            Text("package", c => c.Package),
            Text("lithography", c => c.Lithography),
            Numeric("ips", (c, f) => f.Ips(c.Ips), c => c.Ips),
            Numeric("ipc", (c, f) => f.Number(c.Ipc, string.Empty), c => c.Ipc),
            Numeric("frequency", (c, f) => f.Frequency(c.Frequency), c => c.Frequency),
            Numeric("cores", (c, f) => f.Cores(c.CoreCount), c => c.CoreCount),
            new RowDef { Id = "smt", Text = (c, f) => SmtText(c, f) },
            Numeric("power", (c, f) => f.Number(c.Power, PowerUnit), c => c.Power),
            Numeric("temperature", (c, f) => f.Number(c.Temperature, TemperatureUnit), c => c.Temperature),
            Numeric("yield", (c, f) => f.Percent(YieldOf(c)), c => YieldOf(c)),
            Numeric("unitCost", (c, f) => f.Money(c.RealUnitCost), c => c.RealUnitCost),
            Numeric("price", (c, f) => f.Money(c.Price), c => c.Price),
            Numeric("margin", (c, f) => f.Money(c.Price - c.RealUnitCost), c => c.Price - c.RealUnitCost),
            Numeric("ipsPerWatt", (c, f) => f.Ips(IpsPerWatt(c) ?? 0f), c => IpsPerWatt(c)),
            Numeric("ipsPerDollar", (c, f) => f.Ips(IpsPerDollar(c) ?? 0f), c => IpsPerDollar(c)),
            Numeric("popularity", (c, f) => f.Integer(c.AvgPopularity * 100f), c => c.AvgPopularity * 100f),
            Numeric("soldLastMonth", (c, f) => f.Integer(c.SoldLastMonth), c => c.SoldLastMonth),
            Numeric("dieSize", (c, f) => f.Number(c.DieSize, DieSizeUnit), c => c.DieSize),
            Numeric("transistors", (c, f) => f.Integer(c.Transistors), c => c.Transistors),
            Text("memory", c => c.Memory),
            Numeric("l1", (c, f) => f.Cache(1, c.L1Steps), c => c.L1Steps),
            Numeric("l2", (c, f) => f.Cache(2, c.L2Steps), c => c.L2Steps),
            Numeric("l3", (c, f) => f.Cache(3, c.L3Steps), c => c.L3Steps),
            Numeric("year", (c, f) => c.Year.ToString(CultureInfo.InvariantCulture), c => c.Year),
        };

        /// <summary>The ids of the built-in rows, in the order of the code (not the order of the table).</summary>
        public static IReadOnlyList<string> BuiltInIds
        {
            get
            {
                var ids = new List<string>(Rows.Length);
                foreach (RowDef row in Rows) ids.Add(row.Id);
                return ids;
            }
        }

        /// <summary>
        /// Builds the table for two processors. Either may be null (that side is not chosen): its column is "—" and the difference is "—".
        /// <paramref name="cfg"/> gives the direction and the position of each built-in row (null: the defaults), <paramref name="extraRows"/> the rows of
        /// other features (may be null; a row with an id that is taken is dropped; a row without <see cref="CompareLine.DeltaText"/> gets its
        /// difference and verdict from its numbers). The result is sorted by <see cref="CompareLine.Order"/> and has at most <see cref="MaxRows"/> rows.
        /// </summary>
        public static List<CompareLine> Build(CpuFacts a, CpuFacts b, CompareConfig cfg, IFormatter fmt, IReadOnlyList<CompareLine> extraRows)
        {
            if (fmt == null) throw new ArgumentNullException("fmt");
            if (cfg == null) cfg = CompareConfig.Defaults();

            CompareConfig defaults = null;
            string separator = SeparatorOf(fmt);
            float threshold = cfg.DeltaHighlightPercent;
            var lines = new List<CompareLine>(Rows.Length + 4);
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (RowDef def in Rows)
            {
                CompareRowSpec spec = cfg.SpecOf(def.Id);
                if (spec == null)
                {
                    if (defaults == null) defaults = CompareConfig.Defaults();
                    spec = defaults.SpecOf(def.Id);
                }

                var line = new CompareLine
                {
                    Id = def.Id,
                    LabelKey = LabelKeyPrefix + def.Id,
                    Better = spec == null ? CompareDirection.None : spec.Better,
                    Order = spec == null ? 0 : spec.Order,
                };
                FillValues(line, def, a, b, fmt);
                Judge(line, threshold, separator);

                lines.Add(line);
                ids.Add(def.Id);
            }

            AddExtra(lines, ids, extraRows, threshold, separator);
            return SortByOrder(lines);
        }

        /// <summary>
        /// The difference in percent, <c>(B − A) / |A| × 100</c>; null when A is 0 and B is not (the ratio has no meaning), when either side has no
        /// number or the numbers are no numbers. Two equal numbers, zero included, differ by 0 % (AC-6.2: the same processor on both sides).
        /// </summary>
        public static float? DeltaPercent(float? a, float? b)
        {
            if (!a.HasValue || !b.HasValue) return null;

            double x = a.Value;
            double y = b.Value;
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) return null;
            if (x == y) return 0f;
            if (x == 0.0) return null;

            double percent = (y - x) * 100.0 / Math.Abs(x);
            if (double.IsNaN(percent) || double.IsInfinity(percent)) return null;
            return (float)percent;
        }

        /// <summary>
        /// The text of the difference column: "+18 %", "−5 %", "0 %" when the two are equal (to a tenth of a percent), "—" without a difference.
        /// Below 10 % one decimal is kept ("+2,5 %"), above it whole percents ("+13 300 %"). The sign of a fall is the minus sign of the tooltips.
        /// </summary>
        public static string DeltaText(float? delta, string decimalSeparator)
        {
            if (!delta.HasValue) return Dash;

            double value = delta.Value;
            double abs = Math.Abs(value);
            double rounded = Math.Round(abs, 1, MidpointRounding.AwayFromZero);
            if (rounded < 0.1) return "0 %";

            string number;
            if (rounded < 10.0)
            {
                number = rounded.ToString("0.#", CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(decimalSeparator) && decimalSeparator != ".") number = number.Replace(".", decimalSeparator);
            }
            else
            {
                number = TipNumberFormat.Round(abs);
            }
            return (value < 0 ? "−" : "+") + number + " %";
        }

        /// <summary>
        /// The verdict of a row: -1 when A is the better side, +1 when B is, 0 for none. A side is better when the difference reaches
        /// <paramref name="thresholdPercent"/> in the direction of the row; no difference, no direction or a small one gives 0.
        /// </summary>
        public static int WinnerOf(float? delta, CompareDirection better, float thresholdPercent)
        {
            if (!delta.HasValue || better == CompareDirection.None) return 0;

            double value = delta.Value;
            if (Math.Abs(value) + ThresholdSlack < thresholdPercent) return 0;
            if (value == 0.0) return 0;

            bool bIsHigher = value > 0.0;
            if (better == CompareDirection.Higher) return bIsHigher ? 1 : -1;
            return bIsHigher ? -1 : 1;
        }

        // ---- building the rows ----

        private static void FillValues(CompareLine line, RowDef def, CpuFacts a, CpuFacts b, IFormatter fmt)
        {
            line.ValueA = ValueOf(def, a, fmt, out line.NumA);
            line.ValueB = ValueOf(def, b, fmt, out line.NumB);
        }

        private static string ValueOf(RowDef def, CpuFacts facts, IFormatter fmt, out float? number)
        {
            number = null;
            if (facts == null) return Dash;

            if (def.Number != null)
            {
                number = Finite(def.Number(facts));
                if (!number.HasValue) return Dash;   // a number the row cannot work out (IPS per dollar at a price of 0)
            }

            string text = def.Text(facts, fmt);
            return string.IsNullOrEmpty(text) ? Dash : text;
        }

        private static void Judge(CompareLine line, float threshold, string separator)
        {
            float? delta = DeltaPercent(line.NumA, line.NumB);
            line.DeltaText = DeltaText(delta, separator);
            line.Winner = WinnerOf(delta, line.Better, threshold);
        }

        private static void AddExtra(List<CompareLine> lines, HashSet<string> ids, IReadOnlyList<CompareLine> extraRows, float threshold, string separator)
        {
            if (extraRows == null) return;

            foreach (CompareLine row in extraRows)
            {
                if (lines.Count >= MaxRows) break;
                if (row == null || string.IsNullOrEmpty(row.Id) || !ids.Add(row.Id)) continue;

                var line = new CompareLine
                {
                    Id = row.Id,
                    LabelKey = row.LabelKey ?? string.Empty,
                    ValueA = string.IsNullOrEmpty(row.ValueA) ? Dash : row.ValueA,
                    ValueB = string.IsNullOrEmpty(row.ValueB) ? Dash : row.ValueB,
                    NumA = Finite(row.NumA),
                    NumB = Finite(row.NumB),
                    Better = row.Better,
                    Order = row.Order,
                    DeltaText = row.DeltaText,
                    Winner = row.Winner,
                };
                if (string.IsNullOrEmpty(line.DeltaText)) Judge(line, threshold, separator);
                lines.Add(line);
            }
        }

        // A stable sort: List.Sort is not, and rows with the same position keep the order they were added in.
        private static List<CompareLine> SortByOrder(List<CompareLine> lines)
        {
            var indexed = new List<KeyValuePair<int, CompareLine>>(lines.Count);
            for (int i = 0; i < lines.Count; i++) indexed.Add(new KeyValuePair<int, CompareLine>(i, lines[i]));
            indexed.Sort((x, y) =>
            {
                int byOrder = x.Value.Order.CompareTo(y.Value.Order);
                return byOrder != 0 ? byOrder : x.Key.CompareTo(y.Key);
            });

            var sorted = new List<CompareLine>(lines.Count);
            foreach (KeyValuePair<int, CompareLine> pair in indexed) sorted.Add(pair.Value);
            return sorted;
        }

        // ---- the values of the rows ----

        private static RowDef Text(string id, Func<CpuFacts, string> read)
        {
            return new RowDef { Id = id, Text = (c, f) => read(c) };
        }

        private static RowDef Numeric(string id, Func<CpuFacts, IFormatter, string> text, Func<CpuFacts, float?> number)
        {
            return new RowDef { Id = id, Text = text, Number = number };
        }

        // CpuUI.SetCpu: Clamp(ReversePercentageScale(FailureRate), 0, 1), and ReversePercentageScale(x) is 1 - x.
        private static float YieldOf(CpuFacts c)
        {
            return Clamp01(1f - c.FailureRate);
        }

        private static float? IpsPerWatt(CpuFacts c)
        {
            float power = Math.Min(Math.Max(c.Power, MinPowerForRatio), MaxPowerForRatio);
            if (float.IsNaN(c.Power)) return null;
            return c.Ips / power;
        }

        private static float? IpsPerDollar(CpuFacts c)
        {
            return c.Price > 0f ? c.Ips / c.Price : (float?)null;
        }

        // The inspector's words: Enabled / Disabled, and "Not Supported" for a package with one core.
        private static string SmtText(CpuFacts c, IFormatter fmt)
        {
            if (!c.MultiCoreSupported) return fmt.Text("rtux.compare.smt.na");
            return fmt.Text(c.Smt ? "rtux.compare.smt.on" : "rtux.compare.smt.off");
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static float? Finite(float? value)
        {
            if (!value.HasValue) return null;
            return float.IsNaN(value.Value) || float.IsInfinity(value.Value) ? (float?)null : value;
        }

        private static string SeparatorOf(IFormatter fmt)
        {
            string separator = fmt.Text("rtux.decimal");
            return string.IsNullOrEmpty(separator) || separator.Length > 2 ? "." : separator;
        }
    }
}
