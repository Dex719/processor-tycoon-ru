using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the requirement block of the breakdown needs, from <c>RequirementReport</c> and the contract; filled by the panel.</summary>
    public struct RequirementPanelInput
    {
        public RequirementResult Ips;
        public RequirementResult Power;
        public RequirementResult Package;

        /// <summary><c>mipsToleranceMultiplier</c> of the contract (0.75 in the game).</summary>
        public float Tolerance;

        /// <summary><c>Contract.PackageTypeString()</c>.</summary>
        public string RequiredPackageText;

        /// <summary><c>IPackage.PackageTypeString()</c> of the CPU.</summary>
        public string CpuPackageText;

        /// <summary><c>Contract.FailsAtAnyRequirement(cpu)</c>: the verdict the panel shows, because it is the game's (FR-12 item 1).</summary>
        public bool FailsByGame;

        /// <summary>The mod's own verdict and the game's differ.</summary>
        public bool MismatchWithGame;
    }

    /// <summary>
    /// The three lines of requirements of the breakdown with their numbers (FR-1): IPS, power, package, each green or red, a detail line under a red one (how much is missing, how much is over, which
    /// package is needed), the verdict, and the note when the mod and the game disagree. The numbers are plain; the panel prints them with the game's formatters.
    /// </summary>
    public static class RequirementLines
    {
        public static List<PanelLine> Build(RequirementPanelInput input)
        {
            var lines = new List<PanelLine>();

            // IPS: the CPU's value, the lowest the contract takes (MinIps x tolerance), and what share of the asked-for value that is.
            RequirementResult ips = input.Ips;
            lines.Add(new PanelLine("ct.req.ips", ToneOf(ips),
                PanelArg.Ips(ips.Actual), PanelArg.Ips(ips.Limit), PanelArg.Percent(Math.Round(input.Tolerance * 100.0, 3)), PanelArg.Ips(ips.Required)));
            if (!ips.Passed)
                lines.Add(new PanelLine("ct.req.ips.fail", LineTone.Bad, PanelArg.Ips(ips.Gap), PanelArg.Percent(ips.GapPercent)).Indented(1));

            RequirementResult power = input.Power;
            lines.Add(new PanelLine("ct.req.power", ToneOf(power), PanelArg.Whole(power.Actual), PanelArg.Whole(power.Limit)));
            if (!power.Passed)
                lines.Add(new PanelLine("ct.req.power.fail", LineTone.Bad, PanelArg.Whole(power.Gap)).Indented(1));

            RequirementResult package = input.Package;
            lines.Add(new PanelLine("ct.req.package", ToneOf(package), PanelArg.OfText(input.CpuPackageText), PanelArg.OfText(input.RequiredPackageText)));
            if (!package.Passed)
                lines.Add(new PanelLine("ct.req.package.fail", LineTone.Bad, PanelArg.OfText(input.RequiredPackageText), PanelArg.OfText(input.CpuPackageText)).Indented(1));

            // The verdict is the game's: AddOffer turns a CPU down silently and the chance is 0 % (FR-1 item 6).
            if (input.FailsByGame) lines.Add(new PanelLine("ct.req.rejected", LineTone.Bad).AsBold());
            else lines.Add(new PanelLine("ct.req.allOk", LineTone.Good).AsBold());

            if (input.MismatchWithGame) lines.Add(new PanelLine("ct.req.mismatch", LineTone.Warn));
            return lines;
        }

        private static LineTone ToneOf(RequirementResult result)
        {
            return result.Passed ? LineTone.Good : LineTone.Bad;
        }
    }
}
