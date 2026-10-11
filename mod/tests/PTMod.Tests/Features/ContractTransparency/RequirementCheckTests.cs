using System;
using System.Collections.Generic;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-004: the three requirements of <c>Contract.AddOffer</c> (FR-1). The border cases are the game's: <c>Ips &gt;= MinIps × tolerance</c> and
    /// <c>Power &lt;= Max</c> pass at equality. The package family has nine cases: contract type 0, 1, 2 against a DIP, a PLCC and a PGA CPU.
    /// </summary>
    [Covers(typeof(RequirementCheck), typeof(RequirementResult), typeof(RequirementKind))]
    public class RequirementCheckTests
    {
        private const float Tolerance = 0.75f;

        private static float OneUlpBelow(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits - 1), 0);
        }

        // ---- IPS ----

        [Test]
        public void Ips_EqualToTheLowestAcceptedValue_Passes()
        {
            float minIps = 1.2f;
            float limit = (float)(minIps * Tolerance);

            RequirementResult result = RequirementCheck.Ips(limit, minIps, Tolerance);

            Assert.True(result.Passed);
            Assert.Equal(RequirementKind.Ips, result.Kind);
            Assert.Equal(limit, result.Limit);
            Assert.Equal(minIps, result.Required);
            Assert.Equal(limit, result.Actual);
            Assert.Equal(0f, result.Gap);
            Assert.Equal(0f, result.GapPercent);
        }

        [Test]
        public void Ips_OneStepBelowTheLowestAcceptedValue_Fails()
        {
            float minIps = 1.2f;
            float limit = (float)(minIps * Tolerance);

            RequirementResult result = RequirementCheck.Ips(OneUlpBelow(limit), minIps, Tolerance);

            Assert.False(result.Passed);
            Assert.True(result.Gap > 0f, "a failed requirement has a gap");
        }

        [Test]
        public void Ips_AC_1_1_ShowsTheShortfallInMipsAndPercentOfTheAcceptedMinimum()
        {
            // MinIps 1.20, tolerance 0.75 -> the lowest accepted value is 0.90; the CPU has 0.80.
            RequirementResult result = RequirementCheck.Ips(0.80f, 1.20f, Tolerance);

            Assert.False(result.Passed);
            Assert.Near(0.90, result.Limit, 1e-6);
            Assert.Near(1.20, result.Required, 1e-6);
            Assert.Near(0.80, result.Actual, 1e-6);
            Assert.Near(0.10, result.Gap, 1e-5);
            Assert.Near(11.11, result.GapPercent, 0.01);
        }

        [Test]
        public void Ips_AC_12_1_ReadsTheToleranceItIsGiven()
        {
            RequirementResult seventy = RequirementCheck.Ips(0.70f, 1.0f, 0.7f);
            RequirementResult vanilla = RequirementCheck.Ips(0.70f, 1.0f, 0.75f);

            Assert.True(seventy.Passed);
            Assert.Near(0.7, seventy.Limit, 1e-6);
            Assert.False(vanilla.Passed);
            Assert.Near(0.75, vanilla.Limit, 1e-6);
        }

        [Test]
        public void Ips_OfACpuAboveTheRequirement_Passes_AndOfANotANumber_Fails_WithoutAGap()
        {
            Assert.True(RequirementCheck.Ips(5f, 1f, Tolerance).Passed);
            Assert.True(RequirementCheck.Ips(5f, 0f, Tolerance).Passed);

            RequirementResult nan = RequirementCheck.Ips(float.NaN, 1f, Tolerance);
            Assert.False(nan.Passed);
            Assert.Equal(0f, nan.Gap);
            Assert.Equal(0f, nan.GapPercent);
        }

        // ---- power ----

        [Test]
        public void Power_EqualToTheMaximum_Passes()
        {
            RequirementResult result = RequirementCheck.Power(60f, 60f);

            Assert.True(result.Passed);
            Assert.Equal(RequirementKind.Power, result.Kind);
            Assert.Equal(60f, result.Limit);
            Assert.Equal(60f, result.Required);
            Assert.Equal(60f, result.Actual);
            Assert.Equal(0f, result.Gap);
        }

        [Test]
        public void Power_AC_1_2_ShowsTheExcessInWatts()
        {
            RequirementResult result = RequirementCheck.Power(70f, 60f);

            Assert.False(result.Passed);
            Assert.Equal(10f, result.Gap);
            Assert.Near(16.67, result.GapPercent, 0.01);
        }

        [Test]
        public void Power_BelowTheMaximum_Passes()
        {
            Assert.True(RequirementCheck.Power(45f, 60f).Passed);
            Assert.True(RequirementCheck.Power(0f, 60f).Passed);
        }

        // ---- package: contract type 0 (any), 1 (DIP or PLCC), 2 (PGA or LGA) against DIP, PLCC, PGA ----

        [Test]
        public void Package_AnyContract_AcceptsDip()
        {
            Assert.True(RequirementCheck.Package(0, true, false, false).Passed);
        }

        [Test]
        public void Package_AnyContract_AcceptsPlcc()
        {
            Assert.True(RequirementCheck.Package(0, false, true, false).Passed);
        }

        [Test]
        public void Package_AnyContract_AcceptsPga()
        {
            Assert.True(RequirementCheck.Package(0, false, false, true).Passed);
        }

        [Test]
        public void Package_DipOrPlccContract_AcceptsDip()
        {
            Assert.True(RequirementCheck.Package(1, true, false, false).Passed);
        }

        [Test]
        public void Package_DipOrPlccContract_AcceptsPlcc()
        {
            Assert.True(RequirementCheck.Package(1, false, true, false).Passed);
        }

        [Test]
        public void Package_DipOrPlccContract_RejectsPga_AC_1_3()
        {
            RequirementResult result = RequirementCheck.Package(1, false, false, true);

            Assert.False(result.Passed);
            Assert.Equal(RequirementKind.Package, result.Kind);
            Assert.Equal((float)RequirementCheck.PackageDipPlcc, result.Required);
            Assert.Equal((float)RequirementCheck.PackagePgaLga, result.Actual);
        }

        [Test]
        public void Package_PgaOrLgaContract_RejectsDip()
        {
            Assert.False(RequirementCheck.Package(2, true, false, false).Passed);
        }

        [Test]
        public void Package_PgaOrLgaContract_RejectsPlcc()
        {
            Assert.False(RequirementCheck.Package(2, false, true, false).Passed);
        }

        [Test]
        public void Package_PgaOrLgaContract_AcceptsPga()
        {
            Assert.True(RequirementCheck.Package(2, false, false, true).Passed);
        }

        [Test]
        public void Package_OfACpuInNeitherFamily_FitsOnlyAContractThatTakesAny()
        {
            // A BGA: not DIP, not PLCC, not PGA/LGA.
            Assert.True(RequirementCheck.Package(0, false, false, false).Passed);
            Assert.False(RequirementCheck.Package(1, false, false, false).Passed);
            Assert.False(RequirementCheck.Package(2, false, false, false).Passed);
            Assert.Equal((float)RequirementCheck.PackageOther, RequirementCheck.Package(2, false, false, false).Actual);
        }

        [Test]
        public void Package_OfAnUnknownContractType_MatchesNothing_AsInTheGame()
        {
            Assert.False(RequirementCheck.Package(3, true, true, true).Passed);
            Assert.False(RequirementCheck.Package(-1, false, false, true).Passed);
        }

        [Test]
        public void Package_ReportsTheClassOfTheCpu()
        {
            Assert.Equal((float)RequirementCheck.PackageDipPlcc, RequirementCheck.Package(0, true, false, false).Actual);
            Assert.Equal((float)RequirementCheck.PackageDipPlcc, RequirementCheck.Package(0, false, true, false).Actual);
            Assert.Equal((float)RequirementCheck.PackagePgaLga, RequirementCheck.Package(0, false, false, true).Actual);
            Assert.Equal(0f, RequirementCheck.Package(2, true, false, false).Gap, "a package has no gap");
        }

        // ---- the total ----

        [Test]
        public void AnyFailed_IsTrueWhenOneRequirementFails()
        {
            RequirementResult ok = RequirementCheck.Power(10f, 60f);
            RequirementResult bad = RequirementCheck.Power(70f, 60f);

            Assert.False(RequirementCheck.AnyFailed(new List<RequirementResult> { ok, ok, ok }));
            Assert.True(RequirementCheck.AnyFailed(new List<RequirementResult> { ok, bad, ok }));
            Assert.True(RequirementCheck.AnyFailed(new[] { bad }));
        }

        [Test]
        public void AnyFailed_OfNothing_IsFalse()
        {
            Assert.False(RequirementCheck.AnyFailed(null));
            Assert.False(RequirementCheck.AnyFailed(new List<RequirementResult>()));
        }
    }
}
