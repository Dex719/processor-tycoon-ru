using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.GpuProductLine
{
    // The pieces the windows of the feature share and the pure models are built from (design.md, "UI", TSK-022...TSK-026). Nothing here knows Unity or the game:
    // the models of the tabs (Logic/Ui) turn the services into plain data and texts, the classes of the Ui folder draw them with ModUI.

    /// <summary>The words and numbers of the window, in the language of the player and in the format of the game (NFR-4); the game side wraps <c>ModL10n</c> and <c>StringFormatter</c>.</summary>
    public interface IGpuText
    {
        /// <summary>The player reads Russian (<c>ModL10n.Language</c> is <c>ru</c>): the names of the makers that the content gives in both languages are taken in it.</summary>
        bool IsRussian { get; }

        /// <summary>The text of a key with <c>{0}..{n}</c> filled in (<c>ModL10n.L</c>).</summary>
        string L(string key, params object[] args);

        /// <summary>True when the key has a text in the current language (or in the fallback one): a name from the content may name a key nobody wrote.</summary>
        bool Has(string key);

        /// <summary>The form of a text that fits <paramref name="n"/> (<c>ModL10n.Plural</c>); <c>{0}</c> is the number.</summary>
        string Plural(string key, long n, params object[] args);

        /// <summary>Money (<c>StringFormatter.FloatToMoney</c>, abbreviated, with the currency).</summary>
        string Money(float amount);

        /// <summary>A fraction as a percentage (<c>StringFormatter.FloatToPercentageString</c>): 0.25 is "25%".</summary>
        string Percent(float fraction);

        /// <summary>A clock in megahertz (<c>StringFormatter.FloatToFrequency</c>).</summary>
        string Frequency(float megahertz);

        /// <summary>A number with a unit (<c>StringFormatter.FloatToString</c>).</summary>
        string Number(float value, string unit);

        /// <summary>A big number shortened: 52.5M (<c>StringFormatter.FloatToString</c> with the abbreviation).</summary>
        string Compact(double value);

        /// <summary>A whole number with the separators of the game (<c>StringFormatter.FloatToIntString</c>).</summary>
        string Integer(double value);

        /// <summary>A date the way the player chose to read dates (<c>StringFormatter.DateToString</c>).</summary>
        string Date(DateTime date);

        /// <summary>A month of a year: "Mar 1996".</summary>
        string MonthYear(int year, int month);
    }

    /// <summary>The tone of a line of the window: the game's own colours for good, warning and bad news (they follow the theme); muted is the dimmed text.</summary>
    public enum GpuTone
    {
        Normal = 0,
        Good = 1,
        Warn = 2,
        Bad = 3,
        Muted = 4,
    }

    /// <summary>The tabs of the window "Видеокарты" (design.md, "UI-классы"); <see cref="Auto"/> is the one last shown.</summary>
    public enum GpuTab
    {
        Auto = 0,
        Research = 1,
        Designer = 2,
        Lineup = 3,
        Market = 4,
    }

    /// <summary>The tabs in the order of the bar, their captions and the number kept in the save (<c>ModFlags</c>, key <see cref="FlagKey"/>).</summary>
    public static class GpuTabs
    {
        /// <summary>The key of the flag that remembers the tab last open (FR-1 item 7).</summary>
        public const string FlagKey = "lastTab";

        /// <summary>The tabs of a founded division, in the order of the bar.</summary>
        public static readonly GpuTab[] All = { GpuTab.Research, GpuTab.Designer, GpuTab.Lineup, GpuTab.Market };

        /// <summary>The caption key of a tab.</summary>
        public static string TitleKey(GpuTab tab)
        {
            switch (tab)
            {
                case GpuTab.Designer: return "gpu.tab.designer";
                case GpuTab.Lineup: return "gpu.tab.lineup";
                case GpuTab.Market: return "gpu.tab.market";
                default: return "gpu.tab.research";
            }
        }

        /// <summary>The number that is kept in the save for a tab (1..4); 0 for <see cref="GpuTab.Auto"/>.</summary>
        public static int ToStored(GpuTab tab)
        {
            return tab >= GpuTab.Research && tab <= GpuTab.Market ? (int)tab : 0;
        }

        /// <summary>The tab of a stored number; a number that is no tab (never stored, a save of another version) gives the first tab.</summary>
        public static GpuTab FromStored(int stored)
        {
            return stored >= (int)GpuTab.Research && stored <= (int)GpuTab.Market ? (GpuTab)stored : GpuTab.Research;
        }

        /// <summary>The tab to show: the one asked for, or the stored one for <see cref="GpuTab.Auto"/>.</summary>
        public static GpuTab Resolve(GpuTab requested, int stored)
        {
            return requested == GpuTab.Auto ? FromStored(stored) : requested;
        }
    }

    /// <summary>The range of a slider whose ends and step are chosen together, so that both ends are on the grid and a round value is reachable.</summary>
    public struct GpuSliderRange
    {
        public float Min, Max, Step;

        private static readonly float[] NiceSteps = { 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 20f, 25f, 50f, 100f, 200f, 250f, 500f, 1000f };

        /// <summary>The number of positions from the low end to the high end.</summary>
        public int Positions
        {
            get { return Step > 0f ? (int)Math.Round((Max - Min) / Step) + 1 : 1; }
        }

        /// <summary>
        /// A range from about <paramref name="low"/> to about <paramref name="high"/> with a step of the nice sizes (1, 2, 5, 10, 20, 25, 50...) that gives
        /// at most <paramref name="maxPositions"/> positions; the low end is rounded up and the high end down to the step, so both are values the slider can take.
        /// A range that is empty or not finite becomes one position at <paramref name="low"/>.
        /// </summary>
        public static GpuSliderRange Nice(float low, float high, int maxPositions)
        {
            if (float.IsNaN(low) || float.IsInfinity(low)) low = 0f;
            if (float.IsNaN(high) || float.IsInfinity(high) || high < low) high = low;
            int positions = Math.Max(2, maxPositions);

            foreach (float step in NiceSteps)
            {
                float min = (float)Math.Ceiling(low / step - 1e-6) * step;
                float max = (float)Math.Floor(high / step + 1e-6) * step;
                if (max < min) continue;
                if ((max - min) / step + 1 <= positions) return new GpuSliderRange { Min = Clean(min), Max = Clean(max), Step = step };
            }

            float last = NiceSteps[NiceSteps.Length - 1];
            float lastMin = (float)Math.Ceiling(low / last) * last;
            float lastMax = Math.Max(lastMin, (float)Math.Floor(high / last) * last);
            return new GpuSliderRange { Min = lastMin, Max = lastMax, Step = last };
        }

        /// <summary>The value of the grid nearest to <paramref name="value"/>, held to the ends.</summary>
        public float Snap(float value)
        {
            if (float.IsNaN(value)) return Min;
            if (!(Step > 0f)) return Math.Min(Max, Math.Max(Min, value));
            float steps = (float)Math.Round((value - Min) / Step);
            float snapped = Min + steps * Step;
            return Clean(Math.Min(Max, Math.Max(Min, snapped)));
        }

        // 0.1 steps in single precision: 0.30000001 is shown and compared as 0.3.
        private static float Clean(float value)
        {
            return (float)Math.Round(value, 4);
        }
    }

    /// <summary>The small calculations of the windows that have no other home.</summary>
    public static class GpuUiMath
    {
        /// <summary>The longest span written in days; a longer one is written in years ("≈ 10 лет").</summary>
        public const int MaxDaysSpan = 548;

        /// <summary>The share of the performance that memory bandwidth takes away: <c>1 - clamp(r, ratioMin, 1)^exponent</c> (F6), 0 when memory keeps up.</summary>
        public static float BandwidthLoss(float ratio, float ratioMin, float exponent)
        {
            if (float.IsNaN(ratio) || ratio >= 1f) return 0f;
            double lo = Math.Min(1d, Math.Max(0d, ratioMin));
            double used = Math.Min(1d, Math.Max(lo, ratio));
            double loss = 1d - Math.Pow(used, exponent);
            return loss > 0d ? (float)Math.Min(1d, loss) : 0f;
        }

        /// <summary>
        /// The powers of two (in megabytes) from about <paramref name="maxMB"/> down: at most <paramref name="count"/> values, none below <paramref name="floorMB"/>,
        /// the largest power of two that is not above <paramref name="maxMB"/> last; the smallest first. Empty for a maximum below the floor.
        /// </summary>
        public static List<float> PowersOfTwo(float maxMB, int count, float floorMB)
        {
            var values = new List<float>();
            if (float.IsNaN(maxMB) || float.IsInfinity(maxMB) || !(maxMB > 0f) || count < 1) return values;

            double top = Math.Pow(2d, Math.Floor(Math.Log(maxMB, 2d) + 1e-9));
            double value = top;
            double floor = floorMB > 0f ? floorMB : 0.0625d;
            for (int i = 0; i < count && value >= floor - 1e-12; i++)
            {
                values.Insert(0, (float)value);
                value /= 2d;
            }
            return values;
        }

        /// <summary>The index of the value nearest to <paramref name="target"/> on a log scale (the powers of two are evenly spaced on it); -1 for an empty list.</summary>
        public static int NearestIndex(IList<float> values, float target)
        {
            if (values == null || values.Count == 0) return -1;
            int best = 0;
            double bestDistance = double.MaxValue;
            double goal = Math.Log(Math.Max(1e-6, target));
            for (int i = 0; i < values.Count; i++)
            {
                double distance = Math.Abs(Math.Log(Math.Max(1e-6, values[i])) - goal);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The smallest index whose value is not below <paramref name="target"/>, or the last one; -1 for an empty list.</summary>
        public static int FirstAtLeast(IList<float> values, float target)
        {
            if (values == null || values.Count == 0) return -1;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] >= target) return i;
            }
            return values.Count - 1;
        }

        /// <summary>A span of days written for the player: days up to <see cref="MaxDaysSpan"/>, otherwise whole years; "—" for a span that is unknown (negative).</summary>
        public static string Span(IGpuText text, int days)
        {
            if (days < 0) return "—";
            if (days <= MaxDaysSpan) return text.Plural("gpu.fmt.days", days);
            long years = (long)Math.Round(days / 365.0);
            return text.Plural("gpu.fmt.years", Math.Max(1, years));
        }

        /// <summary>The number as the invariant culture writes it (for the text of an input field).</summary>
        public static string Plain(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A number the player typed. Spaces, a currency sign and letters are ignored; "1 249,5" and "1,249.5" are both 1249.5 (with both separators the last one is
        /// the decimal point; a lone comma is a decimal point when one or two digits follow it, otherwise a thousands separator). False for text that is no number
        /// or a number that is not finite.
        /// </summary>
        public static bool TryParseNumber(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text)) return false;

            var kept = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                if ((ch >= '0' && ch <= '9') || ch == '.' || ch == ',' || ch == '-') kept.Append(ch);
            }
            string digits = kept.ToString();
            int dot = digits.LastIndexOf('.');
            int comma = digits.LastIndexOf(',');
            if (dot >= 0 && comma >= 0)
            {
                // the later one is the decimal point, the other one groups thousands
                char thousands = dot > comma ? ',' : '.';
                digits = digits.Replace(thousands.ToString(), string.Empty);
                digits = digits.Replace(',', '.');
            }
            else if (comma >= 0)
            {
                bool single = digits.IndexOf(',') == comma;
                int after = digits.Length - comma - 1;
                digits = single && after >= 1 && after <= 2 ? digits.Replace(',', '.') : digits.Replace(",", string.Empty);
            }

            float parsed;
            if (!float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (float.IsNaN(parsed) || float.IsInfinity(parsed)) return false;
            value = parsed;
            return true;
        }
    }
}
