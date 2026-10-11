namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Which side of a numeric compare row is the better one (FR-6 item 4): the window highlights the better CPU when the difference reaches
    /// the threshold. Part of the public API of <c>research-tree-ux</c>; the Dto folder holds only plain data types without Unity or game
    /// references.
    /// </summary>
    public enum Better
    {
        /// <summary>No direction: the row is never highlighted (price, die size, year, text rows).</summary>
        None = 0,

        /// <summary>A larger number is better (IPS, frequency, cores).</summary>
        Higher = 1,

        /// <summary>A smaller number is better (power, temperature, unit cost).</summary>
        Lower = 2,
    }
}
