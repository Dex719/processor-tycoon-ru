namespace PTMod.Features.EventSystem
{
    /// <summary>The shape of the names that content uses as keys: event ids, categories, choice ids, exclusive groups. Saved data and logs carry them as they are.</summary>
    internal static class ContentNames
    {
        /// <summary>The longest event id.</summary>
        public const int MaxIdLength = 80;

        /// <summary>The longest category, choice id and exclusive group.</summary>
        public const int MaxShortLength = 40;

        /// <summary>The id of the effects shared by every choice in <c>AppliedEffect.id</c> (<c>&lt;instance&gt;/common/&lt;index&gt;</c>); a choice cannot take it.</summary>
        public const string CommonChoice = "common";

        /// <summary>Letters, digits, '_', '-' and '.', not empty, at most <paramref name="maxLength"/> characters, not starting or ending with a dot.</summary>
        public static bool IsName(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length > maxLength) return false;
            if (text[0] == '.' || text[text.Length - 1] == '.') return false;
            foreach (char c in text)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>A choice id: no dots (it sits between slashes in <c>AppliedEffect.id</c>), at most <see cref="MaxShortLength"/> characters.</summary>
        public static bool IsChoiceId(string text)
        {
            return IsName(text, MaxShortLength) && text.IndexOf('.') < 0;
        }
    }
}
