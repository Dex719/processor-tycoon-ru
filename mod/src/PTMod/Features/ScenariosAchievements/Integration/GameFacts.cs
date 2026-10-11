using System;
using System.Runtime.CompilerServices;
using PTMod.Features.ScenariosAchievements.Logic;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The few facts about the player and the game that the director and the starter read (TSK-019, TSK-020): the live values of a pass of the engine, the cheats
    /// and the difficulty of the game that just started, the money and the lines of the company, and the unlocking of technologies. The game's classes are
    /// touched only here (no inlining); every method answers something harmless when the player does not exist yet.
    /// </summary>
    internal static class GameFacts
    {
        /// <summary>The live values of the game for a pass of the engine (<see cref="LiveRun"/>).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static LiveRun Live(DateTime today, bool strictAssisted)
        {
            GamePlayer player = GamePlayer.Instance;
            ICompany company = player == null ? null : player.Company;
            return new LiveRun
            {
                Today = today,
                CompanyType = GameCpus.TypeOf(company),
                CompanyName = GameCpus.NameOf(company),
                Difficulty = player == null ? 0 : player.DifficultyLevel,
                StrictAssisted = strictAssisted,
            };
        }

        /// <summary><c>Player.Instance.DifficultyLevel</c>; the default (2, Normal) while there is no player.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Difficulty()
        {
            GamePlayer player = GamePlayer.Instance;
            return player == null ? 2 : player.DifficultyLevel;
        }

        /// <summary><c>Player.Instance.EnableCheats</c>.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool CheatsEnabled()
        {
            GamePlayer player = GamePlayer.Instance;
            return player != null && player.EnableCheats;
        }

        /// <summary><c>GameDefines.EndYear</c>, the year the game ends (2030); 2030 when the game has not set it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int EndYear()
        {
            int year = ProcessorTycoon.GameDefines.EndYear;
            return year > 0 ? year : ChallengeRuntime.EndYear;
        }

        /// <summary>The money of the player's company and its production lines; false while there is no company.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ReadCompany(out double money, out int lines)
        {
            money = 0;
            lines = 0;
            GamePlayer player = GamePlayer.Instance;
            ICompany company = player == null ? null : player.Company;
            if (ReferenceEquals(company, null)) return false;

            money = company.MoneyAmount;
            lines = company.Factory == null ? 0 : company.Factory.ProductionCapacity;
            return true;
        }

        /// <summary>
        /// <c>GameManager.QuitToMenu()</c>: the game goes back to the main menu (a scene change of two seconds). It does not save, which is what the results window wants (FR-9 item 4).
        /// False while the game manager does not exist.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool QuitToMenu()
        {
            ProcessorTycoon.GameManager manager = ProcessorTycoon.GameManager.Instance;
            if (manager == null) return false;

            manager.QuitToMenu();
            return true;
        }

        /// <summary><c>ResearchSector.UnlockTechnologies(year, 0)</c> of the player's company (FR-4 item 3). False when the company has no research sector yet.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool UnlockTechnologies(int year)
        {
            GamePlayer player = GamePlayer.Instance;
            ICompany company = player == null ? null : player.Company;
            if (ReferenceEquals(company, null) || company.ResearchSector == null) return false;

            company.ResearchSector.UnlockTechnologies(year, 0);
            return true;
        }
    }
}
