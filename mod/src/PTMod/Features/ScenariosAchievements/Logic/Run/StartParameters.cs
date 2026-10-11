using System;
using System.Globalization;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The fields of the game's <c>PlayerInitialData</c> that a scenario sets, as plain numbers (the game's enums are their integer values): what
    /// <see cref="StartParameters"/> computes and <c>ScenarioStarter</c> copies into the game's object, and takes back from it for the copy it makes
    /// before it changes anything (FR-4 item 4).
    /// </summary>
    public struct StartFields
    {
        public int Year;

        /// <summary>The game's <c>CompanyType</c>: 0 CPU (idm), 1 CPU fabless, 2 foundry.</summary>
        public int CompanyType;

        public int Difficulty;

        /// <summary>The game's <c>StartingTechnology</c> value.</summary>
        public int StartingTechnology;

        public float Funds;

        public int FactorySize;

        public bool Cheats;
    }

    /// <summary>
    /// The arithmetic of the start of a scenario (TSK-020, FR-4): the fields a scenario writes into <c>PlayerInitialData</c> in
    /// <c>ModNewGame.BeforeStart</c> before the fast-forward, and the check of the money and the lines once the game is up (item 5).
    /// </summary>
    public static class StartParameters
    {
        /// <summary>
        /// The fields after the scenario's start is applied to <paramref name="current"/> (the fields the player chose): the year, the company type,
        /// the difficulty, the technology, the money and the lines are the scenario's; cheats stay as the player left them when the scenario allows
        /// them and are off otherwise (FR-3 item 5). Throws for a scenario with no start or an unknown company type.
        /// </summary>
        public static StartFields Apply(StartFields current, ScenarioStart start)
        {
            if (start == null) throw new ArgumentNullException("start");
            int type = ContentVocabulary.CompanyTypeCode(start.CompanyType);
            if (type < 0) throw new ArgumentException("unknown company type '" + start.CompanyType + "'", "start");

            return new StartFields
            {
                Year = start.Year,
                CompanyType = type,
                Difficulty = start.DifficultyLevel,
                StartingTechnology = start.StartingTechnology,
                Funds = start.StartingFunds,
                FactorySize = start.FactorySize,
                Cheats = start.AllowCheats && current.Cheats,
            };
        }

        /// <summary>
        /// Whether the year, the company type and the difficulty the new-game window shows are the scenario's (the money and the lines the window computes for itself
        /// are not compared: the start overwrites them). False tells the player that the lists could not be locked to the scenario.
        /// </summary>
        public static bool Matches(ScenarioStart start, StartFields shown)
        {
            if (start == null) throw new ArgumentNullException("start");
            return shown.Year == start.Year && shown.CompanyType == ContentVocabulary.CompanyTypeCode(start.CompanyType) && shown.Difficulty == start.DifficultyLevel;
        }

        /// <summary>
        /// Compares the money and the lines of the player's company with the scenario's (FR-4 item 5): null when they agree, otherwise one English line
        /// for <c>ModLog</c> that names what differs. The money is a <c>float</c> in the game, so the difference of half a unit is not a difference.
        /// </summary>
        public static string Check(ScenarioStart start, double actualFunds, int actualLines)
        {
            if (start == null) throw new ArgumentNullException("start");

            string problem = null;
            if (double.IsNaN(actualFunds) || Math.Abs(actualFunds - start.StartingFunds) > 0.5)
            {
                problem = "money " + Number(actualFunds) + " instead of " + Number(start.StartingFunds);
            }
            if (actualLines != start.FactorySize)
            {
                string lines = "lines " + actualLines.ToString(CultureInfo.InvariantCulture) + " instead of " + start.FactorySize.ToString(CultureInfo.InvariantCulture);
                problem = problem == null ? lines : problem + ", " + lines;
            }
            return problem;
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
