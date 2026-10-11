namespace PTMod.Features.CpuArchitectures
{
    /// <summary>The words the section of the side file uses for the enums of the state (design.md, "Валидация при загрузке"). Internal: only the registry maps them.</summary>
    internal static class ArchSaveNames
    {
        public static string Of(PauseReason reason)
        {
            switch (reason)
            {
                case PauseReason.TechnologyChosen: return "technology";
                case PauseReason.Default: return "default";
                case PauseReason.NoFunds: return "funds";
                case PauseReason.FeatureDisabled: return "disabled";
                default: return "none";
            }
        }

        public static bool TryParse(string text, out PauseReason reason)
        {
            switch (text)
            {
                case "none": reason = PauseReason.None; return true;
                case "technology": reason = PauseReason.TechnologyChosen; return true;
                case "default": reason = PauseReason.Default; return true;
                case "funds": reason = PauseReason.NoFunds; return true;
                case "disabled": reason = PauseReason.FeatureDisabled; return true;
                default: reason = PauseReason.None; return false;
            }
        }

        public static string Of(SupportTier tier)
        {
            switch (tier)
            {
                case SupportTier.Low: return "low";
                case SupportTier.Normal: return "normal";
                case SupportTier.High: return "high";
                default: return "off";
            }
        }

        public static bool TryParse(string text, out SupportTier tier)
        {
            switch (text)
            {
                case "off": tier = SupportTier.Off; return true;
                case "low": tier = SupportTier.Low; return true;
                case "normal": tier = SupportTier.Normal; return true;
                case "high": tier = SupportTier.High; return true;
                default: tier = SupportTier.Off; return false;
            }
        }
    }
}
