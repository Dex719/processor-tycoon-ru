using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Rng;

namespace PTMod.Features.EventSystem
{
    /// <summary>The three numbers a category lottery takes from the daily stream every day (D6): the roll, the event, the target.</summary>
    public struct LotteryDraw
    {
        /// <summary>"Is there an event today": the category fires when <c>U1 &lt; dailyChance × frequency</c>.</summary>
        public float U1;

        /// <summary>Which event of the category, by <c>weight</c>.</summary>
        public float U2;

        /// <summary>Which company, by <c>targets.weighting</c>.</summary>
        public float U3;
    }

    /// <summary>
    /// A local generator of the engine (D5, FR-9 items 3-4): xoshiro128** started from a seed derived from the world seed and a key, the same algorithm and
    /// the same expansion of the seed as the streams of <c>ModRandom</c>. It is not saved: the same key always gives the same sequence again, so an effect or a
    /// trigger rolls the same numbers after a load, and using it never moves the daily stream.
    /// </summary>
    public sealed class DerivedRng : IModRng
    {
        private readonly Xoshiro128 _inner;

        public DerivedRng(uint seed)
        {
            Seed = seed;
            _inner = new Xoshiro128(seed);
        }

        /// <summary>The seed the generator started from.</summary>
        public uint Seed { get; private set; }

        public int Range(int minInclusive, int maxExclusive)
        {
            return _inner.Range(minInclusive, maxExclusive);
        }

        public float Value()
        {
            return _inner.Value();
        }

        public bool Chance(float probability)
        {
            return _inner.Chance(probability);
        }

        public int WeightedIndex(IList<float> weights)
        {
            return _inner.WeightedIndex(weights);
        }

        public T Pick<T>(IList<T> items)
        {
            return _inner.Pick(items);
        }
    }

    /// <summary>
    /// The randomness of the engine (D5, FR-9), and nothing else: the engine never calls <c>System.Random</c> or <c>UnityEngine.Random</c>.
    /// <list type="bullet">
    /// <item><b>Daily stream</b> <c>ModRandom.Stream("event-system/daily")</c>: <see cref="DrawCategory"/> takes exactly <see cref="NumbersPerCategory"/> numbers
    /// for every category with <c>DailyChance</c> events, every processed day, whatever happens next. Adding an event or changing a condition therefore never
    /// shifts the sequence (AC-9.2). Its state is saved by <c>ModRandom</c>.</item>
    /// <item><b>Derived generators</b> <see cref="DerivedRng"/>: <c>hash32(WorldSeed, "inst", instanceId)</c> for the effects of an instance and
    /// <c>hash32(WorldSeed, "trg", eventId, day)</c> for the rolls of <c>Date</c>, <c>Condition</c> and <c>Manual</c> triggers. <c>hash32</c> is the seed rule of
    /// <c>ModRandom</c> (world seed XOR FNV-1a of the text) over <c>event-system/inst/&lt;id&gt;</c> and <c>event-system/trg/&lt;eventId&gt;/&lt;day&gt;</c>.</item>
    /// </list>
    /// </summary>
    public sealed class EventRandom
    {
        /// <summary>The id of the daily stream of <c>ModRandom</c>.</summary>
        public const string DailyStreamId = "event-system/daily";

        /// <summary>Numbers the lottery of one category takes from the daily stream every day.</summary>
        public const int NumbersPerCategory = 3;

        private const string KeyRoot = "event-system/";
        private const string InstanceTag = "inst";
        private const string TriggerTag = "trg";
        private const string DecisionTag = "ai";

        private readonly IModRng _daily;
        private readonly Func<uint> _worldSeed;

        /// <param name="daily">The daily stream (<c>ModRandom.Stream(DailyStreamId)</c>; a seeded generator in tests).</param>
        /// <param name="worldSeed">The world seed of the game (<c>ModRandom.WorldSeed</c>), read whenever a generator is derived.</param>
        public EventRandom(IModRng daily, Func<uint> worldSeed)
        {
            if (daily == null) throw new ArgumentNullException("daily");
            if (worldSeed == null) throw new ArgumentNullException("worldSeed");
            _daily = daily;
            _worldSeed = worldSeed;
        }

        /// <summary>The daily stream.</summary>
        public IModRng Daily
        {
            get { return _daily; }
        }

        /// <summary>The world seed the derived generators start from.</summary>
        public uint WorldSeed
        {
            get { return _worldSeed(); }
        }

        /// <summary>The three numbers of one category lottery, always all three (FR-9 item 2).</summary>
        public LotteryDraw DrawCategory()
        {
            var draw = new LotteryDraw();
            draw.U1 = _daily.Value();
            draw.U2 = _daily.Value();
            draw.U3 = _daily.Value();
            return draw;
        }

        /// <summary>The generator of the effects of an instance (FR-9 item 3).</summary>
        public DerivedRng ForInstance(int instanceId)
        {
            return new DerivedRng(InstanceSeed(WorldSeed, instanceId));
        }

        /// <summary>The generator of the rolls of a trigger on a day (FR-9 item 4).</summary>
        public DerivedRng ForTrigger(string eventId, int day)
        {
            return new DerivedRng(TriggerSeed(WorldSeed, eventId, day));
        }

        /// <summary>
        /// The generator handed to the AI choosers deciding on an instance (<c>IEventAiChooser.Choose</c>): <c>hash32(WorldSeed, "ai", instanceId)</c>, apart
        /// from the generator of the instance's effects, so a chooser's draws never repeat the numbers of the effects.
        /// </summary>
        public DerivedRng ForDecision(int instanceId)
        {
            return new DerivedRng(Hash32(WorldSeed, DecisionTag, instanceId.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary><c>hash32(worldSeed, "inst", instanceId)</c>.</summary>
        public static uint InstanceSeed(uint worldSeed, int instanceId)
        {
            return Hash32(worldSeed, InstanceTag, instanceId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary><c>hash32(worldSeed, "trg", eventId, day)</c>.</summary>
        public static uint TriggerSeed(uint worldSeed, string eventId, int day)
        {
            return Hash32(worldSeed, TriggerTag, eventId ?? string.Empty, day.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The seed of a derived generator: the seed rule of the named streams of <c>ModRandom</c> (<c>RngStreams.DeriveStreamSeed</c>: the world seed XOR the
        /// FNV-1a hash of the UTF-8 text) over <c>event-system/</c> and the parts joined with <c>/</c>. Ids of events contain no <c>/</c>.
        /// </summary>
        public static uint Hash32(uint worldSeed, params string[] parts)
        {
            string key = KeyRoot + string.Join("/", parts ?? new string[0]);
            return RngStreams.DeriveStreamSeed(worldSeed, key);
        }
    }
}
