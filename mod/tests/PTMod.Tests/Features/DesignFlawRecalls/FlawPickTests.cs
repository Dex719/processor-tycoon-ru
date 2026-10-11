using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-005 (FR-3): the choice of the severity and of the type of a flaw. The distribution of the severity at p = 0.233 over 100 000 draws with a
    /// fixed seed, the window of years of the types, the weight of <c>stability</c> under overclock, the fallback to <c>arith</c>; and that the logic
    /// draws from the <c>IModRng</c> of the core (the feature has no random interface of its own).
    /// </summary>
    [Covers(typeof(FlawRiskModel))]
    public class FlawPickTests
    {
        // ---- severity ----

        [Test]
        public void SeverityWeights_AtP233_AreTheNumbersOfTheDesign()
        {
            // w = [0.60, 0.30 * (1 + 2p), 0.10 * (1 + 4p)] = [0.60, 0.44, 0.19]
            float[] w = FlawRiskModel.SeverityWeights(0.233f, FlawBalance.Defaults());

            Assert.Equal(3, w.Length);
            Assert.Near(0.60, w[0], 1e-6);
            Assert.Near(0.30 * (1 + 2 * 0.233), w[1], 1e-5);
            Assert.Near(0.10 * (1 + 4 * 0.233), w[2], 1e-5);
            Assert.Near(0.44, w[1], 0.005);
            Assert.Near(0.19, w[2], 0.005);
        }

        [Test]
        public void SeverityWeights_AtZero_AreTheBaseWeights_AndShiftWithP()
        {
            FlawBalance b = FlawBalance.Defaults();

            float[] none = FlawRiskModel.SeverityWeights(0f, b);
            Assert.Equal(new[] { 0.60f, 0.30f, 0.10f }, none);

            float[] half = FlawRiskModel.SeverityWeights(0.5f, b);
            Assert.Near(0.30 * 2.0, half[1], 1e-6);
            Assert.Near(0.10 * 3.0, half[2], 1e-6);
            Assert.Equal(none[0], half[0]);
        }

        [Test]
        public void PickSeverity_AtP233_100000Draws_MatchTheDesign()
        {
            // The design: [0.49, 0.36, 0.15] +- 0.01 (the exact shares are [0.487, 0.357, 0.157]).
            FlawBalance b = FlawBalance.Defaults();
            var rng = new Xoshiro128(20260101u);
            var counts = new int[3];
            const int draws = 100000;

            for (int i = 0; i < draws; i++) counts[(int)FlawRiskModel.PickSeverity(0.233f, b, rng)]++;

            Assert.Near(0.49, counts[0] / (double)draws, 0.01, "Minor");
            Assert.Near(0.36, counts[1] / (double)draws, 0.01, "Major");
            Assert.Near(0.15, counts[2] / (double)draws, 0.01, "Critical");
        }

        [Test]
        public void PickSeverity_IsReproducible_WithTheSameSeed()
        {
            FlawBalance b = FlawBalance.Defaults();
            var first = new Xoshiro128(99u);
            var second = new Xoshiro128(99u);

            for (int i = 0; i < 200; i++) Assert.Equal(FlawRiskModel.PickSeverity(0.3f, b, first), FlawRiskModel.PickSeverity(0.3f, b, second));
        }

        [Test]
        public void PickSeverity_ScriptedRng_PicksByTheCumulativeWeights()
        {
            FlawBalance b = FlawBalance.Defaults();   // p = 0: weights [0.6, 0.3, 0.1] of 1.0

            Assert.Equal(FlawSeverity.Minor, FlawRiskModel.PickSeverity(0f, b, new FakeRng(0.59f)));
            Assert.Equal(FlawSeverity.Major, FlawRiskModel.PickSeverity(0f, b, new FakeRng(0.61f)));
            Assert.Equal(FlawSeverity.Major, FlawRiskModel.PickSeverity(0f, b, new FakeRng(0.89f)));
            Assert.Equal(FlawSeverity.Critical, FlawRiskModel.PickSeverity(0f, b, new FakeRng(0.91f)));
            Assert.Equal(FlawSeverity.Critical, FlawRiskModel.PickSeverity(0f, b, new FakeRng(0.9999f)));
        }

        [Test]
        public void PickSeverity_DrawsExactlyOneNumber()
        {
            var rng = new FakeRng(0.3f);
            FlawRiskModel.PickSeverity(0.2f, FlawBalance.Defaults(), rng);
            Assert.Equal(1, rng.Draws);
        }

        [Test]
        public void PickSeverity_BrokenWeights_GiveMinorAndDoNotThrow()
        {
            FlawBalance b = FlawBalance.Defaults();
            b.Severity.BaseWeights = new[] { 0f, 0f, 0f };

            Assert.Equal(FlawSeverity.Minor, FlawRiskModel.PickSeverity(0.3f, b, new FakeRng(0.5f)));

            b.Severity.BaseWeights = new float[0];
            Assert.Equal(FlawSeverity.Minor, FlawRiskModel.PickSeverity(0.3f, b, new FakeRng(0.5f)));
        }

        // ---- type ----

        [Test]
        public void TypeWeight_Stability_AtOverclock04_IsNine()
        {
            // AC-3.2: 3 * (1 + 5 * 0.4) = 9
            FlawBalance b = FlawBalance.Defaults();
            FlawTypeDef stability = b.Types.First(t => t.Id == FlawTypeIds.Stability);

            Assert.Near(9.0, FlawRiskModel.TypeWeight(stability, new FlawRiskFactors { OC = 0.4f }, 2000), 1e-5);
            Assert.Near(3.0, FlawRiskModel.TypeWeight(stability, new FlawRiskFactors { OC = 0f }, 2000), 1e-6);
        }

        [Test]
        public void TypeWeight_Thermal_GrowsWithOverheating_AndArithDoesNot()
        {
            FlawBalance b = FlawBalance.Defaults();
            FlawTypeDef thermal = b.Types.First(t => t.Id == FlawTypeIds.Thermal);
            FlawTypeDef arith = b.Types.First(t => t.Id == FlawTypeIds.Arithmetic);
            var hot = new FlawRiskFactors { OC = 0.4f, TH = 0.5f };

            Assert.Near(2.0 * (1 + 5 * 0.5), FlawRiskModel.TypeWeight(thermal, hot, 2000), 1e-5);
            Assert.Near(3.0, FlawRiskModel.TypeWeight(arith, hot, 2000), 1e-6);
        }

        [Test]
        public void TypeWeight_Security_OnlyInsideItsWindowOfYears()
        {
            FlawTypeDef security = FlawBalance.Defaults().Types.First(t => t.Id == FlawTypeIds.Security);
            var f = new FlawRiskFactors();

            Assert.Near(0.0, FlawRiskModel.TypeWeight(security, f, 2010), 1e-9);   // AC-3.1
            Assert.Near(0.0, FlawRiskModel.TypeWeight(security, f, 2014), 1e-9);
            Assert.Near(2.0, FlawRiskModel.TypeWeight(security, f, 2015), 1e-6);
            Assert.Near(2.0, FlawRiskModel.TypeWeight(security, f, 2030), 1e-6);
            Assert.Near(0.0, FlawRiskModel.TypeWeight(security, f, 2031), 1e-9);
            Assert.Near(0.0, FlawRiskModel.TypeWeight(null, f, 2020), 1e-9);
        }

        [Test]
        public void PickType_2010_NeverReturnsSecurity()
        {
            FlawBalance b = FlawBalance.Defaults();
            var rng = new Xoshiro128(5u);
            var f = new FlawRiskFactors { OC = 0.2f, TH = 0.1f };
            var seen = new HashSet<string>();

            for (int i = 0; i < 5000; i++) seen.Add(FlawRiskModel.PickType(f, 2010, b.Types, rng).Id);

            Assert.False(seen.Contains(FlawTypeIds.Security), "security opens in 2015 (AC-3.1)");
            Assert.True(seen.Contains(FlawTypeIds.Arithmetic) && seen.Contains(FlawTypeIds.Stability) && seen.Contains(FlawTypeIds.Thermal),
                "the other three types are all drawn: " + string.Join(",", seen));
        }

        [Test]
        public void PickType_2018_CanReturnSecurity()
        {
            FlawBalance b = FlawBalance.Defaults();
            var rng = new Xoshiro128(6u);
            var seen = new HashSet<string>();

            for (int i = 0; i < 5000; i++) seen.Add(FlawRiskModel.PickType(new FlawRiskFactors(), 2018, b.Types, rng).Id);

            Assert.True(seen.Contains(FlawTypeIds.Security), "from 2015 on security can come");
        }

        [Test]
        public void PickType_ShareFollowsTheWeights()
        {
            // 2000, OC 0.4: arith 3, stability 3 * 3 = 9, thermal 2, security 2 -> total 16 (shares 0.1875, 0.5625, 0.125, 0.125).
            FlawBalance b = FlawBalance.Defaults();
            var rng = new Xoshiro128(11u);
            var f = new FlawRiskFactors { OC = 0.4f };
            var counts = new Dictionary<string, int>();
            const int draws = 40000;

            for (int i = 0; i < draws; i++)
            {
                string id = FlawRiskModel.PickType(f, 2020, b.Types, rng).Id;
                counts[id] = (counts.ContainsKey(id) ? counts[id] : 0) + 1;
            }

            Assert.Near(3.0 / 16, counts[FlawTypeIds.Arithmetic] / (double)draws, 0.01);
            Assert.Near(9.0 / 16, counts[FlawTypeIds.Stability] / (double)draws, 0.01);
            Assert.Near(2.0 / 16, counts[FlawTypeIds.Thermal] / (double)draws, 0.01);
            Assert.Near(2.0 / 16, counts[FlawTypeIds.Security] / (double)draws, 0.01);
        }

        [Test]
        public void PickType_ScriptedRng_PicksByTheWeightsOfTheYear()
        {
            FlawBalance b = FlawBalance.Defaults();   // 1980, no factors: arith 3, stability 3, thermal 2, security 0 -> 8

            Assert.Equal(FlawTypeIds.Arithmetic, FlawRiskModel.PickType(new FlawRiskFactors(), 1980, b.Types, new FakeRng(0.10f)).Id);
            Assert.Equal(FlawTypeIds.Stability, FlawRiskModel.PickType(new FlawRiskFactors(), 1980, b.Types, new FakeRng(0.50f)).Id);
            Assert.Equal(FlawTypeIds.Thermal, FlawRiskModel.PickType(new FlawRiskFactors(), 1980, b.Types, new FakeRng(0.90f)).Id);
        }

        [Test]
        public void PickType_NoTypeAvailable_FallsBackToArith_WithoutDrawing()
        {
            var onlySecurity = new List<FlawTypeDef> { FlawBalance.Defaults().Types.First(t => t.Id == FlawTypeIds.Security) };
            var rng = new FakeRng(0.9f);

            FlawTypeDef picked = FlawRiskModel.PickType(new FlawRiskFactors(), 2010, onlySecurity, rng);

            Assert.Equal(FlawTypeIds.Arithmetic, picked.Id);
            Assert.Equal(0, rng.Draws);
            Assert.Equal(FlawTypeIds.Arithmetic, FlawRiskModel.PickType(new FlawRiskFactors(), 2010, new List<FlawTypeDef>(), rng).Id);
            Assert.Equal(FlawTypeIds.Arithmetic, FlawRiskModel.PickType(new FlawRiskFactors(), 2010, null, rng).Id);
            Assert.Equal(0, rng.Draws);
        }

        [Test]
        public void PickType_Fallback_UsesTheArithOfTheList_WhenItIsThere()
        {
            // The content may change the numbers of arith; the fallback is that entry even if its window does not hold the year.
            FlawBalance b = FlawBalance.Defaults();
            FlawTypeDef arith = b.Types.First(t => t.Id == FlawTypeIds.Arithmetic);
            arith.FromYear = 2000;
            FlawTypeDef security = b.Types.First(t => t.Id == FlawTypeIds.Security);
            var types = new List<FlawTypeDef> { security, arith };

            Assert.True(ReferenceEquals(arith, FlawRiskModel.PickType(new FlawRiskFactors(), 1980, types, new FakeRng(0.1f))), "the entry of the list");
        }

        [Test]
        public void PickType_ZeroWeights_FallBackToArith()
        {
            FlawBalance b = FlawBalance.Defaults();
            foreach (FlawTypeDef type in b.Types) type.Weight = 0f;

            Assert.Equal(FlawTypeIds.Arithmetic, FlawRiskModel.PickType(new FlawRiskFactors(), 2020, b.Types, new FakeRng(0.3f)).Id);
        }

        // ---- the randomness is the core's ----

        [Test]
        public void Logic_UsesTheCoreInterfaceOfRandomness_NotItsOwn()
        {
            string logic = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls", "Logic");
            var own = new Regex(@"\binterface\s+(\w*(Rng|Random)\w*)\b", RegexOptions.Compiled);

            foreach (string path in SourceScan.Files(logic, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in own.Matches(code))
                {
                    Assert.Fail(Path.GetFileName(path) + " declares its own random interface " + match.Groups[1].Value + "; use IModRng of the core");
                }
            }

            string model = SourceScan.ReadText(Path.Combine(logic, "FlawRiskModel.cs"));
            Assert.True(model.Contains("IModRng rng"), "the pickers take the IModRng of the core");
            Assert.False(model.Contains("System.Random") || model.Contains("UnityEngine.Random"), "no system generator");
        }
    }
}
