using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the achievements and the profile need to know about the run in play, in plain values: the id and the scenario, whether it is a
    /// legacy run, the difficulty (at the start and now), the player's company, today's game date, the assisted flag with the setting
    /// <c>strictAssisted</c>, the challenges. The game-side director builds it fresh for a pass (the difficulty, the company and the date are live
    /// values of the game); <see cref="From"/> takes the rest from the <see cref="RunState"/>.
    /// </summary>
    public sealed class RunInfo
    {
        public string RunId;

        /// <summary>The id of the scenario of the run; null in free play.</summary>
        public string ScenarioId;

        /// <summary>The run was born from a save made without the mod (<c>origin = legacy</c>).</summary>
        public bool IsLegacy;

        /// <summary>The difficulty the run started with (0..5).</summary>
        public int DifficultyAtStart;

        /// <summary>The difficulty now (0..5).</summary>
        public int DifficultyLevel;

        /// <summary>The company type of the player now (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); null when it is not known.</summary>
        public string CompanyType;

        /// <summary>The name the player gave the company.</summary>
        public string CompanyName;

        /// <summary>Today's game date.</summary>
        public DateTime GameDate;

        public bool Assisted;

        /// <summary>The setting <c>strictAssisted</c>: an assisted run earns no achievements.</summary>
        public bool StrictAssisted;

        /// <summary>The scenario of the run was changed or added by the player's content: no records for it.</summary>
        public bool Custom;

        /// <summary>The ids of the challenges the run was started with; null for none.</summary>
        public IList<string> Challenges;

        /// <summary>How many challenges the run was started with.</summary>
        public int ChallengeCount
        {
            get { return Challenges == null ? 0 : Challenges.Count; }
        }

        /// <summary>
        /// The info of a run: the fields the state keeps (id, scenario, origin, difficulty at the start, assisted, custom, challenges) are copied;
        /// the live values come as arguments. The current difficulty defaults to the one at the start.
        /// </summary>
        public static RunInfo From(RunState run, DateTime gameDate, string companyType = null, string companyName = null,
                                   int? currentDifficulty = null, bool strictAssisted = false)
        {
            if (run == null) throw new ArgumentNullException("run");
            return new RunInfo
            {
                RunId = run.RunId,
                ScenarioId = run.ScenarioId,
                IsLegacy = run.IsLegacy,
                DifficultyAtStart = run.DifficultyAtStart,
                DifficultyLevel = currentDifficulty ?? run.DifficultyAtStart,
                CompanyType = companyType,
                CompanyName = companyName,
                GameDate = gameDate,
                Assisted = run.Assisted,
                StrictAssisted = strictAssisted,
                Custom = run.Custom,
                Challenges = new List<string>(run.Challenges),
            };
        }
    }
}
