using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>Where a goal stands: not decided yet, done, or failed (its <c>by</c> date passed, or a race was lost).</summary>
    public enum GoalStatus
    {
        Open,
        Done,
        Failed,
    }

    /// <summary>
    /// The state of one goal of a run (the <c>goals</c> block of the save section): the status, the day it was decided, and for a goal that must
    /// hold for some days (<c>holdDays</c>) how many days in a row it has held. Plain fields for the save codec.
    /// </summary>
    public sealed class GoalState
    {
        public GoalStatus Status = GoalStatus.Open;

        /// <summary>The day the goal was done; null while it is not.</summary>
        public DateTime? DoneOn;

        /// <summary>The day the goal was failed; null while it is not. Not in the example of the design: the codec may leave it out.</summary>
        public DateTime? FailedOn;

        /// <summary>Days in a row the condition has held (only a goal with <c>holdDays</c> uses it).</summary>
        public int HoldDays;
    }
}
