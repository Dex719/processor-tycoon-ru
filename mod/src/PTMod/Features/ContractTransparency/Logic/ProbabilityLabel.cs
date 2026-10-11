namespace PTMod.Features.ContractTransparency
{
    /// <summary>What decides whether the probability field of the contract window gets the actual probability (FR-2 item 8).</summary>
    public struct ProbabilityContext
    {
        /// <summary>The feature is on.</summary>
        public bool FeatureActive;

        /// <summary>The setting "show the actual probability".</summary>
        public bool SettingOn;

        /// <summary>All the private members of the game the analysis needs were found (FR-12 item 3).</summary>
        public bool InspectorAvailable;

        /// <summary>A contract and a CPU are chosen in the window.</summary>
        public bool HasSelection;

        /// <summary>The CPU fails a requirement: the game writes "0%" and nothing more is true to add.</summary>
        public bool CpuFailsRequirements;

        /// <summary>The window is on the screen. A closed window is not worked out at every day change (NFR-1); it is refreshed when it opens.</summary>
        public bool WindowShown;
    }

    /// <summary>
    /// The text "40% (actual 21 %)" that the mod writes after the probability the game wrote (FR-2 item 8): when to write it and what the line is. The patch of
    /// <c>ContractWindow.UpdateProbabilityText</c> asks and prints.
    /// </summary>
    public static class ProbabilityLabel
    {
        /// <summary>The key of the text; <c>{0}</c> is the game's text, <c>{1}</c> the actual probability in percent.</summary>
        public const string Key = "ct.odds.actual";

        /// <summary>
        /// Whether the actual probability goes after the game's text. It does not for a CPU that fails a requirement (the game says "0%"), when the setting is off, when the members of the game are
        /// missing, and when no offers are there yet (the probability is the 100 % the game shows already).
        /// </summary>
        public static bool ShouldAppend(ProbabilityContext context, bool noOffersYet)
        {
            return context.FeatureActive && context.SettingOn && context.InspectorAvailable && context.HasSelection
                   && !context.CpuFailsRequirements && !noOffersYet;
        }

        /// <summary>Whether the work has to be done at all now: a closed window waits until it is shown.</summary>
        public static bool NeedsWork(ProbabilityContext context)
        {
            return context.FeatureActive && context.SettingOn && context.InspectorAvailable && context.HasSelection
                   && !context.CpuFailsRequirements && context.WindowShown;
        }

        /// <summary>The line to print: the text of the game and the actual probability (0..1).</summary>
        public static PanelLine Line(string gameText, double actual)
        {
            return new PanelLine(Key, LineTone.Normal, PanelArg.OfText(gameText), PanelArg.Actual(actual));
        }

        /// <summary>
        /// The text the game wrote, given what the field shows now and what the mod wrote into it last time: when the field still holds the mod's own text, the game's text is what the mod
        /// remembered (the field is taken back to it); otherwise the field holds the game's fresh text.
        /// </summary>
        public static string GameTextOf(string current, string lastWritten, string rememberedGameText)
        {
            if (lastWritten != null && current == lastWritten && rememberedGameText != null) return rememberedGameText;
            return current ?? string.Empty;
        }
    }
}
