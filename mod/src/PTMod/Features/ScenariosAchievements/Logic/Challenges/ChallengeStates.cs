using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>Where a challenge of a run stands: going, failed (a fail rule held), or passed (the years were played through).</summary>
    public enum ChallengeStatus
    {
        Active,
        Failed,
        Passed,
    }

    /// <summary>
    /// One challenge of a run (an item of <c>run.challengeStates</c>): its status, the day it failed or passed, the ids of the records it added
    /// to <c>ModModifiers</c> (what <c>ModModifiers.Remove</c> takes when it fails in free play) and the hold counters of its fail rules.
    /// </summary>
    public sealed class ChallengeState
    {
        public string Id;

        public ChallengeStatus Status = ChallengeStatus.Active;

        public DateTime? FailedOn;

        /// <summary>The day the challenge was passed. Not in the example of the design: the codec may leave it out.</summary>
        public DateTime? PassedOn;

        /// <summary>The ids of the <c>ModModifiers</c> records the challenge added; emptied once they were handed out for removal.</summary>
        public List<string> EntryIds = new List<string>();

        public FailState Fail = new FailState();
    }

    /// <summary>
    /// The challenges of a run (<c>run.challengeStates</c>) by id, and the day they all started (the clock of the years). Plain data for the
    /// save codec; <see cref="ChallengeRuntime"/> changes it.
    /// </summary>
    public sealed class ChallengeStates
    {
        public readonly Dictionary<string, ChallengeState> Items = new Dictionary<string, ChallengeState>(StringComparer.Ordinal);

        /// <summary>The first day of the run: a challenge is passed when <c>challengeYears</c> have gone by since.</summary>
        public DateTime StartDate;

        public ChallengeStates()
        {
        }

        public ChallengeStates(DateTime startDate)
        {
            StartDate = startDate.Date;
        }

        /// <summary>Starts a challenge: active, with the ids of the records it added.</summary>
        public ChallengeState Add(string challengeId, IEnumerable<string> entryIds)
        {
            if (challengeId == null) throw new ArgumentNullException("challengeId");
            var state = new ChallengeState { Id = challengeId };
            if (entryIds != null) state.EntryIds.AddRange(entryIds);
            Items[challengeId] = state;
            return state;
        }

        /// <summary>The state of a challenge, or null when the run has none by that id.</summary>
        public ChallengeState Get(string challengeId)
        {
            ChallengeState state;
            return challengeId != null && Items.TryGetValue(challengeId, out state) ? state : null;
        }
    }
}
