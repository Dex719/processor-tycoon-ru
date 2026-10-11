using System.Globalization;
using System.Text;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The TextMeshPro tags a line of a panel is wrapped in: an indent, bold, the colour of its tone and the dimming of a muted line. Pure string work, so the tags can be checked: they are always closed in
    /// the reverse order of their opening (an open tag would colour the rest of the window), and a text with no look gets no tag at all.
    /// </summary>
    public static class PanelMarkup
    {
        /// <summary>The alpha of a muted line, <c>#A0</c> of <c>#FF</c>: the colour of the theme, dimmed (a muted line needs no colour of its own, so every theme gets a readable one).</summary>
        public const string MutedAlpha = "#A0";

        /// <summary>One level of indent, in letter widths (<c>em</c>).</summary>
        public const float IndentStep = 1.5f;

        /// <summary>
        /// <paramref name="text"/> wrapped for the look it asks for. <paramref name="colorHex"/> is the colour of the tone (<c>#RRGGBB</c>) or null for the colour of the theme;
        /// <paramref name="muted"/> dims the text; <paramref name="bold"/> makes it bold; <paramref name="indent"/> pushes it in by that many levels. An empty text stays empty.
        /// </summary>
        public static string Wrap(string text, string colorHex, bool muted, bool bold, int indent)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            bool colored = !string.IsNullOrEmpty(colorHex);
            if (!colored && !muted && !bold && indent <= 0) return text;

            var sb = new StringBuilder(text.Length + 48);
            if (indent > 0) sb.Append("<indent=").Append((indent * IndentStep).ToString("0.#", CultureInfo.InvariantCulture)).Append("em>");
            if (bold) sb.Append("<b>");
            if (colored) sb.Append("<color=").Append(colorHex).Append('>');
            if (muted) sb.Append("<alpha=").Append(MutedAlpha).Append('>');
            sb.Append(text);
            if (muted) sb.Append("<alpha=#FF>");
            if (colored) sb.Append("</color>");
            if (bold) sb.Append("</b>");
            return sb.ToString();
        }
    }
}
