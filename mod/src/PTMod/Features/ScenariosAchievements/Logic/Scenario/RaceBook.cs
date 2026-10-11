using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>How a race stands: still on, won by the player, or lost to an AI company.</summary>
    public enum RaceStatus
    {
        Open,
        Won,
        Lost,
    }

    /// <summary>The result of a decided race: who won it and when. The <c>races</c> block of the save section holds these by goal id.</summary>
    public sealed class RaceEntry
    {
        public string GoalId;

        public RaceStatus Status = RaceStatus.Open;

        /// <summary>The name of the winning company as the game shows it (the player's company name when the player won).</summary>
        public string WinnerName;

        /// <summary>The game day the winning CPU was released.</summary>
        public DateTime? WonOn;
    }

    /// <summary>
    /// The races of a run, by goal id (design.md, "Гонки"; FR-6). A race that is not in the book is open; once decided it stays decided.
    /// <see cref="StartDate"/> is the first day of the run: a release dated before it is not part of the race.
    /// </summary>
    public sealed class RaceBook
    {
        /// <summary>The decided races by goal id.</summary>
        public readonly Dictionary<string, RaceEntry> Entries = new Dictionary<string, RaceEntry>(StringComparer.Ordinal);

        /// <summary>The first day of the run; <c>default</c> (no limit) until it is set.</summary>
        public DateTime StartDate;

        public RaceBook()
        {
        }

        public RaceBook(DateTime startDate)
        {
            StartDate = startDate;
        }

        /// <summary>The entry of a goal, or null when the race is still open.</summary>
        public RaceEntry Get(string goalId)
        {
            RaceEntry entry;
            return goalId != null && Entries.TryGetValue(goalId, out entry) ? entry : null;
        }

        /// <summary>The status of the race of a goal (<see cref="RaceStatus.Open"/> when it is not in the book).</summary>
        public RaceStatus StatusOf(string goalId)
        {
            RaceEntry entry = Get(goalId);
            return entry == null ? RaceStatus.Open : entry.Status;
        }
    }

    /// <summary>What <see cref="RaceResolver"/> decided with one release: which race, who won, and whether losing it loses the scenario.</summary>
    public sealed class RaceOutcome
    {
        public string GoalId;

        /// <summary>The player was first.</summary>
        public bool PlayerWon;

        /// <summary>The goal asks for the scenario to be lost when the race is lost (<see cref="CheckSpec.FailOnLose"/>).</summary>
        public bool FailOnLose;

        /// <summary>The name of the company that was first.</summary>
        public string WinnerName;

        public DateTime Date;
    }
}
