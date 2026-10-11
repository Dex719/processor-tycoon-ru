using System;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The pace of the events (FR-2 items 3, 6, 7; design.md "Планировщик"): the frequency setting, the gap and the active limit of a category, the cooldown
    /// of an event per company (per event for <c>AllCompanies</c>), <c>oncePerGame</c>, and the limit of the player's pending choices. It reads and writes
    /// <see cref="SchedulerState"/> and <see cref="EventHistory"/>; the scheduler asks it before every firing and tells it after.
    /// </summary>
    public sealed class Pacing
    {
        /// <summary>The value of the parameter <c>frequency</c> by default: "Обычно".</summary>
        public const int DefaultFrequencySetting = 2;

        private static readonly float[] FrequencyFactors = { 0f, 0.5f, 1f, 1.5f };

        private readonly SchedulerState _state;
        private readonly EventHistory _history;
        private readonly Func<EventConfig> _config;

        /// <param name="config">The merged settings of the packs (<c>maxPendingChoices</c>), read at every use: the packs are loaded after the start.</param>
        public Pacing(SchedulerState state, EventHistory history, Func<EventConfig> config)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (history == null) throw new ArgumentNullException("history");
            _state = state;
            _history = history;
            _config = config ?? (() => EventConfig.Defaults);
        }

        /// <summary>
        /// <c>F_settings</c> of the lottery (design.md, "Лотерея категории"): the parameter <c>frequency</c> 0..3 - "Выкл" 0, "Редко" 0.5, "Обычно" 1,
        /// "Часто" 1.5. A value outside the range is brought into it.
        /// </summary>
        public static float FrequencyFactor(int setting)
        {
            if (setting < 0) setting = 0;
            if (setting >= FrequencyFactors.Length) setting = FrequencyFactors.Length - 1;
            return FrequencyFactors[setting];
        }

        /// <summary>The company key of the cooldown: the company, or <c>*</c> for an <c>AllCompanies</c> event (new companies must not lift it).</summary>
        public static string CooldownKey(EventDefinition definition, string companyKey)
        {
            bool group = definition != null && definition.targets != null && definition.targets.type == EventTargetType.AllCompanies;
            return group ? EventCompanyKeys.AllCompaniesCooldown : companyKey;
        }

        /// <summary>The largest number of open choices the player may have (<c>maxPendingChoices</c>, 2 by default).</summary>
        public int MaxPendingChoices
        {
            get
            {
                EventConfig config = _config() ?? EventConfig.Defaults;
                return config.MaxPendingChoices;
            }
        }

        /// <summary>The lottery of the category may run today: <c>minGapDays</c> have passed since its last event and fewer than <c>maxActive</c> are going on.</summary>
        public bool CategoryAllows(CategoryDefinition category, int today)
        {
            if (category == null) return false;
            int last = _state.CategoryLastDay(category.Id);
            if (last != SchedulerState.NoDay && today - last < category.MinGapDays) return false;
            if (category.MaxActive >= PackConfigReader.NoLimit) return true;
            return _history.Active.ActiveInCategory(category.Id) < category.MaxActive;
        }

        /// <summary>The event is off its cooldown for the company (AC-2.2): <c>cooldownDays</c> have passed since it last fired for it (for <c>*</c>).</summary>
        public bool CooldownAllows(EventDefinition definition, string companyKey, int today)
        {
            if (definition == null) return false;
            if (definition.cooldownDays <= 0) return true;
            int last = _state.LastFired(definition.id, CooldownKey(definition, companyKey));
            return last == SchedulerState.NoDay || today - last >= definition.cooldownDays;
        }

        /// <summary>The event is not used up: a <c>oncePerGame</c> event that fired, or a <c>Date</c> trigger that fired or waits in the queue.</summary>
        public bool OnceAllows(EventDefinition definition)
        {
            return definition != null && !_state.IsUsedUp(definition.id);
        }

        /// <summary>
        /// The firing does not open one choice too many for the player (FR-2 item 6, AC-2.3): an event with choices that hits the player while the player
        /// has <see cref="MaxPendingChoices"/> open choices has to wait (the lottery skips the day, other triggers queue it).
        /// </summary>
        public bool PendingAllows(FiringPlan plan)
        {
            if (plan == null || !plan.HasChoices || !plan.IncludesPlayer) return true;
            return _history.Active.PendingChoices(CompanyKey.Player) < MaxPendingChoices;
        }

        /// <summary>
        /// Notes a firing that created its instances: the cooldown per target (or <c>*</c>), the history per target, the last day of the category, and the
        /// event as used up when it is <c>oncePerGame</c> or a <c>Date</c> trigger.
        /// </summary>
        public void RecordFiring(FiringPlan plan)
        {
            if (plan == null) return;
            EventDefinition definition = plan.Definition;
            foreach (string key in plan.Targets)
            {
                _state.SetLastFired(definition.id, CooldownKey(definition, key), plan.Today);
                _history.RecordFired(definition.id, key, plan.Today);
            }

            if (!string.IsNullOrEmpty(definition.category)) _state.SetCategoryLastDay(definition.category, plan.Today);
            if (definition.oncePerGame || IsDateTrigger(definition)) _state.MarkUsedUp(definition.id);
        }

        internal static bool IsDateTrigger(EventDefinition definition)
        {
            return definition.trigger != null && definition.trigger.type == EventTriggerType.Date;
        }
    }
}
