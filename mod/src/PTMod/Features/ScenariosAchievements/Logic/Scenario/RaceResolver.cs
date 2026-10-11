using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Decides the races of a scenario (design.md, "Гонки"; FR-6): on every release of a CPU, by the player or by an AI company, after the start
    /// of the run, the open race goals are checked against it; the first company whose CPU fits the filter wins. Releases come in the order the
    /// game raises them, so two releases on one day are decided by who came first. Releases during a fast-forward or a load never reach the
    /// resolver: the caller drops them (the core raises no event while loading).
    /// </summary>
    public sealed class RaceResolver
    {
        /// <summary>
        /// Puts a release against the open races. <paramref name="raceGoals"/> are the goals of the scenario (any kind; only the <c>race</c> ones are
        /// looked at). Returns what was decided by this release - nothing when the release is older than the run, fits no open race, or the
        /// races it fits are decided already.
        /// </summary>
        public IList<RaceOutcome> OnCpuReleased(RaceBook book, IList<GoalSpec> raceGoals, CpuInfo cpu, bool isPlayer,
                                                string companyName, DateTime date)
        {
            var decided = new List<RaceOutcome>();
            if (book == null) throw new ArgumentNullException("book");
            if (raceGoals == null || cpu == null) return decided;
            if (book.StartDate != default(DateTime) && date.Date < book.StartDate.Date) return decided;

            for (int i = 0; i < raceGoals.Count; i++)
            {
                GoalSpec goal = raceGoals[i];
                if (goal == null || goal.Check == null || goal.Check.Kind != CheckSpec.KindRace || string.IsNullOrEmpty(goal.Id)) continue;
                if (book.StatusOf(goal.Id) != RaceStatus.Open) continue;
                if (!CpuMatcher.Matches(goal.Check.Cpu, cpu, date)) continue;

                var entry = new RaceEntry
                {
                    GoalId = goal.Id,
                    Status = isPlayer ? RaceStatus.Won : RaceStatus.Lost,
                    WinnerName = companyName,
                    WonOn = date.Date,
                };
                book.Entries[goal.Id] = entry;

                decided.Add(new RaceOutcome
                {
                    GoalId = goal.Id,
                    PlayerWon = isPlayer,
                    FailOnLose = goal.Check.FailOnLose,
                    WinnerName = companyName,
                    Date = date.Date,
                });
            }
            return decided;
        }
    }
}
