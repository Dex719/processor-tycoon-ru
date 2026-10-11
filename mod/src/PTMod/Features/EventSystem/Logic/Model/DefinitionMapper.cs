using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <c>JsonObject</c> to <see cref="EventDefinition"/> (FR-1; the <c>map</c> function of <c>ModContent.LoadRecords</c>). The mapper reads the shape:
    /// kinds, names of enums, ranges of single numbers, texts and the three condition nodes (<c>conditions</c>, <c>targets.filter</c>,
    /// <c>choices[].requires</c>, parsed by the function given to the constructor - <c>ModConditions.Parse</c> in the game). Everything that needs
    /// the world - the owner prefix, the registered effect types, the channels, the links between fields - belongs to <see cref="ContentValidator"/>.
    /// <list type="bullet">
    /// <item>Any mistake rejects the whole record (null) with every line collected, not just the first; the author fixes a record in one go.</item>
    /// <item>A field nobody knows is a warning (it comes back in <c>messages</c> of an accepted record, and <c>ContentMerger</c> keeps it as a warning).</item>
    /// <item>Missing optional fields get the defaults of the contract: weight 1, no cooldown, target the player, no choices, no effects.</item>
    /// </list>
    /// Never throws for content; a null or foreign node is a message.
    /// </summary>
    public sealed class DefinitionMapper
    {
        /// <summary>The largest span of days a field of a record may name: 100 years.</summary>
        public const int MaxDays = 36500;

        /// <summary>The largest <c>timerDays</c>.</summary>
        public const int MaxTimerDays = 3650;

        /// <summary>The largest <c>weight</c>, <c>aiWeight</c> and <c>aiMinCashMultiple</c>: a guard against typos (1e9), not a design limit.</summary>
        public const float MaxWeight = 1000000f;

        private static readonly string[] CheckEveryNames = { "Day", "Month" };

        private readonly Func<JsonNode, List<string>, string, ICondition> _parseCondition;

        /// <param name="parseCondition">Parses a condition node: <c>(node, errors, path)</c>, null with errors for a bad one (<c>ModConditions.Parse</c>).</param>
        public DefinitionMapper(Func<JsonNode, List<string>, string, ICondition> parseCondition)
        {
            if (parseCondition == null) throw new ArgumentNullException("parseCondition");
            _parseCondition = parseCondition;
        }

        /// <summary>
        /// Builds the definition of one record. Returns null and adds the reasons to <paramref name="messages"/> when the record is unusable;
        /// otherwise returns the definition, and <paramref name="messages"/> gets the warnings (unknown fields). <c>packId</c> is not set here.
        /// </summary>
        public EventDefinition Map(JsonObject json, List<string> messages)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            EventDefinition definition = null;
            if (json == null) errors.Add("the record is not a JSON object");
            else definition = Read(json, errors, warnings);

            if (errors.Count > 0)
            {
                if (messages != null) messages.AddRange(errors);
                return null;
            }

            if (messages != null) messages.AddRange(warnings);
            return definition;
        }

        private EventDefinition Read(JsonObject json, List<string> errors, List<string> warnings)
        {
            var r = new RecordReader(json, string.Empty, errors, warnings);
            var definition = new EventDefinition();

            definition.id = r.Str("id", true);
            definition.category = r.Str("category", false);
            definition.title = r.Loc("title", true);
            definition.text = r.Loc("text", true);
            definition.cause = r.Loc("cause", false);
            definition.trigger = ReadTrigger(r, errors, warnings);
            definition.conditions = ReadCondition(r, "conditions", errors);
            definition.weight = r.Float("weight", 1f, 0f, MaxWeight);
            definition.cooldownDays = r.Int("cooldownDays", 0, 0, MaxDays);
            definition.oncePerGame = r.Bool("oncePerGame", false);
            definition.targets = ReadTargets(r, errors, warnings);
            definition.choices = ReadChoices(r, errors, warnings);
            definition.timerDays = r.Int("timerDays", 0, 0, MaxTimerDays);
            definition.defaultChoice = r.Str("defaultChoice", false);
            definition.effects = ReadEffects(r.Arr("effects"), r.PathOf("effects"), errors, warnings);
            definition.important = r.Bool("important", false);
            definition.notifyPlayer = r.Bool("notifyPlayer", false);

            r.ReportUnknown("disabled");
            return definition;
        }

        private static EventTrigger ReadTrigger(RecordReader parent, List<string> errors, List<string> warnings)
        {
            JsonObject node = parent.Obj("trigger", true);
            if (node == null) return null;

            var r = new RecordReader(node, parent.PathOf("trigger"), errors, warnings);
            var trigger = new EventTrigger();

            EventTriggerType type;
            r.EnumValue("type", true, EventTriggerType.Manual, out type);
            trigger.type = type;
            trigger.year = r.Int("year", 0, 0, 9999);
            trigger.month = r.Int("month", 0, 0, 99);
            trigger.day = r.Int("day", 0, 0, 99);
            trigger.graceDays = r.Int("graceDays", trigger.graceDays, 0, MaxTimerDays);
            trigger.chance = r.Float("chance", 1f, 0f, 1f);
            trigger.edge = r.Bool("edge", false);

            string every = r.Str("checkEvery", false);
            if (every != null)
            {
                string canonical = Canonical(CheckEveryNames, every);
                if (canonical == null) r.Error("checkEvery", "'checkEvery' must be one of " + string.Join(", ", CheckEveryNames) + ", got '" + every + "'");
                else trigger.checkEvery = canonical;
            }

            r.ReportUnknown();
            return trigger;
        }

        private EventTargetSpec ReadTargets(RecordReader parent, List<string> errors, List<string> warnings)
        {
            var spec = new EventTargetSpec { type = EventTargetType.Player, weighting = TargetWeighting.Uniform };

            JsonObject node = parent.Obj("targets", false);
            if (node == null) return spec;   // no 'targets': the player

            var r = new RecordReader(node, parent.PathOf("targets"), errors, warnings);
            EventTargetType type;
            r.EnumValue("type", true, EventTargetType.Player, out type);
            spec.type = type;
            spec.companyKey = r.Str("companyKey", false);
            spec.filter = ReadCondition(r, "filter", errors);
            TargetWeighting weighting;
            r.EnumValue("weighting", false, TargetWeighting.Uniform, out weighting);
            spec.weighting = weighting;

            r.ReportUnknown();
            return spec;
        }

        private EventChoice[] ReadChoices(RecordReader parent, List<string> errors, List<string> warnings)
        {
            JsonArray array = parent.Arr("choices");
            if (array == null) return new EventChoice[0];

            string path = parent.PathOf("choices");
            var choices = new List<EventChoice>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = RecordReader.IndexPath(path, i);
                var node = array[i] as JsonObject;
                if (node == null)
                {
                    errors.Add("a choice must be an object, got " + RecordReader.KindName(array[i]) + " (" + itemPath + ")");
                    continue;
                }

                var r = new RecordReader(node, itemPath, errors, warnings);
                var choice = new EventChoice();
                choice.id = r.Str("id", true);
                choice.textKey = r.Str("textKey", true);
                choice.effects = ReadEffects(r.Arr("effects"), r.PathOf("effects"), errors, warnings);
                choice.requires = ReadCondition(r, "requires", errors);
                choice.aiWeight = r.Float("aiWeight", 1f, 0f, MaxWeight);
                choice.aiMinCashMultiple = r.Float("aiMinCashMultiple", 0f, 0f, MaxWeight);
                r.ReportUnknown();
                choices.Add(choice);
            }
            return choices.ToArray();
        }

        private static EventEffect[] ReadEffects(JsonArray array, string path, List<string> errors, List<string> warnings)
        {
            if (array == null) return new EventEffect[0];

            var effects = new List<EventEffect>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = RecordReader.IndexPath(path, i);
                var node = array[i] as JsonObject;
                if (node == null)
                {
                    errors.Add("an effect must be an object, got " + RecordReader.KindName(array[i]) + " (" + itemPath + ")");
                    continue;
                }

                var r = new RecordReader(node, itemPath, errors, warnings);
                var effect = new EventEffect();
                effect.type = r.Str("type", true);
                effect.@params = new EffectParams(r.Obj("params", false));
                effect.durationDays = r.Int("durationDays", 0, 0, MaxDays);
                r.ReportUnknown();
                effects.Add(effect);
            }
            return effects.ToArray();
        }

        private ICondition ReadCondition(RecordReader r, string field, List<string> errors)
        {
            JsonNode node = r.Node(field);
            if (node == null) return null;

            var problems = new List<string>();
            ICondition condition = _parseCondition(node, problems, r.PathOf(field));
            if (condition != null && problems.Count == 0) return condition;

            if (problems.Count == 0) problems.Add(r.PathOf(field) + ": the condition could not be parsed");
            errors.AddRange(problems);
            return null;
        }

        private static string Canonical(string[] names, string text)
        {
            foreach (string name in names)
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return name;
            }
            return null;
        }
    }
}
