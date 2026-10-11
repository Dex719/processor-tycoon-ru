namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The category of a technology as the tab strip sees it (FR-1): the seven values of the game's <c>TechnologyType</c> plus <see cref="All"/>,
    /// the tab that shows everything. The numbers are the values of <c>TechnologyType</c> (the logic keeps the type as a plain <c>int</c>, because
    /// it must not reference the game), so a cast from the game's enum is the identity.
    /// </summary>
    public enum TechCategory
    {
        /// <summary>Not a technology type: the tab "All" (every button and line of the tree).</summary>
        All = -1,

        Package = 0,
        Lithography = 1,
        Memory = 2,
        Frequency = 3,
        CacheSize = 4,
        WaferSize = 5,
        Multicore = 6
    }
}
