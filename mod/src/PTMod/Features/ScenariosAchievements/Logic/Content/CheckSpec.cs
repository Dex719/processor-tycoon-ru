using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// One check of the content: the condition of a goal, of a fail rule, of a challenge's fail rule or of an achievement (design.md,
    /// "Контент"). One class for every kind, the fields that the <see cref="Kind"/> does not use stay at their defaults. Built by
    /// <see cref="ContentMapper"/>, checked by <see cref="ContentValidator"/>, evaluated by the evaluators of the later tasks.
    /// </summary>
    public sealed class CheckSpec
    {
        /// <summary><c>metric</c>: <see cref="Metric"/> <see cref="Op"/> <see cref="Value"/> (held for <see cref="HoldDays"/> days when that is set).</summary>
        public const string KindMetric = "metric";

        /// <summary><c>cpuReleased</c>: the player released a CPU that matches <see cref="Cpu"/> (after the start of the run, not a transferred one).</summary>
        public const string KindCpuReleased = "cpuReleased";

        /// <summary><c>race</c>: the player is first to release a CPU that matches <see cref="Cpu"/>; an AI company first loses it (goals only).</summary>
        public const string KindRace = "race";

        /// <summary><c>event</c>: the internal event <see cref="Event"/> happened <see cref="Count"/> times.</summary>
        public const string KindEvent = "event";

        /// <summary><c>condition</c>: an expression of <c>ModConditions</c> (<see cref="Condition"/>).</summary>
        public const string KindCondition = "condition";

        /// <summary><c>profile</c>: a rule over the profile (<see cref="Profile"/>); achievements only.</summary>
        public const string KindProfile = "profile";

        /// <summary><c>all</c>: every check of <see cref="All"/> is true.</summary>
        public const string KindAll = "all";

        public const string CadenceDaily = "daily";
        public const string CadenceMonthly = "monthly";
        public const string CadenceEvent = "event";

        public string Kind;

        /// <summary>A metric id (<see cref="ContentVocabulary.Metrics"/>); for <see cref="KindMetric"/>.</summary>
        public string Metric;

        /// <summary>One of <see cref="ContentVocabulary.Operators"/>; for <see cref="KindMetric"/>.</summary>
        public string Op;

        public double Value;

        /// <summary>Days the condition must hold in a row before the check is true (0..3650); <see cref="KindMetric"/> and <see cref="KindCondition"/> only.</summary>
        public int HoldDays;

        /// <summary>The filter of <see cref="KindCpuReleased"/> and <see cref="KindRace"/>.</summary>
        public CpuFilter Cpu;

        /// <summary>For <see cref="KindRace"/>: losing the race loses the scenario.</summary>
        public bool FailOnLose;

        /// <summary>An internal event id (<see cref="ContentVocabulary.Events"/>); for <see cref="KindEvent"/>.</summary>
        public string Event;

        /// <summary>How many times the event must have happened (at least 1); for <see cref="KindEvent"/>.</summary>
        public int Count = 1;

        /// <summary>The expression of <see cref="KindCondition"/>.</summary>
        public ConditionRef Condition;

        /// <summary>The children of <see cref="KindAll"/>.</summary>
        public CheckSpec[] All;

        /// <summary>Extra preconditions that must hold for the check to count (any kind); null for none.</summary>
        public RequireSpec Require;

        /// <summary>When the check is evaluated: <see cref="CadenceDaily"/>, <see cref="CadenceMonthly"/> or <see cref="CadenceEvent"/>.</summary>
        public string Cadence;

        /// <summary>The rule of <see cref="KindProfile"/>.</summary>
        public ProfileSpec Profile;
    }

    /// <summary>
    /// What a released CPU has to look like to count for a <c>cpuReleased</c> or <c>race</c> check. Every criterion that is set must hold; a
    /// filter without any criterion would match every CPU (use an <c>event</c> check for that).
    /// </summary>
    public sealed class CpuFilter
    {
        /// <summary>Lowest frequency in kHz (1 000 000 = 1 GHz); 0 = ignore.</summary>
        public float MinFrequency;

        /// <summary>Fewest cores; 0 = ignore.</summary>
        public int MinCores;

        /// <summary>Highest power in watts; 0 = ignore.</summary>
        public float MaxPower;

        /// <summary>The CPU must have SMT.</summary>
        public bool RequireSmt;

        /// <summary>Company type of the player at the moment of the release (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); null = any.</summary>
        public string CompanyTypeAtRelease;

        /// <summary>The CPU must be released before this date (<c>yyyy-MM-dd</c>, the release day itself does not count); null = no limit.</summary>
        public string ReleasedBefore;

        /// <summary>At least one criterion is set.</summary>
        public bool HasCriteria
        {
            get
            {
                return MinFrequency > 0 || MinCores > 0 || MaxPower > 0 || RequireSmt
                       || !string.IsNullOrEmpty(CompanyTypeAtRelease) || !string.IsNullOrEmpty(ReleasedBefore);
            }
        }
    }

    /// <summary>
    /// A condition of <c>ModConditions</c> as the content carries it: the JSON expression (<c>{ "type": "…" }</c>, <c>all</c>/<c>any</c>/<c>not</c>).
    /// <see cref="Compiled"/> is filled by the game-side layer (<c>ModConditions.Parse</c>); the logic never reads it.
    /// </summary>
    public sealed class ConditionRef
    {
        public JsonNode Node;

        /// <summary>The parsed <c>ICondition</c> of the core, a cache of the game-side layer.</summary>
        public object Compiled;
    }

    /// <summary>Extra preconditions of a check (<c>require</c>). All that are set must hold.</summary>
    public sealed class RequireSpec
    {
        /// <summary>The difficulty of the run is at least this level (0..5); -1 = not required.</summary>
        public int DifficultyLevel = -1;

        /// <summary>The player has not gone bankrupt in this run.</summary>
        public bool NotBankrupt;

        /// <summary>The player's company type (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); null = any.</summary>
        public string CompanyType;

        /// <summary>The run has at least this many challenges chosen; 0 = not required.</summary>
        public int MinChallenges;

        public bool IsEmpty
        {
            get { return DifficultyLevel < 0 && !NotBankrupt && string.IsNullOrEmpty(CompanyType) && MinChallenges <= 0; }
        }
    }

    /// <summary>The rule of a <c>profile</c> check.</summary>
    public sealed class ProfileSpec
    {
        /// <summary>Every standard scenario of the catalog has at least <see cref="Medal"/> in the profile.</summary>
        public const string RuleAllScenarioMedals = "allScenarioMedals";

        public string Rule;

        /// <summary>One of <see cref="ContentVocabulary.Medals"/>.</summary>
        public string Medal;
    }
}
