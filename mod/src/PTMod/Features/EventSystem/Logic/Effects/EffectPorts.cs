using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the engine hands to an effect while it is resolved, applied, ticked, removed or restored: the game-free part of <c>EventContext</c> (D1, D5). In the
    /// game the adapter of a pack's handler turns it into an <c>EventContext</c> (with the <c>ICompany</c> and the game facade); the standard effects read it as
    /// it is. Valid only for the duration of the call.
    /// </summary>
    public sealed class EffectCall
    {
        /// <summary>The event; null for a <c>Direct</c> instance (<c>EventEffects.ApplyDirect</c>).</summary>
        public EventDefinition Definition;

        /// <summary>The instance the effect belongs to.</summary>
        public EventInstanceDto Instance;

        /// <summary>The effect as the definition writes it; null when the engine no longer has it (a restore after a load). Words only, never numbers after <c>Resolve</c>.</summary>
        public EventEffect Spec;

        /// <summary><c>player</c>, <c>ai:&lt;UniqueID&gt;</c> or <c>global</c> (a global-scope effect).</summary>
        public string CompanyKey;

        /// <summary>Today in days from 1971-01-01.</summary>
        public int Today;

        /// <summary>The generator derived for the instance (D5); a handler takes the same count of numbers whatever the data.</summary>
        public IModRng Rng;

        /// <summary><c>PackOptions.Severity</c> for an effect with <c>params.scalable = true</c>, 1 otherwise (D15).</summary>
        public float Severity = 1f;

        /// <summary>The aggregator of modifier contributions (limits of the channels, the contributions of timed effects).</summary>
        public IEffectModifiers Contributions;

        /// <summary>The effect's company is the whole market.</summary>
        public bool IsGlobal
        {
            get { return string.Equals(CompanyKey, EventCompanyKeys.Global, StringComparison.Ordinal); }
        }
    }

    /// <summary>
    /// One effect type as the engine's runtime calls it: the order and the meaning of <c>IEventEffectHandler</c> / <c>ITimedEffectHandler</c>, over
    /// <see cref="EffectCall"/> instead of the game-bound <c>EventContext</c>. The standard types implement it directly (<c>Logic/Effects/Handlers</c>); a pack's
    /// <c>IEventEffectHandler</c> is wrapped by the game side. Every call is made by the engine inside a try/catch.
    /// </summary>
    public interface IEffectHandlerPort
    {
        /// <summary>The type name as JSON writes it.</summary>
        string Type { get; }

        /// <summary>The effect lasts (<c>ITimedEffectHandler</c>): it is kept active until its <c>endDay</c>, ticked, removed and restored.</summary>
        bool IsTimed { get; }

        /// <summary>All rolls and formulas, once (D1). The engine fills the id, the company, the chance, the state and the text afterwards.</summary>
        AppliedEffect Resolve(EventEffect spec, EffectCall call);

        /// <summary>The one-off action or the start of a timed effect, from the saved numbers. Setting <c>effect.state</c> to <c>Skipped</c> says "nothing to apply it to".</summary>
        void Apply(AppliedEffect effect, EffectCall call);

        /// <summary>The line of the effect from its saved numbers only.</summary>
        EffectText Describe(AppliedEffect effect);

        /// <summary>Every day while the effect is active (curves).</summary>
        void Tick(AppliedEffect effect, EffectCall call);

        /// <summary>The effect ends: undo <see cref="Apply"/>.</summary>
        void Remove(AppliedEffect effect, EffectCall call);

        /// <summary>After a load (or when the effect's pack is switched on again): put the effect back from its saved numbers.</summary>
        void Restore(AppliedEffect effect, EffectCall call);
    }

    /// <summary>The effect types the runtime can use: the registered handlers by type name.</summary>
    public interface IEffectTable
    {
        /// <summary>The handler of the type, or null when no handler is registered.</summary>
        IEffectHandlerPort Get(string type);
    }

    /// <summary>
    /// What the standard one-off effects change in the game, by company key (the game side implements it over <c>IGameFacade</c>, <c>ModReputation</c> and
    /// <c>ModMail</c>; the tests give a recorder). The key <c>global</c> means every company of the market where that makes sense.
    /// </summary>
    public interface IEffectWorld
    {
        /// <summary><c>InflationHandler.Instance.Inflation</c> (1 when unknown).</summary>
        float Inflation { get; }

        /// <summary>Revenue of the last three monthly reports (0 while there are fewer than three).</summary>
        float QuarterlyRevenue(string companyKey);

        /// <summary><c>ICompany.SpendMoney(amount, origin)</c>; <paramref name="origin"/> is a name of <c>ExpenseOrigin</c>. False when the company is not there.</summary>
        bool Spend(string companyKey, float amount, string origin);

        /// <summary><c>ICompany.EarnMoney(amount, origin)</c>; <paramref name="origin"/> is a name of <c>RevenueOrigin</c>. False when the company is not there.</summary>
        bool Earn(string companyKey, float amount, string origin);

        /// <summary>
        /// Adds <paramref name="delta"/> to <c>CpuBase.Popularities</c> of the company's CPUs on the market (<c>AllMarket</c>) or of its newest one (<c>Newest</c>),
        /// in one segment or in all (<see cref="EffectMath.Popularity"/> keeps each value in [0.05; 1]). Returns how many CPUs changed (0 - none on the market).
        /// </summary>
        int AddPopularity(string companyKey, float delta, string cpuSelector, string segment);

        /// <summary>Moves the company's current research by <paramref name="delta"/> (<see cref="EffectMath.ResearchProgress"/>); false when it researches nothing.</summary>
        bool AddResearchProgress(string companyKey, float delta);

        /// <summary><c>ModReputation.Add(company, delta, reasonKey)</c>; false when the company is not there.</summary>
        bool AddReputation(string companyKey, float delta, string reasonKey);

        /// <summary>An information mail to the player (<c>ModMail.Send</c>, no choices, priority <c>Normal</c>); the mail id, or -1 when the mail was refused.</summary>
        int SendMail(string titleKey, string textKey, string tag, string[] args);
    }

    /// <summary>What a <c>FireEvent</c> effect does to the scheduler and to the memory of the predicates (<see cref="ScheduledFires"/> in the engine).</summary>
    public interface IChainHost
    {
        /// <summary>Puts a firing into the delayed firings (<c>scheduled</c>), due on <paramref name="dueDay"/>; a chain skips the conditions of the event.</summary>
        void Schedule(string eventId, int dueDay, string companyKey, int parentId, string[] args, int depth);

        /// <summary>Sets a flag of a chain for the company (<c>es.eventFlag</c>).</summary>
        void SetFlag(string flag, string companyKey, int day);
    }

    /// <summary>
    /// The table of effect types by name (<c>EventEffects.Register</c>, AC-6.1): the first registration of a type wins, a second one is refused with a warning
    /// (FR-6 item 5). The order of registration is kept for <c>EventEffects.RegisteredTypes</c>.
    /// </summary>
    public sealed class TypeTable<T> where T : class
    {
        private readonly Dictionary<string, T> _handlers = new Dictionary<string, T>(StringComparer.Ordinal);
        private readonly List<string> _types = new List<string>();

        /// <summary>Registers <paramref name="handler"/> for <paramref name="type"/>. False, and a line to <paramref name="warn"/>, when the type is taken or the call is empty.</summary>
        public bool Register(string type, T handler, Action<string> warn)
        {
            if (string.IsNullOrEmpty(type) || handler == null)
            {
                if (warn != null) warn("api: EventEffects.Register ignored, the type or the handler is missing");
                return false;
            }

            T first;
            if (_handlers.TryGetValue(type, out first))
            {
                if (warn != null)
                {
                    warn("api: effect type '" + type + "' is registered already by " + first.GetType().FullName + "; the second handler "
                         + handler.GetType().FullName + " is ignored");
                }
                return false;
            }

            _handlers.Add(type, handler);
            _types.Add(type);
            return true;
        }

        /// <summary>The handler of the type, or null.</summary>
        public T Get(string type)
        {
            T handler;
            return type != null && _handlers.TryGetValue(type, out handler) ? handler : null;
        }

        public bool Contains(string type)
        {
            return type != null && _handlers.ContainsKey(type);
        }

        /// <summary>The registered types in the order of registration (a copy).</summary>
        public IList<string> Types
        {
            get { return new List<string>(_types).AsReadOnly(); }
        }
    }
}
