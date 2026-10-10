using System.Globalization;

namespace PTMod.Core.Logic.Companies
{
    /// <summary>
    /// Stable string keys of companies (FR-13): <c>player</c> for the player and <c>ai:&lt;UniqueID&gt;</c> for an AI
    /// company (<c>AICompany.UniqueID</c> is the index in <c>CompanySpawner.historicalCompanies</c>, so it survives save and load).
    /// The key is what the side file, the reputation store, modifier scopes and conditions use; the game's
    /// <c>ICompany</c> object never reaches the logic. A valid key matches <c>^(player|ai:\d+)$</c>, checked here by
    /// hand: ASCII digits only and no trailing line break (the regex <c>$</c> and <c>\d</c> would accept both).
    /// The logic treats keys as opaque ordinal strings; <see cref="ForAi"/> always writes the id without leading zeros.
    /// </summary>
    public static class CompanyKey
    {
        /// <summary>Key of the player's company.</summary>
        public const string Player = "player";

        /// <summary>Prefix of an AI company key.</summary>
        public const string AiPrefix = "ai:";

        /// <summary>
        /// Selector used in conditions (not a key): any AI company. Resolved by the code that evaluates a condition.
        /// </summary>
        public const string AnyAi = "anyAI";

        /// <summary>
        /// True for <c>player</c> and for <c>ai:</c> followed by one or more ASCII digits (any number of them, as the
        /// pattern says); false for null, empty, other case, spaces and everything else.
        /// </summary>
        public static bool IsValid(string key)
        {
            if (key == null) return false;
            if (string.Equals(key, Player, System.StringComparison.Ordinal)) return true;
            return IsAiKey(key);
        }

        public static bool IsPlayer(string key)
        {
            return string.Equals(key, Player, System.StringComparison.Ordinal);
        }

        /// <summary>True for <c>ai:&lt;digits&gt;</c>.</summary>
        public static bool IsAi(string key)
        {
            return key != null && IsAiKey(key);
        }

        /// <summary>The key of the AI company with this <c>UniqueID</c>; null for a negative id (an unset id).</summary>
        public static string ForAi(int uniqueId)
        {
            if (uniqueId < 0) return null;
            return AiPrefix + uniqueId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads the <c>UniqueID</c> out of an AI key. False for the player key, an invalid key and an id that does
        /// not fit in an <see cref="int"/> (such a key is valid but no game company can have it).
        /// </summary>
        public static bool TryGetAiId(string key, out int uniqueId)
        {
            uniqueId = 0;
            if (key == null || !IsAiKey(key)) return false;

            long value = 0;
            for (int i = AiPrefix.Length; i < key.Length; i++)
            {
                value = value * 10 + (key[i] - '0');
                if (value > int.MaxValue) return false;
            }
            uniqueId = (int)value;
            return true;
        }

        /// <summary>
        /// A key or a selector as it can be written in content: a valid key or <see cref="AnyAi"/>.
        /// </summary>
        public static bool IsKeyOrSelector(string text)
        {
            return IsValid(text) || string.Equals(text, AnyAi, System.StringComparison.Ordinal);
        }

        private static bool IsAiKey(string key)
        {
            if (key.Length <= AiPrefix.Length) return false;
            if (!key.StartsWith(AiPrefix, System.StringComparison.Ordinal)) return false;
            for (int i = AiPrefix.Length; i < key.Length; i++)
            {
                char c = key[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }
}
