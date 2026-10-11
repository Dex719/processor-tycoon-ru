using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>One patch of the feature as the registry of the core reports it: the game method it targets and whether it is in the game.</summary>
    public struct RivalPatchLine
    {
        /// <summary><c>Class.Method</c> of the target, as <c>ModHooks.Report</c> names it (the type of the declaration, not the alias of the source).</summary>
        public string Target;

        /// <summary>The patch is applied (not declared only, not failed, not rejected).</summary>
        public bool Applied;

        /// <summary>The feature goes on without it when it cannot be applied.</summary>
        public bool Optional;

        /// <summary>Why the registry says the patch is not in the game (English); null when it is.</summary>
        public string Detail;
    }

    /// <summary>
    /// What the game side saw when it looked at the feature (design.md "Самопроверка на старте", TSK-024): plain facts, no game types, so the rules can be
    /// tested. The lists are filled by <c>Diagnostics/RivalSelfCheck</c> from the registries of the core and the event system.
    /// </summary>
    public sealed class RivalCheckFacts
    {
        /// <summary>The patches the feature declared, with their state (<c>ModHooks.Report</c> filtered to the feature).</summary>
        public readonly List<RivalPatchLine> Patches = new List<RivalPatchLine>();

        /// <summary>Archetypes and default assignments the content gave (<c>ArchetypeCatalog</c>).</summary>
        public int Archetypes;

        public int Assignments;

        /// <summary>The catalog has at least one archetype to give characters from.</summary>
        public bool CatalogUsable;

        /// <summary>The content has been read (the feature is enabled).</summary>
        public bool ContentLoaded = true;

        /// <summary>The event system works now, so its registries can be asked.</summary>
        public bool EventSystemActive;

        /// <summary>Ids of the events of the pack that the event system holds (the engine has read the pack).</summary>
        public readonly List<string> RegisteredEvents = new List<string>();

        /// <summary>Types of effects the event system knows.</summary>
        public readonly List<string> RegisteredEffects = new List<string>();

        /// <summary>Names of the conditions the core knows (<c>ModConditions.KnownTypes</c>).</summary>
        public readonly List<string> KnownPredicates = new List<string>();

        /// <summary>The sample keys of the strings that are not in the table of the current language.</summary>
        public readonly List<string> MissingStrings = new List<string>();
    }

    /// <summary>
    /// The verdict of the self-check (design.md "Самопроверка на старте", NFR-1, NFR-2, TSK-024): what was counted, what is wrong and whether the feature
    /// must stop. <see cref="Summary"/> is the line of the log: <c>self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)</c>. A problem is
    /// something that is wrong (a target that is not in the game, a patch on a method the feature must not touch, no archetype, an event the engine does not
    /// have); a warning is something missing that costs only a part (the optional patch, the event system is off). The feature does not stop on a problem
    /// unless <see cref="IsFatal"/>: the game goes on without it.
    /// </summary>
    public sealed class RivalSelfCheckReport
    {
        /// <summary>How many of the patch targets of the design are applied (12 when all are; 11 without the optional <c>AcceptableFailureRate</c>).</summary>
        public int Patches;

        public int Archetypes;
        public int Assignments;

        /// <summary>How many of the five events are known to the event system, or declared by the pack when the event system is off (it is then a warning).</summary>
        public int Events;

        /// <summary>The targets of the design that are not applied, <c>Class.Method</c>, required ones first (for the list in the log).</summary>
        public readonly List<string> MissingTargets = new List<string>();

        /// <summary>What is wrong, in English, one line each.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>What is missing but costs only a part of the feature, in English, one line each.</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>The feature cannot work: a required target is not in the game, or there is no archetype to give characters from. The game side switches it off.</summary>
        public bool IsFatal;

        /// <summary>Nothing is wrong (warnings do not count).</summary>
        public bool IsOk
        {
            get { return Problems.Count == 0; }
        }

        public void Fail(string problem, bool fatal)
        {
            if (!string.IsNullOrEmpty(problem)) Problems.Add(problem);
            if (fatal) IsFatal = true;
        }

        public void Warn(string warning)
        {
            if (!string.IsNullOrEmpty(warning)) Warnings.Add(warning);
        }

        /// <summary><c>self-check OK (12 patches, 7 archetypes, 13 assignments, 5 events)</c>; <c>FAILED</c> instead of <c>OK</c> when something is wrong.</summary>
        public string Summary()
        {
            var sb = new StringBuilder("self-check ");
            sb.Append(IsOk ? "OK" : "FAILED").Append(" (");
            sb.Append(Count(Patches, "patch", "patches")).Append(", ");
            sb.Append(Count(Archetypes, "archetype", "archetypes")).Append(", ");
            sb.Append(Count(Assignments, "assignment", "assignments")).Append(", ");
            sb.Append(Count(Events, "event", "events")).Append(')');
            if (IsFatal) sb.Append("; the feature is switched off");
            return sb.ToString();
        }

        /// <summary>The line about the targets that are missing (<c>missing targets: A.B, C.D</c>), or null when none is.</summary>
        public string MissingLine()
        {
            return MissingTargets.Count == 0 ? null : "missing targets: " + string.Join(", ", MissingTargets.ToArray());
        }

        private static string Count(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }
    }

    /// <summary>
    /// The rules of the self-check that need no game (TSK-024): which patches the design has and which of them are required, which methods the feature must
    /// never patch, what the event system must hold, what the content must give. The game side (<c>Diagnostics/RivalSelfCheck</c>) reads the registries and
    /// hands the facts in. The check only LOOKS: it never changes anything; the one consequence, switching the feature off on <see cref="RivalSelfCheckReport.IsFatal"/>,
    /// is the game side's. Never throws.
    /// </summary>
    public static class RivalSelfCheckRules
    {
        /// <summary>
        /// The targets of the design table (design.md "Патчи Harmony"), <c>Class.Method</c> as the registry names them: 11 required ones. A required target that is
        /// not in the game makes the feature <c>Unavailable</c> (the core does it when it applies the patches; the self-check says it again by name).
        /// </summary>
        public static readonly string[] RequiredTargets =
        {
            "AICompany.Initialize",
            "AIBehaviourController.ReformCyrux",
            "AIBehaviourCreation.OnTick",
            "AIBehaviourResearch.GetMaxFunding",
            "AIBehaviourMarket.OnTick",
            "Company.AddTechnology",
            "ContractManager.PromoteContractToActive",
            "BusinessContractManager.BreakContract",
            "ContractNegotiationWindow.UpdateScores",
            "AIMath.GetProviderScores",
            "AIMath.GetClientScores",
        };

        /// <summary>The only optional target: without it the caution after a public flaw (FR-17) does not work, nothing else is lost.</summary>
        public const string OptionalTarget = "AIBehaviourCreation.AcceptableFailureRate";

        /// <summary>
        /// The methods the feature must never patch (design.md D-table): the release of a CPU is the core's event <c>ModEvents.CpuReleased</c>, not a patch of
        /// these.
        /// </summary>
        public static readonly string[] ForbiddenTargets = { "Company.AddCpu", "AICompany.AddCpu" };

        /// <summary>The number of targets in the design table: 12.</summary>
        public static int TargetCount
        {
            get { return RequiredTargets.Length + 1; }
        }

        /// <summary>The target is one the design has (required or optional).</summary>
        public static bool IsDesignTarget(string target)
        {
            return IsRequired(target) || string.Equals(target, OptionalTarget, StringComparison.Ordinal);
        }

        /// <summary>The target is one of the 11 required ones.</summary>
        public static bool IsRequired(string target)
        {
            return Contains(RequiredTargets, target);
        }

        /// <summary>The target is a method the feature must not patch.</summary>
        public static bool IsForbidden(string target)
        {
            return Contains(ForbiddenTargets, target);
        }

        /// <summary>Runs every rule over the facts and returns the verdict. A null fact set is a problem of its own.</summary>
        public static RivalSelfCheckReport Run(RivalCheckFacts facts)
        {
            var report = new RivalSelfCheckReport();
            if (facts == null)
            {
                report.Fail("the facts of the self-check are missing", false);
                return report;
            }

            CheckPatches(facts, report);
            CheckContent(facts, report);
            CheckEvents(facts, report);
            CheckPredicates(facts, report);
            CheckStrings(facts, report);
            return report;
        }

        // ---- the parts ----

        private static void CheckPatches(RivalCheckFacts facts, RivalSelfCheckReport report)
        {
            var applied = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var unapplied = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (RivalPatchLine line in facts.Patches)
            {
                if (string.IsNullOrEmpty(line.Target)) continue;

                if (IsForbidden(line.Target))
                {
                    report.Fail("the feature patches " + line.Target + ", which belongs to the core (use ModEvents.CpuReleased)", false);
                    continue;
                }
                if (!IsDesignTarget(line.Target))
                {
                    report.Warn("the patch on " + line.Target + " is not in the design table");
                    continue;
                }

                declared.Add(line.Target);
                if (line.Applied) applied.Add(line.Target);
                else if (!unapplied.ContainsKey(line.Target)) unapplied[line.Target] = line.Detail;
            }

            // a target with two patches (prefix and postfix, prefix and finalizer) is in the game only when all of them are
            foreach (string target in unapplied.Keys) applied.Remove(target);

            foreach (string target in RequiredTargets)
            {
                if (applied.Contains(target)) continue;
                report.MissingTargets.Add(target);
                string why = !declared.Contains(target) ? "is not declared by the feature" : "is not applied" + Because(unapplied[target]);
                report.Fail("the patch on " + target + " " + why, true);
            }

            if (!applied.Contains(OptionalTarget))
            {
                report.MissingTargets.Add(OptionalTarget);
                string why = !declared.Contains(OptionalTarget) ? "is not declared by the feature" : "is not applied" + Because(unapplied[OptionalTarget]);
                report.Warn("the optional patch on " + OptionalTarget + " " + why + ": AI companies are not more careful after a public flaw");
            }

            report.Patches = applied.Count;
        }

        private static void CheckContent(RivalCheckFacts facts, RivalSelfCheckReport report)
        {
            report.Archetypes = facts.Archetypes;
            report.Assignments = facts.Assignments;

            if (!facts.ContentLoaded)
            {
                report.Fail("the content has not been read", false);
                return;
            }
            if (!facts.CatalogUsable || facts.Archetypes < 1)
            {
                report.Fail("no usable archetype: the feature has no characters to give", true);
                return;
            }
            if (facts.Assignments < 1) report.Warn("assignments.json gave no assignment: every company gets the fallback archetype");
        }

        private static void CheckEvents(RivalCheckFacts facts, RivalSelfCheckReport report)
        {
            if (!facts.EventSystemActive)
            {
                // nothing is planned without the event system (GateResult.NoEventSystem); the rest of the feature works
                report.Events = ReactionTags.EventIds.Length;
                report.Warn("the event system is not active: the " + ReactionTags.EventIds.Length.ToString(CultureInfo.InvariantCulture)
                            + " events of the pack are not checked and no reaction is planned");
                return;
            }

            int found = 0;
            foreach (string id in ReactionTags.EventIds)
            {
                if (facts.RegisteredEvents.Contains(id)) found++;
                else report.Fail("the event system does not know the event " + id + " (was events.json rejected?)", false);
            }
            report.Events = found;

            foreach (string type in ReactionTags.EffectTypes)
            {
                if (!facts.RegisteredEffects.Contains(type)) report.Fail("the event system does not know the effect type " + type, false);
            }
        }

        private static void CheckPredicates(RivalCheckFacts facts, RivalSelfCheckReport report)
        {
            foreach (string name in RivalPredicateNames.All)
            {
                if (!facts.KnownPredicates.Contains(name)) report.Warn("the condition " + name + " is not known to the core (scenarios cannot use it)");
            }
        }

        private static void CheckStrings(RivalCheckFacts facts, RivalSelfCheckReport report)
        {
            foreach (string key in facts.MissingStrings)
            {
                report.Warn("the string '" + key + "' is not in the table of the language (the strings of the feature are not loaded?)");
            }
        }

        private static string Because(string detail)
        {
            return string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")";
        }

        private static bool Contains(string[] list, string value)
        {
            if (value == null) return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
