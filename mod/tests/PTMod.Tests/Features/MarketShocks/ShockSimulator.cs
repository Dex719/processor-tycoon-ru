using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.MarketShocks
{
    // The simulator of the balance of the pack (TSK-015). It plays the days of a game over the shipped files Content/market-shocks/config.json and
    // shocks.json with the rules of the event system as design.md of event-system writes them down:
    //
    //   * the daily lottery of a category: three numbers a day whatever happens; u1 < dailyChance x F_settings x F_pack, then the pace of the category
    //     (minGapDays since the last firing of ANY event of the category, maxActive going on), then one fitting event by weight (u2) and its target by
    //     weight (u3); a company is a candidate when it passes the filter of the targets, the conditions of the event and the cooldown of the event;
    //   * AllCompanies is one group: every fitting company, global effects on the first instance only; the cooldown of a group is kept per event ("*");
    //   * Condition triggers on the first of a month, then the cooldown; Manual events fire only through FireEvent (with its delay and its chance);
    //   * an instance is "going on" while it waits for a choice or has a running effect (es.activeInCategory, maxActive, maxPendingChoices);
    //   * effects: Money = round_to_1000(clamp(f x QR, min x I, max x I) x s); multipliers = clamp(1 + (m - 1) x s, limit min, limit max) at Resolve (D15);
    //     the ramps and waves are the pack's own RampSpec / WaveSpec / ShockCurves; the aggregate of a channel = product of the live contributions of
    //     the company and the global ones, clamped by the narrowest channel limit of the pack;
    //   * choices: the AI takes an option with aiMinCashMultiple only when it has the money, otherwise by aiWeight; the player waits out the timer
    //     and gets defaultChoice (the worst case) unless a test says otherwise.
    //
    // The engine itself is not part of the build of the feature tests (build_feature.py takes Logic of the feature alone), so this is a model of its
    // rules, not its code; the lottery and the cooldowns follow Pacing / EventScheduler / TargetSelector of event-system line by line. Not a test and
    // not a logic type: exempt from the [Covers] check.

    internal enum PlayerPolicy
    {
        /// <summary>The player lets the timer run out: defaultChoice.</summary>
        Default,

        /// <summary>The player takes the first option.</summary>
        First,

        /// <summary>The player takes the last option.</summary>
        Last,
    }

    internal sealed class SimCompany
    {
        public SimCompany(string key, int lines, double share, double quarterlyRevenue)
        {
            Key = key;
            Lines = lines;
            Share = share;
            QuarterlyRevenue = quarterlyRevenue;
        }

        public readonly string Key;
        public int Lines;
        public double Share;
        public double QuarterlyRevenue;
        public bool SellsBelowCost;
        public bool HasRecentCpu = true;
        public double MoneySpent;

        public bool IsPlayer
        {
            get { return Key == CompanyKey.Player; }
        }
    }

    internal sealed class SimSetup
    {
        public DateTime Start = new DateTime(1971, 1, 1);   // DateController starts on 1971-01-01 (CODEMAP), day 0 of the engine
        public int Seed = 1;
        public float Severity = 1f;
        public float PackFrequency = 1f;
        public float Inflation = 3f;
        public bool AiHasCash = true;
        public PlayerPolicy Player = PlayerPolicy.Default;
        public bool TrackChannels = true;

        /// <summary>Keep every resolved effect in <see cref="ShockSimulator.Resolved"/> (the parity tests read them).</summary>
        public bool KeepResolved;

        /// <summary>The share of the AI companies that sell below cost, re-rolled every month: the dumping complaints need such a company.</summary>
        public double DumpingShare = 0.25;

        public List<SimCompany> Companies = DefaultCompanies();

        /// <summary>The Condition triggers (the two cycles) do not run: a scenario of one or two hand-fired shocks wants a quiet channel.</summary>
        public bool DisableCycles;

        /// <summary>A daily chance that replaces the one of config.json for a category (what-if runs of the balance report); empty for the shipped numbers.</summary>
        public readonly Dictionary<string, double> DailyChance = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>[first day, last day] of a running historical event (category "history"), counted from <see cref="Start"/>.</summary>
        public readonly List<int[]> HistoryWindows = new List<int[]>();

        /// <summary>No lottery and no cycle: only what a test fires by hand happens.</summary>
        public SimSetup Quiet()
        {
            DisableCycles = true;
            DumpingShare = -1;   // the companies keep the flags a test gave them
            foreach (string category in new[] { "ms.supply", "ms.industry", "ms.price", "ms.demand", "ms.trade", "ms.cycle" }) DailyChance[category] = 0.0;
            return this;
        }

        /// <summary>The player holds 30 of 150 lines (AC-2.1) and the nine AI companies the other 120; the shares of the market are made up, the player's is 11 %.</summary>
        public static List<SimCompany> DefaultCompanies()
        {
            int[] lines = { 26, 22, 18, 15, 12, 10, 8, 5, 4 };
            double[] shares = { 0.20, 0.16, 0.13, 0.11, 0.09, 0.07, 0.06, 0.04, 0.03 };
            var list = new List<SimCompany>();
            list.Add(new SimCompany(CompanyKey.Player, 30, 0.11, 55000000));
            for (int i = 0; i < lines.Length; i++) list.Add(new SimCompany(CompanyKey.AiPrefix + (i + 1), lines[i], shares[i], shares[i] * 500000000));
            return list;
        }
    }

    internal sealed class SimFiring
    {
        public int Day;
        public string EventId;
        public string Category;
        public string Origin;       // Lottery, Condition, Manual, Scheduled
        public string[] Targets;
        public bool IsGroup;
    }

    internal sealed class SimNews
    {
        public int Day;
        public string EventId;
        public string Company;
        public int Rank;            // 1 = the leader among the AI companies by market share
    }

    internal sealed class SimChannelStat
    {
        public double Min = 1, Max = 1;             // after the limits
        public double RawMin = 1, RawMax = 1;       // before the limits
        public int Days;
        public int ClampedDays;
    }

    /// <summary>One effect as the model resolved it, for the parity tests.</summary>
    internal sealed class SimResolved
    {
        public string EventId;
        public string ChoiceId;     // null: an effect of the event itself
        public string Type;
        public string Company;
        public string Channel;
        public double Amount;       // Money
        public float Value;         // the multiplier / the peak / the amplitude after the intensity and the limits
        public int Duration;
        public bool Clamped;
        public bool Hit = true;
    }

    internal sealed class ShockSimulator
    {
        private const string Global = "global";
        private const int MaxPendingChoices = 2;
        private const int NoDay = int.MinValue;

        // ---- the content ----

        private sealed class Category
        {
            public string Id;
            public double DailyChance;
            public int MinGapDays;
            public int MaxActive;
        }

        internal sealed class Choice
        {
            public string Id;
            public double AiWeight = 1;
            public double AiMinCash;
            public List<JsonObject> Effects = new List<JsonObject>();
        }

        internal sealed class Def
        {
            public string Id;
            public string Category;
            public string Trigger;              // DailyChance, Condition, Manual
            public bool EveryDay;               // Condition: checkEvery Day
            public double Chance = 1;           // Condition
            public double Weight = 1;
            public int Cooldown;
            public string TargetType = "Player";
            public string Weighting = "Uniform";
            public ICondition Filter;
            public ICondition Conditions;
            public int Timer;
            public string DefaultChoice;
            public bool Notify;
            public readonly List<JsonObject> Effects = new List<JsonObject>();
            internal readonly List<Choice> Choices = new List<Choice>();

            public bool HasChoices
            {
                get { return Choices.Count > 0; }
            }

            public bool IsGroup
            {
                get { return TargetType == "AllCompanies"; }
            }
        }

        private sealed class Limits
        {
            public double Min = 0.1, Max = 10;
        }

        private sealed class Instance
        {
            public int Id;
            public int GroupId;
            public Def Def;
            public string Company;
            public int FiredDay;
            public bool Awaiting;
            public int Deadline;
            public int LastEnd = NoDay;
            public Random Rng;
        }

        private sealed class Contribution
        {
            public int InstanceId;
            public string Channel;
            public string Key;
            public int Start;
            public int End;
            public float Value;
            public bool Clamped;
            public RampCurve? Ramp;
            public WaveCurve? Wave;

            public float At(int day)
            {
                int age = day - Start;
                if (Ramp.HasValue) return Ramp.Value.ValueAt(age);
                if (Wave.HasValue) return Wave.Value.ValueAt(age);
                return Value;
            }
        }

        private sealed class Scheduled
        {
            public string EventId;
            public int Due;
            public string Company;
        }

        // ---- state ----

        private readonly SimSetup _setup;
        private readonly Dictionary<string, Category> _categories = new Dictionary<string, Category>(StringComparer.Ordinal);
        private readonly List<Category> _lotteryOrder = new List<Category>();
        private readonly Dictionary<string, Def> _defs = new Dictionary<string, Def>(StringComparer.Ordinal);
        private readonly List<Def> _all = new List<Def>();
        private readonly List<KeyValuePair<string, Limits>> _limits = new List<KeyValuePair<string, Limits>>();
        private readonly Dictionary<string, SimCompany> _companies = new Dictionary<string, SimCompany>(StringComparer.Ordinal);
        private readonly List<string> _keys = new List<string>();
        private readonly FakeGameState _state = new FakeGameState();
        private readonly ConditionParser _parser;

        private readonly Dictionary<string, int> _lastFired = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _categoryLast = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Scheduled> _scheduled = new List<Scheduled>();
        private readonly List<Instance> _instances = new List<Instance>();
        private readonly List<Contribution> _contribs = new List<Contribution>();
        private readonly WavePhaseBook _phases = new WavePhaseBook();
        private readonly Random _daily;
        private readonly Random _world;

        private int _today;
        private int _nextId;

        public readonly List<SimFiring> Firings = new List<SimFiring>();
        public readonly List<SimNews> News = new List<SimNews>();
        public readonly List<string> Journal = new List<string>();
        public readonly List<SimResolved> Resolved = new List<SimResolved>();
        public readonly Dictionary<string, SimChannelStat> Channels = new Dictionary<string, SimChannelStat>(StringComparer.Ordinal);

        /// <summary>Lotteries that passed the chance and the pace of their category and then found no event with a candidate target (the draws the cooldowns wasted).</summary>
        public int LotteriesWithoutCandidate;

        public int Today
        {
            get { return _today; }
        }

        public DateTime Date
        {
            get { return _setup.Start.AddDays(_today); }
        }

        public SimSetup Setup
        {
            get { return _setup; }
        }

        public IEnumerable<SimCompany> Companies
        {
            get { return _companies.Values; }
        }

        public SimCompany CompanyOf(string key)
        {
            return _companies[key];
        }

        public Def DefOf(string id)
        {
            return _defs[id];
        }

        public IList<Def> Defs
        {
            get { return _all; }
        }

        public ShockSimulator(SimSetup setup)
        {
            _setup = setup;
            _daily = new Random(setup.Seed);
            _world = new Random(setup.Seed * 7919 + 13);

            foreach (SimCompany company in setup.Companies)
            {
                _companies[company.Key] = company;
                _keys.Add(company.Key);
                FakeGameState.FakeCompany fake = _state.AddCompany(company.Key);
                fake.CpuOnMarket = true;
                fake.FactoryLines = company.Lines;
            }

            ConditionParser parser = null;
            ShockContentData.WithPrefixes(() => parser = BuildParser());
            _parser = parser;

            LoadConfig(ShockContentData.Config());
            foreach (JsonObject record in ShockContentData.Records()) AddDef(record);
            BuildLotteryOrder();
        }

        // ---- loading ----

        private ConditionParser BuildParser()
        {
            PredicateRegistry registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
            Register(registry, "es.hasRevenueHistory", "event-system", ctx => _today >= 92);
            Register(registry, "es.hasFab", "event-system", ctx => CompanyOf(ctx.SubjectKey).Lines > 0);
            Register(registry, "ms.sellsBelowCost", ShockContentData.Feature, ctx => CompanyOf(ctx.SubjectKey).SellsBelowCost);
            Register(registry, "ms.hasRecentCpu", ShockContentData.Feature, ctx => CompanyOf(ctx.SubjectKey).HasRecentCpu);
            Assert.True(registry.Register("es.activeInCategory", "event-system", (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                string category = args.RequireString("category");
                string scope = args.OptionalChoice("scope", "subject", "subject", "any");
                if (!args.Finish()) return null;
                return new DelegateCondition("es.activeInCategory", ctx => IsActiveInCategory(category, scope == "any" ? null : ctx.SubjectKey));
            }), "registry refused es.activeInCategory");
            return new ConditionParser(registry);
        }

        private static void Register(PredicateRegistry registry, string name, string owner, Func<ConditionContext, bool> test)
        {
            Assert.True(registry.Register(name, owner, (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                return args.Finish() ? new DelegateCondition(name, test) : null;
            }), "registry refused " + name);
        }

        private void LoadConfig(JsonObject config)
        {
            foreach (JsonNode node in config.GetArray("categories"))
            {
                var o = (JsonObject)node;
                var category = new Category
                {
                    Id = o.GetString("id", string.Empty),
                    DailyChance = o.GetDouble("dailyChance", 0),
                    MinGapDays = o.GetInt32("minGapDays", 0),
                    MaxActive = o.GetInt32("maxActive", int.MaxValue),
                };
                double overridden;
                if (_setup.DailyChance.TryGetValue(category.Id, out overridden)) category.DailyChance = overridden;
                _categories[category.Id] = category;
            }

            foreach (JsonNode node in config.GetArray("channelLimits"))
            {
                var o = (JsonObject)node;
                _limits.Add(new KeyValuePair<string, Limits>(o.GetString("channel", string.Empty), new Limits { Min = o.GetDouble("min", 0.1), Max = o.GetDouble("max", 10) }));
            }
        }

        private void AddDef(JsonObject record)
        {
            JsonObject trigger = record.GetObject("trigger");
            JsonObject targets = record.GetObject("targets");
            var def = new Def
            {
                Id = record.GetString("id", string.Empty),
                Category = record.GetString("category", string.Empty),
                Trigger = trigger.GetString("type", "Manual"),
                EveryDay = string.Equals(trigger.GetString("checkEvery", "Month"), "Day", StringComparison.OrdinalIgnoreCase),
                Chance = trigger.GetDouble("chance", 1.0),
                Weight = record.GetDouble("weight", 1),
                Cooldown = record.GetInt32("cooldownDays", 0),
                TargetType = targets != null ? targets.GetString("type", "Player") : "Player",
                Weighting = targets != null ? targets.GetString("weighting", "Uniform") : "Uniform",
                Timer = record.GetInt32("timerDays", 0),
                DefaultChoice = record.GetString("defaultChoice", null),
                Notify = record.GetBool("notifyPlayer", false),
            };

            JsonNode node;
            if (targets != null && targets.TryGetValue("filter", out node)) def.Filter = ShockContentData.Parse(_parser, node, "$." + def.Id + ".targets.filter");
            if (record.TryGetValue("conditions", out node)) def.Conditions = ShockContentData.Parse(_parser, node, "$." + def.Id + ".conditions");

            JsonArray effects = record.GetArray("effects");
            if (effects != null) foreach (JsonNode e in effects) def.Effects.Add((JsonObject)e);

            JsonArray choices = record.GetArray("choices");
            if (choices != null)
            {
                foreach (JsonNode c in choices)
                {
                    var o = (JsonObject)c;
                    var choice = new Choice { Id = o.GetString("id", string.Empty), AiWeight = o.GetDouble("aiWeight", 1), AiMinCash = o.GetDouble("aiMinCashMultiple", 0) };
                    JsonArray list = o.GetArray("effects");
                    if (list != null) foreach (JsonNode e in list) choice.Effects.Add((JsonObject)e);
                    def.Choices.Add(choice);
                }
            }

            _defs[def.Id] = def;
            _all.Add(def);
        }

        private void BuildLotteryOrder()
        {
            // The categories of the config in the order of the file that have at least one DailyChance event (EnsureIndex of the scheduler).
            foreach (KeyValuePair<string, Category> pair in _categories)
            {
                foreach (Def def in _all)
                {
                    if (def.Category == pair.Key && def.Trigger == "DailyChance")
                    {
                        _lotteryOrder.Add(pair.Value);
                        break;
                    }
                }
            }
        }

        // ---- the limits and the channels ----

        /// <summary>The narrowest limit of the pack for a channel: the exact name, else a family such as "market.demand.*", else the engine's own [0.1; 10].</summary>
        public void LimitsOf(string channel, out float min, out float max)
        {
            Limits found = null;
            foreach (KeyValuePair<string, Limits> pair in _limits)
            {
                if (pair.Key == channel) { found = pair.Value; break; }
            }

            if (found == null)
            {
                foreach (KeyValuePair<string, Limits> pair in _limits)
                {
                    if (pair.Key.EndsWith("*", StringComparison.Ordinal) && channel.StartsWith(pair.Key.Substring(0, pair.Key.Length - 1), StringComparison.Ordinal))
                    {
                        found = pair.Value;
                        break;
                    }
                }
            }

            found = found ?? new Limits();
            min = (float)found.Min;
            max = (float)found.Max;
        }

        /// <summary>The aggregate of a channel for a company now: the product of the live contributions of the company and the global ones, before and after the limits.</summary>
        public float ChannelValue(string channel, string companyKey, out float raw, out bool clamped)
        {
            double product = 1.0;
            foreach (Contribution c in _contribs)
            {
                if (c.Channel != channel || _today < c.Start || _today >= c.End) continue;
                if (c.Key == Global || c.Key == companyKey) product *= c.At(_today);
            }

            raw = (float)product;
            float min, max;
            LimitsOf(channel, out min, out max);
            float value = Math.Min(max, Math.Max(min, raw));
            clamped = value != raw;
            return value;
        }

        public float ChannelValue(string channel, string companyKey)
        {
            float raw;
            bool clamped;
            return ChannelValue(channel, companyKey, out raw, out clamped);
        }

        // ---- the days ----

        public void RunDays(int days)
        {
            for (int i = 0; i < days; i++) ProcessDay();
        }

        public void RunYears(int years)
        {
            DateTime end = _setup.Start.AddDays(_today).AddYears(years);
            RunDays((int)(end - _setup.Start.AddDays(_today)).TotalDays);
        }

        /// <summary>Plays one day and moves on to the next.</summary>
        public void ProcessDay()
        {
            Expire();
            RunScheduled();
            RunDeadlines();
            if (_setup.DumpingShare >= 0 && Date.Day == 1) RerollWorld();
            RunConditionTriggers();
            RunLotteries();
            if (_setup.TrackChannels) Sample();
            _today++;
        }

        private void RerollWorld()
        {
            foreach (SimCompany company in _companies.Values)
            {
                company.SellsBelowCost = _world.NextDouble() < _setup.DumpingShare;
                company.HasRecentCpu = _world.NextDouble() < 0.8;
            }
        }

        private void Expire()
        {
            for (int i = _contribs.Count - 1; i >= 0; i--)
            {
                if (_contribs[i].End <= _today) _contribs.RemoveAt(i);
            }

            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                Instance inst = _instances[i];
                if (!inst.Awaiting && inst.LastEnd <= _today) _instances.RemoveAt(i);
            }
        }

        private void RunScheduled()
        {
            if (_scheduled.Count == 0) return;
            var due = new List<Scheduled>();
            foreach (Scheduled s in _scheduled) if (s.Due <= _today) due.Add(s);
            due.Sort((a, b) => a.Due.CompareTo(b.Due));
            foreach (Scheduled s in due)
            {
                _scheduled.Remove(s);
                Def def = _defs[s.EventId];
                FireNow(def, s.Company, false, "Scheduled");
            }
        }

        private void RunDeadlines()
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                Instance inst = _instances[i];
                if (!inst.Awaiting || _today < inst.Deadline) continue;
                inst.Awaiting = false;
                Choice choice = PlayerChoice(inst.Def);
                if (choice != null) foreach (JsonObject effect in choice.Effects) Apply(inst, effect, choice.Id);
            }
        }

        private Choice PlayerChoice(Def def)
        {
            if (def.Choices.Count == 0) return null;
            switch (_setup.Player)
            {
                case PlayerPolicy.First: return def.Choices[0];
                case PlayerPolicy.Last: return def.Choices[def.Choices.Count - 1];
            }

            foreach (Choice c in def.Choices) if (c.Id == def.DefaultChoice) return c;
            return def.Choices[0];
        }

        // ---- the triggers ----

        private void RunConditionTriggers()
        {
            if (_setup.DisableCycles) return;

            bool firstOfMonth = Date.Day == 1;
            foreach (Def def in _all)
            {
                if (def.Trigger != "Condition" || (!def.EveryDay && !firstOfMonth)) continue;

                var keys = new List<string>();
                var weights = new List<double>();
                Candidates(def, null, false, false, keys, weights);
                if (keys.Count == 0) continue;

                Random rng = Trigger(def);
                double roll = rng.NextDouble();
                double u = rng.NextDouble();
                if (!(roll < def.Chance)) continue;

                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    if (CooldownAllows(def, keys[i])) continue;
                    keys.RemoveAt(i);
                    weights.RemoveAt(i);
                }
                if (keys.Count == 0) continue;

                List<string> chosen = Pick(def, keys, weights, u, null);
                if (chosen == null || !PendingAllows(def, chosen)) continue;   // (the engine queues the firing for up to 30 days; the model drops it)
                Create(def, chosen, "Condition");
            }
        }

        private void RunLotteries()
        {
            float settings = 1f;
            foreach (Category category in _lotteryOrder)
            {
                double u1 = _daily.NextDouble(), u2 = _daily.NextDouble(), u3 = _daily.NextDouble();
                double chance = category.DailyChance * settings * _setup.PackFrequency;
                if (!(u1 < chance)) continue;
                if (!CategoryAllows(category)) continue;

                var events = new List<Def>();
                var eventWeights = new List<double>();
                var targetKeys = new List<List<string>>();
                var targetWeights = new List<List<double>>();
                foreach (Def def in _all)
                {
                    if (def.Category != category.Id || def.Trigger != "DailyChance") continue;
                    var keys = new List<string>();
                    var weights = new List<double>();
                    Candidates(def, null, false, true, keys, weights);
                    if (keys.Count == 0) continue;
                    events.Add(def);
                    eventWeights.Add(def.Weight);
                    targetKeys.Add(keys);
                    targetWeights.Add(weights);
                }

                int index = WeightedIndex(eventWeights, u2);
                if (index < 0)
                {
                    LotteriesWithoutCandidate++;
                    continue;
                }

                List<string> chosen = Pick(events[index], targetKeys[index], targetWeights[index], u3, null);
                if (chosen == null || !PendingAllows(events[index], chosen)) continue;
                Create(events[index], chosen, "Lottery");
            }
        }

        // ---- firing ----

        /// <summary>Fires an event now (<c>EventBus.Fire</c>): the target as the event's own <c>targets</c> say, or the named company.</summary>
        public bool Fire(string eventId, string companyKey = null, bool ignoreConditions = false)
        {
            return FireNow(_defs[eventId], companyKey, ignoreConditions, "Manual");
        }

        private bool FireNow(Def def, string named, bool ignoreConditions, string origin)
        {
            var keys = new List<string>();
            var weights = new List<double>();
            Candidates(def, named, ignoreConditions, !ignoreConditions, keys, weights);
            if (keys.Count == 0) return false;

            List<string> chosen = Pick(def, keys, weights, Trigger(def).NextDouble(), named);
            if (chosen == null) return false;
            if (!PendingAllows(def, chosen)) return false;
            Create(def, chosen, origin);
            return true;
        }

        private Random Trigger(Def def)
        {
            return new Random(Hash(_setup.Seed, def.Id, _today));
        }

        private List<string> Pick(Def def, List<string> keys, List<double> weights, double u, string named)
        {
            if (keys.Count == 0) return null;
            if (def.IsGroup && string.IsNullOrEmpty(named)) return new List<string>(keys);
            int index = WeightedIndex(weights, u);
            return index < 0 ? null : new List<string> { keys[index] };
        }

        private bool PendingAllows(Def def, List<string> targets)
        {
            if (!def.HasChoices || !targets.Contains(CompanyKey.Player)) return true;
            int pending = 0;
            foreach (Instance inst in _instances) if (inst.Awaiting && inst.Company == CompanyKey.Player) pending++;
            return pending < MaxPendingChoices;
        }

        private void Create(Def def, List<string> targets, string origin)
        {
            int group = def.IsGroup && targets.Count > 0 ? ++_nextId : 0;
            var made = new List<Instance>();
            for (int i = 0; i < targets.Count; i++)
            {
                var inst = new Instance
                {
                    Id = ++_nextId,
                    GroupId = group,
                    Def = def,
                    Company = targets[i],
                    FiredDay = _today,
                };
                inst.Rng = new Random(Hash(_setup.Seed, "inst", inst.Id));
                made.Add(inst);
                _instances.Add(inst);

                foreach (JsonObject effect in def.Effects)
                {
                    // Expand(): the effects of scope Global belong to the first instance only.
                    if (IsGlobal(effect) && i > 0) continue;
                    Apply(inst, effect, null);
                }

                if (def.HasChoices) Decide(inst);
                Journal.Add(def.Id + "@" + inst.Company);
                MaybeNews(def, inst);
            }

            foreach (string key in targets) _lastFired[def.Id + "|" + (def.IsGroup ? "*" : key)] = _today;
            if (!string.IsNullOrEmpty(def.Category)) _categoryLast[def.Category] = _today;
            Firings.Add(new SimFiring { Day = _today, EventId = def.Id, Category = def.Category, Origin = origin, Targets = targets.ToArray(), IsGroup = def.IsGroup });
        }

        private void Decide(Instance inst)
        {
            Def def = inst.Def;
            if (inst.Company == CompanyKey.Player)
            {
                inst.Awaiting = true;
                inst.Deadline = _today + def.Timer;
                return;
            }

            // The AI: an option with aiMinCashMultiple only with money to spare, then by aiWeight.
            var options = new List<Choice>();
            var weights = new List<double>();
            foreach (Choice c in def.Choices)
            {
                if (c.AiMinCash > 0 && !_setup.AiHasCash) continue;
                options.Add(c);
                weights.Add(c.AiWeight);
            }

            if (options.Count == 0) return;

            // The decision of the AI has a stream of its own, so the numbers of the effects (the draws of the instance) are the player's whatever the AI picks (D5).
            double draw = new Random(Hash(_setup.Seed, "choice", inst.Id)).NextDouble();
            Choice picked = options[Math.Max(0, WeightedIndex(weights, draw))];
            foreach (JsonObject effect in picked.Effects) Apply(inst, effect, picked.Id);
        }

        private void MaybeNews(Def def, Instance inst)
        {
            if (!def.Notify || inst.Company == CompanyKey.Player) return;
            int rank = 1;
            double share = _companies[inst.Company].Share;
            foreach (SimCompany company in _companies.Values)
            {
                if (!company.IsPlayer && company.Share > share) rank++;
            }

            if (rank <= ShockRegistration.RivalNewsTopN) News.Add(new SimNews { Day = _today, EventId = def.Id, Company = inst.Company, Rank = rank });
        }

        // ---- the rules of the pace ----

        private bool CategoryAllows(Category category)
        {
            int last;
            if (_categoryLast.TryGetValue(category.Id, out last) && _today - last < category.MinGapDays) return false;
            if (category.MaxActive == int.MaxValue) return true;
            return ActiveInCategory(category.Id) < category.MaxActive;
        }

        private bool CooldownAllows(Def def, string companyKey)
        {
            if (def.Cooldown <= 0) return true;
            int last;
            return !_lastFired.TryGetValue(def.Id + "|" + (def.IsGroup ? "*" : companyKey), out last) || _today - last >= def.Cooldown;
        }

        private bool Active(Instance inst)
        {
            return inst.Awaiting || inst.LastEnd > _today;
        }

        /// <summary>Firings of the category that are going on; the instances of one group count once.</summary>
        public int ActiveInCategory(string category)
        {
            var groups = new HashSet<int>();
            foreach (Instance inst in _instances)
            {
                if (inst.Def.Category == category && Active(inst)) groups.Add(inst.GroupId != 0 ? inst.GroupId : inst.Id);
            }
            return groups.Count;
        }

        /// <summary><c>es.activeInCategory</c>: an event of the category is going on for the company; null company: for any company or with a global effect.</summary>
        public bool IsActiveInCategory(string category, string companyKey)
        {
            if (category == "history")
            {
                foreach (int[] window in _setup.HistoryWindows)
                {
                    if (_today >= window[0] && _today <= window[1]) return true;
                }
            }

            foreach (Instance inst in _instances)
            {
                if (inst.Def.Category != category || !Active(inst)) continue;
                if (companyKey == null || inst.Company == companyKey) return true;
            }
            return false;
        }

        // ---- the targets ----

        private void Candidates(Def def, string named, bool ignoreConditions, bool applyCooldown, List<string> keys, List<double> weights)
        {
            keys.Clear();
            weights.Clear();
            bool isNamed = !string.IsNullOrEmpty(named);
            bool weighed = !isNamed && (def.TargetType == "RandomAI" || def.TargetType == "RandomCompany" || def.TargetType == "AllCompanies");
            bool checkFilter = def.Filter != null && !(isNamed && ignoreConditions);
            bool checkConditions = def.Conditions != null && !ignoreConditions;

            foreach (string key in BaseCandidates(def, named))
            {
                if (checkFilter && !Check(def.Filter, key)) continue;
                if (checkConditions && !Check(def.Conditions, key)) continue;
                if (applyCooldown && !CooldownAllows(def, key)) continue;

                double weight = weighed ? WeightOf(key, def.Weighting) : 1.0;
                if (!(weight > 0)) continue;
                keys.Add(key);
                weights.Add(weight);
            }
        }

        private IEnumerable<string> BaseCandidates(Def def, string named)
        {
            if (!string.IsNullOrEmpty(named))
            {
                if (_companies.ContainsKey(named)) yield return named;
                yield break;
            }

            switch (def.TargetType)
            {
                case "Player":
                    yield return CompanyKey.Player;
                    break;
                case "RandomAI":
                    foreach (string key in _keys) if (key != CompanyKey.Player) yield return key;
                    break;
                default:
                    foreach (string key in _keys) yield return key;
                    break;
            }
        }

        private bool Check(ICondition condition, string subject)
        {
            return condition.Evaluate(new ConditionContext(_state, Date, subject));
        }

        private double WeightOf(string key, string weighting)
        {
            SimCompany company = _companies[key];
            switch (weighting)
            {
                case "FactoryCapacity": return company.Lines > 0 ? company.Lines : 0;
                case "MarketShare": return company.Share + 0.01;
                case "QuarterlyRevenue": return Math.Max(company.QuarterlyRevenue, 0) + 1;
                default: return 1;
            }
        }

        /// <summary>The index whose share of the total weight contains u: the first with u x total &lt; running sum (WeightedPick of the scheduler).</summary>
        private static int WeightedIndex(IList<double> weights, double u)
        {
            double total = 0;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!(weights[i] > 0)) continue;
                total += weights[i];
                last = i;
            }
            if (last < 0) return -1;

            double point = (u > 0 ? (u >= 1 ? 0.99999999 : u) : 0) * total;
            double sum = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!(weights[i] > 0)) continue;
                sum += weights[i];
                if (point < sum) return i;
            }
            return last;
        }

        // ---- the effects ----

        private static bool IsGlobal(JsonObject effect)
        {
            JsonObject p = effect.GetObject("params");
            return p != null && string.Equals(p.GetString("scope", "Target"), "Global", StringComparison.OrdinalIgnoreCase);
        }

        private void Apply(Instance inst, JsonObject effect, string choiceId)
        {
            string type = effect.GetString("type", string.Empty);
            JsonObject p = effect.GetObject("params") ?? new JsonObject();
            bool packType = type == "ModifierRamp" || type == "ModifierWave" || type == "InventoryLoss";
            float severity = p.GetBool("scalable", packType) ? _setup.Severity : 1f;
            double chance = p.GetDouble("chance", 1.0);
            double u = inst.Rng.NextDouble();
            int duration = effect.GetInt32("durationDays", 0);
            string key = IsGlobal(effect) ? Global : inst.Company;

            var record = new SimResolved { EventId = inst.Def.Id, ChoiceId = choiceId, Type = type, Company = inst.Company, Duration = duration, Hit = u < chance };
            if (!record.Hit)
            {
                if (_setup.KeepResolved) Resolved.Add(record);
                return;
            }

            switch (type)
            {
                case "Money":
                    ApplyMoney(inst, p, severity, record);
                    break;

                case "FactoryOutput":
                    record.Channel = "production.output";
                    AddMultiplier(inst, record, key, (float)p.GetDouble("multiplier", 1.0), severity, duration);
                    break;

                case "ModifierTimed":
                    record.Channel = p.GetString("channel", string.Empty);
                    AddMultiplier(inst, record, key, (float)p.GetDouble("multiplier", 1.0), severity, duration);
                    break;

                case "ModifierRamp":
                    ApplyRamp(inst, p, key, severity, record);
                    break;

                case "ModifierWave":
                    ApplyWave(inst, p, key, severity, record);
                    break;

                case "FireEvent":
                    ApplyFireEvent(inst, p);
                    break;
            }

            if (_setup.KeepResolved) Resolved.Add(record);
        }

        private void ApplyMoney(Instance inst, JsonObject p, float severity, SimResolved record)
        {
            double lo, hi;
            ReadRange(p["revenueFraction"], out lo, out hi);
            double f = lo + (hi - lo) * inst.Rng.NextDouble();
            double qr = _companies[inst.Company].QuarterlyRevenue;
            double inflation = _setup.Inflation;
            double amount = Math.Min(Math.Max(f * qr, p.GetDouble("min") * inflation), p.GetDouble("max") * inflation) * severity;
            amount = Math.Round(amount / 1000.0) * 1000.0;
            record.Amount = amount;
            if (p.GetString("direction", "Spend") == "Spend") _companies[inst.Company].MoneySpent += amount;
        }

        private void AddMultiplier(Instance inst, SimResolved record, string key, float multiplier, float severity, int duration)
        {
            float min, max;
            LimitsOf(record.Channel, out min, out max);
            float unclamped = 1f + (multiplier - 1f) * severity;
            float value = Math.Min(max, Math.Max(min, unclamped));
            record.Value = value;
            record.Clamped = value != unclamped;
            if (duration <= 0) return;

            Add(inst, new Contribution { Channel = record.Channel, Key = key, Start = _today, End = _today + duration, Value = value, Clamped = record.Clamped });
        }

        private void ApplyRamp(Instance inst, JsonObject p, string key, float severity, SimResolved record)
        {
            RampSpec spec = RampOf(p);
            float min, max;
            LimitsOf(spec.Channel, out min, out max);
            RampCurve curve = spec.Resolve(_setup.Severity, min, max);
            record.Channel = spec.Channel;
            record.Value = curve.Peak;
            record.Clamped = curve.Clamped;
            record.Duration = curve.Total;
            Add(inst, new Contribution { Channel = spec.Channel, Key = key, Start = _today, End = _today + curve.Total, Ramp = curve, Clamped = curve.Clamped });
        }

        private void ApplyWave(Instance inst, JsonObject p, string key, float severity, SimResolved record)
        {
            WaveSpec spec = WaveOf(p);
            float min, max;
            LimitsOf(spec.Channel, out min, out max);
            float phase = _phases.PhaseOf(spec, (float)inst.Rng.NextDouble(), (long)inst.Id * 100000L + _today);
            WaveCurve curve = spec.Resolve(_setup.Severity, min, max, phase);
            record.Channel = spec.Channel;
            record.Value = curve.Amplitude;
            record.Clamped = curve.Clamped;
            record.Duration = spec.TotalDays;
            Add(inst, new Contribution { Channel = spec.Channel, Key = key, Start = _today, End = _today + spec.TotalDays, Wave = curve, Clamped = curve.Clamped });
        }

        private void ApplyFireEvent(Instance inst, JsonObject p)
        {
            double lo, hi;
            ReadRange(p["delayDays"], out lo, out hi);
            int delay = (int)Math.Round(lo + (hi - lo) * inst.Rng.NextDouble());
            string target = p.GetString("target", "Same");
            string company = target == "Player" ? CompanyKey.Player : inst.Company;
            _scheduled.Add(new Scheduled { EventId = p.GetString("eventId", string.Empty), Due = _today + Math.Max(0, delay), Company = company });
        }

        private void Add(Instance inst, Contribution contribution)
        {
            contribution.InstanceId = inst.Id;
            _contribs.Add(contribution);
            inst.LastEnd = Math.Max(inst.LastEnd, contribution.End);
        }

        private readonly Dictionary<JsonObject, RampSpec> _ramps = new Dictionary<JsonObject, RampSpec>();
        private readonly Dictionary<JsonObject, WaveSpec> _waves = new Dictionary<JsonObject, WaveSpec>();

        private RampSpec RampOf(JsonObject p)
        {
            RampSpec spec;
            if (_ramps.TryGetValue(p, out spec)) return spec;
            var errors = new List<string>();
            Assert.True(RampSpec.TryRead(p, ShockTestData.AnyChannel, errors, out spec), "ramp spec rejected: " + string.Join("; ", errors.ToArray()));
            _ramps[p] = spec;
            return spec;
        }

        private WaveSpec WaveOf(JsonObject p)
        {
            WaveSpec spec;
            if (_waves.TryGetValue(p, out spec)) return spec;
            var errors = new List<string>();
            Assert.True(WaveSpec.TryRead(p, ShockTestData.AnyChannel, errors, out spec), "wave spec rejected: " + string.Join("; ", errors.ToArray()));
            _waves[p] = spec;
            return spec;
        }

        private static void ReadRange(JsonNode node, out double lo, out double hi)
        {
            var array = node as JsonArray;
            if (array != null && array.Count >= 2)
            {
                lo = ((JsonValue)array[0]).NumberValue;
                hi = ((JsonValue)array[1]).NumberValue;
                return;
            }

            var value = node as JsonValue;
            lo = hi = value != null ? value.NumberValue : 0;
        }

        private static int Hash(int seed, string text, int number)
        {
            unchecked
            {
                int h = seed * 16777619 + number;
                foreach (char ch in text) h = h * 31 + ch;
                return h;
            }
        }

        // ---- sampling ----

        private readonly List<string> _sampleChannels = new List<string>();
        private readonly List<string> _sampleKeys = new List<string>();
        private readonly List<double> _sampleProducts = new List<double>();
        private readonly Dictionary<string, float[]> _limitCache = new Dictionary<string, float[]>(StringComparer.Ordinal);

        private void Sample()
        {
            if (_contribs.Count == 0) return;

            _sampleChannels.Clear();
            foreach (Contribution c in _contribs)
            {
                if (_today < c.Start || _today >= c.End) continue;
                if (!_sampleChannels.Contains(c.Channel)) _sampleChannels.Add(c.Channel);
            }

            foreach (string channel in _sampleChannels)
            {
                double global = 1.0;
                _sampleKeys.Clear();
                _sampleProducts.Clear();
                foreach (Contribution c in _contribs)
                {
                    if (c.Channel != channel || _today < c.Start || _today >= c.End) continue;
                    double value = c.At(_today);
                    if (c.Key == Global)
                    {
                        global *= value;
                        continue;
                    }

                    int index = _sampleKeys.IndexOf(c.Key);
                    if (index < 0)
                    {
                        _sampleKeys.Add(c.Key);
                        _sampleProducts.Add(value);
                    }
                    else
                    {
                        _sampleProducts[index] *= value;
                    }
                }

                if (_sampleKeys.Count == 0) Record(channel, global);
                else for (int i = 0; i < _sampleKeys.Count; i++) Record(channel, global * _sampleProducts[i]);
            }
        }

        private void Record(string channel, double raw)
        {
            float[] limits;
            if (!_limitCache.TryGetValue(channel, out limits))
            {
                float min, max;
                LimitsOf(channel, out min, out max);
                _limitCache[channel] = limits = new[] { min, max };
            }

            double value = Math.Min(limits[1], Math.Max(limits[0], raw));

            SimChannelStat stat;
            if (!Channels.TryGetValue(channel, out stat)) Channels[channel] = stat = new SimChannelStat();
            stat.Days++;
            if (value != raw) stat.ClampedDays++;
            stat.Min = Math.Min(stat.Min, value);
            stat.Max = Math.Max(stat.Max, value);
            stat.RawMin = Math.Min(stat.RawMin, raw);
            stat.RawMax = Math.Max(stat.RawMax, raw);
        }

        // ---- reading the result ----

        public int CountFirings(string category, string origin = null)
        {
            int count = 0;
            foreach (SimFiring firing in Firings)
            {
                if (firing.Category == category && (origin == null || firing.Origin == origin)) count++;
            }
            return count;
        }

        public int CountFirings(string category, int fromDay, int toDayExclusive, string origin)
        {
            int count = 0;
            foreach (SimFiring firing in Firings)
            {
                if (firing.Category == category && firing.Day >= fromDay && firing.Day < toDayExclusive && (origin == null || firing.Origin == origin)) count++;
            }
            return count;
        }

        /// <summary>The <c>conditions</c> of the event are true for the company on the current day (no conditions: true).</summary>
        public bool ConditionsHold(string eventId, string companyKey)
        {
            Def def = _defs[eventId];
            return def.Conditions == null || Check(def.Conditions, companyKey);
        }

        public int DayOf(DateTime date)
        {
            return (int)(date - _setup.Start).TotalDays;
        }
    }
}
