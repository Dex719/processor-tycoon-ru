namespace PTMod.Core
{
    /// <summary>
    /// What a provider returns when it wants to explain its multiplier (FR-12 item 14): the multiplier and, for
    /// <c>Explain</c>, a ModL10n key with arguments. A plain <see cref="float"/> converts implicitly and means
    /// "multiplier without a reason". Create it with the constructor or the conversion: a <c>default</c> value has
    /// the multiplier 0, not 1.
    /// </summary>
    public struct ProviderResult
    {
        public float Multiplier;

        /// <summary>ModL10n key of the reason (for example "press.mod.hype"), or null. Overrides the key given at registration.</summary>
        public string ReasonKey;

        /// <summary>Arguments {0}, {1}... of <see cref="ReasonKey"/>, or null.</summary>
        public string[] ReasonArgs;

        public ProviderResult(float multiplier, string reasonKey = null, string[] reasonArgs = null)
        {
            Multiplier = multiplier;
            ReasonKey = reasonKey;
            ReasonArgs = reasonArgs;
        }

        public static implicit operator ProviderResult(float multiplier)
        {
            return new ProviderResult(multiplier);
        }
    }
}
