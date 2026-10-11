using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-018 (FR-12 item 3) and TSK-019 (FR-14, AC-14.1): the two small pieces of the interface of the game. The mark "Flaw: &lt;status&gt;" on the card of a CPU
    /// (<see cref="FlawBadgeModel"/>: only a known, open flaw of the player gets one; a card without a flaw is not touched) and the risk line of the CPU designer
    /// (<see cref="FlawRiskHint"/>: the same probability function as the roll, rounded to 5 percent, no random number). The patches themselves (<c>CpuBadgePatch</c>,
    /// <c>DesignerRiskHint</c>) cannot be compiled by the unit build, so their rules are checked on their source.
    /// </summary>
    [Covers(typeof(FlawBadgeModel), typeof(FlawRiskHint))]
    public class FlawBadgeAndHintTests
    {
        // ---- the badge (TSK-018) ----

        [Test]
        public void Badge_KnownOpenFlawsOfThePlayer_GetAStatusWord()
        {
            Assert.Equal("waiting", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.InternalKnown)));
            Assert.Equal("waiting", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Public)));
            Assert.Equal("ignored", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.InternalKnown, response: FlawResponse.Ignore)));
            Assert.Equal("ignored", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Public, response: FlawResponse.Ignore)));
            Assert.Equal("stepping", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Stepping, response: FlawResponse.Stepping)));
            Assert.Equal("patched", FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Mitigated, response: FlawResponse.Patch)));
        }

        [Test]
        public void Badge_NoFlaw_HiddenClosedOrARivals_MeansNoMark()
        {
            Assert.Null(FlawBadgeModel.StateKeyOf(null), "a card without a flaw is not touched");
            Assert.Null(FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Hidden)), "a hidden flaw is not hinted at (FR-12 item 4)");
            Assert.Null(FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Resolved, response: FlawResponse.Recall)), "closed");
            Assert.Null(FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Expired)), "closed");
            Assert.Null(FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Closed)), "closed");
            Assert.Null(FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(FlawState.Public, company: "ai:4")), "the flaws of rivals are not marked");
        }

        [Test]
        public void Badge_EveryStatusWord_HasAText_InBothLanguages()
        {
            Dictionary<string, string> ru = FlawApiKit.Own("ru");
            Dictionary<string, string> en = FlawApiKit.Own("en");

            foreach (string state in FlawBadgeModel.AllStateKeys)
            {
                Assert.True(ru.ContainsKey(FlawBadgeModel.StateKeyPrefix + state) && en.ContainsKey(FlawBadgeModel.StateKeyPrefix + state), "the word " + state);
            }
            Assert.Equal("Брак: {0}", ru[FlawBadgeModel.BadgeKey]);
            Assert.Equal("Flaw: {0}", en[FlawBadgeModel.BadgeKey]);

            // Every word StateKeyOf can return is in AllStateKeys.
            var seen = new HashSet<string>();
            foreach (FlawState state in Enum.GetValues(typeof(FlawState)).Cast<FlawState>())
            {
                foreach (FlawResponse response in Enum.GetValues(typeof(FlawResponse)).Cast<FlawResponse>())
                {
                    string key = FlawBadgeModel.StateKeyOf(FlawCycleKit.Record(state, response: response));
                    if (key != null) seen.Add(key);
                }
            }
            Assert.Equal(FlawBadgeModel.AllStateKeys.OrderBy(k => k).ToArray(), seen.OrderBy(k => k).ToArray(), "the words that are returned are the words that are listed");
        }

        [Test]
        public void BadgePatch_IsAnOptionalPostfixOfUpdateDailyTexts_InTheColourOfANegativeFigure()
        {
            string code = FlawApiKit.Code("Patches/CpuBadgePatch.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CpuSalesUI\), ""UpdateDailyTexts"", PatchKind\.Postfix, ArgTypes = new Type\[0\], Optional = true, Priority = \d+\)\]"),
                "a postfix of CpuSalesUI.UpdateDailyTexts, Optional, with a priority");
            Match priority = Regex.Match(code, @"Priority = (\d+)\)\]");
            Assert.True(int.Parse(priority.Groups[1].Value) < 400, "it runs after the postfixes of the other mods (a priority below the normal one)");
            Assert.True(code.Contains("Colors.Instance.DynamicNegativeHex()"), "the colour of the negative figures of the game");
            Assert.True(code.Contains("FlawBadgeModel.BadgeKey") && code.Contains("FlawBadgeModel.StateKeyPrefix"), "the words come from the model");
            Assert.True(FlawApiKit.Code("DesignFlawRecallsFeature.cs").Contains("ctx.Patch(typeof(CpuBadgePatch));"), "declared in Register");
        }

        [Test]
        public void BadgePatch_WithoutAFlaw_DoesNotTouchTheText()
        {
            string code = FlawApiKit.Code("Patches/CpuBadgePatch.cs");
            string postfix = FlawApiKit.Between(code, "private static void PostfixUpdateDailyTexts(", "internal static void RefreshCards()");

            int leave = postfix.IndexOf("if (state == null) return;", StringComparison.Ordinal);
            int write = postfix.IndexOf("___popularityText.Text =", StringComparison.Ordinal);
            Assert.True(leave > 0 && write > leave, "the text is written only after the check that there is a mark to write");
            Assert.Equal(1, FlawApiKit.Occurrences(postfix, "___popularityText.Text ="), "and written once");
            Assert.True(postfix.Contains("ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId)"), "a feature that is off marks nothing");
            Assert.True(postfix.Contains("ModHooks.ReportFault("), "an exception never reaches the game");
            Assert.False(postfix.Contains("cpuNameText") || postfix.Contains("___cpuNameText"),
                "not the name: FixedUpdate of the card writes the name back, a mark behind it would be gone at once");
        }

        // ---- the risk line (TSK-019) ----

        [Test]
        public void Hint_RoundsTheProbabilityToFivePercent()
        {
            Assert.Equal(0, FlawRiskHint.Percent(0f));
            Assert.Equal(0, FlawRiskHint.Percent(0.0249f));
            Assert.Equal(5, FlawRiskHint.Percent(0.025f), "halves go up");
            Assert.Equal(5, FlawRiskHint.Percent(0.07f), "AC-1.1 row: 0.070");
            Assert.Equal(10, FlawRiskHint.Percent(0.118f), "0.118");
            Assert.Equal(25, FlawRiskHint.Percent(0.233f), "0.233");
            Assert.Equal(15, FlawRiskHint.Percent(0.167f), "0.167");
            Assert.Equal(50, FlawRiskHint.Percent(0.488f), "0.488");
            Assert.Equal(50, FlawRiskHint.Percent(0.5f), "the cap pMax");
            Assert.Equal(100, FlawRiskHint.Percent(1f));
            Assert.Equal(100, FlawRiskHint.Percent(7f), "never above 100");
            Assert.Equal(0, FlawRiskHint.Percent(-1f), "never below 0");
            Assert.Equal(0, FlawRiskHint.Percent(float.NaN), "a broken number is 0");
            for (float p = 0f; p <= 1f; p += 0.013f) Assert.Equal(0, FlawRiskHint.Percent(p) % FlawRiskHint.StepPercent, "a multiple of 5 for " + p);
        }

        [Test]
        public void Hint_ARiskBelowHalfAStep_IsSaidAsUnderFivePercent_NotAsNothing()
        {
            Assert.True(FlawRiskHint.IsBelowStep(0.01f));
            Assert.False(FlawRiskHint.IsBelowStep(0f), "no risk at all is 0 %");
            Assert.False(FlawRiskHint.IsBelowStep(0.03f), "it rounds to 5 %");
            Assert.Equal("меньше 5 %", FlawApiKit.Own("ru")["dfr.hint.low"]);
            Assert.Equal("under 5 %", FlawApiKit.Own("en")["dfr.hint.low"]);
        }

        [Test]
        public void Hint_IsTheSameFunctionAsTheRoll()
        {
            FlawBalance b = FlawBalance.Defaults();
            FlawRiskInput design = FlawApiKit.Design(1998);

            float roll = FlawRiskModel.Probability(design, b, 1f);
            float hint = FlawRiskHint.Probability(design, b, 1f, false);

            Assert.Equal(roll, hint, "the hint is the probability the roll will use");
            Assert.Near(0.233, hint, 0.001, "the example of AC-1.1");
            Assert.Equal(25, FlawRiskHint.Percent(hint));
        }

        [Test]
        public void AC14_1_ThePushOfTheFrequencyToTheMaximum_RaisesTheEstimate()
        {
            FlawBalance b = FlawBalance.Defaults();
            FlawRiskInput low = FlawApiKit.Design(1998);
            low.Frequency = 100f;     // at the ceiling: no overclock
            FlawRiskInput high = low;
            high.Frequency = 150f;    // the slider at the maximum with the overclock switch on (1.5 x the ceiling)

            float before = FlawRiskHint.Probability(low, b, 1f, false);
            float after = FlawRiskHint.Probability(high, b, 1f, false);

            Assert.True(after > before, "the estimate grows with the frequency: " + before + " -> " + after);
            Assert.True(FlawRiskHint.Percent(after) > FlawRiskHint.Percent(before), "and the rounded number moves");
        }

        [Test]
        public void Hint_FollowsTheFlawFrequencySetting_TheRushSwitch_AndNeverTakesTheAiFactor()
        {
            FlawBalance b = FlawBalance.Defaults();
            FlawRiskInput design = FlawApiKit.Design(1998);

            float normal = FlawRiskHint.Probability(design, b, 1.0f, false);
            Assert.True(FlawRiskHint.Probability(design, b, 2.0f, false) > normal, "the slider of the frequency of flaws (up to 2.0)");
            Assert.True(FlawRiskHint.Probability(design, b, 0.5f, false) < normal, "and down to 0.5");
            Assert.True(FlawRiskHint.Probability(design, b, 1.0f, true) > normal, "the switch 'Rush project' raises it (FR-14 item 2)");

            design.IsAi = true;
            Assert.Equal(normal, FlawRiskHint.Probability(design, b, 1.0f, false), "the designer is the player's: the AI factor never applies");
        }

        [Test]
        public void Hint_DrawsNoRandomNumber_AndStaysInsideItsPatch()
        {
            Assert.False(FlawApiKit.Code("Logic/FlawRiskHint.cs").Contains("ModRandom") || FlawApiKit.Code("Logic/FlawRiskHint.cs").Contains("IModRng"), "the pure estimate has no random source");
            string patch = FlawApiKit.Code("Patches/DesignerRiskHint.cs");
            Assert.False(patch.Contains("ModRandom") || patch.Contains("IModRng") || patch.Contains("Rng"), "the patch never draws a number (FR-14 item 2)");
            Assert.True(patch.Contains("FlawRiskHint.Probability("), "it asks the same function as the roll");
        }

        [Test]
        public void HintPatch_IsOptional_FollowsTheSetting_AndSwitchesItselfOffWhenTheFieldsAreMissing()
        {
            string code = FlawApiKit.Code("Patches/DesignerRiskHint.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(CreatorCpu\), ""UpdateInfoTexts"", PatchKind\.Postfix, ArgTypes = new Type\[0\], Optional = true, Priority = \d+\)\]"),
                "a postfix of CreatorCpu.UpdateInfoTexts, Optional");
            Assert.True(code.Contains("FlawSettings.ShowRiskHint"), "shown while showRiskHint is on");
            Assert.False(code.Contains("DifficultyLevel >") || code.Contains("DifficultyLevel <") || code.Contains("DifficultyLevel =="), "not tied to the difficulty (the owner's decision)");

            foreach (string field in new[] { "failureRate", "frequency", "maxFrequency", "temperature", "currentPackage", "currentLithography", "nodeManufacturer" })
            {
                Assert.True(code.Contains("AccessTools.Field(typeof(CreatorCpu), \"" + field + "\")"), "the private field " + field + " is looked up");
            }

            string resolve = FlawApiKit.Between(code, "private static bool Resolve()", "private static bool EnsureLine(");
            Assert.True(resolve.Contains("_off = true;") && resolve.Contains("ModLog.Warn("), "a missing field turns the hint off with a line in the log");
            Assert.True(code.Contains("if (_off || __instance == null) return;"), "and it stays off for the session");
            Assert.True(FlawApiKit.Code("DesignFlawRecallsFeature.cs").Contains("ctx.Patch(typeof(DesignerRiskHint));"), "declared in Register");
        }

        [Test]
        public void HintPatch_WorksAtMostTenTimesASecond_AndWritesOnlyWhenTheTextChanged()
        {
            string code = FlawApiKit.Code("Patches/DesignerRiskHint.cs");

            Assert.True(code.Contains("private const float Interval = 0.1f;") && code.Contains("if (now < _nextTime && ReferenceEquals(_creator, __instance)) return;"),
                "UpdateInfoTexts runs every frame: the work is throttled");
            Assert.True(code.Contains("if (!string.Equals(text, _shown, StringComparison.Ordinal))"), "the text is written when it changed");
            Assert.True(code.Contains("RushSource") && code.Contains("RushIsOn()"), "the state of the switch 'Rush project' comes through a hook (FR-20 follows in TSK-029)");
        }

        [Test]
        public void HintTexts_AreInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> table = FlawApiKit.Own(language);
                foreach (string key in new[] { "dfr.hint.title", "dfr.hint.value", "dfr.hint.low", "dfr.hint.risk", "dfr.hint.tip" })
                {
                    Assert.True(table.ContainsKey(key), language + " has " + key);
                }
                Assert.True(table["dfr.hint.value"].Contains("{0}") && table["dfr.hint.risk"].Contains("{0}"), language + ": the number is a placeholder");
            }
            Assert.Equal("Риск скрытого брака: ~{0} %", FlawApiKit.Own("ru")["dfr.hint.risk"]);
            Assert.Equal("Hidden flaw risk: ~{0} %", FlawApiKit.Own("en")["dfr.hint.risk"]);
        }
    }
}
