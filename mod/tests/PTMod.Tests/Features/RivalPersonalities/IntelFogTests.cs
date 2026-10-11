using System;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The fog of the intelligence panel (TSK-029; FR-16, formula 15): with it on the share is rounded to 5 percentage points ("≈ 15 %" for 17.3 %) and the
    /// <c>Ips</c> to two significant digits ("≈ 120" for 123); with it off the numbers are exact. The attitude, the threats, the effects and the history do not
    /// depend on it, and the panel follows a change of the parameter: those two facts are checked in <c>IntelBuilderTests</c> and <c>IntelWiringTests</c>.
    /// </summary>
    [Covers(typeof(IntelFog))]
    public class IntelFogTests
    {
        // ---- the share ----

        [Test]
        public void AC_16_1_AShareOf17Point3_IsFifteen_WithTheFog_AndSeventeen_WithoutIt()
        {
            Assert.Equal("≈ 15 %", IntelFog.FormatShare(0.173f, true));
            Assert.Equal("17 %", IntelFog.FormatShare(0.173f, false));
        }

        [Test]
        public void TheShare_IsRoundedToTheStep_HalvesGoUp()
        {
            Assert.Near(0.15, IntelFog.RoundShare(0.173f, 5f), 1e-6);
            Assert.Near(0.20, IntelFog.RoundShare(0.18f, 5f), 1e-6, "17.5 and above go up");
            Assert.Near(0.20, IntelFog.RoundShare(0.175001f, 5f), 1e-6);
            Assert.Near(0.0, IntelFog.RoundShare(0.02f, 5f), 1e-6);
            Assert.Near(0.05, IntelFog.RoundShare(0.026f, 5f), 1e-6);
            Assert.Near(0.10, IntelFog.RoundShare(0.1f, 5f), 1e-6, "an exact multiple stays");
            Assert.Near(0.20, IntelFog.RoundShare(0.173f, 10f), 1e-6, "the step is a parameter");
            Assert.Near(0.17, IntelFog.RoundShare(0.173f, 1f), 1e-6);
        }

        [Test]
        public void TheShare_StaysInsideZeroAndOne_AndSurvivesBadNumbers()
        {
            Assert.Near(0.0, IntelFog.RoundShare(-0.3f, 5f), 1e-9);
            Assert.Near(1.0, IntelFog.RoundShare(1.4f, 5f), 1e-9);
            Assert.Near(1.0, IntelFog.RoundShare(0.99f, 5f), 1e-6, "99 % rounds to 100 %");
            Assert.Near(0.0, IntelFog.RoundShare(float.NaN, 5f), 1e-9);
            Assert.Near(0.0, IntelFog.RoundShare(float.PositiveInfinity, 5f), 1e-9);

            Assert.Near(0.173, IntelFog.RoundShare(0.173f, 0f), 1e-6, "a step of 0 leaves the share as it is");
            Assert.Near(0.173, IntelFog.RoundShare(0.173f, -5f), 1e-6);
            Assert.Near(0.173, IntelFog.RoundShare(0.173f, float.NaN), 1e-6);

            Assert.Equal("0 %", IntelFog.FormatShare(float.NaN, false));
            Assert.Equal("≈ 0 %", IntelFog.FormatShare(float.NaN, true));
            Assert.Equal("100 %", IntelFog.FormatShare(2f, false), "a share is never above everything");
            Assert.Equal("0 %", IntelFog.FormatShare(-1f, false));
        }

        [Test]
        public void TheShareWithoutTheFog_IsWholePercents_InAnyCulture()
        {
            Assert.Equal("17 %", IntelFog.FormatShare(0.173f, false));
            Assert.Equal("18 %", IntelFog.FormatShare(0.176f, false));
            Assert.Equal("0 %", IntelFog.FormatShare(0.004f, false));
            Assert.Equal("33 %", IntelFog.FormatShare(1f / 3f, false));
            Assert.Equal("≈ 35 %", IntelFog.FormatShare(1f / 3f, true));
        }

        [Test]
        public void TheShare_TakesTheMarkOfTheLanguage_AndTheStepOfTheConfig()
        {
            string text = IntelFog.FormatShare(0.173f, true, 10f, number => "~" + number + "!");
            Assert.Equal("~20 %!", text);
            Assert.Equal("17 %", IntelFog.FormatShare(0.173f, false, 10f, number => "~" + number + "!"), "the mark is for the rounded number only");
        }

        // ---- the Ips ----

        [Test]
        public void AC_16_2_AnIpsOf123_IsOneTwenty_WithTheFog_AndExact_WithoutIt()
        {
            Assert.Equal("≈ 120", IntelFog.FormatIps(123f, true));
            Assert.Equal("123", IntelFog.FormatIps(123f, false));
        }

        [Test]
        public void TheIps_IsRoundedToTheSignificantDigits()
        {
            Assert.Near(120.0, IntelFog.RoundSignificant(123f, 2), 1e-4);
            Assert.Near(1900.0, IntelFog.RoundSignificant(1949f, 2), 1e-3);
            Assert.Near(2000.0, IntelFog.RoundSignificant(1950f, 2), 1e-3, "halves go up");
            Assert.Near(46.0, IntelFog.RoundSignificant(45.6f, 2), 1e-4);
            Assert.Near(120000.0, IntelFog.RoundSignificant(123456f, 2), 1e-2);
            Assert.Near(100.0, IntelFog.RoundSignificant(99.6f, 2), 1e-4, "it carries over: 99.6 is 100");
            Assert.Near(130.0, IntelFog.RoundSignificant(125f, 2), 1e-4);
            Assert.Near(123.0, IntelFog.RoundSignificant(123f, 3), 1e-4, "the digits are a parameter");
            Assert.Near(100.0, IntelFog.RoundSignificant(123f, 1), 1e-4);
            Assert.Near(100.0, IntelFog.RoundSignificant(123f, 0), 1e-4, "fewer than one digit counts as one");
            Assert.Near(-120.0, IntelFog.RoundSignificant(-123f, 2), 1e-4, "the sign stays");
            Assert.Near(0.012, IntelFog.RoundSignificant(0.0123f, 2), 1e-6, "a small number is rounded by its own size");
        }

        [Test]
        public void TheIps_SurvivesZeroAndBadNumbers()
        {
            Assert.Near(0.0, IntelFog.RoundSignificant(0f, 2), 1e-12);
            Assert.Near(0.0, IntelFog.RoundSignificant(float.NaN, 2), 1e-12);
            Assert.Near(0.0, IntelFog.RoundSignificant(float.NegativeInfinity, 2), 1e-12);
            Assert.Equal("0", IntelFog.FormatIps(0f, false));
            Assert.Equal("≈ 0", IntelFog.FormatIps(0f, true));
            Assert.Equal("0", IntelFog.FormatIps(float.NaN, false));
        }

        [Test]
        public void TheIps_IsGroupedByThreeDigits_AndFormattedByTheGame_WhenItIsGiven()
        {
            Assert.Equal("1 234 567", IntelFog.FormatIps(1234567f, false));
            Assert.Equal("≈ 1 200 000", IntelFog.FormatIps(1234567f, true));

            // the game's formatter writes the number in its own units; the fog rounds the value before the formatter sees it
            Func<float, string> game = value => (value / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
            Assert.Equal("≈ 120K", IntelFog.FormatIps(123000f, true, 2, game, null));
            Assert.Equal("123K", IntelFog.FormatIps(123000f, false, 2, game, null));
            Assert.Equal("[120K]", IntelFog.FormatIps(123000f, true, 2, game, text => "[" + text + "]"));
        }

        // ---- nothing but the display ----

        [Test]
        public void TheFog_ChangesNothingButTheText_TheInputsAreNotTouched()
        {
            float share = 0.173f;
            float ips = 123f;
            IntelFog.FormatShare(share, true);
            IntelFog.FormatIps(ips, true);
            Assert.Near(0.173, share, 1e-7);
            Assert.Near(123.0, ips, 1e-9);

            Assert.Equal("≈ ", IntelFog.Approx);
            Assert.Near(5.0, IntelFog.DefaultShareStepPp, 1e-9);
            Assert.Equal(2, IntelFog.DefaultSignificantDigits);
        }

        [Test]
        public void TheShippedConfig_HasTheNumbersOfTheDesign()
        {
            var rig = new AttitudeRig();
            Assert.Near(IntelFog.DefaultShareStepPp, rig.Catalog.Config.intelFog.shareStepPp, 1e-9);
            Assert.Equal(IntelFog.DefaultSignificantDigits, rig.Catalog.Config.intelFog.ipsSignificantDigits);
        }
    }
}
