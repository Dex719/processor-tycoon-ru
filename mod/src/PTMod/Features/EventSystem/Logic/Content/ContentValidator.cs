using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The rules a mapped <see cref="EventDefinition"/> must satisfy before it enters the registry (FR-1 item 3, NFR-2; the <c>validate</c> function of
    /// <c>ModContent.LoadRecords</c>). One validator serves one pack: it knows the pack's owner feature (whose prefixes the ids must carry), asks
    /// <see cref="IContentEnvironment"/> about effect types and channels, and asks a function whether a category is defined.
    /// <list type="bullet">
    /// <item><b>Rejects</b> (errors): an id that is not <c>&lt;prefix&gt;.&lt;name&gt;</c> of the owner (<c>ModIds.IsEventIdAllowed</c>, AC-1.4); a <c>DailyChance</c> event
    /// without a defined category; a bad <c>Date</c>; a <c>Condition</c> trigger without <c>conditions</c>; a <c>Specific</c> target without a valid key; choice ids that are
    /// bad, repeated or <c>common</c>; an event with choices but no <c>timerDays</c> or no <c>defaultChoice</c>, a <c>defaultChoice</c> that is not a choice or has
    /// <c>requires</c>; an unknown effect type (AC-1.2); bad common parameters; an unknown modifier channel in <c>params.channel</c>; the nine standard types against <see cref="StandardEffectRules"/>; whatever the
    /// handler of the type rejects; exclusive groups whose chances add up to more than 1; a <c>FireEvent</c> of the event itself without a delay.</item>
    /// <item><b>Warns</b> (the record still loads): L10n keys outside the owner's prefixes, a category no pack defines for an event that is not a lottery one,
    /// settings that have no effect (<c>defaultChoice</c> without choices), a weight of 0 for a lottery event, unknown parameters of standard effects.</item>
    /// </list>
    /// What needs every pack loaded - a <c>FireEvent</c> that names an event nobody defines - is checked afterwards by <see cref="PackCatalog"/>.
    /// Errors are English lines <c>problem (where)</c>; a message list that gets any error means "rejected".
    /// </summary>
    public sealed class ContentValidator
    {
        /// <summary>First and last year of a <c>Date</c> trigger: the game's calendar starts on 1971-01-01.</summary>
        public const int FirstYear = 1971;

        public const int LastYear = 2100;

        /// <summary>Chances of one exclusive group may add up to this much more than 1 (floating-point noise of 0.1 + 0.2 + 0.7).</summary>
        private const double GroupTolerance = 1e-6;

        private static readonly string[] Scopes = { EffectScopes.Target, EffectScopes.Global };

        private readonly IContentEnvironment _env;
        private readonly string _packId;
        private readonly string _owner;
        private readonly Func<string, bool> _isCategoryDefined;

        /// <param name="env">What the running mod knows (effect types, channels, money origins).</param>
        /// <param name="packId">The pack the records come from, for messages.</param>
        /// <param name="ownerFeature">The feature whose prefixes the event ids and L10n keys of the pack carry.</param>
        /// <param name="isCategoryDefined">Whether some pack's <c>config.json</c> defines the category; null - every category is defined.</param>
        public ContentValidator(IContentEnvironment env, string packId, string ownerFeature, Func<string, bool> isCategoryDefined)
        {
            if (env == null) throw new ArgumentNullException("env");
            _env = env;
            _packId = packId ?? string.Empty;
            _owner = ownerFeature ?? string.Empty;
            _isCategoryDefined = isCategoryDefined;
        }

        public string PackId
        {
            get { return _packId; }
        }

        public string OwnerFeature
        {
            get { return _owner; }
        }

        /// <summary>
        /// Checks one definition. False when it must be skipped: <paramref name="messages"/> then holds the reasons (errors only). True: the
        /// definition is accepted, and <paramref name="messages"/> holds the warnings, if any.
        /// </summary>
        public bool Validate(EventDefinition definition, List<string> messages)
        {
            var pass = new Pass(this, definition);
            pass.Run();

            if (messages != null)
            {
                messages.AddRange(pass.Errors);
                if (pass.Errors.Count == 0) messages.AddRange(pass.Warnings);
            }
            return pass.Errors.Count == 0;
        }

        private sealed class Pass
        {
            private readonly ContentValidator _v;
            private readonly EventDefinition _def;

            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();

            public Pass(ContentValidator validator, EventDefinition definition)
            {
                _v = validator;
                _def = definition;
            }

            private void Error(string where, string problem)
            {
                Errors.Add(problem + " (" + where + ")");
            }

            private void Warn(string where, string problem)
            {
                Warnings.Add(problem + " (" + where + ")");
            }

            public void Run()
            {
                if (_def == null)
                {
                    Errors.Add("the record could not be read");
                    return;
                }

                CheckId();
                CheckTexts();
                CheckTrigger();
                CheckCategory();
                CheckTargets();
                CheckChoicesAndTimer();
                CheckEffects(_def.effects, "effects");
            }

            private bool IsLottery
            {
                get { return _def.trigger != null && _def.trigger.type == EventTriggerType.DailyChance; }
            }

            // ---- identity, texts, category ----

            private void CheckId()
            {
                if (!ContentNames.IsName(_def.id, ContentNames.MaxIdLength))
                {
                    Error("id", "event id '" + _def.id + "' may use letters, digits, '_', '-' and '.' only, up to " + ContentNames.MaxIdLength + " characters");
                    return;
                }
                if (!ModIds.IsEventIdAllowed(_v._owner, _def.id)) Error("id", ModIds.Explain(IdKind.EventId, _v._owner, _def.id));
            }

            private void CheckTexts()
            {
                CheckKey(_def.title, "title");
                CheckKey(_def.text, "text");
                CheckKey(_def.cause, "cause");
            }

            private void CheckKey(LocText text, string where)
            {
                if (text == null || string.IsNullOrEmpty(text.Key)) return;
                if (!ModIds.IsL10nKeyAllowed(_v._owner, text.Key)) Warn(where, ModIds.Explain(IdKind.L10nKey, _v._owner, text.Key));
            }

            private void CheckCategory()
            {
                string category = _def.category;
                if (string.IsNullOrEmpty(category))
                {
                    if (IsLottery) Error("category", "a DailyChance event needs a 'category'");
                    return;
                }

                if (!ContentNames.IsName(category, ContentNames.MaxShortLength))
                {
                    Error("category", "category '" + category + "' may use letters, digits, '_', '-' and '.' only, up to " + ContentNames.MaxShortLength + " characters");
                    return;
                }
                if (_v._isCategoryDefined == null || _v._isCategoryDefined(category)) return;

                if (IsLottery) Error("category", "unknown category '" + category + "': define it in the config.json of a pack");
                else Warn("category", "category '" + category + "' is not defined by any pack, no pacing rules apply to it");
            }

            // ---- trigger, targets ----

            private void CheckTrigger()
            {
                EventTrigger trigger = _def.trigger;
                if (trigger == null)
                {
                    Error("trigger", "'trigger' is required");
                    return;
                }

                switch (trigger.type)
                {
                    case EventTriggerType.Date:
                        if (!IsCalendarDate(trigger.year, trigger.month, trigger.day))
                        {
                            Error("trigger", "a Date trigger needs a calendar date from " + FirstYear + " to " + LastYear + ", got "
                                             + trigger.year + "-" + trigger.month + "-" + trigger.day);
                        }
                        break;

                    case EventTriggerType.Condition:
                        if (_def.conditions == null) Error("conditions", "a Condition trigger needs 'conditions'");
                        if (trigger.chance <= 0f) Warn("trigger.chance", "chance 0: the event never fires");
                        break;

                    case EventTriggerType.DailyChance:
                        if (_def.weight <= 0f) Warn("weight", "weight 0: the lottery never picks the event");
                        break;
                }
            }

            private static bool IsCalendarDate(int year, int month, int day)
            {
                if (year < FirstYear || year > LastYear || month < 1 || month > 12) return false;
                return day >= 1 && day <= DateTime.DaysInMonth(year, month);
            }

            private void CheckTargets()
            {
                EventTargetSpec targets = _def.targets;
                if (targets == null) return;

                if (targets.type == EventTargetType.Specific)
                {
                    if (string.IsNullOrEmpty(targets.companyKey)) Error("targets.companyKey", "a Specific target needs 'companyKey' ('player' or 'ai:<id>')");
                    else if (!CompanyKey.IsValid(targets.companyKey)) Error("targets.companyKey", "company key '" + targets.companyKey + "' is neither 'player' nor 'ai:<digits>'");
                }
                else if (!string.IsNullOrEmpty(targets.companyKey))
                {
                    Warn("targets.companyKey", "'companyKey' is only used by a Specific target");
                }
            }

            // ---- choices, timer ----

            private void CheckChoicesAndTimer()
            {
                EventChoice[] choices = _def.choices ?? new EventChoice[0];
                var seen = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < choices.Length; i++)
                {
                    EventChoice choice = choices[i];
                    string where = "choices[" + i + "]";
                    if (choice == null)
                    {
                        Error(where, "the choice is empty");
                        continue;
                    }

                    if (!ContentNames.IsChoiceId(choice.id))
                    {
                        Error(where + ".id", "choice id '" + choice.id + "' may use letters, digits, '_' and '-' only, up to " + ContentNames.MaxShortLength + " characters");
                    }
                    else if (string.Equals(choice.id, ContentNames.CommonChoice, StringComparison.Ordinal))
                    {
                        Error(where + ".id", "choice id '" + ContentNames.CommonChoice + "' is reserved for the effects of the event itself");
                    }
                    else if (!seen.Add(choice.id))
                    {
                        Error(where + ".id", "duplicate choice id '" + choice.id + "'");
                    }

                    if (!string.IsNullOrEmpty(choice.textKey) && !ModIds.IsL10nKeyAllowed(_v._owner, choice.textKey))
                    {
                        Warn(where + ".textKey", ModIds.Explain(IdKind.L10nKey, _v._owner, choice.textKey));
                    }

                    CheckEffects(choice.effects, where + ".effects");
                }

                if (choices.Length == 0)
                {
                    if (!string.IsNullOrEmpty(_def.defaultChoice)) Warn("defaultChoice", "'defaultChoice' is ignored: the event has no choices");
                    if (_def.timerDays > 0) Warn("timerDays", "'timerDays' is ignored: the event has no choices");
                    return;
                }

                if (_def.timerDays < 1) Error("timerDays", "an event with choices needs 'timerDays' of at least 1");
                CheckDefaultChoice(choices);
            }

            private void CheckDefaultChoice(EventChoice[] choices)
            {
                string name = _def.defaultChoice;
                if (string.IsNullOrEmpty(name))
                {
                    Error("defaultChoice", "an event with choices needs 'defaultChoice': the timer and the AI companies fall back to it");
                    return;
                }

                EventChoice found = null;
                foreach (EventChoice choice in choices)
                {
                    if (choice != null && string.Equals(choice.id, name, StringComparison.Ordinal))
                    {
                        found = choice;
                        break;
                    }
                }

                if (found == null) Error("defaultChoice", "defaultChoice '" + name + "' is not one of the choices");
                else if (found.requires != null) Error("defaultChoice", "defaultChoice '" + name + "' has 'requires': the default choice must always be available");
            }

            // ---- effects ----

            private void CheckEffects(EventEffect[] effects, string path)
            {
                if (effects == null) return;

                var groups = new Dictionary<string, double>(StringComparer.Ordinal);
                for (int i = 0; i < effects.Length; i++) CheckEffect(effects[i], path + "[" + i + "]", groups);

                foreach (KeyValuePair<string, double> group in groups)
                {
                    if (group.Value > 1.0 + GroupTolerance)
                    {
                        Error(path, "exclusive group '" + group.Key + "' has chances adding up to " + RecordReader.Num(group.Value) + ", the sum must be at most 1");
                    }
                }
            }

            private void CheckEffect(EventEffect effect, string path, Dictionary<string, double> groups)
            {
                if (effect == null || string.IsNullOrEmpty(effect.type))
                {
                    Error(path, "the effect has no 'type'");
                    return;
                }
                if (!_v._env.IsEffectTypeRegistered(effect.type))
                {
                    Error(path + ".type", "unknown effect type '" + effect.type + "'");
                    return;
                }

                JsonObject raw = effect.@params != null && effect.@params.Raw != null ? effect.@params.Raw : new JsonObject();
                CheckCommonParams(raw, path + ".params", groups);

                StandardEffectRules.Check(effect, _v._env, path, Errors, Warnings);
                if (!StandardEffectRules.IsStandard(effect.type)) CheckChannelParam(raw, path + ".params");
                if (string.Equals(effect.type, StandardEffectRules.FireEvent, StringComparison.Ordinal)) CheckSelfFire(effect, path);

                var fromHandler = new List<string>();
                bool accepted = _v._env.ValidateWithHandler(effect, fromHandler);
                foreach (string line in fromHandler) Error(path, line);
                if (!accepted && fromHandler.Count == 0) Error(path, "the handler of '" + effect.type + "' rejected the effect");
            }

            /// <summary>
            /// The effect types of the packs (<c>ModifierRamp</c>, <c>ModifierWave</c>) name a modifier channel in <c>params.channel</c> like <c>ModifierTimed</c>
            /// does: an unknown one rejects the record (FR-1 item 3). A <c>channel</c> that is not a string is the handler's business.
            /// </summary>
            private void CheckChannelParam(JsonObject raw, string at)
            {
                var channel = Present(raw, "channel") as JsonValue;
                if (channel == null || channel.Kind != JsonKind.String || channel.StringValue.Length == 0) return;
                if (!_v._env.IsChannelDeclared(channel.StringValue))
                {
                    Error(at + ".channel", "unknown channel '" + channel.StringValue + "': ModModifiers does not know it (core.json or ModModifiers.DeclareChannel)");
                }
            }

            /// <summary>An event that fires itself must wait: with no delay it would loop within the day.</summary>
            private void CheckSelfFire(EventEffect effect, string path)
            {
                string target = effect.@params.GetString("eventId", null);
                if (!string.Equals(target, _def.id, StringComparison.Ordinal)) return;

                FloatRange delay = effect.@params.GetRange("delayDays", new FloatRange(0f, 0f));
                if (delay.Lo < 1f) Error(path + ".params.delayDays", "an event cannot fire itself without a delay of at least 1 day");
            }

            /// <summary>The keys every effect has: scope, chance, exclusiveGroup, scalable (<see cref="EffectParamKeys"/>).</summary>
            private void CheckCommonParams(JsonObject raw, string at, Dictionary<string, double> groups)
            {
                JsonNode node = Present(raw, EffectParamKeys.Scope);
                if (node != null && !IsScope(node))
                {
                    Error(at + "." + EffectParamKeys.Scope, "'scope' must be " + string.Join(" or ", Scopes) + ", got " + RecordReader.Describe(node));
                }

                double chance = 1.0;
                node = Present(raw, EffectParamKeys.Chance);
                if (node != null)
                {
                    if (!RecordReader.TryNumber(node, out chance) || chance < 0.0 || chance > 1.0)
                    {
                        Error(at + "." + EffectParamKeys.Chance, "'chance' must be a number from 0 to 1, got " + RecordReader.Describe(node));
                        chance = 1.0;
                    }
                }

                node = Present(raw, EffectParamKeys.ExclusiveGroup);
                if (node != null)
                {
                    var name = node as JsonValue;
                    if (name == null || name.Kind != JsonKind.String || !ContentNames.IsName(name.StringValue, ContentNames.MaxShortLength))
                    {
                        Error(at + "." + EffectParamKeys.ExclusiveGroup, "'exclusiveGroup' must be a name of letters, digits, '_', '-' and '.', got " + RecordReader.Describe(node));
                    }
                    else
                    {
                        double sum;
                        groups.TryGetValue(name.StringValue, out sum);
                        groups[name.StringValue] = sum + chance;
                    }
                }

                node = Present(raw, EffectParamKeys.Scalable);
                if (node != null)
                {
                    var flag = node as JsonValue;
                    if (flag == null || flag.Kind != JsonKind.Bool) Error(at + "." + EffectParamKeys.Scalable, "'scalable' must be true or false, got " + RecordReader.Describe(node));
                }
            }

            private static JsonNode Present(JsonObject raw, string key)
            {
                JsonNode node = raw[key];
                return node == null || node.IsNull ? null : node;
            }

            private static bool IsScope(JsonNode node)
            {
                var value = node as JsonValue;
                if (value == null || value.Kind != JsonKind.String) return false;
                foreach (string scope in Scopes)
                {
                    if (string.Equals(scope, value.StringValue, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }
    }
}
