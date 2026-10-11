using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>The run of a new game and the definitions of its challenges: what <see cref="RunStart.Create"/> makes.</summary>
    public sealed class RunPlan
    {
        public RunState Run;

        /// <summary>The definitions of the challenges of the run, in the order the player chose them (the ones the catalog knows).</summary>
        public List<ChallengeDefinition> Challenges = new List<ChallengeDefinition>();
    }

    /// <summary>
    /// The birth of the run of a new game (TSK-019, FR-4 item 2, FR-15 item 1): a <see cref="RunState"/> made from what the player chose
    /// (<see cref="PendingRun"/>), the difficulty and the cheats the game started with, the date, and the features that are switched on. The
    /// snapshot of the scenario in the run is the definition itself (the run does not follow later changes of the content, D1); a challenge the catalog
    /// does not know is left out (its id is reported to the caller for the log). Free play (no pending run) is a run with no scenario. Pure logic:
    /// the modifier records and the letters are the director's.
    /// </summary>
    public static class RunStart
    {
        /// <summary>
        /// Makes the run. <paramref name="pending"/> may be null (free play). <paramref name="findChallenge"/> looks a challenge up by id; unknown ones
        /// are dropped and their ids are added to <paramref name="skipped"/> when it is given. The run is assisted with the reason
        /// <see cref="AssistedRegistry.ReasonCheats"/> when <paramref name="cheats"/> is true.
        /// </summary>
        public static RunPlan Create(string runId, DateTime today, int difficulty, bool cheats, PendingRun pending,
                                     IEnumerable<string> featuresAtStart, Func<string, ChallengeDefinition> findChallenge,
                                     IList<string> skipped = null)
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("a run needs an id", "runId");

            var plan = new RunPlan();
            var ids = new List<string>();
            if (pending != null && findChallenge != null)
            {
                foreach (string id in pending.Challenges ?? new string[0])
                {
                    ChallengeDefinition definition = string.IsNullOrEmpty(id) ? null : findChallenge(id);
                    if (definition == null)
                    {
                        if (skipped != null) skipped.Add(id ?? string.Empty);
                        continue;
                    }
                    if (ids.Contains(definition.Id)) continue;
                    ids.Add(definition.Id);
                    plan.Challenges.Add(definition);
                }
            }

            ScenarioDefinition scenario = pending == null ? null : pending.Scenario;
            plan.Run = RunState.StartNew(runId, today, difficulty, scenario, ids, featuresAtStart);
            if (cheats) plan.Run.MarkAssisted(AssistedRegistry.ReasonCheats);
            return plan;
        }
    }
}
