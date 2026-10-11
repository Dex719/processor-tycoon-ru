using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>The three requirements a contract puts on a CPU (<c>Contract.AddOffer</c>, <c>Contract.FailsAtAnyRequirement</c>, <c>Contract.IsRequestedPackage</c>).</summary>
    public enum RequirementKind
    {
        Ips,
        Power,
        Package,
    }

    /// <summary>
    /// The verdict on one requirement with the numbers behind it (FR-1). For <see cref="RequirementKind.Ips"/>: <see cref="Actual"/> is the CPU's IPS,
    /// <see cref="Required"/> the contract's <c>MinIps</c>, <see cref="Limit"/> the lowest IPS the contract accepts (<c>MinIps × tolerance</c>). For
    /// <see cref="RequirementKind.Power"/>: <see cref="Actual"/> is the CPU's consumption, <see cref="Required"/> and <see cref="Limit"/> the contract's
    /// maximum. For <see cref="RequirementKind.Package"/>: <see cref="Actual"/> is the class of the CPU's package and <see cref="Required"/> and
    /// <see cref="Limit"/> the class the contract asks for (<see cref="RequirementCheck.PackageAny"/>, <see cref="RequirementCheck.PackageDipPlcc"/>,
    /// <see cref="RequirementCheck.PackagePgaLga"/>; <see cref="RequirementCheck.PackageOther"/> for a CPU in neither family).
    /// </summary>
    public struct RequirementResult
    {
        public RequirementKind Kind;

        /// <summary>The CPU meets the requirement.</summary>
        public bool Passed;

        public float Actual;
        public float Limit;
        public float Required;

        /// <summary>For a failed requirement how far the CPU is from the limit (MIPS short, watts over); 0 for a passed one and for a package.</summary>
        public float Gap;

        /// <summary>
        /// <see cref="Gap"/> as a percent of <see cref="Limit"/> (for IPS: of the lowest accepted IPS, "11 % short"; for power: of the maximum, "17 % over");
        /// 0 for a passed requirement and for a package.
        /// </summary>
        public float GapPercent;
    }

    /// <summary>
    /// The three checks of <c>Contract.AddOffer</c> as pure functions with the numbers the player wants to see (FR-1). Comparisons are the game's,
    /// in float: <c>Ips &gt;= MinIps × tolerance</c>, <c>Power &lt;= MaxPower</c>, and the package families of <c>IsRequestedPackage</c>. The adapter checks the
    /// total against <c>Contract.FailsAtAnyRequirement</c> (FR-12).
    /// </summary>
    public static class RequirementCheck
    {
        /// <summary>The contract takes any package (<c>Contract.PackageType</c> 0).</summary>
        public const int PackageAny = 0;

        /// <summary>The contract asks for DIP or PLCC (<c>PackageType</c> 1).</summary>
        public const int PackageDipPlcc = 1;

        /// <summary>The contract asks for PGA or LGA (<c>PackageType</c> 2).</summary>
        public const int PackagePgaLga = 2;

        /// <summary>The class of a CPU whose package is in neither family (a BGA, for one): it fits only a contract that takes any package.</summary>
        public const int PackageOther = 0;

        /// <summary>The IPS requirement: the CPU passes when <c>cpuIps &gt;= minIps × tolerance</c> (the game's tolerance is 0.75). The border passes.</summary>
        public static RequirementResult Ips(float cpuIps, float minIps, float tolerance)
        {
            float limit = (float)(minIps * tolerance);
            bool passed = cpuIps >= limit;

            var result = new RequirementResult
            {
                Kind = RequirementKind.Ips,
                Passed = passed,
                Actual = cpuIps,
                Limit = limit,
                Required = minIps,
            };
            if (!passed) FillGap(ref result, limit - cpuIps);
            return result;
        }

        /// <summary>The power requirement: the CPU passes when <c>cpuPower &lt;= maxPower</c>. The border passes.</summary>
        public static RequirementResult Power(float cpuPower, float maxPower)
        {
            bool passed = cpuPower <= maxPower;

            var result = new RequirementResult
            {
                Kind = RequirementKind.Power,
                Passed = passed,
                Actual = cpuPower,
                Limit = maxPower,
                Required = maxPower,
            };
            if (!passed) FillGap(ref result, cpuPower - maxPower);
            return result;
        }

        /// <summary>
        /// The package requirement, the logic of <c>Contract.IsRequestedPackage</c>: 0 takes any CPU, 1 wants DIP or PLCC, 2 wants PGA or LGA; any other
        /// value of the contract matches nothing (as in the game).
        /// </summary>
        /// <param name="packageType"><c>Contract.PackageType</c>.</param>
        /// <param name="isDip"><c>IPackage.IsDip</c> of the CPU.</param>
        /// <param name="isPlcc"><c>IPackage.IsPlcc</c>.</param>
        /// <param name="isPgaOrLga"><c>IPackage.IsPgaOrLga</c>.</param>
        public static RequirementResult Package(int packageType, bool isDip, bool isPlcc, bool isPgaOrLga)
        {
            bool passed;
            switch (packageType)
            {
                case PackageAny:
                    passed = true;
                    break;
                case PackageDipPlcc:
                    passed = isDip || isPlcc;
                    break;
                case PackagePgaLga:
                    passed = isPgaOrLga;
                    break;
                default:
                    passed = false;
                    break;
            }

            return new RequirementResult
            {
                Kind = RequirementKind.Package,
                Passed = passed,
                Actual = isDip || isPlcc ? PackageDipPlcc : isPgaOrLga ? PackagePgaLga : PackageOther,
                Limit = packageType,
                Required = packageType,
            };
        }

        /// <summary>True when at least one of the results failed (a null or empty list fails nothing).</summary>
        public static bool AnyFailed(IReadOnlyList<RequirementResult> results)
        {
            if (results == null) return false;
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].Passed) return true;
            }
            return false;
        }

        private static void FillGap(ref RequirementResult result, float gap)
        {
            if (float.IsNaN(gap) || float.IsInfinity(gap) || gap < 0f) gap = 0f;
            result.Gap = gap;
            result.GapPercent = result.Limit > 0f && !float.IsInfinity(result.Limit) ? gap / result.Limit * 100f : 0f;
        }
    }
}
