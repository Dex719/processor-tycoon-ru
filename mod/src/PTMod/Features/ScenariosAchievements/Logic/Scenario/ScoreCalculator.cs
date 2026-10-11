using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>The parts of a score: the points of the goals, the bonus for finishing early, the bonus of the challenges, the total.</summary>
    public sealed class ScoreBreakdown
    {
        /// <summary>The sum of the points of the goals that were done, required and optional.</summary>
        public int GoalPoints;

        /// <summary>The bonus for finishing early; 0 for a loss.</summary>
        public int TimeBonus;

        /// <summary>Days from the day the scenario ended to the deadline (0 when it ended later).</summary>
        public int DaysLeft;

        /// <summary>Days from the start to the deadline.</summary>
        public int TotalDays;

        /// <summary>The bonus of the chosen challenges in per cent (15 + 10 = 25).</summary>
        public int ChallengeBonusPercent;

        /// <summary>The score: <c>round((GoalPoints + TimeBonus) × (1 + bonus))</c>; 0 for a loss.</summary>
        public int Score;
    }

    /// <summary>
    /// The score and the medal (design.md, "Счёт"; FR-8):
    /// <c>timeBonus = round(timeBonusMax × daysLeft / totalDays)</c>, <c>score = round((Σ points of done goals + timeBonus) × (1 + Σ challenge bonuses))</c>,
    /// the bonuses of the challenges add up (15% and 10% make ×1.25), a loss is 0. The medal is the best threshold the score reaches.
    /// The arithmetic is done in <c>decimal</c> and rounds halves away from zero, so a bonus of 0.15f does not turn 0.14999999 into a different score.
    /// </summary>
    public sealed class ScoreCalculator
    {
        /// <summary>The medal of a lost scenario (the text <c>scn.medal.none</c>).</summary>
        public const string MedalNone = "none";

        /// <summary>
        /// Computes the score of the scenario as it stands: for a lost one 0 (the points of the goals are still listed), otherwise as if it ended
        /// on <paramref name="finishedOn"/> - so the same call answers "what would I get now" while the scenario is going.
        /// </summary>
        public ScoreBreakdown Compute(ScenarioDefinition s, ScenarioState st, DateTime finishedOn, IList<ChallengeDefinition> challenges)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (st == null) throw new ArgumentNullException("st");

            var breakdown = new ScoreBreakdown();
            int goalPoints = 0;
            if (s.Goals != null)
            {
                foreach (GoalSpec goal in s.Goals)
                {
                    if (goal == null) continue;
                    GoalState gs = st.FindGoal(goal.Id);
                    if (gs != null && gs.Status == GoalStatus.Done) goalPoints += goal.Points;
                }
            }
            breakdown.GoalPoints = goalPoints;

            DateTime deadline;
            DateTime start = st.StartDate.Date;
            if (s.TryGetDeadline(out deadline))
            {
                breakdown.TotalDays = Math.Max(0, (deadline.Date - start).Days);
                breakdown.DaysLeft = Math.Max(0, (deadline.Date - finishedOn.Date).Days);
                if (breakdown.DaysLeft > breakdown.TotalDays) breakdown.DaysLeft = breakdown.TotalDays;
            }

            decimal bonus = ChallengeBonus(challenges);
            breakdown.ChallengeBonusPercent = (int)Math.Round(bonus * 100m, MidpointRounding.AwayFromZero);

            if (st.Status == ScenarioStatus.Lost)
            {
                breakdown.TimeBonus = 0;
                breakdown.Score = 0;
                return breakdown;
            }

            if (s.TimeBonusMax > 0 && breakdown.TotalDays > 0)
            {
                decimal raw = (decimal)s.TimeBonusMax * breakdown.DaysLeft / breakdown.TotalDays;
                breakdown.TimeBonus = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            }

            decimal score = (breakdown.GoalPoints + breakdown.TimeBonus) * (1m + bonus);
            breakdown.Score = (int)Math.Round(score, MidpointRounding.AwayFromZero);
            return breakdown;
        }

        /// <summary><c>gold</c> from the gold threshold up, <c>silver</c>, <c>bronze</c>, otherwise <c>completed</c> (a win below the bronze threshold).</summary>
        public string Medal(ScenarioDefinition s, int score)
        {
            if (s == null) throw new ArgumentNullException("s");
            MedalThresholds medals = s.Medals;
            if (medals == null) return "completed";
            if (score >= medals.Gold) return "gold";
            if (score >= medals.Silver) return "silver";
            if (score >= medals.Bronze) return "bronze";
            return "completed";
        }

        /// <summary>The bonus of the chosen challenges in whole per cent, for the window of the new game ("Бонус к счёту: +25%").</summary>
        public static int BonusPercent(IList<ChallengeDefinition> challenges)
        {
            return (int)Math.Round(ChallengeBonus(challenges) * 100m, MidpointRounding.AwayFromZero);
        }

        // The bonuses add up; a challenge listed twice counts once.
        private static decimal ChallengeBonus(IList<ChallengeDefinition> challenges)
        {
            decimal sum = 0m;
            if (challenges == null) return sum;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ChallengeDefinition challenge in challenges)
            {
                if (challenge == null) continue;
                if (challenge.Id != null && !seen.Add(challenge.Id)) continue;
                sum += (decimal)challenge.ScoreBonus;
            }
            return sum;
        }
    }
}
