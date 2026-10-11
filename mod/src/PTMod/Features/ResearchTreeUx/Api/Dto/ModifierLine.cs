namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// One line of the "All modifiers" panel and its tooltip (FR-5): a localized label with arguments and, when the line is a factor, the
    /// multiplier. The feature builds its own lines from the game's modifier table and from <c>ModModifiers.Explain</c>; other features add
    /// groups of lines through <see cref="IModifierLineSource"/>.
    /// <para>A plain data holder with public fields (the shape fixed by the spec of <c>research-tree-ux</c>).</para>
    /// </summary>
    public sealed class ModifierLine
    {
        /// <summary><c>ModL10n</c> key of the line text; the key belongs to the feature that makes the line.</summary>
        public string LabelKey;

        /// <summary>Arguments of the label (<c>{0}</c>, <c>{1}</c>... of the translated text), already formatted for the player; null for none.</summary>
        public object[] Args;

        /// <summary>The factor shown as "×1,25"; null when the line has no multiplier (a text or a sum).</summary>
        public float? Multiplier;

        /// <summary>Id of the feature the line comes from (<c>cpu-architectures</c>); null for a line of the game itself.</summary>
        public string SourceFeatureId;

        /// <summary>True when the balance range or the fuse limited the value ("(limited)" is appended to the line).</summary>
        public bool Capped;
    }
}
