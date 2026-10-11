using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// What a condition of an entry names: the predicates (<c>type</c>) and the roles (<c>role</c> parameters) found in its JSON. The parsed
    /// <see cref="PTMod.Core.ICondition"/> tree does not tell, and the validator must (a predicate of another feature is not allowed in the calendar,
    /// a role must be one the entry declares).
    /// </summary>
    public sealed class ConditionUse
    {
        /// <summary>Every <c>type</c> that occurs, in the order of appearance (a name can repeat).</summary>
        public List<string> Predicates { get; set; } = new List<string>();

        /// <summary>Every <c>role</c> parameter of a node that has one.</summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>Walks the JSON of a condition (<c>all</c> / <c>any</c> / <c>not</c> / <c>type</c> nodes) and adds what it names. Deep trees are cut at 64 levels.</summary>
        public void Collect(JsonNode node)
        {
            Collect(node, 0);
        }

        private void Collect(JsonNode node, int depth)
        {
            if (node == null || depth > ConditionDepthLimit) return;

            var array = node as JsonArray;
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++) Collect(array[i], depth + 1);
                return;
            }

            var obj = node as JsonObject;
            if (obj == null) return;

            var type = obj["type"] as JsonValue;
            if (type != null && type.Kind == JsonKind.String) Predicates.Add(type.StringValue);

            var role = obj["role"] as JsonValue;
            if (role != null && role.Kind == JsonKind.String) Roles.Add(role.StringValue);

            Collect(obj["all"], depth + 1);
            Collect(obj["any"], depth + 1);
            Collect(obj["not"], depth + 1);
        }

        private const int ConditionDepthLimit = 64;
    }

    /// <summary>
    /// One firing of an event of the pack (design.md, Component 2): which event, for which role (<c>global</c>, a declared role or a group), with
    /// which extra arguments, and an optional condition checked for each company of the role.
    /// </summary>
    public sealed class FireSpec
    {
        /// <summary>The id of an event of the pack (<c>hist.…</c>).</summary>
        public string EventId { get; set; }

        /// <summary>A role the entry declares, or an implicit one (<see cref="RoleKinds.IsImplicitRole"/>); <c>global</c> when the JSON does not say.</summary>
        public string Role { get; set; } = RoleKinds.Global;

        /// <summary>Arguments handed to the event (<c>["eu2009"]</c>: the id of a case); never null.</summary>
        public string[] ExtraArgs { get; set; } = new string[0];

        /// <summary><c>onlyIf</c>: checked for every company of the role, the firing for a company whose condition is false is dropped; null for none.</summary>
        public ICondition OnlyIf { get; set; }

        /// <summary>What <see cref="OnlyIf"/> names (see <see cref="ConditionUse"/>).</summary>
        public ConditionUse OnlyIfUse { get; set; } = new ConditionUse();
    }

    /// <summary>One stage of an entry: the fires that happen <see cref="OffsetDays"/> after the entry fired (stage 0 is the firing itself).</summary>
    public sealed class StageSpec
    {
        /// <summary>Days after the firing; 0 for the first stage.</summary>
        public int OffsetDays { get; set; }

        public List<FireSpec> Fires { get; set; } = new List<FireSpec>();

        /// <summary>The stage is skipped when the entry carries this flag (set by the <c>HistFlag</c> effect); null for none.</summary>
        public string SkipIfFlag { get; set; }
    }

    /// <summary>
    /// A role the entry looks for in the world: a company that plays a part in the event (design.md, Component 4, table "Виды ролей"). The company is
    /// never named in the content except by <see cref="UniqueId"/>; its name comes from the game.
    /// </summary>
    public sealed class RoleSpec
    {
        /// <summary>The months of sales a share is measured over when the JSON does not say (the width of the share cache, <c>shareCacheMonths</c>).</summary>
        public const int DefaultMonths = 36;

        /// <summary>The name the fires and conditions refer to (<c>target</c>, <c>plaintiff</c>); a fallback role keeps the name of the role it stands in for.</summary>
        public string Name { get; set; }

        /// <summary>One of <see cref="RoleKinds"/>.</summary>
        public string Kind { get; set; }

        /// <summary>The segments a share is counted in; <see cref="Segment.Any"/> alone means all of them. Never null.</summary>
        public Segment[] Segments { get; set; } = new[] { Segment.Any };

        /// <summary>The months of sales a share is counted over.</summary>
        public int Months { get; set; } = DefaultMonths;

        /// <summary>The share the company must have (0 to 1).</summary>
        public float MinShare { get; set; }

        /// <summary><c>AICompany.UniqueID</c> of a <c>named</c> role; -1 when not given.</summary>
        public int UniqueId { get; set; } = -1;

        /// <summary>A <c>named</c> company must be a foundry.</summary>
        public bool RequireFoundry { get; set; }

        /// <summary>A <c>named</c> company must have production capacity.</summary>
        public bool RequireCapacity { get; set; }

        /// <summary>What to try when this role finds nobody, in order; never null. Ignored in the <c>strict</c> mode.</summary>
        public List<RoleSpec> Fallback { get; set; } = new List<RoleSpec>();
    }

    /// <summary>The "how it really was" reference of an entry (FR-1 item 2): the keys of the title and the fact in the string tables, and the sources.</summary>
    public sealed class HistoryInfo
    {
        public string TitleKey { get; set; }
        public string FactKey { get; set; }

        /// <summary>URLs of the sources; only displayed (the mod never opens them).</summary>
        public List<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>
    /// One entry of the calendar (<c>calendar.json</c>, FR-1; design.md, Component 2): a real event with its dates, the roles it looks for, the
    /// conditions it needs, the stages it plays and what to do if it never fits. Built by <see cref="FromJson"/> from the JSON, judged by
    /// <see cref="CalendarValidator"/>. The mapper checks the shape only (types and unreadable values); an absent member reads as a default and the
    /// validator decides whether it was needed.
    /// </summary>
    public sealed class CalendarEntry : IContentRecord
    {
        private static readonly string[] KnownMembers =
        {
            "id", "category", "major", "catchUp", "anchor", "window", "jitterDays", "roles", "conditions", "stages", "fallback", "fallbackFires", "history",
            "disabled",
        };

        private static readonly string[] KnownRoleMembers =
        {
            "name", "kind", "segments", "months", "minShare", "uniqueId", "requireFoundry", "requireCapacity", "fallback",
        };

        /// <summary>The deepest chain of <c>fallback</c> roles the mapper follows; deeper ones are a problem (a hand-made cycle cannot hang the loader).</summary>
        public const int MaxRoleDepth = 4;

        /// <summary><c>hist.&lt;year&gt;.&lt;name&gt;</c>; also the id of the event of the pack that announces it.</summary>
        public string Id { get; set; }

        /// <summary>One of <see cref="HistCategories"/>.</summary>
        public string Category { get; set; }

        /// <summary>A big event: two of them are at least <c>majorSpacingDays</c> apart.</summary>
        public bool Major { get; set; }

        /// <summary>Started after its date, the entry's lasting effects are caught up (FR-4).</summary>
        public bool CatchUp { get; set; }

        /// <summary>The real date of the event.</summary>
        public SimpleDate Anchor { get; set; }

        /// <summary>The first day the entry may fire.</summary>
        public SimpleDate WindowFrom { get; set; }

        /// <summary>The last day the entry may fire; after it the entry expires.</summary>
        public SimpleDate WindowTo { get; set; }

        /// <summary>The firing moves by a whole number of days in [-JitterDays, +JitterDays], rolled once per game.</summary>
        public int JitterDays { get; set; }

        public List<RoleSpec> Roles { get; set; } = new List<RoleSpec>();

        /// <summary>All of them must hold (the entry's <c>conditions</c> list is a conjunction).</summary>
        public List<ICondition> Conditions { get; set; } = new List<ICondition>();

        /// <summary>What <see cref="Conditions"/> name (see <see cref="ConditionUse"/>).</summary>
        public ConditionUse ConditionUse { get; set; } = new ConditionUse();

        /// <summary>Stage 0 plays at the firing; the later ones at their offset.</summary>
        public List<StageSpec> Stages { get; set; } = new List<StageSpec>();

        /// <summary>When the window ends without a firing: <see cref="FallbackModes"/>.</summary>
        public string Fallback { get; set; }

        /// <summary>Fires that play instead of the stage-0 fires when a role cannot be found (for example the plain event when no design-flaw feature is there).</summary>
        public List<FireSpec> FallbackFires { get; set; } = new List<FireSpec>();

        public HistoryInfo History { get; set; }

        public override string ToString()
        {
            return Id ?? "(no id)";
        }

        /// <summary>
        /// The mapper for <c>ModContent.LoadRecords</c>: <c>json =&gt; FromJson(json, errors, parseCondition)</c>. <paramref name="parseCondition"/> is
        /// <c>ModConditions.Parse</c> in the game, a parser over a test registry in tests.
        /// </summary>
        public static Func<JsonObject, List<string>, CalendarEntry> Mapper(Func<JsonNode, List<string>, string, ICondition> parseCondition)
        {
            return (json, errors) => FromJson(json, errors, parseCondition);
        }

        /// <summary>
        /// Builds an entry from its JSON record. A member of the wrong type, a date that is not <c>yyyy-MM-dd</c>, an unknown segment, a condition that
        /// does not parse: the reasons go to <paramref name="errors"/> and the result is null. Unknown members are warnings (the entry still loads).
        /// Never throws.
        /// </summary>
        public static CalendarEntry FromJson(JsonObject json, List<string> errors, Func<JsonNode, List<string>, string, ICondition> parseCondition)
        {
            if (json == null) return null;

            string id = null;
            try
            {
                id = HistJsonReader.Get(json, "id") is JsonValue idNode && idNode.Kind == JsonKind.String ? idNode.StringValue : null;
                var reader = new HistJsonReader("entry " + (id ?? "(no id)"), errors);
                CalendarEntry entry = Read(json, reader, parseCondition);
                return reader.Problems > 0 ? null : entry;
            }
            catch (Exception ex)
            {
                if (errors != null) errors.Add("entry " + (id ?? "(no id)") + ": the record could not be read: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static CalendarEntry Read(JsonObject json, HistJsonReader r, Func<JsonNode, List<string>, string, ICondition> parseCondition)
        {
            var entry = new CalendarEntry();
            entry.Id = r.Str(json, "id", string.Empty);
            if (string.IsNullOrEmpty(entry.Id)) r.Fail("id", "is required");

            entry.Category = r.Str(json, "category", string.Empty);
            entry.Major = r.Bool(json, "major", string.Empty, false);
            entry.CatchUp = r.Bool(json, "catchUp", string.Empty, false);
            entry.Anchor = ReadDate(json, "anchor", string.Empty, r);
            entry.JitterDays = r.Int(json, "jitterDays", string.Empty, 0);

            JsonObject window = r.Obj(json, "window", string.Empty);
            if (window != null)
            {
                entry.WindowFrom = ReadDate(window, "from", "window", r);
                entry.WindowTo = ReadDate(window, "to", "window", r);
                r.WarnUnknown(window, "window", "from", "to");
            }

            JsonArray roles = r.Arr(json, "roles", string.Empty);
            if (roles != null)
            {
                for (int i = 0; i < roles.Count; i++)
                {
                    RoleSpec role = ReadRole(roles[i], "roles[" + i + "]", null, 0, r);
                    if (role != null) entry.Roles.Add(role);
                }
            }

            JsonArray conditions = r.Arr(json, "conditions", string.Empty);
            if (conditions != null)
            {
                for (int i = 0; i < conditions.Count; i++)
                {
                    ICondition condition = ReadCondition(conditions[i], "$.conditions[" + i + "]", parseCondition, r);
                    if (condition == null) continue;
                    entry.Conditions.Add(condition);
                    entry.ConditionUse.Collect(conditions[i]);
                }
            }

            JsonArray stages = r.Arr(json, "stages", string.Empty);
            if (stages != null)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageSpec stage = ReadStage(stages[i], "stages[" + i + "]", parseCondition, r);
                    if (stage != null) entry.Stages.Add(stage);
                }
            }

            entry.Fallback = r.Str(json, "fallback", string.Empty);

            JsonArray fallbackFires = r.Arr(json, "fallbackFires", string.Empty);
            if (fallbackFires != null)
            {
                for (int i = 0; i < fallbackFires.Count; i++)
                {
                    FireSpec fire = ReadFire(fallbackFires[i], "fallbackFires[" + i + "]", parseCondition, r);
                    if (fire != null) entry.FallbackFires.Add(fire);
                }
            }

            JsonObject history = r.Obj(json, "history", string.Empty);
            if (history != null)
            {
                entry.History = new HistoryInfo
                {
                    TitleKey = r.Str(history, "titleKey", "history"),
                    FactKey = r.Str(history, "factKey", "history"),
                    Sources = r.Strings(history, "sources", "history"),
                };
                r.WarnUnknown(history, "history", "titleKey", "factKey", "sources");
            }

            r.WarnUnknown(json, string.Empty, KnownMembers);
            return entry;
        }

        private static SimpleDate ReadDate(JsonObject obj, string name, string path, HistJsonReader r)
        {
            string text = r.Str(obj, name, path);
            if (text == null) return default(SimpleDate);

            SimpleDate date;
            if (SimpleDate.TryParse(text, out date)) return date;

            r.Fail(HistJsonReader.Join(path, name), "expected a date as yyyy-MM-dd, got \"" + text + "\"");
            return default(SimpleDate);
        }

        private static RoleSpec ReadRole(JsonNode node, string path, string inheritedName, int depth, HistJsonReader r)
        {
            var json = node as JsonObject;
            if (json == null)
            {
                r.Fail(path, "expected an object, got " + HistJsonReader.KindName(node));
                return null;
            }

            if (depth > MaxRoleDepth)
            {
                r.Fail(path, "fallback roles are nested deeper than " + MaxRoleDepth + " levels");
                return null;
            }

            var role = new RoleSpec();
            role.Name = r.Str(json, "name", path) ?? inheritedName;
            role.Kind = r.Str(json, "kind", path);
            role.Months = r.Int(json, "months", path, RoleSpec.DefaultMonths);
            role.MinShare = (float)r.Num(json, "minShare", path, 0);
            role.UniqueId = r.Int(json, "uniqueId", path, -1);
            role.RequireFoundry = r.Bool(json, "requireFoundry", path, false);
            role.RequireCapacity = r.Bool(json, "requireCapacity", path, false);

            if (HistJsonReader.Get(json, "segments") != null)
            {
                var segments = new List<Segment>();
                foreach (string name in r.Strings(json, "segments", path))
                {
                    Segment segment;
                    if (HistSegments.TryParse(name, out segment)) segments.Add(segment);
                    else r.Fail(path + ".segments", "unknown segment \"" + name + "\" (all, highend, midrange, lowend, industries)");
                }
                role.Segments = segments.ToArray();
            }

            JsonArray fallback = r.Arr(json, "fallback", path);
            if (fallback != null)
            {
                for (int i = 0; i < fallback.Count; i++)
                {
                    RoleSpec inner = ReadRole(fallback[i], path + ".fallback[" + i + "]", role.Name, depth + 1, r);
                    if (inner != null) role.Fallback.Add(inner);
                }
            }

            r.WarnUnknown(json, path, KnownRoleMembers);
            return role;
        }

        private static StageSpec ReadStage(JsonNode node, string path, Func<JsonNode, List<string>, string, ICondition> parseCondition, HistJsonReader r)
        {
            var json = node as JsonObject;
            if (json == null)
            {
                r.Fail(path, "expected an object, got " + HistJsonReader.KindName(node));
                return null;
            }

            var stage = new StageSpec();
            stage.OffsetDays = r.Int(json, "offsetDays", path, 0);
            stage.SkipIfFlag = r.Str(json, "skipIfFlag", path);

            JsonArray fires = r.Arr(json, "fires", path);
            if (fires != null)
            {
                for (int i = 0; i < fires.Count; i++)
                {
                    FireSpec fire = ReadFire(fires[i], path + ".fires[" + i + "]", parseCondition, r);
                    if (fire != null) stage.Fires.Add(fire);
                }
            }

            r.WarnUnknown(json, path, "offsetDays", "fires", "skipIfFlag");
            return stage;
        }

        private static FireSpec ReadFire(JsonNode node, string path, Func<JsonNode, List<string>, string, ICondition> parseCondition, HistJsonReader r)
        {
            var json = node as JsonObject;
            if (json == null)
            {
                r.Fail(path, "expected an object, got " + HistJsonReader.KindName(node));
                return null;
            }

            var fire = new FireSpec();
            fire.EventId = r.Str(json, "eventId", path);
            fire.Role = r.Str(json, "role", path) ?? RoleKinds.Global;
            fire.ExtraArgs = r.Strings(json, "extraArgs", path).ToArray();

            JsonNode onlyIf = HistJsonReader.Get(json, "onlyIf");
            if (onlyIf != null)
            {
                fire.OnlyIf = ReadCondition(onlyIf, "$." + path + ".onlyIf", parseCondition, r);
                if (fire.OnlyIf != null) fire.OnlyIfUse.Collect(onlyIf);
            }

            r.WarnUnknown(json, path, "eventId", "role", "extraArgs", "onlyIf");
            return fire;
        }

        private static ICondition ReadCondition(JsonNode node, string path, Func<JsonNode, List<string>, string, ICondition> parseCondition, HistJsonReader r)
        {
            if (parseCondition == null)
            {
                r.Fail(path, "conditions cannot be read here: no condition parser was given");
                return null;
            }

            var problems = new List<string>();
            ICondition condition = parseCondition(node, problems, path);
            foreach (string problem in problems) r.FailRaw(problem);
            if (condition == null && problems.Count == 0) r.Fail(path, "the condition could not be read");
            return problems.Count == 0 ? condition : null;
        }
    }

    /// <summary>The words of the content for the market segments (<c>all</c>, <c>highend</c>, ...), as <see cref="Segment"/> values.</summary>
    internal static class HistSegments
    {
        /// <summary>Case-insensitive. <c>all</c> is <see cref="Segment.Any"/>.</summary>
        public static bool TryParse(string name, out Segment segment)
        {
            segment = Segment.Any;
            if (name == null) return false;

            switch (name.ToLowerInvariant())
            {
                case "all": segment = Segment.Any; return true;
                case "highend": segment = Segment.HighEnd; return true;
                case "midrange": segment = Segment.MidRange; return true;
                case "lowend": segment = Segment.LowEnd; return true;
                case "industries": segment = Segment.Industries; return true;
                default: return false;
            }
        }
    }
}
