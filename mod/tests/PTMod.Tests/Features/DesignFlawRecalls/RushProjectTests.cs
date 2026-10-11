using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-029 (FR-20, FR-14; AC-20.2): the rush project in the CPU designer. The pending projects (the name of the CPU and the planned end), their match with the CPU that arrives
    /// when the project ends (a name and 3 days of tolerance), their life through a save and a load, the mark <c>rushCpuIds</c> that the roll reads (<c>kRush</c> in the logged
    /// probability), the variant that shares the mark of its base CPU and the AI that never has one. The patches and the switch are compiled only by the game build: the source
    /// tests at the end check their shape (Optional patches, the targets, the private fields, the single warning, the strings in both languages).
    /// </summary>
    [Covers(typeof(RushProjectBook))]
    public class RushProjectTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        private sealed class Books
        {
            public readonly FlawSaveSection Section = new FlawSaveSection(() => new int[0], null);
            public readonly FlawStore Store;
            public readonly RushProjectBook Book;

            public Books()
            {
                Store = new FlawStore(() => Section.Data);
                Book = new RushProjectBook(Store);
            }
        }

        // ---- the pending projects ----

        [Test]
        public void Register_RemembersTheNameAndThePlannedEnd_OfTheCompanyOfThePlayer()
        {
            var b = new Books();

            Assert.True(b.Book.Register("Nova 3", Day + 300, Day));

            Assert.Equal(1, b.Book.Count);
            RushProjectDto project = b.Store.Data.rushProjects[0];
            Assert.Equal("Nova 3", project.cpuName);
            Assert.Equal(Day + 300, project.plannedEndDay);
            Assert.Equal("player", project.companyKey, "the AI does not rush: the only company is the player");
            Assert.Equal(1, b.Book.Snapshot().Count);
            Assert.False(ReferenceEquals(b.Book.Snapshot()[0], project), "a snapshot is a copy");
        }

        [Test]
        public void Register_TheSameProjectTwice_IsOnePendingProject()
        {
            var b = new Books();
            b.Book.Register("Nova 3", Day + 300, Day);
            Assert.True(b.Book.Register("Nova 3", Day + 300, Day), "it is remembered");

            Assert.Equal(1, b.Book.Count);

            b.Book.Register("Nova 3", Day + 301, Day);
            b.Book.Register("Nova 4", Day + 300, Day);
            Assert.Equal(3, b.Book.Count, "another end or another name is another project");
        }

        [Test]
        public void Register_RefusesAnEmptyName_AndAnEndBeforeTheStart()
        {
            var b = new Books();

            Assert.False(b.Book.Register(null, Day + 10, Day));
            Assert.False(b.Book.Register("", Day + 10, Day));
            Assert.False(b.Book.Register("Nova", 0, Day));
            Assert.False(b.Book.Register("Nova", Day - 1, Day));
            Assert.Equal(0, b.Book.Count);
        }

        // ---- the match (FR-20 item 3: the name and plus or minus 3 days) ----

        [Test]
        public void Complete_ACpuOfTheNameThatArrivesWithinThreeDays_BecomesRushed()
        {
            foreach (int offset in new[] { -3, -2, -1, 0, 1, 2, 3 })
            {
                var b = new Books();
                b.Book.Register("Nova 3", Day + 300, Day);

                bool matched = b.Book.Complete("Nova 3", 2051, Day + 300 + offset);

                Assert.True(matched, "arrived " + offset + " days from the plan");
                Assert.True(b.Store.IsRush(2051), "the SaveID is in rushCpuIds (AC-20.2)");
                Assert.Equal(new List<int> { 2051 }, b.Store.Data.rushCpuIds);
                Assert.Equal(0, b.Book.Count, "the pending project turned into the mark");
            }
        }

        [Test]
        public void Complete_ACpuThatArrivesFourDaysOffThePlan_IsNotThatProject()
        {
            foreach (int offset in new[] { -4, -10, 4, 10 })
            {
                var b = new Books();
                b.Book.Register("Nova 3", Day + 300, Day);

                Assert.False(b.Book.Complete("Nova 3", 2051, Day + 300 + offset), "arrived " + offset + " days from the plan");
                Assert.False(b.Store.IsRush(2051));
                Assert.Equal(1, b.Book.Count, "the project still waits");
            }
        }

        [Test]
        public void Complete_ADifferentName_IsNotTheProject()
        {
            var b = new Books();
            b.Book.Register("Nova 3", Day + 300, Day);

            Assert.False(b.Book.Complete("Nova 4", 2051, Day + 300));
            Assert.False(b.Book.Complete("nova 3", 2051, Day + 300), "the names are compared exactly");
            Assert.False(b.Book.Complete(null, 2051, Day + 300));
            Assert.False(b.Book.Complete("", 2051, Day + 300));
            Assert.False(b.Store.IsRush(2051));
            Assert.Equal(1, b.Book.Count);
        }

        [Test]
        public void Complete_AnOrdinaryProject_FindsNothing_AndMarksNothing()
        {
            var b = new Books();

            Assert.False(b.Book.Complete("Plain", 100, Day));
            Assert.Equal(0, b.Store.Data.rushCpuIds.Count);
        }

        [Test]
        public void Complete_TakesTheProjectOffTheList_SoASecondCpuOfTheNameIsNotRushed()
        {
            var b = new Books();
            b.Book.Register("Nova 3", Day + 300, Day);

            Assert.True(b.Book.Complete("Nova 3", 2051, Day + 300));
            Assert.False(b.Book.Complete("Nova 3", 2052, Day + 300), "one project makes one CPU");
            Assert.False(b.Store.IsRush(2052));
        }

        [Test]
        public void Complete_OfTwoProjectsOfTheSameName_TheNearestEndWins()
        {
            var b = new Books();
            b.Book.Register("Nova", Day + 300, Day);
            b.Book.Register("Nova", Day + 302, Day);

            Assert.True(b.Book.Complete("Nova", 1, Day + 302));
            Assert.Equal(1, b.Book.Count);
            Assert.Equal(Day + 300, b.Store.Data.rushProjects[0].plannedEndDay, "the project that ended on day +302 is the one that was taken");

            Assert.True(b.Book.Complete("Nova", 2, Day + 300));
            Assert.Equal(0, b.Book.Count);
            Assert.Equal(new List<int> { 1, 2 }, b.Store.Data.rushCpuIds);
        }

        [Test]
        public void Complete_OnATie_TheFirstRegisteredWins()
        {
            var b = new Books();
            b.Book.Register("Nova", Day + 298, Day);
            b.Book.Register("Nova", Day + 302, Day);

            Assert.True(b.Book.Complete("Nova", 1, Day + 300));

            Assert.Equal(Day + 302, b.Store.Data.rushProjects[0].plannedEndDay);
        }

        [Test]
        public void Complete_NeverMatchesAProjectOfAnotherCompany()
        {
            var b = new Books();
            b.Store.Data.rushProjects.Add(new RushProjectDto { companyKey = "ai:3", cpuName = "Nova", plannedEndDay = Day + 10 });

            Assert.False(b.Book.Complete("Nova", 7, Day + 10), "the AI does not rush: a project of the AI in the save is not honoured");
            Assert.False(b.Store.IsRush(7));
        }

        [Test]
        public void Complete_ASecondMarkOfTheSameCpu_ChangesNothing()
        {
            var b = new Books();
            b.Book.Register("A", Day + 10, Day);
            b.Book.Register("B", Day + 10, Day);
            b.Book.Complete("A", 5, Day + 10);
            b.Book.Complete("B", 5, Day + 10);   // the same SaveID can not happen in the game, but the mark is a set

            Assert.Equal(new List<int> { 5 }, b.Store.Data.rushCpuIds);
        }

        // ---- the pruning ----

        [Test]
        public void Prune_DropsTheProjectsThatEndedLongAgo_AndKeepsTheOthers()
        {
            var b = new Books();
            b.Book.Register("Old", Day + 10, Day);
            b.Book.Register("Late", Day + 100, Day);

            Assert.Equal(0, b.Book.Prune(Day + 10 + RushProjectBook.StaleAfterDays), "exactly at the margin the project is kept");
            Assert.Equal(1, b.Book.Prune(Day + 10 + RushProjectBook.StaleAfterDays + 1));
            Assert.Equal(1, b.Book.Count);
            Assert.Equal("Late", b.Store.Data.rushProjects[0].cpuName);
            Assert.Equal(0, b.Book.Prune(0), "outside the game (no date) nothing is dropped");
        }

        [Test]
        public void Register_DropsTheStaleOnes_AndDoesNotGrowWithoutBound()
        {
            var b = new Books();
            b.Book.Register("Gone", Day + 5, Day);

            Assert.True(b.Book.Register("New", Day + 500, Day + 5 + RushProjectBook.StaleAfterDays + 10));
            Assert.Equal(1, b.Book.Count, "the stale project went with the registration");

            var c = new Books();
            for (int i = 0; i < RushProjectBook.MaxPending + 20; i++) c.Book.Register("P" + i, Day + 1000 + i, Day);
            Assert.Equal(RushProjectBook.MaxPending, c.Book.Count);
            Assert.Equal("P20", c.Store.Data.rushProjects[0].cpuName, "the oldest were dropped");
            Assert.Equal("P" + (RushProjectBook.MaxPending + 19), c.Store.Data.rushProjects.Last().cpuName);
        }

        [Test]
        public void Prune_DropsBrokenEntries()
        {
            var b = new Books();
            b.Store.Data.rushProjects.Add(null);
            b.Store.Data.rushProjects.Add(new RushProjectDto { companyKey = "player", cpuName = "", plannedEndDay = Day + 5 });
            b.Store.Data.rushProjects.Add(new RushProjectDto { companyKey = "player", cpuName = "Ok", plannedEndDay = Day + 5 });

            Assert.Equal(2, b.Book.Prune(Day));
            Assert.Equal(1, b.Book.Count);
        }

        // ---- the save (rushProjects, rushCpuIds) ----

        [Test]
        public void APendingProject_SurvivesASaveAndALoad_AndStillBecomesTheMark()
        {
            var first = new Books();
            first.Book.Register("Nova 3", Day + 240, Day);
            first.Book.Register("Nova 4", Day + 400, Day);

            JsonObject written = first.Section.Write();
            Assert.True(ModJson.Write(written, false).Contains("Nova 3"), "the pending project is in the section");

            var loaded = new Books();
            loaded.Section.Read(FlawTestKit.ParseObject(ModJson.Write(written, false)), 1);
            Assert.Equal(2, loaded.Book.Count);

            Assert.True(loaded.Book.Complete("Nova 3", 2051, Day + 241), "the project of the old session ends in the new one (a day off the plan)");
            Assert.True(loaded.Store.IsRush(2051));
            Assert.Equal(1, loaded.Book.Count, "the other waits");

            JsonObject again = loaded.Section.Write();
            var third = new Books();
            third.Section.Read(FlawTestKit.ParseObject(ModJson.Write(again, false)), 1);
            Assert.True(third.Store.IsRush(2051), "the mark is saved too");
            Assert.Equal(1, third.Book.Count);
        }

        [Test]
        public void TheDesignExample_HoldsAPendingProjectAndAMark_AndBothAreRead()
        {
            var b = new Books();
            b.Section.Read(FlawTestKit.ParseObject(FlawTestKit.DesignSaveExample()), 1);

            Assert.True(b.Store.IsRush(2051), "rushCpuIds: [2051]");
            Assert.Equal(1, b.Book.Count);
            Assert.True(b.Book.Complete("Nova 3", 2060, 17240), "rushProjects: Nova 3 ends on day 17240");
            Assert.True(b.Store.IsRush(2060));
        }

        // ---- the roll reads the mark (AC-20.2) ----

        private sealed class RollHost : IFlawRollHost
        {
            public FlawBalance Balance_ = FlawBalance.Defaults();
            public IModRng Rng_ = FakeRng.Always(0.99f);   // a miss: the point is the logged probability
            public readonly List<string> Lines = new List<string>();

            public FlawBalance Balance { get { return Balance_; } }
            public IModRng Rng { get { return Rng_; } }
            public float FlawFrequency { get { return 1f; } }
            public bool AiFlawsEnabled { get { return true; } }
            public bool SaveLocked { get { return false; } }
            public int Today { get { return Day; } }
            public float Inflation { get { return 1f; } }
            public void Log(string message) { Lines.Add(message); }
        }

        /// <summary>The first row of the table of design.md: 1980, failure rate 0.18, no overclock, no heat, a node at 60 percent. p = 0.070, with a rush project 0.118.</summary>
        private static FlawRiskInput Design1980()
        {
            return new FlawRiskInput
            {
                FailureRate = 0.18f,
                Frequency = 100f,
                MaxFrequency = 100f,
                Temperature = 70f,
                SafeTemperature = 80f,
                HasNodeRecord = true,
                NodeProgress = 0.6f,
                Year = 1980,
                Difficulty = 2,
            };
        }

        [Test]
        public void AC20_2_ARushedProjectThatEnded_RollsWithKRush_InTheLoggedProbability()
        {
            var b = new Books();
            var host = new RollHost();
            var engine = new FlawRollEngine(b.Store, host);
            b.Book.Register("Nova 3", Day + 300, Day);
            b.Book.Complete("Nova 3", 2051, Day + 300);   // ProjectCompletedHandler.AddCpu

            engine.OnRelease("player", false, 2051, -1, false, false, Design1980);      // the release of the rushed CPU
            engine.OnRelease("player", false, 2052, -1, false, false, Design1980);      // an ordinary CPU of the same design

            Assert.Equal(2, host.Lines.Count);
            Assert.True(host.Lines[0].StartsWith("roll cpu=2051 p=0.118 ", StringComparison.Ordinal), host.Lines[0]);
            Assert.True(host.Lines[1].StartsWith("roll cpu=2052 p=0.070 ", StringComparison.Ordinal), host.Lines[1]);
        }

        [Test]
        public void ARushMarkedCpuOfAnAiCompany_RollsWithoutKRush()
        {
            var b = new Books();
            var host = new RollHost();
            var engine = new FlawRollEngine(b.Store, host);
            b.Store.MarkRush(3000);   // a mark that could only come from a broken save

            engine.OnRelease("ai:3", true, 3000, -1, false, false, Design1980);
            engine.OnRelease("ai:3", true, 3001, -1, false, false, Design1980);

            string p3000 = Probability(host.Lines[0]);
            string p3001 = Probability(host.Lines[1]);
            Assert.Equal(p3001, p3000, "FR-20 item 6: the AI never rushes, so the mark changes nothing for it");
        }

        [Test]
        public void AVariant_SharesTheMarkOfItsBaseCpu_ItNeverRollsAndJoinsTheRecordOfTheBase()
        {
            var b = new Books();
            var host = new RollHost { Rng_ = FakeRng.After(0.5f, 0.0f, 0.5f, 0.0f) };   // the roll of the base hits (0.0 < 0.118)
            var engine = new FlawRollEngine(b.Store, host);
            b.Store.MarkRush(2051);

            Assert.Equal(FlawRollOutcome.FlawCreated, engine.OnRelease("player", false, 2051, -1, false, false, Design1980));
            Assert.True(host.Lines[0].Contains("p=0.118"), "the base CPU rolled as a rushed one: " + host.Lines[0]);

            FlawRollOutcome variant = engine.OnRelease("player", false, 2059, 2051, true, false, Design1980);

            Assert.Equal(FlawRollOutcome.VariantAttached, variant, "D10: the variant has the flaw of its base, with the mark the base had");
            Assert.Equal(1, host.Lines.Count, "and no second roll");
            Assert.False(b.Store.IsRush(2059), "the variant is not marked itself: it has no roll to be marked for");
            Assert.True(b.Store.FindByCpu(2051).CoversCpu(2059));
        }

        // ---- the hint of the risk follows the switch (FR-14 item 2) ----

        [Test]
        public void TheHint_WithTheSwitchOn_ShowsTheHigherRisk_ByKRush()
        {
            FlawBalance balance = FlawBalance.Defaults();
            FlawRiskInput design = Design1980();

            float off = FlawRiskHint.Probability(design, balance, 1f, false);
            float on = FlawRiskHint.Probability(design, balance, 1f, true);

            Assert.Near(0.070, off, 0.001);
            Assert.Near(0.118, on, 0.001);
            Assert.True(FlawRiskHint.Percent(on) >= FlawRiskHint.Percent(off), "the rounded number does not fall");
        }

        // ---- the source: what only the game build compiles ----

        [Test]
        public void Source_ThePatches_AreOptional_AndAimAtTheFiveTargetsOfTheDesign()
        {
            string code = Code("Patches/RushProjectPatches.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CreatorCpu\), ""Project"", PatchKind\.Postfix, ArgTypes = new Type\[0\], Optional = true\)\]"), "postfix CreatorCpu.Project");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CreatorCpu\), ""CreateCpu"", PatchKind\.Prefix, ArgTypes = new Type\[0\], Optional = true\)\]"), "prefix CreatorCpu.CreateCpu");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CreatorCpu\), ""CreateCpu"", PatchKind\.Postfix, ArgTypes = new Type\[0\], Optional = true\)\]"), "the switch is off again after it");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CreatorCpu\), ""UpdateUI"", PatchKind\.Postfix, ArgTypes = new\[\] \{ typeof\(bool\) \}, Optional = true\)\]"), "the switch is made when the window opens");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(ProjectCompletedHandler\), ""AddCpu"", PatchKind\.Postfix, ArgTypes = new\[\] \{ typeof\(GameCpu\) \}, Optional = true\)\]"),
                "postfix ProjectCompletedHandler.AddCpu");
            Assert.Equal(FlawApiKit.Occurrences(code, "[ModPatch("), FlawApiKit.Occurrences(code, "Optional = true"), "every patch of the class is Optional");
            Assert.Equal(5, FlawApiKit.Occurrences(code, "[ModPatch("));
            Assert.True(Code("DesignFlawRecallsFeature.cs").Contains("ctx.Patch(typeof(RushProjectPatches));"), "declared in Register");
        }

        [Test]
        public void Source_TheProjectPatch_UsesRushMath_OnTheTwoPrivateFields_AndGuardsItselfInPlace()
        {
            string code = Code("Patches/RushProjectPatches.cs");
            string body = FlawApiKit.Between(code, "private static void PostfixProject(", "[ModPatch(typeof(CreatorCpu), \"CreateCpu\", PatchKind.Prefix");

            Assert.True(body.Contains("ref int ___projectTime") && body.Contains("ref float ___projectCost"), "the private fields of the designer, injected by name");
            Assert.True(body.Contains("RushMath.ApplyTime(___projectTime, balance)") && body.Contains("RushMath.ApplyCost(___projectCost, balance)"), "the term and the cost through RushMath");
            Assert.True(body.Contains("option.IsOn"), "only with the switch on");
            Assert.True(body.Contains("ModHooks.CanRun(") && body.Contains("ModHooks.ReportFault("), "it runs every frame: the guard is spelled out, no closure");
            Assert.False(body.Contains("=>"), "no lambda in the per-frame patch");
        }

        [Test]
        public void Source_TheCreatePrefix_RemembersTheNameAndTheShownTerm_AndTheAddCpuPostfixMakesTheMark()
        {
            string code = Code("Patches/RushProjectPatches.cs");
            string option = Code("RushProjectOption.cs");

            Assert.True(code.Contains("TMP_InputField ___nameInput, int ___projectTime"), "the prefix reads the name typed and the term shown (already rushed)");
            Assert.True(code.Contains("option.OnProjectCreating(name, ___projectTime)"));
            Assert.True(code.Contains("option.OnProjectCompleted(cpu)"));
            Assert.True(option.Contains("_book.Register(cpuName, today + projectTime, today)"), "the planned end is today plus the shown term");
            Assert.True(option.Contains("_book.Complete(cpu.Name, cpu.SaveID, today)"), "the CPU is found by its name and the day");
            Assert.True(code.Contains("ModHooks.Guard(Feature, CreatingSite") && code.Contains("ModHooks.Guard(Feature, CompletedSite") && code.Contains("ModHooks.Guard(Feature, UiSite"), "the other patches use the guard");
        }

        [Test]
        public void Source_TheSwitch_IsAModUiToggle_OffByDefault_NextToTheCreateButton_AndHiddenByTheParameter()
        {
            string option = Code("RushProjectOption.cs");

            Assert.True(option.Contains("ModUI.Toggle(parent, ModL10n.L(\"dfr.rush.toggle\"), false, OnToggled, tooltip)"), "the switch of the designer, off for a new project");
            Assert.True(option.Contains("ModL10n.L(\"dfr.rush.tooltip\", RushMath.TimeCutPercent(rush), RushMath.CostPremiumPercent(rush))"), "the tooltip tells the numbers of the file");
            Assert.True(option.Contains("AccessTools.Field(typeof(CreatorCpu), \"createCpuButton\")"), "next to the button that creates the CPU");
            Assert.True(option.Contains("FlawSettings.RushEnabled"), "rushEnabled = false hides the switch");
            Assert.True(option.Contains("ModHooks.IsApplied(typeof(RushProjectPatches))"), "without every patch there is no switch");
            Assert.True(option.Contains("SetVisible(false)") && option.Contains("_on = false"), "a hidden switch is off");
            Assert.True(option.Contains("internal bool IsOn") && option.Contains("_on && _available"), "IsOn is the state of a switch that is offered");
        }

        [Test]
        public void Source_AMissingTargetOrField_GivesOneWarning_AndNoSwitch()
        {
            string option = Code("RushProjectOption.cs");

            Assert.Equal(1, FlawApiKit.Occurrences(option, "ModLog.Warn("), "one place writes the warning");
            Assert.True(option.Contains("if (_warned) return;") && option.Contains("_warned = true;"), "and only once");
            Assert.True(option.Contains("CreatorCpu has no field 'createCpuButton'"), "the missing field is named");
            Assert.True(option.Contains("a patch of the designer is not applied"), "a patch that did not apply is named");
            Assert.True(option.Contains("if (!Wanted())") && option.Contains("Hide();"), "no switch: hidden, and the rest of the feature is untouched");
        }

        [Test]
        public void Source_TheSwitchIsOffAgainAfterTheProjectExists_AndWhenTheFeatureStops()
        {
            string option = Code("RushProjectOption.cs");
            string created = FlawApiKit.Between(option, "internal void OnProjectCreated()", "internal void OnProjectCompleted(");
            Assert.True(created.Contains("_on = false;") && created.Contains("ResetToggle();"), "off for every new project");

            string feature = Code("DesignFlawRecallsFeature.cs");
            Assert.True(feature.Contains("ModHooks.Guard(FeatureId, \"feature/rushHide\", Rush.Hide);"), "OnDisabled hides the switch (its patches are removed with the feature)");
            Assert.True(feature.Contains("DesignerRiskHint.RushSource = Rush.IsRushOn;"), "the hint of the risk follows the switch");
        }

        [Test]
        public void Source_TheMarkIsWrittenOnlyByTheRushOption_AndTheAiHasNoWayToGetOne()
        {
            Dictionary<string, string> all = FlawApiKit.AllCode();
            foreach (KeyValuePair<string, string> file in all)
            {
                if (file.Key == "Logic/FlawStore.cs" || file.Key == "Logic/RushProjectBook.cs") continue;
                Assert.False(file.Value.Contains(".MarkRush("), file.Key + " must not mark a CPU as rushed");
            }
            Assert.True(all["Logic/RushProjectBook.cs"].Contains("_store.MarkRush(cpuSaveId)"));
            Assert.True(all["Logic/RushProjectBook.cs"].Contains("PlayerKey = \"player\""), "only the player's projects are honoured");
        }

        [Test]
        public void Strings_OfTheSwitch_AreInBothLanguages_WithTheTwoNumbers()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> table = FlawApiKit.Own(language);
                Assert.True(table.ContainsKey("dfr.rush.toggle"), language + " has dfr.rush.toggle");
                Assert.True(table.ContainsKey("dfr.rush.tooltip"), language + " has dfr.rush.tooltip");
                Assert.True(table["dfr.rush.tooltip"].Contains("{0}") && table["dfr.rush.tooltip"].Contains("{1}"), language + ": the cut and the premium are placeholders");
                Assert.False(table["dfr.rush.toggle"].Contains("{"), language + ": a caption");
            }
            Assert.Equal("Ускоренный проект", FlawApiKit.Own("ru")["dfr.rush.toggle"]);
            Assert.Equal("Rush project", FlawApiKit.Own("en")["dfr.rush.toggle"]);
            Assert.Equal("Срок −{0}%, стоимость +{1}%, выше риск скрытого брака", FlawApiKit.Own("ru")["dfr.rush.tooltip"]);
            Assert.Equal("Time −{0}%, cost +{1}%, higher risk of a hidden flaw", FlawApiKit.Own("en")["dfr.rush.tooltip"]);
        }

        [Test]
        public void Strings_EveryKeyTheRushCodeNames_IsInTheTables()
        {
            var keys = new List<string>();
            keys.AddRange(FlawApiKit.KeyLiterals(Code("RushProjectOption.cs")));
            keys.AddRange(FlawApiKit.KeyLiterals(Code("Patches/RushProjectPatches.cs")));
            keys.AddRange(FlawApiKit.KeyLiterals(Code("FlawTransferHandler.cs")));

            Assert.True(keys.Contains("dfr.rush.toggle") && keys.Contains("dfr.rush.tooltip"));
            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> table = FlawApiKit.Own(language);
                foreach (string key in keys.Distinct())
                {
                    Assert.True(table.ContainsKey(key), language + " lacks " + key);
                }
            }
        }

        private static string Probability(string line)
        {
            Match match = Regex.Match(line, @"p=([0-9.]+)");
            Assert.True(match.Success, line);
            return match.Groups[1].Value;
        }

        private static string Code(string relative)
        {
            return FlawApiKit.Code(relative);
        }
    }
}
