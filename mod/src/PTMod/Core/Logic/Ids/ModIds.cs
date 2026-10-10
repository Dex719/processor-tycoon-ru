using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Ids
{
    /// <summary>
    /// The kinds of identifier the naming convention (FR-25, D28) covers. The kind decides the character that follows the prefix:
    /// a dot for keys, predicates and events, a colon for mail tags (so a tag can never be mistaken for a key of the string tables).
    /// </summary>
    public enum IdKind
    {
        /// <summary>A key of the string tables: <c>ms.channel.production.memoryCost</c>.</summary>
        L10nKey = 0,

        /// <summary>A predicate of <c>ModConditions</c>: <c>es.hasFab</c> for a feature, <c>dateAtLeast</c> (no dot) for the framework.</summary>
        PredicateName = 1,

        /// <summary>An event id of <c>event-system</c> content: <c>ms.earthquake</c>.</summary>
        EventId = 2,

        /// <summary>A tag of a mod mail: <c>onb:welcome</c>, <c>scn:briefing:run7</c>.</summary>
        Tag = 3
    }

    /// <summary>
    /// The identifier convention of the mod (FR-25, D28), as pure checks over the table "feature to prefixes" (<c>prefixes</c> in <c>core.json</c>, 17 rows).
    /// Strings of the tables, predicates, event ids and mail tags carry the prefix of their feature, so two features cannot collide and the core can
    /// say whose an identifier is:
    /// <list type="bullet">
    /// <item><b>Keys, predicates, event ids</b> of a feature are <c>&lt;prefix&gt;.&lt;name&gt;</c>; <b>tags</b> are <c>&lt;prefix&gt;:&lt;...&gt;</c>. A feature may own several
    /// prefixes (<c>scenarios-achievements</c>: <c>scn</c>, <c>chl</c>, <c>ach</c>); each prefix belongs to one feature only.</item>
    /// <item><b>Predicates of the framework</b> (<c>mod-framework</c>) have no dot at all, so no feature predicate can ever collide with a future one of the core.</item>
    /// <item>The match is exact: ordinal, case-sensitive, the prefix is a whole segment (<c>ms.x</c> matches <c>ms</c>, <c>msx.y</c> and <c>Ms.x</c> do not),
    /// and something must follow the prefix. A feature that has no row in the table owns nothing.</item>
    /// </list>
    /// The table is loaded by <see cref="Load"/> (the runtime adapter does it from <c>core.json</c> before any feature registers; tests load their own) and is
    /// replaced as a whole, so a check never sees half a table. Checks do not allocate when the answer is yes; <see cref="Explain"/> says why it is no. Nothing here throws.
    /// </summary>
    public static class ModIds
    {
        /// <summary>The id of the framework itself; its row of the table is <c>core</c>, and its predicates are the ones without a dot.</summary>
        public const string CoreFeatureId = FeatureGraph.CoreFeatureId;

        /// <summary>Longest prefix a row of the table may hold.</summary>
        public const int MaxPrefixLength = 16;

        private const char KeySeparator = '.';
        private const char TagSeparator = ':';
        private const int MaxQuotedLength = 64;

        private static readonly ReadOnlyCollection<string> NoStrings = new ReadOnlyCollection<string>(new string[0]);

        private static volatile Table _table = new Table();

        // ---- the table ----

        /// <summary>True when the table has at least one row (<see cref="Load"/> has run and found something usable).</summary>
        public static bool IsLoaded
        {
            get { return _table.Features.Count > 0; }
        }

        /// <summary>The features of the table in the order of the rows; empty before <see cref="Load"/>.</summary>
        public static IReadOnlyList<string> Features
        {
            get { return _table.Features; }
        }

        /// <summary>
        /// Builds the table from the <c>prefixes</c> object of <c>core.json</c> (<c>{ "market-shocks": ["ms"], "scenarios-achievements": ["scn", "chl", "ach"] }</c>)
        /// and makes it the current one, replacing the previous table whatever it held. A bad row is skipped and described in
        /// <paramref name="problems"/> (one line each, starting with the place in the object): a row that is not a non-empty array of strings, a blank feature id,
        /// a prefix that is not 1 to <see cref="MaxPrefixLength"/> lower-case letters, a repeated prefix, a prefix that another row owns already (the earlier row keeps it).
        /// A null object gives an empty table. Returns the number of rows taken.
        /// </summary>
        public static int Load(JsonObject prefixes, List<string> problems = null)
        {
            var table = new Table();
            if (prefixes == null)
            {
                Report(problems, "prefixes: the table is missing; no feature owns any identifier");
            }
            else
            {
                foreach (KeyValuePair<string, JsonNode> row in prefixes) AddRow(table, row.Key, row.Value, problems);
            }

            table.Seal();
            _table = table;
            return table.Features.Count;
        }

        /// <summary>Empties the table (every check answers no until the next <see cref="Load"/>). For tests and for a failed start.</summary>
        public static void Clear()
        {
            _table = new Table();
        }

        /// <summary>The prefixes of the feature in the order of its row (<c>["scn", "chl", "ach"]</c>); an empty list for an unknown feature, never null.</summary>
        public static IReadOnlyList<string> PrefixesOf(string featureId)
        {
            ReadOnlyCollection<string> prefixes;
            return featureId != null && _table.ByFeature.TryGetValue(featureId, out prefixes) ? prefixes : NoStrings;
        }

        /// <summary>The feature that owns the prefix (<c>"ms"</c> gives <c>market-shocks</c>); null for an unknown prefix. Exact match, no separator.</summary>
        public static string FeatureOfPrefix(string prefix)
        {
            string feature;
            return prefix != null && _table.ByPrefix.TryGetValue(prefix, out feature) ? feature : null;
        }

        // ---- the checks (FR-25 item 5) ----

        /// <summary>A key of the string tables of <paramref name="featureId"/>: <c>&lt;prefix&gt;.&lt;rest&gt;</c> with one of the prefixes of the feature (the framework's is <c>core</c>).</summary>
        public static bool IsL10nKeyAllowed(string featureId, string key)
        {
            string unused;
            return Judge(IdKind.L10nKey, featureId, key, out unused) == Verdict.Allowed;
        }

        /// <summary>
        /// A predicate name for <c>ModConditions.Register</c>: <c>&lt;prefix&gt;.&lt;name&gt;</c> for a feature; for the framework (<see cref="CoreFeatureId"/>) any
        /// non-empty name with no dot.
        /// </summary>
        public static bool IsPredicateNameAllowed(string featureId, string name)
        {
            string unused;
            return Judge(IdKind.PredicateName, featureId, name, out unused) == Verdict.Allowed;
        }

        /// <summary>An event id of the content of <c>event-system</c>: <c>&lt;prefix&gt;.&lt;name&gt;</c> with a prefix of the feature that owns the pack.</summary>
        public static bool IsEventIdAllowed(string featureId, string eventId)
        {
            string unused;
            return Judge(IdKind.EventId, featureId, eventId, out unused) == Verdict.Allowed;
        }

        /// <summary>A tag of a mod mail: <c>&lt;prefix&gt;:&lt;...&gt;</c> with a prefix of the sending feature. A null or empty tag is not allowed here (a mail may have no tag; the caller decides what that means).</summary>
        public static bool IsTagAllowed(string featureId, string tag)
        {
            string unused;
            return Judge(IdKind.Tag, featureId, tag, out unused) == Verdict.Allowed;
        }

        /// <summary>The same check by <paramref name="kind"/> (a validator that handles all kinds the same way).</summary>
        public static bool IsAllowed(IdKind kind, string featureId, string id)
        {
            string unused;
            return Judge(kind, featureId, id, out unused) == Verdict.Allowed;
        }

        /// <summary>
        /// Why <paramref name="id"/> is not allowed for <paramref name="featureId"/> - one English sentence for a log line or a report, naming the identifier and
        /// the prefixes that would do (<c>the mail tag "welcome" must start with "onb:"</c>); null when it is allowed.
        /// </summary>
        public static string Explain(IdKind kind, string featureId, string id)
        {
            string owner;
            Verdict verdict = Judge(kind, featureId, id, out owner);
            if (verdict == Verdict.Allowed) return null;

            string what = NounOf(kind);
            string quoted = Quote(id);
            switch (verdict)
            {
                case Verdict.NoFeature:
                    return "the feature id is not given, so the " + what + " " + quoted + " cannot be checked";
                case Verdict.EmptyId:
                    return "the " + what + " is empty";
                case Verdict.FeatureNotInTable:
                    return "the feature \"" + featureId + "\" has no row in the prefixes table of core.json, so the " + what + " " + quoted + " has no prefix to carry";
                case Verdict.CorePredicateWithDot:
                    return "the " + what + " " + quoted + " has a dot; the predicates of the framework are named without one";
                case Verdict.EmptyName:
                    return "the " + what + " " + quoted + " has nothing after the prefix";
                case Verdict.ForeignPrefix:
                    return "the " + what + " " + quoted + " must start with " + FormsOf(kind, featureId) + " (its prefix belongs to " + owner + ")";
                default:
                    return "the " + what + " " + quoted + " must start with " + FormsOf(kind, featureId);
            }
        }

        /// <summary>
        /// The feature whose prefix <paramref name="id"/> starts with: <c>"ms.earthquake"</c> gives <c>market-shocks</c>. A predicate without a dot belongs to the framework.
        /// Null when the id has no prefix of the table. Says nothing about whether the id is well formed - the caller asks for that with the checks above.
        /// </summary>
        public static string FeatureOfId(IdKind kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            int cut = id.IndexOf(SeparatorOf(kind));
            if (cut < 0) return kind == IdKind.PredicateName ? CoreFeatureId : null;
            if (cut == 0) return null;

            string owner = FeatureOfPrefix(id.Substring(0, cut));
            return kind == IdKind.PredicateName && string.Equals(owner, CoreFeatureId, StringComparison.Ordinal) ? null : owner;
        }

        /// <summary>The character after the prefix: ':' for a tag, '.' for everything else.</summary>
        public static char SeparatorOf(IdKind kind)
        {
            return kind == IdKind.Tag ? TagSeparator : KeySeparator;
        }

        // ---- the rule ----

        private enum Verdict
        {
            Allowed,
            NoFeature,
            EmptyId,
            FeatureNotInTable,
            CorePredicateWithDot,
            WrongPrefix,
            ForeignPrefix,
            EmptyName
        }

        private static Verdict Judge(IdKind kind, string featureId, string id, out string owner)
        {
            owner = null;
            if (string.IsNullOrEmpty(featureId)) return Verdict.NoFeature;
            if (string.IsNullOrEmpty(id)) return Verdict.EmptyId;

            // The framework's own predicates are the one case that does not use the table: no dot, that is all.
            if (kind == IdKind.PredicateName && string.Equals(featureId, CoreFeatureId, StringComparison.Ordinal))
                return id.IndexOf(KeySeparator) >= 0 ? Verdict.CorePredicateWithDot : Verdict.Allowed;

            Table table = _table;
            ReadOnlyCollection<string> prefixes;
            if (!table.ByFeature.TryGetValue(featureId, out prefixes)) return Verdict.FeatureNotInTable;

            int cut = id.IndexOf(SeparatorOf(kind));
            if (cut <= 0) return Verdict.WrongPrefix;

            for (int i = 0; i < prefixes.Count; i++)
            {
                string prefix = prefixes[i];
                if (prefix.Length != cut || string.CompareOrdinal(id, 0, prefix, 0, cut) != 0) continue;
                return cut == id.Length - 1 ? Verdict.EmptyName : Verdict.Allowed;
            }

            string other = table.OwnerOfPrefixAt(id, cut);
            if (other == null) return Verdict.WrongPrefix;
            owner = other;
            return Verdict.ForeignPrefix;
        }

        // ---- messages ----

        private static string NounOf(IdKind kind)
        {
            switch (kind)
            {
                case IdKind.L10nKey: return "l10n key";
                case IdKind.PredicateName: return "predicate name";
                case IdKind.EventId: return "event id";
                default: return "mail tag";
            }
        }

        /// <summary>The identifier in quotes, cut when long, so a log line stays a line.</summary>
        private static string Quote(string id)
        {
            if (id == null) return "\"\"";
            return id.Length <= MaxQuotedLength ? "\"" + id + "\"" : "\"" + id.Substring(0, MaxQuotedLength) + "...\"";
        }

        /// <summary><c>"onb:"</c>, or <c>"scn:", "chl:" or "ach:"</c> for a feature with several prefixes.</summary>
        private static string FormsOf(IdKind kind, string featureId)
        {
            if (kind == IdKind.PredicateName && string.Equals(featureId, CoreFeatureId, StringComparison.Ordinal)) return "a name without a dot";

            IReadOnlyList<string> prefixes = PrefixesOf(featureId);
            char separator = SeparatorOf(kind);
            var sb = new StringBuilder();
            for (int i = 0; i < prefixes.Count; i++)
            {
                if (i > 0) sb.Append(i == prefixes.Count - 1 ? " or " : ", ");
                sb.Append('"').Append(prefixes[i]).Append(separator).Append('"');
            }
            return sb.ToString();
        }

        // ---- loading ----

        private static void AddRow(Table table, string feature, JsonNode value, List<string> problems)
        {
            string where = "prefixes." + feature;
            if (string.IsNullOrWhiteSpace(feature))
            {
                Report(problems, "prefixes: a row has a blank feature id and is skipped");
                return;
            }

            var list = value as JsonArray;
            if (list == null || list.Count == 0)
            {
                Report(problems, where + ": must be a non-empty array of prefixes; the feature owns nothing");
                return;
            }

            var taken = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string at = where + "[" + i + "]";
                var item = list[i] as JsonValue;
                string prefix = item != null && item.Kind == JsonKind.String ? item.StringValue : null;
                if (!IsValidPrefix(prefix))
                {
                    Report(problems, at + ": a prefix is 1 to " + MaxPrefixLength + " lower-case letters (a-z), skipped");
                    continue;
                }
                if (taken.Contains(prefix))
                {
                    Report(problems, at + ": the prefix \"" + prefix + "\" is listed twice, skipped");
                    continue;
                }

                string owner;
                if (table.ByPrefix.TryGetValue(prefix, out owner))
                {
                    Report(problems, at + ": the prefix \"" + prefix + "\" belongs to " + owner + " already, skipped");
                    continue;
                }

                taken.Add(prefix);
                table.ByPrefix[prefix] = feature;
            }

            if (taken.Count == 0)
            {
                Report(problems, where + ": no usable prefix; the feature owns nothing");
                return;
            }
            table.ByFeature[feature] = new ReadOnlyCollection<string>(taken);
            table.FeatureOrder.Add(feature);
        }

        private static bool IsValidPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || prefix.Length > MaxPrefixLength) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (prefix[i] < 'a' || prefix[i] > 'z') return false;
            }
            return true;
        }

        private static void Report(List<string> problems, string message)
        {
            if (problems != null) problems.Add(message);
        }

        /// <summary>One table: immutable once <see cref="Seal"/> has run, so it can be swapped in as a whole.</summary>
        private sealed class Table
        {
            public readonly Dictionary<string, ReadOnlyCollection<string>> ByFeature = new Dictionary<string, ReadOnlyCollection<string>>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> ByPrefix = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<string> FeatureOrder = new List<string>();

            public ReadOnlyCollection<string> Features { get; private set; }

            public Table()
            {
                Features = NoStrings;
            }

            public void Seal()
            {
                Features = new ReadOnlyCollection<string>(FeatureOrder);
            }

            /// <summary>The feature that owns the first <paramref name="length"/> characters of <paramref name="id"/> as a prefix; null when none does.</summary>
            public string OwnerOfPrefixAt(string id, int length)
            {
                string owner;
                return ByPrefix.TryGetValue(id.Substring(0, length), out owner) ? owner : null;
            }
        }
    }
}
