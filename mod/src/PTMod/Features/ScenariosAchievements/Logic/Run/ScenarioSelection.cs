using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The choice the player has made so far in the new-game window (TSK-021, FR-3): the scenario ("Free play" is no scenario) and the challenges.
    /// The section on the panel "Mods..." changes it, the window of the challenges (TSK-022) changes the challenges, and
    /// <c>ScenarioStarter.OnBeforeStart</c> turns it into a <see cref="PendingRun"/> when "Start" is pressed. Plain data and a few rules, no
    /// game types: a scenario is never chosen twice over another, a challenge is chosen at most once, and a challenge the new choice forbids is
    /// taken off (<see cref="Revalidate"/>).
    /// </summary>
    public sealed class ScenarioSelection
    {
        private readonly List<string> _challenges = new List<string>();

        /// <summary>The id of the chosen scenario; null for free play.</summary>
        public string ScenarioId { get; private set; }

        /// <summary>Free play: no scenario is chosen (the challenges may be).</summary>
        public bool IsFreePlay
        {
            get { return ScenarioId == null; }
        }

        /// <summary>The ids of the chosen challenges, in the order they were chosen.</summary>
        public IList<string> Challenges
        {
            get { return _challenges.AsReadOnly(); }
        }

        /// <summary>How many challenges are chosen (the number on the button "Challenges (N)...").</summary>
        public int ChallengeCount
        {
            get { return _challenges.Count; }
        }

        /// <summary>Raised after every change of the choice (not when a call changed nothing).</summary>
        public event Action Changed;

        /// <summary>Chooses a scenario; null or an empty id is free play. Returns true when the choice changed.</summary>
        public bool SelectScenario(string scenarioId)
        {
            string id = string.IsNullOrEmpty(scenarioId) ? null : scenarioId;
            if (string.Equals(id, ScenarioId, StringComparison.Ordinal)) return false;

            ScenarioId = id;
            RaiseChanged();
            return true;
        }

        public bool IsChallengeSelected(string challengeId)
        {
            return challengeId != null && _challenges.Contains(challengeId);
        }

        /// <summary>Chooses or drops a challenge. Returns true when the choice changed.</summary>
        public bool SetChallenge(string challengeId, bool selected)
        {
            if (string.IsNullOrEmpty(challengeId)) return false;

            bool has = _challenges.Contains(challengeId);
            if (selected == has) return false;

            if (selected) _challenges.Add(challengeId);
            else _challenges.Remove(challengeId);
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Takes off the challenges that cannot be played with the present choice (the scenario does not list them, the company type does not fit, the
        /// credit patch is missing - <see cref="ChallengeRuntime.CheckSelectable"/>). A challenge the catalog does not know is taken off too.
        /// Returns the ids that were taken off.
        /// </summary>
        public IList<string> Revalidate(Func<string, ChallengeDefinition> findChallenge, string companyType, ScenarioDefinition scenario,
                                        bool perCompanyCreditActive)
        {
            var removed = new List<string>();
            foreach (string id in _challenges.ToArray())
            {
                ChallengeDefinition definition = findChallenge == null ? null : findChallenge(id);
                if (definition == null || !ChallengeRuntime.CheckSelectable(definition, companyType, scenario, perCompanyCreditActive).IsAvailable)
                {
                    _challenges.Remove(id);
                    removed.Add(id);
                }
            }
            if (removed.Count > 0) RaiseChanged();
            return removed;
        }

        /// <summary>The bonus to the score of the chosen challenges in whole per cent (15% and 10% make 25).</summary>
        public int BonusPercent(Func<string, ChallengeDefinition> findChallenge)
        {
            var definitions = new List<ChallengeDefinition>();
            foreach (string id in _challenges)
            {
                ChallengeDefinition definition = findChallenge == null ? null : findChallenge(id);
                if (definition != null) definitions.Add(definition);
            }
            return ScoreCalculator.BonusPercent(definitions);
        }

        /// <summary>
        /// What goes to the game when "Start" is pressed: the scenario (looked up by <paramref name="findScenario"/>; a scenario the catalog no longer
        /// has is free play) and the challenges chosen.
        /// </summary>
        public PendingRun ToPending(Func<string, ScenarioDefinition> findScenario)
        {
            ScenarioDefinition scenario = ScenarioId == null || findScenario == null ? null : findScenario(ScenarioId);
            return scenario == null ? PendingRun.Free(_challenges) : PendingRun.For(scenario, _challenges);
        }

        /// <summary>Back to free play with no challenges (a game started, the scene was left). Raises <see cref="Changed"/> when something was chosen.</summary>
        public void Clear()
        {
            if (ScenarioId == null && _challenges.Count == 0) return;
            ScenarioId = null;
            _challenges.Clear();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Action handlers = Changed;
            if (handlers != null) handlers();
        }
    }
}
