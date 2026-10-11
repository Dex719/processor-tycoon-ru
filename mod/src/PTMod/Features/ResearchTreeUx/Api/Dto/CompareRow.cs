namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// One row of the CPU compare window (FR-6): the label, the two formatted values, and, for numeric rows, the numbers the window uses for
    /// the difference column and the highlight. Built-in rows are made by the feature itself; other features add rows through
    /// <see cref="ICompareRowProvider"/> (for example <c>cpu-architectures</c> adds "Architecture" and "Rating multiplier").
    /// <para>
    /// A plain data holder with public fields (the shape fixed by the spec of <c>research-tree-ux</c>). A provider normally sets
    /// <see cref="Id"/>, <see cref="LabelKey"/>, <see cref="ValueA"/>, <see cref="ValueB"/> and <see cref="Order"/>; a row with numbers
    /// (<see cref="NumA"/>, <see cref="NumB"/>, <see cref="Better"/>) may leave <see cref="DeltaText"/> and <see cref="Winner"/> at their
    /// defaults, the compare model fills them in from the numbers.
    /// </para>
    /// </summary>
    public sealed class CompareRow
    {
        /// <summary>Stable identifier of the row, unique among all rows of the table (built-in ones: <c>ips</c>, <c>frequency</c>, <c>year</c>...).</summary>
        public string Id;

        /// <summary><c>ModL10n</c> key of the row label; the key belongs to the feature that adds the row.</summary>
        public string LabelKey;

        /// <summary>The value of CPU A as shown to the player (already formatted and localized).</summary>
        public string ValueA;

        /// <summary>The value of CPU B as shown to the player (already formatted and localized).</summary>
        public string ValueB;

        /// <summary>The number behind <see cref="ValueA"/>; null for a text row. Used for the difference and the highlight.</summary>
        public float? NumA;

        /// <summary>The number behind <see cref="ValueB"/>; null for a text row.</summary>
        public float? NumB;

        /// <summary>Which side of the numbers is better; <see cref="ResearchTreeUx.Better.None"/> for no highlight.</summary>
        public Better Better;

        /// <summary>
        /// Sort key, ascending; rows with equal values keep the order in which they were added. The built-in rows use 10...200 in steps of ten
        /// (<c>Content/research-tree-ux/ui.json</c>).
        /// </summary>
        public int Order;

        /// <summary>The difference column text: "+18 %" or "—" when there is nothing to compare. Null in a row made by a provider (filled in by the window).</summary>
        public string DeltaText;

        /// <summary>The highlighted side: -1 = A is better, 0 = none, +1 = B is better. 0 in a row made by a provider (filled in by the window).</summary>
        public int Winner;
    }
}
