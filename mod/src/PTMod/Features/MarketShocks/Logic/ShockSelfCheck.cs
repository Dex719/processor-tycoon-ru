using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.MarketShocks
{
    /// <summary>How serious a finding of the self-check of the pack is.</summary>
    public enum ShockCheckLevel
    {
        /// <summary>The pack works, a part of it (a display patch, a string in one language) does not.</summary>
        Warning = 0,

        /// <summary>Something the pack stands on is wrong: it will not work as designed.</summary>
        Error = 1,
    }

    /// <summary>One finding of the self-check: where it was found and what is wrong, in English, for the log.</summary>
    public sealed class ShockCheckProblem
    {
        public ShockCheckProblem(ShockCheckLevel level, string area, string message)
        {
            Level = level;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ShockCheckLevel Level { get; }

        /// <summary><c>patches</c>, <c>channels</c>, <c>types</c>, <c>predicates</c>, <c>events</c> or <c>strings</c>.</summary>
        public string Area { get; }

        public string Message { get; }

        public override string ToString()
        {
            return Area + ": " + Message;
        }
    }

    /// <summary>A game method the pack patches, as the self-check names it (<c>CpuMath.MaterialCost</c>).</summary>
    public sealed class ShockPatchTarget
    {
        public ShockPatchTarget(string type, string method, bool optional)
        {
            Type = type;
            Method = method;
            Optional = optional;
        }

        /// <summary>The short name of the game's class.</summary>
        public string Type { get; }

        public string Method { get; }

        /// <summary><c>ModPatch.Optional</c>: the pack works without this patch (a display only).</summary>
        public bool Optional { get; }

        /// <summary><c>Class.Method</c>, the form <c>PatchReport.Target</c> uses.</summary>
        public string Name
        {
            get { return Type + "." + Method; }
        }
    }

    /// <summary>What the game and the core say about one of the patches of the pack. Filled by <c>MarketShocksSelfCheck</c>; the rules read it.</summary>
    public sealed class ShockPatchFact
    {
        public ShockPatchFact(string name)
        {
            Name = name;
        }

        /// <summary><c>Class.Method</c>.</summary>
        public string Name { get; }

        /// <summary>The method exists in the game.</summary>
        public bool TargetFound;

        /// <summary>The core reserves the method for a feature (the "formulas of the feature" of the patch table).</summary>
        public bool Reserved;

        /// <summary>The reservation is a <c>ReservedKind.FeatureFormula</c> (the others are channels and services of the core).</summary>
        public bool ReservedAsFeatureFormula;

        /// <summary>The feature that owns the reservation; <c>market-shocks</c> expected.</summary>
        public string ReservedOwner;

        /// <summary><c>ModHooks.Report</c> has the patch of the feature.</summary>
        public bool Declared;

        /// <summary>The patch is in the game (<c>PatchStatus.Applied</c>).</summary>
        public bool Applied;

        /// <summary>The status the core gives (<c>Failed</c>, <c>Rejected</c>, ...) and its English detail, for the message.</summary>
        public string Status;

        public string Detail;
    }

    /// <summary>
    /// Everything the self-check of the pack looks at, as plain data (TSK-017; FR-1, NFR-4): the patches, the channels of the core, the types and predicates the
    /// feature registered, the events the engine accepted and the strings. Collected from the game by <c>MarketShocksSelfCheck</c>; the tests make it up.
    /// </summary>
    public sealed class ShockCheckFacts
    {
        public readonly List<ShockPatchFact> Patches = new List<ShockPatchFact>();

        /// <summary>The channels to look for in the registry of the core: name and "declared" (<c>ModModifiers.IsDeclared</c>).</summary>
        public readonly List<KeyValuePair<string, bool>> Channels = new List<KeyValuePair<string, bool>>();

        /// <summary>The effect types the engine has handlers for (<c>EventEffects.RegisteredTypes</c>).</summary>
        public readonly List<string> EffectTypes = new List<string>();

        /// <summary>The predicate names <c>ModConditions</c> knows (<c>ModConditions.KnownTypes</c>).</summary>
        public readonly List<string> Predicates = new List<string>();

        /// <summary>The ids of the events of the pack that the engine accepted (<c>EventRegistry.All</c> with <c>packId == market-shocks</c>).</summary>
        public readonly List<string> EventIds = new List<string>();

        /// <summary>The keys of the pack's strings that the tables of Russian lack.</summary>
        public readonly List<string> MissingRu = new List<string>();

        /// <summary>The keys of the pack's strings that the tables of English lack.</summary>
        public readonly List<string> MissingEn = new List<string>();

        /// <summary>The keys of the pack that only one of the two languages has (the other lacks them, whether or not the code names them).</summary>
        public readonly List<string> OnlyInOneLanguage = new List<string>();

        /// <summary><c>ModIds.IsPredicateNameAllowed</c> for the feature; null skips the check.</summary>
        public Func<string, bool> PredicateNameAllowed;

        /// <summary><c>ModIds.IsEventIdAllowed</c> for the feature; null skips the check.</summary>
        public Func<string, bool> EventIdAllowed;
    }

    /// <summary>What the self-check came to: the findings and the one line for the log.</summary>
    public sealed class ShockCheckVerdict
    {
        internal ShockCheckVerdict(IList<ShockCheckProblem> problems, int events, int patches)
        {
            Problems = new List<ShockCheckProblem>(problems);
            Events = events;
            Patches = patches;
        }

        public IList<ShockCheckProblem> Problems { get; }

        /// <summary>Events of the pack the engine has accepted.</summary>
        public int Events { get; }

        /// <summary>Patches that are in order: found, reserved for the feature and applied.</summary>
        public int Patches { get; }

        public bool Ok
        {
            get { return Problems.Count == 0; }
        }

        public bool HasErrors
        {
            get
            {
                foreach (ShockCheckProblem problem in Problems) if (problem.Level == ShockCheckLevel.Error) return true;
                return false;
            }
        }

        /// <summary>
        /// The summary in one line: <c>selfcheck OK: 21 events, 4 patches</c>, or, with findings, <c>selfcheck found 2 problems: patches: ...; strings: ...</c>
        /// (the details are the lines of <see cref="Problems"/>).
        /// </summary>
        public string Line
        {
            get
            {
                if (Ok) return "selfcheck OK: " + Events + " events, " + Patches + " patches";

                var sb = new StringBuilder("selfcheck found ").Append(Problems.Count).Append(Problems.Count == 1 ? " problem" : " problems").Append(": ");
                for (int i = 0; i < Problems.Count; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(Problems[i]);
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// The rules of the self-check of the pack (TSK-017; design.md, "Самопроверка на старте"). Pure: <see cref="Evaluate"/> judges the
    /// <see cref="ShockCheckFacts"/> that <c>MarketShocksSelfCheck</c> reads from the game. It checks that
    /// <list type="bullet">
    /// <item>the four targets of the patches are in the game, reserved by the core as formulas of this feature (<c>FeatureFormula</c>, owner <c>market-shocks</c>) and
    /// applied (an optional patch that is missing is a warning: the pack works without its display);</item>
    /// <item>the channels <c>production.memoryCost</c> and <c>production.waferCost</c> (and every channel the pack writes) are declared by the core;</item>
    /// <item>the three effect types and the two predicates are registered, and the names of the predicates and the ids of the events carry the prefix of the feature;</item>
    /// <item>the engine has accepted all 21 events of <c>shocks.json</c>;</item>
    /// <item>every string key the pack needs exists in <c>ru</c> and in <c>en</c>.</item>
    /// </list>
    /// </summary>
    public static class ShockSelfCheck
    {
        /// <summary>How many patches the pack declares (the number in the summary line).</summary>
        public const int ExpectedPatches = 4;

        private const int ListedMost = 6;

        /// <summary>The four game methods the pack patches, in the order of the patch table (F1, F1, F2, F2).</summary>
        public static readonly IList<ShockPatchTarget> PatchTargets = new List<ShockPatchTarget>
        {
            new ShockPatchTarget("CpuMath", "MaterialCost", false),
            new ShockPatchTarget("ProductionMath", "WaferMaterialCost", false),
            new ShockPatchTarget("ProductionMath", "WaferCost", true),
            new ShockPatchTarget("Memory", "GetDescriptions", true),
        }.AsReadOnly();

        /// <summary>The two channels the patches of the pack read; the core declares them (<c>core.json</c>, owner <c>market-shocks</c>).</summary>
        public static readonly IList<string> RequiredChannels = new List<string> { "production.memoryCost", "production.waferCost" }.AsReadOnly();

        /// <summary>The 21 records of <c>shocks.json</c> in the order of the file.</summary>
        public static readonly IList<string> ExpectedEventIds = new List<string>
        {
            "ms.fab_fire", "ms.cleanroom_contamination", "ms.grid_failure", "ms.litho_breakdown", "ms.earthquake", "ms.photoresist_shortage", "ms.logistics_crunch",
            "ms.dram_spike_warning", "ms.dram_spike", "ms.dram_glut", "ms.wafer_shortage", "ms.energy_crisis", "ms.memory_cycle",
            "ms.recession_warning", "ms.recession", "ms.pc_boom", "ms.server_boom", "ms.embedded_boom", "ms.silicon_cycle",
            "ms.dumping_complaint", "ms.launch_hype",
        }.AsReadOnly();

        /// <summary>The keys of the string tables that the code of the pack builds itself (the records' own keys are read from the records).</summary>
        public static readonly IList<string> CodeKeys = new List<string>
        {
            "ms.feature.name", "ms.feature.desc", "ms.warn.contract_fines", "ms.ui.cache_cost",
            "ms.settings.title", "ms.settings.now", "ms.settings.intensity", "ms.settings.intensity.tooltip", "ms.settings.frequency", "ms.settings.frequency.tooltip",
            "ms.settings.intensity.mild", "ms.settings.intensity.normal", "ms.settings.intensity.harsh", "ms.settings.intensity.chaos",
            "ms.fx.ramp", "ms.fx.wave", "ms.fx.wave.up_first", "ms.fx.wave.down_first", "ms.fx.inventory", "ms.fx.inventory.units", "ms.fx.clamped", "ms.fx.forecast_missed",
        }.AsReadOnly();

        /// <summary>Judges the facts. Never throws; a null or empty collection of facts is a finding like any other.</summary>
        public static ShockCheckVerdict Evaluate(ShockCheckFacts facts)
        {
            var problems = new List<ShockCheckProblem>();
            if (facts == null)
            {
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "selfcheck", "nothing was collected"));
                return new ShockCheckVerdict(problems, 0, 0);
            }

            int patchesInOrder = CheckPatches(facts, problems);
            CheckChannels(facts, problems);
            CheckTypes(facts, problems);
            CheckPredicates(facts, problems);
            CheckEvents(facts, problems);
            CheckStrings(facts, problems);

            return new ShockCheckVerdict(problems, facts.EventIds.Count, patchesInOrder);
        }

        // ---- the patches ----

        private static int CheckPatches(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            int inOrder = 0;
            foreach (ShockPatchTarget target in PatchTargets)
            {
                int before = problems.Count;
                ShockCheckLevel level = target.Optional ? ShockCheckLevel.Warning : ShockCheckLevel.Error;
                ShockPatchFact fact = FindPatch(facts, target.Name);

                if (fact == null)
                {
                    problems.Add(new ShockCheckProblem(level, "patches", target.Name + ": nothing is known about the patch"));
                }
                else if (!fact.TargetFound)
                {
                    problems.Add(new ShockCheckProblem(level, "patches", target.Name + ": the game has no such method" + (target.Optional ? " (the display of the pack is without it)" : string.Empty)));
                }
                else
                {
                    if (!fact.Reserved)
                        problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "patches", target.Name + ": the core does not reserve it as a formula of " + ShockRegistration.FeatureId + ", any feature could patch it"));
                    else if (!fact.ReservedAsFeatureFormula || !string.Equals(fact.ReservedOwner, ShockRegistration.FeatureId, StringComparison.Ordinal))
                        problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "patches", target.Name + ": reserved for '" + (fact.ReservedOwner ?? "?") + "' or not as a feature formula, expected FeatureFormula of " + ShockRegistration.FeatureId));

                    if (!fact.Declared)
                        problems.Add(new ShockCheckProblem(level, "patches", target.Name + ": the patch is not declared by the feature"));
                    else if (!fact.Applied)
                        problems.Add(new ShockCheckProblem(level, "patches", target.Name + ": the patch is " + (fact.Status ?? "not applied") + (string.IsNullOrEmpty(fact.Detail) ? string.Empty : " (" + fact.Detail + ")")));
                }

                if (problems.Count == before) inOrder++;
            }
            return inOrder;
        }

        private static ShockPatchFact FindPatch(ShockCheckFacts facts, string name)
        {
            foreach (ShockPatchFact fact in facts.Patches)
            {
                if (string.Equals(fact.Name, name, StringComparison.Ordinal)) return fact;
            }
            return null;
        }

        // ---- the channels ----

        private static void CheckChannels(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            foreach (string required in RequiredChannels)
            {
                if (!ContainsChannel(facts, required))
                    problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "channels", required + ": not looked at, the pack's patches read it"));
            }

            foreach (KeyValuePair<string, bool> channel in facts.Channels)
            {
                if (!channel.Value)
                    problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "channels", channel.Key + ": not declared in the registry of the core (ModModifiers.IsDeclared)"));
            }
        }

        private static bool ContainsChannel(ShockCheckFacts facts, string name)
        {
            foreach (KeyValuePair<string, bool> channel in facts.Channels)
            {
                if (string.Equals(channel.Key, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ---- the types and the predicates ----

        private static void CheckTypes(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            var missing = new List<string>();
            foreach (string type in ShockRegistration.EffectTypes)
            {
                if (!facts.EffectTypes.Contains(type)) missing.Add(type);
            }

            if (missing.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "types", "effect types the engine has no handler for: " + Listed(missing)));
        }

        private static void CheckPredicates(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            var missing = new List<string>();
            var badNames = new List<string>();
            foreach (string name in ShockRegistration.PredicateNames)
            {
                if (!facts.Predicates.Contains(name)) missing.Add(name);
                if (facts.PredicateNameAllowed != null && !facts.PredicateNameAllowed(name)) badNames.Add(name);
            }

            if (missing.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "predicates", "predicates ModConditions does not know: " + Listed(missing)));
            if (badNames.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "predicates", "names without the prefix of the feature: " + Listed(badNames)));
        }

        // ---- the events ----

        private static void CheckEvents(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            var missing = new List<string>();
            foreach (string id in ExpectedEventIds)
            {
                if (!facts.EventIds.Contains(id)) missing.Add(id);
            }

            if (facts.EventIds.Count != ShockRegistration.ExpectedEvents || missing.Count > 0)
            {
                string detail = missing.Count > 0 ? "; missing: " + Listed(missing) : string.Empty;
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "events",
                    facts.EventIds.Count + " of " + ShockRegistration.ExpectedEvents + " events of the pack are loaded" + detail + " (see the content report of shocks.json)"));
            }

            if (facts.EventIdAllowed != null)
            {
                var badIds = new List<string>();
                foreach (string id in facts.EventIds)
                {
                    if (!facts.EventIdAllowed(id)) badIds.Add(id);
                }

                if (badIds.Count > 0)
                    problems.Add(new ShockCheckProblem(ShockCheckLevel.Error, "events", "ids without the prefix of the feature: " + Listed(badIds)));
            }
        }

        // ---- the strings ----

        private static void CheckStrings(ShockCheckFacts facts, List<ShockCheckProblem> problems)
        {
            if (facts.MissingRu.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Warning, "strings", "ru lacks " + facts.MissingRu.Count + " keys: " + Listed(facts.MissingRu)));
            if (facts.MissingEn.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Warning, "strings", "en lacks " + facts.MissingEn.Count + " keys: " + Listed(facts.MissingEn)));
            if (facts.OnlyInOneLanguage.Count > 0)
                problems.Add(new ShockCheckProblem(ShockCheckLevel.Warning, "strings", "keys of one language only: " + Listed(facts.OnlyInOneLanguage)));
        }

        // ---- the keys the pack needs ----

        /// <summary>
        /// The keys the pack needs from the tables: those the code builds (<see cref="CodeKeys"/>), the name of every channel its ramps and waves write
        /// (<c>ms.channel.&lt;channel&gt;</c>) and the keys of the records that the engine accepted. Sorted, without repeats.
        /// </summary>
        public static IList<string> RequiredKeys(IEnumerable<string> recordKeys, IEnumerable<string> waveAndRampChannels)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string key in CodeKeys) keys.Add(key);
            if (recordKeys != null)
            {
                foreach (string key in recordKeys) if (!string.IsNullOrEmpty(key)) keys.Add(key);
            }
            if (waveAndRampChannels != null)
            {
                foreach (string channel in waveAndRampChannels) if (!string.IsNullOrEmpty(channel)) keys.Add("ms.channel." + channel);
            }
            return new List<string>(keys);
        }

        private static string Listed(IList<string> items)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < ListedMost; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(items[i]);
            }
            if (items.Count > ListedMost) sb.Append(" (+").Append(items.Count - ListedMost).Append(" more)");
            return sb.ToString();
        }
    }
}
