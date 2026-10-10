using System.Globalization;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// Why <see cref="ModJson.TryParse"/> rejected a text, with the place of the problem. Line and column are 1-based;
    /// the column counts UTF-16 characters from the start of the line; "\n", "\r\n" and "\r" each end a line.
    /// <see cref="Offset"/> is the 0-based index of the offending character in the input string (equal to the text
    /// length when the input ended too early). Errors about the whole input (too large, null) are reported at line 1, column 1.
    /// </summary>
    public sealed class JsonError
    {
        public JsonError(string message, int line, int column, int offset)
        {
            Message = message ?? string.Empty;
            Line = line;
            Column = column;
            Offset = offset;
        }

        public string Message { get; }

        public int Line { get; }

        public int Column { get; }

        public int Offset { get; }

        /// <summary>"line 3, column 14: unexpected character '}'" - ready for the log.</summary>
        public override string ToString()
        {
            return "line " + Line.ToString(CultureInfo.InvariantCulture)
                   + ", column " + Column.ToString(CultureInfo.InvariantCulture) + ": " + Message;
        }
    }
}
