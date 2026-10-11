using System;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// A scenario as the content describes it (design.md, "Контент"; FR-1). Built from a JSON record by <see cref="ContentMapper"/>; the
    /// numbers live in <c>scenarios.json</c>, not in the code (NFR-8). The fields are plain: the validator, not the type, keeps them legal.
    /// </summary>
    public sealed class ScenarioDefinition
    {
        /// <summary><c>[a-z0-9-]{3,40}</c>, unique; the key of an override in <c>scenarios.json</c>.</summary>
        public string Id;

        /// <summary>Position in the list of the new-game window (smaller first).</summary>
        public int Order;

        /// <summary>L10n keys of the title, of the description (at most 6 lines) and of the briefing.</summary>
        public string TitleKey, DescKey, BriefingKey;

        public ScenarioStart Start;

        /// <summary>The tutorial the scenario asks for: <c>allowed</c>, <c>forcedOn</c> or <c>forcedOff</c> (<see cref="ContentVocabulary.OnboardingModes"/>).</summary>
        public string Onboarding = "allowed";

        /// <summary>Modifiers added once when the run starts (standard channels of <c>ModModifiers</c> only).</summary>
        public ModifierSpec[] Modifiers = new ModifierSpec[0];

        /// <summary>The last day of the scenario, <c>yyyy-MM-dd</c>, after the first day of <see cref="ScenarioStart.Year"/>.</summary>
        public string Deadline;

        /// <summary>The most points an early win adds (0..2000), scaled by the share of time left.</summary>
        public int TimeBonusMax;

        public GoalSpec[] Goals = new GoalSpec[0];

        /// <summary>Rules that lose the scenario besides the deadline.</summary>
        public FailSpec[] Fail = new FailSpec[0];

        public MedalThresholds Medals;

        /// <summary>Ids of the challenges that may be chosen with this scenario.</summary>
        public string[] AllowedChallenges = new string[0];

        /// <summary>Computed on load (first 8 hex digits of the SHA-1 of the canonical JSON of the record), never written in the file.</summary>
        public string ContentHash;

        /// <summary>The record differs from the built-in one with the same id, or there is no built-in one (an override). Computed on load, not in the file.</summary>
        public bool IsCustom;

        /// <summary>
        /// A copy of the JSON record this definition was mapped from: the snapshot that the run keeps in the save (FR-1 item 4) and the input of
        /// <see cref="ContentHash"/>. Null for a definition built by hand.
        /// </summary>
        public JsonObject Source;

        /// <summary>The first day of the scenario: 1 January of <see cref="ScenarioStart.Year"/>; false when the year is not a calendar year.</summary>
        public bool TryGetStartDate(out DateTime date)
        {
            date = default(DateTime);
            if (Start == null || Start.Year < 1 || Start.Year > 9999) return false;
            date = new DateTime(Start.Year, 1, 1);
            return true;
        }

        /// <summary>The last day of the scenario; false when <see cref="Deadline"/> is not a <c>yyyy-MM-dd</c> date.</summary>
        public bool TryGetDeadline(out DateTime date)
        {
            return ContentVocabulary.TryParseDate(Deadline, out date);
        }

        /// <summary>The goal with this id, or null.</summary>
        public GoalSpec FindGoal(string goalId)
        {
            if (Goals == null) return null;
            foreach (GoalSpec goal in Goals)
            {
                if (goal != null && string.Equals(goal.Id, goalId, StringComparison.Ordinal)) return goal;
            }
            return null;
        }

        /// <summary>Sum of the points of the required goals (R of the medal rule).</summary>
        public int RequiredPoints()
        {
            return SumPoints(true, false);
        }

        /// <summary>Sum of the points of the optional goals.</summary>
        public int OptionalPoints()
        {
            return SumPoints(false, true);
        }

        /// <summary>The most a run can score before the bonus of challenges: all goals plus the whole <see cref="TimeBonusMax"/>.</summary>
        public int MaxScoreWithoutChallenges()
        {
            return SumPoints(true, true) + TimeBonusMax;
        }

        private int SumPoints(bool required, bool optional)
        {
            int sum = 0;
            if (Goals == null) return sum;
            foreach (GoalSpec goal in Goals)
            {
                if (goal == null) continue;
                if (goal.Required ? required : optional) sum += goal.Points;
            }
            return sum;
        }
    }

    /// <summary>The parameters of the start, written to <c>PlayerInitialData</c> in <c>ModNewGame.BeforeStart</c> (FR-4 item 1).</summary>
    public sealed class ScenarioStart
    {
        /// <summary>1975, 1980, 1985, 1990, 1995 or 2000 (the years of the menu).</summary>
        public int Year;

        /// <summary><c>idm</c>, <c>fabless</c> or <c>foundry</c>; <see cref="ContentVocabulary.CompanyTypeCode"/> gives the game's enum value.</summary>
        public string CompanyType;

        /// <summary>0 (Very Easy) .. 5 (Impossible); 5 only with <c>idm</c>.</summary>
        public int DifficultyLevel;

        /// <summary>The game's <c>StartingTechnology</c> value, 0..4.</summary>
        public int StartingTechnology;

        /// <summary>Money at the start, -1e9..1e11 (a negative number is a debt).</summary>
        public float StartingFunds;

        /// <summary>Production lines, 0..1000 (0 for <c>fabless</c>).</summary>
        public int FactorySize;

        public bool AllowCheats;

        /// <summary>0 = not used; else a year (1971..2030) whose technologies are unlocked for the player after the start (FR-4 item 3).</summary>
        public int TechYear;
    }

    /// <summary>One goal of a scenario.</summary>
    public sealed class GoalSpec
    {
        /// <summary><c>[a-z0-9-]{1,40}</c>, unique inside the scenario.</summary>
        public string Id;

        /// <summary>The goal must be done to win.</summary>
        public bool Required;

        /// <summary>Points for the goal, 0..10 000.</summary>
        public int Points;

        public string TitleKey;

        public CheckSpec Check;

        /// <summary>Optional intermediate deadline, <c>yyyy-MM-dd</c>: a goal not done by this date is failed. After the start, not after the deadline.</summary>
        public string By;

        /// <summary>What the tracker shows as progress; null = no progress line.</summary>
        public ProgressSpec Progress;
    }

    /// <summary>The progress line of a goal: a metric against a target, shown in a format.</summary>
    public sealed class ProgressSpec
    {
        public string Metric;

        public double Target;

        /// <summary><c>percent</c>, <c>money</c> or <c>count</c> (<see cref="ContentVocabulary.ProgressFormats"/>).</summary>
        public string Format;
    }

    /// <summary>A rule that loses a scenario (or fails a challenge) when its check becomes true.</summary>
    public sealed class FailSpec
    {
        /// <summary><c>[a-z0-9-]{1,40}</c>, unique inside the scenario or the challenge.</summary>
        public string Id;

        /// <summary>L10n key of the reason shown to the player.</summary>
        public string TextKey;

        public CheckSpec Check;
    }

    /// <summary>A modifier added through a standard channel of <c>ModModifiers</c>.</summary>
    public sealed class ModifierSpec
    {
        public string Channel;

        /// <summary>0.1..10.</summary>
        public float Multiplier;

        /// <summary><c>player</c>, <c>ai</c> or <c>all</c> (<see cref="ContentVocabulary.Scopes"/>); maps to <c>ModifierScope.Player</c>, <c>AIOnly</c>, <c>AllCompanies</c>.</summary>
        public string AppliesTo = "player";
    }

    /// <summary>Score needed for each medal; strictly increasing.</summary>
    public sealed class MedalThresholds
    {
        public int Bronze, Silver, Gold;
    }
}
