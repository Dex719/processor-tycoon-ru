namespace PTMod.Core
{
    /// <summary>
    /// The question a feature wants the player to answer before the hub switches it off (FR-3 item 8, FR-17 item 3): what <c>FeatureContext.ConfirmBeforeDisable</c>
    /// provider returns, <c>null</c> meaning "nothing to ask, switch off". The hub shows it with <c>ModUI.Confirm</c>: the body is <see cref="MessageKey"/> filled with
    /// <see cref="Args"/>, the "Yes" button is <see cref="ConfirmLabelKey"/>, the other button is "Cancel". "Yes" switches the feature off (its <c>OnDisabled</c> runs once),
    /// "Cancel" and every other way out leave it on. Only the hub asks: a switch from code (<c>ModFeatures.SetEnabled</c>) never shows it.
    /// <para>
    /// Build a new object on every call of the provider (the numbers in it are those of the moment the player flipped the toggle); the hub reads it at once and keeps no reference.
    /// </para>
    /// </summary>
    public sealed class DisableConfirmation
    {
        /// <summary>
        /// The key of the body of the question in <see cref="ModL10n"/> (<c>{0}</c>, <c>{1}</c>... are <see cref="Args"/>). An empty key, or one no table knows, makes the hub
        /// ask its own question "Turn off &lt;feature name&gt;?" instead (and the log says why), so a slip here never blocks the switch.
        /// </summary>
        public string MessageKey;

        /// <summary>The arguments of the message, already formatted for the player (money with the game's formatter, dates with the game's formatter); null for none.</summary>
        public string[] Args;

        /// <summary>The key of the caption of the "Yes" button (for example "Turn off and settle"); null or empty gives <c>core.common.yes</c>.</summary>
        public string ConfirmLabelKey;

        public override string ToString()
        {
            return "DisableConfirmation(" + (string.IsNullOrEmpty(MessageKey) ? "no message" : MessageKey) + ", " + (Args == null ? 0 : Args.Length) + " arg(s))";
        }
    }
}
