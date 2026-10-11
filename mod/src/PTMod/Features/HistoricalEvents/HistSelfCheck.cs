using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Features.EventSystem;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameCompanyEventHandler = ProcessorTycoon.CompanySystem.CompanyEventHandler;
using GameLicenseHelper = ProcessorTycoon.LicenseHelper;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The feature's contribution to the self-check of the mod (TSK-024; FR-17, NFR-2; design.md, "Самопроверка на старте"). It looks at what the feature stands on,
    /// collects the answers into <see cref="HistCheckFacts"/> (the rules are <see cref="HistSelfCheckRules"/>'s, pure and tested) and writes the verdict to the log under the
    /// feature's id: one line <c>selfcheck OK: ...</c>, or one line per finding and the summary after them.
    /// <list type="bullet">
    /// <item><b>When.</b> Once per session, as soon as the calendar is read and the event system has read its packs (<see cref="HistoricalEventsFeature.RunSelfCheckOnce"/>,
    /// at <c>GameReady</c> or on the first day when the packs come later). The core's own self-check runs earlier and is not told about this one; the findings are in the log.</item>
    /// <item><b>What.</b> The targets in the game (the spin-off method, the events of the licence and of the bankruptcies); the pack and the events the calendar fires
    /// (<c>EventRegistry.All</c>); the eight effect types (<c>EventEffects.RegisteredTypes</c>) and the five predicates (<c>ModConditions.KnownTypes</c>); the channels
    /// <c>market.rating.cpu</c> (declared for a CPU and a segment), <c>acquisition.price</c> and <c>gpu.demand</c> (<c>ModModifiers.SpecOf</c>); the calendar (25 entries or
    /// more); the prefix <c>hist.</c> of the names (<c>ModIds</c>); the patches of the feature (none on <c>AddCpu</c> or <c>NextMonth</c>); the texts of the pack.</item>
    /// <item><b>What it does.</b> A failed <c>market.rating.cpu</c> switches the categories <c>export</c> and <c>race</c> off for the session (<see cref="HistSettings.Blocks"/>): their
    /// entries get the status <c>Disabled</c> and the rating provider answers 1. Nothing else is changed.</item>
    /// <item><b>Safe.</b> Every group of questions is asked inside its own try/catch: a question that cannot be asked is a line in the log and a gap in the facts (which the
    /// rules then report), never an exception. The check only reads.</item>
    /// </list>
    /// </summary>
    internal static class HistSelfCheck
    {
        private const string FeatureId = HistIds.Feature;
        private const string Site = "selfcheck";

        private const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>The verdict of the last run; null before the first one.</summary>
        internal static HistCheckVerdict Last { get; private set; }

        /// <summary>Runs the check, switches off what failed and writes the verdict to the log. Never throws.</summary>
        internal static void RunAndLog(HistoricalEventsFeature feature)
        {
            ModHooks.Guard(FeatureId, Site, () =>
            {
                HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(Collect(feature));
                Last = verdict;
                Apply(verdict);
                Write(verdict);
            });
        }

        /// <summary>The rule "a failed channel disables its categories": the categories the verdict names are blocked for the session.</summary>
        internal static void Apply(HistCheckVerdict verdict)
        {
            if (verdict == null) return;

            foreach (string category in verdict.DisabledCategories)
            {
                HistSettings.Blocks.Block(category, "selfcheck: " + HistSelfCheckRules.RatingChannel + " is not usable");
                ModLog.Error(FeatureId, "selfcheck: the category " + category + " is disabled because " + HistSelfCheckRules.RatingChannel + " is not usable; its entries get the status Disabled");
            }
        }

        private static void Write(HistCheckVerdict verdict)
        {
            if (verdict.Ok)
            {
                ModLog.Info(FeatureId, verdict.Line);
                return;
            }

            foreach (HistCheckProblem problem in verdict.Problems)
            {
                if (problem.Level == HistCheckLevel.Error) ModLog.Error(FeatureId, "selfcheck: " + problem);
                else ModLog.Warn(FeatureId, "selfcheck: " + problem);
            }

            if (verdict.HasErrors) ModLog.Error(FeatureId, verdict.Line);
            else ModLog.Warn(FeatureId, verdict.Line);
        }

        // ---- the facts ----

        private static HistCheckFacts Collect(HistoricalEventsFeature feature)
        {
            var facts = new HistCheckFacts();
            var definitions = new List<EventDefinition>();

            Ask("targets", () => CollectTargets(facts));
            Ask("calendar", () => CollectCalendar(feature, facts));
            Ask("pack", () => CollectPack(facts, definitions));
            Ask("types", () => facts.EffectTypes.AddRange(EventEffects.RegisteredTypes));
            Ask("predicates", () => facts.Predicates.AddRange(ModConditions.KnownTypes));
            Ask("channels", () => CollectChannels(facts));
            Ask("patches", () => CollectPatches(facts));
            Ask("names", () =>
            {
                facts.EventIdAllowed = id => ModIds.IsEventIdAllowed(FeatureId, id);
                facts.PredicateNameAllowed = name => ModIds.IsPredicateNameAllowed(FeatureId, name);
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

        private static void CollectTargets(HistCheckFacts facts)
        {
            facts.SpinOffMethodFound = typeof(GameCompanyEventHandler).GetMethod("TriggerSolarFoundriesSpinOff", AnyMember) != null;
            facts.LicenseEventFound = typeof(GameLicenseHelper).GetEvent("OnX86LicenseRevoked", AnyMember) != null;
            facts.BankruptcyEventFound = typeof(GameBankruptcyHandler).GetEvent("OnCompanyWentBankrupt", AnyMember) != null;
        }

        private static void CollectCalendar(HistoricalEventsFeature feature, HistCheckFacts facts)
        {
            facts.CalendarLoaded = feature.CalendarLoaded;
            facts.ValidEntries = feature.Calendar.Count;

            foreach (CalendarEntry entry in feature.Calendar)
            {
                foreach (StageSpec stage in entry.Stages)
                {
                    if (stage == null || stage.Fires == null) continue;
                    foreach (FireSpec fire in stage.Fires) AddEvent(facts, fire);
                }
                if (entry.FallbackFires == null) continue;
                foreach (FireSpec fire in entry.FallbackFires) AddEvent(facts, fire);
            }
        }

        private static void AddEvent(HistCheckFacts facts, FireSpec fire)
        {
            if (fire != null && !string.IsNullOrEmpty(fire.EventId) && !facts.CalendarEventIds.Contains(fire.EventId)) facts.CalendarEventIds.Add(fire.EventId);
        }

        private static void CollectPack(HistCheckFacts facts, List<EventDefinition> definitions)
        {
            foreach (EventDefinition definition in EventRegistry.All)
            {
                if (definition == null || definition.packId != HistIds.PackId) continue;
                definitions.Add(definition);
                facts.PackEventIds.Add(definition.id);
            }
        }

        private static void CollectChannels(HistCheckFacts facts)
        {
            foreach (string name in HistSelfCheckRules.RequiredChannels)
            {
                var fact = new HistChannelFact(name) { Declared = ModModifiers.IsDeclared(name) };
                ChannelSpec spec = fact.Declared ? ModModifiers.SpecOf(name) : null;
                if (spec != null)
                {
                    fact.PerCpu = spec.PerCpu;
                    fact.PerSegment = spec.PerSegment;
                }
                facts.Channels.Add(fact);
            }
        }

        private static void CollectPatches(HistCheckFacts facts)
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            if (reports == null) return;

            foreach (PatchReport report in reports)
            {
                if (report.FeatureId == FeatureId) facts.OwnPatchTargets.Add(report.Target);
            }
        }

        private static void CollectStrings(HistCheckFacts facts, List<EventDefinition> definitions)
        {
            var keys = new List<string>();
            foreach (EventDefinition definition in definitions)
            {
                AddKey(keys, definition.title);
                AddKey(keys, definition.text);
                AddKey(keys, definition.cause);
                if (definition.choices == null) continue;
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && !string.IsNullOrEmpty(choice.textKey)) keys.Add(choice.textKey);
                }
            }

            var ru = new HashSet<string>(ModL10n.Keys("ru"), StringComparer.Ordinal);
            var en = new HashSet<string>(ModL10n.Keys("en"), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (!seen.Add(key)) continue;
                if (!ru.Contains(key)) facts.MissingRu.Add(key);
                if (!en.Contains(key)) facts.MissingEn.Add(key);
            }
        }

        private static void AddKey(List<string> keys, LocText text)
        {
            if (text != null && !string.IsNullOrEmpty(text.Key)) keys.Add(text.Key);
        }
    }
}
