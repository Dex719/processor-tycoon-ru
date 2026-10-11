using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The requirements of a contract against one CPU (FR-1, FR-12 item 1; <c>ContractInspector.BuildRequirements</c>): the three results with their
    /// numbers, the verdict of the mod and of the game, and the names of the two packages for the line "needs DIP or PLCC, the CPU has PGA".
    /// </summary>
    public sealed class RequirementReport
    {
        /// <summary>The IPS requirement: the limit is <c>MinIps × Tolerance</c>.</summary>
        public RequirementResult Ips;

        /// <summary>The power requirement.</summary>
        public RequirementResult Power;

        /// <summary>The package requirement.</summary>
        public RequirementResult Package;

        /// <summary><c>Contract.mipsToleranceMultiplier</c> as read from the contract (0.75 in the game, another mod may change it).</summary>
        public float Tolerance;

        /// <summary>The mod's own verdict: at least one of the three failed.</summary>
        public bool FailsByMod;

        /// <summary><c>Contract.FailsAtAnyRequirement(cpu)</c>.</summary>
        public bool FailsByGame;

        /// <summary>The verdict the panel shows: always the game's. <c>Contract.AddOffer</c> turns such a CPU away silently and the signing chance is 0 %.</summary>
        public bool Fails
        {
            get { return FailsByGame; }
        }

        /// <summary>The mod and the game disagree: the panel is marked "differs from the game" and the details are in the log (FR-12 item 1).</summary>
        public bool MismatchWithGame
        {
            get { return FailsByMod != FailsByGame; }
        }

        /// <summary><c>Contract.PackageTypeString()</c>: the package the contract asks for, in the game's words.</summary>
        public string RequiredPackageText;

        /// <summary><c>IPackage.PackageTypeString()</c> of the CPU.</summary>
        public string CpuPackageText;

        /// <summary>The three results in the order of the panel: IPS, power, package.</summary>
        public IReadOnlyList<RequirementResult> Results
        {
            get { return new[] { Ips, Power, Package }; }
        }
    }
}
