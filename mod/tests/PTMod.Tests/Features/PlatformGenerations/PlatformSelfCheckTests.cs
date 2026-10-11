using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-020, NFR-2: the self-check. What it looks for (the members of the game and the hooks of the core, held as data in
    /// <see cref="PlatformSelfCheckTable"/> - the acceptance names seven of them), the book that turns the findings into the lines of the log, and the
    /// seams of the game side that cannot be loaded here (it runs when the feature starts, once, and looks without changing anything), checked on the source.
    /// </summary>
    [Covers(typeof(PlatformSelfCheckBook), typeof(PlatformSelfCheckTable), typeof(GameMemberSpec), typeof(GameMemberKind))]
    public class PlatformSelfCheckTests
    {
        private static string FeatureDirectory()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations");
        }

        private static string Code(string file)
        {
            string path = Path.Combine(FeatureDirectory(), file);
            Assert.True(File.Exists(path), "missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string Squeeze(string text)
        {
            return Regex.Replace(text, @"\s+", " ");
        }

        private static GameMemberSpec Find(string display)
        {
            foreach (GameMemberSpec spec in PlatformSelfCheckTable.Members)
            {
                if (spec.Display == display) return spec;
            }
            Assert.Fail("the table of the self-check has no " + display);
            return null;
        }

        // ---- the table ----

        [Test]
        public void TheTable_HasTheSevenMembersTheAcceptanceNames()
        {
            // TSK-020: ProjectReleaseWindow.Open, CustomSocket.CustomHardwareID, Cpu.Memory, Memory.Name, CpuBase.Market, DateController.get_MonthsSinceStart, ContractManager.OnNewContracts.
            Assert.Equal(GameMemberKind.Method, Find("ProjectReleaseWindow.Open").Kind);
            Assert.Equal(GameMemberKind.Property, Find("CustomSocket.CustomHardwareID").Kind);
            Assert.Equal(GameMemberKind.Property, Find("Cpu.Memory").Kind);
            Assert.Equal(GameMemberKind.Property, Find("Memory.Name").Kind);
            Assert.Equal(GameMemberKind.Property, Find("CpuBase.Market").Kind);
            Assert.Equal(GameMemberKind.Property, Find("DateController.MonthsSinceStart").Kind);
            Assert.Equal(GameMemberKind.Event, Find("ContractManager.OnNewContracts").Kind);
        }

        [Test]
        public void TheTable_AlsoHasTheOtherMembersOfTheDesign()
        {
            // design.md: CustomSocket.get_BasePackage, IPackage.get_IsCustomized, CpuBase.MarketData.Sold / SoldByContracts.
            Assert.Equal("ProcessorTycoon.Hardware.Package", Find("CustomSocket.BasePackage").ValueType);
            Assert.Equal("System.Boolean", Find("IPackage.IsCustomized").ValueType);
            Assert.Equal("ProcessorTycoon.Hardware.CpuBase+MarketData", Find("CpuBase.Market").ValueType);
            Assert.True(Find("CpuBase.MarketData.Sold").ValueType.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal));
            Assert.True(Find("CpuBase.MarketData.SoldByContracts").ValueType.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal));
            Assert.Equal("ProcessorTycoon.Hardware.CpuBase+MarketData", Find("CpuBase.MarketData.Sold").TypeName);
        }

        [Test]
        public void TheOpenOfTheReleaseWindow_IsFoundByItsParameterType()
        {
            GameMemberSpec open = Find("ProjectReleaseWindow.Open");
            Assert.Equal("ProcessorTycoon.ProjectSystem.ProjectReleaseWindow", open.TypeName);
            Assert.NotNull(open.ParameterTypes);
            Assert.Equal(1, open.ParameterTypes.Length);
            Assert.Equal("ProcessorTycoon.Hardware.Cpu", open.ParameterTypes[0]);
            Assert.False(open.Optional, "the patch is optional, but its target is looked for like any other member");
        }

        [Test]
        public void TheTable_IsMadeOfFullyNamedClassesOfTheGame_WithoutRepeats()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<GameMemberSpec> members = PlatformSelfCheckTable.Members;
            Assert.True(members.Count >= 30, "the members the feature stands on: " + members.Count);
            foreach (GameMemberSpec spec in members)
            {
                Assert.True(spec.TypeName.StartsWith("ProcessorTycoon.", StringComparison.Ordinal), spec.Display + " is not a class of the game: " + spec.TypeName);
                Assert.False(string.IsNullOrWhiteSpace(spec.Name), spec.TypeName);
                Assert.False(string.IsNullOrWhiteSpace(spec.Use), spec.Display + " has no purpose given");
                Assert.True(seen.Add(spec.TypeName + "." + spec.Name + "/" + spec.Kind), "listed twice: " + spec.Display);
            }
        }

        [Test]
        public void OnlyTheNamesOfCompaniesAndCustomHardware_AreOptional()
        {
            // The rest is read by the adapter without a fallback: a missing one is a problem. The names have fallbacks (the key of the company, the base package of the socket).
            var optional = new List<string>();
            foreach (GameMemberSpec spec in PlatformSelfCheckTable.Members)
            {
                if (spec.Optional) optional.Add(spec.Display);
            }
            optional.Sort(StringComparer.Ordinal);
            Assert.Equal("CustomHardwareHelper.GetCustomHardware, CustomHardwareHelper.Instance, ICompany.Name", string.Join(", ", optional));
            Assert.Equal(PlatformSelfCheckTable.Members.Count - 3, PlatformSelfCheckTable.RequiredCount);
        }

        [Test]
        public void EveryMemberTheAdapterReads_IsInTheTable()
        {
            string adapter = Code("GameAdapter.cs");
            string table = Code("Logic/PlatformSelfCheckBook.cs");

            foreach (string name in new[]
            {
                "MonthsSinceStart", "CustomHardwareID", "BasePackage", "IsCustomized", "Memory", "Market", "Sold", "SoldByContracts", "OnNewContracts",
                "GetAvailableContracts", "GetActiveContracts", "GetAllCpus", "AcceptanceDate", "PaymentPerUnit", "GetCustomHardware", "SaveID", "IsReleased", "IsRetired",
            })
            {
                Assert.True(Regex.IsMatch(adapter, @"[.\s]" + name + @"\b"), "GameAdapter does not use " + name + " any more (update the table)");
                Assert.True(table.Contains("\"" + name + "\""), "the table of the self-check lacks " + name);
            }

            string content = Code("PlatformContent.cs");
            Assert.True(content.Contains("FindPackage(") && table.Contains("\"FindPackage\""), "the loader of the content is in the table");
            Assert.True(content.Contains("FindMemory(") && table.Contains("\"FindMemory\""));
        }

        [Test]
        public void TheReleaseWindowTarget_IsTheOneThePatchDeclares()
        {
            Assert.Equal("ProjectReleaseWindow.Open", PlatformSelfCheckTable.ReleaseWindowTarget);
            string patch = Code("Patches/ReleaseWarning.cs");
            Assert.True(Regex.IsMatch(patch, @"\[ModPatch\(typeof\(GameReleaseWindow\), ""Open"", PatchKind\.Postfix, Optional = true\)\]"), "the patch of the feature");
            Assert.True(patch.Contains("using GameReleaseWindow = ProcessorTycoon.ProjectSystem.ProjectReleaseWindow;"));
        }

        [Test]
        public void TheReservedAddCpu_AreTheTwoClassesOfTheCore()
        {
            Assert.Equal(2, PlatformSelfCheckTable.ReservedAddCpu.Length);
            Assert.Equal("ProcessorTycoon.CompanySystem.Company", PlatformSelfCheckTable.ReservedAddCpu[0]);
            Assert.Equal("ProcessorTycoon.CompanySystem.AICompany", PlatformSelfCheckTable.ReservedAddCpu[1]);
            Assert.Equal("market.rating.cpu", PlatformSelfCheckTable.Channels[0]);
            Assert.Equal("contract.signChance", PlatformSelfCheckTable.Channels[1]);
        }

        // ---- the book ----

        [Test]
        public void ABookWithEverythingFound_SaysOkWithTheCounts()
        {
            var book = new PlatformSelfCheckBook();
            foreach (GameMemberSpec spec in PlatformSelfCheckTable.Members) book.Member(spec, null);
            book.Hook("hook one", null);
            book.Hook("hook two", null);

            Assert.True(book.Ok);
            Assert.Equal(PlatformSelfCheckTable.Members.Count, book.MembersExpected);
            Assert.Equal(PlatformSelfCheckTable.Members.Count, book.MembersFound);
            Assert.Equal(2, book.HooksExpected);
            Assert.Equal(2, book.HooksOk);
            Assert.Equal("self-check OK (members " + PlatformSelfCheckTable.Members.Count + "/" + PlatformSelfCheckTable.Members.Count + ", hooks 2/2)", book.Summary());
            Assert.Equal(0, book.Problems.Count);
            Assert.Equal(0, book.Notes.Count);
        }

        [Test]
        public void AMissingRequiredMember_IsAProblem_NamedWithItsPurpose()
        {
            var book = new PlatformSelfCheckBook();
            book.Member(Find("Cpu.Memory"), "missing");
            book.Member(Find("Memory.Name"), null);

            Assert.False(book.Ok);
            Assert.Equal(1, book.Problems.Count);
            Assert.True(book.Problems[0].StartsWith("Cpu.Memory (", StringComparison.Ordinal), book.Problems[0]);
            Assert.True(book.Problems[0].Contains("the full platform"), "says what the feature does with it: " + book.Problems[0]);
            Assert.True(book.Problems[0].EndsWith(": missing", StringComparison.Ordinal), book.Problems[0]);
            Assert.Equal("self-check found 1 problem(s) (members 1/2, hooks 0/0)", book.Summary());
        }

        [Test]
        public void AMissingOptionalMember_IsANote_NotAProblem()
        {
            var book = new PlatformSelfCheckBook();
            book.Member(Find("ICompany.Name"), "missing");

            Assert.True(book.Ok, "the feature has a fallback");
            Assert.Equal(0, book.Problems.Count);
            Assert.Equal(1, book.Notes.Count);
            Assert.True(book.Notes[0].Contains("ICompany.Name") && book.Notes[0].Contains("fallback"), book.Notes[0]);
            Assert.Equal("self-check OK (members 0/1, hooks 0/0, 1 note(s))", book.Summary());
        }

        [Test]
        public void AHookThatIsNotInPlace_IsAProblem_UnlessItIsOptional()
        {
            var book = new PlatformSelfCheckBook();
            book.Hook("ModEvents.CpuReleased", "nobody listens");
            book.Hook("patch ProjectReleaseWindow.Open", "the patch is Failed", true);

            Assert.False(book.Ok);
            Assert.Equal(1, book.Problems.Count);
            Assert.Equal("ModEvents.CpuReleased: nobody listens", book.Problems[0]);
            Assert.Equal(1, book.Notes.Count);
            Assert.Equal(2, book.HooksExpected);
            Assert.Equal(0, book.HooksOk);
        }

        [Test]
        public void ANullSpec_IsIgnored_AndTheReportListsEverything()
        {
            var book = new PlatformSelfCheckBook();
            book.Member(null, "missing");
            Assert.Equal(0, book.MembersExpected);

            book.Member(Find("CpuBase.Market"), "has the type System.Object instead of ProcessorTycoon.Hardware.CpuBase+MarketData");
            book.Member(Find("ICompany.Name"), "missing");
            string report = book.Report();
            Assert.True(report.StartsWith("self-check found 1 problem(s)", StringComparison.Ordinal), report);
            Assert.True(report.Contains("\n  problem: CpuBase.Market"), report);
            Assert.True(report.Contains("\n  note: ICompany.Name"), report);
        }

        // ---- the game side (the source) ----

        [Test]
        public void TheSelfCheck_RunsOnce_WhenTheFeatureStarts_AfterThePatchesAreApplied()
        {
            string feature = Squeeze(Code("PlatformGenerationsFeature.cs"));
            Assert.True(feature.Contains("private void CheckSelfOnce() { if (_selfChecked) return; _selfChecked = true; PlatformSelfCheck.Run(this); }"), "once per session");

            // OnEnabled comes after the core applied the patches of the feature (Register, then activation, then OnEnabled), so the report of the hooks is complete.
            int enabled = feature.IndexOf("public void OnEnabled()", StringComparison.Ordinal);
            int call = feature.IndexOf("CheckSelfOnce();", enabled, StringComparison.Ordinal);
            int content = feature.IndexOf("EnsureContent();", enabled, StringComparison.Ordinal);
            Assert.True(enabled >= 0 && call > enabled && call < content, "first thing in OnEnabled");
        }

        [Test]
        public void TheSelfCheck_OnlyLooks_NothingIsWrittenOrPatched()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDirectory(), "PlatformSelfCheck.cs")), true);
            foreach (string forbidden in new[] { "SetValue(", "Invoke(", "ModHooks.ApplyFeature", "ModHooks.RemoveFeature", "ModModifiers.Add(", "RegisterProvider", ".Patch(", "Activator." })
            {
                Assert.False(code.Contains(forbidden), "the self-check must only look: " + forbidden);
            }
            Assert.True(code.Contains("catch (Exception"), "a check that fails is a line in the log, not an exception");
        }

        [Test]
        public void TheSelfCheck_LooksAtTheHooksOfTheCore_TheDesignNames()
        {
            string code = Code("PlatformSelfCheck.cs");
            Assert.True(code.Contains("ModHooks.Report"), "the patch of the release window is applied");
            Assert.True(code.Contains("ModHooks.IsReserved(open, out info)"), "and is not reserved by the core");
            Assert.True(code.Contains("ModHooks.IsReserved(methods[i], out info)"), "AddCpu of both companies is reserved by the core (P20)");
            Assert.True(code.Contains("SubscriptionProblem(\"CpuReleased\")") && code.Contains("SubscriptionProblem(\"CpuTransferred\")"), "the subscriptions to the events of the core");
            Assert.True(code.Contains("handler.Target is PlatformReleaseHandlers"), "are the handlers of the feature");
            Assert.True(code.Contains("ModModifiers.IsDeclared(channel)") && code.Contains("ModModifiers.HasAny(channel)"), "the channels are declared and the providers are on them");
            Assert.True(code.Contains("feature.Provider.IsRegistered"), "and the feature registered its providers");
        }

        [Test]
        public void TheLinesOfTheSelfCheck_GoToTheLog_AtTheRightLevel()
        {
            string code = Squeeze(Code("PlatformSelfCheck.cs"));
            Assert.True(code.Contains("if (book.Ok) ModLog.Info(Feature, book.Summary()); else ModLog.Warn(Feature, book.Summary());"));
            Assert.True(code.Contains("foreach (string line in book.Problems) ModLog.Warn(Feature,"));
        }

        [Test]
        public void TheProviderRegistration_IsVisibleToTheSelfCheck()
        {
            string provider = Squeeze(Code("PlatformModifierProvider.cs"));
            Assert.True(provider.Contains("public bool IsRegistered { get { return _registered; } }"));
        }
    }
}
