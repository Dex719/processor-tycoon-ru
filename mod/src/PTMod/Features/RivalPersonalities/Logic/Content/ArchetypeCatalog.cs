using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What <see cref="ArchetypeCatalog.Validate"/> and <see cref="ArchetypeCatalog.Build"/> found. An error means a record was left out; a
    /// warning means a record was accepted after a repair (a number clipped, a name dropped). Every line is English and names the record.
    /// </summary>
    public sealed class ValidationReport
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>Records that were left out, and files that could not be used.</summary>
        public List<string> Errors
        {
            get { return _errors; }
        }

        /// <summary>Repairs that kept the record.</summary>
        public List<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>No record was left out (warnings do not count).</summary>
        public bool IsClean
        {
            get { return _errors.Count == 0; }
        }

        /// <summary>The catalog that was built from the accepted records; null until it is built.</summary>
        public ArchetypeCatalog Catalog { get; internal set; }
    }

    /// <summary>
    /// The archetypes, the default assignments and the numbers of the feature, checked (design.md Component 2, FR-1, FR-2). Built once from the
    /// content files by the game side (<c>RivalContent</c>) and read-only afterwards. The checking is pure and lives here:
    /// <list type="bullet">
    /// <item><see cref="MapArchetype"/> / <see cref="ValidateArchetype"/> and <see cref="MapAssignment"/> / <see cref="ValidateAssignment"/> are the
    /// mapper and the validator that <c>ModContent.LoadRecords</c> wants, one record at a time (the content merger, with the player's override file,
    /// does the rest). A record without an <c>id</c> is skipped, the numbers are clipped with a warning, the unknown reaction types and triggers
    /// are dropped.</item>
    /// <item><see cref="Build"/> checks the records against each other (a duplicate, an assignment of an archetype that does not exist gives
    /// <see cref="DefaultArchetypeId"/>) and makes the catalog.</item>
    /// <item><see cref="Validate"/> runs all of it on ready records in one call (the self-check and the tests).</item>
    /// </list>
    /// </summary>
    public sealed class ArchetypeCatalog
    {
        /// <summary>The archetype of a company that has no assignment, or whose assignment names an archetype that does not exist.</summary>
        public const string DefaultArchetypeId = "conservative";

        private static readonly string[] ArchetypeMembers =
        {
            "id", "name", "description", "behaviour", "attitude", "cooldownDays", "reactions", "reactionParams", "eventStyle", "flawResponseBias", "disabled",
        };

        private static readonly string[] BehaviourMembers =
        {
            "approvalBias", "researchFundingMult", "launchPriceMult", "designFocusOverride", "powerFocusOverride", "negotiationDiscount",
        };

        private static readonly string[] AttitudeMembers = { "baseline", "sensitivity" };

        private static readonly string[] AssignmentMembers = { "id", "uniqueId", "key", "archetype", "disabled" };

        private const int MaxIdLength = 64;
        private const int MaxUniqueId = 100000;

        private readonly List<Archetype> _all;
        private readonly Dictionary<string, Archetype> _byId;
        private readonly List<AssignmentRecord> _assignments;
        private readonly Dictionary<int, AssignmentRecord> _byUniqueId;

        private ArchetypeCatalog(RivalConfig config, List<Archetype> archetypes, List<AssignmentRecord> assignments)
        {
            Config = config ?? RivalConfig.Defaults();
            _all = archetypes;
            _assignments = assignments;
            _byId = new Dictionary<string, Archetype>(StringComparer.Ordinal);
            foreach (Archetype archetype in archetypes) _byId[archetype.id] = archetype;
            _byUniqueId = new Dictionary<int, AssignmentRecord>();
            foreach (AssignmentRecord assignment in assignments) _byUniqueId[assignment.uniqueId] = assignment;
        }

        /// <summary>A catalog with the default numbers and nothing else: what the feature holds until its content is read, and when it cannot be.</summary>
        public static ArchetypeCatalog Empty
        {
            get { return new ArchetypeCatalog(RivalConfig.Defaults(), new List<Archetype>(), new List<AssignmentRecord>()); }
        }

        /// <summary>The numbers of the feature (<c>config.json</c>); never null.</summary>
        public RivalConfig Config { get; private set; }

        /// <summary>The archetypes in file order, then the ones an override added.</summary>
        public IReadOnlyList<Archetype> All
        {
            get { return _all; }
        }

        /// <summary>The default assignments, one per company, in file order.</summary>
        public IReadOnlyList<AssignmentRecord> Assignments
        {
            get { return _assignments; }
        }

        /// <summary>The catalog has at least one archetype; without one the feature cannot give characters (FR-1).</summary>
        public bool IsUsable
        {
            get { return _all.Count > 0; }
        }

        public bool TryGet(string archetypeId, out Archetype archetype)
        {
            archetype = null;
            return archetypeId != null && _byId.TryGetValue(archetypeId, out archetype);
        }

        /// <summary>The assignment of the company with this <c>UniqueID</c>, or null.</summary>
        public AssignmentRecord AssignmentFor(int uniqueId)
        {
            AssignmentRecord assignment;
            return _byUniqueId.TryGetValue(uniqueId, out assignment) ? assignment : null;
        }

        /// <summary>
        /// The id of the archetype the company gets by default (FR-2): the one in <c>assignments.json</c>, else <see cref="DefaultArchetypeId"/>.
        /// When even that archetype is not in the catalog (an override removed it) the first archetype of the catalog is the answer; an empty
        /// catalog answers <see cref="DefaultArchetypeId"/> (then <see cref="TryGet"/> finds nothing).
        /// </summary>
        public string DefaultArchetypeFor(int uniqueId)
        {
            AssignmentRecord assignment = AssignmentFor(uniqueId);
            if (assignment != null && assignment.archetype != null && _byId.ContainsKey(assignment.archetype)) return assignment.archetype;
            return FallbackId(_byId, _all);
        }

        // ---- one record at a time (for ModContent.LoadRecords) ----

        /// <summary>
        /// The mapper for <c>archetypes.json</c>: builds the record from its JSON object with <c>ModJson.Bind</c>. Null (the reason in
        /// <paramref name="messages"/>) for a record without an <c>id</c> or one that cannot be read at all; for a readable record the lines in
        /// <paramref name="messages"/> (a member of the wrong type that kept its default, an unknown member that is ignored) are warnings.
        /// </summary>
        public static Archetype MapArchetype(JsonObject json, List<string> messages)
        {
            if (json == null) return null;

            string id = json.GetString("id", null);
            if (string.IsNullOrEmpty(id))
            {
                Say(messages, "id: is required");
                return null;
            }

            var errors = new List<string>();
            Archetype archetype;
            try
            {
                archetype = ModJson.Bind<Archetype>(json, errors);
            }
            catch (Exception ex)
            {
                Say(messages, "the record could not be read: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            if (archetype == null)
            {
                Say(messages, "the record could not be read");
                return null;
            }

            if (messages != null)
            {
                messages.AddRange(errors);
                AddUnknown(json, "", ArchetypeMembers, messages);
                AddUnknown(json.GetObject("behaviour"), "behaviour.", BehaviourMembers, messages);
                AddUnknown(json.GetObject("attitude"), "attitude.", AttitudeMembers, messages);
            }
            return archetype;
        }

        /// <summary>
        /// The validator for <c>archetypes.json</c>: false (the record is skipped) when the <c>id</c> is missing or is not a plain name; otherwise
        /// true, with a line in <paramref name="messages"/> for everything that was repaired. Clips <c>approvalBias</c> to 0.8..1.3,
        /// <c>researchFundingMult</c> to 0.7..1.3, <c>launchPriceMult</c> to 0.85..1.15, the baseline to -50..50, the sensitivity to 0.3..2.0, the
        /// cooldown to 60..720 days, every base chance to 0..0.9 and every <c>flawResponseBias</c> to 0.5..2.0 (a list of another length gives
        /// all 1.0); drops the reaction tables of unknown triggers, the rows of unknown reaction types (and of the friendly <c>Partnership</c>),
        /// the <c>reactionParams</c> of unknown reactions and unknown numbers. Accepts <c>negotiationDiscount</c>.
        /// </summary>
        public static bool ValidateArchetype(Archetype archetype, List<string> messages)
        {
            if (archetype == null) return false;
            if (!IsPlainId(archetype.id))
            {
                Say(messages, "id: is required and must be 1-" + MaxIdLength + " letters, digits, '-' or '_'");
                return false;
            }

            if (archetype.behaviour == null) archetype.behaviour = new ArchetypeBehaviour();
            if (archetype.attitude == null) archetype.attitude = new ArchetypeAttitude();

            if (archetype.name == null || archetype.name.IsNull)
                Say(messages, "name: is missing, the key rival.archetype." + archetype.id + ".name is used");
            if (archetype.description == null || archetype.description.IsNull)
                Say(messages, "description: is missing, the key rival.archetype." + archetype.id + ".desc is used");

            ArchetypeBehaviour b = archetype.behaviour;
            Limits.Float(ref b.approvalBias, 0.8f, 1.3f, 1f, "behaviour.approvalBias", messages);
            Limits.Float(ref b.researchFundingMult, 0.7f, 1.3f, 1f, "behaviour.researchFundingMult", messages);
            Limits.Float(ref b.launchPriceMult, 0.85f, 1.15f, 1f, "behaviour.launchPriceMult", messages);
            if (!Enum.IsDefined(typeof(DesignFocusOverride), b.designFocusOverride))
            {
                Say(messages, "behaviour.designFocusOverride: unknown value, no override is used");
                b.designFocusOverride = DesignFocusOverride.None;
            }
            if (!Enum.IsDefined(typeof(PowerFocusOverride), b.powerFocusOverride))
            {
                Say(messages, "behaviour.powerFocusOverride: unknown value, no override is used");
                b.powerFocusOverride = PowerFocusOverride.None;
            }

            ArchetypeAttitude a = archetype.attitude;
            Limits.Float(ref a.baseline, -50f, 50f, 0f, "attitude.baseline", messages);
            Limits.Float(ref a.sensitivity, 0.3f, 2.0f, 1f, "attitude.sensitivity", messages);

            Limits.Int(ref archetype.cooldownDays, 60, 720, "cooldownDays", messages);

            if (!Enum.IsDefined(typeof(EventStyle), archetype.eventStyle))
            {
                Say(messages, "eventStyle: unknown value, 'neutral' is used");
                archetype.eventStyle = EventStyle.Neutral;
            }

            archetype.reactions = CleanReactions(archetype.reactions, messages);
            archetype.reactionParams = CleanReactionParams(archetype.reactionParams, messages);

            float[] bias = archetype.flawResponseBias;
            if (bias == null || bias.Length != 4)
            {
                Say(messages, "flawResponseBias: expected 4 numbers [Recall, Stepping, Patch, Ignore], all 1.0 is used");
                bias = new[] { 1f, 1f, 1f, 1f };
            }
            else
            {
                bias = (float[])bias.Clone();
            }
            for (int i = 0; i < bias.Length; i++) Limits.Float(ref bias[i], 0.5f, 2.0f, 1f, "flawResponseBias[" + i + "]", messages);
            archetype.flawResponseBias = bias;
            return true;
        }

        /// <summary>
        /// The mapper for <c>assignments.json</c>. Null for a record without an <c>id</c> or one that cannot be read; unknown members are
        /// warnings.
        /// </summary>
        public static AssignmentRecord MapAssignment(JsonObject json, List<string> messages)
        {
            if (json == null) return null;

            string id = json.GetString("id", null);
            if (string.IsNullOrEmpty(id))
            {
                Say(messages, "id: is required");
                return null;
            }

            var errors = new List<string>();
            AssignmentRecord assignment;
            try
            {
                assignment = ModJson.Bind<AssignmentRecord>(json, errors);
            }
            catch (Exception ex)
            {
                Say(messages, "the record could not be read: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            if (assignment == null)
            {
                Say(messages, "the record could not be read");
                return null;
            }

            if (messages != null)
            {
                messages.AddRange(errors);
                AddUnknown(json, "", AssignmentMembers, messages);
            }
            return assignment;
        }

        /// <summary>
        /// The validator for <c>assignments.json</c>: false when the <c>id</c> is not a plain name or <c>uniqueId</c> is not a whole number from 0
        /// up; a record without <c>key</c> is accepted with a warning, one without <c>archetype</c> gets <see cref="DefaultArchetypeId"/> with a warning.
        /// Whether the archetype exists is checked later, in <see cref="Build"/> (it needs the other file).
        /// </summary>
        public static bool ValidateAssignment(AssignmentRecord assignment, List<string> messages)
        {
            if (assignment == null) return false;
            if (!IsPlainId(assignment.id))
            {
                Say(messages, "id: is required and must be 1-" + MaxIdLength + " letters, digits, '-' or '_'");
                return false;
            }
            if (assignment.uniqueId < 0 || assignment.uniqueId > MaxUniqueId)
            {
                Say(messages, "uniqueId: is required and must be a whole number from 0 to " + MaxUniqueId + " (the UniqueID of the AI company)");
                return false;
            }

            if (string.IsNullOrEmpty(assignment.key)) Say(messages, "key: is missing (the machine name of the company, for readable content)");
            if (string.IsNullOrEmpty(assignment.archetype))
            {
                Say(messages, "archetype: is missing, '" + DefaultArchetypeId + "' is used");
                assignment.archetype = DefaultArchetypeId;
            }
            return true;
        }

        // ---- the catalog ----

        /// <summary>
        /// Checks the accepted records against each other and builds the catalog. Null entries are skipped. Of two archetypes with one id the first
        /// stays (an error for the second); of two assignments for one company the later one stays (a warning: this is how an override replaces a
        /// built-in record under another id). An assignment of an archetype that is not in the catalog is changed to
        /// <see cref="DefaultArchetypeId"/> (or, when that is missing too, to the first archetype) with a warning (FR-2). Problems go to
        /// <paramref name="report"/> (may be null); the catalog is also stored in <see cref="ValidationReport.Catalog"/>.
        /// </summary>
        public static ArchetypeCatalog Build(IEnumerable<Archetype> archetypes, IEnumerable<AssignmentRecord> assignments, RivalConfig config, ValidationReport report)
        {
            var all = new List<Archetype>();
            var byId = new Dictionary<string, Archetype>(StringComparer.Ordinal);
            if (archetypes != null)
            {
                foreach (Archetype archetype in archetypes)
                {
                    if (archetype == null || string.IsNullOrEmpty(archetype.id)) continue;
                    if (byId.ContainsKey(archetype.id))
                    {
                        Fail(report, "archetype '" + archetype.id + "': the id is used twice, the first record stays");
                        continue;
                    }
                    byId[archetype.id] = archetype;
                    all.Add(archetype);
                }
            }

            if (all.Count == 0) Fail(report, "archetypes: none is usable, characters cannot be given");

            string fallback = FallbackId(byId, all);
            var kept = new List<AssignmentRecord>();
            var position = new Dictionary<int, int>();
            if (assignments != null)
            {
                foreach (AssignmentRecord assignment in assignments)
                {
                    if (assignment == null || assignment.uniqueId < 0) continue;

                    if (all.Count > 0 && (assignment.archetype == null || !byId.ContainsKey(assignment.archetype)))
                    {
                        Warn(report, "assignment '" + assignment.id + "': the archetype '" + assignment.archetype + "' does not exist, '" + fallback + "' is used");
                        assignment.archetype = fallback;
                    }

                    int index;
                    if (position.TryGetValue(assignment.uniqueId, out index))
                    {
                        Warn(report, "assignment '" + assignment.id + "': uniqueId " + assignment.uniqueId + " is already assigned by '" + kept[index].id + "', the later record is used");
                        kept[index] = assignment;
                    }
                    else
                    {
                        position[assignment.uniqueId] = kept.Count;
                        kept.Add(assignment);
                    }
                }
            }

            var catalog = new ArchetypeCatalog(config, all, kept);
            if (report != null) report.Catalog = catalog;
            return catalog;
        }

        /// <summary>
        /// Validates ready records and builds the catalog in one call: <see cref="ValidateArchetype"/> and <see cref="ValidateAssignment"/> for every
        /// record (the lines of a skipped record are errors, those of an accepted one are warnings, each with the id of the record in front), then
        /// <see cref="Build"/>. The numbers of <paramref name="config"/> are brought into their ranges too (null gives the defaults). The records
        /// are changed in place (clipped). The result never is null.
        /// </summary>
        public static ValidationReport Validate(IEnumerable<Archetype> archetypes, IEnumerable<AssignmentRecord> assignments, RivalConfig config)
        {
            var report = new ValidationReport();

            RivalConfig checkedConfig = config ?? RivalConfig.Defaults();
            var configMessages = new List<string>();
            checkedConfig.Normalize(configMessages);
            foreach (string line in configMessages) report.Warnings.Add("config: " + line);

            var acceptedArchetypes = new List<Archetype>();
            if (archetypes != null)
            {
                foreach (Archetype archetype in archetypes)
                {
                    var messages = new List<string>();
                    bool ok = ValidateArchetype(archetype, messages);
                    string where = "archetype '" + (archetype == null ? "" : archetype.id) + "': ";
                    foreach (string line in messages) (ok ? report.Warnings : report.Errors).Add(where + line);
                    if (!ok && archetype == null) report.Errors.Add(where + "the record is empty");
                    if (ok) acceptedArchetypes.Add(archetype);
                }
            }

            var acceptedAssignments = new List<AssignmentRecord>();
            if (assignments != null)
            {
                foreach (AssignmentRecord assignment in assignments)
                {
                    var messages = new List<string>();
                    bool ok = ValidateAssignment(assignment, messages);
                    string where = "assignment '" + (assignment == null ? "" : assignment.id) + "': ";
                    foreach (string line in messages) (ok ? report.Warnings : report.Errors).Add(where + line);
                    if (!ok && assignment == null) report.Errors.Add(where + "the record is empty");
                    if (ok) acceptedAssignments.Add(assignment);
                }
            }

            Build(acceptedArchetypes, acceptedAssignments, checkedConfig, report);
            return report;
        }

        // ---- cleaning of the parts ----

        private static Dictionary<string, List<ReactionRule>> CleanReactions(Dictionary<string, List<ReactionRule>> source, List<string> messages)
        {
            var result = new Dictionary<string, List<ReactionRule>>(StringComparer.Ordinal);
            if (source == null) return result;

            foreach (KeyValuePair<string, List<ReactionRule>> pair in source)
            {
                TriggerType trigger;
                if (!ReactionKinds.TryParseTrigger(pair.Key, out trigger))
                {
                    Say(messages, "reactions." + pair.Key + ": unknown trigger, the table is dropped");
                    continue;
                }

                var rules = new List<ReactionRule>();
                if (pair.Value != null)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        ReactionRule rule = pair.Value[i];
                        string path = "reactions." + pair.Key + "[" + i + "]";
                        if (rule == null) continue;

                        ReactionType type;
                        if (!ReactionKinds.TryParseReaction(rule.type, out type))
                        {
                            Say(messages, path + ".type: unknown reaction '" + rule.type + "', the row is dropped");
                            continue;
                        }
                        if (!ReactionKinds.IsTableReaction(type))
                        {
                            Say(messages, path + ".type: '" + rule.type + "' is not an answer to a trigger, the row is dropped");
                            continue;
                        }

                        Limits.Float(ref rule.@base, 0f, 0.9f, 0f, path + ".base", messages);
                        rules.Add(rule);
                    }
                }
                result[pair.Key] = rules;
            }
            return result;
        }

        private static Dictionary<string, Dictionary<string, float>> CleanReactionParams(Dictionary<string, Dictionary<string, float>> source, List<string> messages)
        {
            var result = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
            if (source == null) return result;

            foreach (KeyValuePair<string, Dictionary<string, float>> pair in source)
            {
                ReactionType type;
                if (!ReactionKinds.TryParseReaction(pair.Key, out type))
                {
                    Say(messages, "reactionParams." + pair.Key + ": unknown reaction, the entry is dropped");
                    continue;
                }

                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                if (pair.Value != null)
                {
                    foreach (KeyValuePair<string, float> item in pair.Value)
                    {
                        string path = "reactionParams." + pair.Key + "." + item.Key;
                        float min, max;
                        if (!TryParamLimits(type, item.Key, out min, out max))
                        {
                            Say(messages, path + ": unknown number of this reaction, it is dropped");
                            continue;
                        }

                        float value = item.Value;
                        Limits.Float(ref value, min, max, min, path, messages);
                        values[item.Key] = value;
                    }
                }
                result[pair.Key] = values;
            }
            return result;
        }

        /// <summary>The numbers an archetype may set for a reaction, with their ranges.</summary>
        private static bool TryParamLimits(ReactionType type, string name, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            switch (type)
            {
                case ReactionType.PriceCut:
                    if (name == "depth" || name == "floorMargin")
                    {
                        max = 0.5f;
                        return true;
                    }
                    return false;

                case ReactionType.CounterLaunch:
                    if (name == "approvalBoost" || name == "fundingBoost")
                    {
                        min = 1f;
                        max = 3f;
                        return true;
                    }
                    return false;

                case ReactionType.Poach:
                    if (name == "rivalResearchSpeed")
                    {
                        min = 1f;
                        max = 2f;
                        return true;
                    }
                    if (name == "rivalBoostDays")
                    {
                        min = 1f;
                        max = 720f;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        // ---- small helpers ----

        private static string FallbackId(Dictionary<string, Archetype> byId, List<Archetype> all)
        {
            if (byId.ContainsKey(DefaultArchetypeId)) return DefaultArchetypeId;
            return all.Count > 0 ? all[0].id : DefaultArchetypeId;
        }

        private static bool IsPlainId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
            foreach (char ch in id)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
                if (!ok) return false;
            }
            return true;
        }

        private static void AddUnknown(JsonObject json, string prefix, string[] known, List<string> messages)
        {
            if (json == null) return;
            foreach (KeyValuePair<string, JsonNode> pair in json)
            {
                if (Array.IndexOf(known, pair.Key) < 0) messages.Add(prefix + pair.Key + ": unknown member, it is ignored");
            }
        }

        private static void Say(List<string> messages, string text)
        {
            if (messages != null) messages.Add(text);
        }

        private static void Fail(ValidationReport report, string text)
        {
            if (report != null) report.Errors.Add(text);
        }

        private static void Warn(ValidationReport report, string text)
        {
            if (report != null) report.Warnings.Add(text);
        }
    }
}
