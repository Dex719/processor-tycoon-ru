using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The numbers of <c>Content/contract-transparency/rules.json</c> (design.md, "Контент rules.json"; FR-4, FR-13, NFR-6): the thresholds of the risk
    /// flags, the limits of the log, the margin threshold of the AI and the number of decimals the panels print. Immutable and free of the game:
    /// <see cref="FromJson"/> reads the merged config object (<c>ModContent.LoadConfig</c> has already laid the player's override over the built-in file)
    /// and never throws. Every value that is missing, of a wrong kind or outside its range is replaced by the default, and the reason goes
    /// into <c>problems</c> as one English line; the feature writes each line to <c>ModLog</c>.
    /// <para>
    /// Ranges: <c>0 &lt; lowActualChance &lt; 1</c>, <c>0 &lt; breakFineVsCashRatio &lt;= 100</c>, <c>50 &lt;= capacity &lt;= 5000</c>,
    /// <c>1 &lt;= tenderHistoryYears &lt;= 60</c>, <c>0 &lt;= contractMarginThreshold &lt;= 1</c>, <c>0 &lt;= decimals &lt;= 3</c>.
    /// A field this version does not know is reported (a typo in an override would be ignored silently otherwise) and ignored.
    /// </para>
    /// </summary>
    public sealed class ContractRules
    {
        /// <summary>The <c>schema</c> of <c>rules.json</c> this version reads (<c>ModContent.LoadConfig</c> ignores an override with another one).</summary>
        public const int SupportedSchema = 1;

        /// <summary>The name of the content file inside <c>Content/contract-transparency/</c>.</summary>
        public const string FileName = "rules.json";

        public const double DefaultLowActualChance = 0.10;
        public const bool DefaultUseAutomationMarginAsThreshold = true;
        public const double DefaultBreakFineVsCashRatio = 1.0;
        public const int DefaultLogCapacity = 500;
        public const int DefaultTenderHistoryYears = 10;
        public const double DefaultAiContractMarginThreshold = 0.45;
        public const int DefaultPercentDecimals = 0;
        public const int DefaultActualPercentDecimals = 0;

        public const int MinLogCapacity = 50;
        public const int MaxLogCapacity = 5000;
        public const int MinTenderHistoryYears = 1;
        public const int MaxTenderHistoryYears = 60;
        public const double MaxBreakFineVsCashRatio = 100.0;
        public const int MaxDecimals = 3;

        private static readonly ContractRules DefaultRules = new ContractRules(
            DefaultLowActualChance, DefaultUseAutomationMarginAsThreshold, DefaultBreakFineVsCashRatio,
            DefaultLogCapacity, DefaultTenderHistoryYears, DefaultAiContractMarginThreshold,
            DefaultPercentDecimals, DefaultActualPercentDecimals);

        private ContractRules(
            double lowActualChance, bool useAutomationMarginAsThreshold, double breakFineVsCashRatio,
            int logCapacity, int tenderHistoryYears, double aiContractMarginThreshold,
            int percentDecimals, int actualPercentDecimals)
        {
            LowActualChance = lowActualChance;
            UseAutomationMarginAsThreshold = useAutomationMarginAsThreshold;
            BreakFineVsCashRatio = breakFineVsCashRatio;
            LogCapacity = logCapacity;
            TenderHistoryYears = tenderHistoryYears;
            AiContractMarginThreshold = aiContractMarginThreshold;
            PercentDecimals = percentDecimals;
            ActualPercentDecimals = actualPercentDecimals;
        }

        /// <summary>The rules with every default (what the built-in file says; used when the file cannot be read at all).</summary>
        public static ContractRules Default
        {
            get { return DefaultRules; }
        }

        /// <summary><c>risk.lowActualChance</c>: an actual probability below this is a risk (<c>RiskFlags.LowActualChance</c>), 0.10.</summary>
        public double LowActualChance { get; }

        /// <summary><c>risk.useAutomationMarginAsThreshold</c>: the margin threshold of the contract automation (<c>Player.ContractProfitMargin</c>) is a risk line too.</summary>
        public bool UseAutomationMarginAsThreshold { get; }

        /// <summary><c>risk.breakFineVsCashRatio</c>: the break fine counts as a risk when it exceeds this share of the company's money, 1.0.</summary>
        public double BreakFineVsCashRatio { get; }

        /// <summary><c>log.capacity</c>: the most entries the contract log keeps, 500.</summary>
        public int LogCapacity { get; }

        /// <summary><c>log.tenderHistoryYears</c>: tender results older than this many years are dropped when a save is loaded, 10.</summary>
        public int TenderHistoryYears { get; }

        /// <summary><c>ai.contractMarginThreshold</c>: a copy of the literal <c>0.45f</c> of <c>AIBehaviourContract.OnTick</c>; only the estimate of FR-11 uses it.</summary>
        public double AiContractMarginThreshold { get; }

        /// <summary><c>display.percentDecimals</c>: decimals of the chances the game shows, 0.</summary>
        public int PercentDecimals { get; }

        /// <summary><c>display.actualPercentDecimals</c>: decimals of the actual probability, 0.</summary>
        public int ActualPercentDecimals { get; }

        /// <summary>
        /// Reads the rules from the merged content object. <paramref name="config"/> may be null or empty (the built-in file could not be read):
        /// the defaults are used then and the reason is in <paramref name="problems"/>. Never throws.
        /// </summary>
        public static ContractRules FromJson(JsonObject config, List<string> problems)
        {
            if (config == null || config.Count == 0)
            {
                Add(problems, FileName + ": the config is empty; the built-in defaults are used");
                return DefaultRules;
            }

            JsonObject risk = Group(config, "risk", problems);
            JsonObject log = Group(config, "log", problems);
            JsonObject ai = Group(config, "ai", problems);
            JsonObject display = Group(config, "display", problems);

            CheckUnknown(config, problems, "schema", "risk", "log", "ai", "display");
            CheckUnknown(risk, problems, "lowActualChance", "useAutomationMarginAsThreshold", "breakFineVsCashRatio");
            CheckUnknown(log, problems, "capacity", "tenderHistoryYears");
            CheckUnknown(ai, problems, "contractMarginThreshold");
            CheckUnknown(display, problems, "percentDecimals", "actualPercentDecimals");

            return new ContractRules(
                ReadNumber(risk, "risk", "lowActualChance", DefaultLowActualChance, 0, 1, false, false, problems),
                ReadBool(risk, "risk", "useAutomationMarginAsThreshold", DefaultUseAutomationMarginAsThreshold, problems),
                ReadNumber(risk, "risk", "breakFineVsCashRatio", DefaultBreakFineVsCashRatio, 0, MaxBreakFineVsCashRatio, false, true, problems),
                ReadInt(log, "log", "capacity", DefaultLogCapacity, MinLogCapacity, MaxLogCapacity, problems),
                ReadInt(log, "log", "tenderHistoryYears", DefaultTenderHistoryYears, MinTenderHistoryYears, MaxTenderHistoryYears, problems),
                ReadNumber(ai, "ai", "contractMarginThreshold", DefaultAiContractMarginThreshold, 0, 1, true, true, problems),
                ReadInt(display, "display", "percentDecimals", DefaultPercentDecimals, 0, MaxDecimals, problems),
                ReadInt(display, "display", "actualPercentDecimals", DefaultActualPercentDecimals, 0, MaxDecimals, problems));
        }

        // ---- reading ----

        private static JsonObject Group(JsonObject config, string name, List<string> problems)
        {
            JsonNode node = config[name];
            if (node == null) return null;   // a missing group is the same as a group with every field missing
            var group = node as JsonObject;
            if (group == null) Add(problems, FileName + ": '" + name + "' is not an object; the defaults of the group are used");
            return group;
        }

        private static void CheckUnknown(JsonObject node, List<string> problems, params string[] known)
        {
            if (node == null) return;
            foreach (KeyValuePair<string, JsonNode> pair in node)
            {
                if (Array.IndexOf(known, pair.Key) < 0) Add(problems, FileName + ": unknown field '" + pair.Key + "' is ignored");
            }
        }

        private static double ReadNumber(
            JsonObject group, string groupName, string key, double fallback,
            double min, double max, bool minInclusive, bool maxInclusive, List<string> problems)
        {
            string path = groupName + "." + key;
            JsonNode node = group == null ? null : group[key];
            if (node == null) return fallback;

            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Number || double.IsNaN(value.NumberValue) || double.IsInfinity(value.NumberValue))
            {
                Add(problems, FileName + ": " + path + " is not a number; the default " + Format(fallback) + " is used");
                return fallback;
            }

            double number = value.NumberValue;
            bool above = minInclusive ? number >= min : number > min;
            bool below = maxInclusive ? number <= max : number < max;
            if (above && below) return number;

            Add(problems, FileName + ": " + path + " = " + Format(number) + " is out of range (" +
                              (minInclusive ? "[" : "(") + Format(min) + ", " + Format(max) + (maxInclusive ? "]" : ")") +
                              "); the default " + Format(fallback) + " is used");
            return fallback;
        }

        private static int ReadInt(JsonObject group, string groupName, string key, int fallback, int min, int max, List<string> problems)
        {
            string path = groupName + "." + key;
            JsonNode node = group == null ? null : group[key];
            if (node == null) return fallback;

            var value = node as JsonValue;
            int number;
            if (value == null || value.Kind != JsonKind.Number || !value.TryGetInt32(out number))
            {
                Add(problems, FileName + ": " + path + " is not a whole number; the default " + fallback.ToString(CultureInfo.InvariantCulture) + " is used");
                return fallback;
            }

            if (number >= min && number <= max) return number;

            Add(problems, FileName + ": " + path + " = " + number.ToString(CultureInfo.InvariantCulture) + " is out of range [" +
                              min.ToString(CultureInfo.InvariantCulture) + ", " + max.ToString(CultureInfo.InvariantCulture) +
                              "]; the default " + fallback.ToString(CultureInfo.InvariantCulture) + " is used");
            return fallback;
        }

        private static bool ReadBool(JsonObject group, string groupName, string key, bool fallback, List<string> problems)
        {
            JsonNode node = group == null ? null : group[key];
            if (node == null) return fallback;

            var value = node as JsonValue;
            if (value != null && value.Kind == JsonKind.Bool) return value.BoolValue;

            Add(problems, FileName + ": " + groupName + "." + key + " is not true or false; the default " + (fallback ? "true" : "false") + " is used");
            return fallback;
        }

        private static string Format(double number)
        {
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void Add(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
