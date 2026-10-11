using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The checks of the table "Параметры стандартных типов" of design.md ("Проверка при загрузке"), applied by <see cref="ContentValidator"/> to the nine
    /// standard effect types before the type's own handler (<c>IEventEffectHandler.Validate</c>) is asked. They live here, as data about the types, so
    /// that the content of a pack is judged the same whichever handler class is registered and so that the tests can drive them without the game.
    /// A handler of a standard type does not have to repeat them. Everything is read from the raw <c>params</c> object; a mistake adds one
    /// English line <c>problem (path.params.key)</c> to the error list, and an unknown parameter of a standard type is a warning.
    /// </summary>
    public static class StandardEffectRules
    {
        public const string Money = "Money";
        public const string ModifierTimed = "ModifierTimed";
        public const string FactoryOutput = "FactoryOutput";
        public const string MarketDemand = "MarketDemand";
        public const string Popularity = "Popularity";
        public const string Reputation = "Reputation";
        public const string ResearchProgress = "ResearchProgress";
        public const string SendMail = "SendMail";
        public const string FireEvent = "FireEvent";

        /// <summary>The standard types in the order of the design table.</summary>
        public static readonly IReadOnlyList<string> Types = new[]
        {
            Money, ModifierTimed, FactoryOutput, MarketDemand, Popularity, Reputation, ResearchProgress, SendMail, FireEvent,
        };

        /// <summary>The values of <c>segment</c> (<c>MarketDemand</c>, <c>Popularity</c>).</summary>
        public static readonly IReadOnlyList<string> Segments = new[] { "highend", "midrange", "lowend", "industries", "all" };

        /// <summary>The values of <c>Popularity.cpuSelector</c>.</summary>
        public static readonly IReadOnlyList<string> CpuSelectors = new[] { "AllMarket", "Newest" };

        /// <summary>The values of <c>FireEvent.target</c>.</summary>
        public static readonly IReadOnlyList<string> FireTargets = new[] { "Same", "Player", "Definition" };

        private static readonly string[] Directions = { "Spend", "Earn" };

        /// <summary>The keys every effect has (<see cref="EffectParamKeys"/>); the engine reads them, <see cref="ContentValidator"/> checks them.</summary>
        private static readonly string[] CommonKeys =
        {
            EffectParamKeys.Scope, EffectParamKeys.Chance, EffectParamKeys.ExclusiveGroup, EffectParamKeys.Scalable,
        };

        private const double MaxMoney = 1e12;

        public static bool IsStandard(string type)
        {
            if (type == null) return false;
            foreach (string name in Types)
            {
                if (string.Equals(name, type, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks the parameters of a standard effect. <paramref name="path"/> is where the effect sits in the record (<c>choices[0].effects[1]</c>).
        /// Does nothing for a type that is not standard. The type must be registered already (the validator asks first).
        /// </summary>
        public static void Check(EventEffect effect, IContentEnvironment env, string path, List<string> errors, List<string> warnings)
        {
            if (effect == null || env == null || !IsStandard(effect.type)) return;

            JsonObject raw = effect.@params != null && effect.@params.Raw != null ? effect.@params.Raw : new JsonObject();
            var p = new Reader(raw, path, errors, warnings);

            switch (effect.type)
            {
                case Money: CheckMoney(p, env, effect); break;
                case ModifierTimed: CheckModifierTimed(p, env, effect); break;
                case FactoryOutput: CheckFactoryOutput(p, effect); break;
                case MarketDemand: CheckMarketDemand(p, effect); break;
                case Popularity: CheckPopularity(p, effect); break;
                case Reputation: CheckReputation(p, effect); break;
                case ResearchProgress: CheckResearchProgress(p, effect); break;
                case SendMail: CheckSendMail(p, effect); break;
                case FireEvent: CheckFireEvent(p, effect); break;
            }
        }

        // ---- the types ----

        private static void CheckMoney(Reader p, IContentEnvironment env, EventEffect effect)
        {
            p.Known("direction", "origin", "revenueFraction", "min", "max", "inflation");
            p.NoDuration(effect);

            string direction = p.Choice("direction", true, Directions);
            string origin = p.Text("origin", false);
            if (origin != null && direction != null && !env.IsOriginName(direction == "Spend", origin))
            {
                p.Error("origin", "unknown origin '" + origin + "': " + (direction == "Spend" ? "ExpenseOrigin" : "RevenueOrigin") + " of the game has no such name");
            }

            double lo, hi;
            if (p.Range("revenueFraction", false, out lo, out hi))
            {
                if (lo < 0 || hi > 2) p.Error("revenueFraction", "'revenueFraction' must be within 0..2, got " + p.Show(lo, hi));
            }

            double min = 0, max = 0;
            bool hasMin = p.Number("min", false, 0, MaxMoney, out min);
            bool hasMax = p.Number("max", false, 0, MaxMoney, out max);
            if (hasMin && hasMax && min > max) p.Error("min", "'min' (" + RecordReader.Num(min) + ") must not exceed 'max' (" + RecordReader.Num(max) + ")");

            p.Bool("inflation");
        }

        private static void CheckModifierTimed(Reader p, IContentEnvironment env, EventEffect effect)
        {
            p.Known("channel", "multiplier");
            p.NeedDuration(effect);

            string channel = p.Text("channel", true);
            if (channel != null && !env.IsChannelDeclared(channel))
            {
                p.Error("channel", "unknown channel '" + channel + "': ModModifiers does not know it (core.json or ModModifiers.DeclareChannel)");
            }

            double multiplier;
            p.Number("multiplier", true, 0.05, 5, out multiplier);
        }

        private static void CheckFactoryOutput(Reader p, EventEffect effect)
        {
            p.Known("multiplier");
            p.NeedDuration(effect);

            double multiplier;
            p.Number("multiplier", true, 0.1, 2, out multiplier);
        }

        private static void CheckMarketDemand(Reader p, EventEffect effect)
        {
            p.Known("segment", "multiplier");
            p.NeedDuration(effect);

            p.Choice("segment", false, Segments);
            double multiplier;
            p.Number("multiplier", true, 0.4, 2.5, out multiplier);
        }

        private static void CheckPopularity(Reader p, EventEffect effect)
        {
            p.Known("delta", "cpuSelector", "segment");
            p.NoDuration(effect);

            double lo, hi;
            if (p.Range("delta", true, out lo, out hi) && (Math.Abs(lo) > 0.5 || Math.Abs(hi) > 0.5))
            {
                p.Error("delta", "'delta' must be within -0.5..0.5, got " + p.Show(lo, hi));
            }
            p.Choice("cpuSelector", false, CpuSelectors);
            p.Choice("segment", false, Segments);
        }

        private static void CheckReputation(Reader p, EventEffect effect)
        {
            p.Known("delta");
            p.NoDuration(effect);

            double lo, hi;
            if (p.Range("delta", true, out lo, out hi) && (Math.Abs(lo) > 50 || Math.Abs(hi) > 50))
            {
                p.Error("delta", "'delta' must be within -50..50, got " + p.Show(lo, hi));
            }
        }

        private static void CheckResearchProgress(Reader p, EventEffect effect)
        {
            p.Known("delta");
            p.NoDuration(effect);

            double lo, hi;
            if (p.Range("delta", true, out lo, out hi) && (lo <= 0 || hi > 0.5))
            {
                p.Error("delta", "'delta' must be above 0 and at most 0.5, got " + p.Show(lo, hi));
            }
        }

        private static void CheckSendMail(Reader p, EventEffect effect)
        {
            p.Known("title", "text");
            p.NoDuration(effect);

            p.Text("title", true);
            p.Text("text", true);
        }

        private static void CheckFireEvent(Reader p, EventEffect effect)
        {
            p.Known("eventId", "delayDays", "target", ChainFlags.FlagParam);
            p.NoDuration(effect);

            string flag = p.Text(ChainFlags.FlagParam, false);
            if (flag != null && !ChainFlags.IsFlagName(flag))
            {
                p.Error(ChainFlags.FlagParam, "'flag' may use letters, digits, '_', '-' and '.' only, at most " + ContentNames.MaxShortLength + " characters, got '" + flag + "'");
            }

            string eventId = p.Text("eventId", true);
            if (eventId != null && !ContentNames.IsName(eventId, ContentNames.MaxIdLength))
            {
                p.Error("eventId", "'eventId' may use letters, digits, '_', '-' and '.' only, got '" + eventId + "'");
            }

            double lo, hi;
            if (p.Range("delayDays", false, out lo, out hi))
            {
                if (Math.Floor(lo) != lo || Math.Floor(hi) != hi || lo < 0 || hi > DefinitionMapper.MaxTimerDays)
                {
                    p.Error("delayDays", "'delayDays' must be a whole number of days (or a pair of them) from 0 to " + DefinitionMapper.MaxTimerDays + ", got " + p.Show(lo, hi));
                }
            }
            p.Choice("target", false, FireTargets);
        }

        // ---- reading the params object ----

        private sealed class Reader
        {
            private readonly JsonObject _params;
            private readonly string _at;
            private readonly List<string> _errors;
            private readonly List<string> _warnings;

            public Reader(JsonObject raw, string effectPath, List<string> errors, List<string> warnings)
            {
                _params = raw;
                _at = effectPath + ".params";
                _errors = errors;
                _warnings = warnings;
            }

            public void Error(string key, string problem)
            {
                _errors.Add(problem + " (" + _at + "." + key + ")");
            }

            /// <summary>Warns about every parameter that is neither one of <paramref name="keys"/> nor a common one.</summary>
            public void Known(params string[] keys)
            {
                foreach (KeyValuePair<string, JsonNode> pair in _params)
                {
                    if (Array.IndexOf(keys, pair.Key) >= 0 || Array.IndexOf(CommonKeys, pair.Key) >= 0) continue;
                    _warnings.Add("unknown parameter '" + pair.Key + "' is ignored (" + _at + "." + pair.Key + ")");
                }
            }

            /// <summary>A one-off effect has no duration; a number there is ignored.</summary>
            public void NoDuration(EventEffect effect)
            {
                if (effect.durationDays > 0) _warnings.Add("'durationDays' is ignored by " + effect.type + " (" + _at.Replace(".params", ".durationDays") + ")");
            }

            /// <summary>A timed effect lasts at least a day.</summary>
            public void NeedDuration(EventEffect effect)
            {
                if (effect.durationDays < 1) _errors.Add(effect.type + " needs 'durationDays' of at least 1 (" + _at.Replace(".params", ".durationDays") + ")");
            }

            public string Text(string key, bool required)
            {
                JsonNode node = Find(key);
                if (node == null)
                {
                    if (required) Error(key, "'" + key + "' is required");
                    return null;
                }

                var value = node as JsonValue;
                if (value == null || value.Kind != JsonKind.String || value.StringValue.Length == 0)
                {
                    Error(key, "'" + key + "' must be a non-empty string, got " + RecordReader.Describe(node));
                    return null;
                }
                return value.StringValue;
            }

            public void Bool(string key)
            {
                JsonNode node = Find(key);
                if (node == null) return;

                var value = node as JsonValue;
                if (value == null || value.Kind != JsonKind.Bool) Error(key, "'" + key + "' must be true or false, got " + RecordReader.Describe(node));
            }

            /// <summary>One of <paramref name="allowed"/> (case-insensitive); returns the spelling from the list, null when missing or wrong.</summary>
            public string Choice(string key, bool required, IReadOnlyList<string> allowed)
            {
                JsonNode node = Find(key);
                if (node == null)
                {
                    if (required) Error(key, "'" + key + "' is required (" + string.Join(", ", allowed) + ")");
                    return null;
                }

                var value = node as JsonValue;
                if (value != null && value.Kind == JsonKind.String)
                {
                    foreach (string name in allowed)
                    {
                        if (string.Equals(name, value.StringValue, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
                Error(key, "'" + key + "' must be one of " + string.Join(", ", allowed) + ", got " + RecordReader.Describe(node));
                return null;
            }

            /// <summary>A number within [min, max]. True when it is there and in range.</summary>
            public bool Number(string key, bool required, double min, double max, out double number)
            {
                number = 0;
                JsonNode node = Find(key);
                if (node == null)
                {
                    if (required) Error(key, "'" + key + "' is required");
                    return false;
                }

                if (!RecordReader.TryNumber(node, out number))
                {
                    Error(key, "'" + key + "' must be a number from " + RecordReader.Num(min) + " to " + RecordReader.Num(max) + ", got " + RecordReader.Describe(node));
                    return false;
                }
                if (number < min || number > max)
                {
                    Error(key, "'" + key + "' must be from " + RecordReader.Num(min) + " to " + RecordReader.Num(max) + ", got " + RecordReader.Num(number));
                    return false;
                }
                return true;
            }

            /// <summary>A number or a pair <c>[lo, hi]</c> with lo not above hi. True when it is there and well formed (the caller checks the limits).</summary>
            public bool Range(string key, bool required, out double lo, out double hi)
            {
                lo = 0;
                hi = 0;
                JsonNode node = Find(key);
                if (node == null)
                {
                    if (required) Error(key, "'" + key + "' is required (a number or [lo, hi])");
                    return false;
                }

                if (!EffectParams.TryRange(node, out lo, out hi))
                {
                    Error(key, "'" + key + "' must be a number or a pair [lo, hi] of numbers, got " + RecordReader.Describe(node));
                    return false;
                }
                if (lo > hi)
                {
                    Error(key, "'" + key + "' must be [lo, hi] with lo not above hi, got " + Show(lo, hi));
                    return false;
                }
                return true;
            }

            public string Show(double lo, double hi)
            {
                return lo == hi ? RecordReader.Num(lo) : "[" + RecordReader.Num(lo) + ", " + RecordReader.Num(hi) + "]";
            }

            private JsonNode Find(string key)
            {
                JsonNode node = _params[key];
                return node == null || node.IsNull ? null : node;
            }
        }
    }
}
