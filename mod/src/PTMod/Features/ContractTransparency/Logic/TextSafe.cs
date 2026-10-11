using System.Text;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The care the feature takes of text that comes from the game or from a save (NFR-7): names of clients and CPUs are cut to <see cref="MaxLength"/> characters
    /// before they are stored, and the angle brackets are taken out before they are put into a text the game draws with TextMeshPro markup (a CPU named
    /// <c>&lt;size=500&gt;</c> must not become a tag). Pure, no allocation for text that needs nothing.
    /// </summary>
    public static class TextSafe
    {
        /// <summary>The longest string the log keeps (NFR-7).</summary>
        public const int MaxLength = 128;

        /// <summary>What an opening angle bracket becomes in text for the screen: a look-alike that is not markup.</summary>
        public const char OpenReplacement = '‹';

        /// <summary>What a closing angle bracket becomes in text for the screen.</summary>
        public const char CloseReplacement = '›';

        /// <summary>
        /// The text as the log stores it: never null, control characters (line breaks, tabs) replaced by a space, at most <see cref="MaxLength"/> characters
        /// (a surrogate pair is never cut in two).
        /// </summary>
        public static string Clean(string text)
        {
            return Clean(text, MaxLength);
        }

        /// <summary><see cref="Clean(string)"/> with another limit (a limit below 0 counts as 0).</summary>
        public static string Clean(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (maxLength < 0) maxLength = 0;

            bool dirty = text.Length > maxLength;
            for (int i = 0; !dirty && i < text.Length; i++)
            {
                if (text[i] < ' ') dirty = true;
            }
            if (!dirty) return text;

            int length = text.Length > maxLength ? maxLength : text.Length;
            if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;

            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = text[i];
                sb.Append(c < ' ' ? ' ' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The text for a label of the game's UI: <c>&lt;</c> and <c>&gt;</c> are replaced by <see cref="OpenReplacement"/> and <see cref="CloseReplacement"/>,
        /// so nothing in it can open a TextMeshPro tag. Null gives an empty string.
        /// </summary>
        public static string EscapeMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('<') < 0 && text.IndexOf('>') < 0) return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<') sb.Append(OpenReplacement);
                else if (c == '>') sb.Append(CloseReplacement);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
