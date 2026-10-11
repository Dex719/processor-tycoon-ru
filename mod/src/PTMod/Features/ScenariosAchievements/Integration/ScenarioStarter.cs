using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Logic;
using CompanyType = ProcessorTycoon.InitialData.CompanyType;
using PlayerInitialData = ProcessorTycoon.InitialData.PlayerInitialData;
using StartingTechnology = ProcessorTycoon.InitialData.StartingTechnology;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The choice the player has made in the new-game window and the pending run built of it (TSK-020, design.md "Старт сценария"; FR-4 items 1, 3, 4, 5, D2).
    /// <see cref="Selection"/> is what the section on the panel "Mods..." and the window of the challenges change. When "Start" is pressed the core raises
    /// <c>ModNewGame.BeforeStart</c> with the very <c>PlayerInitialData</c> the game will use, before the scene is loaded and the years are fast-forwarded:
    /// <see cref="OnBeforeStart"/> writes the year, the company type, the difficulty, the technology, the money, the lines and the cheats of the scenario
    /// there - the only place where the feature touches that object - and fixes <see cref="Pending"/>, which crosses the change of scene in a static field.
    /// <list type="bullet">
    /// <item><b>No patch.</b> <c>GameManager.StartNewGame</c>, <c>Player.InitializeAsNewGame</c> and <c>MenuGameSetup.*</c> belong to the core's services; the money
    /// and the lines are applied by <c>Company.InitializeAsNewGame</c> after the fast-forward, so the numbers written here are not lost.</item>
    /// <item><b>A failure</b> puts the fields back from the copy taken before anything was changed, drops the scenario and the challenges (the game starts as it
    /// would without the mod) and sets <see cref="PendingRun.StartFailed"/>; the player is told once the game is ready (<c>scn.toast.startFailed</c>).</item>
    /// <item><b>After the start</b> (<see cref="AfterStart"/>, on <c>GameReady(NewGame)</c>): the technologies of <c>start.techYear</c> are unlocked for the
    /// player's company, and the money and the lines the game set are compared with the scenario's - a difference is a line in the log, not an error.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class ScenarioStarter
    {
        private const string Site = "ScenarioStarter.OnBeforeStart";

        /// <summary>The choice in the new-game window; cleared when the menu scene is shown again (a new session of the window).</summary>
        internal static readonly ScenarioSelection Selection = new ScenarioSelection();

        /// <summary>What was fixed at "Start" for the game that is starting; null before any start and after <see cref="TakePending"/>.</summary>
        internal static PendingRun Pending { get; private set; }

        /// <summary>Listens to the start of a new game and to the menu scene (once per session).</summary>
        internal static void Install()
        {
            ModNewGame.BeforeStart += OnBeforeStart;
            PTModHost.SceneReady += OnSceneReady;
        }

        // ---- the start ----

        /// <summary><c>ModNewGame.BeforeStart</c>: fixes the pending run and applies the start of the scenario to <paramref name="data"/>.</summary>
        internal static void OnBeforeStart(PlayerInitialData data)
        {
            Pending = null;
            if (data == null) return;

            StartFields before = Read(data);
            try
            {
                if (Selection.IsFreePlay && Selection.ChallengeCount == 0)
                {
                    Pending = PendingRun.Free();
                    return;
                }

                ScenarioDefinition scenario = Selection.ScenarioId == null ? null : ScenarioCatalog.FindScenario(Selection.ScenarioId);

                // the type of the company is final now: a challenge that does not fit it (the player changed the type after choosing) is taken off
                string companyType = TypeIdOf(scenario == null ? before.CompanyType : StartParameters.Apply(before, scenario.Start).CompanyType);
                IList<string> dropped = Selection.Revalidate(ScenarioCatalog.FindChallenge, companyType, scenario, ModCredit.IsPerCompanyActive);
                if (dropped.Count > 0) ModLog.Info(ScenariosFeature.FeatureId, "new game: the challenges " + string.Join(", ", new List<string>(dropped).ToArray()) + " do not fit the start and were left out");

                PendingRun pending = Selection.ToPending(ScenarioCatalog.FindScenario);
                if (pending.HasScenario) Write(data, StartParameters.Apply(before, pending.Scenario.Start));
                Pending = pending;

                ModLog.Info(ScenariosFeature.FeatureId, "new game: " + (pending.HasScenario ? "scenario '" + pending.ScenarioId + "'" : "free play")
                                                       + ", challenges [" + string.Join(", ", pending.Challenges) + "]");
            }
            catch (Exception ex)
            {
                try
                {
                    Write(data, before);   // the copy taken before anything was changed
                }
                catch (Exception restore)
                {
                    ModLog.Error(ScenariosFeature.FeatureId, "new game: the fields of the start could not be put back", restore);
                }
                Pending = PendingRun.Failed();
                ModHooks.ReportFault(ScenariosFeature.FeatureId, Site, ex);
            }
        }

        /// <summary>Takes the pending run for the game that is ready (null when nothing was fixed) and forgets it.</summary>
        internal static PendingRun TakePending()
        {
            PendingRun pending = Pending;
            Pending = null;
            return pending;
        }

        /// <summary>
        /// After the start (<c>GameReady(NewGame)</c>): the technologies of the scenario's <c>techYear</c> for the player (FR-4 item 3), the check of the money
        /// and the lines (item 5), the notice when the start failed (item 4).
        /// </summary>
        internal static void AfterStart(PendingRun pending)
        {
            if (pending == null) return;
            if (pending.StartFailed)
            {
                ModNotify.ShowKey(ScenariosFeature.FeatureId, "scn.toast.startFailed");
                return;
            }
            if (!pending.HasScenario) return;

            ScenarioStart start = pending.Scenario.Start;
            if (start.TechYear > 0)
            {
                bool unlocked = GameFacts.UnlockTechnologies(start.TechYear);
                ModLog.Info(ScenariosFeature.FeatureId, unlocked
                    ? "new game: the technologies of " + start.TechYear + " are unlocked"
                    : "new game: the technologies of " + start.TechYear + " could not be unlocked (no research sector yet)");
            }

            double money;
            int lines;
            if (!GameFacts.ReadCompany(out money, out lines))
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "new game: the company of the player was not there to check the start of '" + pending.ScenarioId + "'");
                return;
            }
            string problem = StartParameters.Check(start, money, lines);
            if (problem != null) ModLog.Warn(ScenariosFeature.FeatureId, "new game: the start of '" + pending.ScenarioId + "' differs from the scenario: " + problem);
            else ModLog.Debug(ScenariosFeature.FeatureId, "new game: the start of '" + pending.ScenarioId + "' is as the scenario says");
        }

        // ---- the menu ----

        // The window of a new game is shown again: its old choice and a pending run nobody took are out of date.
        private static void OnSceneReady(int scene)
        {
            if (scene != PTModHost.MainMenuScene) return;
            Pending = null;
            Selection.Clear();
        }

        // ---- PlayerInitialData <-> the plain fields ----

        private static StartFields Read(PlayerInitialData data)
        {
            return new StartFields
            {
                Year = data.Year,
                CompanyType = (int)data.CompanyType,
                Difficulty = data.DifficultyLevel,
                StartingTechnology = (int)data.StartingTechnology,
                Funds = data.StartingFunds,
                FactorySize = data.FactorySize,
                Cheats = data.EnableCheats,
            };
        }

        private static void Write(PlayerInitialData data, StartFields fields)
        {
            data.Year = fields.Year;
            data.CompanyType = (CompanyType)fields.CompanyType;
            data.DifficultyLevel = fields.Difficulty;
            data.StartingTechnology = (StartingTechnology)fields.StartingTechnology;
            data.StartingFunds = fields.Funds;
            data.FactorySize = fields.FactorySize;
            data.EnableCheats = fields.Cheats;
        }

        private static string TypeIdOf(int code)
        {
            return code >= 0 && code < ContentVocabulary.CompanyTypes.Count ? ContentVocabulary.CompanyTypes[code] : null;
        }
    }
}
