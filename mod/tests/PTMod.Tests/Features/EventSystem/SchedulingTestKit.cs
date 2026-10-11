using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Rng;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    // Fakes of the scheduler's surroundings, shared by the scheduling tests. Everything is internal, so the runner never takes it for a test.

    /// <summary>A world of companies from a table: keys, lines, shares, revenues; conditions are evaluated with the company as the subject and today's date.</summary>
    internal sealed class FakeWorld : IEventWorld
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, int> _lines = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _shares = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _revenues = new Dictionary<string, float>(StringComparer.Ordinal);

        public readonly HashSet<string> Bankrupt = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> RevenueHistory = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Fabs = new HashSet<string>(StringComparer.Ordinal);

        public int Today { get; set; }

        /// <summary>The view of the game the conditions read (a <c>FakeGameState</c>); null - every predicate that needs the game is false.</summary>
        public IGameState State { get; set; }

        /// <summary>A world with the player and <paramref name="aiCount"/> AI companies <c>ai:0</c>.. .</summary>
        public static FakeWorld WithCompanies(int aiCount)
        {
            var world = new FakeWorld();
            world.Add(CompanyKey.Player);
            for (int i = 0; i < aiCount; i++) world.Add(CompanyKey.ForAi(i));
            return world;
        }

        public FakeWorld Add(string key, int lines = 0, float share = 0f, float revenue = 0f)
        {
            if (!_order.Contains(key)) _order.Add(key);
            _lines[key] = lines;
            _shares[key] = share;
            _revenues[key] = revenue;
            return this;
        }

        public IList<string> CompanyKeys
        {
            get
            {
                var keys = new List<string>();
                foreach (string key in _order)
                {
                    if (!Bankrupt.Contains(key)) keys.Add(key);
                }
                return new ReadOnlyCollection<string>(keys);
            }
        }

        public bool IsAlive(string companyKey)
        {
            return companyKey != null && _order.Contains(companyKey) && !Bankrupt.Contains(companyKey);
        }

        public int FactoryLines(string companyKey)
        {
            int value;
            return companyKey != null && _lines.TryGetValue(companyKey, out value) ? value : 0;
        }

        public float MarketShare(string companyKey)
        {
            float value;
            return companyKey != null && _shares.TryGetValue(companyKey, out value) ? value : 0f;
        }

        public float QuarterlyRevenue(string companyKey)
        {
            float value;
            return companyKey != null && _revenues.TryGetValue(companyKey, out value) ? value : 0f;
        }

        public bool HasRevenueHistory(string companyKey)
        {
            return companyKey != null && RevenueHistory.Contains(companyKey);
        }

        public bool HasFab(string companyKey)
        {
            return companyKey != null && Fabs.Contains(companyKey);
        }

        public bool Check(ICondition condition, string companyKey)
        {
            if (condition == null) return true;
            return ConditionEvaluator.Evaluate(condition, new ConditionContext(State, EventDays.ToDate(Today), companyKey));
        }
    }

    /// <summary>A catalog of definitions built in code, with one config and the pack options by id.</summary>
    internal sealed class FakeCatalog : ISchedulerCatalog
    {
        private readonly List<EventDefinition> _all = new List<EventDefinition>();
        private readonly Dictionary<string, PackOptions> _packs = new Dictionary<string, PackOptions>(StringComparer.Ordinal);
        private readonly ReadOnlyCollection<EventDefinition> _view;

        public FakeCatalog(EventConfig config, params EventDefinition[] definitions)
        {
            Config = config ?? EventConfig.Defaults;
            _all.AddRange(definitions);
            _view = new ReadOnlyCollection<EventDefinition>(_all);
        }

        /// <summary>A config of the pack <c>event-system</c> with these categories and <c>maxPendingChoices</c>.</summary>
        public static EventConfig ConfigWith(int maxPendingChoices, params CategoryDefinition[] categories)
        {
            var pack = new PackConfig { PackId = "event-system", MaxPendingChoices = maxPendingChoices };
            pack.Categories.AddRange(categories);
            return EventConfig.Merge(new[] { pack }, line => { });
        }

        /// <summary>A category of the pack <c>event-system</c> without pacing limits unless given.</summary>
        public static CategoryDefinition Category(string id, float dailyChance, int minGapDays = 0, int maxActive = PackConfigReader.NoLimit, string packId = "event-system")
        {
            return new CategoryDefinition { Id = id, DailyChance = dailyChance, MinGapDays = minGapDays, MaxActive = maxActive, PackId = packId };
        }

        public FakeCatalog Pack(string packId, PackOptions options)
        {
            _packs[packId] = options;
            return this;
        }

        public EventDefinition Get(string eventId)
        {
            foreach (EventDefinition definition in _all)
            {
                if (string.Equals(definition.id, eventId, StringComparison.Ordinal)) return definition;
            }
            return null;
        }

        public IList<EventDefinition> All
        {
            get { return _view; }
        }

        public IReadOnlyList<EventDefinition> InCategory(string category)
        {
            var list = new List<EventDefinition>();
            foreach (EventDefinition definition in _all)
            {
                if (string.Equals(definition.category, category, StringComparison.Ordinal)) list.Add(definition);
            }
            return list;
        }

        public EventConfig Config { get; private set; }

        public PackOptions OptionsOf(string packId)
        {
            PackOptions options;
            return packId != null && _packs.TryGetValue(packId, out options) ? options : null;
        }
    }

    /// <summary>
    /// A runtime that records the steps of the day and the firings. A firing creates one instance per target; an event with choices for the player opens a
    /// pending choice in the history's index (the way the choice controller will), so the limit of open choices can be seen.
    /// </summary>
    internal sealed class RecordingRuntime : ISchedulerRuntime
    {
        public readonly List<string> Steps = new List<string>();
        public readonly List<FiringPlan> Fired = new List<FiringPlan>();

        /// <summary>The history whose index gets the player's pending choices; null - none.</summary>
        public EventHistory History;

        /// <summary>Replaces the answer of <see cref="Fire"/>.</summary>
        public Func<FiringPlan, int> OnFire;

        public int NextInstanceId = 1;

        public void ExpireEffects(int today)
        {
            Steps.Add("expire");
        }

        public void TickEffects(int today)
        {
            Steps.Add("tick");
        }

        public void RunDeadlines(int today)
        {
            Steps.Add("deadlines");
        }

        public int Fire(FiringPlan plan)
        {
            Steps.Add("fire:" + plan.Definition.id + ":" + plan.Origin);
            if (OnFire != null) return OnFire(plan);

            Fired.Add(plan);
            foreach (string key in plan.Targets)
            {
                int id = NextInstanceId++;
                if (History != null && plan.HasChoices && CompanyKey.IsPlayer(key))
                {
                    History.Active.Set(id, 0, plan.Definition.id, plan.Definition.category, key, true, false);
                }
            }
            return plan.Targets.Count;
        }

        public int FiredCount(string eventId)
        {
            int count = 0;
            foreach (FiringPlan plan in Fired)
            {
                if (plan.Definition.id == eventId) count++;
            }
            return count;
        }
    }

    /// <summary>A seeded xoshiro128** that counts the numbers taken from it.</summary>
    internal sealed class CountingRng : IModRng
    {
        private readonly Xoshiro128 _inner;

        public CountingRng(uint seed)
        {
            _inner = new Xoshiro128(seed);
        }

        public int Draws { get; private set; }

        public int Range(int minInclusive, int maxExclusive)
        {
            Draws++;
            return _inner.Range(minInclusive, maxExclusive);
        }

        public float Value()
        {
            Draws++;
            return _inner.Value();
        }

        public bool Chance(float probability)
        {
            Draws++;
            return _inner.Chance(probability);
        }

        public int WeightedIndex(IList<float> weights)
        {
            Draws++;
            return _inner.WeightedIndex(weights);
        }

        public T Pick<T>(IList<T> items)
        {
            Draws++;
            return _inner.Pick(items);
        }
    }

    /// <summary>Definitions built in code (the mapper and the validator are tested elsewhere).</summary>
    internal static class Defs
    {
        public const string Pack = "event-system";

        public static EventDefinition Daily(string id, string category, float weight = 1f, EventTargetType target = EventTargetType.Player, int cooldownDays = 0)
        {
            return New(id, category, new EventTrigger { type = EventTriggerType.DailyChance }, target, weight, cooldownDays);
        }

        public static EventDefinition Date(string id, int year, int month, int day, int graceDays = 31, EventTargetType target = EventTargetType.Player)
        {
            return New(id, null, new EventTrigger { type = EventTriggerType.Date, year = year, month = month, day = day, graceDays = graceDays }, target, 1f, 0);
        }

        public static EventDefinition Condition(string id, ICondition conditions, string checkEvery = "Day", bool edge = false, float chance = 1f,
            EventTargetType target = EventTargetType.Player)
        {
            EventDefinition definition = New(id, null,
                new EventTrigger { type = EventTriggerType.Condition, checkEvery = checkEvery, edge = edge, chance = chance }, target, 1f, 0);
            definition.conditions = conditions;
            return definition;
        }

        public static EventDefinition Manual(string id, EventTargetType target = EventTargetType.Player)
        {
            return New(id, null, new EventTrigger { type = EventTriggerType.Manual }, target, 1f, 0);
        }

        /// <summary>Gives the event two choices (<c>a</c>, the default, and <c>b</c>) and a timer.</summary>
        public static EventDefinition WithChoices(this EventDefinition definition)
        {
            definition.choices = new[] { new EventChoice { id = "a", textKey = "es.t.a" }, new EventChoice { id = "b", textKey = "es.t.b" } };
            definition.defaultChoice = "a";
            definition.timerDays = 14;
            return definition;
        }

        /// <summary>A condition from a function of the subject and the day.</summary>
        public static ICondition When(Func<string, int, bool> test)
        {
            return new DelegateCondition("test", ctx => test(ctx.SubjectKey, EventDays.FromDate(ctx.Today)));
        }

        public static int Day(int year, int month, int day)
        {
            int result;
            Assert.True(EventDays.TryOf(year, month, day, out result), "bad test date");
            return result;
        }

        private static EventDefinition New(string id, string category, EventTrigger trigger, EventTargetType target, float weight, int cooldownDays)
        {
            return new EventDefinition
            {
                id = id,
                category = category,
                trigger = trigger,
                targets = new EventTargetSpec { type = target },
                weight = weight,
                cooldownDays = cooldownDays,
                packId = Pack,
            };
        }
    }

    /// <summary>A scheduler over the fakes, with the faults and the log lines it reported.</summary>
    internal sealed class SchedulerRig
    {
        public readonly FakeWorld World;
        public readonly FakeCatalog Catalog;
        public readonly RecordingRuntime Runtime = new RecordingRuntime();
        public readonly CountingRng Daily;
        public readonly EventRandom Random;
        public readonly SchedulerState State = new SchedulerState();
        public readonly EventHistory History = new EventHistory();
        public readonly SchedulerOptions Options = new SchedulerOptions();
        public readonly EventScheduler Scheduler;
        public readonly List<string> Faults = new List<string>();
        public readonly List<string> Infos = new List<string>();
        public readonly HashSet<string> FrozenPacks = new HashSet<string>(StringComparer.Ordinal);
        public int Frequency = Pacing.DefaultFrequencySetting;

        public SchedulerRig(FakeCatalog catalog, FakeWorld world = null, uint seed = 20261010u, uint worldSeed = 777u)
        {
            Catalog = catalog;
            World = world ?? FakeWorld.WithCompanies(3);
            Daily = new CountingRng(seed);
            Random = new EventRandom(Daily, () => worldSeed);
            Runtime.History = History;
            Options.FrequencySetting = () => Frequency;
            Options.IsPackActive = pack => !FrozenPacks.Contains(pack);
            Options.OnFault = (site, error) => Faults.Add(site + ": " + error.Message);
            Options.OnInfo = line => Infos.Add(line);
            Scheduler = new EventScheduler(Catalog, World, Runtime, Random, State, History, Options);
        }

        /// <summary>Processes <paramref name="count"/> days from <paramref name="first"/> on, keeping the world's today in step.</summary>
        public void RunDays(int first, int count)
        {
            for (int day = first; day < first + count; day++)
            {
                World.Today = day;
                Scheduler.ProcessDay(day);
            }
        }

        public void RunDay(int day)
        {
            RunDays(day, 1);
        }
    }
}
