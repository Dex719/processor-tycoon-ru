namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The game's grey-out of a technology the company does not need: a copy of <c>ResearchButton.TechnologyIsUnnecessary</c>, which the list
    /// and the tooltip must agree with (FR-2, FR-4). The kinds are the company types of the game: a fabless company has no fab of its own,
    /// a foundry only builds chips for others, anything else (IDM) needs all of it.
    /// <list type="bullet">
    /// <item>Fabless: lithography (type 1) and wafer size (type 5) are unnecessary, the rest is needed.</item>
    /// <item>Foundry: only lithography and wafer size are needed, the rest is unnecessary.</item>
    /// <item>IDM (neither flag): nothing is unnecessary.</item>
    /// </list>
    /// The fabless check comes first, as in the game: a company that reports both flags is treated as fabless.
    /// </summary>
    public static class UnnecessaryRule
    {
        /// <summary>The game's <c>TechnologyType.Lithography</c>.</summary>
        public const int Lithography = 1;

        /// <summary>The game's <c>TechnologyType.WaferSize</c>.</summary>
        public const int WaferSize = 5;

        /// <param name="technologyType">The value of the game's <c>TechnologyType</c>.</param>
        /// <param name="isFabless"><c>Player.Company.IsFabless</c>.</param>
        /// <param name="isFoundry"><c>Player.Company.IsFoundry</c>.</param>
        public static bool IsUnnecessary(int technologyType, bool isFabless, bool isFoundry)
        {
            bool manufacturingType = technologyType == Lithography || technologyType == WaferSize;
            if (isFabless) return manufacturingType;
            if (isFoundry) return !manufacturingType;
            return false;
        }
    }
}
