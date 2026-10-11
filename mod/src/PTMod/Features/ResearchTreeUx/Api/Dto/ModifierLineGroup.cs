using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A titled block of <see cref="ModifierLine"/> in the "All modifiers" panel (FR-5): "Research speed", "Research cost", and a block of
    /// another feature returned by <see cref="IModifierLineSource.GetGroup"/> (for example "Architectures" of <c>cpu-architectures</c>).
    /// <para>A plain data holder with public fields (the shape fixed by the spec of <c>research-tree-ux</c>).</para>
    /// </summary>
    public sealed class ModifierLineGroup
    {
        /// <summary><c>ModL10n</c> key of the block heading; the key belongs to the feature that makes the group.</summary>
        public string TitleKey;

        /// <summary>The lines of the block, top to bottom. Empty by default, never null on a new object.</summary>
        public List<ModifierLine> Lines = new List<ModifierLine>();
    }
}
