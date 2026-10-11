using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    // Fakes of the surroundings of the effects, shared by the effect tests. Everything is internal, so the runner never takes it for a test.

    /// <summary>The game as the standard effects change it, as a table: revenues, money calls, popularity, research, reputation, mails.</summary>
    internal sealed class FakeEffectWorld : IEffectWorld
    {
        public float InflationValue = 1f;
        public readonly Dictionary<string, float> Revenue = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<string> Calls = new List<string>();

        /// <summary>CPUs on the market per company (0 - none); a missing key has 1.</summary>
        public readonly Dictionary<string, int> CpusOnMarket = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The progress of the current research per company; a company without an entry researches nothing.</summary>
        public readonly Dictionary<string, float> Research = new Dictionary<string, float>(StringComparer.Ordinal);

        public readonly Dictionary<string, float> Reputation = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly List<string> ReputationReasons = new List<string>();
        public readonly List<string[]> Mails = new List<string[]>();
        public bool RefuseMail;
        public int NextMailId = 100000;

        /// <summary>Spends and earns: (company, signed amount, origin).</summary>
        public readonly List<KeyValuePair<string, float>> Money = new List<KeyValuePair<string, float>>();
        public readonly List<string> Origins = new List<string>();

        public float Inflation
        {
            get { return InflationValue; }
        }

        public float QuarterlyRevenue(string companyKey)
        {
            float value;
            return companyKey != null && Revenue.TryGetValue(companyKey, out value) ? value : 0f;
        }

        public bool Spend(string companyKey, float amount, string origin)
        {
            if (Missing.Contains(companyKey)) return false;
            Calls.Add("spend " + companyKey + " " + amount + " " + origin);
            Money.Add(new KeyValuePair<string, float>(companyKey, -amount));
            Origins.Add(origin);
            return true;
        }

        public bool Earn(string companyKey, float amount, string origin)
        {
            if (Missing.Contains(companyKey)) return false;
            Calls.Add("earn " + companyKey + " " + amount + " " + origin);
            Money.Add(new KeyValuePair<string, float>(companyKey, amount));
            Origins.Add(origin);
            return true;
        }

        public int AddPopularity(string companyKey, float delta, string cpuSelector, string segment)
        {
            Calls.Add("popularity " + companyKey + " " + delta + " " + cpuSelector + " " + segment);
            if (Missing.Contains(companyKey)) return 0;
            int count;
            return CpusOnMarket.TryGetValue(companyKey, out count) ? count : 1;
        }

        public bool AddResearchProgress(string companyKey, float delta)
        {
            float progress;
            if (companyKey == null || !Research.TryGetValue(companyKey, out progress)) return false;
            Research[companyKey] = EffectMath.ResearchProgress(progress, delta);
            Calls.Add("research " + companyKey + " " + delta);
            return true;
        }

        public bool AddReputation(string companyKey, float delta, string reasonKey)
        {
            if (Missing.Contains(companyKey)) return false;
            float value;
            Reputation.TryGetValue(companyKey, out value);
            Reputation[companyKey] = value + delta;
            ReputationReasons.Add(reasonKey);
            Calls.Add("reputation " + companyKey + " " + delta + " " + reasonKey);
            return true;
        }

        public int SendMail(string titleKey, string textKey, string tag, string[] args)
        {
            if (RefuseMail) return -1;
            Mails.Add(new[] { titleKey, textKey, tag, string.Join(",", args ?? new string[0]) });
            return NextMailId++;
        }

        public int CallsStartingWith(string prefix)
        {
            int count = 0;
            foreach (string call in Calls)
            {
                if (call.StartsWith(prefix, StringComparison.Ordinal)) count++;
            }
            return count;
        }
    }

    /// <summary>A generator that hands out the given numbers in order (then 0.5) and counts them.</summary>
    internal sealed class ScriptedRng : IModRng
    {
        private readonly Queue<float> _values;

        public ScriptedRng(params float[] values)
        {
            _values = new Queue<float>(values ?? new float[0]);
        }

        public int Draws { get; private set; }

        public float Value()
        {
            Draws++;
            return _values.Count > 0 ? _values.Dequeue() : 0.5f;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)Math.Floor(Value() * (maxExclusive - minInclusive));
        }

        public bool Chance(float probability)
        {
            return Value() < probability;
        }

        public int WeightedIndex(IList<float> weights)
        {
            return WeightedPick.Index(weights, Value());
        }

        public T Pick<T>(IList<T> items)
        {
            if (items == null || items.Count == 0) return default(T);
            return items[Range(0, items.Count)];
        }
    }

    /// <summary>A sink that records the providers and every invalidation, and answers nothing.</summary>
    internal sealed class RecordingSink : IModifierChannelSink
    {
        public readonly List<string> Registered = new List<string>();
        public readonly Dictionary<string, Func<string, ProviderResult>> Providers = new Dictionary<string, Func<string, ProviderResult>>(StringComparer.Ordinal);
        public readonly List<string> Invalidations = new List<string>();

        public void RegisterProvider(string channel, Func<string, ProviderResult> provider)
        {
            Registered.Add(channel);
            Providers[channel] = provider;
        }

        public void Invalidate(string channel, string companyKey)
        {
            Invalidations.Add(channel + "|" + (companyKey ?? "*"));
        }
    }

    /// <summary>
    /// The sink over the core's own channel engine (<see cref="ChannelRegistry"/>, the logic of <c>ModModifiers</c> with its daily cache): what a formula of the
    /// game would get from <c>ModModifiers.Get</c> and <c>Explain</c>. <see cref="ForwardInvalidate"/> false shows what a missing invalidation would cost.
    /// </summary>
    internal sealed class RegistrySink : IModifierChannelSink
    {
        public readonly ChannelRegistry Registry;
        public readonly List<string> Invalidations = new List<string>();
        public bool ForwardInvalidate = true;

        /// <param name="featureActive">The rule of the core for a feature that is off (its providers are passed by); null - every feature is on.</param>
        public RegistrySink(Func<string, bool> featureActive = null)
        {
            Registry = new ChannelRegistry(featureActive, new DateTime(1990, 1, 1));
        }

        public void RegisterProvider(string channel, Func<string, ProviderResult> provider)
        {
            Assert.True(Registry.RegisterReasonedProvider("event-system", channel, query => provider(query.CompanyKey)), "the registry refused the provider of " + channel);
        }

        public void Invalidate(string channel, string companyKey)
        {
            Invalidations.Add(channel + "|" + (companyKey ?? "*"));
            if (!ForwardInvalidate) return;
            if (companyKey == null) Registry.Invalidate(channel);
            else Registry.Invalidate(channel, companyKey);
        }

        public float Get(string channel, string companyKey)
        {
            return Registry.Get(channel, new ModifierQuery(companyKey));
        }

        public void Declare(string channel, float min, float max, float balanceMin = 0f, float balanceMax = 0f)
        {
            Assert.True(Registry.Declare(new ChannelSpec { Name = channel, Min = min, Max = max, BalanceMin = balanceMin, BalanceMax = balanceMax }), "declare " + channel);
        }
    }

    /// <summary>A table of effect ports built in code.</summary>
    internal sealed class PortTable : IEffectTable
    {
        private readonly Dictionary<string, IEffectHandlerPort> _ports = new Dictionary<string, IEffectHandlerPort>(StringComparer.Ordinal);

        public PortTable Add(IEffectHandlerPort port)
        {
            _ports[port.Type] = port;
            return this;
        }

        public IEffectHandlerPort Get(string type)
        {
            IEffectHandlerPort port;
            return type != null && _ports.TryGetValue(type, out port) ? port : null;
        }

        /// <summary>The nine standard types over the fakes.</summary>
        public static PortTable Standard(IEffectWorld world, IChainHost chain)
        {
            var table = new PortTable();
            table.Add(new MoneyEffect(world)).Add(new ModifierTimedEffect()).Add(new FactoryOutputEffect()).Add(new MarketDemandEffect())
                .Add(new PopularityEffect(world)).Add(new ReputationEffect(world)).Add(new ResearchProgressEffect(world)).Add(new SendMailEffect(world))
                .Add(new FireEventEffect(chain));
            return table;
        }
    }

    /// <summary>A handler of a test type that counts its calls, can throw, and keeps its value as a contribution when it is timed.</summary>
    internal sealed class CountingPort : IEffectHandlerPort
    {
        public int Resolves;
        public int Applies;
        public int Describes;
        public int Ticks;
        public int Removes;
        public int Restores;
        public bool ThrowOnResolve;
        public bool ThrowOnApply;
        public bool ThrowOnTick;
        public float Value = 2f;
        public string Channel = "test.channel";

        public CountingPort(string type, bool timed)
        {
            Type = type;
            IsTimed = timed;
        }

        public string Type { get; private set; }
        public bool IsTimed { get; private set; }

        public AppliedEffect Resolve(EventEffect spec, EffectCall call)
        {
            Resolves++;
            if (ThrowOnResolve) throw new InvalidOperationException("resolve failed on purpose");
            return new AppliedEffect { value = Value, channel = IsTimed ? Channel : string.Empty, durationDays = spec.durationDays };
        }

        public void Apply(AppliedEffect effect, EffectCall call)
        {
            Applies++;
            if (ThrowOnApply) throw new InvalidOperationException("apply failed on purpose");
            if (IsTimed) call.Contributions.Set(effect, effect.channel, effect.companyKey, effect.value);
        }

        public EffectText Describe(AppliedEffect effect)
        {
            Describes++;
            return new EffectText("test.fx", effect.value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void Tick(AppliedEffect effect, EffectCall call)
        {
            Ticks++;
            if (ThrowOnTick) throw new InvalidOperationException("tick failed on purpose");
        }

        public void Remove(AppliedEffect effect, EffectCall call)
        {
            Removes++;
            call.Contributions.Clear(effect);
        }

        public void Restore(AppliedEffect effect, EffectCall call)
        {
            Restores++;
            call.Contributions.Set(effect, effect.channel, effect.companyKey, effect.value);
        }
    }

    /// <summary>Effects and events built in code, with real <c>params</c> objects.</summary>
    internal static class Fx
    {
        /// <summary>An effect with its params from JSON (single quotes stand for double quotes).</summary>
        public static EventEffect Effect(string type, string paramsJson = "{}", int durationDays = 0)
        {
            return new EventEffect { type = type, @params = new EffectParams(EventTestKit.Obj(paramsJson)), durationDays = durationDays };
        }

        public static EventChoice Choice(string id, params EventEffect[] effects)
        {
            return new EventChoice { id = id, textKey = "es.t." + id, effects = effects };
        }

        /// <summary>A manual event of the pack <c>event-system</c> for the player with these choices (the first is the default) and own effects.</summary>
        public static EventDefinition Event(string id, EventChoice[] choices, params EventEffect[] effects)
        {
            var definition = Defs.Manual(id);
            definition.title = LocText.FromKey(id + ".title");
            definition.effects = effects ?? new EventEffect[0];
            if (choices != null && choices.Length > 0)
            {
                definition.choices = choices;
                definition.defaultChoice = choices[0].id;
                definition.timerDays = 14;
            }
            return definition;
        }

        /// <summary>The config of one pack with the limits of the design example (<c>*</c> [0.25; 4], <c>production.output</c> [0.1; 3], ...).</summary>
        public static EventConfig DesignLimits(params ChannelLimit[] extra)
        {
            var pack = new PackConfig { PackId = Defs.Pack };
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "*", Min = 0.25f, Max = 4f, PackId = Defs.Pack });
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "production.output", Min = 0.1f, Max = 3f, PackId = Defs.Pack });
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "market.demand.*", Min = 0.4f, Max = 2.5f, PackId = Defs.Pack });
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "popularity.gain", Min = 0.7f, Max = 1.6f, PackId = Defs.Pack });
            foreach (ChannelLimit limit in extra) pack.ChannelLimits.Add(limit);
            return EventConfig.Merge(new[] { pack }, line => { });
        }
    }

    /// <summary>
    /// The scheduler and the effect runtime together over the fakes: a catalog of definitions, a world of companies, the standard effect types over
    /// <see cref="FakeEffectWorld"/>, the real aggregator over the core's channel engine, the journal, and the notifications of the bus as lists.
    /// </summary>
    internal sealed class EffectsRig
    {
        public readonly FakeCatalog Catalog;
        public readonly FakeWorld World;
        public readonly FakeEffectWorld Game = new FakeEffectWorld();
        public readonly SchedulerState State = new SchedulerState();
        public readonly EventHistory History = new EventHistory();
        public readonly EventRandom Random;
        public readonly RegistrySink Sink;
        public readonly ModifierAggregator Aggregator;
        public readonly EventLogStore Log;
        public readonly ScheduledFires Chain;
        public readonly PortTable Table;
        public readonly EffectRuntimeOptions Options = new EffectRuntimeOptions();
        public readonly EffectRuntime Runtime;
        public readonly EventScheduler Scheduler;
        public readonly List<string> Faults = new List<string>();
        public readonly List<string> Infos = new List<string>();
        public readonly List<string> Bus = new List<string>();
        public readonly HashSet<string> FrozenPacks = new HashSet<string>(StringComparer.Ordinal);

        public EffectsRig(FakeCatalog catalog, FakeWorld world = null, uint worldSeed = 777u, RegistrySink sink = null, DayTimer timer = null)
        {
            Catalog = catalog;
            Sink = sink ?? new RegistrySink();
            World = world ?? FakeWorld.WithCompanies(3);
            Random = new EventRandom(new CountingRng(20261011u), () => worldSeed);
            Log = new EventLogStore(() => Catalog.Config.LogLimit);
            Chain = new ScheduledFires(State, History);
            Table = PortTable.Standard(Game, Chain);

            EffectRuntime runtime = null;
            Aggregator = new ModifierAggregator(Sink, () => Catalog.Config, effect => runtime != null ? runtime.PackOf(effect) : null,
                effect => "name of " + effect.instanceId);
            Options.OnFault = (site, error) => Faults.Add(site + ": " + error.Message);
            Options.OnInfo = line => Infos.Add(line);
            Options.Fired = instance => Bus.Add("fired " + instance.Data.eventId + " " + instance.Data.companyKey);
            Options.ChoiceResolved = (instance, choice, auto) => Bus.Add("resolved " + instance.Data.eventId + " " + choice + (auto ? " auto" : ""));
            Options.EffectExpired = effect => Bus.Add("expired " + effect.id);
            runtime = new EffectRuntime(Table, Aggregator, Random, History, Catalog, Log, Options);
            Runtime = runtime;

            var schedulerOptions = new SchedulerOptions
            {
                OnFault = (site, error) => Faults.Add("scheduler/" + site + ": " + error.Message),
                OnInfo = line => Infos.Add(line),
                IsPackActive = pack => !FrozenPacks.Contains(pack),
                Timer = timer,
            };
            Scheduler = new EventScheduler(Catalog, World, Runtime, Random, State, History, schedulerOptions);
        }

        /// <summary>Fires an event now for the company (or by its targets) on the world's today.</summary>
        public EventFireResult Fire(string eventId, string companyKey = null, int delay = 0)
        {
            return Scheduler.Fire(eventId, new EventFireContext { CompanyKey = companyKey, DelayDays = delay, IgnoreConditions = true });
        }

        /// <summary>Processes the days from <paramref name="first"/> to <paramref name="last"/> inclusive.</summary>
        public void RunDays(int first, int last)
        {
            for (int day = first; day <= last; day++)
            {
                World.Today = day;
                Scheduler.ProcessDay(day);
            }
        }

        /// <summary>The live instance of the event (the first), or null.</summary>
        public EventInstanceDto Instance(string eventId)
        {
            foreach (EventInstanceDto instance in Runtime.Instances)
            {
                if (instance.eventId == eventId) return instance;
            }
            return null;
        }

        /// <summary>The journal entries of a kind.</summary>
        public List<EventLogEntry> Entries(string kind)
        {
            var list = new List<EventLogEntry>();
            foreach (EventLogEntry entry in Log.Entries)
            {
                if (entry.kind == kind) list.Add(entry);
            }
            return list;
        }

        /// <summary>The value of a channel as a formula of the game would read it now.</summary>
        public float Channel(string channel, string companyKey = CompanyKey.Player)
        {
            return Sink.Get(channel, companyKey);
        }
    }
}
