using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Features.EventSystem
{
    /// <summary>One category of the lottery (D6): its daily chance and its pacing (FR-2 items 2, 7). From the <c>categories</c> of a pack's <c>config.json</c>.</summary>
    public sealed class CategoryDefinition
    {
        public string Id;

        /// <summary>The chance per day that the lottery of the category picks an event, before the player's frequency setting and the pack's frequency.</summary>
        public float DailyChance;

        /// <summary>Days after the last event of the category before the lottery may run again.</summary>
        public int MinGapDays;

        /// <summary>How many events of the category may be active (pending or running) at once.</summary>
        public int MaxActive = PackConfigReader.NoLimit;

        /// <summary>The pack whose <c>config.json</c> defines the category.</summary>
        public string PackId;
    }

    /// <summary>One entry of <c>channelLimits</c>: the range of the product of the event contributions to a channel, for an exact channel, a family or all channels.</summary>
    public sealed class ChannelLimit
    {
        /// <summary>An exact name (<c>production.output</c>), a family (<c>market.demand.*</c>) or <c>*</c>.</summary>
        public string Pattern;

        public float Min;
        public float Max;
        public string PackId;
    }

    /// <summary>What one pack's <c>config.json</c> says (FR-1, FR-2; design.md, "Контент: файлы пака"). A value the file does not set stays at -1 (scalars).</summary>
    public sealed class PackConfig
    {
        public string PackId;
        public readonly List<CategoryDefinition> Categories = new List<CategoryDefinition>();
        public readonly List<ChannelLimit> ChannelLimits = new List<ChannelLimit>();
        public int MaxPendingChoices = -1;
        public int ReminderDaysBefore = -1;
        public int LogLimit = -1;
    }

    /// <summary>
    /// Reads the <c>config.json</c> of a pack into a <see cref="PackConfig"/>. An entry that is wrong is skipped with one line in the problem list, the
    /// rest of the file loads (the same rule as for the events); a wrong scalar keeps its default. Never throws for content.
    /// </summary>
    public static class PackConfigReader
    {
        /// <summary>The <c>schema</c> of <c>config.json</c> this version reads.</summary>
        public const int Schema = 1;

        /// <summary>The file name of a pack's config (without the extension).</summary>
        public const string FileName = "config";

        /// <summary>The <c>maxActive</c> of a category that does not set one: no practical limit.</summary>
        public const int NoLimit = 100;

        public static PackConfig Read(string packId, JsonObject json, List<string> problems)
        {
            var config = new PackConfig { PackId = packId };
            if (json == null) return config;

            var errors = new List<string>();
            var warnings = new List<string>();
            var r = new RecordReader(json, string.Empty, errors, warnings);

            ReadCategories(r, config, packId, errors);
            ReadChannelLimits(r, config, packId, errors);
            config.MaxPendingChoices = r.Int("maxPendingChoices", -1, 1, 10);
            config.ReminderDaysBefore = r.Int("reminderDaysBefore", -1, 0, 30);
            config.LogLimit = r.Int("logLimit", -1, 10, 2000);
            r.ReportUnknown("schema");

            if (problems != null)
            {
                problems.AddRange(errors);
                problems.AddRange(warnings);
            }
            return config;
        }

        private static void ReadCategories(RecordReader parent, PackConfig config, string packId, List<string> problems)
        {
            JsonArray array = parent.Arr("categories");
            if (array == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                string path = RecordReader.IndexPath("categories", i);
                var node = array[i] as JsonObject;
                if (node == null)
                {
                    problems.Add("skip category: it must be an object, got " + RecordReader.KindName(array[i]) + " (" + path + ")");
                    continue;
                }

                var entryErrors = new List<string>();
                var entryWarnings = new List<string>();
                var r = new RecordReader(node, path, entryErrors, entryWarnings);
                var category = new CategoryDefinition { PackId = packId };
                category.Id = r.Str("id", true);
                category.DailyChance = r.Float("dailyChance", 0f, 0f, 1f);
                category.MinGapDays = r.Int("minGapDays", 0, 0, DefinitionMapper.MaxTimerDays);
                category.MaxActive = r.Int("maxActive", NoLimit, 1, NoLimit);
                r.ReportUnknown();

                if (category.Id != null && !ContentNames.IsName(category.Id, ContentNames.MaxShortLength))
                {
                    entryErrors.Add("category id '" + category.Id + "' may use letters, digits, '_', '-' and '.' only, up to " + ContentNames.MaxShortLength + " characters (" + path + ".id)");
                }
                if (category.Id != null && !seen.Add(category.Id)) entryErrors.Add("duplicate category '" + category.Id + "' in this file (" + path + ".id)");

                if (entryErrors.Count > 0)
                {
                    foreach (string line in entryErrors) problems.Add("skip category: " + line);
                    continue;
                }
                problems.AddRange(entryWarnings);
                config.Categories.Add(category);
            }
        }

        private static void ReadChannelLimits(RecordReader parent, PackConfig config, string packId, List<string> problems)
        {
            JsonArray array = parent.Arr("channelLimits");
            if (array == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                string path = RecordReader.IndexPath("channelLimits", i);
                var node = array[i] as JsonObject;
                if (node == null)
                {
                    problems.Add("skip channel limit: it must be an object, got " + RecordReader.KindName(array[i]) + " (" + path + ")");
                    continue;
                }

                var entryErrors = new List<string>();
                var entryWarnings = new List<string>();
                var r = new RecordReader(node, path, entryErrors, entryWarnings);
                var limit = new ChannelLimit { PackId = packId };
                limit.Pattern = r.Str("channel", true);
                if (!r.Has("min")) r.Error("min", "'min' is required");
                if (!r.Has("max")) r.Error("max", "'max' is required");
                limit.Min = r.Float("min", 0f, 0f, 1000f);
                limit.Max = r.Float("max", 0f, 0f, 1000f);
                r.ReportUnknown();

                if (limit.Pattern != null && !IsPattern(limit.Pattern))
                {
                    entryErrors.Add("channel pattern '" + limit.Pattern + "' must be '*', a channel name or a family such as 'market.demand.*' (" + path + ".channel)");
                }
                if (entryErrors.Count == 0 && limit.Min > limit.Max)
                {
                    entryErrors.Add("'min' (" + RecordReader.Num(limit.Min) + ") must not exceed 'max' (" + RecordReader.Num(limit.Max) + ") (" + path + ")");
                }
                if (limit.Pattern != null && !seen.Add(limit.Pattern)) entryErrors.Add("duplicate limit for '" + limit.Pattern + "' in this file (" + path + ".channel)");

                if (entryErrors.Count > 0)
                {
                    foreach (string line in entryErrors) problems.Add("skip channel limit: " + line);
                    continue;
                }
                problems.AddRange(entryWarnings);
                config.ChannelLimits.Add(limit);
            }
        }

        /// <summary>True for <c>*</c>, a valid channel name and a valid family name.</summary>
        public static bool IsPattern(string pattern)
        {
            return pattern == EventConfig.AllChannels || ChannelFamilies.IsValidChannelName(pattern, true);
        }
    }

    /// <summary>
    /// The settings of the engine as all registered packs together define them (FR-1, FR-2, FR-5): the categories of the lottery, the limits of the
    /// event contributions to modifier channels, <c>maxPendingChoices</c>, <c>reminderDaysBefore</c>, <c>logLimit</c>. Immutable once built by <see cref="Merge"/>.
    /// <list type="bullet">
    /// <item><b>Order.</b> Packs are taken in ordinal order of their ids, so the result does not depend on the order the features registered their packs.</item>
    /// <item><b>Categories</b> of all packs are united; an id defined twice keeps the first definition and warns.</item>
    /// <item><b>Channel limits</b> (D8: a pack may narrow a limit, never widen it). Inside one pack the most specific entry for a channel counts - the exact name,
    /// else the longest matching family, else <c>*</c> - so a pack can say "<c>*</c> 0.25..4, <c>production.output</c> 0.1..3". Across packs the narrowest wins: the
    /// highest minimum and the lowest maximum of the packs that have an entry for the channel. A channel no pack mentions gets
    /// [<see cref="DefaultLimitMin"/>, <see cref="DefaultLimitMax"/>]. If narrowing leaves no room (min above max) the range collapses to the maximum.</item>
    /// <item><b>Scalars</b> are the smallest value any pack sets (the narrowest); the defaults apply when no pack sets one.</item>
    /// </list>
    /// </summary>
    public sealed class EventConfig
    {
        /// <summary>The pattern that matches every channel.</summary>
        public const string AllChannels = "*";

        public const int DefaultMaxPendingChoices = 2;
        public const int DefaultReminderDaysBefore = 3;
        public const int DefaultLogLimit = 400;
        public const float DefaultLimitMin = 0.25f;
        public const float DefaultLimitMax = 4.0f;

        private readonly List<CategoryDefinition> _categories = new List<CategoryDefinition>();
        private readonly Dictionary<string, CategoryDefinition> _byId = new Dictionary<string, CategoryDefinition>(StringComparer.Ordinal);
        private readonly List<ChannelLimit[]> _limitsByPack = new List<ChannelLimit[]>();
        private readonly Dictionary<string, KeyValuePair<float, float>> _limitCache = new Dictionary<string, KeyValuePair<float, float>>(StringComparer.Ordinal);

        private EventConfig()
        {
            MaxPendingChoices = DefaultMaxPendingChoices;
            ReminderDaysBefore = DefaultReminderDaysBefore;
            LogLimit = DefaultLogLimit;
        }

        /// <summary>No packs: the defaults.</summary>
        public static EventConfig Defaults
        {
            get { return new EventConfig(); }
        }

        /// <summary>All categories, in the order of the packs (by id) and of each file.</summary>
        public IReadOnlyList<CategoryDefinition> Categories
        {
            get { return _categories; }
        }

        /// <summary>How many events with a choice may wait for the player at once (FR-2 item 6).</summary>
        public int MaxPendingChoices { get; private set; }

        /// <summary>Days before the deadline when the player is reminded; 0 - no reminder.</summary>
        public int ReminderDaysBefore { get; private set; }

        /// <summary>The most entries the journal keeps.</summary>
        public int LogLimit { get; private set; }

        /// <summary>The category with this id, or null.</summary>
        public CategoryDefinition Category(string id)
        {
            CategoryDefinition category;
            return id != null && _byId.TryGetValue(id, out category) ? category : null;
        }

        public bool HasCategory(string id)
        {
            return Category(id) != null;
        }

        /// <summary>The lower limit of the product of event contributions on the channel (see the class summary).</summary>
        public float LimitMin(string channel)
        {
            return LimitsOf(channel).Key;
        }

        /// <summary>The upper limit of the product of event contributions on the channel.</summary>
        public float LimitMax(string channel)
        {
            return LimitsOf(channel).Value;
        }

        /// <summary>
        /// Merges the configs of the packs (see the class summary). <paramref name="warn"/> gets one line per conflict (a category defined by two packs).
        /// </summary>
        public static EventConfig Merge(IEnumerable<PackConfig> packs, Action<string> warn)
        {
            var merged = new EventConfig();
            if (packs == null) return merged;

            var ordered = new List<PackConfig>();
            foreach (PackConfig pack in packs)
            {
                if (pack != null) ordered.Add(pack);
            }
            ordered.Sort((a, b) => string.CompareOrdinal(a.PackId, b.PackId));

            int pending = -1, reminder = -1, log = -1;
            foreach (PackConfig pack in ordered)
            {
                foreach (CategoryDefinition category in pack.Categories)
                {
                    CategoryDefinition first;
                    if (merged._byId.TryGetValue(category.Id, out first))
                    {
                        if (warn != null)
                        {
                            warn("category '" + category.Id + "' of pack '" + pack.PackId + "' is ignored: pack '" + first.PackId + "' already defines it");
                        }
                        continue;
                    }
                    merged._byId[category.Id] = category;
                    merged._categories.Add(category);
                }

                if (pack.ChannelLimits.Count > 0) merged._limitsByPack.Add(SortBySpecificity(pack.ChannelLimits));
                pending = Narrow(pending, pack.MaxPendingChoices);
                reminder = Narrow(reminder, pack.ReminderDaysBefore);
                log = Narrow(log, pack.LogLimit);
            }

            if (pending >= 0) merged.MaxPendingChoices = pending;
            if (reminder >= 0) merged.ReminderDaysBefore = reminder;
            if (log >= 0) merged.LogLimit = log;
            return merged;
        }

        // ---- limits ----

        private KeyValuePair<float, float> LimitsOf(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return new KeyValuePair<float, float>(DefaultLimitMin, DefaultLimitMax);

            KeyValuePair<float, float> cached;
            if (_limitCache.TryGetValue(channel, out cached)) return cached;

            float min = float.NegativeInfinity;
            float max = float.PositiveInfinity;
            bool any = false;
            foreach (ChannelLimit[] pack in _limitsByPack)
            {
                ChannelLimit best = MostSpecific(pack, channel);
                if (best == null) continue;
                any = true;
                if (best.Min > min) min = best.Min;
                if (best.Max < max) max = best.Max;
            }

            if (!any)
            {
                min = DefaultLimitMin;
                max = DefaultLimitMax;
            }
            else if (min > max)
            {
                min = max;   // narrowing left no room: the maximum protects the game, the minimum gives way
            }

            var result = new KeyValuePair<float, float>(min, max);
            _limitCache[channel] = result;
            return result;
        }

        /// <summary>The first entry of a list sorted by <see cref="SortBySpecificity"/> that matches the channel.</summary>
        private static ChannelLimit MostSpecific(ChannelLimit[] sorted, string channel)
        {
            foreach (ChannelLimit limit in sorted)
            {
                if (Matches(limit.Pattern, channel)) return limit;
            }
            return null;
        }

        private static bool Matches(string pattern, string channel)
        {
            if (pattern == AllChannels) return true;
            if (ChannelFamilies.IsFamilyName(pattern)) return ChannelFamilies.Matches(pattern, channel);
            return string.Equals(pattern, channel, StringComparison.Ordinal);
        }

        /// <summary>Exact names first, then families from the longest prefix to the shortest, then <c>*</c>.</summary>
        private static ChannelLimit[] SortBySpecificity(List<ChannelLimit> limits)
        {
            var sorted = new List<ChannelLimit>(limits);
            sorted.Sort((a, b) =>
            {
                int byRank = Rank(b.Pattern).CompareTo(Rank(a.Pattern));
                return byRank != 0 ? byRank : string.CompareOrdinal(a.Pattern, b.Pattern);
            });
            return sorted.ToArray();
        }

        /// <summary>Bigger is more specific: an exact name beats every family, a longer family beats a shorter one, <c>*</c> comes last.</summary>
        private static int Rank(string pattern)
        {
            if (pattern == AllChannels) return 0;
            if (ChannelFamilies.IsFamilyName(pattern)) return 1 + pattern.Length;
            return 1000;
        }

        private static int Narrow(int current, int candidate)
        {
            if (candidate < 0) return current;
            return current < 0 || candidate < current ? candidate : current;
        }
    }
}
