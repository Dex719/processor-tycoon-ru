using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// A challenge: a rule that makes a game harder in return for a bonus to the score (design.md, "Контент: испытания"; FR-11). Standard
    /// channels of <c>ModModifiers</c> plus fail rules; no new channels. Built from a record of <c>challenges.json</c> by <see cref="ContentMapper"/>.
    /// </summary>
    public sealed class ChallengeDefinition
    {
        /// <summary><c>[a-z0-9-]{3,40}</c>, unique; the key of an override in <c>challenges.json</c>.</summary>
        public string Id;

        public string TitleKey, DescKey;

        /// <summary>Bonus to the score of a scenario, 0..1 (0.15 = +15%); the bonuses of the chosen challenges add up.</summary>
        public float ScoreBonus;

        /// <summary>Modifiers added once when the run starts; removed again when the challenge fails in free play.</summary>
        public ModifierSpec[] Modifiers = new ModifierSpec[0];

        /// <summary>Rules that fail the challenge.</summary>
        public FailSpec[] Fail = new FailSpec[0];

        /// <summary>Company types the challenge can be chosen for (<c>idm</c>, <c>fabless</c>, <c>foundry</c>); empty = any.</summary>
        public string[] RequiresCompanyType = new string[0];

        /// <summary>
        /// The challenge needs the per-company default threshold of the player (<c>ModCredit.IsPerCompanyActive</c>): without it the channel
        /// <c>bank.creditLimit</c> would only act globally, so the challenge cannot be chosen and <see cref="UnavailableKey"/> says why.
        /// </summary>
        public bool RequiresPerCompanyCredit;

        /// <summary>The L10n key of the reason the challenge is unavailable (by convention <c>chl.&lt;id&gt;.unavailable</c>).</summary>
        public string UnavailableKey
        {
            get { return "chl." + Id + ".unavailable"; }
        }

        /// <summary>Computed on load (first 8 hex digits of the SHA-1 of the canonical JSON of the record), never written in the file.</summary>
        public string ContentHash;

        /// <summary>The record differs from the built-in one with the same id, or there is no built-in one. Computed on load.</summary>
        public bool IsCustom;

        /// <summary>A copy of the JSON record this definition was mapped from; null for a definition built by hand.</summary>
        public JsonObject Source;
    }
}
