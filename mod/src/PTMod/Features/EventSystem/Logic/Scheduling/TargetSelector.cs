using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>Why an event fires (the step of the day or the caller), for the log and the runtime.</summary>
    public enum FireOrigin
    {
        /// <summary>The daily lottery of the event's category.</summary>
        Lottery,

        /// <summary>A <c>Date</c> trigger.</summary>
        Date,

        /// <summary>A <c>Condition</c> trigger.</summary>
        Condition,

        /// <summary>A delayed firing that came due (<c>FireEvent</c> with a delay, <c>EventBus.Fire</c> with <c>DelayDays</c>).</summary>
        Scheduled,

        /// <summary>A firing that waited for a free slot of the player's pending choices (FR-2 item 6).</summary>
        Deferred,

        /// <summary><c>EventBus.Fire</c> without a delay.</summary>
        Manual,
    }

    /// <summary>Picks an index by weight with a number already drawn (the lottery numbers are drawn whether or not they are used, D5).</summary>
    public static class WeightedPick
    {
        /// <summary>
        /// The index whose share of the total weight contains <paramref name="u"/> (0..1): the first with <c>u × total &lt; running sum</c>. Weights that are
        /// not finite and above zero never win. -1 when no weight qualifies.
        /// </summary>
        public static int Index(IList<float> weights, float u)
        {
            if (weights == null) return -1;

            double total = 0.0;
            int last = -1;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!IsUsable(weights[i])) continue;
                total += weights[i];
                last = i;
            }
            if (last < 0) return -1;

            double point = Clamp01(u) * total;
            double sum = 0.0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (!IsUsable(weights[i])) continue;
                sum += weights[i];
                if (point < sum) return i;
            }
            return last;
        }

        private static bool IsUsable(float weight)
        {
            return weight > 0f && !float.IsInfinity(weight);
        }

        private static double Clamp01(float u)
        {
            if (!(u > 0f)) return 0.0;     // NaN too
            return u >= 1f ? 0.99999999 : u;
        }
    }

    /// <summary>The scope of an effect (<c>params.scope</c>): on the target company or on the whole market.</summary>
    public static class EffectScope
    {
        /// <summary><c>params.scope</c> is <c>"Global"</c> (any case).</summary>
        public static bool IsGlobal(EventEffect effect)
        {
            if (effect == null || effect.@params == null) return false;
            string scope = effect.@params.GetString(EffectParamKeys.Scope, EffectScopes.Target);
            return string.Equals(scope, EffectScopes.Global, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// One instance a firing will create: its company, its place in the group, and which effects of the event itself (<c>effects</c>) it carries. The choices
    /// (and their effects) belong to every instance; a global-scope effect of the event belongs to the first instance only (D8).
    /// </summary>
    public sealed class PlannedInstance
    {
        public PlannedInstance(string companyKey, int index, IList<EventEffect> targetEffects, IList<EventEffect> globalEffects, bool hasChoices)
        {
            CompanyKey = companyKey;
            Index = index;
            TargetEffects = new ReadOnlyCollection<EventEffect>(new List<EventEffect>(targetEffects));
            GlobalEffects = new ReadOnlyCollection<EventEffect>(new List<EventEffect>(globalEffects));
            HasChoices = hasChoices;
        }

        public string CompanyKey { get; private set; }

        /// <summary>Position in the group (0 for a single instance).</summary>
        public int Index { get; private set; }

        /// <summary>Effects of the event with <c>scope: "Target"</c>: applied to this instance's company.</summary>
        public IList<EventEffect> TargetEffects { get; private set; }

        /// <summary>Effects of the event with <c>scope: "Global"</c>: applied once per firing, so only the first instance carries them (company key <c>global</c>).</summary>
        public IList<EventEffect> GlobalEffects { get; private set; }

        /// <summary>The instance offers the choices of the definition.</summary>
        public bool HasChoices { get; private set; }
    }

    /// <summary>
    /// What a firing is: the event, its targets (one company, or every fitting company of an <c>AllCompanies</c> event as one group), the day and the origin.
    /// The scheduler builds it, the instance factory turns it into instances (<see cref="Expand"/>), the pacing records it.
    /// </summary>
    public sealed class FiringPlan
    {
        private static readonly EventEffect[] NoEffects = new EventEffect[0];

        public FiringPlan(EventDefinition definition, IList<string> targets, bool isGroup)
        {
            if (definition == null) throw new ArgumentNullException("definition");
            if (targets == null || targets.Count == 0) throw new ArgumentException("A firing needs at least one target.", "targets");
            Definition = definition;
            Targets = new ReadOnlyCollection<string>(new List<string>(targets));
            IsGroup = isGroup;
            Args = new string[0];
        }

        public EventDefinition Definition { get; private set; }

        /// <summary>The company keys of the instances, in the order of <c>IEventWorld.CompanyKeys</c>.</summary>
        public IList<string> Targets { get; private set; }

        /// <summary>One firing of an <c>AllCompanies</c> event: the instances share a group id and the global effects are applied once.</summary>
        public bool IsGroup { get; private set; }

        /// <summary>The day of the firing.</summary>
        public int Today;

        public FireOrigin Origin;

        /// <summary>The instance whose <c>FireEvent</c> started this one; 0 for none.</summary>
        public int ParentId;

        /// <summary>The depth of the chain the firing continues (<c>ChainFlags</c>); 0 - not a chain, or the runtime works it out from <see cref="ParentId"/>.</summary>
        public int ChainDepth;

        /// <summary>Positional arguments of the texts; never null.</summary>
        public string[] Args;

        /// <summary>The definition offers choices.</summary>
        public bool HasChoices
        {
            get { return Definition.choices != null && Definition.choices.Length > 0; }
        }

        /// <summary>The player's company is among the targets.</summary>
        public bool IncludesPlayer
        {
            get
            {
                foreach (string key in Targets)
                {
                    if (CompanyKey.IsPlayer(key)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// The instances of the firing (D8, AC-3.2): one per target, every one with the choices; the event's own target-scope effects on every one, its
        /// global-scope effects on the first only.
        /// </summary>
        public IList<PlannedInstance> Expand()
        {
            var target = new List<EventEffect>();
            var global = new List<EventEffect>();
            foreach (EventEffect effect in Definition.effects ?? NoEffects)
            {
                if (effect == null) continue;
                if (EffectScope.IsGlobal(effect)) global.Add(effect);
                else target.Add(effect);
            }

            var result = new List<PlannedInstance>(Targets.Count);
            for (int i = 0; i < Targets.Count; i++)
            {
                result.Add(new PlannedInstance(Targets[i], i, target, i == 0 ? global : (IList<EventEffect>)NoEffects, HasChoices));
            }
            return result;
        }
    }

    /// <summary>What a selection may and may not look at.</summary>
    public sealed class TargetQuery
    {
        /// <summary>A company named by the caller (<c>EventFireContext.CompanyKey</c>, a chain's target); null - the <c>targets</c> of the definition decide.</summary>
        public string CompanyKey;

        /// <summary>Skip the <c>conditions</c> of the definition; with a named company also its <c>targets.filter</c> (the caller has decided).</summary>
        public bool IgnoreConditions;

        /// <summary>The pacing per company (the cooldown); null - every company is allowed.</summary>
        public Func<string, bool> Allowed;
    }

    /// <summary>
    /// Chooses the companies an event hits (FR-3), by the same rules for the player and the AI (FR-3 item 7).
    /// <list type="bullet">
    /// <item><c>Player</c> - the player; <c>RandomAI</c> - one AI company; <c>RandomCompany</c> - one company of all, the player included; <c>AllCompanies</c> -
    /// every fitting company as one group; <c>Specific</c> - the company of <c>targets.companyKey</c>, none when it is gone or bankrupt (the firing is then
    /// skipped without using the cooldown, FR-3 item 5).</item>
    /// <item>A candidate passes <c>targets.filter</c> and the <c>conditions</c> of the definition (the company is the subject) and the pacing of the caller.</item>
    /// <item>Weights (design.md, "Выбор цели"): <c>Uniform</c> 1, <c>FactoryCapacity</c> the lines (0 lines - not a candidate), <c>MarketShare</c> share + 0.01,
    /// <c>QuarterlyRevenue</c> max(QR, 0) + 1. They pick one company of the random kinds and decide who belongs to an <c>AllCompanies</c> group;
    /// <c>Player</c>, <c>Specific</c> and a named company are not weighed.</item>
    /// </list>
    /// </summary>
    public sealed class TargetSelector
    {
        private readonly IEventWorld _world;

        public TargetSelector(IEventWorld world)
        {
            if (world == null) throw new ArgumentNullException("world");
            _world = world;
        }

        /// <summary>The weight of a company with these numbers; 0 means "not a candidate".</summary>
        public static float WeightOf(TargetWeighting weighting, int factoryLines, float marketShare, float quarterlyRevenue)
        {
            switch (weighting)
            {
                case TargetWeighting.FactoryCapacity:
                    return factoryLines > 0 ? factoryLines : 0f;
                case TargetWeighting.MarketShare:
                    return Finite(marketShare) + 0.01f;
                case TargetWeighting.QuarterlyRevenue:
                    return Math.Max(Finite(quarterlyRevenue), 0f) + 1f;
                default:
                    return 1f;
            }
        }

        /// <summary>The weight of a company of the world.</summary>
        public float WeightOf(string companyKey, TargetWeighting weighting)
        {
            switch (weighting)
            {
                case TargetWeighting.FactoryCapacity:
                    return WeightOf(weighting, _world.FactoryLines(companyKey), 0f, 0f);
                case TargetWeighting.MarketShare:
                    return WeightOf(weighting, 0, _world.MarketShare(companyKey), 0f);
                case TargetWeighting.QuarterlyRevenue:
                    return WeightOf(weighting, 0, 0f, _world.QuarterlyRevenue(companyKey));
                default:
                    return 1f;
            }
        }

        /// <summary>The event targets a group of every fitting company (and no company is named).</summary>
        public static bool IsGroupFiring(EventDefinition definition, TargetQuery query)
        {
            return TypeOf(definition) == EventTargetType.AllCompanies && (query == null || string.IsNullOrEmpty(query.CompanyKey));
        }

        /// <summary>The fitting companies and their weights, in the order of the world's companies. The lists are cleared first.</summary>
        public void Candidates(EventDefinition definition, TargetQuery query, List<string> keys, List<float> weights)
        {
            if (definition == null) throw new ArgumentNullException("definition");
            keys.Clear();
            weights.Clear();
            query = query ?? new TargetQuery();

            EventTargetType type = TypeOf(definition);
            EventTargetSpec spec = definition.targets;
            bool named = !string.IsNullOrEmpty(query.CompanyKey);
            bool weighed = !named && (type == EventTargetType.RandomAI || type == EventTargetType.RandomCompany || type == EventTargetType.AllCompanies);
            TargetWeighting weighting = spec != null ? spec.weighting : TargetWeighting.Uniform;
            bool checkFilter = spec != null && spec.filter != null && !(named && query.IgnoreConditions);
            bool checkConditions = definition.conditions != null && !query.IgnoreConditions;

            foreach (string key in BaseCandidates(type, spec, query.CompanyKey))
            {
                if (checkFilter && !_world.Check(spec.filter, key)) continue;
                if (checkConditions && !_world.Check(definition.conditions, key)) continue;
                if (query.Allowed != null && !query.Allowed(key)) continue;

                float weight = weighed ? WeightOf(key, weighting) : 1f;
                if (!(weight > 0f)) continue;
                keys.Add(key);
                weights.Add(weight);
            }
        }

        /// <summary>
        /// The firing for candidates found by <see cref="Candidates"/>: the whole list for a group, otherwise the company picked by weight with
        /// <paramref name="u"/>. Null when there is no candidate (FR-3 item 6).
        /// </summary>
        public FiringPlan Pick(EventDefinition definition, IList<string> keys, IList<float> weights, float u, TargetQuery query)
        {
            if (keys == null || keys.Count == 0) return null;
            if (IsGroupFiring(definition, query)) return new FiringPlan(definition, keys, true);

            int index = WeightedPick.Index(weights, u);
            if (index < 0) return null;
            return new FiringPlan(definition, new[] { keys[index] }, false);
        }

        /// <summary><see cref="Candidates"/> and <see cref="Pick"/> together.</summary>
        public FiringPlan Select(EventDefinition definition, float u, TargetQuery query)
        {
            var keys = new List<string>();
            var weights = new List<float>();
            Candidates(definition, query, keys, weights);
            return Pick(definition, keys, weights, u, query);
        }

        // ---- helpers ----

        private IEnumerable<string> BaseCandidates(EventTargetType type, EventTargetSpec spec, string named)
        {
            if (!string.IsNullOrEmpty(named))
            {
                if (_world.IsAlive(named)) yield return named;
                yield break;
            }

            switch (type)
            {
                case EventTargetType.Player:
                    if (_world.IsAlive(CompanyKey.Player)) yield return CompanyKey.Player;
                    break;

                case EventTargetType.Specific:
                    string key = spec != null ? spec.companyKey : null;
                    if (CompanyKey.IsValid(key) && _world.IsAlive(key)) yield return key;
                    break;

                case EventTargetType.RandomAI:
                    foreach (string company in Snapshot())
                    {
                        if (CompanyKey.IsAi(company)) yield return company;
                    }
                    break;

                default:   // RandomCompany, AllCompanies
                    foreach (string company in Snapshot()) yield return company;
                    break;
            }
        }

        private IList<string> Snapshot()
        {
            IList<string> keys = _world.CompanyKeys;
            return keys ?? new string[0];
        }

        private static EventTargetType TypeOf(EventDefinition definition)
        {
            return definition.targets != null ? definition.targets.type : EventTargetType.Player;
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
