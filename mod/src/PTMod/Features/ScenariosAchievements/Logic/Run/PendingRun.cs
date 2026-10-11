using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the player chose for the new game that is about to start (design.md, "Интеграция"; FR-3 item 6, FR-4): the scenario (or free play) and the
    /// challenges. <c>ScenarioStarter.OnBeforeStart</c> fixes it when "Start" is pressed; it lives in a static field, because it has to survive the change
    /// of scene from the menu to the game; <c>RunDirector</c> takes it on <c>GameReady(NewGame)</c> and makes the run of it.
    /// <para>
    /// <see cref="Scenario"/> is the definition the player saw (its <see cref="ScenarioDefinition.Source"/> is the snapshot the run will keep). When
    /// applying the start failed (<see cref="StartFailed"/>), the game starts as vanilla free play with no challenges, and the player is told once
    /// the game is ready.
    /// </para>
    /// </summary>
    public sealed class PendingRun
    {
        public string ScenarioId;

        /// <summary>The ids of the chosen challenges.</summary>
        public string[] Challenges = new string[0];

        /// <summary>Eight hex digits of the scenario's content (<see cref="ScenarioDefinition.ContentHash"/>); null in free play.</summary>
        public string ContentHash;

        /// <summary>The scenario was changed or added by the player's content: its results make no records.</summary>
        public bool IsCustom;

        /// <summary>The definition of the chosen scenario; null in free play.</summary>
        public ScenarioDefinition Scenario;

        /// <summary>The start of the scenario could not be applied: the game starts as it would without the mod and the player is told.</summary>
        public bool StartFailed;

        /// <summary>A scenario was chosen.</summary>
        public bool HasScenario
        {
            get { return Scenario != null; }
        }

        /// <summary>Free play, with the challenges the player chose (none by default).</summary>
        public static PendingRun Free(IEnumerable<string> challenges = null)
        {
            return new PendingRun { Challenges = ToArray(challenges) };
        }

        /// <summary>The chosen scenario with its challenges.</summary>
        public static PendingRun For(ScenarioDefinition scenario, IEnumerable<string> challenges = null)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            return new PendingRun
            {
                ScenarioId = scenario.Id,
                Scenario = scenario,
                ContentHash = scenario.ContentHash,
                IsCustom = scenario.IsCustom,
                Challenges = ToArray(challenges),
            };
        }

        /// <summary>The start of the scenario failed: plain free play, no challenges, and the flag that tells the player.</summary>
        public static PendingRun Failed()
        {
            return new PendingRun { StartFailed = true };
        }

        private static string[] ToArray(IEnumerable<string> values)
        {
            var list = new List<string>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrEmpty(value) && !list.Contains(value)) list.Add(value);
                }
            }
            return list.ToArray();
        }
    }
}
