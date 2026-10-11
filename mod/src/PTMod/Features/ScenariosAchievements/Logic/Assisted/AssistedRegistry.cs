using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The "assisted" flag of the run in play (FR-15; design.md, "Испытания, достижения, assisted"): a run that had help - cheats on, a feature
    /// switched off in the middle of a scenario, or a reason another feature gave - earns its achievements marked "with help" (or none at all
    /// with the setting <c>strictAssisted</c>) and makes no records.
    /// <para>
    /// The flag is sticky: there is no way to lower it. The reasons are short texts without duplicates (<see cref="RunState.MaxReasonLength"/>
    /// at most; a longer text is cut and still raises the flag). The registry is bound to the run by the director (<see cref="Reset"/>); a mark
    /// that comes while no run is bound (another feature is quicker than the start of the run) is kept and applied to the next run that is
    /// bound, and dropped when the run is unbound. Main thread only. The decisions that need no state - who may unlock, which feature
    /// switch counts - are static functions of their arguments, so they are tested without the binding.
    /// </para>
    /// </summary>
    public static class AssistedRegistry
    {
        /// <summary>The run started with cheats on (<c>PlayerInitialData.EnableCheats</c>).</summary>
        public const string ReasonCheats = "cheats.enabled";

        /// <summary>The start of the reason of a feature that was switched off during the run: <c>feature.disabled:&lt;id&gt;</c>.</summary>
        public const string ReasonFeatureDisabledPrefix = "feature.disabled:";

        private static RunState _run;
        private static readonly List<string> Pending = new List<string>();

        /// <summary>The run in play is assisted (or a mark is waiting for the run to start).</summary>
        public static bool IsAssisted
        {
            get { return _run != null ? _run.Assisted : Pending.Count > 0; }
        }

        /// <summary>The reasons of the run in play (a copy); the marks that wait for the run when none is bound.</summary>
        public static IList<string> Reasons
        {
            get { return new List<string>(_run != null ? _run.AssistedReasons : (IList<string>)Pending); }
        }

        /// <summary>
        /// Public API for the other features: raises the assisted flag of the run in play with a reason (<c>cheats.enabled</c>,
        /// <c>feature.disabled:&lt;id&gt;</c>, a text of their own). Returns true when something changed. Never throws.
        /// </summary>
        public static bool Mark(string reason)
        {
            try
            {
                if (_run != null) return _run.MarkAssisted(reason);

                string normalized = RunState.NormalizeReason(reason);
                if (Pending.Contains(normalized) || Pending.Count >= RunState.MaxReasons) return false;
                Pending.Add(normalized);
                return true;
            }
            catch (Exception)
            {
                // A mark is a side channel of another feature: it must never bring that feature down.
                return false;
            }
        }

        /// <summary>
        /// Binds the registry to a run (null: no run). The marks that were waiting go to the run; with no run the waiting marks are dropped.
        /// Called by the director when a run starts or is loaded and when the scene is left.
        /// </summary>
        internal static void Reset(RunState run)
        {
            RunState previous = _run;
            _run = run;
            if (run != null && !ReferenceEquals(run, previous))
            {
                foreach (string reason in Pending) run.MarkAssisted(reason);
            }
            Pending.Clear();
        }

        /// <summary>The reason for a feature that was switched off: <c>feature.disabled:&lt;id&gt;</c>.</summary>
        public static string FeatureDisabledReason(string featureId)
        {
            return ReasonFeatureDisabledPrefix + featureId;
        }

        /// <summary>
        /// The reason to mark when the player switches a feature off (FR-15 item 2), or null when it is no business of the run: the feature must
        /// have been on at the start of the run, and a scenario or a challenge must be going (the scenario itself is a feature of the run, so
        /// switching off <c>scenarios-achievements</c> counts too). Switching a feature on never counts.
        /// </summary>
        public static string ReasonForDisabledFeature(RunState run, string featureId)
        {
            if (run == null || string.IsNullOrEmpty(featureId)) return null;
            if (!run.FeaturesAtStart.Contains(featureId)) return null;
            if (!run.IsScenarioActive && !run.HasActiveChallenge) return null;
            return FeatureDisabledReason(featureId);
        }

        /// <summary>Applies <see cref="ReasonForDisabledFeature"/> to a run: marks it assisted when the switch counts. Returns true when something changed.</summary>
        public static bool MarkFeatureDisabled(RunState run, string featureId)
        {
            string reason = ReasonForDisabledFeature(run, featureId);
            return reason != null && run.MarkAssisted(reason);
        }

        /// <summary>
        /// May the run earn achievements (FR-15 item 7)? An assisted run may, with a mark, unless the setting <c>strictAssisted</c> is on; a clean
        /// run always may.
        /// </summary>
        public static bool CanUnlockAchievements(bool assisted, bool strictAssisted)
        {
            return !(assisted && strictAssisted);
        }

        /// <summary>May the result of a scenario run become a record? Not an assisted one (FR-15 item 6); a custom run is kept out by the profile.</summary>
        public static bool CanMakeRecords(bool assisted)
        {
            return !assisted;
        }
    }
}
