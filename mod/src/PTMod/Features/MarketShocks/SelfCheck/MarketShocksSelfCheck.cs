using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Features.EventSystem;
using GameCpuMath = ProcessorTycoon.Hardware.Math.CpuMath;
using GameHardwareOption = ProcessorTycoon.Hardware.IHardwareOption;
using GameMemory = ProcessorTycoon.Hardware.Memory;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using GameProductionMath = ProcessorTycoon.Production.Math.ProductionMath;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The feature's contribution to the self-check of the mod (TSK-017; FR-1, NFR-4; design.md, "Самопроверка на старте"). It looks at what the pack stands on,
    /// collects the answers into <see cref="ShockCheckFacts"/> (the rules are <see cref="ShockSelfCheck"/>'s, pure and tested) and writes the verdict to the log under
    /// the feature's id: the one line <c>selfcheck OK: 21 events, 4 patches</c>, or one line per finding and the summary after them.
    /// <list type="bullet">
    /// <item><b>When.</b> After the engine has read the pack: the feature's <c>GameReady</c> handler calls <see cref="RunAndLog"/> when the number of events is known
    /// (<c>MarketShocksFeature.ReportPack</c>). The core's own self-check (<c>ModSelfCheck</c>) runs earlier and is not told about this one; the findings are in the log.</item>
    /// <item><b>What.</b> The four patch targets: in the game, reserved by the core as <c>FeatureFormula</c> of <c>market-shocks</c> (<c>ModHooks.IsReserved</c>), applied
    /// (<c>ModHooks.Report</c>); the channels <c>production.memoryCost</c> and <c>production.waferCost</c> and every channel the pack's records write
    /// (<c>ModModifiers.IsDeclared</c>); the effect types of the engine (<c>EventEffects.RegisteredTypes</c>) and the predicates (<c>ModConditions.KnownTypes</c>);
    /// the names with the prefix <c>ms</c> (<c>ModIds</c>); the events the engine accepted (<c>EventRegistry.All</c>); the keys <c>ms.*</c> in <c>ru</c> and <c>en</c>
    /// (<c>ModL10n.Keys</c>).</item>
    /// <item><b>Safe.</b> Every group of questions is asked inside its own try/catch: a question that cannot be asked is a line in the log and a gap in the facts (which the
    /// rules then report), never an exception. The check only reads.</item>
    /// </list>
    /// </summary>
    internal static class MarketShocksSelfCheck
    {
        private const string FeatureId = ShockRegistration.FeatureId;
        private const string Site = "selfcheck";

        private const BindingFlags AnyMethod = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>The verdict of the last run; null before the first one.</summary>
        public static ShockCheckVerdict Last { get; private set; }

        /// <summary>Runs the check and writes the verdict to the log. Never throws.</summary>
        public static void RunAndLog()
        {
            ModHooks.Guard(FeatureId, Site, () =>
            {
                ShockCheckVerdict verdict = Run();
                Last = verdict;
                Write(verdict);
            });
        }

        /// <summary>Collects the facts and judges them.</summary>
        public static ShockCheckVerdict Run()
        {
            return ShockSelfCheck.Evaluate(Collect());
        }

        private static void Write(ShockCheckVerdict verdict)
        {
            if (verdict.Ok)
            {
                ModLog.Info(FeatureId, verdict.Line);
                return;
            }

            foreach (ShockCheckProblem problem in verdict.Problems)
            {
                if (problem.Level == ShockCheckLevel.Error) ModLog.Error(FeatureId, "selfcheck: " + problem);
                else ModLog.Warn(FeatureId, "selfcheck: " + problem);
            }

            string line = verdict.Line;
            if (verdict.HasErrors) ModLog.Error(FeatureId, line);
            else ModLog.Warn(FeatureId, line);
        }

        // ---- the facts ----

        private static ShockCheckFacts Collect()
        {
            var facts = new ShockCheckFacts();
            var definitions = new List<EventDefinition>();

            Ask("events", () => CollectEvents(facts, definitions));
            Ask("patches", () => CollectPatches(facts));
            Ask("channels", () => CollectChannels(facts, definitions));
            Ask("types", () => facts.EffectTypes.AddRange(EventEffects.RegisteredTypes));
            Ask("predicates", () => facts.Predicates.AddRange(ModConditions.KnownTypes));
            Ask("names", () =>
            {
                facts.PredicateNameAllowed = name => ModIds.IsPredicateNameAllowed(FeatureId, name);
                facts.EventIdAllowed = id => ModIds.IsEventIdAllowed(FeatureId, id);
            });
            Ask("strings", () => CollectStrings(facts, definitions));
            return facts;
        }

        private static void Ask(string area, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                ModLog.Warn(FeatureId, "selfcheck: could not look at " + area + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void CollectEvents(ShockCheckFacts facts, List<EventDefinition> definitions)
        {
            foreach (EventDefinition definition in EventRegistry.All)
            {
                if (definition == null || definition.packId != ShockPack.PackId) continue;
                definitions.Add(definition);
                facts.EventIds.Add(definition.id);
            }
        }

        // ---- the patches ----

        private static void CollectPatches(ShockCheckFacts facts)
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            foreach (ShockPatchTarget target in ShockSelfCheck.PatchTargets)
            {
                var fact = new ShockPatchFact(target.Name);
                MethodBase method = Resolve(target);
                fact.TargetFound = method != null;

                ReservedTarget reserved;
                if (method != null && ModHooks.IsReserved(method, out reserved) && reserved != null)
                {
                    fact.Reserved = true;
                    fact.ReservedAsFeatureFormula = reserved.Kind == ReservedKind.FeatureFormula;
                    fact.ReservedOwner = reserved.OwnerFeature;
                }

                PatchReport report = ReportOf(reports, target.Name);
                if (report != null)
                {
                    fact.Declared = true;
                    fact.Applied = report.Status == PatchStatus.Applied;
                    fact.Status = report.Status.ToString();
                    fact.Detail = report.Detail;
                }

                facts.Patches.Add(fact);
            }
        }

        private static PatchReport ReportOf(IReadOnlyList<PatchReport> reports, string target)
        {
            if (reports == null) return null;
            foreach (PatchReport report in reports)
            {
                if (report.FeatureId == FeatureId && string.Equals(report.Target, target, StringComparison.Ordinal)) return report;
            }
            return null;
        }

        /// <summary>The game method of a target, found by its exact parameter types (the same ones the patch classes declare); null when the game has none.</summary>
        private static MethodBase Resolve(ShockPatchTarget target)
        {
            switch (target.Name)
            {
                case "CpuMath.MaterialCost":
                    return Method(typeof(GameCpuMath), "MaterialCost", typeof(GamePackage), typeof(GameMemory), typeof(GameProcessNode), typeof(int), typeof(int), typeof(int), typeof(float));
                case "ProductionMath.WaferMaterialCost":
                    return Method(typeof(GameProductionMath), "WaferMaterialCost", typeof(float));
                case "ProductionMath.WaferCost":
                    return Method(typeof(GameProductionMath), "WaferCost", typeof(ICompany), typeof(float), typeof(float));
                case "Memory.GetDescriptions":
                    return Method(typeof(GameMemory), "GetDescriptions", typeof(ICompany), typeof(GameHardwareOption));
                default:
                    return null;
            }
        }

        private static MethodBase Method(Type type, string name, params Type[] arguments)
        {
            return type.GetMethod(name, AnyMethod, null, arguments, null);
        }

        // ---- the channels ----

        private static void CollectChannels(ShockCheckFacts facts, List<EventDefinition> definitions)
        {
            var names = new List<string>(ShockSelfCheck.RequiredChannels);
            foreach (string channel in ChannelsWritten(definitions))
            {
                if (!names.Contains(channel)) names.Add(channel);
            }

            foreach (string name in names) facts.Channels.Add(new KeyValuePair<string, bool>(name, ModModifiers.IsDeclared(name)));
        }

        /// <summary>The channels the loaded records write with <c>ModifierRamp</c>, <c>ModifierWave</c> and <c>ModifierTimed</c> (<c>FactoryOutput</c> writes the standard output channel).</summary>
        private static IEnumerable<string> ChannelsWritten(List<EventDefinition> definitions)
        {
            foreach (EventDefinition definition in definitions)
            {
                foreach (EventEffect effect in EffectsOf(definition))
                {
                    if (effect == null || effect.@params == null) continue;
                    if (effect.type != "ModifierRamp" && effect.type != "ModifierWave" && effect.type != "ModifierTimed") continue;

                    string channel = effect.@params.GetString("channel", null);
                    if (!string.IsNullOrEmpty(channel)) yield return channel;
                }
            }
        }

        private static IEnumerable<EventEffect> EffectsOf(EventDefinition definition)
        {
            if (definition.effects != null)
            {
                foreach (EventEffect effect in definition.effects) yield return effect;
            }

            if (definition.choices == null) yield break;
            foreach (EventChoice choice in definition.choices)
            {
                if (choice == null || choice.effects == null) continue;
                foreach (EventEffect effect in choice.effects) yield return effect;
            }
        }

        // ---- the strings ----

        private static void CollectStrings(ShockCheckFacts facts, List<EventDefinition> definitions)
        {
            var recordKeys = new List<string>();
            var rampAndWaveChannels = new List<string>();
            foreach (EventDefinition definition in definitions)
            {
                AddKey(recordKeys, definition.title);
                AddKey(recordKeys, definition.text);
                AddKey(recordKeys, definition.cause);
                if (definition.choices != null)
                {
                    foreach (EventChoice choice in definition.choices)
                    {
                        if (choice != null && !string.IsNullOrEmpty(choice.textKey)) recordKeys.Add(choice.textKey);
                    }
                }

                foreach (EventEffect effect in EffectsOf(definition))
                {
                    if (effect == null || effect.@params == null) continue;
                    if (effect.type != "ModifierRamp" && effect.type != "ModifierWave") continue;
                    string channel = effect.@params.GetString("channel", null);
                    if (!string.IsNullOrEmpty(channel) && !rampAndWaveChannels.Contains(channel)) rampAndWaveChannels.Add(channel);
                }
            }

            var ru = new HashSet<string>(ModL10n.Keys("ru"), StringComparer.Ordinal);
            var en = new HashSet<string>(ModL10n.Keys("en"), StringComparer.Ordinal);

            foreach (string key in ShockSelfCheck.RequiredKeys(recordKeys, rampAndWaveChannels))
            {
                if (!ru.Contains(key)) facts.MissingRu.Add(key);
                if (!en.Contains(key)) facts.MissingEn.Add(key);
            }

            // Keys of the pack (the prefix ms.) that only one language has and that are not already reported as missing above.
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in facts.MissingRu) known.Add(key);
            foreach (string key in facts.MissingEn) known.Add(key);
            var lonely = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string key in ru) if (key.StartsWith("ms.", StringComparison.Ordinal) && !en.Contains(key) && !known.Contains(key)) lonely.Add(key);
            foreach (string key in en) if (key.StartsWith("ms.", StringComparison.Ordinal) && !ru.Contains(key) && !known.Contains(key)) lonely.Add(key);
            facts.OnlyInOneLanguage.AddRange(lonely);
        }

        private static void AddKey(List<string> keys, LocText text)
        {
            if (text != null && !string.IsNullOrEmpty(text.Key)) keys.Add(text.Key);
        }
    }
}
