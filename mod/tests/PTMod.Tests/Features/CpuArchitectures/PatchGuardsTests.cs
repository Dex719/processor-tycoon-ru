using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The wiring of the feature that cannot run without the game, checked in the source (FR-9 items 2 and 4, NFR-2, TSK-018, TSK-034): every patch handler
    /// leaves first thing while the feature is neutral (switched off, or the fast-forward of a new game), every patch is optional and none is on a target the
    /// core reserves, every patch class is declared to the core and none is declared twice, the lab is refereed on <c>EarlyTick</c>, switching the feature
    /// off hands the lab back and creates nothing anew. The source is read with comments and strings blanked, so a word in a comment proves nothing.
    /// </summary>
    public class PatchGuardsTests
    {
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

        /// <summary>The body of the first method with this signature fragment, from its opening brace to the matching one (strings blanked for the matching only).</summary>
        private static string BodyOf(string code, string signature)
        {
            int at = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, "no '" + signature + "' in the source");

            string blanked = SourceScan.Strip(code, true);
            int open = blanked.IndexOf('{', at);
            Assert.True(open > 0, "no body after '" + signature + "'");
            int depth = 0;
            for (int i = open; i < blanked.Length; i++)
            {
                if (blanked[i] == '{') depth++;
                else if (blanked[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            Assert.Fail("the body of '" + signature + "' is not closed");
            return null;
        }

        private sealed class Handler
        {
            public string File, Name, Attribute, Body;
        }

        private static readonly Regex HandlerPattern = new Regex(
            @"\[ModPatch\((?<attr>.*?)\)\]\s*private static (?<ret>\w+) (?<name>\w+)\((?<args>[^)]*)\)\s*\{", RegexOptions.Singleline | RegexOptions.Compiled);

        private static List<Handler> Handlers()
        {
            var list = new List<Handler>();
            string folder = Path.Combine(FeatureDir(), "Patches");
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in HandlerPattern.Matches(code))
                {
                    list.Add(new Handler
                    {
                        File = Path.GetFileName(path),
                        Name = match.Groups["name"].Value,
                        Attribute = match.Groups["attr"].Value,
                        Body = BodyOf(code, match.Value.Substring(match.Value.IndexOf(" static ", StringComparison.Ordinal))),
                    });
                }
            }
            return list;
        }

        // ---- the handlers ----

        [Test]
        public void EveryPatchHandler_LeavesFirstWhileTheFeatureIsNeutral()
        {
            List<Handler> handlers = Handlers();
            Assert.True(handlers.Count >= 11, "found the patch handlers of the feature: " + handlers.Count);

            var problems = new List<string>();
            foreach (Handler handler in handlers)
            {
                // The first statement: up to the first ';' or '{' - "if (ArchitecturesFeature.NeutralNow) return;" or "if (...NeutralNow) { ... }".
                string body = handler.Body.Substring(1).TrimStart();
                int end = body.IndexOfAny(new[] { ';', '{' });
                string first = end < 0 ? body : body.Substring(0, end);
                if (first.IndexOf("ArchitecturesFeature.NeutralNow", StringComparison.Ordinal) < 0)
                {
                    problems.Add(handler.File + ": " + handler.Name + " starts with '" + first.Trim() + "'");
                }
            }
            Assert.True(problems.Count == 0, "a patch handler that does not ask whether the feature is neutral first:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryPatch_IsOptional_SoLosingItCostsAPartNotTheFeature()
        {
            var problems = new List<string>();
            foreach (Handler handler in Handlers())
            {
                if (!Regex.IsMatch(handler.Attribute, @"Optional\s*=\s*true")) problems.Add(handler.File + ": " + handler.Name);
            }
            Assert.True(problems.Count == 0, "patches that are not Optional (design.md, 'Патчи Harmony'):\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void NoPatch_IsOnATargetTheCoreReserves()
        {
            // design.md: the feature patches none of these; their answers come through ModEvents and the channels of ModModifiers.
            var reserved = new[]
            {
                new[] { "Company", "AddCpu" }, new[] { "AICompany", "AddCpu" }, new[] { "CpuMath", "ProjectCost" }, new[] { "Market", "CalculateRatings" },
            };

            var problems = new List<string>();
            foreach (Handler handler in Handlers())
            {
                Match target = Regex.Match(handler.Attribute, @"^\s*typeof\((?<type>[\w.]+)\)\s*,\s*""(?<method>\w+)""");
                Assert.True(target.Success, handler.Name + ": the attribute is not read: " + handler.Attribute);
                foreach (string[] pair in reserved)
                {
                    if (target.Groups["type"].Value == pair[0] && target.Groups["method"].Value == pair[1]) problems.Add(handler.File + ": " + handler.Name + " patches " + pair[0] + "." + pair[1]);
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void EveryPatchClass_IsDeclaredToTheCoreOnce_AndEveryDeclaredClassExists()
        {
            string feature = Code("ArchitecturesFeature.cs");
            var declared = new List<string>();
            foreach (Match match in Regex.Matches(feature, @"ctx\.Patch\(typeof\((\w+)\)\)")) declared.Add(match.Groups[1].Value);

            Assert.Equal(declared.Count, declared.Distinct().Count(), "a patch class declared twice: " + string.Join(", ", declared.ToArray()));

            var classes = new List<string>();
            foreach (string path in SourceScan.Files(Path.Combine(FeatureDir(), "Patches"), ".cs"))
            {
                foreach (Match match in Regex.Matches(SourceScan.Strip(SourceScan.ReadText(path), false), @"internal static class (\w+)")) classes.Add(match.Groups[1].Value);
            }

            Assert.True(classes.Count >= 5, "found the patch classes: " + string.Join(", ", classes.ToArray()));
            foreach (string name in classes) Assert.True(declared.Contains(name), name + " is not declared with ctx.Patch");
            foreach (string name in declared) Assert.True(classes.Contains(name), "ctx.Patch(typeof(" + name + ")) has no class in Patches/");
        }

        [Test]
        public void TheLabPrefix_SkipsTheAisChoiceOnlyForALabAnArchitectureHolds()
        {
            string code = Code(Path.Combine("Patches", "LabPatches.cs"));
            string body = BodyOf(code, "PrefixChooseANewResearch(");

            Assert.True(Regex.IsMatch(body, @"if \(ArchitecturesFeature\.NeutralNow\) return true;"), "off or fast-forwarding: the game chooses as always");
            Assert.True(body.Contains("return !lab.IsOccupiedByArchitecture(___company);"), "false (skip) only while an active architecture holds the lab of this company");
            Assert.True(Regex.IsMatch(body, @"catch \(Exception ex\)\s*\{[^}]*return true;"), "a failure lets the original run, as if the patch were not there");
            Assert.True(Regex.IsMatch(code, @"""ChooseANewResearch"",\s*PatchKind\.Prefix"), "the prefix is on AIBehaviourResearch.ChooseANewResearch");
        }

        // ---- the lab and the switch ----

        [Test]
        public void TheLab_IsRefereedOnEarlyTick_AndTheOccupancyIsToldToResearchTreeUx()
        {
            string feature = Code("ArchitecturesFeature.cs");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.EarlyTick, _lab.OnEarlyTick);"), "the arbiter runs before the progress and the costs of the day");
            Assert.True(feature.Contains("ResearchTreeUxApi.RegisterLabOccupancy(new LabOccupancySource(this));"), "FR-2 item 10");

            // EarlyTick comes before Tick in the registration (and in the game); the daily research also asks the arbiter before it charges.
            Assert.True(feature.IndexOf("TickPhase.EarlyTick", StringComparison.Ordinal) < feature.IndexOf("TickPhase.Tick,", StringComparison.Ordinal));
            string research = Code(Path.Combine("Services", "ArchResearchService.cs"));
            Assert.True(research.Contains("_lab.Arbitrate(company, state)"), "a second look before anything is charged");
        }

        [Test]
        public void SwitchingTheFeatureOff_HandsTheLabBack_HidesTheUi_AndCreatesNothingAnew()
        {
            string feature = Code("ArchitecturesFeature.cs");
            string disabled = BodyOf(feature, "public void OnDisabled()");
            string enabled = BodyOf(feature, "public void OnEnabled()");

            Assert.True(disabled.Contains("_lab.OnFeatureDisabled()"), "AC-9.2: the lab goes back to the interrupted technology");
            Assert.True(disabled.Contains("_designer.SetVisible(false)"), "the list of the designer is hidden");
            Assert.True(disabled.Contains("_ui.OnFeatureDisabled()"), "the tab, the window and the panels are hidden");
            Assert.True(disabled.Contains("ModModifiers.Invalidate(ModChannels.DevelopmentCostDesign)"), "the cached values of the channels are dropped");
            Assert.True(disabled.Contains("_rating.InvalidateAll()"));

            foreach (KeyValuePair<string, string> pair in new Dictionary<string, string> { { "OnDisabled", disabled }, { "OnEnabled", enabled } })
            {
                foreach (string forbidden in new[] { "new ArchRegistry", "RegisterReasonedProvider", "RegisterProvider", "ResetToDefaults", ".Clear()", "Remove(", "new ArchSaveParticipant", "new ArchRatingProvider", "new DesignCostProvider" })
                {
                    Assert.False(pair.Value.Contains(forbidden), pair.Key + " must not create or empty anything (FR-9 item 2): " + forbidden);
                }
            }

            // The providers are registered once, in Register, and nowhere else.
            Assert.False(Regex.IsMatch(BodyOf(feature, "public void OnEnabled()"), @"_rating\.Register\(\)|_designCost\.Register\(\)"));
            Assert.Equal(1, Regex.Matches(feature, @"_rating\.Register\(\)").Count);
            Assert.Equal(1, Regex.Matches(feature, @"_designCost\.Register\(\)").Count);
        }

        [Test]
        public void TheSelfCheck_NamesTheMembersOfTheLabParts()
        {
            string check = Code("ArchSelfCheck.cs");
            foreach (string needed in new[]
            {
                "ArchParts.Ai, typeof(AIBehaviourResearch), \"ChooseANewResearch\", Prefix", "typeof(AIBehaviourResearch), \"company\"",
                "ArchParts.LabUi, typeof(CurrentResearchUI), \"targetTechnology\"", "ArchParts.LabUi, typeof(CurrentResearchUI), \"Hide\"",
                "ArchParts.LabUi, typeof(CurrentResearchUI), \"SetTechnology\"", "ArchParts.LabUi, typeof(CurrentResearchUI), \"LoadFunding\"",
            })
            {
                Assert.True(check.Contains(needed), "the self-check lacks: " + needed);
            }
        }

        [Test]
        public void TheParams_AreTheKnobAndTheSwitchOfTheHub()
        {
            string feature = Code("ArchitecturesFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"Key = RatingStrengthParam,[^}]*LabelKey = ""arch\.param\.ratingStrength"",[^}]*Type = ParamType\.Float,[^}]*Default = 1\.0,[^}]*Min = 0\.0,[^}]*Max = 1\.5,"), "FR-9 item 1: 0-150 %, 100 % by default");
            Assert.True(Regex.IsMatch(feature, @"Key = NotificationsParam,[^}]*LabelKey = ""arch\.param\.notifications"",[^}]*Type = ParamType\.Bool,[^}]*Default = 1,"), "the switch is on by default");
            Assert.True(feature.Contains("ctx.HubPage(\"arch.hub.title\", BuildHubPage);"), "the page of the hub");
        }
    }
}
