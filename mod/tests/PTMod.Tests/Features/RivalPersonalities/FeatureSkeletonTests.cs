using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The skeleton of the feature (FR-10, TSK-001): the switches of the hub, and the facts about <c>RivalPersonalitiesFeature</c> that the unit
    /// tests cannot see by running it (it needs the game): they are read from the source of the feature, comments and strings aside.
    /// </summary>
    [Covers(typeof(RivalParams))]
    public class FeatureSkeletonTests
    {
        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        /// <summary>The code of a source file of the feature with the comments removed and the strings kept.</summary>
        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureFolder(), relative);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // ---- the switches of the hub ----

        [Test]
        public void TheHubSwitches_AreRandomHiddenAndFog_WithTheDefaultsOfTheSpec()
        {
            ParamSpec[] specs = RivalParams.Create(RivalTestKit.Feature);

            ParamSpec random = specs.Single(s => s.Key == "randomCharacters");
            ParamSpec hidden = specs.Single(s => s.Key == "hiddenCharacters");
            ParamSpec fog = specs.Single(s => s.Key == "intelFog");

            Assert.Equal(0.0, random.Default, "randomCharacters is off by default");
            Assert.Equal(1.0, hidden.Default, "hiddenCharacters is on by default");
            Assert.Equal(1.0, fog.Default, "intelFog is on by default");
            Assert.Equal(new[] { "randomCharacters", "hiddenCharacters", "intelFog" }, specs.Take(3).Select(s => s.Key).ToArray(), "the order of the hub page");
        }

        [Test]
        public void TheHubSwitches_AreValidSwitchesOfTheFeature()
        {
            foreach (ParamSpec spec in RivalParams.Create(RivalTestKit.Feature))
            {
                string problem;
                Assert.True(spec.Normalize(out problem), spec.Key + ": " + problem);
                Assert.Equal(RivalTestKit.Feature, spec.FeatureId);
                Assert.Equal(ParamType.Bool, spec.Type, spec.Key);
                Assert.Equal("rival.param." + spec.Key + ".label", spec.LabelKey);
                Assert.Equal("rival.param." + spec.Key + ".tooltip", spec.TooltipKey);
                Assert.False(spec.RequiresReload, spec.Key + " takes effect at once");
            }
        }

        [Test]
        public void TheDebugSwitch_ExistsOnlyInTheDebugBuild()
        {
            ParamSpec[] specs = RivalParams.Create(RivalTestKit.Feature);
#if PTMOD_DEBUG
            Assert.Equal(4, specs.Length);
            ParamSpec force = specs.Single(s => s.Key == "forceReactions");
            Assert.Equal(0.0, force.Default, "off even in a debug build until somebody switches it on");
#else
            Assert.Equal(3, specs.Length);
            Assert.False(specs.Any(s => s.Key == "forceReactions"), "a release build has no debug switch");
#endif
            Assert.Equal("rival-personalities.debug.forceReactions", RivalParams.DebugFlag);
        }

        // ---- RivalPersonalitiesFeature, read from its source ----

        [Test]
        public void TheFeatureClass_IsTheEntryPointOfTheContract()
        {
            string code = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+RivalPersonalitiesFeature\s*:\s*IModFeature\b"),
                "[ModFeature] public sealed class RivalPersonalitiesFeature : IModFeature");
            Assert.True(Regex.IsMatch(code, @"const\s+string\s+FeatureId\s*=\s*""rival-personalities""\s*;"), "FeatureId");
            Assert.True(Regex.IsMatch(code, @"string\s+Id\s*\{\s*get\s*\{\s*return\s+FeatureId\s*;"), "Id is the slug");
            Assert.True(Regex.IsMatch(code, @"string\s+NameKey\s*\{\s*get\s*\{\s*return\s+""rival\.feature\.name""\s*;"), "NameKey");
            Assert.True(Regex.IsMatch(code, @"string\s+DescriptionKey\s*\{\s*get\s*\{\s*return\s+""rival\.feature\.desc""\s*;"), "DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"string\[\]\s+DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[\s*0\s*\]\s*;"), "no hard dependency: the links are soft");
            Assert.True(Regex.IsMatch(code, @"bool\s+EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true\s*;"), "on by default");
            Assert.True(Regex.IsMatch(code, @"bool\s+SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true\s*;"), "can be switched in a game");
        }

        [Test]
        public void TheFeature_RegistersThroughTheFeatureContext()
        {
            string code = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(code, @"ctx\.SaveParticipant\s*\("), "the save participant goes through ctx");
            Assert.True(Regex.IsMatch(Code("RivalSettings.cs"), @"ctx\.Param\s*\("), "the switches go through ctx.Param");
            Assert.True(Regex.IsMatch(code, @"RivalSettings\.Declare\s*\(\s*ctx\s*\)"), "Register declares the switches");
        }

        [Test]
        public void TheFeatureId_IsTheSameInTheFeatureAndInTheApi()
        {
            // Api/ is compiled into every consumer without the rest of the feature, so it keeps its own copy of the slug
            string api = Code(Path.Combine("Api", "RivalPersonalitiesApi.cs"));
            Match apiId = Regex.Match(api, @"const\s+string\s+FeatureId\s*=\s*""([^""]+)""");
            Match featureId = Regex.Match(Code("RivalPersonalitiesFeature.cs"), @"const\s+string\s+FeatureId\s*=\s*""([^""]+)""");

            Assert.True(apiId.Success && featureId.Success, "both define FeatureId");
            Assert.Equal(apiId.Groups[1].Value, featureId.Groups[1].Value);
            Assert.Equal(RivalTestKit.Feature, featureId.Groups[1].Value);
        }

        [Test]
        public void TheSaveAdapter_IsTheSectionOfTheFeature()
        {
            string code = Code(Path.Combine("State", "RivalSaveAdapter.cs"));

            Assert.True(Regex.IsMatch(code, @"class\s+RivalSaveAdapter\s*:\s*IModSaveParticipant\b"), "IModSaveParticipant");
            Assert.True(Regex.IsMatch(code, @"new\s+RivalSaveSection\s*\(\s*featureId"), "the section id is the feature id");
        }

        [Test]
        public void TheFeature_SubscribesToNothingOfTheGameByItself()
        {
            // TSK-001: everything goes through the FeatureContext and the services of the core
            var subscription = new Regex(
                @"(?:DateController|SaveHandler|Player)\s*\.\s*(?:Instance\s*\.\s*)?On\w+\s*(?:\+=|-=)|\bOnNewGame\s*(?:\+=|-=)",
                RegexOptions.Compiled);

            string src = SourceScan.SrcDirOrFail();
            var problems = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in subscription.Matches(code))
                    problems.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }

            Assert.True(files > 0, "no sources found");
            Assert.True(problems.Count == 0, "use ctx.OnTick / ctx.SaveParticipant / ModTicks, not the game's events:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void ThePureLogic_DoesNotTouchUnityOrTheGame()
        {
            // Features/RivalPersonalities/Logic is compiled by the unit tests; the architecture test of the core covers Core/Logic only
            string src = SourceScan.SrcDirOrFail();
            string logic = Path.Combine(FeatureFolder(), "Logic");
            var engine = new Regex(
                @"\busing\s+(?:static\s+)?(?:global::)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b"
                + @"|(?<![\w.])(?:global::)?(?:UnityEngine|UnityEditor|TMPro|HarmonyLib|ProcessorTycoon)(?=\s*\.\s*\w)", RegexOptions.Compiled);

            var problems = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(logic, ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in engine.Matches(code))
                    problems.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value.Trim());
            }

            Assert.True(files >= 8, "too few files under Logic: " + files);
            Assert.True(problems.Count == 0, "Logic/** must compile without Unity and the game:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheShippedFiles_AreAllInPlace()
        {
            foreach (string file in new[] { "archetypes.json", "assignments.json", "config.json", "events.json", "pack.json" })
            {
                Assert.True(File.Exists(RivalTestKit.ContentPath(file)), "missing Content/rival-personalities/" + file);
            }

            // The event system reads the config of the pack from pack.json (ReactionTags.PackConfigFile); config.json holds the numbers of this feature.
            Assert.Equal("pack", ReactionTags.PackConfigFile);
            JsonObject pack = RivalTestKit.ParseObject(RivalTestKit.ReadContent("pack.json"));
            Assert.True(RivalTestKit.ReadContent("pack.json").Contains("\"id\": \"rival\""), "pack.json defines the category rival of the events");
            Assert.False(pack.ContainsKey("attitude") || pack.ContainsKey("reactions"), "no numbers of the feature in the config of the pack");
            Assert.NotNull(RivalTestKit.ReadL10n("ru"));
            Assert.NotNull(RivalTestKit.ReadL10n("en"));
        }
    }
}
