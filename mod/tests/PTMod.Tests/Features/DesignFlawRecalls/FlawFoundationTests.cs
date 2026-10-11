using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-001 (FR-15, NFR-2) and the small pure pieces under it: the five parameters, the day count, the numbers of the public enums (they are part of
    /// the save format), the strings the skeleton needs, and the rules of the feature class that the unit build cannot compile (checked on the source).
    /// </summary>
    [Covers(typeof(FlawParams), typeof(FlawDays), typeof(FlawSeverity), typeof(FlawState), typeof(DiscoverySource), typeof(FlawResponse),
        typeof(FlawResponseBiasOrder), typeof(FlawTypeIds))]
    public class FlawFoundationTests
    {
        // ---- the parameters (FR-15 item 3) ----

        [Test]
        public void Params_AreTheFiveOfTheDesign_InTheOrderOfTheHub()
        {
            ParamSpec[] specs = FlawParams.Create(FlawTestKit.Feature);

            Assert.Equal(new[] { "aiFlaws", "showRiskHint", "rushEnabled", "flawFrequency", "debug" }, specs.Select(s => s.Key).ToArray());
            Assert.True(specs.All(s => s.FeatureId == FlawTestKit.Feature), "every parameter belongs to the feature");
        }

        [Test]
        public void Params_Defaults_AreTrueTrueTrueOneFalse()
        {
            ParamSpec[] specs = FlawParams.Create(FlawTestKit.Feature);

            Assert.Equal(ParamType.Bool, Spec(specs, "aiFlaws").Type);
            Assert.Equal(1.0, Spec(specs, "aiFlaws").Default);
            Assert.Equal(1.0, Spec(specs, "showRiskHint").Default);
            Assert.Equal(1.0, Spec(specs, "rushEnabled").Default);
            Assert.Equal(1.0, Spec(specs, "flawFrequency").Default);
            Assert.Equal(0.0, Spec(specs, "debug").Default);
            Assert.Equal(ParamType.Bool, Spec(specs, "debug").Type);
        }

        [Test]
        public void Params_FlawFrequency_IsAFloatFromHalfToTwoInTenths()
        {
            ParamSpec spec = Spec(FlawParams.Create(FlawTestKit.Feature), "flawFrequency");

            Assert.Equal(ParamType.Float, spec.Type);
            Assert.Near(0.5, spec.Min, 1e-12);
            Assert.Near(2.0, spec.Max, 1e-12);
            Assert.Near(0.1, spec.Step, 1e-12);
            Assert.Near(0.5, spec.Clamp(0.1), 1e-12);
            Assert.Near(2.0, spec.Clamp(9), 1e-12);
            Assert.False(spec.RequiresReload, "the settings act without a restart (FR-15 item 3)");
        }

        [Test]
        public void Params_AreValidDeclarations()
        {
            foreach (ParamSpec spec in FlawParams.Create(FlawTestKit.Feature))
            {
                string problem;
                Assert.True(spec.Clone().Normalize(out problem), spec.Key + ": " + problem);
                Assert.True(spec.InRange(spec.Default), spec.Key + ": the default must be legal");
                Assert.False(spec.RequiresReload, spec.Key);
            }
        }

        [Test]
        public void Params_TextKeys_ExistInBothLanguages_WithThePrefixOfTheFeature()
        {
            JsonObject ru = FlawL10n("ru");
            JsonObject en = FlawL10n("en");
            foreach (ParamSpec spec in FlawParams.Create(FlawTestKit.Feature))
            {
                foreach (string key in new[] { spec.LabelKey, spec.TooltipKey })
                {
                    Assert.True(key.StartsWith("dfr.param.", StringComparison.Ordinal), key);
                    Assert.True(ru.ContainsKey(key), "ru lacks " + key);
                    Assert.True(en.ContainsKey(key), "en lacks " + key);
                }
            }
            Assert.Equal("dfr.param.aiFlaws.label", FlawParams.LabelKey("aiFlaws"));
            Assert.Equal("dfr.param.debug.tooltip", FlawParams.TooltipKey("debug"));
        }

        // ---- the strings of the skeleton ----

        [Test]
        public void Strings_FeatureNameDescriptionAndTypeNames_ExistInBothLanguages()
        {
            JsonObject ru = FlawL10n("ru");
            JsonObject en = FlawL10n("en");
            var keys = new List<string> { "dfr.feature.name", "dfr.feature.desc" };
            keys.AddRange(FlawBalance.Defaults().Types.Select(t => t.NameKey));

            foreach (string key in keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(ru.GetString(key)), "ru lacks " + key);
                Assert.False(string.IsNullOrWhiteSpace(en.GetString(key)), "en lacks " + key);
            }
        }

        [Test]
        public void Strings_BothLanguages_HaveTheSameKeys_AllWithTheFeaturePrefix()
        {
            JsonObject ru = FlawL10n("ru");
            JsonObject en = FlawL10n("en");

            Assert.Equal(ru.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray(), en.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.True(ru.All(p => p.Key.StartsWith("dfr.", StringComparison.Ordinal)), "every key starts with dfr.");
        }

        [Test]
        public void Strings_TypeNames_AreTheNamesOfTheDesign()
        {
            JsonObject en = FlawL10n("en");

            Assert.Equal("arithmetic erratum", en.GetString("dfr.type.arith"));
            Assert.Equal("clock instability", en.GetString("dfr.type.stability"));
            Assert.Equal("thermal degradation", en.GetString("dfr.type.thermal"));
            Assert.Equal("security vulnerability", en.GetString("dfr.type.security"));
            Assert.Equal("CPU flaws and recalls", en.GetString("dfr.feature.name"));
        }

        // ---- days ----

        [Test]
        public void Days_CountFromThe1stOfJanuary1971()
        {
            Assert.Equal(0, FlawDays.FromDate(new DateTime(1971, 1, 1)));
            Assert.Equal(1, FlawDays.FromDate(new DateTime(1971, 1, 2)));
            Assert.Equal(365, FlawDays.FromDate(new DateTime(1972, 1, 1)));
            Assert.Equal(365 + 366, FlawDays.FromDate(new DateTime(1973, 1, 1)));
            Assert.Equal((int)(new DateTime(1985, 3, 1) - new DateTime(1971, 1, 1)).TotalDays, FlawDays.FromDate(new DateTime(1985, 3, 1)));
        }

        [Test]
        public void Days_IgnoreTheTimeOfDay()
        {
            Assert.Equal(FlawDays.FromDate(new DateTime(1990, 5, 5)), FlawDays.FromDate(new DateTime(1990, 5, 5, 23, 59, 59)));
        }

        [Test]
        public void Days_UnknownDates_AreZero()
        {
            Assert.Equal(0, FlawDays.FromDate(default(DateTime)), "ModTicks.Today outside the game scene");
            Assert.Equal(0, FlawDays.FromDate(new DateTime(1960, 6, 6)));
            Assert.Equal(0, FlawDays.FromDate(DateTime.MinValue));
        }

        [Test]
        public void Days_RoundTrip_AndNegativeIsDayZero()
        {
            var date = new DateTime(2018, 7, 9);
            Assert.Equal(date, FlawDays.ToDate(FlawDays.FromDate(date)));
            Assert.Equal(FlawDays.Epoch, FlawDays.ToDate(0));
            Assert.Equal(FlawDays.Epoch, FlawDays.ToDate(-5));
            Assert.Equal(new DateTime(1971, 1, 1), FlawDays.Epoch);
        }

        // ---- the numbers that are part of the save format ----

        [Test]
        public void EnumNumbers_AreTheSaveFormat()
        {
            Assert.Equal(new[] { 0, 1, 2 }, new[] { (int)FlawSeverity.Minor, (int)FlawSeverity.Major, (int)FlawSeverity.Critical });
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[]
            {
                (int)FlawState.Hidden, (int)FlawState.InternalKnown, (int)FlawState.Public, (int)FlawState.Stepping,
                (int)FlawState.Mitigated, (int)FlawState.Resolved, (int)FlawState.Expired, (int)FlawState.Closed,
            });
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, new[]
            {
                (int)DiscoverySource.None, (int)DiscoverySource.Internal, (int)DiscoverySource.External,
                (int)DiscoverySource.Leak, (int)DiscoverySource.Forced, (int)DiscoverySource.Api,
            });
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, new[]
            {
                (int)FlawResponse.None, (int)FlawResponse.Recall, (int)FlawResponse.Stepping, (int)FlawResponse.Patch, (int)FlawResponse.Ignore,
            });
            Assert.Equal(3, Enum.GetValues(typeof(FlawSeverity)).Length);
            Assert.Equal(8, Enum.GetValues(typeof(FlawState)).Length);
            Assert.Equal(6, Enum.GetValues(typeof(DiscoverySource)).Length);
            Assert.Equal(5, Enum.GetValues(typeof(FlawResponse)).Length);
        }

        [Test]
        public void BiasOrder_IsRecallSteppingPatchIgnore()
        {
            Assert.Equal(0, FlawResponseBiasOrder.Recall);
            Assert.Equal(1, FlawResponseBiasOrder.Stepping);
            Assert.Equal(2, FlawResponseBiasOrder.Patch);
            Assert.Equal(3, FlawResponseBiasOrder.Ignore);
            Assert.Equal(4, FlawResponseBiasOrder.Length);
            Assert.Equal(FlawResponseBiasOrder.Recall, (int)FlawResponse.Recall - 1);
            Assert.Equal(FlawResponseBiasOrder.Ignore, (int)FlawResponse.Ignore - 1);
        }

        [Test]
        public void TypeIds_AreTheBuiltInTypesOfTheContent()
        {
            Assert.Equal(new[] { FlawTypeIds.Arithmetic, FlawTypeIds.Stability, FlawTypeIds.Thermal, FlawTypeIds.Security },
                FlawBalance.Defaults().Types.Select(t => t.Id).ToArray());
            Assert.Equal("arith", FlawTypeIds.Arithmetic);
            Assert.Equal("security", FlawTypeIds.Security);
            Assert.Equal(FlawTypeIds.Arithmetic, FlawTypeDef.FallbackId);
        }

        // ---- the feature class (TSK-001): checked on the source, the unit build cannot compile it ----

        [Test]
        public void FeatureClass_DeclaresTheIdentityOfTheDesign()
        {
            string code = FeatureCode("DesignFlawRecallsFeature.cs");

            Assert.True(code.Contains("[ModFeature]"), "[ModFeature]");
            Assert.True(code.Contains("public sealed partial class DesignFlawRecallsFeature : IModFeature"), "the class is partial: the Api part holds Registry");
            Assert.True(Contains(code, "FeatureId = DesignFlawRecallsApi.FeatureId"), "FeatureId");
            Assert.True(Contains(code, "return \"dfr.feature.name\";"), "NameKey");
            Assert.True(Contains(code, "return \"dfr.feature.desc\";"), "DescriptionKey");
            Assert.True(Contains(code, "return new[] { \"event-system\" };"), "DependsOn is event-system alone: the soft links are not listed");
            Assert.Equal(2, Occurrences(code, "return true;"), "EnabledByDefault and SupportsHotToggle are true");
            Assert.True(Contains(FeatureCode("Api/DesignFlawRecallsApi.cs"), "FeatureId = \"design-flaw-recalls\""), "the id");
        }

        [Test]
        public void FeatureClass_DoesNotDeclareRegistryAgain()
        {
            // CS0102 otherwise: the public Registry is in Api/DesignFlawRecallsFeature.Api.cs.
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                if (path.Replace(Path.DirectorySeparatorChar, '/').Contains("/Api/")) continue;   // the Api part is where Registry is declared
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(code.Contains("FlawRegistry Registry"), "Registry is declared once, in the Api part: " + path);
            }
        }

        [Test]
        public void FeatureClass_RegistersTheParametersAndTheSaveSection()
        {
            string code = FeatureCode("DesignFlawRecallsFeature.cs");
            string settings = FeatureCode("FlawSettings.cs");

            Assert.True(Contains(code, "FlawSettings.Declare(ctx);"), "Register declares the parameters");
            Assert.True(Contains(code, "ctx.SaveParticipant(Save);"), "Register registers the save section");
            Assert.True(Contains(settings, "FlawParams.Create(ctx.FeatureId)"), "the parameters come from FlawParams");
            Assert.True(Contains(settings, "ctx.Param(spec)"), "ctx.Param");
        }

        [Test]
        public void FeatureSources_NeverRemoveTheModifiers()
        {
            // NFR-2, D13: the entries of ModModifiers of a switched-off feature are kept and not applied; RemoveAll is never called by the feature.
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(code, @"ModModifiers\s*\.\s*RemoveAll"), "no call of ModModifiers.RemoveAll in the feature (" + Path.GetFileName(path) + ")");
            }
        }

        [Test]
        public void OnDisabled_RemembersTheDay_AndOnEnabledRecreatesNothing()
        {
            string code = FeatureCode("DesignFlawRecallsFeature.cs");
            string disabled = Between(code, "public void OnDisabled()", "private void RememberLastActiveDay()");
            string enabled = Between(code, "public void OnEnabled()", "public void OnDisabled()");

            Assert.True(disabled.Contains("RememberLastActiveDay"), "OnDisabled remembers lastActiveDay");
            Assert.True(code.Contains("Save.Data.lastActiveDay ="), "lastActiveDay is written into the section");
            Assert.False(disabled.Contains("ModModifiers"), "OnDisabled does not touch ModModifiers");
            Assert.False(enabled.Contains("ModModifiers"), "OnEnabled does not recreate the entries of ModModifiers");
            Assert.False(enabled.Contains(".Add("), "OnEnabled adds nothing");
        }

        [Test]
        public void FeatureFolder_HasNoPatchOfTheReleaseOfACpu()
        {
            // The release and the transfer of a CPU arrive as events of the core; a patch on Company.AddCpu / AICompany.AddCpu would be refused by ModHooks. The one
            // AddCpu the feature does patch is the queue of finished projects (ProjectCompletedHandler.AddCpu, FR-20: the mark of a rushed CPU); it is not a release.
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(code, @"ModPatch\s*\(\s*typeof\s*\(\s*(?!ProjectCompletedHandler\b)\w+\s*\)\s*,\s*""AddCpu"""),
                    "no patch on the AddCpu of a company (" + Path.GetFileName(path) + ")");
            }
        }

        // ---- helpers ----

        private static ParamSpec Spec(ParamSpec[] specs, string key)
        {
            return specs.First(s => s.Key == key);
        }

        private static JsonObject FlawL10n(string language)
        {
            return FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/" + language + "/design-flaw-recalls.json"));
        }

        private static string FeatureFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls");
        }

        /// <summary>The code of a file of the feature folder without comments (strings kept), line ends as "\n".</summary>
        private static string FeatureCode(string relative)
        {
            string path = Path.GetFullPath(Path.Combine(FeatureFolder(), relative));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false).Replace("\r\n", "\n");
        }

        private static bool Contains(string code, string text)
        {
            return code.Contains(text);
        }

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "not found: " + to);
            return text.Substring(start, end - start);
        }
    }
}
