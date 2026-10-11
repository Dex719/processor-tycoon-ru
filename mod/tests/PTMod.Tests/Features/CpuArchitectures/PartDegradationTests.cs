using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.CpuArchitectures;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The degradation of the parts (TSK-030, NFR-2): when the game lacks a member the feature stands on, only the part that stands on it goes off, and the rest goes on. A missing private
    /// field of <c>CreatorCpu</c> takes the designer away (the rating and the research go on); a missing <c>CurrentResearchUI.targetTechnology</c> takes the interruption of the player's
    /// technology away (<c>LabUi</c>) and nothing else. The verdicts are <see cref="PartCheck"/>; the table of members is the one of <c>ArchSelfCheck</c>, read from its source, so the test
    /// checks the table the game really uses. In the debug build a member can be hidden by a flag of <c>ptmod_settings.json</c> to see this happen without another version of the game.
    /// </summary>
    [Covers(typeof(PartCheck))]
    public class PartDegradationTests
    {
        private sealed class TableRow
        {
            public string Part, Owner, Member;
            public bool Optional;

            public override string ToString()
            {
                return Part + ": " + Owner + "." + Member + (Optional ? " (optional)" : string.Empty);
            }
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "CpuArchitectures");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static readonly Regex RowPattern = new Regex(
            @"(?:Row\s+(?<var>\w+)\s*=\s*|yield return\s+)(?<kind>Method|Patched|Field)\(ArchParts\.(?<part>\w+),\s*typeof\((?<owner>[\w.]+)\),\s*""(?<member>\w+)""\s*[,)]", RegexOptions.Compiled);

        /// <summary>The members of the table of <c>ArchSelfCheck</c> that are named by a literal (the series of the market data are built in a loop and are not here).</summary>
        private static List<TableRow> Table()
        {
            string code = Code("ArchSelfCheck.cs");
            var rows = new List<TableRow>();
            foreach (Match match in RowPattern.Matches(code))
            {
                string owner = match.Groups["owner"].Value;
                string variable = match.Groups["var"].Value;
                bool optional = variable.Length > 0 && Regex.IsMatch(code, @"\b" + Regex.Escape(variable) + @"\.Optional\s*=\s*true");
                rows.Add(new TableRow
                {
                    Part = match.Groups["part"].Value,
                    Owner = owner.Substring(owner.LastIndexOf('.') + 1),
                    Member = match.Groups["member"].Value,
                    Optional = optional,
                });
            }
            return rows;
        }

        /// <summary>The self-check over the table with the given members hidden (<c>owner.member</c>): the parts that go off.</summary>
        private static List<string> OffWhenHidden(List<TableRow> table, params string[] hidden)
        {
            var check = new PartCheck();
            foreach (TableRow row in table)
            {
                bool isHidden = hidden.Contains(row.Owner + "." + row.Member);
                check.Add(row.Part, row.Owner + "." + row.Member, isHidden ? PartCheck.HiddenState : PartCheck.Ok, row.Optional);
            }
            return check.OffParts();
        }

        // ---- the verdicts ----

        [Test]
        public void ARequiredMemberThatIsMissing_SwitchesItsPartOff_AndOnlyThatPart()
        {
            var check = new PartCheck();
            check.Add("Designer", "CreatorCpu.ipc", PartCheck.Ok, false);
            check.Add("Designer", "CreatorCpu.coreCountDropdown", "missing", false);
            check.Add("Research", "ResearchSector.StopResearching()", PartCheck.Ok, false);

            Assert.True(check.IsOff("Designer"));
            Assert.False(check.IsOff("Research"));
            Assert.False(check.IsOff("Ai"), "a part nothing was recorded for works");
            Assert.Equal(1, check.OffParts().Count);
            Assert.Equal("Designer", check.OffParts()[0]);
            Assert.Equal("CreatorCpu.ipc ok; CreatorCpu.coreCountDropdown missing", check.Line("Designer"));
            Assert.Equal("ResearchSector.StopResearching() ok", check.Line("Research"));
            Assert.Equal("", check.Line("Ai"));
            Assert.Equal(3, check.Members);
        }

        [Test]
        public void AnOptionalMemberThatIsMissing_IsReported_AndSwitchesNothingOff()
        {
            var check = new PartCheck();
            check.Add("LabUi", "CurrentResearchUI.targetTechnology", PartCheck.Ok, false);
            check.Add("LabUi", "CurrentResearchUI.LoadFunding()", "missing", true);
            Assert.False(check.IsOff("LabUi"));
            Assert.Equal("CurrentResearchUI.targetTechnology ok; CurrentResearchUI.LoadFunding() missing (optional)", check.Line("LabUi"));
            Assert.Equal(0, check.OffParts().Count);
        }

        [Test]
        public void AnyVerdictButOk_IsAFailure_AMemberOfAnotherTypeAHiddenOneAReservedOne()
        {
            foreach (string state in new[] { "missing", "has type Single", "reserved: P4 is held by the core", "error: AmbiguousMatchException", PartCheck.HiddenState })
            {
                var check = new PartCheck();
                check.Add("Binder", "Project.SetProjectType(CpuProject)", state, false);
                Assert.True(check.IsOff("Binder"), state);
                Assert.True(check.Line("Binder").EndsWith(state, StringComparison.Ordinal), check.Line("Binder"));
            }
            Assert.True(PartCheck.IsOk("ok"));
            Assert.False(PartCheck.IsOk("OK"));
            Assert.False(PartCheck.IsOk(null));
        }

        [Test]
        public void TheOffPartsAreListedInTheOrderTheyWereRecorded()
        {
            var check = new PartCheck();
            check.Add("Research", "a", PartCheck.Ok, false);
            check.Add("Ai", "b", "missing", false);
            check.Add("Designer", "c", "missing", false);
            check.Add("Ai", "d", "missing", false);
            Assert.Equal("Ai,Designer", string.Join(",", check.OffParts().ToArray()));
            Assert.Equal("Research,Ai,Designer", string.Join(",", check.Parts.ToArray()));
            Assert.Throws<ArgumentException>(() => check.Add("", "x", "ok", false));
        }

        [Test]
        public void TheDebugFlagThatHidesAMember_IsNamedAfterItsTypeAndName()
        {
            Assert.Equal("cpu-architectures.hide.CreatorCpu.coreCountDropdown", PartCheck.HideFlag("CreatorCpu", "coreCountDropdown"));
            Assert.Equal("cpu-architectures.hide.CurrentResearchUI.targetTechnology", PartCheck.HideFlag("CurrentResearchUI", "targetTechnology"));
            Assert.True(PartCheck.HideFlag("CreatorCpu", "ipc").StartsWith(PartCheck.HidePrefix, StringComparison.Ordinal));
            Assert.Equal(PartCheck.HidePrefix + ".", PartCheck.HideFlag(null, null));
        }

        // ---- the table of the self-check ----

        [Test]
        public void TheTable_HoldsTheMembersOfEveryPartThatDependsOnTheGame()
        {
            List<TableRow> table = Table();
            Assert.True(table.Count > 50, "rows read from ArchSelfCheck.cs: " + table.Count);

            var parts = new HashSet<string>(table.Select(r => r.Part));
            foreach (string part in new[] { "Research", "Designer", "Binder", "Inspector", "Ai", "LabUi", "Support", "Ecosystem", "Royalty", "Extensions" })
            {
                Assert.True(parts.Contains(part), "the table has members of " + part);
            }
            Assert.Equal(0, OffWhenHidden(table).Count, "with nothing hidden every part works");
        }

        [Test]
        public void AMissingFieldOfCreatorCpu_TakesTheDesignerAway_AndNothingElse()
        {
            List<TableRow> table = Table();
            foreach (string field in new[] { "ipc", "ips", "ipsWithoutBottleneck", "consumption", "temperature", "currentPackage", "coreCountDropdown" })
            {
                Assert.True(table.Any(r => r.Owner == "CreatorCpu" && r.Member == field && r.Part == "Designer" && !r.Optional), "CreatorCpu." + field + " is a required member of the designer");
                List<string> off = OffWhenHidden(table, "CreatorCpu." + field);
                Assert.Equal("Designer", string.Join(",", off.ToArray()), "CreatorCpu." + field + " hidden: the rating (Market), the research and the lab go on");
            }
        }

        [Test]
        public void TheCoresAndSmtOfTheDesign_AreAPartOfTheirOwn_TheDesignerKeepsWorking()
        {
            List<TableRow> table = Table();
            foreach (string field in new[] { "currentCoreCount", "smtEnabled" })
            {
                Assert.Equal("Extensions", string.Join(",", OffWhenHidden(table, "CreatorCpu." + field).ToArray()), field);
            }
        }

        [Test]
        public void AMissingTargetTechnology_TakesOnlyTheInterruptionOfThePlayersTechnologyAway()
        {
            List<TableRow> table = Table();
            Assert.True(table.Any(r => r.Owner == "CurrentResearchUI" && r.Member == "targetTechnology" && r.Part == "LabUi" && !r.Optional));
            Assert.True(table.Any(r => r.Owner == "CurrentResearchUI" && r.Member == "Hide" && r.Part == "LabUi" && !r.Optional));

            Assert.Equal("LabUi", string.Join(",", OffWhenHidden(table, "CurrentResearchUI.targetTechnology").ToArray()));
            Assert.Equal("LabUi", string.Join(",", OffWhenHidden(table, "CurrentResearchUI.Hide").ToArray()));

            // What the game offers for the tidy-up of the panel is optional: losing it costs nothing.
            Assert.Equal(0, OffWhenHidden(table, "CurrentResearchUI.LoadFunding", "ResearchTreeWindow.ForceUIUpdate").Count);
        }

        [Test]
        public void TheOtherPrivateFields_EachTakeOnlyTheirOwnPartAway()
        {
            List<TableRow> table = Table();
            Assert.Equal("Inspector", string.Join(",", OffWhenHidden(table, "CpuUI.architectureText").ToArray()));
            Assert.Equal("Ai", string.Join(",", OffWhenHidden(table, "AIBehaviourResearch.company").ToArray()));
            Assert.Equal("Ai", string.Join(",", OffWhenHidden(table, "AIBehaviourResearch.ChooseANewResearch").ToArray()));
        }

        [Test]
        public void EveryRequiredMember_SwitchesOffExactlyTheParts_ThatListIt()
        {
            // A member that two parts stand on takes both away; there are few of them and they are named, so a new one is a decision, not an accident.
            List<TableRow> table = Table();
            var shared = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var group in table.Where(r => !r.Optional).GroupBy(r => r.Owner + "." + r.Member))
            {
                var expected = new SortedSet<string>(group.Select(r => r.Part), StringComparer.Ordinal);
                var actual = new SortedSet<string>(OffWhenHidden(table, group.Key), StringComparer.Ordinal);
                Assert.True(expected.SetEquals(actual), group.Key + " is listed under " + string.Join(",", expected.ToArray()) + " but hiding it switches off " + string.Join(",", actual.ToArray()));
                if (expected.Count > 1) shared.Add(group.Key + " -> " + string.Join("+", expected.ToArray()));
            }
            Assert.Equal("GameCpu.get_Company -> Ecosystem+Royalty|ICompany.SpendMoney -> Royalty+Support", string.Join("|", shared.ToArray()), "the members that two parts stand on");
        }

        // ---- the wiring ----

        [Test]
        public void TheSelfCheck_ReadsTheHideFlagOnlyInTheDebugBuild_AndHandsTheVerdictsToThePartCheck()
        {
            string code = Code("ArchSelfCheck.cs");
            Assert.True(Regex.IsMatch(code, @"BuildInfo\.IsDebug\s*&&\s*ModSettings\.Debug\(PartCheck\.HideFlag\(row\.Owner\.Name,\s*row\.Name\)\)"),
                        "the flag is read in the debug build only, by the name of the type and the member");
            Assert.True(code.Contains("new PartCheck()") && code.Contains("check.Add(row.Part.ToString(), Display(row), Check(row), row.Optional)"), "the verdicts go to the PartCheck");
            Assert.True(code.Contains("check.IsOff(name)"), "a part is on unless the check turned it off");
            Assert.True(code.Contains("working |= ArchParts.Ui;"), "the panel stands on the templates of ModUI and is not part of this check");
        }

        [Test]
        public void TheRatingAndTheResearch_DoNotStandOnTheDesignerOrTheLabPanel()
        {
            // NFR-2: "the rating and the research go on". The code of those parts must not ask for the parts that can go off for a field of the designer or of the panel.
            foreach (string file in new[] { "Services/ArchRatingProvider.cs", "Services/ArchResearchService.cs", "Services/ArchSeeder.cs", "Services/EcosystemService.cs", "Services/SupportService.cs" })
            {
                string code = Code(file);
                Assert.False(code.Contains("ArchParts.Designer"), file + " does not depend on the part Designer");
                Assert.False(code.Contains("ArchParts.LabUi"), file + " does not depend on the part LabUi");
                Assert.False(code.Contains("ArchParts.Extensions"), file + " does not depend on the part Extensions");
            }
            Assert.True(Code("Services/ArchRatingProvider.cs").Contains("ArchParts.Market"), "the rating stands on the channels");
            Assert.True(Code("Services/ArchResearchService.cs").Contains("ArchParts.Research"), "the research stands on the public API of the lab");
        }

        [Test]
        public void ThePartsThatCanGoOff_AreAskedWhereTheyAreUsed()
        {
            // Designer: the list, the multipliers of the player and of the AI; LabUi: the interruption of the technology of the player.
            Assert.True(Code("Designer/DesignerIntegration.cs").Contains("ArchParts.Designer"), "the list of the designer asks for its part");
            Assert.True(Code("Designer/DesignContext.cs").Contains("ArchParts.Designer"), "the family of a design asks for the part");
            Assert.True(Code("Services/LabArbiter.cs").Contains("ArchParts.LabUi"), "the lab asks whether the panel of the technology can be interrupted");
            Assert.True(Code("UI/InspectorIntegration.cs").Contains("ArchParts.Inspector"));
            Assert.True(Code("Services/ArchitectureAi.cs").Contains("ArchParts.Ai"));
        }
    }
}
