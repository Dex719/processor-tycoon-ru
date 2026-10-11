using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>The parameters of the feature in the hub (FR-11 item 1; design.md "Точка входа фичи").</summary>
    public static class EventParams
    {
        /// <summary>Frequency of the lotteries: 0 "Выкл", 1 "Редко", 2 "Обычно" (default), 3 "Часто" (<see cref="Pacing.FrequencyFactor"/>).</summary>
        public const string Frequency = "frequency";

        /// <summary>A modal window with a pause for every event with a choice, not only the important ones.</summary>
        public const string PauseAllChoices = "pauseAllChoices";

        /// <summary>News to the player about events of AI companies.</summary>
        public const string RivalNews = "rivalNews";

        /// <summary>The three declarations, in the order the hub shows them.</summary>
        public static ParamSpec[] Create(string featureId)
        {
            return new[]
            {
                new ParamSpec
                {
                    FeatureId = featureId, Key = Frequency, LabelKey = "es.param.frequency", TooltipKey = "es.param.frequency.tooltip",
                    Type = ParamType.Int, Default = Pacing.DefaultFrequencySetting, Min = 0, Max = 3, Step = 1,
                },
                new ParamSpec
                {
                    FeatureId = featureId, Key = PauseAllChoices, LabelKey = "es.param.pauseAllChoices", TooltipKey = "es.param.pauseAllChoices.tooltip",
                    Type = ParamType.Bool, Default = 0, Min = 0, Max = 1,
                },
                new ParamSpec
                {
                    FeatureId = featureId, Key = RivalNews, LabelKey = "es.param.rivalNews", TooltipKey = "es.param.rivalNews.tooltip",
                    Type = ParamType.Bool, Default = 1, Min = 0, Max = 1,
                },
            };
        }
    }

    /// <summary>One line of the debug list of the hub page: an event the registry has, with what the owner needs to press "Запустить сейчас".</summary>
    public sealed class DebugEventRow
    {
        public string EventId;

        /// <summary>The pack the event belongs to; its rows are grouped by it.</summary>
        public string PackId;

        /// <summary>The title in the current language (the id when the event has none).</summary>
        public string Title;

        /// <summary>The category and the kind of trigger, for the owner to see how the event is started normally.</summary>
        public string Detail;

        /// <summary>The key of the reason the button is grey (the pack is frozen), or null when it works.</summary>
        public string BlockedKey;

        public bool CanFire
        {
            get { return string.IsNullOrEmpty(BlockedKey); }
        }
    }

    /// <summary>
    /// What the settings page of the hub is made of (FR-11, design.md "UI"): the labels of the frequency levels, the rule of the debug list and the list itself. The widgets
    /// are <c>EventSettingsPage</c>; the parameters themselves are drawn by the hub from <see cref="EventParams"/>, so they are in the hub whether this page is built or not.
    /// </summary>
    public static class EventSettings
    {
        /// <summary>The debug flag (<c>ModSettings.Debug</c>) that shows the list "Запустить сейчас"; the core keeps every <c>debug.*</c> flag false in a release build (AC-11.2).</summary>
        public const string DebugFlag = "debug.events";

        /// <summary>The number of levels of the frequency setting.</summary>
        public const int FrequencyLevels = 4;

        /// <summary>The key of the label of a frequency level (<c>es.hub.frequency.0</c> ... <c>3</c>); a number out of range is brought into it.</summary>
        public static string FrequencyLabelKey(int setting)
        {
            return "es.hub.frequency." + Clamp(setting).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The line under the frequency slider that says what each number means: <c>0 - Выкл, 1 - Редко (×0,5), 2 - Обычно (×1), 3 - Часто (×1,5)</c>.</summary>
        public static string FrequencyLegend(ITextSource text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var parts = new string[FrequencyLevels];
            for (int level = 0; level < FrequencyLevels; level++)
            {
                parts[level] = level.ToString(System.Globalization.CultureInfo.InvariantCulture) + " — " + text.L(FrequencyLabelKey(level));
            }
            return text.L("es.hub.frequency.legend", string.Join(", ", parts));
        }

        /// <summary>What "Запустить сейчас" asks of the engine (AC-11.2): the player is the target, the conditions of the event are skipped, it happens today.</summary>
        public static EventFireContext DebugFire()
        {
            return new EventFireContext { CompanyKey = CompanyKey.Player, IgnoreConditions = true, DelayDays = 0 };
        }

        /// <summary>
        /// The rows of the debug list: every event of the registry, packs in the order of the registry, the events of a pack by id. An event of a frozen pack is listed
        /// with its button grey, because firing it would answer <c>Disabled</c>.
        /// </summary>
        public static IList<DebugEventRow> DebugRows(IEnumerable<EventDefinition> definitions, Func<string, bool> packActive, ITextSource text)
        {
            var rows = new List<DebugEventRow>();
            if (definitions == null || text == null) return rows;

            var packs = new List<string>();
            var byPack = new Dictionary<string, List<EventDefinition>>(StringComparer.Ordinal);
            foreach (EventDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.id)) continue;
                string pack = definition.packId ?? string.Empty;
                List<EventDefinition> list;
                if (!byPack.TryGetValue(pack, out list))
                {
                    list = new List<EventDefinition>();
                    byPack[pack] = list;
                    packs.Add(pack);
                }
                list.Add(definition);
            }

            foreach (string pack in packs)
            {
                List<EventDefinition> list = byPack[pack];
                list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
                bool active = packActive == null || packActive(pack);
                foreach (EventDefinition definition in list)
                {
                    string title = text.Resolve(definition.title, null);
                    rows.Add(new DebugEventRow
                    {
                        EventId = definition.id,
                        PackId = pack,
                        Title = string.IsNullOrEmpty(title) ? definition.id : title,
                        Detail = Detail(definition),
                        BlockedKey = active ? null : "es.hub.debug.frozen",
                    });
                }
            }
            return rows;
        }

        private static string Detail(EventDefinition definition)
        {
            string trigger = definition.trigger != null ? definition.trigger.type.ToString() : string.Empty;
            string category = definition.category ?? string.Empty;
            if (trigger.Length == 0) return category;
            return category.Length == 0 ? trigger : trigger + ", " + category;
        }

        private static int Clamp(int setting)
        {
            return Math.Max(0, Math.Min(FrequencyLevels - 1, setting));
        }
    }
}
