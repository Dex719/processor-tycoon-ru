using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The rules of the self-check at the start (TSK-029; NFR-2, NFR-5; design.md "Самопроверка на старте"): what the feature stands on and how bad it is when a part is missing. The facts come
    /// from the game side (<c>ScenariosSelfCheck</c> reads the registry of the core, the game's classes, the catalog and the string tables); the decisions are here, pure, so the tests can
    /// feed any combination of broken parts.
    /// <list type="bullet">
    /// <item><b>Patches</b> (<see cref="ExpectedPatchTargets"/>): the feature has exactly two, <c>ContractManager.PromoteContractToActive</c> and <c>GameplaySettingsWindow.ChangeDifficulty</c>; both
    /// declared and applied, and none on a method the core or the services of the core took (<see cref="IsForbiddenTarget"/>). A patch that is not in the game is an error, since what it
    /// served stops working (the OEM contracts are not counted, the difficulty can be lowered during a scenario).</item>
    /// <item><b>Members of the game</b> (<see cref="Members"/>): the four lists of <c>MenuGameSetup</c> the lock reads (a warning: the start still writes the scenario's numbers), the field
    /// <c>GameplaySettingsWindow.difficultySettings</c> (an error), the method <c>GameManager.QuitToMenu</c>, the two events of the scene.</item>
    /// <item><b>Content</b>: the catalog was loaded and holds scenarios, challenges and achievements; no record of the built-in files was rejected (a rejected record of the player's override is a warning).</item>
    /// <item><b>Channels</b>: every modifier channel the loaded records write to is declared in the registry of the core.</item>
    /// <item><b>Strings</b>: every key of <see cref="ScenarioTextKeys.Required"/> is in the Russian table (an error) and in the English one (a warning).</item>
    /// </list>
    /// The verdict is a list; <c>self-check: OK</c> is the empty one.
    /// </summary>
    public static class ScenariosSelfCheckRules
    {
        /// <summary>The areas of the findings.</summary>
        public const string AreaPatches = "patches", AreaGame = "game", AreaContent = "content", AreaChannels = "channels", AreaStrings = "strings", AreaCheck = "check";

        /// <summary>How many names a finding lists before it says "and N more".</summary>
        public const int MaxNamesPerFinding = 10;

        private const string MenuGameSetup = "ProcessorTycoon.Menu.MenuGameSetup";
        private const string GameplaySettingsWindow = "ProcessorTycoon.Desktop.GameplaySettings.GameplaySettingsWindow";
        private const string DropdownUi = "ProcessorTycoon.UI.DropdownUI";
        private const string ToggleUi = "ProcessorTycoon.UI.ToggleUI";

        private static readonly string[] ExpectedTargets =
        {
            "ContractManager.PromoteContractToActive",
            "GameplaySettingsWindow.ChangeDifficulty",
        };

        private static readonly string[] ExpectedConsequences =
        {
            "OEM contracts are not counted: the metric oem.signed.total, the event oem.signed and the goals and achievements built on them stay at zero",
            "lowering the difficulty during a scenario is not blocked",
        };

        /// <summary>
        /// Methods the feature must not patch: the services of the core replace them (<c>ModEvents</c> for the releases, <c>ModNewGame</c> for the start, <c>ModTicks</c> for the calendar), and the
        /// patches of the core sit on them. <c>MenuGameSetup.*</c> is closed as a whole (<see cref="IsForbiddenTarget"/>).
        /// </summary>
        private static readonly string[] ForbiddenTargets =
        {
            "Company.AddCpu", "AICompany.AddCpu", "GameManager.StartNewGame", "Player.InitializeAsNewGame", "Cheats.AddMoney", "DateController.NextMonth",
        };

        private static readonly GameMemberSpec[] MemberTable =
        {
            Field(MenuGameSetup, "companyTypeDropdown", DropdownUi, SelfCheckLevel.Warning,
                "the list of company types of the new-game window is not locked to the chosen scenario (the start still writes the numbers of the scenario)"),
            Field(MenuGameSetup, "difficultyDropdown", DropdownUi, SelfCheckLevel.Warning,
                "the list of difficulties of the new-game window is not locked to the chosen scenario (the start still writes the numbers of the scenario)"),
            Field(MenuGameSetup, "startDateDropdown", DropdownUi, SelfCheckLevel.Warning,
                "the list of start years of the new-game window is not locked to the chosen scenario (the start still writes the numbers of the scenario)"),
            Field(MenuGameSetup, "enableCheatsToggle", ToggleUi, SelfCheckLevel.Warning,
                "the switch of the cheats of the new-game window is not locked when the scenario forbids them (the start still switches them off)"),
            Field(GameplaySettingsWindow, "difficultySettings", DropdownUi, SelfCheckLevel.Error,
                "the difficulty cannot be put back when the player lowers it during a scenario"),
            new GameMemberSpec
            {
                TypeName = "ProcessorTycoon.GameManager",
                Kind = ProbeKind.Method,
                Name = "QuitToMenu",
                ParameterTypes = new string[0],
                Level = SelfCheckLevel.Warning,
                Consequence = "the button 'To the main menu' of the results window does nothing",
            },
            new GameMemberSpec
            {
                TypeName = "ProcessorTycoon.Bank.BankruptcyHandler",
                Kind = ProbeKind.Event,
                Name = "OnCompanyWentBankrupt",
                Level = SelfCheckLevel.Error,
                Consequence = "the bankruptcy of the player does not end a scenario and the bankruptcies of the rivals are not counted",
            },
            new GameMemberSpec
            {
                TypeName = "ProcessorTycoon.CompanySystem.Business.BusinessContractManager",
                Kind = ProbeKind.Event,
                Name = "OnPlayerSignedContract",
                Level = SelfCheckLevel.Warning,
                Consequence = "the signing of a foundry contract is not counted (the event foundry.signed)",
            },
        };

        /// <summary>The two patches of the feature, <c>Class.Method</c> as the registry of the core names them (design.md "Патчи Harmony").</summary>
        public static IReadOnlyList<string> ExpectedPatchTargets
        {
            get { return ExpectedTargets; }
        }

        /// <summary>The members of the game the feature reaches by name; the rows are read, never changed.</summary>
        public static IReadOnlyList<GameMemberSpec> Members
        {
            get { return MemberTable; }
        }

        /// <summary>The target is a method the feature must not patch (the services of the core took it), or any method of the new-game window.</summary>
        public static bool IsForbiddenTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (target.StartsWith("MenuGameSetup.", StringComparison.Ordinal)) return true;
            foreach (string forbidden in ForbiddenTargets)
            {
                if (string.Equals(forbidden, target, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Judges the facts. Never throws on a facts object that is incomplete: a part with no facts is simply not judged.</summary>
        public static SelfCheckVerdict Evaluate(SelfCheckFacts facts)
        {
            if (facts == null) throw new ArgumentNullException("facts");

            var errors = new List<SelfCheckFinding>();
            var warnings = new List<SelfCheckFinding>();

            CheckPatches(facts, errors);
            CheckMembers(facts, errors, warnings);
            CheckContent(facts, errors, warnings);
            CheckChannels(facts, errors);
            CheckStrings(facts, errors, warnings);
            foreach (string line in facts.NotLooked) Add(warnings, SelfCheckLevel.Warning, AreaCheck, "could not look at " + line);

            errors.AddRange(warnings);
            return new SelfCheckVerdict(errors, CountsOf(facts));
        }

        // ---- the parts ----

        private static void CheckPatches(SelfCheckFacts facts, List<SelfCheckFinding> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PatchFact patch in facts.Patches)
            {
                if (patch == null) continue;
                int index = IndexOfExpected(patch.Target);
                if (IsForbiddenTarget(patch.Target))
                {
                    Add(errors, SelfCheckLevel.Error, AreaPatches, "the feature patches " + patch.Target + ", which the services of the core took (use ModEvents, ModNewGame, ModTicks and the channels of ModModifiers)");
                    continue;
                }
                if (index < 0)
                {
                    Add(errors, SelfCheckLevel.Error, AreaPatches, "the patch on " + patch.Target + " is not part of the design (the feature has exactly two: " + string.Join(", ", ExpectedTargets) + ")");
                    continue;
                }

                seen.Add(patch.Target);
                if (patch.Applied) continue;

                string status = string.IsNullOrEmpty(patch.Status) ? "not applied" : patch.Status;
                string detail = string.IsNullOrEmpty(patch.Detail) ? string.Empty : " (" + patch.Detail + ")";
                Add(errors, SelfCheckLevel.Error, AreaPatches, "the patch on " + patch.Target + " is not in the game: " + status + detail + "; " + ExpectedConsequences[index]);
            }

            for (int i = 0; i < ExpectedTargets.Length; i++)
            {
                if (!seen.Contains(ExpectedTargets[i]))
                {
                    Add(errors, SelfCheckLevel.Error, AreaPatches, "the patch on " + ExpectedTargets[i] + " is not declared; " + ExpectedConsequences[i]);
                }
            }
        }

        private static int IndexOfExpected(string target)
        {
            for (int i = 0; i < ExpectedTargets.Length; i++)
            {
                if (string.Equals(ExpectedTargets[i], target, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static void CheckMembers(SelfCheckFacts facts, List<SelfCheckFinding> errors, List<SelfCheckFinding> warnings)
        {
            foreach (MemberFact member in facts.Members)
            {
                if (member == null || member.Spec == null || string.IsNullOrEmpty(member.Problem)) continue;
                List<SelfCheckFinding> into = member.Spec.Level == SelfCheckLevel.Error ? errors : warnings;
                Add(into, member.Spec.Level, AreaGame, member.Spec.Display + ": " + member.Problem + "; " + member.Spec.Consequence);
            }
        }

        private static void CheckContent(SelfCheckFacts facts, List<SelfCheckFinding> errors, List<SelfCheckFinding> warnings)
        {
            if (!facts.ContentLoaded)
            {
                Add(errors, SelfCheckLevel.Error, AreaContent, "the content was not loaded: the new-game window, the goals and the achievements have nothing to work with (see the log of the start)");
                return;
            }

            if (facts.Scenarios == 0) Add(errors, SelfCheckLevel.Error, AreaContent, "no scenario is loaded: the new-game window offers only free play");
            if (facts.Challenges == 0) Add(errors, SelfCheckLevel.Error, AreaContent, "no challenge is loaded: the challenges window is empty");
            if (facts.Achievements == 0) Add(errors, SelfCheckLevel.Error, AreaContent, "no achievement is loaded: the achievements window is empty");

            foreach (ContentFileFact file in facts.ContentFiles)
            {
                if (file == null || file.Skipped <= 0) continue;

                // a file of the player may carry a record that does not pass; the built-in files must not
                SelfCheckLevel level = file.OverrideFound ? SelfCheckLevel.Warning : SelfCheckLevel.Error;
                string first = string.IsNullOrEmpty(file.FirstError) ? string.Empty : "; the first: " + file.FirstError;
                string whose = file.OverrideFound ? " (the override file of the player is in use)" : string.Empty;
                Add(level == SelfCheckLevel.Error ? errors : warnings, level, AreaContent,
                    file.File + ": " + file.Skipped.ToString(CultureInfo.InvariantCulture) + " record(s) rejected" + first + whose);
            }
        }

        private static void CheckChannels(SelfCheckFacts facts, List<SelfCheckFinding> errors)
        {
            foreach (ChannelFact channel in facts.Channels)
            {
                if (channel == null || channel.Declared) continue;
                Add(errors, SelfCheckLevel.Error, AreaChannels, "the channel '" + channel.Channel + "' used by " + channel.UsedBy + " is not declared in the modifier registry of the core: the modifier does nothing");
            }
        }

        private static void CheckStrings(SelfCheckFacts facts, List<SelfCheckFinding> errors, List<SelfCheckFinding> warnings)
        {
            if (facts.RequiredKeys.Count == 0) return;

            if (facts.RussianKeys.Count == 0)
            {
                Add(errors, SelfCheckLevel.Error, AreaStrings, "the Russian table has no string of the feature (l10n/ru/scenarios-achievements.json is not in PTMod.dll?); every text shows as [key]");
            }
            else
            {
                AddMissing(errors, SelfCheckLevel.Error, "the Russian table", facts.RequiredKeys, facts.RussianKeys);
            }

            if (facts.EnglishKeys.Count == 0)
            {
                Add(warnings, SelfCheckLevel.Warning, AreaStrings, "the English table has no string of the feature (l10n/en/scenarios-achievements.json is not in PTMod.dll?)");
            }
            else
            {
                AddMissing(warnings, SelfCheckLevel.Warning, "the English table", facts.RequiredKeys, facts.EnglishKeys);
            }
        }

        private static void AddMissing(List<SelfCheckFinding> into, SelfCheckLevel level, string table, List<string> required, HashSet<string> present)
        {
            var missing = new List<string>();
            foreach (string key in required)
            {
                if (!ScenarioTextKeys.IsPresent(present, key)) missing.Add(key);
            }
            if (missing.Count == 0) return;

            Add(into, level, AreaStrings, table + " lacks " + missing.Count.ToString(CultureInfo.InvariantCulture) + " key(s): " + Names(missing));
        }

        // ---- helpers ----

        private static string Names(List<string> names)
        {
            var sb = new StringBuilder();
            int shown = Math.Min(names.Count, MaxNamesPerFinding);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(names[i]);
            }
            if (names.Count > shown) sb.Append(" and ").Append((names.Count - shown).ToString(CultureInfo.InvariantCulture)).Append(" more");
            return sb.ToString();
        }

        private static string CountsOf(SelfCheckFacts facts)
        {
            int applied = 0;
            foreach (PatchFact patch in facts.Patches)
            {
                if (patch != null && patch.Applied) applied++;
            }
            int found = 0;
            foreach (MemberFact member in facts.Members)
            {
                if (member != null && string.IsNullOrEmpty(member.Problem)) found++;
            }

            return facts.Scenarios.ToString(CultureInfo.InvariantCulture) + " scenario(s), "
                   + facts.Challenges.ToString(CultureInfo.InvariantCulture) + " challenge(s), "
                   + facts.Achievements.ToString(CultureInfo.InvariantCulture) + " achievement(s), "
                   + applied.ToString(CultureInfo.InvariantCulture) + " patch(es) applied, "
                   + found.ToString(CultureInfo.InvariantCulture) + " of " + facts.Members.Count.ToString(CultureInfo.InvariantCulture) + " game member(s) found, "
                   + facts.Channels.Count.ToString(CultureInfo.InvariantCulture) + " channel(s), "
                   + facts.RequiredKeys.Count.ToString(CultureInfo.InvariantCulture) + " text key(s) checked";
        }

        private static void Add(List<SelfCheckFinding> into, SelfCheckLevel level, string area, string message)
        {
            into.Add(new SelfCheckFinding { Level = level, Area = area, Message = message });
        }

        private static GameMemberSpec Field(string type, string name, string valueType, SelfCheckLevel level, string consequence)
        {
            return new GameMemberSpec
            {
                TypeName = type,
                Kind = ProbeKind.Field,
                Name = name,
                ValueType = valueType,
                Level = level,
                Consequence = consequence,
            };
        }
    }
}
