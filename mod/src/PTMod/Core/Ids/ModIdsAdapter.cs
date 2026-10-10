using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;

namespace PTMod.Core
{
    /// <summary>
    /// The runtime side of the identifier convention (FR-25, D28, TSK-066): it reads the <c>prefixes</c> table out of <c>core.json</c> (through
    /// <see cref="ModContent"/>, so the player's override of the file is applied) and hands it to the pure logic of <see cref="ModIds"/>, which holds it and does
    /// the checks. The services of the core ask here instead of at <see cref="ModIds"/> so that the table is always loaded when they look:
    /// <list type="bullet">
    /// <item><b>Loading.</b> The Declare stage of <see cref="ModBootstrap"/> loads the table before any feature registers anything (D21, D28); every call below
    /// also loads it on first use, so the order of the services does not matter. It is read once per session; a bad row is a warning in the log
    /// (<see cref="Problems"/> keeps the lines for the self-check) and the other rows work. A table that cannot be read at all leaves every feature without
    /// prefixes: their predicates are refused and their strings and tags are reported, and the rest of the mod runs.</item>
    /// <item><b>Users.</b> <see cref="ModConditions.Register"/> plugs <see cref="IsPredicateNameAllowed"/> into its registry (FR-11 item 6, AC-11.2),
    /// <see cref="ModMail.Send"/> asks <see cref="Explain"/> about a tag (AC-25.2), and the self-check asks about the keys of the string tables and the
    /// rejected predicates (FR-8 item 6, FR-19). Features call <see cref="ModIds"/> directly for their own checks (event ids of a pack, keys of their content).</item>
    /// </list>
    /// Main thread only (the table itself can be read from any thread). Never throws.
    /// </summary>
    internal static class ModIdsAdapter
    {
        private const string CoreId = ModLog.CoreFeatureId;

        private static readonly object Sync = new object();
        private static readonly List<string> LoadProblems = new List<string>();

        private static bool _loaded;
        private static string _summary = "not loaded";

        // ---- the table ----

        /// <summary>Declare stage: the table of prefixes is read and handed to <see cref="ModIds"/>, before the registry of features exists.</summary>
        [CoreInit(CoreInitStage.Declare, -700, Optional = true)]
        internal static void Declare()
        {
            EnsureLoaded();
        }

        /// <summary>Loads the table from <c>core.json</c> if that has not happened yet. Safe to call at any time.</summary>
        internal static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;   // first: a failure below must not be retried (and logged) by every check

                try
                {
                    JsonObject core = ModContent.LoadConfig(ModContent.CoreFeatureId, ModContent.CoreFile, ModContent.CoreSchema);
                    Apply(core.GetObject("prefixes"));
                }
                catch (Exception ex)
                {
                    ModIds.Clear();
                    _summary = "unreadable";
                    ModLog.Error(CoreId, "ids: the prefixes table of " + ModContent.CoreFile + " could not be read; the features own no identifiers", ex);
                }
            }
        }

        /// <summary>The lines about the rows of the table that were skipped (empty when it loaded cleanly); a copy, for the self-check and the Diagnostics tab.</summary>
        internal static IReadOnlyList<string> Problems
        {
            get
            {
                EnsureLoaded();
                lock (Sync) return new List<string>(LoadProblems);
            }
        }

        /// <summary>One line for the log and the Diagnostics tab: <c>17 features, 19 prefixes</c>.</summary>
        internal static string Summary
        {
            get
            {
                EnsureLoaded();
                return _summary;
            }
        }

        /// <summary>The features of the table in the order of the rows.</summary>
        internal static IReadOnlyList<string> Features
        {
            get
            {
                EnsureLoaded();
                return ModIds.Features;
            }
        }

        /// <summary>The offline harness: a table of its own instead of <c>core.json</c> (null empties it). The next call does not load the file again.</summary>
        internal static void UseTableForTests(JsonObject prefixes)
        {
            lock (Sync)
            {
                _loaded = true;
                Apply(prefixes);
            }
        }

        // ---- the checks (the same as ModIds, with the table guaranteed) ----

        /// <summary>A key of the string tables of the feature (<c>&lt;prefix&gt;.&lt;rest&gt;</c>).</summary>
        internal static bool IsL10nKeyAllowed(string featureId, string key)
        {
            EnsureLoaded();
            return ModIds.IsL10nKeyAllowed(featureId, key);
        }

        /// <summary>A predicate name for <see cref="ModConditions.Register"/>: <c>&lt;prefix&gt;.&lt;name&gt;</c> for a feature, a name without a dot for the framework.</summary>
        internal static bool IsPredicateNameAllowed(string featureId, string name)
        {
            // The framework's own predicates need no table: keep their registration (32 of them, at the Declare stage) free of any file read.
            if (!string.Equals(featureId, ModIds.CoreFeatureId, StringComparison.Ordinal)) EnsureLoaded();
            return ModIds.IsPredicateNameAllowed(featureId, name);
        }

        /// <summary>An event id of a pack of the feature (<c>&lt;prefix&gt;.&lt;name&gt;</c>).</summary>
        internal static bool IsEventIdAllowed(string featureId, string eventId)
        {
            EnsureLoaded();
            return ModIds.IsEventIdAllowed(featureId, eventId);
        }

        /// <summary>A tag of a mail of the feature (<c>&lt;prefix&gt;:&lt;...&gt;</c>).</summary>
        internal static bool IsTagAllowed(string featureId, string tag)
        {
            EnsureLoaded();
            return ModIds.IsTagAllowed(featureId, tag);
        }

        /// <summary>Why the identifier does not follow the convention for the feature, in one English sentence; null when it does (see <see cref="ModIds.Explain"/>).</summary>
        internal static string Explain(IdKind kind, string featureId, string id)
        {
            EnsureLoaded();
            return ModIds.Explain(kind, featureId, id);
        }

        /// <summary>The prefixes of the feature in the order of its row; empty for a feature without a row.</summary>
        internal static IReadOnlyList<string> PrefixesOf(string featureId)
        {
            EnsureLoaded();
            return ModIds.PrefixesOf(featureId);
        }

        /// <summary>The feature that owns the prefix; null for an unknown prefix.</summary>
        internal static string FeatureOfPrefix(string prefix)
        {
            EnsureLoaded();
            return ModIds.FeatureOfPrefix(prefix);
        }

        /// <summary>The feature whose prefix the identifier starts with; null when it starts with none (see <see cref="ModIds.FeatureOfId"/>).</summary>
        internal static string FeatureOfId(IdKind kind, string id)
        {
            EnsureLoaded();
            return ModIds.FeatureOfId(kind, id);
        }

        // ---- internals ----

        private static void Apply(JsonObject prefixes)
        {
            var problems = new List<string>();
            int rows = ModIds.Load(prefixes, problems);

            LoadProblems.Clear();
            LoadProblems.AddRange(problems);

            int count = 0;
            foreach (string feature in ModIds.Features) count += ModIds.PrefixesOf(feature).Count;
            _summary = rows + " features, " + count + " prefixes";

            foreach (string problem in problems) ModLog.Warn(CoreId, "ids: " + ModContent.CoreFile + " " + problem);
            ModLog.Debug(CoreId, "ids: " + _summary + " in the identifier table");
        }
    }
}
