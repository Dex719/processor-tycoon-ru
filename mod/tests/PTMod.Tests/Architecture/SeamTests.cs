using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PTMod.Tests.Architecture
{
    // Seam checks between the spec and the code that cannot run in the test exe (they need Unity, Harmony or the game): the sources are read
    // as text, like the other tests of this folder do. They catch a table edited on one side only (design.md "Патчи Harmony" / "Шаблоны"
    // against Core/Hooks/ReservedTargets.cs and Core/UI/ModUITemplates.cs) and a start method the bootstrap would skip without anyone noticing.

    /// <summary>TSK-023, TSK-034, TSK-022: the text of the Unity-dependent core sources agrees with design.md and with the bootstrap's rules.</summary>
    public class SeamTests
    {
        // The "Резерв" column of the patch table of design.md: kind of reservation, patch number, Class.Method of every reserved target.
        // 28 rows: channel P1-P11, P19, P23 (16), service P15-P18, P20-P22 (8), formula of market-shocks F1, F2 (4).
        private static readonly string[] DesignReserved =
        {
            "Channel P1 Modifiers.GetModifier",
            "Channel P2 Factory.ProduceWafers",
            "Channel P3 PopulationData.GetCurrentSize",
            "Channel P4 Market.CalculateRatings",
            "Channel P5 Market.UpdatePopularities",
            "Channel P6 ContractSeller.SellContracts",
            "Channel P7 Company.EarnMoney",
            "Channel P7 AICompany.EarnMoney",
            "Channel P8 Contract.CpuOffer.DefineChance",
            "Channel P9 CentralBank.UpdateCreditLimit",
            "Channel P10 BankruptcyHandler.TickBankruptcies",
            "Channel P11 MoneyBalance.UpdateUI",
            "Channel P11 Money.UpdateMoneyText",
            "Channel P11 BankruptcyHandler.UpdateUI",
            "Channel P19 CpuMath.ProjectCost",
            "Channel P23 Contract.GetBestOffers",
            "Service P15 EmailWindow.GetEmails",
            "Service P16 EmailWindow.UpdateEmailText",
            "Service P17 GameManager.StartNewGame",
            "Service P18 MenuGameSetup.InitializeUI",
            "Service P20 Company.AddCpu",
            "Service P20 AICompany.AddCpu",
            "Service P21 DateController.NextMonth",
            "Service P22 MenuGameSetup.UpdateInitialData",
            "Formula F1 CpuMath.MaterialCost",
            "Formula F1 ProductionMath.WaferMaterialCost",
            "Formula F2 ProductionMath.WaferCost",
            "Formula F2 Memory.GetDescriptions",
        };

        // The keys of the template table of design.md and the scenes each one has a row for.
        private static readonly string[] DesignTemplateKeys =
        {
            "Window.Modal", "Window.Panel", "Window.MenuPanel", "Button.Taskbar", "Button.MenuFallback", "Button.NewGame", "Widget.Button",
            "Widget.ButtonCTA", "Widget.Toggle", "Widget.Slider", "Widget.Dropdown", "Widget.InfoText", "Mail.Host",
        };

        private static readonly Regex ReservedRow = new Regex(
            @"\b(?<kind>Channel|Service|Formula)\(\s*""(?<patch>[PF]\d+)""\s*,\s*""(?<type>[^""]+)""\s*,\s*""(?<method>[^""]+)""\s*,\s*""[^""]*""(?<rest>[^;]*?)\)\s*,",
            RegexOptions.Compiled);

        private static readonly Regex TemplateRow = new Regex(
            @"new\s+UiTemplate\(\s*""(?<key>[^""]+)""\s*,\s*(?<scene>\d+)\s*,\s*""(?<window>[^""]*)""\s*,\s*""(?<element>[^""]*)""\s*,\s*""(?<path>[^""]*)""\s*,\s*(?<required>true|false)\s*,",
            RegexOptions.Compiled);

        private static readonly Regex CoreInitMethod = new Regex(
            @"\[CoreInit\((?<args>[^\]]*)\)\]\s*(?<signature>[^\r\n{]*)", RegexOptions.Compiled);

        private static string ReadSource(params string[] relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), Path.Combine(relative));
            Assert.True(File.Exists(path), "no source file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void ReservedTargets_AreTheReservedColumnOfTheDesignPatchTable()
        {
            string text = ReadSource("Core", "Hooks", "ReservedTargets.cs");

            var rows = new List<string>();
            var restByRow = new Dictionary<string, string>();
            foreach (Match m in ReservedRow.Matches(text))
            {
                string type = m.Groups["type"].Value;
                string cls = type.Substring(type.LastIndexOf('.') + 1).Replace('+', '.');
                string row = m.Groups["kind"].Value + " " + m.Groups["patch"].Value + " " + cls + "." + m.Groups["method"].Value;
                rows.Add(row);
                restByRow[row] = m.Groups["rest"].Value;
            }

            var missing = DesignReserved.Where(r => !rows.Contains(r)).ToList();
            var extra = rows.Where(r => !DesignReserved.Contains(r)).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                "ReservedTargets.cs and the 'Резерв' column of design.md differ.\n  only in design.md: " + string.Join("; ", missing)
                + "\n  only in the code: " + string.Join("; ", extra));
            Assert.Equal(DesignReserved.Length, rows.Count, "a row is listed twice in ReservedTargets.cs");
            Assert.Equal(28, rows.Count);

            // Which kinds of patch a reservation closes: P7 the prefix and the transpiler (postfixes stay free for accounting),
            // P10 and P11 only the transpiler (bankruptcy-recovery patches them with prefixes and postfixes), the rest all four.
            foreach (string row in rows)
            {
                string patch = row.Split(' ')[1];
                string rest = Regex.Replace(restByRow[row], @"\s+", " ");
                if (patch == "P7") Assert.True(rest.Contains("PatchKind.Prefix, PatchKind.Transpiler"), row + ": P7 must close prefix and transpiler only: " + rest);
                else if (patch == "P10" || patch == "P11")
                    Assert.True(rest.Contains("PatchKind.Transpiler") && !rest.Contains("PatchKind.Prefix") && !rest.Contains("PatchKind.Postfix"),
                        row + ": " + patch + " must close the transpiler only: " + rest);
                else Assert.False(rest.Contains("PatchKind."), row + " must close every kind of patch: " + rest);
            }
        }

        [Test]
        public void UiTemplateTable_HasEveryKeyOfDesign_AndPathsThatFitTheirScene()
        {
            string text = ReadSource("Core", "UI", "ModUITemplates.cs");

            var rows = new List<Match>();
            foreach (Match m in TemplateRow.Matches(text)) rows.Add(m);
            Assert.Equal(19, rows.Count, "13 keys of design.md and 3 optional extra rows of the main menu, both scenes where design.md says so");

            var keys = rows.Select(m => m.Groups["key"].Value).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var expected = DesignTemplateKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, keys, "the keys of the table in ModUITemplates.cs are not the keys of design.md");

            var pairs = new HashSet<string>();
            foreach (Match m in rows)
            {
                string key = m.Groups["key"].Value;
                int scene = int.Parse(m.Groups["scene"].Value);
                Assert.True(pairs.Add(key + "@" + scene), "the row " + key + " for scene " + scene + " is listed twice");

                // Scene roots: level0 "Main Canvas", level1 "MainCanvas". The path ends with the element (or the window, when the window itself is the template).
                string path = m.Groups["path"].Value;
                string[] segments = path.Split('/');
                Assert.Equal(scene == 0 ? "Main Canvas" : "MainCanvas", segments[0], key + ": the root of the path does not fit scene " + scene);
                string window = m.Groups["window"].Value;
                string element = m.Groups["element"].Value;
                Assert.Equal(element.Length > 0 ? element : window, segments[segments.Length - 1], key + ": the path does not end with its element");
                if (window.Length > 0) Assert.True(segments.Contains(window), key + ": the window " + window + " is not on the path " + path);
            }

            // The rows design.md asks for in each scene must exist and be required.
            foreach (string pair in new[]
            {
                "Window.Modal@1", "Window.Panel@1", "Window.MenuPanel@0", "Button.Taskbar@1", "Button.MenuFallback@1", "Button.NewGame@0",
                "Widget.Button@1", "Widget.ButtonCTA@1", "Widget.Toggle@0", "Widget.Toggle@1", "Widget.Slider@0", "Widget.Slider@1",
                "Widget.Dropdown@0", "Widget.Dropdown@1", "Widget.InfoText@1", "Mail.Host@1",
            })
            {
                Assert.True(pairs.Contains(pair), "design.md needs a row " + pair);
            }
            foreach (Match m in rows)
            {
                bool design = !m.Groups["required"].Value.Equals("false", StringComparison.Ordinal);
                bool extra = m.Groups["key"].Value + "@" + m.Groups["scene"].Value == "Widget.Button@0"
                             || m.Groups["key"].Value + "@" + m.Groups["scene"].Value == "Widget.ButtonCTA@0"
                             || m.Groups["key"].Value + "@" + m.Groups["scene"].Value == "Widget.InfoText@0";
                Assert.True(design != extra, m.Groups["key"].Value + "@" + m.Groups["scene"].Value + ": only the three extra rows of the main menu are optional");
            }
        }

        [Test]
        public void UiTemplateKeyConstants_AreTheKeysOfTheTable()
        {
            string text = ReadSource("Core", "UI", "ModUITemplates.cs");
            var constants = new List<string>();
            foreach (Match m in Regex.Matches(text, @"public\s+const\s+string\s+\w+\s*=\s*""(?<key>[^""]+)""\s*;"))
            {
                string key = m.Groups["key"].Value;
                if (key != "PTModUIStaging") constants.Add(key);
            }
            constants.Sort(StringComparer.Ordinal);
            var expected = DesignTemplateKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, constants);
        }

        // The 21 phases of the TickPhase enum of design.md ("ModTicks"), in its order.
        private static readonly string[] DesignTickPhases =
        {
            "EarlyTick", "AIBehaviourTick", "PreContractSellTick", "ClientProductionTick", "ContractProductionTick",
            "ContractSellTick", "ContractTick", "ProductionTick", "MarketSellTick", "Tick", "LateTick", "DayPassed",
            "CompanySpawnTick", "MonthCacheUpdate", "EarlyMonthPassed", "MonthPassed", "YearPassed",
            "LateMonthPassed", "LateYearPassed", "FirstEverTick", "FastForwardEnded",
        };

        [Test]
        public void TickPhases_AreTheEnumOfDesign_InTheSameOrder()
        {
            // TSK-024: TickPhase lives in Core/Ticks (it needs the dispatcher, not Unity, but is outside the pure logic), so the test reads the text.
            string text = ReadSource("Core", "Ticks", "TickDispatcher.cs");
            Match body = Regex.Match(text, @"enum\s+TickPhase\s*\{(?<body>[^}]*)\}");
            Assert.True(body.Success, "no enum TickPhase in Core/Ticks/TickDispatcher.cs");

            var phases = Regex.Split(body.Groups["body"].Value, @"[\s,]+").Where(s => s.Length > 0).ToList();
            Assert.Equal(new List<string>(DesignTickPhases), phases, "TickPhase differs from the enum of design.md (the values are the order of the arrays)");
            Assert.Equal(21, phases.Count);
        }

        [Test]
        public void ModTicks_SubscribesAndUnsubscribesEveryEventOfDateController_OnceEach()
        {
            // One dispatcher delegate per event and instance (FR-4 item 4). A phase wired to the wrong event, or added without being
            // removed (a leak per scene), cannot be seen without the game: the text of SubscribeDates is the check.
            string text = ReadSource("Core", "Ticks", "ModTicks.cs");
            foreach (string op in new[] { "+=", "-=" })
            {
                var wired = new Dictionary<string, string>();
                foreach (Match m in Regex.Matches(text, @"dates\.(?<event>\w+)\s*" + Regex.Escape(op) + @"\s*D\(TickPhase\.(?<phase>\w+)\)\s*;"))
                {
                    string phase = m.Groups["phase"].Value;
                    Assert.False(wired.ContainsKey(phase), "ModTicks: the phase " + phase + " is wired twice with " + op);
                    wired[phase] = m.Groups["event"].Value;
                }

                Assert.Equal(DesignTickPhases.Length, wired.Count, "ModTicks: phases wired with " + op + ": " + string.Join(", ", wired.Keys));
                foreach (string phase in DesignTickPhases)
                {
                    // design.md: every phase is the event of the same name with "On"; the two exceptions are FirstEverTick (no "On") and OnFastFowardEnded (the game's typo).
                    string expected = phase == "FirstEverTick" ? "FirstEverTick" : phase == "FastForwardEnded" ? "OnFastFowardEnded" : "On" + phase;
                    Assert.True(wired.ContainsKey(phase) && wired[phase] == expected,
                        "ModTicks: " + phase + " must be wired with DateController." + expected + " (" + op + "), it is " + (wired.ContainsKey(phase) ? wired[phase] : "not wired"));
                }
            }
        }

        [Test]
        public void CoreInitMethods_AreStaticVoidWithoutParameters_InTheCoreNamespace()
        {
            // ModBootstrap skips such a method with a warning ("must be static void and take no parameters"): its service would then never start.
            string coreDir = Path.Combine(SourceScan.SrcDirOrFail(), "Core");
            int found = 0;
            var problems = new List<string>();
            foreach (string file in SourceScan.Files(coreDir, ".cs"))
            {
                string relative = SourceScan.Relative(SourceScan.SrcDirOrFail(), file);
                string text = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match m in CoreInitMethod.Matches(text))
                {
                    found++;
                    string signature = m.Groups["signature"].Value;
                    if (!Regex.IsMatch(signature, @"^\s*(?:public|internal|private)\s+static\s+void\s+\w+\s*\(\s*\)\s*$"))
                        problems.Add(relative + ":" + SourceScan.LineOf(text, m.Index) + " [CoreInit] on '" + signature.Trim() + "'");
                    if (relative.StartsWith("Core/Logic/", StringComparison.Ordinal))
                        problems.Add(relative + ":" + SourceScan.LineOf(text, m.Index) + " [CoreInit] in Core/Logic (the pure logic knows nothing of the start)");
                }
            }
            Assert.True(problems.Count == 0, "the bootstrap would skip these start methods:\n  " + string.Join("\n  ", problems));
            Assert.True(found >= 5, "expected the start methods of ModHooks (4) and ModUITemplates (1) at least, found " + found);
        }
    }
}
