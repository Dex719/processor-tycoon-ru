using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>Where a run came from: started as a new game by the mod (a scenario, challenges or free play), or born in memory from a save the mod knew nothing about.</summary>
    public enum RunOrigin
    {
        /// <summary>The run started with the new game (<c>origin = newGame</c> in the save); its whole history is known.</summary>
        NewGame,

        /// <summary>The run was made on loading a save without the section of the feature (<c>origin = legacy</c>): no scenario, the history before the load is unknown.</summary>
        Legacy,
    }

    /// <summary>
    /// The state of the run in play - everything the feature keeps in the section <c>scenarios-achievements</c> of the side save file (design.md,
    /// "Секция ModSave"; FR-16): who and when started it, the assisted flag with its reasons, the features and the challenges of the start, the
    /// snapshot of the scenario with its goals, races and fail rules, the ids of the modifier records and of the letters, the accumulators of the
    /// metrics and the achievements earned in this run. Plain data: <see cref="RunSaveCodec"/> turns it into the section and back, the
    /// <c>RunDirector</c> changes it. A free-play run has no scenario (<see cref="Scenario"/> and <see cref="ScenarioState"/> are null).
    /// <para>
    /// The assisted flag is sticky: it can be raised (<see cref="MarkAssisted"/>) and never lowered; the codec cannot read a run as
    /// not assisted when it has reasons.
    /// </para>
    /// </summary>
    public sealed class RunState
    {
        /// <summary>The longest reason of the assisted flag (design.md, "Security"); a longer one is cut.</summary>
        public const int MaxReasonLength = 64;

        /// <summary>The most reasons a run keeps; the flag is raised by the first one, a flood of reasons cannot grow the save.</summary>
        public const int MaxReasons = 32;

        /// <summary>The reason of a mark without any text.</summary>
        public const string ReasonUnspecified = "unspecified";

        private readonly List<string> _assistedReasons = new List<string>();

        /// <summary>The id of the run (a GUID text): what the profile stores with an achievement or a record.</summary>
        public string RunId;

        public RunOrigin Origin = RunOrigin.NewGame;

        /// <summary>The first game day of the run (for a legacy run: the day of the load).</summary>
        public DateTime StartDate;

        /// <summary>The difficulty the run started with (0..5).</summary>
        public int DifficultyAtStart;

        /// <summary>The run is assisted ("with help"): cheats were on, a feature was switched off, or another feature said so. Sticky.</summary>
        public bool Assisted { get; private set; }

        /// <summary>Why the run is assisted, without duplicates, each at most <see cref="MaxReasonLength"/> long.</summary>
        public IList<string> AssistedReasons
        {
            get { return _assistedReasons.AsReadOnly(); }
        }

        /// <summary>The scenario of the run was changed or added by the player's content (the hash differs from the built-in one): its results do not make records.</summary>
        public bool Custom;

        /// <summary>The ids of the features that were switched on when the run started: the base of the rule <c>feature.disabled:&lt;id&gt;</c>.</summary>
        public List<string> FeaturesAtStart = new List<string>();

        /// <summary>The ids of the challenges the run was started with.</summary>
        public List<string> Challenges = new List<string>();

        /// <summary>The state of every challenge, and the day they started.</summary>
        public ChallengeStates ChallengeStates = new ChallengeStates();

        /// <summary>The ids of the <c>ModModifiers</c> records the scenario added at the start (what <c>Remove</c> takes).</summary>
        public List<string> ScenarioEntryIds = new List<string>();

        /// <summary>The id of the letter with the briefing that <c>ModMail.Send</c> returned; 0 - not sent.</summary>
        public int BriefingMailId;

        /// <summary>The id of the letter with the result; 0 - not sent.</summary>
        public int ResultMailId;

        /// <summary>The snapshot of the definition of the scenario as it was at the start (the run does not follow later changes of the content); null for free play.</summary>
        public ScenarioDefinition Scenario;

        /// <summary>The state of the scenario of the run; null for free play.</summary>
        public ScenarioState ScenarioState;

        /// <summary>The accumulators of the metrics, the counters of events and the CPUs that came by a transfer.</summary>
        public MetricsState Metrics = new MetricsState();

        /// <summary>The achievements earned since the run started.</summary>
        public List<string> AchievementsThisRun = new List<string>();

        public bool IsLegacy
        {
            get { return Origin == RunOrigin.Legacy; }
        }

        /// <summary>The run has a scenario (a snapshot and a state).</summary>
        public bool HasScenario
        {
            get { return Scenario != null && ScenarioState != null; }
        }

        /// <summary>The id of the scenario, or null in free play.</summary>
        public string ScenarioId
        {
            get { return Scenario == null ? null : Scenario.Id; }
        }

        /// <summary>The scenario is going (not won, not lost).</summary>
        public bool IsScenarioActive
        {
            get { return HasScenario && ScenarioState.Status == ScenarioStatus.Active; }
        }

        /// <summary>At least one challenge of the run is still going.</summary>
        public bool HasActiveChallenge
        {
            get
            {
                if (ChallengeStates == null) return false;
                foreach (ChallengeState state in ChallengeStates.Items.Values)
                {
                    if (state != null && state.Status == ChallengeStatus.Active) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Raises the assisted flag with a reason (design.md, "AssistedRegistry"). A blank reason becomes <see cref="ReasonUnspecified"/>, a long one
        /// is cut to <see cref="MaxReasonLength"/>, a reason that is already there is not added again, and past <see cref="MaxReasons"/> reasons
        /// only the flag stays. Returns true when something changed (the flag was raised or a new reason was kept).
        /// </summary>
        public bool MarkAssisted(string reason)
        {
            string normalized = NormalizeReason(reason);
            bool changed = !Assisted;
            Assisted = true;

            if (!_assistedReasons.Contains(normalized) && _assistedReasons.Count < MaxReasons)
            {
                _assistedReasons.Add(normalized);
                changed = true;
            }
            return changed;
        }

        /// <summary>Restores the flag from a save: the saved flag or any reason makes the run assisted (a damaged save cannot lower it).</summary>
        public void RestoreAssisted(bool assisted, IEnumerable<string> reasons)
        {
            if (reasons != null)
            {
                foreach (string reason in reasons)
                {
                    if (!string.IsNullOrWhiteSpace(reason)) MarkAssisted(reason);
                }
            }
            if (assisted) Assisted = true;
        }

        /// <summary>The reason as the run keeps it: trimmed, <see cref="ReasonUnspecified"/> when blank, cut to <see cref="MaxReasonLength"/>.</summary>
        public static string NormalizeReason(string reason)
        {
            string text = (reason ?? string.Empty).Trim();
            if (text.Length == 0) return ReasonUnspecified;
            return text.Length <= MaxReasonLength ? text : text.Substring(0, MaxReasonLength);
        }

        /// <summary>
        /// A new run of a new game: the start day, the difficulty, the scenario (its snapshot and a fresh state; null for free play), the
        /// challenges and the features that are on. Nothing is assisted yet and no modifier record or letter exists.
        /// </summary>
        public static RunState StartNew(string runId, DateTime startDate, int difficulty, ScenarioDefinition scenario,
                                        IEnumerable<string> challenges, IEnumerable<string> featuresAtStart)
        {
            DateTime start = startDate.Date;
            var run = new RunState
            {
                RunId = runId,
                Origin = RunOrigin.NewGame,
                StartDate = start,
                DifficultyAtStart = difficulty,
                ChallengeStates = new ChallengeStates(start),
            };
            if (challenges != null) run.Challenges.AddRange(challenges);
            if (featuresAtStart != null) run.FeaturesAtStart.AddRange(featuresAtStart);
            if (scenario != null)
            {
                run.Scenario = scenario;
                run.ScenarioState = ScenarioState.Start(scenario, start);
                run.Custom = scenario.IsCustom;
            }
            return run;
        }

        /// <summary>
        /// The run of a save made without the mod: no scenario, the day of the load as the start, assisted when cheats are on in that game
        /// (FR-16 item 3). Its achievements that need the history of the whole game are not counted (<c>countsOnlyNewGames</c>).
        /// </summary>
        public static RunState CreateLegacy(string runId, DateTime today, int difficulty, bool cheatsEnabled, IEnumerable<string> featuresAtStart)
        {
            DateTime start = today.Date;
            var run = new RunState
            {
                RunId = runId,
                Origin = RunOrigin.Legacy,
                StartDate = start,
                DifficultyAtStart = difficulty,
                ChallengeStates = new ChallengeStates(start),
            };
            if (featuresAtStart != null) run.FeaturesAtStart.AddRange(featuresAtStart);
            if (cheatsEnabled) run.MarkAssisted(AssistedRegistry.ReasonCheats);
            return run;
        }
    }
}
