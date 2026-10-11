using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>How serious a finding of the self-check is.</summary>
    public enum EventCheckLevel
    {
        /// <summary>The engine works, a part of the content or of the strings does not (a text missing in one language, a channel nobody declared).</summary>
        Warning = 0,

        /// <summary>Something the engine stands on is wrong: an effect with no handler, a chain to nowhere, an id or a predicate under the wrong prefix.</summary>
        Error = 1,
    }

    /// <summary>One finding: the part of the engine it is about and what is wrong, in English, for the log.</summary>
    public sealed class EventCheckProblem
    {
        public EventCheckProblem(EventCheckLevel level, string area, string message)
        {
            Level = level;
            Area = area ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public EventCheckLevel Level { get; private set; }

        /// <summary><c>types</c>, <c>channels</c>, <c>events</c>, <c>ids</c>, <c>predicates</c>, <c>strings</c> or <c>providers</c>.</summary>
        public string Area { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return Area + ": " + Message;
        }
    }

    /// <summary>
    /// Everything the self-check looks at, as plain data and questions the game answers (TSK-026; NFR-2, NFR-4): the events the registry holds, the effect types the
    /// engine has handlers for, whether a channel is declared in the registry of the core, the prefix rules of <c>ModIds</c>, the string tables of both languages and
    /// the number of providers the aggregator put into <c>ModModifiers</c> per channel. <c>EventSelfCheckRunner</c> fills it from the running mod; the tests make it up.
    /// There is nothing about patches in it: the engine declares none.
    /// </summary>
    public sealed class EventCheckFacts
    {
        /// <summary>Every event of every pack that the registry holds (<c>PackCatalog.All</c>).</summary>
        public IList<EventDefinition> Definitions = new EventDefinition[0];

        /// <summary>The feature whose prefixes the ids of the event's pack carry (<c>PackInfo.Owner</c>); null - the pack id.</summary>
        public Func<EventDefinition, string> OwnerOf = definition => definition.packId;

        /// <summary>The effect types that have a handler (<c>EventEffects.RegisteredTypes</c>).</summary>
        public IList<string> EffectTypes = new string[0];

        /// <summary><c>ModModifiers.IsDeclared</c>.</summary>
        public Func<string, bool> ChannelDeclared = channel => true;

        /// <summary><c>ModIds.IsEventIdAllowed(owner, id)</c>.</summary>
        public Func<string, string, bool> EventIdAllowed = (owner, id) => true;

        /// <summary>The predicates the engine registers itself (<c>BuiltinPredicates.Names</c>).</summary>
        public IList<string> EnginePredicates = BuiltinPredicates.Names;

        /// <summary>The predicate names <c>ModConditions</c> knows (<c>ModConditions.KnownTypes</c>).</summary>
        public ICollection<string> KnownPredicates = new string[0];

        /// <summary><c>ModIds.IsPredicateNameAllowed("event-system", name)</c>.</summary>
        public Func<string, bool> PredicateNameAllowed = name => true;

        /// <summary><c>ModIds.IsL10nKeyAllowed("event-system", key)</c>.</summary>
        public Func<string, bool> KeyAllowed = key => true;

        /// <summary>The key is in the Russian table.</summary>
        public Func<string, bool> HasRu = key => true;

        /// <summary>The key is in the English table.</summary>
        public Func<string, bool> HasEn = key => true;

        /// <summary>How many times a provider was registered in <c>ModModifiers</c>, per channel (the aggregator registers one per channel, ever).</summary>
        public IDictionary<string, int> ProviderRegistrations = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>What the self-check came to: the findings and the one line for the log.</summary>
    public sealed class EventCheckVerdict
    {
        internal EventCheckVerdict(IList<EventCheckProblem> problems, int events, int effectTypes)
        {
            Problems = new List<EventCheckProblem>(problems).AsReadOnly();
            Events = events;
            EffectTypes = effectTypes;
        }

        public IList<EventCheckProblem> Problems { get; private set; }

        /// <summary>Events in the registry (all packs).</summary>
        public int Events { get; private set; }

        /// <summary>Effect types with a handler.</summary>
        public int EffectTypes { get; private set; }

        public bool Ok
        {
            get { return Problems.Count == 0; }
        }

        public bool HasErrors
        {
            get
            {
                foreach (EventCheckProblem problem in Problems)
                {
                    if (problem.Level == EventCheckLevel.Error) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The summary in one line: <c>selfcheck OK: 10 events, 9 effect types</c>, or, with findings, <c>selfcheck found 2 problems: types: ...; strings: ...</c>
        /// (the same findings are the lines of <see cref="Problems"/>, one each).
        /// </summary>
        public string Line
        {
            get
            {
                if (Ok) return "selfcheck OK: " + Events + " events, " + EffectTypes + " effect types";

                var sb = new StringBuilder("selfcheck found ").Append(Problems.Count).Append(Problems.Count == 1 ? " problem" : " problems").Append(": ");
                for (int i = 0; i < Problems.Count; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(Problems[i]);
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// The rules of the self-check of the engine at start (TSK-026; NFR-2, NFR-4; design.md "Самопроверка на старте"). Pure: <see cref="Evaluate"/> judges the
    /// <see cref="EventCheckFacts"/> that <c>EventSelfCheckRunner</c> reads from the game. It checks that
    /// <list type="bullet">
    /// <item>the nine standard effect types and every type the events use have a handler (<c>types</c>);</item>
    /// <item>the channels the engine writes and the channels the events name are declared in the registry of the core (<c>channels</c>, a warning: an effect on a channel
    /// nobody declared has no effect);</item>
    /// <item>every <c>FireEvent</c> names an event that exists (<c>events</c>);</item>
    /// <item>the ids of the events carry a prefix of the feature that owns their pack, and the predicates of the engine are registered and carry the prefix of the
    /// engine (<c>ids</c>, <c>predicates</c>);</item>
    /// <item>the strings the events of the engine's own pack name (title, text, cause, the buttons, the mail of <c>SendMail</c>) exist in <c>ru</c> and in <c>en</c> and
    /// carry the prefix <c>es.</c> (<c>strings</c>, warnings);</item>
    /// <item>the aggregator put one provider per channel into <c>ModModifiers</c>, not more (<c>providers</c>).</item>
    /// </list>
    /// The engine has no patches of its own, so there is no check of patch targets. That the sources use neither <c>System.Random</c> nor <c>UnityEngine.Random</c> is
    /// a test of the sources (a running mod has no sources to read).
    /// </summary>
    public static class EventSelfCheck
    {
        /// <summary>The feature whose strings and predicates are checked.</summary>
        public const string FeatureId = "event-system";

        /// <summary>How many names a message lists before it says "and N more".</summary>
        public const int ListedMost = 6;

        /// <summary>The nine effect types the engine registers itself (the types of design.md "Параметры стандартных типов").</summary>
        public static readonly IList<string> StandardTypes = new List<string>
        {
            "Money", "ModifierTimed", "FactoryOutput", "MarketDemand", "Popularity", "Reputation", "ResearchProgress", "SendMail", "FireEvent",
        }.AsReadOnly();

        /// <summary>The channels the standard handlers write without the event naming them: <c>FactoryOutput</c> and the four segments of <c>MarketDemand</c>.</summary>
        public static readonly IList<string> EngineChannels = new List<string>
        {
            "production.output", "market.demand.highend", "market.demand.midrange", "market.demand.lowend", "market.demand.industries",
        }.AsReadOnly();

        /// <summary>Judges the facts. Never throws; a null set of facts is a finding like any other.</summary>
        public static EventCheckVerdict Evaluate(EventCheckFacts facts)
        {
            var problems = new List<EventCheckProblem>();
            if (facts == null)
            {
                problems.Add(new EventCheckProblem(EventCheckLevel.Error, "selfcheck", "nothing was collected"));
                return new EventCheckVerdict(problems, 0, 0);
            }

            IList<EventDefinition> definitions = facts.Definitions ?? new EventDefinition[0];
            var registered = new HashSet<string>(facts.EffectTypes ?? new string[0], StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EventDefinition definition in definitions)
            {
                if (definition != null && definition.id != null) ids.Add(definition.id);
            }

            if (definitions.Count == 0)
            {
                Add(problems, EventCheckLevel.Error, "events", "the registry holds no event: the pack of the engine did not load, or every record of it was rejected (see the lines 'skip' above)");
            }

            CheckTypes(facts, definitions, registered, problems);
            CheckChannels(facts, definitions, problems);
            CheckChains(definitions, ids, problems);
            CheckIds(facts, definitions, problems);
            CheckPredicates(facts, problems);
            CheckStrings(facts, definitions, problems);
            CheckProviders(facts, problems);

            return new EventCheckVerdict(problems, definitions.Count, registered.Count);
        }

        // ---- the effects ----

        /// <summary>Every effect of an event: its own and those of its choices.</summary>
        public static IList<EventEffect> EffectsOf(EventDefinition definition)
        {
            var effects = new List<EventEffect>();
            if (definition == null) return effects;
            if (definition.effects != null) effects.AddRange(definition.effects);
            if (definition.choices == null) return effects;
            foreach (EventChoice choice in definition.choices)
            {
                if (choice != null && choice.effects != null) effects.AddRange(choice.effects);
            }
            return effects;
        }

        private static void CheckTypes(EventCheckFacts facts, IList<EventDefinition> definitions, HashSet<string> registered, List<EventCheckProblem> problems)
        {
            foreach (string type in StandardTypes)
            {
                if (!registered.Contains(type)) Add(problems, EventCheckLevel.Error, "types", "the standard effect type '" + type + "' is not registered");
            }

            var users = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (EventDefinition definition in definitions)
            {
                foreach (EventEffect effect in EffectsOf(definition))
                {
                    if (effect == null || string.IsNullOrEmpty(effect.type) || registered.Contains(effect.type)) continue;
                    Note(users, effect.type, definition.id);
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in users)
            {
                Add(problems, EventCheckLevel.Error, "types", "the effect type '" + pair.Key + "' has no handler (" + Names("events", pair.Value) + ")");
            }
        }

        private static void CheckChannels(EventCheckFacts facts, IList<EventDefinition> definitions, List<EventCheckProblem> problems)
        {
            var users = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string channel in EngineChannels) Note(users, channel, "the standard effect types");
            foreach (EventDefinition definition in definitions)
            {
                foreach (EventEffect effect in EffectsOf(definition))
                {
                    string channel = effect != null && effect.@params != null ? effect.@params.GetString("channel", null) : null;
                    if (!string.IsNullOrEmpty(channel)) Note(users, channel, definition.id);
                }
            }

            foreach (KeyValuePair<string, List<string>> pair in users)
            {
                if (SafeAnswer(facts.ChannelDeclared, pair.Key)) continue;
                Add(problems, EventCheckLevel.Warning, "channels", "channel '" + pair.Key + "' is not declared (" + Names("used by", pair.Value) + "); what writes it has no effect");
            }
        }

        // ---- the chains ----

        private static void CheckChains(IList<EventDefinition> definitions, HashSet<string> ids, List<EventCheckProblem> problems)
        {
            var dangling = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (EventDefinition definition in definitions)
            {
                foreach (EventEffect effect in EffectsOf(definition))
                {
                    if (effect == null || effect.type != "FireEvent" || effect.@params == null) continue;
                    string target = effect.@params.GetString("eventId", null);
                    if (string.IsNullOrEmpty(target) || !ids.Contains(target)) Note(dangling, target ?? "(none)", definition.id);
                }
            }
            foreach (KeyValuePair<string, List<string>> pair in dangling)
            {
                Add(problems, EventCheckLevel.Error, "events", "FireEvent names '" + pair.Key + "', which no pack defines (" + Names("in", pair.Value) + ")");
            }
        }

        // ---- the names ----

        private static void CheckIds(EventCheckFacts facts, IList<EventDefinition> definitions, List<EventCheckProblem> problems)
        {
            var wrong = new List<string>();
            foreach (EventDefinition definition in definitions)
            {
                string owner = SafeOwner(facts, definition);
                if (!string.IsNullOrEmpty(definition.id) && SafeAllowed(facts.EventIdAllowed, owner, definition.id)) continue;
                wrong.Add(definition.id + " (pack " + (definition.packId ?? "?") + ", owner " + (owner ?? "?") + ")");
            }
            if (wrong.Count > 0) Add(problems, EventCheckLevel.Error, "ids", "the id of an event does not carry a prefix of the feature that owns its pack: " + Names(null, wrong));
        }

        private static void CheckPredicates(EventCheckFacts facts, List<EventCheckProblem> problems)
        {
            var known = new HashSet<string>(facts.KnownPredicates ?? new string[0], StringComparer.Ordinal);
            var unregistered = new List<string>();
            var badName = new List<string>();
            foreach (string name in facts.EnginePredicates ?? new string[0])
            {
                if (!SafeAnswer(facts.PredicateNameAllowed, name)) badName.Add(name);
                if (!known.Contains(name)) unregistered.Add(name);
            }
            if (badName.Count > 0) Add(problems, EventCheckLevel.Error, "predicates", "a predicate of the engine does not carry its prefix: " + Names(null, badName));
            if (unregistered.Count > 0) Add(problems, EventCheckLevel.Error, "predicates", "a predicate of the engine is not registered in ModConditions: " + Names(null, unregistered));
        }

        // ---- the strings ----

        /// <summary>The keys of the strings an event names: its title, text and cause, the captions of its buttons and the mail of its <c>SendMail</c> effects.</summary>
        public static IList<string> KeysOf(EventDefinition definition)
        {
            var keys = new List<string>();
            if (definition == null) return keys;
            AddKey(keys, definition.title);
            AddKey(keys, definition.text);
            AddKey(keys, definition.cause);
            if (definition.choices != null)
            {
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && !string.IsNullOrEmpty(choice.textKey) && !keys.Contains(choice.textKey)) keys.Add(choice.textKey);
                }
            }
            foreach (EventEffect effect in EffectsOf(definition))
            {
                if (effect == null || effect.type != "SendMail" || effect.@params == null) continue;
                AddKey(keys, effect.@params.GetString("title", null));
                AddKey(keys, effect.@params.GetString("text", null));
            }
            return keys;
        }

        private static void CheckStrings(EventCheckFacts facts, IList<EventDefinition> definitions, List<EventCheckProblem> problems)
        {
            var foreign = new List<string>();
            var noRu = new List<string>();
            var noEn = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (EventDefinition definition in definitions)
            {
                if (!string.Equals(SafeOwner(facts, definition), FeatureId, StringComparison.Ordinal)) continue;   // the strings of another pack are that feature's to check
                foreach (string key in KeysOf(definition))
                {
                    if (!seen.Add(key)) continue;
                    if (!SafeAnswer(facts.KeyAllowed, key)) foreign.Add(key);
                    if (!SafeAnswer(facts.HasRu, key)) noRu.Add(key);
                    if (!SafeAnswer(facts.HasEn, key)) noEn.Add(key);
                }
            }
            if (foreign.Count > 0) Add(problems, EventCheckLevel.Warning, "strings", "keys of the events without the prefix of the feature (es.): " + Names(null, foreign));
            if (noRu.Count > 0) Add(problems, EventCheckLevel.Warning, "strings", noRu.Count + (noRu.Count == 1 ? " key is" : " keys are") + " missing in ru: " + Names(null, noRu));
            if (noEn.Count > 0) Add(problems, EventCheckLevel.Warning, "strings", noEn.Count + (noEn.Count == 1 ? " key is" : " keys are") + " missing in en: " + Names(null, noEn));
        }

        private static void AddKey(List<string> keys, LocText text)
        {
            if (text != null) AddKey(keys, text.Key);
        }

        private static void AddKey(List<string> keys, string key)
        {
            if (!string.IsNullOrEmpty(key) && !keys.Contains(key)) keys.Add(key);
        }

        // ---- the providers ----

        private static void CheckProviders(EventCheckFacts facts, List<EventCheckProblem> problems)
        {
            if (facts.ProviderRegistrations == null) return;
            var doubled = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in facts.ProviderRegistrations)
            {
                if (pair.Value != 1) doubled[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, int> pair in doubled)
            {
                Add(problems, EventCheckLevel.Error, "providers", "channel '" + pair.Key + "' got " + pair.Value + " providers of the engine, one per channel is the rule (the product would count twice)");
            }
        }

        // ---- helpers ----

        private static void Add(List<EventCheckProblem> problems, EventCheckLevel level, string area, string message)
        {
            problems.Add(new EventCheckProblem(level, area, message));
        }

        private static void Note(IDictionary<string, List<string>> users, string key, string user)
        {
            List<string> list;
            if (!users.TryGetValue(key, out list))
            {
                list = new List<string>();
                users[key] = list;
            }
            if (!list.Contains(user)) list.Add(user);
        }

        /// <summary>A list of names for a message: the first <see cref="ListedMost"/>, then "and N more".</summary>
        private static string Names(string label, IList<string> names)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(label)) sb.Append(label).Append(' ');
            int shown = Math.Min(ListedMost, names.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(names[i]);
            }
            if (names.Count > shown) sb.Append(" and ").Append(names.Count - shown).Append(" more");
            return sb.ToString();
        }

        private static string SafeOwner(EventCheckFacts facts, EventDefinition definition)
        {
            try
            {
                return facts.OwnerOf != null ? facts.OwnerOf(definition) : definition.packId;
            }
            catch (Exception)
            {
                return definition.packId;
            }
        }

        // A question the game answers may throw (the core is not ready, a table is broken): the answer is then "no", which is a finding, never a crash.
        private static bool SafeAnswer(Func<string, bool> question, string argument)
        {
            if (question == null) return true;
            try
            {
                return question(argument);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SafeAllowed(Func<string, string, bool> question, string owner, string id)
        {
            if (question == null) return true;
            try
            {
                return question(owner, id);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
