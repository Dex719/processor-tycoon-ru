using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-029: the self-check at the start. The rules (<see cref="ScenariosSelfCheckRules"/>) are pure and are fed healthy and broken facts: every part of the acceptance has a case - the
    /// two patches, the fields of <c>MenuGameSetup</c>, <c>GameplaySettingsWindow.difficultySettings</c>, <c>GameManager.QuitToMenu</c>, the validity of the content, the keys <c>scn.*</c>,
    /// <c>chl.*</c>, <c>ach.*</c> in <c>ru</c>, the declaration of the modifier channels. A healthy game is built out of the real shipped content and string tables and must give
    /// <c>self-check: OK</c>. The list of keys of the code is held in step with the sources by a scan, the tables of patches and members with the code that uses them. The game side
    /// (<c>ScenariosSelfCheck</c>, <c>GameMemberProbe</c>) needs the core and the game and is read as text.
    /// </summary>
    [Covers(typeof(ScenariosSelfCheckRules), typeof(ScenarioTextKeys), typeof(SelfCheckFacts), typeof(SelfCheckVerdict), typeof(SelfCheckFinding), typeof(SelfCheckLevel),
        typeof(GameMemberSpec), typeof(ProbeKind), typeof(MemberFact), typeof(PatchFact), typeof(ContentFileFact), typeof(ChannelFact))]
    public class SelfCheckTests
    {
        private const string PromoteContract = "ContractManager.PromoteContractToActive";
        private const string ChangeDifficulty = "GameplaySettingsWindow.ChangeDifficulty";

        // ---- the healthy game, made of what is shipped ----

        private static readonly List<ScenarioDefinition> Scenarios = ScenarioTestData.StandardScenarios();
        private static readonly List<ChallengeDefinition> Challenges = ScenarioTestData.StandardChallenges();
        private static readonly List<AchievementDefinition> Achievements = ScenarioTestData.StandardAchievements();

        private static HashSet<string> OwnKeys(string language)
        {
            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson(language, "l10n/" + language + "/scenarios-achievements.json", ScenarioTestData.ReadStrings(language), problems);
            Assert.Equal(0, problems.Count);
            return new HashSet<string>(tables.Keys(language).Where(ScenarioTextKeys.IsOwnKey), StringComparer.Ordinal);
        }

        private static SelfCheckFacts Healthy()
        {
            var facts = new SelfCheckFacts();
            facts.Patches.Add(new PatchFact { Target = PromoteContract, Applied = true, Status = "Applied" });
            facts.Patches.Add(new PatchFact { Target = ChangeDifficulty, Applied = true, Optional = true, Status = "Applied" });
            foreach (GameMemberSpec spec in ScenariosSelfCheckRules.Members) facts.Members.Add(new MemberFact { Spec = spec });

            facts.ContentLoaded = true;
            facts.Scenarios = Scenarios.Count;
            facts.Challenges = Challenges.Count;
            facts.Achievements = Achievements.Count;
            facts.ContentFiles.Add(new ContentFileFact { File = "scenarios.json", Loaded = Scenarios.Count });
            facts.ContentFiles.Add(new ContentFileFact { File = "challenges.json", Loaded = Challenges.Count });
            facts.ContentFiles.Add(new ContentFileFact { File = "achievements.json", Loaded = Achievements.Count });

            var declared = new HashSet<string>(ScenarioTestData.DeclaredChannels, StringComparer.Ordinal);
            foreach (ScenarioDefinition scenario in Scenarios)
            {
                foreach (ModifierSpec modifier in scenario.Modifiers)
                {
                    facts.Channels.Add(new ChannelFact { Channel = modifier.Channel, UsedBy = "scenario '" + scenario.Id + "'", Declared = declared.Contains(modifier.Channel) });
                }
            }
            foreach (ChallengeDefinition challenge in Challenges)
            {
                foreach (ModifierSpec modifier in challenge.Modifiers)
                {
                    facts.Channels.Add(new ChannelFact { Channel = modifier.Channel, UsedBy = "challenge '" + challenge.Id + "'", Declared = declared.Contains(modifier.Channel) });
                }
            }

            facts.RequiredKeys.AddRange(ScenarioTextKeys.Required(Scenarios, Challenges, Achievements));
            foreach (string key in OwnKeys("ru")) facts.RussianKeys.Add(key);
            foreach (string key in OwnKeys("en")) facts.EnglishKeys.Add(key);
            return facts;
        }

        private static bool Has(SelfCheckVerdict verdict, SelfCheckLevel level, string area, string part)
        {
            return verdict.Findings.Any(f => f.Level == level && f.Area == area && f.Message.Contains(part));
        }

        private static string All(SelfCheckVerdict verdict)
        {
            return string.Join("\n  ", verdict.Findings.Select(f => f.Level + " " + f));
        }

        // ---- the healthy game ----

        [Test]
        public void TheShippedGame_GivesOk_AndTheSummaryIsTheLineOfTheAcceptance()
        {
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(Healthy());

            Assert.True(verdict.IsOk, "a healthy game has findings:\n  " + All(verdict));
            Assert.Equal("self-check: OK", verdict.Summary);
            Assert.Equal(0, verdict.Errors);
            Assert.Equal(0, verdict.Warnings);
        }

        [Test]
        public void TheCounts_SayWhatWasLookedAt()
        {
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(Healthy());

            Assert.True(verdict.Counts.StartsWith("8 scenario(s), 8 challenge(s), 37 achievement(s), 2 patch(es) applied, ", StringComparison.Ordinal), verdict.Counts);
            Assert.True(verdict.Counts.Contains(ScenariosSelfCheckRules.Members.Count + " of " + ScenariosSelfCheckRules.Members.Count + " game member(s) found"), verdict.Counts);
            Assert.True(verdict.Counts.Contains("text key(s) checked"), verdict.Counts);
        }

        [Test]
        public void ASummaryWithFindings_CountsTheErrorsAndTheWarnings()
        {
            SelfCheckFacts facts = Healthy();
            facts.Patches[0].Applied = false;
            facts.Members[0].Problem = "no such field";
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.False(verdict.IsOk);
            Assert.Equal(1, verdict.Errors);
            Assert.Equal(1, verdict.Warnings);
            Assert.Equal("self-check: 1 error(s), 1 warning(s)", verdict.Summary);
        }

        [Test]
        public void TheErrors_ComeBeforeTheWarnings()
        {
            SelfCheckFacts facts = Healthy();
            facts.Members[0].Problem = "no such field";                       // a warning
            facts.NotLooked.Add("the strings: boom");                          // a warning
            facts.Patches[1].Applied = false;                                  // an error
            facts.Channels.Add(new ChannelFact { Channel = "x", UsedBy = "challenge 'y'", Declared = false });   // an error

            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);
            Assert.Equal(4, verdict.Findings.Count);
            Assert.Equal(SelfCheckLevel.Error, verdict.Findings[0].Level);
            Assert.Equal(SelfCheckLevel.Error, verdict.Findings[1].Level);
            Assert.Equal(SelfCheckLevel.Warning, verdict.Findings[2].Level);
            Assert.Equal(SelfCheckLevel.Warning, verdict.Findings[3].Level);
        }

        [Test]
        public void NoFacts_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => ScenariosSelfCheckRules.Evaluate(null));
        }

        // ---- the patches ----

        [Test]
        public void ThePatchesOfTheFeature_AreTheTwoOfTheDesign()
        {
            Assert.Equal(new[] { PromoteContract, ChangeDifficulty }, ScenariosSelfCheckRules.ExpectedPatchTargets.ToArray());
        }

        [Test]
        public void ThePatchesInTheCode_AreExactlyTheExpectedOnes()
        {
            // the [ModPatch(typeof(X), "Method"...)] of the feature's sources, with the aliases of the usings resolved to the class
            var found = new List<string>();
            foreach (string path in SourceScan.Files(FeatureRoot(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match m in Regex.Matches(code, @"using\s+(\w+)\s*=\s*[\w.]*?(\w+)\s*;")) aliases[m.Groups[1].Value] = m.Groups[2].Value;
                foreach (Match m in Regex.Matches(code, @"\[ModPatch\(typeof\((\w+)\),\s*""(\w+)"""))
                {
                    string alias = m.Groups[1].Value;
                    string type;
                    found.Add((aliases.TryGetValue(alias, out type) ? type : alias) + "." + m.Groups[2].Value);
                }
            }

            found.Sort(StringComparer.Ordinal);
            var expected = ScenariosSelfCheckRules.ExpectedPatchTargets.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, found.ToArray());
        }

        [Test]
        public void APatchThatIsNotDeclared_IsAnError_WithWhatStopsWorking()
        {
            SelfCheckFacts facts = Healthy();
            facts.Patches.RemoveAt(0);
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Findings.Count);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", "the patch on " + PromoteContract + " is not declared"), All(verdict));
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", "OEM contracts are not counted"), All(verdict));
        }

        [Test]
        public void APatchThatIsNotInTheGame_IsAnError_ForBothPatches_TheRegistrysReasonIncluded()
        {
            SelfCheckFacts facts = Healthy();
            facts.Patches[0].Applied = false;
            facts.Patches[0].Status = "Failed";
            facts.Patches[0].Detail = "no such method";
            facts.Patches[1].Applied = false;
            facts.Patches[1].Status = "Rejected";
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(2, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", PromoteContract + " is not in the game: Failed (no such method)"), All(verdict));
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", ChangeDifficulty + " is not in the game: Rejected"), All(verdict));
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", "lowering the difficulty during a scenario is not blocked"), All(verdict));
        }

        [Test]
        public void TheOptionalPatch_IsStillAnErrorWhenItIsMissing_Design()
        {
            // design.md, "Error Handling": the difficulty guard is Optional so that the feature stays available, but the self-check reports the missing field as an error
            SelfCheckFacts facts = Healthy();
            Assert.True(facts.Patches[1].Optional);
            facts.Patches[1].Applied = false;

            Assert.Equal(1, ScenariosSelfCheckRules.Evaluate(facts).Errors);
        }

        [Test]
        public void APatchOnAMethodTheCoreTook_IsAnError()
        {
            foreach (string target in new[] { "Company.AddCpu", "AICompany.AddCpu", "GameManager.StartNewGame", "Player.InitializeAsNewGame", "Cheats.AddMoney", "DateController.NextMonth", "MenuGameSetup.UpdateInitialData" })
            {
                Assert.True(ScenariosSelfCheckRules.IsForbiddenTarget(target), target);

                SelfCheckFacts facts = Healthy();
                facts.Patches.Add(new PatchFact { Target = target, Applied = true, Status = "Applied" });
                SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);
                Assert.Equal(1, verdict.Errors);
                Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", "the feature patches " + target), All(verdict));
            }
            Assert.False(ScenariosSelfCheckRules.IsForbiddenTarget(PromoteContract));
            Assert.False(ScenariosSelfCheckRules.IsForbiddenTarget(ChangeDifficulty));
            Assert.False(ScenariosSelfCheckRules.IsForbiddenTarget(null));
        }

        [Test]
        public void APatchNotInTheDesign_IsAnError()
        {
            SelfCheckFacts facts = Healthy();
            facts.Patches.Add(new PatchFact { Target = "Market.CalculateRatings", Applied = true, Status = "Applied" });
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "patches", "the patch on Market.CalculateRatings is not part of the design"), All(verdict));
        }

        // ---- the members of the game ----

        [Test]
        public void TheMembersTable_NamesTheFieldsAndTheMethodOfTheAcceptance()
        {
            var names = ScenariosSelfCheckRules.Members.Select(m => m.Display).ToList();
            foreach (string expected in new[]
                     {
                         "MenuGameSetup.companyTypeDropdown", "MenuGameSetup.difficultyDropdown", "MenuGameSetup.startDateDropdown", "MenuGameSetup.enableCheatsToggle",
                         "GameplaySettingsWindow.difficultySettings", "GameManager.QuitToMenu",
                     })
            {
                Assert.True(names.Contains(expected), "the table lacks " + expected + ": " + string.Join(", ", names));
            }

            GameMemberSpec difficulty = ScenariosSelfCheckRules.Members.First(m => m.Name == "difficultySettings");
            Assert.Equal(ProbeKind.Field, difficulty.Kind);
            Assert.Equal(SelfCheckLevel.Error, difficulty.Level);
            Assert.Equal("ProcessorTycoon.Desktop.GameplaySettings.GameplaySettingsWindow", difficulty.TypeName);
            Assert.Equal("ProcessorTycoon.UI.DropdownUI", difficulty.ValueType);

            GameMemberSpec quit = ScenariosSelfCheckRules.Members.First(m => m.Name == "QuitToMenu");
            Assert.Equal(ProbeKind.Method, quit.Kind);
            Assert.Equal(0, quit.ParameterTypes.Length);
            Assert.Equal("ProcessorTycoon.GameManager", quit.TypeName);

            GameMemberSpec cheats = ScenariosSelfCheckRules.Members.First(m => m.Name == "enableCheatsToggle");
            Assert.Equal("ProcessorTycoon.UI.ToggleUI", cheats.ValueType);
            Assert.Equal("ProcessorTycoon.Menu.MenuGameSetup", cheats.TypeName);
            foreach (GameMemberSpec spec in ScenariosSelfCheckRules.Members.Where(m => m.TypeName.EndsWith(".MenuGameSetup", StringComparison.Ordinal)))
            {
                Assert.Equal(SelfCheckLevel.Warning, spec.Level, spec.Display + ": the start still writes the numbers of the scenario, so the lock is a convenience");
            }
        }

        [Test]
        public void TheMembersTable_MatchesTheCodeThatReadsThoseMembers()
        {
            string locks = Source("UI", "MenuSetupLocks.cs");
            Match fields = Regex.Match(locks, @"FieldNames\s*=\s*\{([^}]*)\}");
            Assert.True(fields.Success, "MenuSetupLocks.FieldNames");
            var read = Regex.Matches(fields.Groups[1].Value, "\"(\\w+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(4, read.Count);
            foreach (string name in read)
            {
                Assert.True(ScenariosSelfCheckRules.Members.Any(m => m.Name == name && m.TypeName.EndsWith(".MenuGameSetup", StringComparison.Ordinal)),
                    "the self-check does not look for MenuGameSetup." + name + ", which the lock reads");
            }

            Assert.True(Source("Integration", "DifficultyGuard.cs").Contains("___difficultySettings"), "the guard injects the field the self-check looks for");
            Assert.True(Source("Integration", "GameFacts.cs").Contains("manager.QuitToMenu()"), "the method the self-check looks for is the one the results window calls");

            string events = Source("Integration", "GameEventHooks.cs");
            Assert.True(events.Contains("OnCompanyWentBankrupt") && ScenariosSelfCheckRules.Members.Any(m => m.Kind == ProbeKind.Event && m.Name == "OnCompanyWentBankrupt"));
            Assert.True(events.Contains("OnPlayerSignedContract") && ScenariosSelfCheckRules.Members.Any(m => m.Kind == ProbeKind.Event && m.Name == "OnPlayerSignedContract"));
        }

        [Test]
        public void AMemberThatIsMissing_IsAFindingOfTheLevelOfItsRow()
        {
            foreach (GameMemberSpec spec in ScenariosSelfCheckRules.Members)
            {
                SelfCheckFacts facts = Healthy();
                facts.Members.First(m => m.Spec == spec).Problem = "no such member";
                SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

                Assert.Equal(1, verdict.Findings.Count);
                Assert.Equal(spec.Level, verdict.Findings[0].Level, spec.Display);
                Assert.Equal("game", verdict.Findings[0].Area);
                Assert.True(verdict.Findings[0].Message.StartsWith(spec.Display + ": no such member; ", StringComparison.Ordinal), verdict.Findings[0].Message);
                Assert.True(verdict.Findings[0].Message.EndsWith(spec.Consequence, StringComparison.Ordinal), verdict.Findings[0].Message);
            }
        }

        [Test]
        public void TheDisplayName_IsTheShortClassAndTheMember()
        {
            Assert.Equal("MenuGameSetup.x", new GameMemberSpec { TypeName = "ProcessorTycoon.Menu.MenuGameSetup", Name = "x" }.Display);
            Assert.Equal("Inner.y", new GameMemberSpec { TypeName = "A.Outer+Inner", Name = "y" }.Display);
            Assert.Equal("Lonely.z", new GameMemberSpec { TypeName = "Lonely", Name = "z" }.Display);
        }

        // ---- the content ----

        [Test]
        public void TheContentThatWasNotLoaded_IsAnError_AndNothingElseIsSaidAboutTheCounts()
        {
            SelfCheckFacts facts = Healthy();
            facts.ContentLoaded = false;
            facts.Scenarios = facts.Challenges = facts.Achievements = 0;
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "content", "the content was not loaded"), All(verdict));
        }

        [Test]
        public void AnEmptyCatalog_IsAnErrorForEachKind()
        {
            SelfCheckFacts facts = Healthy();
            facts.Scenarios = 0;
            facts.Challenges = 0;
            facts.Achievements = 0;
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(3, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "content", "no scenario is loaded"));
            Assert.True(Has(verdict, SelfCheckLevel.Error, "content", "no challenge is loaded"));
            Assert.True(Has(verdict, SelfCheckLevel.Error, "content", "no achievement is loaded"));
        }

        [Test]
        public void ARecordOfTheBuiltInFileThatWasRejected_IsAnError_ButOneOfThePlayersOverrideIsAWarning()
        {
            SelfCheckFacts builtin = Healthy();
            builtin.ContentFiles[0].Skipped = 2;
            builtin.ContentFiles[0].FirstError = "scenario 'x' rejected: start.year 1998 not allowed";
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(builtin);
            Assert.Equal(1, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "content", "scenarios.json: 2 record(s) rejected; the first: scenario 'x' rejected: start.year 1998 not allowed"), All(verdict));

            SelfCheckFacts overridden = Healthy();
            overridden.ContentFiles[2].Skipped = 1;
            overridden.ContentFiles[2].OverrideFound = true;
            verdict = ScenariosSelfCheckRules.Evaluate(overridden);
            Assert.Equal(0, verdict.Errors);
            Assert.Equal(1, verdict.Warnings);
            Assert.True(Has(verdict, SelfCheckLevel.Warning, "content", "achievements.json: 1 record(s) rejected"), All(verdict));
            Assert.True(Has(verdict, SelfCheckLevel.Warning, "content", "the override file of the player is in use"), All(verdict));
        }

        // ---- the channels ----

        [Test]
        public void AChannelThatTheCoreDoesNotDeclare_IsAnError_WithWhoWritesToIt()
        {
            SelfCheckFacts facts = Healthy();
            Assert.True(facts.Channels.Count >= 6, "the shipped challenges write to the channels of the core");
            facts.Channels.First(c => c.UsedBy == "challenge 'tight-credit'").Declared = false;
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Errors);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "channels", "the channel 'bank.creditLimit' used by challenge 'tight-credit' is not declared"), All(verdict));
        }

        [Test]
        public void TheShippedContent_WritesOnlyToTheChannelsTheCoreDeclares()
        {
            string core = ScenarioTestData.ReadCoreChannels();
            var used = new HashSet<string>(Scenarios.SelectMany(s => s.Modifiers).Concat(Challenges.SelectMany(c => c.Modifiers)).Select(m => m.Channel), StringComparer.Ordinal);
            Assert.True(used.Count >= 6, "the content uses at least the six channels of the challenges");
            foreach (string channel in used) Assert.True(core.Contains("\"" + channel + "\""), "core.json does not declare " + channel);
        }

        // ---- the strings ----

        [Test]
        public void EveryKeyTheContentAndTheCodeNeed_IsInBothShippedTables()
        {
            SelfCheckFacts facts = Healthy();
            Assert.True(facts.RequiredKeys.Count > 280, "the expected keys: " + facts.RequiredKeys.Count);
            foreach (string key in facts.RequiredKeys)
            {
                Assert.True(ScenarioTextKeys.IsPresent(facts.RussianKeys, key), "ru lacks " + key);
                Assert.True(ScenarioTextKeys.IsPresent(facts.EnglishKeys, key), "en lacks " + key);
            }
        }

        [Test]
        public void AKeyMissingInRussian_IsAnError_InEnglish_AWarning()
        {
            SelfCheckFacts facts = Healthy();
            facts.RussianKeys.Remove("scn.briefing.button");
            facts.EnglishKeys.Remove("ach.first-cpu.title");
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Errors);
            Assert.Equal(1, verdict.Warnings);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "strings", "the Russian table lacks 1 key(s): scn.briefing.button"), All(verdict));
            Assert.True(Has(verdict, SelfCheckLevel.Warning, "strings", "the English table lacks 1 key(s): ach.first-cpu.title"), All(verdict));
        }

        [Test]
        public void ManyMissingKeys_AreListedByTenAndTheRestIsCounted()
        {
            SelfCheckFacts facts = Healthy();
            var gone = facts.RussianKeys.Where(k => k.StartsWith("ach.", StringComparison.Ordinal)).Take(25).ToList();
            foreach (string key in gone) facts.RussianKeys.Remove(key);
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Errors);
            SelfCheckFinding finding = verdict.Findings.First(f => f.Level == SelfCheckLevel.Error);
            Assert.True(finding.Message.Contains("lacks 25 key(s)"), finding.Message);
            Assert.True(finding.Message.EndsWith(" and 15 more"), finding.Message);
            Assert.Equal(10, finding.Message.Split(new[] { "ach." }, StringSplitOptions.None).Length - 1);
        }

        [Test]
        public void ATableWithoutAnyKeyOfTheFeature_IsOneFinding_NotHundredsOfMissingKeys()
        {
            SelfCheckFacts facts = Healthy();
            facts.RussianKeys.Clear();
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(1, verdict.Findings.Count);
            Assert.True(Has(verdict, SelfCheckLevel.Error, "strings", "the Russian table has no string of the feature"), All(verdict));

            facts = Healthy();
            facts.EnglishKeys.Clear();
            verdict = ScenariosSelfCheckRules.Evaluate(facts);
            Assert.Equal(1, verdict.Findings.Count);
            Assert.True(Has(verdict, SelfCheckLevel.Warning, "strings", "the English table has no string of the feature"), All(verdict));
        }

        [Test]
        public void APluralGroup_CountsAsThePresenceOfItsKey()
        {
            var table = new HashSet<string>(StringComparer.Ordinal) { "scn.career.years.one", "scn.career.years.few", "scn.career.years.many", "scn.plain", "scn.en.one", "scn.en.other" };

            Assert.True(ScenarioTextKeys.IsPresent(table, "scn.career.years"));
            Assert.True(ScenarioTextKeys.IsPresent(table, "scn.en"));
            Assert.True(ScenarioTextKeys.IsPresent(table, "scn.plain"));
            Assert.False(ScenarioTextKeys.IsPresent(table, "scn.career"));
            Assert.False(ScenarioTextKeys.IsPresent(table, "scn.absent"));
            Assert.False(ScenarioTextKeys.IsPresent(null, "scn.plain"));
            Assert.False(ScenarioTextKeys.IsPresent(table, null));
        }

        [Test]
        public void ThePrefixes_DecideWhichKeysAreTheFeaturesOwn()
        {
            Assert.True(ScenarioTextKeys.IsOwnKey("scn.hub.title"));
            Assert.True(ScenarioTextKeys.IsOwnKey("chl.no-oem.title"));
            Assert.True(ScenarioTextKeys.IsOwnKey("ach.first-cpu.desc"));
            Assert.False(ScenarioTextKeys.IsOwnKey("core.common.close"));
            Assert.False(ScenarioTextKeys.IsOwnKey("onb.step.1"));
            Assert.False(ScenarioTextKeys.IsOwnKey(null));
            Assert.False(ScenarioTextKeys.IsOwnKey(string.Empty));
        }

        [Test]
        public void TheRequiredKeys_HoldTheTextsOfTheBuiltInRecords_AndNotThoseOfTheOnesThePlayerChanged()
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario("fabless-1985");
            scenario.Id = "my-own";
            scenario.TitleKey = "scn.my-own.title";
            scenario.IsCustom = true;

            List<string> keys = ScenarioTextKeys.Required(new[] { scenario, Scenarios[0] }, Challenges, Achievements);
            Assert.False(keys.Contains("scn.my-own.title"), "a custom record is the player's business");
            Assert.True(keys.Contains(Scenarios[0].TitleKey) && keys.Contains(Scenarios[0].BriefingKey));
            Assert.True(keys.Contains(Scenarios[0].Goals[0].TitleKey), "the title of a goal");

            string failKey = Scenarios.SelectMany(s => s.Fail).First().TextKey;
            Assert.True(ScenarioTextKeys.Required(Scenarios, Challenges, Achievements).Contains(failKey), "the reason of a rule of loss");
            Assert.True(ScenarioTextKeys.Required(Scenarios, Challenges, Achievements).Contains("chl.tight-credit.unavailable"), "the reason a challenge is unavailable");
            Assert.True(ScenarioTextKeys.Required(Scenarios, Challenges, Achievements).Contains("ach.first-cpu.title"));

            var sorted = new List<string>(keys);
            sorted.Sort(StringComparer.Ordinal);
            Assert.Equal(sorted.ToArray(), keys.ToArray());
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void TheFamilies_CoverTheKeysTheCodeBuildsFromAnId()
        {
            var families = ScenarioTextKeys.Families().ToList();
            foreach (string expected in new[]
                     {
                         "scn.medal.gold", "scn.medal.completed", "scn.medal.none", "scn.companyType.idm", "scn.companyType.foundry", "scn.difficulty.0", "scn.difficulty.5",
                         "scn.tech.0", "scn.tech.4", "ach.category.meta", "scn.param.strictAssisted.label", "scn.param.toastsEnabled.tooltip",
                     })
            {
                Assert.True(families.Contains(expected), "the families lack " + expected);
            }
        }

        [Test]
        public void EveryKeyTheCodeWrites_IsInTheListOfTheSelfCheck()
        {
            // the keys written as a literal anywhere in the feature, except the list itself (which would only find itself)
            var literal = new SortedSet<string>(StringComparer.Ordinal);
            Regex key = new Regex("\"((?:scn|chl|ach)\\.[A-Za-z0-9.\\-]*[A-Za-z0-9])\"");
            foreach (string path in SourceScan.Files(FeatureRoot(), ".cs"))
            {
                if (path.EndsWith("ScenarioTextKeys.cs", StringComparison.OrdinalIgnoreCase)) continue;
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in key.Matches(code)) literal.Add(m.Groups[1].Value);
            }
            Assert.True(literal.Count > 120, "the scan found only " + literal.Count + " keys: is it broken?");

            var known = new HashSet<string>(ScenarioTextKeys.Fixed.Concat(ScenarioTextKeys.Families()), StringComparer.Ordinal);
            var unlisted = literal.Where(k => !known.Contains(k)).ToList();
            Assert.True(unlisted.Count == 0, "keys of the code that the self-check does not look for (add them to ScenarioTextKeys.Fixed):\n  " + string.Join("\n  ", unlisted));

            var stale = ScenarioTextKeys.Fixed.Where(k => !literal.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "keys of the list that no code asks for any more (remove them from ScenarioTextKeys.Fixed):\n  " + string.Join("\n  ", stale));
            Assert.Equal(ScenarioTextKeys.Fixed.Count, ScenarioTextKeys.Fixed.Distinct().Count());
        }

        // ---- what could not be looked at ----

        [Test]
        public void APartThatCouldNotBeLookedAt_IsAWarning_NotAGuess()
        {
            SelfCheckFacts facts = Healthy();
            facts.NotLooked.Add("the strings: NullReferenceException: boom");
            SelfCheckVerdict verdict = ScenariosSelfCheckRules.Evaluate(facts);

            Assert.Equal(0, verdict.Errors);
            Assert.Equal(1, verdict.Warnings);
            Assert.True(Has(verdict, SelfCheckLevel.Warning, "check", "could not look at the strings: NullReferenceException: boom"), All(verdict));
        }

        [Test]
        public void AFindingReadsAsAreaAndMessage()
        {
            Assert.Equal("patches: x", new SelfCheckFinding { Area = "patches", Message = "x" }.ToString());
        }

        // ---- the game side, read as text ----

        private static string FeatureRoot()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
        }

        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { FeatureRoot() }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void TheCheck_RunsOnceInOnEnabled_AfterTheCatalogIsLoaded()
        {
            string feature = Source("ScenariosFeature.cs");
            int enabled = feature.IndexOf("public void OnEnabled()", StringComparison.Ordinal);
            int load = feature.IndexOf("ScenarioCatalog.EnsureLoaded();", enabled, StringComparison.Ordinal);
            int check = feature.IndexOf("ScenariosSelfCheck.RunOnce();", enabled, StringComparison.Ordinal);
            int raise = feature.IndexOf("Raise(Enabled, \"OnEnabled\");", enabled, StringComparison.Ordinal);
            Assert.True(enabled >= 0 && load > enabled && check > load && raise > check, "OnEnabled: load the catalog, then the self-check, then tell the windows");

            string code = Source("ScenariosSelfCheck.cs");
            Assert.True(Regex.IsMatch(code, @"RunOnce\(\)\s*\{\s*if \(_ran\) return;\s*_ran = true;\s*Run\(\);"), "once per session");
        }

        [Test]
        public void AFeatureThePatchesOfWhichFailed_StillGetsTheCheck_WhenTheCoreMakesItUnavailable()
        {
            string watch = Source("Integration", "FeatureWatch.cs");
            string handler = Between(watch, "internal static void OnStateChanged(", "ModHooks.Guard(");
            Assert.True(Regex.IsMatch(handler, @"if \(state == FeatureState\.Unavailable && featureId == ScenariosFeature\.FeatureId\) ScenariosSelfCheck\.RunOnce\(\);"),
                "the check runs before the guard, which a feature that is unavailable does not pass");
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "' in the source");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        [Test]
        public void TheCheck_WritesTheLineOfTheAcceptance_UnderTheIdOfTheFeature()
        {
            string code = Source("ScenariosSelfCheck.cs");
            Assert.True(code.Contains("ModLog.Info(FeatureId, verdict.Summary);"), "self-check: OK goes to the log at the level INFO");
            Assert.True(code.Contains("ModLog.Error(FeatureId, \"self-check: \" + finding)") && code.Contains("ModLog.Warn(FeatureId, \"self-check: \" + finding)"), "a line per finding");
            Assert.True(code.Contains("private const string FeatureId = ScenariosFeature.FeatureId;"));
            Assert.Equal("scenarios-achievements", ScenarioTestData.FeatureId);
        }

        [Test]
        public void TheCheck_AsksEachGroupInItsOwnTryCatch_AndNeverThrows()
        {
            string code = Source("ScenariosSelfCheck.cs");
            foreach (string group in new[] { "CollectPatches", "CollectMembers", "CollectContent", "CollectChannels", "CollectStrings" })
            {
                Assert.True(Regex.IsMatch(code, @"Ask\(facts, ""[^""]+"", \(\) => " + group + @"\(facts\)\);"), group + " runs inside Ask");
            }

            string ask = code.Substring(code.IndexOf("private static void Ask(", StringComparison.Ordinal));
            Assert.True(ask.Contains("catch (Exception ex)") && ask.Contains("facts.NotLooked.Add("), "a question that cannot be asked is a finding of the check");

            string run = code.Substring(code.IndexOf("internal static SelfCheckVerdict Run()", StringComparison.Ordinal));
            Assert.True(run.Contains("catch (Exception ex)") && run.Contains("the self-check itself failed"), "the check itself is guarded");
            Assert.Equal(2, Regex.Matches(run.Substring(0, run.IndexOf("private static void Write(", StringComparison.Ordinal)), @"catch \(Exception").Count, "the collecting and the writing of the log are guarded, each on its own");
        }

        [Test]
        public void TheCheck_ReadsTheRegistriesOfTheCore_AndOnlyReads()
        {
            string code = Source("ScenariosSelfCheck.cs");
            foreach (string call in new[]
                     {
                         "ModHooks.Report", "PatchStatus.Applied", "ModContent.ReportOf(FeatureId, file)", "ModModifiers.IsDeclared(modifier.Channel)", "ModL10n.Keys(\"ru\")", "ModL10n.Keys(\"en\")",
                         "ScenarioTextKeys.Required(", "new GameMemberProbe()", "ScenariosSelfCheckRules.Members", "ScenariosSelfCheckRules.Evaluate(Collect())",
                     })
            {
                Assert.True(code.Contains(call), "the check uses " + call);
            }
            foreach (string write in new[] { "ModSettings.Set", "ModModifiers.Add(", "ModProfile.Save", "ModProfile.MarkDirty", "ctx.Patch(", "ModNotify.Show" })
            {
                Assert.False(code.Contains(write), "the check changes nothing and tells the player nothing: " + write);
            }
        }

        [Test]
        public void TheProbe_LooksTheClassUpByItsFullName_InTheGamesAssemblyFirst()
        {
            string code = Source("Integration", "GameMemberProbe.cs");
            Assert.True(code.Contains("AppDomain.CurrentDomain.GetAssemblies()") && code.Contains("\"Assembly-CSharp\""), "the game's assembly is found by name");
            Assert.True(code.Contains("_game.GetType(name, false)"), "a direct lookup first (AccessTools.TypeByName walks every type of every assembly)");
            Assert.True(code.Contains("AccessTools.Field(type, spec.Name)") && code.Contains("AccessTools.Method(type, spec.Name, parameters)") && code.Contains("type.GetEvent(spec.Name, AnyEvent)"));
            Assert.True(code.Contains("catch (Exception ex)"), "a lookup that throws is an answer, not an exception");
            Assert.False(code.Contains("typeof(ProcessorTycoon"), "the probe finds the types by name: a class the game renamed must not stop it from loading");
        }

        [Test]
        public void TheCheck_IsDocumentedInTheRequestsFileAsHavingNoHookInTheCore()
        {
            string root = SourceScan.SrcDirOrFail();
            string path = Path.GetFullPath(Path.Combine(root, "..", "..", "requests", "scenarios-achievements.md"));
            string text = SourceScan.ReadText(path);
            Assert.True(text.Contains("ModSelfCheck"), "the requests file names the missing point in ModSelfCheck");
        }
    }
}
