using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-016 (FR-10 item 3, FR-14): the two sliders of the pack in the hub and what becomes of their values. The levels the pack runs on
    /// (a value off the grid snaps to the nearest), the strings of the settings page, and the wiring that the engine's freeze rule depends on (the pack names the
    /// feature as its switch; the feature declares the sliders and the page, follows the settings and takes them again when it is switched on). The behaviour of a switch
    /// in the middle of a shock is in <see cref="FreezeThawTests"/>.
    /// </summary>
    [Covers(typeof(ShockParamSpecs))]
    public class ShockSettingsTests
    {
        // ---- the levels ----

        [Test]
        public void Intensity_SnapsToTheNearestOfFourLevels_HalvesUp()
        {
            var cases = new[]
            {
                new[] { 0.5f, 0.5f }, new[] { 1f, 1f }, new[] { 1.5f, 1.5f }, new[] { 2f, 2f },
                new[] { 0.74f, 0.5f }, new[] { 0.75f, 1f }, new[] { 1.24f, 1f }, new[] { 1.3f, 1.5f }, new[] { 1.75f, 2f }, new[] { 1.9f, 2f },
                new[] { 0.1f, 0.5f }, new[] { 7f, 2f }, new[] { -1f, 0.5f },
            };
            foreach (float[] c in cases)
                Assert.Near(c[1], ShockParamSpecs.SnapIntensity(c[0]), 1e-6, "intensity " + c[0] + " runs as");
        }

        [Test]
        public void Frequency_SnapsToTheNearestOfThreeLevels_HalvesUp()
        {
            var cases = new[]
            {
                new[] { 0.5f, 0.5f }, new[] { 1f, 1f }, new[] { 1.5f, 1.5f },
                new[] { 0.7f, 0.5f }, new[] { 0.75f, 1f }, new[] { 1.2f, 1f }, new[] { 1.25f, 1.5f }, new[] { 2f, 1.5f }, new[] { 0f, 0.5f },
            };
            foreach (float[] c in cases)
                Assert.Near(c[1], ShockParamSpecs.SnapFrequency(c[0]), 1e-6, "frequency " + c[0] + " runs as");
        }

        [Test]
        public void JunkInTheFile_RunsAsNormal_NotAsAnException()
        {
            foreach (float junk in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Near(1.0, ShockParamSpecs.SnapIntensity(junk), 1e-6, "intensity " + junk);
                Assert.Near(1.0, ShockParamSpecs.SnapFrequency(junk), 1e-6, "frequency " + junk);
            }
        }

        [Test]
        public void EveryLevelThePackRunsOn_IsALegalValueOfItsSlider_AndHasAName()
        {
            ParamSpec[] specs = ShockParamSpecs.Create("market-shocks");
            for (float v = -1f; v <= 4f; v += 0.05f)
            {
                Assert.True(specs[0].InRange(ShockParamSpecs.SnapIntensity(v)), "intensity " + v + " -> " + ShockParamSpecs.SnapIntensity(v));
                Assert.True(specs[1].InRange(ShockParamSpecs.SnapFrequency(v)), "frequency " + v + " -> " + ShockParamSpecs.SnapFrequency(v));
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (float level in new[] { 0.5f, 1f, 1.5f, 2f }) names.Add(ShockParamSpecs.IntensityLevelKey(ShockParamSpecs.SnapIntensity(level)));
            Assert.Equal(4, names.Count, "four levels, four names (Мягко, Обычно, Жёстко, Хаос)");
        }

        [Test]
        public void TheSliders_ReachAChaosOfTwoAndNoFurther_FrequencyOneAndAHalf()
        {
            ParamSpec[] specs = ShockParamSpecs.Create("market-shocks");
            Assert.Near(2.0, specs[0].Max, 1e-12, "up to Chaos x2 (FR-10 item 3)");
            Assert.Near(1.5, specs[1].Max, 1e-12, "frequency up to x1.5 (FR-14 item 1)");
            Assert.Near(0.5, specs[0].Min, 1e-12);
            Assert.Near(0.5, specs[1].Min, 1e-12);
        }

        // ---- the strings of the page ----

        [Test]
        public void ThePageStrings_ExistInBothLanguages_WithTheSamePlaceholders()
        {
            foreach (string key in new[] { ShockParamSpecs.PageTitleKey, ShockParamSpecs.PageNowKey })
            {
                Dictionary<string, string> ru = ShockContentData.Strings("ru"), en = ShockContentData.Strings("en");
                Assert.True(ru.ContainsKey(key) && en.ContainsKey(key), key + " is in both tables");
            }

            string now = ShockContentData.Strings("ru")[ShockParamSpecs.PageNowKey];
            Assert.True(now.Contains("{0}") && now.Contains("{1}") && now.Contains("{2}"), "the line has the level, the multiplier of the intensity and the frequency: " + now);
            Assert.Equal(
                Placeholders(ShockContentData.Strings("ru")[ShockParamSpecs.PageNowKey]),
                Placeholders(ShockContentData.Strings("en")[ShockParamSpecs.PageNowKey]),
                "ru and en agree");
        }

        private static string Placeholders(string text)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(text, @"\{\d+\}")) found.Add(match.Value);
            return string.Join(",", new List<string>(found).ToArray());
        }

        [Test]
        public void TheLineOfThePage_ReadsWellInBothLanguages()
        {
            string ru = string.Format(System.Globalization.CultureInfo.InvariantCulture, ShockContentData.Strings("ru")[ShockParamSpecs.PageNowKey],
                ShockContentData.Strings("ru")[ShockParamSpecs.IntensityLevelKey(2f)], ShockFormat.Factor(2f, true), ShockFormat.Factor(1.5f, true));
            string en = string.Format(System.Globalization.CultureInfo.InvariantCulture, ShockContentData.Strings("en")[ShockParamSpecs.PageNowKey],
                ShockContentData.Strings("en")[ShockParamSpecs.IntensityLevelKey(2f)], ShockFormat.Factor(2f, false), ShockFormat.Factor(1.5f, false));

            Assert.Equal("Сейчас: интенсивность «Хаос» (×2), частота ×1,5.", ru);
            Assert.Equal("Now: intensity \"Chaos\" (×2), frequency ×1.5.", en);
        }

        // ---- the wiring ----

        private static string Source(params string[] parts)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks");
            foreach (string part in parts) path = Path.Combine(path, part);
            Assert.True(File.Exists(path), "no file " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }

        [Test]
        public void ThePackOptions_NameTheFeatureAsTheSwitchOfThePack_SoTheEngineFreezesItWithTheFeature()
        {
            // FR-14 item 2: the engine freezes the effects of a pack while the feature named in PackOptions.SettingsFeatureId is not Enabled (D10).
            string pack = Source("ShockPack.cs");
            Assert.True(Regex.IsMatch(pack, @"SettingsFeatureId\s*=\s*PackId"), "ShockPack.Options.SettingsFeatureId = PackId");
            Assert.True(Regex.IsMatch(pack, @"PackId\s*=\s*ShockChannels\.FeatureId"), "the pack's id is the feature's");
            Assert.True(Regex.IsMatch(Source("Channels", "ShockChannels.cs"), @"FeatureId\s*=\s*ShockRegistration\.FeatureId"), "ShockChannels.FeatureId is ShockRegistration.FeatureId");
            Assert.Equal("market-shocks", ShockRegistration.FeatureId, "the id the hub switches");
        }

        [Test]
        public void TheFeature_DeclaresTheSlidersAndThePage_FollowsTheSettings_AndTakesThemAgainWhenSwitchedOn()
        {
            string feature = Source("MarketShocksFeature.cs");
            Assert.True(feature.Contains("MarketShocksSettings.Declare(ctx)"), "Register declares the parameters and the page");
            Assert.True(feature.Contains("MarketShocksSettings.Attach()"), "Register follows ModSettings.Changed");
            Assert.True(Regex.IsMatch(feature, @"OnEnabled\(\)[\s\S]*?MarketShocksSettings\.Apply\(\)"), "OnEnabled takes the settings again (they could change while the feature was off)");
            Assert.True(Regex.IsMatch(feature, @"OnDisabled\(\)[\s\S]*?ShockChannels\.Invalidate\(\)"), "OnDisabled drops the cache of the channels (the patches are gone)");

            string settings = Source("Settings", "MarketShocksSettings.cs");
            Assert.True(settings.Contains("ctx.Param(spec)"), "the sliders are declared with ctx.Param");
            Assert.True(Regex.IsMatch(settings, @"ModSettings\.Changed\s*\+="), "the settings are followed");
            Assert.True(Regex.IsMatch(settings, @"Options\.Severity\s*=\s*Intensity"), "the intensity goes into PackOptions.Severity");
            Assert.True(Regex.IsMatch(settings, @"Options\.Frequency\s*=\s*Frequency"), "the frequency goes into PackOptions.Frequency");
            Assert.True(settings.Contains("ShockParamSpecs.SnapIntensity") && settings.Contains("ShockParamSpecs.SnapFrequency"), "both go through the snap");
        }

        [Test]
        public void TheSettings_AreNotReadAnywhereElse_TheClassOfTheSettingsIsTheOnlyReaderOfTheTwoKeys()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks");
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                string name = Path.GetFileName(path);
                if (name == "MarketShocksSettings.cs" || name == "ShockRegistration.cs") continue;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"ModSettings\s*\.\s*Get(Float|Int|Bool)")) offenders.Add(SourceScan.Relative(root, path));
            }
            Assert.True(offenders.Count == 0, "the sliders are read in one place:\n  " + string.Join("\n  ", offenders.ToArray()));
        }
    }
}
