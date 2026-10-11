using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The self-check of the feature at the start of a game (design.md "Самопроверка на старте" and "Error Handling", NFR-1, NFR-2, TSK-024). The core's own
    /// <c>ModSelfCheck</c> looks at every declared patch target of every feature (and the Diagnostics tab of the hub shows it), but it has no place where a feature
    /// adds its own findings, so the feature looks at what it stands on and writes to the log:
    /// <c>[PTMod] [rival-personalities] INFO self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)</c>. Otherwise the line says <c>FAILED</c> and
    /// is followed by one line per problem and by the list of the missing targets; a missing required target or an empty catalog switches the feature off
    /// (<c>ModFeatures.MarkUnavailable</c>), and the game goes on without it. The optional patch on <c>AIBehaviourCreation.AcceptableFailureRate</c> is a warning
    /// only: without it the caution after a public flaw (FR-17) does not work, nothing else is lost.
    /// <para>
    /// What is looked at: the patches of the feature in the registry of the core (the 12 targets of the design table, none on <c>AddCpu</c>), the content (the archetypes
    /// and the default assignments), the event system (the five events of the pack and the five effect types), the three predicates <c>rival.*</c> in the core's
    /// conditions, and a few strings in the table of the language. The rules and the verdict are <see cref="RivalSelfCheckRules"/>'s (pure, with the tests); this
    /// class collects the facts. It only LOOKS, with the one exception of the verdict on a fatal problem; it never throws.
    /// </para>
    /// <para>
    /// When: once per session on <c>ModTicks.GameReady</c> (all features have started, the registries are complete) and when the feature is switched on in a running
    /// game. The event system reads the packs at its own start, which may come after ours: when it is active and does not hold the pack yet, the check waits for
    /// the first game day instead of reporting events that are only late.
    /// </para>
    /// </summary>
    internal static class RivalSelfCheck
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private static readonly string[] SampleKeys =
        {
            "rival.feature.name", "rival.feature.desc", "rival.hub.title", "rival.window.title", "rival.event.priceCut.title", "rival.mail.intel.subject",
        };

        private static bool _ranThisSession;
        private static bool _waitingForEngine;

        /// <summary>The verdict of the last run; null before the first one.</summary>
        internal static RivalSelfCheckReport Last { get; private set; }

        /// <summary>The feature was switched on: in a running game the check runs now, otherwise it waits for <c>GameReady</c>.</summary>
        internal static void OnEnabled(RivalPersonalitiesFeature feature)
        {
            _waitingForEngine = false;
            if (ModTicks.IsGameReady) Run(feature, "enabled");
        }

        /// <summary><c>ModTicks.GameReady</c>: the first of the session runs the check.</summary>
        internal static void OnGameReady(RivalPersonalitiesFeature feature)
        {
            if (_ranThisSession) return;
            Run(feature, "ready");
        }

        /// <summary><c>TickPhase.DayPassed</c>: the check that waited for the event system runs now.</summary>
        internal static void OnDay(RivalPersonalitiesFeature feature)
        {
            if (!_waitingForEngine) return;
            _waitingForEngine = false;
            Run(feature, "day");
        }

        /// <summary>Runs the check, writes the verdict to the log and, when it is fatal, switches the feature off. Never throws.</summary>
        internal static RivalSelfCheckReport Run(RivalPersonalitiesFeature feature, string stage)
        {
            RivalSelfCheckReport report;
            try
            {
                RivalCheckFacts facts = Collect(feature);

                // the event system reads the packs at its own start: if it is active but has not read ours, wait a day for it (stage "ready" only)
                if (stage != "day" && facts.EventSystemActive && facts.RegisteredEvents.Count == 0)
                {
                    _waitingForEngine = true;
                    return null;
                }

                report = RivalSelfCheckRules.Run(facts);
            }
            catch (Exception ex)
            {
                report = new RivalSelfCheckReport();
                report.Fail("the self-check itself failed (" + ex.GetType().Name + ": " + ex.Message + ")", false);
            }

            _ranThisSession = true;
            Last = report;
            Write(report, stage);

            if (report.IsFatal) SwitchOff(report);
            return report;
        }

        // ---- the log ----

        private static void Write(RivalSelfCheckReport report, string stage)
        {
            try
            {
                if (report.IsOk) ModLog.Info(Feature, report.Summary());
                else if (report.IsFatal) ModLog.Error(Feature, report.Summary());
                else ModLog.Warn(Feature, report.Summary());

                string missing = report.MissingLine();
                if (missing != null) ModLog.Warn(Feature, "self-check: " + missing);
                foreach (string problem in report.Problems) ModLog.Warn(Feature, "self-check: " + problem);
                foreach (string warning in report.Warnings) ModLog.Warn(Feature, "self-check: " + warning);
                ModLog.Debug(Feature, "self-check: stage " + stage);
            }
            catch (Exception)
            {
                // the log must never be the reason the feature fails to start
            }
        }

        /// <summary>A fatal verdict: the feature is unavailable until the game restarts (the core calls <c>OnDisabled</c>, which gives the prices and the designers back).</summary>
        private static void SwitchOff(RivalSelfCheckReport report)
        {
            try
            {
                string detail = report.Problems.Count > 0 ? report.Problems[0] : "the self-check found a fatal problem";
                ModFeatures.MarkUnavailable(Feature, "self-check: " + detail);
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "self-check: the feature could not be switched off", ex);
            }
        }

        // ---- the facts ----

        private static RivalCheckFacts Collect(RivalPersonalitiesFeature feature)
        {
            var facts = new RivalCheckFacts();
            CollectPatches(facts);

            if (feature != null)
            {
                ArchetypeCatalog catalog = feature.Catalog;
                facts.ContentLoaded = feature.ContentLoaded;
                facts.CatalogUsable = catalog.IsUsable;
                facts.Archetypes = catalog.All.Count;
                facts.Assignments = catalog.Assignments.Count;
            }

            CollectEvents(facts);
            foreach (string name in ModConditions.KnownTypes) facts.KnownPredicates.Add(name);
            foreach (string key in SampleKeys)
            {
                if (!ModL10n.Has(key)) facts.MissingStrings.Add(key);
            }
            return facts;
        }

        private static void CollectPatches(RivalCheckFacts facts)
        {
            IReadOnlyList<PatchReport> reports = ModHooks.Report;
            if (reports == null) return;

            foreach (PatchReport report in reports)
            {
                if (report.FeatureId != Feature) continue;
                facts.Patches.Add(new RivalPatchLine
                {
                    Target = report.Target,
                    Applied = report.Status == PatchStatus.Applied,
                    Optional = report.Optional,
                    Detail = report.Detail,
                });
            }
        }

        private static void CollectEvents(RivalCheckFacts facts)
        {
            facts.EventSystemActive = EventSystemApi.IsActive;
            if (!facts.EventSystemActive) return;

            foreach (EventDefinition definition in EventRegistry.All)
            {
                if (definition != null && definition.packId == ReactionTags.PackId) facts.RegisteredEvents.Add(definition.id);
            }
            foreach (string type in EventEffects.RegisteredTypes) facts.RegisteredEffects.Add(type);
        }
    }
}
