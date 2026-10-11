using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The lab block at the top of the panel "Архитектуры" (FR-3 items 4 and 5; design.md Component 13), pure: which line it shows, which widgets of the
    /// current research are there, which buttons work, and the arithmetic of the funding slider (the slider counts whole percent, the game's
    /// <c>ResearchSector.Funding</c> is a fraction: 1 % … 100 %, up to 500 % with <c>InnovationEffort</c>).
    /// </summary>
    public static class LabBlockModel
    {
        /// <summary>The lowest whole percent of the slider (the game's own slider starts at 1).</summary>
        public const int MinPercent = 1;

        /// <summary>The highest whole percent of the slider.</summary>
        public const int NormalMaxPercent = 100;

        /// <summary>The highest whole percent with <c>ResearchSector.InnovationEffort</c>.</summary>
        public const int InnovationMaxPercent = 500;

        /// <summary><c>ModL10n</c> key of the line that says what occupies the lab: free, a technology, an architecture, a paused architecture.</summary>
        public static string StatusKey(LabStatus status)
        {
            switch (status)
            {
                case LabStatus.Technology: return "arch.lab.tech";
                case LabStatus.Architecture: return "arch.lab.arch";
                case LabStatus.ArchitecturePaused: return "arch.lab.paused";
                default: return "arch.lab.free";
            }
        }

        /// <summary>An architecture is the current research of the company (running or on pause): the progress, the numbers, the slider and the buttons are there.</summary>
        public static bool HasArchitecture(LabStatus status)
        {
            return status == LabStatus.Architecture || status == LabStatus.ArchitecturePaused;
        }

        /// <summary>"Остановить" works: there is an architecture to stop.</summary>
        public static bool CanStop(LabStatus status, bool researchUsable)
        {
            return researchUsable && HasArchitecture(status);
        }

        /// <summary>"Продолжить" works: the architecture is paused (and a technology of the player in the lab, if there is one, can be interrupted).</summary>
        public static bool CanResume(LabStatus status, bool researchUsable, bool technologyBlocks = false)
        {
            return researchUsable && !technologyBlocks && status == LabStatus.ArchitecturePaused;
        }

        /// <summary><c>ModL10n</c> key of the reason a research stands still ("лаборатория занята технологией", "компания в дефолте", …); null for none.</summary>
        public static string PauseKey(PauseReason reason)
        {
            switch (reason)
            {
                case PauseReason.TechnologyChosen: return "arch.pause.technology";
                case PauseReason.Default: return "arch.pause.default";
                case PauseReason.NoFunds: return "arch.pause.noFunds";
                case PauseReason.FeatureDisabled: return "arch.pause.disabled";
                default: return null;
            }
        }

        /// <summary>The top of the slider in whole percent: 100, or 500 with <c>InnovationEffort</c>.</summary>
        public static int MaxPercent(bool innovationEffort)
        {
            return innovationEffort ? InnovationMaxPercent : NormalMaxPercent;
        }

        /// <summary>The position of the slider for a funding: the whole percent, within 1 … <see cref="MaxPercent"/>; an unusable funding is 100 %.</summary>
        public static int FundingToPercent(float funding, bool innovationEffort)
        {
            if (float.IsNaN(funding) || float.IsInfinity(funding)) return NormalMaxPercent;
            int percent = (int)Math.Round(funding * 100f, MidpointRounding.AwayFromZero);
            int max = MaxPercent(innovationEffort);
            return percent < MinPercent ? MinPercent : (percent > max ? max : percent);
        }

        /// <summary>The funding the game gets for a position of the slider: <c>percent / 100</c> within the limits of the player's slider (<see cref="LabRules.ClampPlayerFunding"/>).</summary>
        public static float PercentToFunding(float percent, bool innovationEffort)
        {
            return LabRules.ClampPlayerFunding(percent / 100f, innovationEffort);
        }
    }
}
