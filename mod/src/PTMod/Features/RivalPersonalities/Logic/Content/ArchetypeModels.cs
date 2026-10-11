using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    // The typed form of the content files archetypes.json and assignments.json (design.md, "Контент: archetypes.json", "assignments.json").
    // The fields are public and mirror the JSON member names (the case matters): ArchetypeCatalog.MapArchetype fills them with ModJson.Bind,
    // and ArchetypeCatalog.ValidateArchetype then clips the numbers and drops what the feature does not know. Nothing here touches Unity or the game.

    /// <summary>The way an archetype wants its designers to weigh the focus of a design (<c>behaviour.designFocusOverride</c>).</summary>
    public enum DesignFocusOverride
    {
        /// <summary>No change: the designers keep the focus of the game's prefab.</summary>
        None = 0,

        /// <summary>High performance first.</summary>
        HighPerformance = 1,

        /// <summary>Low unit cost first.</summary>
        LowUnitCost = 2,
    }

    /// <summary>The way an archetype wants its designers to weigh the power consumption (<c>behaviour.powerFocusOverride</c>).</summary>
    public enum PowerFocusOverride
    {
        /// <summary>No change: the designers keep the focus of the game's prefab.</summary>
        None = 0,

        /// <summary>Power consumption does not matter.</summary>
        Irrelevant = 1,

        /// <summary>Low power consumption first.</summary>
        LowConsumption = 2,
    }

    /// <summary>How a rival chooses in the events of the event system (<c>eventStyle</c>, FR-14).</summary>
    public enum EventStyle
    {
        /// <summary>No preference: the default policy of the event system decides.</summary>
        Neutral = 0,

        /// <summary>Prefers the more expensive options.</summary>
        Spend = 1,

        /// <summary>Prefers the cheaper options.</summary>
        Save = 2,
    }

    /// <summary>
    /// One row of a reaction table: when the trigger happens, the rival may answer with <see cref="type"/> with the base chance
    /// <c>base</c>. The rows of one trigger are tried in the order of the table and the first success wins (FR-6 item 1).
    /// </summary>
    public sealed class ReactionRule
    {
        /// <summary>The name of a <see cref="ReactionType"/> (not <c>Partnership</c>): <c>PriceCut</c>, <c>CounterLaunch</c>, <c>OemPush</c>, <c>Poach</c>, <c>Smear</c>.</summary>
        public string type;

        /// <summary>The base chance before the attitude and the difficulty are applied, 0..0.9.</summary>
        public float @base;

        /// <summary>The reaction is allowed whatever the attitude is (otherwise the attitude has to be at most <c>attitude.hostileGate</c>).</summary>
        public bool anyAttitude;

        /// <summary>The reaction of a rule that passed validation. <see cref="ReactionType.PriceCut"/> for a name that is not known (a validated rule never has one).</summary>
        public ReactionType Kind
        {
            get
            {
                ReactionType kind;
                return ReactionKinds.TryParseReaction(type, out kind) ? kind : ReactionType.PriceCut;
            }
        }
    }

    /// <summary>The changes an archetype makes to the AI of a company (<c>behaviour</c>, FR-3).</summary>
    public sealed class ArchetypeBehaviour
    {
        /// <summary>The multiplier of the design rating that the AI compares with its approval threshold, 0.8..1.3.</summary>
        public float approvalBias = 1f;

        /// <summary>The multiplier of the maximal research funding, 0.7..1.3.</summary>
        public float researchFundingMult = 1f;

        /// <summary>The multiplier of the launch price of a new CPU, 0.85..1.15.</summary>
        public float launchPriceMult = 1f;

        public DesignFocusOverride designFocusOverride = DesignFocusOverride.None;
        public PowerFocusOverride powerFocusOverride = PowerFocusOverride.None;

        /// <summary>A foundry partner takes points off the markup in negotiations with the player (FR-8 item 5).</summary>
        public bool negotiationDiscount;
    }

    /// <summary>How an archetype feels about the player (<c>attitude</c>, FR-4).</summary>
    public sealed class ArchetypeAttitude
    {
        /// <summary>The attitude the rival drifts to and starts with, -50..+50.</summary>
        public float baseline;

        /// <summary>The multiplier of every change of the attitude, 0.3..2.0.</summary>
        public float sensitivity = 1f;
    }

    /// <summary>
    /// A character of a rival (FR-1): a record of <c>archetypes.json</c>. Read-only for the rest of the feature after
    /// <see cref="ArchetypeCatalog"/> accepted it.
    /// </summary>
    public sealed class Archetype : IContentRecord
    {
        /// <summary>The technical id (<c>hegemon</c>); never changes. Required.</summary>
        public string id;

        /// <summary>The name for the player: a key of the string tables, or <c>{ "ru": ..., "en": ... }</c>. Read it through <see cref="Name"/>.</summary>
        public JsonNode name;

        /// <summary>The description for the player; same forms as <see cref="name"/>. Read it through <see cref="Description"/>.</summary>
        public JsonNode description;

        public ArchetypeBehaviour behaviour = new ArchetypeBehaviour();
        public ArchetypeAttitude attitude = new ArchetypeAttitude();

        /// <summary>Days before the same rival may start another reaction, 60..720.</summary>
        public int cooldownDays = 180;

        /// <summary>The reaction tables: the name of a <see cref="TriggerType"/> to the rows tried in order.</summary>
        public Dictionary<string, List<ReactionRule>> reactions = new Dictionary<string, List<ReactionRule>>();

        /// <summary>
        /// The numbers of a reaction that differ from the defaults of <c>config.json</c>: the name of a <see cref="ReactionType"/> to a table of
        /// named numbers (<c>PriceCut: { depth: 0.15 }</c>). Read it through <see cref="ParamOr"/>.
        /// </summary>
        public Dictionary<string, Dictionary<string, float>> reactionParams = new Dictionary<string, Dictionary<string, float>>();

        public EventStyle eventStyle = EventStyle.Neutral;

        /// <summary>The multipliers of the AI answers to a design flaw: [Recall, Stepping, Patch, Ignore], each 0.5..2.0 (FR-13 item 5).</summary>
        public float[] flawResponseBias = { 1f, 1f, 1f, 1f };

        private LocText _name;
        private LocText _description;

        /// <summary><see cref="IContentRecord"/>: the same as <see cref="id"/>.</summary>
        public string Id
        {
            get { return id; }
        }

        /// <summary>
        /// The name as a text for <c>ModL10n.Resolve</c>. A name that is missing or unreadable falls back to the key
        /// <c>rival.archetype.&lt;id&gt;.name</c>.
        /// </summary>
        public LocText Name
        {
            get
            {
                if (_name == null) _name = TextOf(name, "name");
                return _name;
            }
        }

        /// <summary>The description as a text for <c>ModL10n.Resolve</c>; falls back to <c>rival.archetype.&lt;id&gt;.desc</c>.</summary>
        public LocText Description
        {
            get
            {
                if (_description == null) _description = TextOf(description, "desc");
                return _description;
            }
        }

        /// <summary>
        /// The rows for a trigger in table order; an empty list when the archetype does not answer that trigger. Never null. The caller must not
        /// change the list.
        /// </summary>
        public IReadOnlyList<ReactionRule> RulesFor(TriggerType trigger)
        {
            List<ReactionRule> rules;
            if (reactions != null && reactions.TryGetValue(ReactionKinds.NameOf(trigger), out rules) && rules != null) return rules;
            return NoRules;
        }

        /// <summary>The number <paramref name="name"/> that this archetype sets for a reaction, or <paramref name="fallback"/> (the default of <c>config.json</c>).</summary>
        public float ParamOr(ReactionType reaction, string name, float fallback)
        {
            Dictionary<string, float> values;
            float value;
            if (reactionParams != null && reactionParams.TryGetValue(ReactionKinds.NameOf(reaction), out values) && values != null
                && values.TryGetValue(name ?? string.Empty, out value))
            {
                return value;
            }
            return fallback;
        }

        private static readonly ReactionRule[] NoRules = new ReactionRule[0];

        private LocText TextOf(JsonNode node, string suffix)
        {
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String && !string.IsNullOrEmpty(scalar.StringValue))
            {
                return LocText.FromKey(scalar.StringValue);
            }

            if (node is JsonObject)
            {
                LocText text = LocText.FromJson(node, "$." + suffix, null);
                if (text != null && !text.IsEmpty) return text;
            }
            return LocText.FromKey("rival.archetype." + id + "." + suffix);
        }
    }

    /// <summary>
    /// The default character of one company (FR-2): a record of <c>assignments.json</c>. The company is the <see cref="uniqueId"/> of the game's
    /// <c>AICompany</c>; <see cref="key"/> is the stable machine name of the historical company, for readable content and overrides. The names
    /// of the companies are not copied into the content (they come from the game).
    /// </summary>
    public sealed class AssignmentRecord : IContentRecord
    {
        /// <summary>The id of the record for the content merger; the shipped file uses the same text as <see cref="key"/>.</summary>
        public string id;

        /// <summary>The <c>UniqueID</c> of the AI company. Required, 0 or more; -1 means "not given".</summary>
        public int uniqueId = -1;

        /// <summary>The machine name of the historical company (<c>intel</c>, <c>amd</c>). A record without it is accepted with a warning.</summary>
        public string key;

        /// <summary>The id of the archetype. A missing or unknown one is replaced by <see cref="ArchetypeCatalog.DefaultArchetypeId"/>.</summary>
        public string archetype;

        /// <summary><see cref="IContentRecord"/>: the same as <see cref="id"/>.</summary>
        public string Id
        {
            get { return id; }
        }
    }
}
