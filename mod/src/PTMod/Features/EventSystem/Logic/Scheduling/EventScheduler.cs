using System;
using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>The loaded content as the scheduler reads it (<c>PackCatalog</c> in the game).</summary>
    public interface ISchedulerCatalog
    {
        /// <summary>The definition with this id, or null.</summary>
        EventDefinition Get(string eventId);

        /// <summary>Every loaded definition, in the fixed order of the catalog (packs by id, then the file).</summary>
        IList<EventDefinition> All { get; }

        /// <summary>The definitions of a category, in the same order.</summary>
        IReadOnlyList<EventDefinition> InCategory(string category);

        /// <summary>The merged <c>config.json</c> of the packs: categories (in pack order), <c>maxPendingChoices</c>.</summary>
        EventConfig Config { get; }

        /// <summary>The options a pack was registered with (read at every use: <c>Frequency</c> may change in place), or null.</summary>
        PackOptions OptionsOf(string packId);
    }

    /// <summary>
    /// The parts of the day that belong to the effects and the choices, and the creation of instances: the scheduler only decides when and for whom an
    /// event fires and calls these in the order of FR-2 item 1. The instance factory and the choice controller implement it.
    /// </summary>
    public interface ISchedulerRuntime
    {
        /// <summary>Step 1: effects whose <c>endDay</c> has come are removed.</summary>
        void ExpireEffects(int today);

        /// <summary>Step 2: the effects that change every day (curves of packs) are updated.</summary>
        void TickEffects(int today);

        /// <summary>Step 4: choices whose deadline has come are closed with their default choice.</summary>
        void RunDeadlines(int today);

        /// <summary>Creates the instances of a firing (resolves their effects, applies the common ones, sends the mail); returns how many were created, 0 - none.</summary>
        int Fire(FiringPlan plan);
    }

    /// <summary>The knobs of the scheduler that come from the settings and the switches of the game.</summary>
    public sealed class SchedulerOptions
    {
        /// <summary>The parameter <c>frequency</c> (0..3), read every day.</summary>
        public Func<int> FrequencySetting = () => Pacing.DefaultFrequencySetting;

        /// <summary>The pack is not frozen (its switch feature is Enabled); a frozen pack fires nothing and its delayed firings wait.</summary>
        public Func<string, bool> IsPackActive = packId => true;

        /// <summary>A failure of a step or of an event: the site and the exception (<c>ModHooks.ReportFault</c> in the game).</summary>
        public Action<string, Exception> OnFault = (site, error) => { };

        /// <summary>A line for the log at information level.</summary>
        public Action<string> OnInfo = line => { };

        /// <summary>The debug timer of <see cref="EventScheduler.ProcessDay"/> (TSK-027, NFR-1); null - the day is not timed.</summary>
        public DayTimer Timer;
    }

    /// <summary>
    /// The day of the event system (FR-2, FR-9; design.md "Планировщик"). <see cref="ProcessDay"/> runs once per game day (<c>TickPhase.DayPassed</c>) in the
    /// order of FR-2 item 1: expiry of effects, curves, delayed firings, deadlines of choices, <c>Date</c> triggers (after the firings that waited for a free
    /// choice slot), <c>Condition</c> triggers, the category lotteries.
    /// <list type="bullet">
    /// <item><b>Lottery</b> (D6): every category that has <c>DailyChance</c> events takes three numbers of the daily stream every day, whatever happens
    /// (FR-9 item 2). <c>u1 &lt; dailyChance × F_settings × F_pack</c> and the category's pace (<c>minGapDays</c>, <c>maxActive</c>) → one of the fitting
    /// events (conditions, cooldown, <c>oncePerGame</c>, a target) by <c>weight</c> with <c>u2</c>, its target with <c>u3</c>. An event with choices for the
    /// player while the player has <c>maxPendingChoices</c> open choices does not fire, the day is skipped (AC-2.3).</item>
    /// <item><b>Date</b>: on the date or on the first processed day of the next <c>graceDays</c>, once (E9). <b>Condition</b>: every day or on the 1st
    /// (<c>checkEvery</c>), by level or only on the change false → true (<c>edge</c>), with <c>chance</c>. Their rolls come from the generator
    /// <c>hash32(WorldSeed, "trg", eventId, day)</c>; a firing for the player that would open one choice too many waits in a queue for up to 30 days.</item>
    /// <item><b>Manual</b>: only <see cref="Fire"/> (<c>EventBus.Fire</c>, a <c>FireEvent</c> effect).</item>
    /// <item><b>Faults</b>: every step and every event runs in its own try/catch; an event that fails three times is off until the game restarts (NFR-2).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class EventScheduler
    {
        /// <summary>Failures after which an event is off for the session (NFR-2).</summary>
        public const int FaultLimit = 3;

        private const string DayCheckEvery = "Day";

        private readonly ISchedulerCatalog _catalog;
        private readonly IEventWorld _world;
        private readonly ISchedulerRuntime _runtime;
        private readonly EventRandom _random;
        private readonly SchedulerState _state;
        private readonly EventHistory _history;
        private readonly SchedulerOptions _options;
        private readonly Pacing _pacing;
        private readonly TargetSelector _selector;

        private readonly Dictionary<string, int> _faults = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _disabled = new HashSet<string>(StringComparer.Ordinal);

        private readonly List<EventDefinition> _dateEvents = new List<EventDefinition>();
        private readonly List<EventDefinition> _conditionEvents = new List<EventDefinition>();
        private readonly List<CategoryDefinition> _lotteryCategories = new List<CategoryDefinition>();
        private IList<EventDefinition> _indexedAll;
        private int _indexedCount = -1;
        private EventConfig _indexedConfig;
        private int _firingsToday;

        public EventScheduler(ISchedulerCatalog catalog, IEventWorld world, ISchedulerRuntime runtime, EventRandom random,
            SchedulerState state, EventHistory history, SchedulerOptions options)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (world == null) throw new ArgumentNullException("world");
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (random == null) throw new ArgumentNullException("random");
            if (state == null) throw new ArgumentNullException("state");
            if (history == null) throw new ArgumentNullException("history");
            _catalog = catalog;
            _world = world;
            _runtime = runtime;
            _random = random;
            _state = state;
            _history = history;
            _options = options ?? new SchedulerOptions();
            _pacing = new Pacing(state, history, () => _catalog.Config);
            _selector = new TargetSelector(world);
        }

        public SchedulerState State
        {
            get { return _state; }
        }

        public EventHistory History
        {
            get { return _history; }
        }

        public Pacing Pacing
        {
            get { return _pacing; }
        }

        public TargetSelector Selector
        {
            get { return _selector; }
        }

        /// <summary>Called with every category lottery: the day, the category and its three numbers (debugging and tests).</summary>
        public Action<int, string, LotteryDraw> LotteryObserver;

        /// <summary>The timer of the day, or null (<see cref="SchedulerOptions.Timer"/>).</summary>
        public DayTimer Timer
        {
            get { return _options.Timer; }
        }

        /// <summary>How many firings (calls of the runtime that created instances) the last <see cref="ProcessDay"/> made.</summary>
        public int FiringsLastDay
        {
            get { return _firingsToday; }
        }

        /// <summary>The event failed <see cref="FaultLimit"/> times and is off until the game restarts.</summary>
        public bool IsDisabled(string eventId)
        {
            return eventId != null && _disabled.Contains(eventId);
        }

        /// <summary>A new game is ready on <paramref name="today"/> (the state was reset by the save participant): that day counts as processed.</summary>
        public void StartNewGame(int today)
        {
            _state.LastProcessedDay = today;
        }

        // ---- the day ----

        /// <summary>Processes one game day (FR-2 item 1); the same day twice in a row is ignored.</summary>
        public void ProcessDay(int today)
        {
            if (_state.LastProcessedDay != SchedulerState.NoDay && today == _state.LastProcessedDay) return;
            _state.LastProcessedDay = today;
            DayTimer timer = _options.Timer;
            long started = timer != null ? timer.Begin() : DayTimer.Off;
            _firingsToday = 0;
            EnsureIndex();

            Step("effects/expire", () => _runtime.ExpireEffects(today));
            Step("effects/tick", () => _runtime.TickEffects(today));
            Step("scheduled", () => RunScheduledFires(today));
            Step("deadlines", () => _runtime.RunDeadlines(today));
            Step("deferred", () => RunDeferred(today));
            Step("date", () => RunDateTriggers(today));
            Step("condition", () => RunConditionTriggers(today));
            Step("lottery", () => RunCategoryLotteries(today));

            if (started != DayTimer.Off) timer.End(started, EventDays.ToDate(today).Year, _firingsToday);
        }

        /// <summary>
        /// Fires an event now or after <c>DelayDays</c> (<c>EventBus.Fire</c>, <c>FireEvent</c>, the debug list). Any trigger type may be fired this way.
        /// <c>UnknownEvent</c> - no such id; <c>Disabled</c> - its pack is frozen; <c>Scheduled</c> - a delay, or the player has too many open choices (the
        /// firing waits up to 30 days); <c>Skipped</c> - used up, failed, no fitting target (conditions, filter, cooldown unless <c>IgnoreConditions</c>);
        /// <c>Fired</c> - the instances exist.
        /// </summary>
        public EventFireResult Fire(string eventId, EventFireContext context)
        {
            EventDefinition definition = string.IsNullOrEmpty(eventId) ? null : _catalog.Get(eventId);
            if (definition == null) return EventFireResult.UnknownEvent;
            if (!IsPackActive(definition)) return EventFireResult.Disabled;

            context = context ?? new EventFireContext();
            int today = _world.Today;
            if (context.DelayDays > 0)
            {
                _state.Scheduled.Add(new ScheduledFire
                {
                    eventId = definition.id,
                    dueDay = today + context.DelayDays,
                    companyKey = context.CompanyKey,
                    parentId = Math.Max(0, context.ParentInstanceId),
                    args = CopyArgs(context.Args),
                    ignoreConditions = context.IgnoreConditions,
                });
                return EventFireResult.Scheduled;
            }

            return FireNow(definition, context, today, FireOrigin.Manual);
        }

        // ---- steps ----

        private void RunScheduledFires(int today)
        {
            if (_state.Scheduled.Count == 0) return;

            var due = new List<KeyValuePair<int, ScheduledFire>>();
            for (int i = 0; i < _state.Scheduled.Count; i++)
            {
                if (_state.Scheduled[i].dueDay <= today) due.Add(new KeyValuePair<int, ScheduledFire>(i, _state.Scheduled[i]));
            }
            if (due.Count == 0) return;
            due.Sort((a, b) => a.Value.dueDay != b.Value.dueDay ? a.Value.dueDay.CompareTo(b.Value.dueDay) : a.Key.CompareTo(b.Key));

            foreach (KeyValuePair<int, ScheduledFire> entry in due)
            {
                ScheduledFire fire = entry.Value;
                EventDefinition definition = _catalog.Get(fire.eventId);
                if (definition == null)
                {
                    _state.Scheduled.Remove(fire);
                    Info("scheduled firing of '" + fire.eventId + "' dropped: the event is not in the registry");
                    continue;
                }
                if (!IsPackActive(definition)) continue;   // waits until its pack is switched on again

                _state.Scheduled.Remove(fire);
                var context = new EventFireContext
                {
                    CompanyKey = fire.companyKey,
                    ParentInstanceId = fire.parentId,
                    IgnoreConditions = fire.ignoreConditions,
                    Args = fire.args,
                };
                FireNow(definition, context, today, FireOrigin.Scheduled, fire.depth);
            }
        }

        private void RunDeferred(int today)
        {
            if (_state.Deferred.Count == 0) return;

            foreach (DeferredFire fire in _state.Deferred.ToArray())
            {
                if (today - fire.queuedDay > SchedulerState.DeferLimitDays)
                {
                    _state.Deferred.Remove(fire);
                    Info("'" + fire.eventId + "' dropped: it waited " + SchedulerState.DeferLimitDays + " days for a free choice slot of the player");
                    continue;
                }

                EventDefinition definition = _catalog.Get(fire.eventId);
                if (definition == null)
                {
                    _state.Deferred.Remove(fire);
                    continue;
                }
                if (!IsPackActive(definition) || IsDisabled(definition.id)) continue;

                try
                {
                    var query = new TargetQuery { CompanyKey = fire.companyKey, IgnoreConditions = true };
                    FiringPlan plan = _selector.Select(definition, 0f, query);
                    if (plan == null)
                    {
                        _state.Deferred.Remove(fire);
                        Info("'" + fire.eventId + "' dropped from the queue: its target is gone");
                        continue;
                    }

                    Prepare(plan, today, FireOrigin.Deferred, fire.parentId, fire.args, fire.depth);
                    if (!_pacing.PendingAllows(plan)) continue;   // still no free slot

                    _state.Deferred.Remove(fire);
                    CreateInstances(plan);
                }
                catch (Exception ex)
                {
                    _state.Deferred.Remove(fire);
                    Fault(definition.id, "deferred", ex);
                }
            }
        }

        private void RunDateTriggers(int today)
        {
            foreach (EventDefinition definition in _dateEvents)
            {
                if (!CanRun(definition) || _state.IsDeferred(definition.id)) continue;

                EventTrigger trigger = definition.trigger;
                int dateDay;
                if (!EventDays.TryOf(trigger.year, trigger.month, trigger.day, out dateDay)) continue;
                if (today < dateDay || today > dateDay + Math.Max(0, trigger.graceDays)) continue;

                EventDefinition current = definition;
                Guard(current, "date", () =>
                {
                    var keys = new List<string>();
                    var weights = new List<float>();
                    _selector.Candidates(current, CooldownQuery(current, today), keys, weights);
                    float u = _random.ForTrigger(current.id, today).Value();
                    Trigger(current, today, FireOrigin.Date, u, keys, weights);
                });
            }
        }

        private void RunConditionTriggers(int today)
        {
            foreach (EventDefinition definition in _conditionEvents)
            {
                if (!CanRun(definition) || _state.IsDeferred(definition.id)) continue;
                if (!IsDue(definition.trigger, today)) continue;

                EventDefinition current = definition;
                Guard(current, "condition", () =>
                {
                    var keys = new List<string>();
                    var weights = new List<float>();
                    _selector.Candidates(current, null, keys, weights);
                    bool level = keys.Count > 0;

                    if (current.trigger.edge)
                    {
                        bool before = _state.EdgeLevel(current.id);
                        _state.SetEdgeLevel(current.id, level);
                        if (!level || before) return;   // fires only on false -> true
                    }
                    else if (!level)
                    {
                        return;
                    }

                    DerivedRng rng = _random.ForTrigger(current.id, today);
                    float roll = rng.Value();
                    float u = rng.Value();
                    if (!(roll < current.trigger.chance)) return;

                    KeepAllowed(current, today, keys, weights);
                    Trigger(current, today, FireOrigin.Condition, u, keys, weights);
                });
            }
        }

        private void RunCategoryLotteries(int today)
        {
            if (_lotteryCategories.Count == 0) return;
            float settings = Pacing.FrequencyFactor(_options.FrequencySetting());

            foreach (CategoryDefinition category in _lotteryCategories)
            {
                LotteryDraw draw = _random.DrawCategory();   // always the three numbers (FR-9 item 2)
                try
                {
                    Action<int, string, LotteryDraw> observer = LotteryObserver;
                    if (observer != null) observer(today, category.Id, draw);
                    RunLottery(category, draw, today, settings);
                }
                catch (Exception ex)
                {
                    _options.OnFault("lottery/" + category.Id, ex);
                }
            }
        }

        private void RunLottery(CategoryDefinition category, LotteryDraw draw, int today, float settings)
        {
            float chance = category.DailyChance * settings * PackFrequency(category.PackId);
            if (!(draw.U1 < chance)) return;
            if (!_pacing.CategoryAllows(category, today)) return;

            var events = new List<EventDefinition>();
            var eventWeights = new List<float>();
            var targetKeys = new List<List<string>>();
            var targetWeights = new List<List<float>>();

            foreach (EventDefinition definition in _catalog.InCategory(category.Id))
            {
                if (!IsDailyChance(definition) || !CanRun(definition)) continue;

                var keys = new List<string>();
                var weights = new List<float>();
                EventDefinition current = definition;
                if (!Guard(current, "lottery", () => _selector.Candidates(current, CooldownQuery(current, today), keys, weights))) continue;
                if (keys.Count == 0) continue;

                events.Add(current);
                eventWeights.Add(current.weight);
                targetKeys.Add(keys);
                targetWeights.Add(weights);
            }

            int index = WeightedPick.Index(eventWeights, draw.U2);
            if (index < 0) return;

            EventDefinition chosen = events[index];
            FiringPlan plan = _selector.Pick(chosen, targetKeys[index], targetWeights[index], draw.U3, null);
            if (plan == null) return;

            Prepare(plan, today, FireOrigin.Lottery, 0, null, 0);
            if (!_pacing.PendingAllows(plan))
            {
                Info("lottery of '" + category.Id + "': '" + chosen.id + "' waits for the player's open choices, the day is skipped");
                return;
            }
            CreateInstancesGuarded(plan);
        }

        // ---- firing ----

        /// <summary>A Date or Condition trigger has decided: pick the target, then fire or queue the firing behind the player's open choices.</summary>
        private void Trigger(EventDefinition definition, int today, FireOrigin origin, float u, List<string> keys, List<float> weights)
        {
            FiringPlan plan = _selector.Pick(definition, keys, weights, u, null);
            if (plan == null) return;   // no target today; a Date trigger tries again tomorrow while its grace lasts

            Prepare(plan, today, origin, 0, null, 0);
            if (!_pacing.PendingAllows(plan))
            {
                Defer(plan);
                return;
            }
            CreateInstances(plan);
        }

        private EventFireResult FireNow(EventDefinition definition, EventFireContext context, int today, FireOrigin origin, int depth = 0)
        {
            if (IsDisabled(definition.id) || !_pacing.OnceAllows(definition)) return EventFireResult.Skipped;

            try
            {
                var query = new TargetQuery { CompanyKey = context.CompanyKey, IgnoreConditions = context.IgnoreConditions };
                if (!context.IgnoreConditions) query.Allowed = CooldownQuery(definition, today).Allowed;

                float u = _random.ForTrigger(definition.id, today).Value();
                FiringPlan plan = _selector.Select(definition, u, query);
                if (plan == null) return EventFireResult.Skipped;

                Prepare(plan, today, origin, context.ParentInstanceId, context.Args, depth);
                if (!_pacing.PendingAllows(plan))
                {
                    Defer(plan);
                    return EventFireResult.Scheduled;
                }
                return CreateInstances(plan) ? EventFireResult.Fired : EventFireResult.Skipped;
            }
            catch (Exception ex)
            {
                Fault(definition.id, "fire", ex);
                return EventFireResult.Skipped;
            }
        }

        /// <summary>Lets the runtime create the instances and records the firing when it did.</summary>
        private bool CreateInstances(FiringPlan plan)
        {
            int created = _runtime.Fire(plan);
            if (created <= 0) return false;
            _firingsToday++;
            _pacing.RecordFiring(plan);
            return true;
        }

        private void CreateInstancesGuarded(FiringPlan plan)
        {
            try
            {
                CreateInstances(plan);
            }
            catch (Exception ex)
            {
                Fault(plan.Definition.id, "fire", ex);
            }
        }

        private void Defer(FiringPlan plan)
        {
            _state.Deferred.Add(new DeferredFire
            {
                eventId = plan.Definition.id,
                companyKey = plan.IsGroup ? null : plan.Targets[0],
                queuedDay = plan.Today,
                parentId = plan.ParentId,
                args = CopyArgs(plan.Args),
                depth = plan.ChainDepth,
            });
            if (Pacing.IsDateTrigger(plan.Definition)) _state.MarkUsedUp(plan.Definition.id);   // a date fires once, queued or not
            Info("'" + plan.Definition.id + "' waits for a free choice slot of the player (up to " + SchedulerState.DeferLimitDays + " days)");
        }

        private static void Prepare(FiringPlan plan, int today, FireOrigin origin, int parentId, string[] args, int depth)
        {
            plan.Today = today;
            plan.Origin = origin;
            plan.ParentId = Math.Max(0, parentId);
            plan.Args = CopyArgs(args);
            plan.ChainDepth = Math.Max(0, depth);
        }

        // ---- helpers ----

        private bool CanRun(EventDefinition definition)
        {
            return definition != null && definition.trigger != null && !IsDisabled(definition.id) && IsPackActive(definition)
                   && _pacing.OnceAllows(definition);
        }

        private bool IsPackActive(EventDefinition definition)
        {
            Func<string, bool> active = _options.IsPackActive;
            return active == null || active(definition.packId ?? string.Empty);
        }

        private float PackFrequency(string packId)
        {
            PackOptions options = _catalog.OptionsOf(packId);
            float factor = options == null ? 1f : options.Frequency;
            return float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f ? 0f : factor;
        }

        private TargetQuery CooldownQuery(EventDefinition definition, int today)
        {
            return new TargetQuery { Allowed = key => _pacing.CooldownAllows(definition, key, today) };
        }

        private void KeepAllowed(EventDefinition definition, int today, List<string> keys, List<float> weights)
        {
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                if (_pacing.CooldownAllows(definition, keys[i], today)) continue;
                keys.RemoveAt(i);
                weights.RemoveAt(i);
            }
        }

        private static bool IsDailyChance(EventDefinition definition)
        {
            return definition != null && definition.trigger != null && definition.trigger.type == EventTriggerType.DailyChance;
        }

        private static bool IsDue(EventTrigger trigger, int today)
        {
            return string.Equals(trigger.checkEvery, DayCheckEvery, StringComparison.OrdinalIgnoreCase) || EventDays.IsFirstOfMonth(today);
        }

        /// <summary>The lists of Date and Condition events and the categories with DailyChance events, rebuilt when the catalog changes.</summary>
        private void EnsureIndex()
        {
            IList<EventDefinition> all = _catalog.All ?? new EventDefinition[0];
            EventConfig config = _catalog.Config ?? EventConfig.Defaults;
            if (ReferenceEquals(all, _indexedAll) && all.Count == _indexedCount && ReferenceEquals(config, _indexedConfig)) return;

            _indexedAll = all;
            _indexedCount = all.Count;
            _indexedConfig = config;
            _dateEvents.Clear();
            _conditionEvents.Clear();
            _lotteryCategories.Clear();

            foreach (EventDefinition definition in all)
            {
                if (definition == null || definition.trigger == null) continue;
                if (definition.trigger.type == EventTriggerType.Date) _dateEvents.Add(definition);
                else if (definition.trigger.type == EventTriggerType.Condition) _conditionEvents.Add(definition);
            }

            foreach (CategoryDefinition category in config.Categories)
            {
                foreach (EventDefinition definition in _catalog.InCategory(category.Id))
                {
                    if (!IsDailyChance(definition)) continue;
                    _lotteryCategories.Add(category);
                    break;
                }
            }
        }

        private void Step(string site, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                _options.OnFault(site, ex);
            }
        }

        private bool Guard(EventDefinition definition, string site, Action body)
        {
            try
            {
                body();
                return true;
            }
            catch (Exception ex)
            {
                Fault(definition.id, site, ex);
                return false;
            }
        }

        private void Fault(string eventId, string site, Exception ex)
        {
            _options.OnFault(site + "/" + eventId, ex);

            int count;
            _faults.TryGetValue(eventId, out count);
            _faults[eventId] = ++count;
            if (count >= FaultLimit && _disabled.Add(eventId))
            {
                Info("event '" + eventId + "' failed " + count + " times and is off until the game restarts");
            }
        }

        private void Info(string line)
        {
            Action<string> info = _options.OnInfo;
            if (info != null) info(line);
        }

        private static string[] CopyArgs(string[] args)
        {
            if (args == null) return new string[0];
            var copy = new string[args.Length];
            Array.Copy(args, copy, args.Length);
            return copy;
        }
    }
}
