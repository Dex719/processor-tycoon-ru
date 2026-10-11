namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Whether the player sees the character of a rival (design.md, Component 11 and formula 14; FR-11 items 2-4, D11). With the mode "hidden
    /// characters" off every character is visible. With it on (the default) a character is visible once the rival has started its first reaction
    /// (the record in <see cref="RivalState.revealed"/>) or while the attitude of the rival to the player is at least <c>attitude.revealAttitude</c>
    /// (+20), which shows the foundry partners, who have no hostile reaction. Pure.
    /// <para>
    /// Visibility is a question about the present, not a record: switching the mode off does not touch <c>revealed</c>, and switching it on again
    /// does not hide a character that was revealed. A character that is visible only by its attitude hides again if the attitude falls below the
    /// level.
    /// </para>
    /// </summary>
    public static class CharacterVisibility
    {
        /// <summary>The attitude from which a character is shown without a reaction (<c>attitude.revealAttitude</c> of the config).</summary>
        public const float DefaultRevealAttitude = 20f;

        /// <summary><c>IsVisible = !hiddenMode || revealed || attitude ≥ revealAttitude</c>. An attitude that is not a number counts as not reaching the level.</summary>
        /// <param name="hiddenMode">The mode "hidden characters" (<c>hiddenCharacters</c>) is on.</param>
        /// <param name="revealed">The rival has started a reaction (its <c>UniqueID</c> is in <c>revealed</c>).</param>
        /// <param name="attitude">The attitude of the rival to the player, -100..+100.</param>
        /// <param name="revealAttitude">The level from which the attitude shows the character, 20.</param>
        public static bool IsVisible(bool hiddenMode, bool revealed, float attitude, float revealAttitude)
        {
            if (!hiddenMode || revealed) return true;
            return !float.IsNaN(attitude) && attitude >= revealAttitude;
        }
    }
}
