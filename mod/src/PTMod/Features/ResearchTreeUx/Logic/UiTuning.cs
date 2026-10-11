using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The numbers of the view part of <c>Content/research-tree-ux/ui.json</c> that the tree, the tooltip and the "new" marks use: how long the
    /// cursor must stay on a technology to count as seen (<c>hoverSeenSeconds</c>), the rules of the game's research time by year
    /// (<c>timeRescale</c>, copied from <c>TechnologyTimeUpdater</c> for the text of the tooltip) and the fundings the tooltip shows the time
    /// left at (<c>tooltipFundings</c>). The tabs are read by <see cref="TabCatalog"/>; the rest of the file belongs to the parts that use it.
    /// <para>
    /// Every value is checked on reading: one that is missing keeps the default silently, one that is out of range or of the wrong kind keeps the
    /// default and adds a line to <c>problems</c> (the adapter writes them to the log, decision 0006). Nothing throws.
    /// </para>
    /// </summary>
    public sealed class UiTuning
    {
        public const float MinHoverSeenSeconds = 0f;
        public const float MaxHoverSeenSeconds = 10f;

        /// <summary>At most this many fundings are kept in <see cref="TooltipFundings"/>.</summary>
        public const int MaxFundings = 8;

        /// <summary>Seconds the cursor stays on a technology before it counts as seen.</summary>
        public float HoverSeenSeconds = HoverSeenTimer.DefaultSeconds;

        /// <summary>Penalty of researching ahead of the year, as a fraction per year (3.5 is +350 % per year; <c>TechnologyTimeUpdater.penaltyPerYear / 100</c>).</summary>
        public float PenaltyPerYear = 3.5f;

        /// <summary>Bonus for researching late, as a fraction per year after the first (0.15 is 15 % per year; <c>bonusPerYear / 100</c>).</summary>
        public float BonusPerYear = 0.15f;

        /// <summary>The most the bonus can reach, as a fraction (0.75 is 75 %).</summary>
        public float BonusCap = 0.75f;

        /// <summary>The fundings (1 is 100 %) the tooltip adds the time left at; positive, ascending. Never null.</summary>
        public float[] TooltipFundings = { 0.5f, 2.0f, 5.0f };

        /// <summary>The values of the spec.</summary>
        public static UiTuning Defaults()
        {
            return new UiTuning();
        }

        /// <summary>Reads the numbers from the merged ui.json. A null file gives the defaults.</summary>
        public static UiTuning FromConfig(JsonObject config, List<string> problems)
        {
            var tuning = new UiTuning();
            if (config == null) return tuning;

            double hover;
            if (config.TryGetDouble("hoverSeenSeconds", out hover))
            {
                if (IsNumber(hover) && hover >= MinHoverSeenSeconds && hover <= MaxHoverSeenSeconds) tuning.HoverSeenSeconds = (float)hover;
                else Report(problems, "hoverSeenSeconds: expected a number from " + MinHoverSeenSeconds + " to " + MaxHoverSeenSeconds + ", " + tuning.HoverSeenSeconds + " is used");
            }
            else if (config.ContainsKey("hoverSeenSeconds"))
            {
                Report(problems, "hoverSeenSeconds: not a number, " + tuning.HoverSeenSeconds + " is used");
            }

            JsonNode rescaleNode;
            if (config.TryGetValue("timeRescale", out rescaleNode))
            {
                var rescale = rescaleNode as JsonObject;
                if (rescale == null)
                {
                    Report(problems, "timeRescale: expected an object, the defaults are used");
                }
                else
                {
                    tuning.PenaltyPerYear = ReadFraction(rescale, "penaltyPerYear", tuning.PenaltyPerYear, 0f, 100f, problems);
                    tuning.BonusPerYear = ReadFraction(rescale, "bonusPerYear", tuning.BonusPerYear, 0f, 10f, problems);
                    tuning.BonusCap = ReadFraction(rescale, "bonusCap", tuning.BonusCap, 0f, 1f, problems);
                }
            }

            JsonNode fundingsNode;
            if (config.TryGetValue("tooltipFundings", out fundingsNode))
            {
                float[] fundings = ReadFundings(fundingsNode as JsonArray, problems);
                if (fundings != null) tuning.TooltipFundings = fundings;
            }

            return tuning;
        }

        private static float ReadFraction(JsonObject record, string key, float fallback, float min, float max, List<string> problems)
        {
            double value;
            if (!record.TryGetDouble(key, out value))
            {
                if (record.ContainsKey(key)) Report(problems, "timeRescale." + key + ": not a number, " + fallback + " is used");
                return fallback;
            }

            if (!IsNumber(value) || value < min || value > max)
            {
                Report(problems, "timeRescale." + key + ": expected a number from " + min + " to " + max + ", " + fallback + " is used");
                return fallback;
            }
            return (float)value;
        }

        // Null: the array is unusable, the defaults stand.
        private static float[] ReadFundings(JsonArray array, List<string> problems)
        {
            if (array == null)
            {
                Report(problems, "tooltipFundings: expected an array of numbers, the defaults are used");
                return null;
            }

            var fundings = new List<float>();
            int index = 0;
            foreach (JsonNode item in array)
            {
                string path = "tooltipFundings[" + index + "]";
                index++;

                var value = item as JsonValue;
                if (value == null || value.Kind != JsonKind.Number || !IsNumber(value.NumberValue) || value.NumberValue <= 0.0 || value.NumberValue > 10.0)
                {
                    Report(problems, path + ": expected a number above 0 and up to 10, the entry is skipped");
                    continue;
                }
                if (fundings.Count >= MaxFundings)
                {
                    Report(problems, "tooltipFundings: more than " + MaxFundings + " entries, the rest are skipped");
                    break;
                }

                float funding = (float)value.NumberValue;
                if (!fundings.Contains(funding)) fundings.Add(funding);
            }

            if (fundings.Count == 0 && array.Count > 0)
            {
                Report(problems, "tooltipFundings: no usable entry, the defaults are used");
                return null;
            }

            fundings.Sort();
            return fundings.ToArray();
        }

        private static bool IsNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }
    }
}
