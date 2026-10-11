using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>The hooks of the runtime that belong to the game side (the bus of the API, the AI choosers) and to the choice controller.</summary>
    public sealed class EffectRuntimeOptions
    {
        /// <summary>A failure of a handler or of a listener: the site and the exception (<c>ModHooks.ReportFault</c> in the game).</summary>
        public Action<string, Exception> OnFault = (site, error) => { };

        /// <summary>A line for the log at information level.</summary>
        public Action<string> OnInfo = line => { };

        /// <summary>
        /// The choice an AI company takes for a new instance (the <c>EventAi</c> chain, then the default policy). Null - the <c>defaultChoice</c> of the
        /// definition at once; a null answer - the instance waits for its deadline.
        /// </summary>
        public Func<EventInstance, string> DecideForAi;

        /// <summary>An instance was created and its own effects applied (<c>EventBus.OnEventFired</c>).</summary>
        public Action<EventInstance> Fired;

        /// <summary>A choice was made and its effects applied (<c>EventBus.OnChoiceResolved</c>): the instance, the choice, automatic.</summary>
        public Action<EventInstance, string, bool> ChoiceResolved;

        /// <summary>A timed effect ended (<c>EventBus.OnEffectExpired</c>).</summary>
        public Action<AppliedEffect> EffectExpired;

        /// <summary>
        /// A waiting instance was cancelled (its target went bankrupt, its event is gone): nothing of its choices was applied. The UI closes the letter and the window.
        /// A failure of the callback is reported and does not stop the day.
        /// </summary>
        public Action<EventInstanceDto> Cancelled;

        /// <summary>
        /// The target of a waiting instance is gone (bankrupt): the instance is cancelled on the next deadline step instead of being closed with its default
        /// choice (FR-4 item 7). Null - no instance is cancelled for this reason.
        /// </summary>
        public Func<EventInstanceDto, bool> TargetGone;

        /// <summary>
        /// The deadline of a waiting instance has come on the day (the choice controller adds the delivery of the mail and the batch run). Null - when
        /// <c>deadlineDay &gt; 0</c> and the day has reached it.
        /// </summary>
        public Func<EventInstanceDto, int, bool> DeadlineDue;
    }

    /// <summary>
    /// The instances and their effects (FR-5, D1, D8, D10; design.md "Runtime-модель", "Состояния экземпляра"), behind the scheduler's
    /// <see cref="ISchedulerRuntime"/>:
    /// <list type="bullet">
    /// <item><b>Firing</b>: <see cref="InstanceFactory"/> resolves every effect once; the event's own effects are applied at once (the chance decides: a miss
    /// is <c>Skipped</c>), the journal gets <c>Fired</c>, <c>EventBus.OnEventFired</c> follows; an AI company decides on the day of the firing.</item>
    /// <item><b>A choice</b> (<see cref="ResolveChoice"/>, the deadline with <c>defaultChoice</c>): the saved effects of the choice are applied without being
    /// computed again; then the history (<c>es.choiceMade</c>), the journal and <c>EventBus.OnChoiceResolved</c>.</item>
    /// <item><b>Timed effects</b> are active from the day they were applied until <c>endDay = startDay + durationDays</c>, when they are removed (FR-5 item 3);
    /// curves tick every day. Their contributions go through the aggregator. A frozen pack (D10) keeps its effects as <c>Suspended</c>: the aggregator leaves
    /// them out and they do not tick, but their end dates stay absolute.</item>
    /// <item><b>Failures</b>: an exception of a handler marks the effect <c>Failed</c>, goes to <see cref="EffectRuntimeOptions.OnFault"/> and the journal
    /// (<c>EffectFailed</c>); the other effects and the day go on (FR-5 item 8).</item>
    /// <item><b><see cref="ApplyDirect"/></b> (AC-6.3): a service instance of kind <c>Direct</c> whose <c>eventId</c> is the source id: saved, expiring and
    /// journaled like any other.</item>
    /// <item><b>Saving</b> (<see cref="IEventSavePart"/>): the instances that wait for a choice or have an effect running, the next id and the journal. After a
    /// load the effects are put back by their handlers' <c>Restore</c> (<see cref="AfterLoad"/>).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class EffectRuntime : ISchedulerRuntime, IEventSavePart
    {
        private readonly IEffectTable _table;
        private readonly ModifierAggregator _aggregator;
        private readonly EventRandom _random;
        private readonly EventHistory _history;
        private readonly ISchedulerCatalog _catalog;
        private readonly EventLogStore _log;
        private readonly EffectRuntimeOptions _options;
        private readonly EffectResolver _resolver;
        private readonly InstanceFactory _factory;

        private readonly List<EventInstanceDto> _instances = new List<EventInstanceDto>();
        private readonly Dictionary<int, EventInstanceDto> _byId = new Dictionary<int, EventInstanceDto>();
        private readonly List<AppliedEffect> _active = new List<AppliedEffect>();
        private readonly Dictionary<string, EventEffect> _directSpecs = new Dictionary<string, EventEffect>(StringComparer.Ordinal);
        private int _nextId = 1;
        private bool _needsRestore;

        public EffectRuntime(IEffectTable table, ModifierAggregator aggregator, EventRandom random, EventHistory history, ISchedulerCatalog catalog,
            EventLogStore log, EffectRuntimeOptions options)
        {
            if (table == null) throw new ArgumentNullException("table");
            if (aggregator == null) throw new ArgumentNullException("aggregator");
            if (random == null) throw new ArgumentNullException("random");
            if (history == null) throw new ArgumentNullException("history");
            if (catalog == null) throw new ArgumentNullException("catalog");
            _table = table;
            _aggregator = aggregator;
            _random = random;
            _history = history;
            _catalog = catalog;
            _log = log ?? new EventLogStore();
            _options = options ?? new EffectRuntimeOptions();
            _resolver = new EffectResolver(table, aggregator, catalog.OptionsOf, Fault);
            _factory = new InstanceFactory(_resolver, random);
        }

        public EffectResolver Resolver
        {
            get { return _resolver; }
        }

        public InstanceFactory Factory
        {
            get { return _factory; }
        }

        public ModifierAggregator Aggregator
        {
            get { return _aggregator; }
        }

        public EventLogStore Log
        {
            get { return _log; }
        }

        /// <summary>The live instances by id (waiting for a choice, or with an effect running).</summary>
        public IList<EventInstanceDto> Instances
        {
            get { return new ReadOnlyCollection<EventInstanceDto>(new List<EventInstanceDto>(_instances)); }
        }

        /// <summary>The timed effects that run (active or frozen), in the order they were applied.</summary>
        public IList<AppliedEffect> ActiveEffects
        {
            get { return new ReadOnlyCollection<AppliedEffect>(new List<AppliedEffect>(_active)); }
        }

        /// <summary>The id the next instance gets.</summary>
        public int NextInstanceId
        {
            get { return _nextId; }
        }

        /// <summary>The live instance with this id, or null.</summary>
        public EventInstanceDto Find(int instanceId)
        {
            EventInstanceDto instance;
            return _byId.TryGetValue(instanceId, out instance) ? instance : null;
        }

        /// <summary>The definition of an instance (null for <c>Direct</c> and for an event no longer in the registry).</summary>
        public EventDefinition DefinitionOf(EventInstanceDto instance)
        {
            if (instance == null || string.Equals(instance.kind, EventInstanceKinds.Direct, StringComparison.Ordinal)) return null;
            return _catalog.Get(instance.eventId);
        }

        /// <summary>The pack of an effect (by its instance's event); null for a <c>Direct</c> effect or an unknown one.</summary>
        public string PackOf(AppliedEffect effect)
        {
            if (effect == null) return null;
            EventDefinition definition = DefinitionOf(Find(effect.instanceId));
            return definition != null && !string.IsNullOrEmpty(definition.packId) ? definition.packId : null;
        }

        // ---- ISchedulerRuntime ----

        public int Fire(FiringPlan plan)
        {
            if (plan == null) return 0;
            int depth = DepthOf(plan);
            if (depth > ChainFlags.MaxDepth)
            {
                Info("'" + plan.Definition.id + "' is not fired: the chain would be deeper than " + ChainFlags.MaxDepth + " levels");
                return 0;
            }

            IList<EventInstanceDto> created = _factory.Build(plan, () => _nextId++, depth);
            foreach (EventInstanceDto instance in created) Add(instance);
            foreach (EventInstanceDto instance in created) Start(instance, plan.Definition, plan.Today);
            return created.Count;
        }

        public void ExpireEffects(int today)
        {
            if (_needsRestore) AfterLoad(today);

            foreach (AppliedEffect effect in _active.ToArray())
            {
                if (effect.endDay <= 0 || today < effect.endDay) continue;
                End(effect, today, false);
            }
        }

        public void TickEffects(int today)
        {
            if (_needsRestore) AfterLoad(today);

            foreach (AppliedEffect effect in _active.ToArray())
            {
                if (!string.Equals(effect.state, AppliedEffectStates.Active, StringComparison.Ordinal)) continue;
                IEffectHandlerPort handler = _table.Get(effect.type);
                if (handler == null || !handler.IsTimed) continue;

                EventInstanceDto instance = Find(effect.instanceId);
                try
                {
                    handler.Tick(effect, CallFor(instance, effect, today));
                }
                catch (Exception ex)
                {
                    Fault("effects/tick/" + effect.type, ex);
                    End(effect, today, true);
                }
            }
        }

        public void RunDeadlines(int today)
        {
            Func<EventInstanceDto, int, bool> due = _options.DeadlineDue ?? DefaultDeadlineDue;
            foreach (EventInstanceDto instance in _instances.ToArray())
            {
                if (!string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) continue;

                if (IsGone(instance))
                {
                    Cancel(instance, today);
                    continue;
                }

                bool now;
                try
                {
                    now = due(instance, today);
                }
                catch (Exception ex)
                {
                    Fault("deadlines/" + instance.eventId, ex);
                    continue;
                }
                if (!now) continue;

                EventDefinition definition = DefinitionOf(instance);
                string choice = definition != null ? definition.defaultChoice : null;
                if (!Close(instance, choice, true, today, EventInstanceStates.Expired)) Cancel(instance, today);
            }
        }

        /// <summary>The rule of the deadline without a mail: <c>deadlineDay &gt; 0</c> and <c>today ≥ deadlineDay</c>.</summary>
        public static bool DefaultDeadlineDue(EventInstanceDto instance, int today)
        {
            return instance != null && instance.deadlineDay > 0 && today >= instance.deadlineDay;
        }

        // ---- choices ----

        /// <summary>
        /// The company of a waiting instance takes a choice (the player's click, an AI decision): the saved effects of the choice are applied, the choice is
        /// recorded, the instance becomes <c>Resolved</c>. False when the instance does not wait (a second answer is ignored) or has no such choice.
        /// </summary>
        public bool ResolveChoice(int instanceId, string choiceId, bool auto, int today)
        {
            EventInstanceDto instance = Find(instanceId);
            return instance != null && Close(instance, choiceId, auto, today, EventInstanceStates.Resolved);
        }

        /// <summary>Cancels a waiting instance (its target went bankrupt, its event is gone): nothing of its choices is applied, the journal gets <c>Cancelled</c>.</summary>
        public bool Cancel(int instanceId, int today)
        {
            EventInstanceDto instance = Find(instanceId);
            if (instance == null || !string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) return false;
            Cancel(instance, today);
            return true;
        }

        // ---- EventEffects.ApplyDirect ----

        /// <summary>
        /// Resolves and applies one effect outside an event (AC-6.3): a service instance of kind <c>Direct</c> with <c>eventId = sourceId</c>, its effect
        /// <c>&lt;id&gt;/common/0</c>. Returns the effect with its final numbers, or null when the type is unknown, the company is not valid, or the effect failed.
        /// </summary>
        public AppliedEffect ApplyDirect(EventEffect spec, string companyKey, string sourceId, string[] args, int today)
        {
            if (spec == null || string.IsNullOrEmpty(spec.type)) return null;
            if (!IsTarget(companyKey))
            {
                Info("ApplyDirect from '" + sourceId + "' ignored: '" + companyKey + "' is not a company key");
                return null;
            }

            IEffectHandlerPort handler = _table.Get(spec.type);
            if (handler == null)
            {
                Info("ApplyDirect from '" + sourceId + "' ignored: no handler is registered for the effect type '" + spec.type + "'");
                return null;
            }

            EventEffect effectSpec = spec.@params != null ? spec : new EventEffect { type = spec.type, @params = new EffectParams(new JsonObject()), durationDays = spec.durationDays };
            int id = _nextId++;
            var instance = new EventInstanceDto
            {
                id = id,
                groupId = id,
                kind = EventInstanceKinds.Direct,
                eventId = string.IsNullOrEmpty(sourceId) ? "direct" : sourceId,
                companyKey = companyKey,
                firedDay = today,
                state = EventInstanceStates.Resolved,
                args = Copy(args),
            };

            DerivedRng rng = _random.ForInstance(id);
            bool hit = ChanceRoller.Roll(new[] { effectSpec }, rng)[0];
            string key = EffectScope.IsGlobal(effectSpec) ? EventCompanyKeys.Global : companyKey;
            AppliedEffect effect = _resolver.ResolveOne(effectSpec, EffectResolver.EffectId(id, ContentNames.CommonChoice, 0), hit,
                _resolver.CallFor(null, instance, effectSpec, key, today, rng));
            instance.common.Add(effect);

            if (string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal))
            {
                _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.EffectFailed, null, new[] { effect }));
                return null;
            }

            Add(instance);
            _directSpecs[effect.id] = effectSpec;
            Apply(instance, null, effect, today, effectSpec);
            UpdateIndex(instance);
            _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.Fired, null, instance.common));
            LogFailures(instance, instance.common, today);
            Cleanup(instance);
            return string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal) ? null : effect;
        }

        // ---- packs (D10) ----

        /// <summary>Freezes the effects of a pack: the aggregator leaves out their contributions, they do not tick; their end dates stay.</summary>
        public void SuspendPack(string packId)
        {
            if (string.IsNullOrEmpty(packId)) return;
            _aggregator.Suspend(packId);
            foreach (AppliedEffect effect in _active)
            {
                if (string.Equals(effect.state, AppliedEffectStates.Active, StringComparison.Ordinal) && string.Equals(PackOf(effect), packId, StringComparison.Ordinal))
                {
                    effect.state = AppliedEffectStates.Suspended;
                }
            }
        }

        /// <summary>Thaws the effects of a pack.</summary>
        public void ResumePack(string packId)
        {
            if (string.IsNullOrEmpty(packId)) return;
            _aggregator.Resume(packId);
            foreach (AppliedEffect effect in _active)
            {
                if (string.Equals(effect.state, AppliedEffectStates.Suspended, StringComparison.Ordinal) && string.Equals(PackOf(effect), packId, StringComparison.Ordinal))
                {
                    effect.state = AppliedEffectStates.Active;
                }
            }
        }

        /// <summary>Brings the frozen packs in line with their switches (<paramref name="isActive"/> per pack id).</summary>
        public void SyncPacks(IEnumerable<string> packIds, Func<string, bool> isActive)
        {
            if (packIds == null || isActive == null) return;
            foreach (string pack in packIds)
            {
                if (string.IsNullOrEmpty(pack)) continue;
                bool active = isActive(pack);
                if (active && _aggregator.IsSuspended(pack)) ResumePack(pack);
                else if (!active && !_aggregator.IsSuspended(pack)) SuspendPack(pack);
            }
        }

        // ---- IEventSavePart ----

        /// <summary>Forgets every instance, every contribution and the journal (a new game, before a save is read).</summary>
        public void Clear()
        {
            _instances.Clear();
            _byId.Clear();
            _active.Clear();
            _directSpecs.Clear();
            _nextId = 1;
            _needsRestore = false;
            _aggregator.ClearAll();
            _history.Active.Clear();
            _log.Clear();
        }

        public void Export(EventSystemSaveDto dto)
        {
            if (dto == null) return;
            dto.nextInstanceId = _nextId;
            dto.instances = new List<EventInstanceDto>(_instances);
            _log.Export(dto);
        }

        /// <summary>
        /// Takes the saved instances and the journal. An instance or an effect that does not make sense (NFR-7: no id, a bad company key or state, a number that
        /// is not finite, a multiplier out of range, a negative duration) is dropped with a line in <paramref name="problems"/>. The effects are put back into the
        /// game later, in <see cref="AfterLoad"/>, when the packs are loaded.
        /// </summary>
        public void Import(EventSystemSaveDto dto, List<string> problems)
        {
            Clear();
            if (dto == null) return;
            problems = problems ?? new List<string>();

            int maxId = 0;
            foreach (EventInstanceDto instance in dto.instances ?? new List<EventInstanceDto>())
            {
                string problem = SaveChecks.Instance(instance);
                if (problem == null && _byId.ContainsKey(instance.id)) problem = "a repeated id " + instance.id;
                if (problem != null)
                {
                    problems.Add("instances: an instance is dropped (" + problem + ")");
                    continue;
                }

                SaveChecks.Effects(instance, problems);
                if (!string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal) && !HasRunning(instance)) continue;

                _instances.Add(instance);
                _byId[instance.id] = instance;
                maxId = Math.Max(maxId, instance.id);
            }
            _instances.Sort((a, b) => a.id.CompareTo(b.id));

            foreach (EventInstanceDto instance in _instances)
            {
                foreach (AppliedEffect effect in AllEffects(instance))
                {
                    if (!IsRunning(effect)) continue;
                    effect.state = AppliedEffectStates.Active;   // a frozen pack is frozen again by its switch
                    _active.Add(effect);
                }
            }

            _nextId = Math.Max(Math.Max(1, dto.nextInstanceId), maxId + 1);
            _log.Import(dto, problems);
            _needsRestore = _instances.Count > 0;
        }

        /// <summary>
        /// After a load (<c>GameReady(Loaded)</c>, when the packs are loaded): the instances of events no longer in the registry are cancelled, the index of what
        /// is going on is built again, and every running effect is put back by its handler's <c>Restore</c>. Called by the first day too if the game did not.
        /// </summary>
        public void AfterLoad(int today)
        {
            _needsRestore = false;
            bool registryLoaded = _catalog.All != null && _catalog.All.Count > 0;

            foreach (EventInstanceDto instance in _instances.ToArray())
            {
                if (!registryLoaded || string.Equals(instance.kind, EventInstanceKinds.Direct, StringComparison.Ordinal) || DefinitionOf(instance) != null) continue;
                Info("instance " + instance.id + " of '" + instance.eventId + "' is cancelled: the event is no longer in the registry");
                foreach (AppliedEffect effect in AllEffects(instance))
                {
                    if (!IsRunning(effect)) continue;
                    effect.state = AppliedEffectStates.Done;
                    _active.Remove(effect);
                }
                if (string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) Cancel(instance, today);
                else
                {
                    _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.Cancelled, null, null));
                    Cleanup(instance);
                }
            }

            _history.Active.Clear();
            foreach (EventInstanceDto instance in _instances) UpdateIndex(instance);

            foreach (AppliedEffect effect in _active.ToArray())
            {
                EventInstanceDto instance = Find(effect.instanceId);
                IEffectHandlerPort handler = _table.Get(effect.type);
                if (handler == null || !handler.IsTimed)
                {
                    Fault("effects/restore/" + effect.type, new InvalidOperationException("no timed handler for the effect type '" + effect.type + "'"));
                    End(effect, today, true);
                    continue;
                }

                try
                {
                    handler.Restore(effect, CallFor(instance, effect, today));
                }
                catch (Exception ex)
                {
                    Fault("effects/restore/" + effect.type, ex);
                    End(effect, today, true);
                }
            }
        }

        // ---- creating and applying ----

        private int DepthOf(FiringPlan plan)
        {
            if (plan.ChainDepth > 0) return plan.ChainDepth;
            if (plan.ParentId <= 0) return 0;
            EventInstanceDto parent = Find(plan.ParentId);
            return (parent != null ? parent.chainDepth : 0) + 1;
        }

        private void Add(EventInstanceDto instance)
        {
            _instances.Add(instance);
            _byId[instance.id] = instance;
        }

        /// <summary>The event's own effects of a new instance, the index, the journal, the bus, and the decision of an AI company.</summary>
        private void Start(EventInstanceDto instance, EventDefinition definition, int today)
        {
            foreach (AppliedEffect effect in instance.common) Apply(instance, definition, effect, today, InstanceFactory.SpecOf(definition, effect));
            UpdateIndex(instance);
            _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.Fired, null, instance.common));
            LogFailures(instance, instance.common, today);
            Raise("fired", () =>
            {
                Action<EventInstance> fired = _options.Fired;
                if (fired != null) fired(new EventInstance(instance, definition));
            });

            if (string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal) && !InstanceFactory.IsPlayer(instance))
            {
                string choice = DecideForAi(instance, definition);
                if (!string.IsNullOrEmpty(choice)) Close(instance, choice, true, today, EventInstanceStates.Resolved);
            }
            Cleanup(instance);
        }

        private string DecideForAi(EventInstanceDto instance, EventDefinition definition)
        {
            Func<EventInstance, string> decide = _options.DecideForAi;
            if (decide == null) return definition.defaultChoice;
            try
            {
                return decide(new EventInstance(instance, definition));
            }
            catch (Exception ex)
            {
                Fault("ai/" + instance.eventId, ex);
                return definition.defaultChoice;
            }
        }

        /// <summary>
        /// Applies one resolved effect from its saved numbers: a miss is <c>Skipped</c>; a timed effect runs until <c>endDay = today + durationDays</c>; a failure
        /// is <c>Failed</c> (reported) and the rest goes on.
        /// </summary>
        private void Apply(EventInstanceDto instance, EventDefinition definition, AppliedEffect effect, int today, EventEffect spec)
        {
            if (effect == null || string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal)) return;
            if (!effect.hit)
            {
                effect.state = AppliedEffectStates.Skipped;
                return;
            }

            IEffectHandlerPort handler = _table.Get(effect.type);
            if (handler == null)
            {
                effect.state = AppliedEffectStates.Failed;
                Fault("effects/apply/" + effect.type, new InvalidOperationException("no handler is registered for the effect type '" + effect.type + "'"));
                return;
            }

            bool timed = handler.IsTimed;
            effect.state = AppliedEffectStates.Pending;
            effect.startDay = today;
            effect.endDay = timed ? today + Math.Max(1, effect.durationDays) : 0;
            try
            {
                handler.Apply(effect, Call(instance, definition, spec, effect.companyKey, today));
            }
            catch (Exception ex)
            {
                effect.state = AppliedEffectStates.Failed;
                Fault("effects/apply/" + effect.type, ex);
                if (timed) SafeClear(effect);
                return;
            }

            if (string.Equals(effect.state, AppliedEffectStates.Skipped, StringComparison.Ordinal) || string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal))
            {
                if (timed) SafeClear(effect);
                effect.endDay = 0;
                return;
            }

            if (!timed)
            {
                effect.state = AppliedEffectStates.Done;
                return;
            }

            string pack = definition != null ? definition.packId : null;
            effect.state = _aggregator.IsSuspended(pack) ? AppliedEffectStates.Suspended : AppliedEffectStates.Active;
            _active.Add(effect);
        }

        /// <summary>A waiting instance takes a choice; false when it does not wait or has no such choice.</summary>
        private bool Close(EventInstanceDto instance, string choiceId, bool auto, int today, string state)
        {
            if (instance == null || string.IsNullOrEmpty(choiceId) || !string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) return false;
            ChoiceOptionDto option = null;
            foreach (ChoiceOptionDto candidate in instance.options)
            {
                if (candidate != null && string.Equals(candidate.choiceId, choiceId, StringComparison.Ordinal)) option = candidate;
            }
            if (option == null) return false;

            EventDefinition definition = DefinitionOf(instance);
            instance.chosen = choiceId;
            instance.auto = auto;
            instance.state = state;
            foreach (AppliedEffect effect in option.effects)
            {
                EventEffect spec = InstanceFactory.SpecOf(definition, effect);
                Apply(instance, definition, effect, today, spec);
            }

            _history.RecordChoice(instance.eventId, choiceId, instance.companyKey, today);
            UpdateIndex(instance);
            _log.Add(EventLogStore.Entry(today, instance, auto ? EventLogKinds.AutoResolved : EventLogKinds.Resolved, choiceId, option.effects));
            LogFailures(instance, option.effects, today);
            Raise("resolved", () =>
            {
                Action<EventInstance, string, bool> resolved = _options.ChoiceResolved;
                if (resolved != null) resolved(new EventInstance(instance, definition), choiceId, auto);
            });
            Cleanup(instance);
            return true;
        }

        private void Cancel(EventInstanceDto instance, int today)
        {
            instance.state = EventInstanceStates.Cancelled;
            UpdateIndex(instance);
            _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.Cancelled, null, null));
            Raise("cancelled", () =>
            {
                Action<EventInstanceDto> cancelled = _options.Cancelled;
                if (cancelled != null) cancelled(instance);
            });
            Cleanup(instance);
        }

        private bool IsGone(EventInstanceDto instance)
        {
            Func<EventInstanceDto, bool> gone = _options.TargetGone;
            if (gone == null) return false;
            try
            {
                return gone(instance);
            }
            catch (Exception ex)
            {
                Fault("deadlines/gone/" + instance.eventId, ex);
                return false;
            }
        }

        /// <summary>A timed effect ends: its handler removes it, its contributions are dropped, the journal and the bus learn of it.</summary>
        private void End(AppliedEffect effect, int today, bool failed)
        {
            _active.Remove(effect);
            EventInstanceDto instance = Find(effect.instanceId);
            IEffectHandlerPort handler = _table.Get(effect.type);
            if (handler != null)
            {
                try
                {
                    handler.Remove(effect, CallFor(instance, effect, today));
                }
                catch (Exception ex)
                {
                    Fault("effects/remove/" + effect.type, ex);
                }
            }
            SafeClear(effect);

            effect.state = failed ? AppliedEffectStates.Failed : AppliedEffectStates.Done;
            _log.Add(EventLogStore.Entry(today, instance ?? Orphan(effect), failed ? EventLogKinds.EffectFailed : EventLogKinds.EffectEnded, null, new[] { effect }));
            if (!failed)
            {
                Raise("expired", () =>
                {
                    Action<AppliedEffect> expired = _options.EffectExpired;
                    if (expired != null) expired(effect);
                });
            }

            if (instance != null)
            {
                UpdateIndex(instance);
                Cleanup(instance);
            }
        }

        // ---- helpers ----

        private EffectCall CallFor(EventInstanceDto instance, AppliedEffect effect, int today)
        {
            EventDefinition definition = DefinitionOf(instance);
            EventEffect spec = InstanceFactory.SpecOf(definition, effect);
            EventEffect direct;
            if (spec == null && effect != null && effect.id != null && _directSpecs.TryGetValue(effect.id, out direct)) spec = direct;
            return Call(instance, definition, spec, effect != null ? effect.companyKey : null, today);
        }

        private EffectCall Call(EventInstanceDto instance, EventDefinition definition, EventEffect spec, string companyKey, int today)
        {
            EffectCall call = _resolver.CallFor(definition, instance, spec, companyKey, today, instance != null ? _random.ForInstance(instance.id) : null);
            return call;
        }

        private void UpdateIndex(EventInstanceDto instance)
        {
            EventDefinition definition = DefinitionOf(instance);
            bool awaiting = string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal);
            _history.Active.Set(instance.id, instance.groupId, instance.eventId, definition != null ? definition.category : null, instance.companyKey, awaiting,
                HasRunning(instance));
        }

        /// <summary>An instance that neither waits nor has an effect running leaves the store (the journal keeps it).</summary>
        private void Cleanup(EventInstanceDto instance)
        {
            if (string.Equals(instance.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal) || HasRunning(instance)) return;
            _instances.Remove(instance);
            _byId.Remove(instance.id);
            _history.Active.Remove(instance.id);
            foreach (AppliedEffect effect in AllEffects(instance))
            {
                if (effect != null && effect.id != null) _directSpecs.Remove(effect.id);
            }
        }

        private static bool HasRunning(EventInstanceDto instance)
        {
            foreach (AppliedEffect effect in AllEffects(instance))
            {
                if (IsRunning(effect)) return true;
            }
            return false;
        }

        private static bool IsRunning(AppliedEffect effect)
        {
            return effect != null && effect.endDay > 0
                   && (string.Equals(effect.state, AppliedEffectStates.Active, StringComparison.Ordinal)
                       || string.Equals(effect.state, AppliedEffectStates.Suspended, StringComparison.Ordinal));
        }

        private static IEnumerable<AppliedEffect> AllEffects(EventInstanceDto instance)
        {
            if (instance.common != null)
            {
                foreach (AppliedEffect effect in instance.common) yield return effect;
            }
            if (instance.options == null) yield break;
            foreach (ChoiceOptionDto option in instance.options)
            {
                if (option == null || option.effects == null) continue;
                foreach (AppliedEffect effect in option.effects) yield return effect;
            }
        }

        private void LogFailures(EventInstanceDto instance, IList<AppliedEffect> effects, int today)
        {
            List<AppliedEffect> failed = null;
            foreach (AppliedEffect effect in effects)
            {
                if (effect == null || !string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal)) continue;
                if (failed == null) failed = new List<AppliedEffect>();
                failed.Add(effect);
            }
            if (failed != null) _log.Add(EventLogStore.Entry(today, instance, EventLogKinds.EffectFailed, instance.chosen, failed));
        }

        private void SafeClear(AppliedEffect effect)
        {
            try
            {
                _aggregator.Clear(effect);
            }
            catch (Exception ex)
            {
                Fault("effects/clear/" + effect.type, ex);
            }
        }

        private void Raise(string name, Action raise)
        {
            try
            {
                raise();
            }
            catch (Exception ex)
            {
                Fault("bus/" + name, ex);
            }
        }

        private static EventInstanceDto Orphan(AppliedEffect effect)
        {
            return new EventInstanceDto { id = effect.instanceId, eventId = "?", companyKey = effect.companyKey };
        }

        private static bool IsTarget(string companyKey)
        {
            return CompanyKey.IsValid(companyKey) || string.Equals(companyKey, EventCompanyKeys.Global, StringComparison.Ordinal);
        }

        private void Fault(string site, Exception error)
        {
            Action<string, Exception> fault = _options.OnFault;
            if (fault != null) fault(site, error);
        }

        private void Info(string line)
        {
            Action<string> info = _options.OnInfo;
            if (info != null) info(line);
        }

        private static string[] Copy(string[] args)
        {
            if (args == null) return new string[0];
            var copy = new string[args.Length];
            for (int i = 0; i < args.Length; i++) copy[i] = args[i] ?? string.Empty;
            return copy;
        }
    }

    /// <summary>The checks of saved instances and effects (NFR-7): what a hand-edited side file must not bring in.</summary>
    public static class SaveChecks
    {
        /// <summary>The widest multiplier a saved modifier effect may carry; content allows at most 5 and the limits of the channels are narrower.</summary>
        public const float MaxMultiplier = 10f;

        /// <summary>The longest an effect or a choice may last, in game days (a hundred years); a hand-edited file must not hold a timer that never ends.</summary>
        public const int MaxDurationDays = 36500;

        /// <summary>The last day number a saved day may have (the year 2900; <c>DateTime</c> ends in the year 9999, and the texts turn the number into a date).</summary>
        public const int MaxDay = 365 * 930;

        /// <summary>The largest amount of money, popularity or reputation a saved effect may carry (a trillion; money is in dollars).</summary>
        public const float MaxAmount = 1e12f;

        private static readonly string[] InstanceStates =
        {
            EventInstanceStates.AwaitingChoice, EventInstanceStates.Resolved, EventInstanceStates.Expired, EventInstanceStates.Cancelled,
        };

        private static readonly string[] EffectStates =
        {
            AppliedEffectStates.Pending, AppliedEffectStates.Active, AppliedEffectStates.Done, AppliedEffectStates.Skipped, AppliedEffectStates.Suspended,
            AppliedEffectStates.Failed, AppliedEffectStates.Clamped,
        };

        /// <summary>What is wrong with a saved instance, or null when it can be used.</summary>
        public static string Instance(EventInstanceDto instance)
        {
            if (instance == null) return "empty";
            if (instance.id <= 0) return "id " + instance.id;
            if (string.IsNullOrEmpty(instance.eventId)) return "no event id";
            if (!CompanyKey.IsValid(instance.companyKey) && !string.Equals(instance.companyKey, EventCompanyKeys.Global, StringComparison.Ordinal)) return "company '" + instance.companyKey + "'";
            if (Array.IndexOf(InstanceStates, instance.state) < 0) return "state '" + instance.state + "'";
            if (!string.Equals(instance.kind, EventInstanceKinds.Event, StringComparison.Ordinal) && !string.Equals(instance.kind, EventInstanceKinds.Direct, StringComparison.Ordinal)) return "kind '" + instance.kind + "'";
            if (instance.firedDay < 0 || instance.deadlineDay < 0 || (instance.deadlineDay > 0 && instance.deadlineDay < instance.firedDay)) return "days";
            if (instance.firedDay > MaxDay || instance.deliveredDay > MaxDay || instance.deadlineDay > MaxDay || (instance.deadlineDay > 0 && instance.deadlineDay - instance.firedDay > MaxDurationDays)) return "days out of range";
            if (instance.deliveredDay != 0 && instance.deliveredDay < instance.firedDay) return "delivery day";
            if (instance.chainDepth < 0 || instance.chainDepth > ChainFlags.MaxDepth || instance.parentId < 0 || instance.groupId < 0) return "chain";
            return null;
        }

        /// <summary>Drops the effects of the instance that cannot be used and fills the lists that are missing.</summary>
        public static void Effects(EventInstanceDto instance, List<string> problems)
        {
            if (instance.args == null) instance.args = new string[0];
            if (instance.chosen == null) instance.chosen = string.Empty;
            if (instance.options == null) instance.options = new List<ChoiceOptionDto>();
            if (instance.common == null) instance.common = new List<AppliedEffect>();
            instance.options.RemoveAll(option => option == null || string.IsNullOrEmpty(option.choiceId));

            Keep(instance.common, instance, problems);
            foreach (ChoiceOptionDto option in instance.options)
            {
                if (option.effects == null) option.effects = new List<AppliedEffect>();
                Keep(option.effects, instance, problems);
            }
        }

        /// <summary>What is wrong with a saved effect, or null when it can be used.</summary>
        public static string Effect(AppliedEffect effect, EventInstanceDto instance)
        {
            if (effect == null) return "empty";
            if (string.IsNullOrEmpty(effect.id) || string.IsNullOrEmpty(effect.type)) return "no id or type";
            if (effect.instanceId != instance.id) return "the instance id";
            if (Array.IndexOf(EffectStates, effect.state) < 0) return "state '" + effect.state + "'";
            if (float.IsNaN(effect.value) || float.IsInfinity(effect.value)) return "a number that is not finite";
            if (effect.durationDays < 0 || effect.startDay < 0 || effect.endDay < 0 || (effect.endDay > 0 && effect.endDay < effect.startDay)) return "a negative duration";
            if (effect.durationDays > MaxDurationDays || effect.startDay > MaxDay || effect.endDay > MaxDay + MaxDurationDays) return "a duration out of range";
            if (float.IsNaN(effect.chance) || effect.chance < 0f || effect.chance > 1f) return "the chance " + effect.chance;
            if (!string.IsNullOrEmpty(effect.channel) && (effect.value < 0f || effect.value > MaxMultiplier)) return "the multiplier " + effect.value;
            if (string.IsNullOrEmpty(effect.channel) && Math.Abs(effect.value) > MaxAmount) return "the amount " + effect.value;
            if (effect.curve != null)
            {
                foreach (float number in effect.curve)
                {
                    if (float.IsNaN(number) || float.IsInfinity(number)) return "a curve number that is not finite";
                }
            }
            if (effect.textArgs == null) effect.textArgs = new string[0];
            if (effect.channel == null) effect.channel = string.Empty;
            return null;
        }

        private static void Keep(List<AppliedEffect> effects, EventInstanceDto instance, List<string> problems)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                string problem = Effect(effects[i], instance);
                if (problem == null) continue;
                if (problems != null) problems.Add("instances: an effect of instance " + instance.id + " is dropped (" + problem + ")");
                effects.RemoveAt(i);
            }
        }
    }
}
