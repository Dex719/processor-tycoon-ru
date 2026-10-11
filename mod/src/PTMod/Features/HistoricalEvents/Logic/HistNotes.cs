using System;
using System.Collections.Generic;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The two notes every event of the pack carries besides the numbers of its arguments (FR-14; design.md, "Письма и окна"): the line "Источник: домен" that
    /// ends the <c>text</c> of the event, and the reference "как это было" that is its <c>cause</c>, which the setting <c>historyNotes</c> may switch off.
    /// <para>
    /// The engine fills the texts of an event with the positional arguments of its instance (<c>{0}</c>, <c>{1}</c>, ...) and nothing else, so both notes travel as
    /// arguments, in two slots after the arguments of the table of <see cref="DispatcherCore.BuildArgs"/> (<c>[0]</c> the id of the entry, <c>[1]</c> the company,
    /// <c>[2]</c> and <c>[3]</c> the second company, <c>[4..]</c> the extra arguments, at most <see cref="MaxExtraArgs"/>): the text of every event ends with
    /// <c>{<see cref="SourceSlot"/>}</c> and its cause is the key <c>hist.cause</c>, which is <c>{<see cref="NotesSlot"/>}</c>. <see cref="Decorate"/> fills the slots and pads
    /// the arguments to <see cref="Length"/>; the one door every firing of the feature goes through (<c>EventBusFirer</c>) calls it, so no event of the feature reaches the engine
    /// without them (a text with a placeholder that has no argument is shown unformatted by <c>ModL10n</c>).
    /// </para>
    /// <para>
    /// The slot of the source holds the whole line with its line break (<c>"\nИсточник: wikipedia.org"</c>), or nothing: an event that belongs to no entry has no source.
    /// The slot of the cause holds the text of the fact of the entry (<c>history.factKey</c>) in the language of the moment of the firing, or nothing when the notes are off.
    /// </para>
    /// </summary>
    public static class HistNotes
    {
        /// <summary>The first of the extra arguments of a firing (<c>FireSpec.ExtraArgs</c>).</summary>
        public const int FirstExtra = HistArgs.FirstExtraIndex;

        /// <summary>How many extra arguments a firing may have; the validator refuses more (they would run into the slots of the notes).</summary>
        public const int MaxExtraArgs = 2;

        /// <summary>The position of the line with the source, last line of the <c>text</c> of an event.</summary>
        public const int SourceSlot = FirstExtra + MaxExtraArgs;

        /// <summary>The position of the text of the cause of an event ("как это было").</summary>
        public const int NotesSlot = SourceSlot + 1;

        /// <summary>The length of the arguments of every event of the feature.</summary>
        public const int Length = NotesSlot + 1;

        /// <summary>The key of the line with the source: <c>Источник: {0}</c>, the domain as <c>{0}</c>.</summary>
        public const string SourceKey = "hist.mail.source";

        /// <summary>The key every cause of the pack's events uses: its text is the placeholder of <see cref="NotesSlot"/>.</summary>
        public const string CauseKey = "hist.cause";

        private static readonly string[] SecondLevelLabels = { "co", "com", "org", "net", "gov", "edu", "ac" };

        /// <summary>
        /// The id of the calendar entry an event belongs to, or null: the news that an entry did not happen carries it second (<c>[title, id]</c>), every other event
        /// first.
        /// </summary>
        public static string EntryIdOf(string eventId, string[] args)
        {
            if (args == null) return null;

            int index = string.Equals(eventId, DispatcherCore.FizzleEventId, StringComparison.Ordinal) ? 1 : 0;
            if (index >= args.Length || string.IsNullOrEmpty(args[index])) return null;
            return args[index];
        }

        /// <summary>
        /// The domain a source is shown by: the host of the URL without <c>www.</c> and without the name of a language or a server in front of the registered domain
        /// (<c>https://en.wikipedia.org/wiki/X</c> gives <c>wikipedia.org</c>, <c>https://www.csis.org/</c> gives <c>csis.org</c>, <c>https://www.bbc.co.uk/</c> gives
        /// <c>bbc.co.uk</c>). Empty for anything that is not a URL with a host.
        /// </summary>
        public static string DomainOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            string rest = url.Trim();
            int scheme = rest.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) rest = rest.Substring(scheme + 3);

            int end = rest.IndexOfAny(new[] { '/', '?', '#' });
            string authority = end < 0 ? rest : rest.Substring(0, end);

            int at = authority.LastIndexOf('@');
            if (at >= 0) authority = authority.Substring(at + 1);

            int colon = authority.IndexOf(':');
            if (colon >= 0) authority = authority.Substring(0, colon);

            string host = authority.Trim().Trim('.').ToLowerInvariant();
            foreach (char ch in host)
            {
                if (char.IsWhiteSpace(ch) || ch == '"' || ch == '<' || ch == '>') return string.Empty;
            }
            if (host.Length == 0 || host.IndexOf('.') < 0) return host;

            string[] labels = host.Split('.');
            if (labels.Length <= 2) return host;

            string last = labels[labels.Length - 1];
            string second = labels[labels.Length - 2];
            bool compound = last.Length == 2 && Array.IndexOf(SecondLevelLabels, second) >= 0;
            int keep = compound ? 3 : 2;
            if (labels.Length <= keep) return host;

            return string.Join(".", labels, labels.Length - keep, keep);
        }

        /// <summary>The domain of the first source of the history that has one; empty when none does.</summary>
        public static string SourceDomain(HistoryInfo info)
        {
            if (info == null || info.Sources == null) return string.Empty;

            foreach (string source in info.Sources)
            {
                string domain = DomainOf(source);
                if (domain.Length > 0) return domain;
            }
            return string.Empty;
        }

        /// <summary>
        /// The arguments of a firing with the notes in their slots. The result is a new array of at least <see cref="Length"/> elements (never null elements); the
        /// slots of the notes are overwritten, so a firing that repeats the arguments of an earlier one (the replacement event of a flaw) is decorated afresh.
        /// </summary>
        /// <param name="eventId">The event that is fired.</param>
        /// <param name="args">The arguments of the table; may be null.</param>
        /// <param name="historyOf">The history of an id: an event with a reference of its own (the news about the game's own events) or a calendar entry; null for an id it does not know.</param>
        /// <param name="notes">The setting <c>historyNotes</c>: when false the cause is empty.</param>
        /// <param name="text">The text of a key with its arguments in the current language (<c>ModL10n.L</c>).</param>
        public static string[] Decorate(string eventId, string[] args, Func<string, HistoryInfo> historyOf, bool notes, Func<string, string[], string> text)
        {
            int length = Math.Max(args == null ? 0 : args.Length, Length);
            var result = new string[length];
            for (int i = 0; i < length; i++) result[i] = args != null && i < args.Length && args[i] != null ? args[i] : string.Empty;
            result[SourceSlot] = string.Empty;
            result[NotesSlot] = string.Empty;
            if (historyOf == null || text == null) return result;

            // The entry the arguments name first (the pack's events are shared by entries), then the event itself (news about the game's own events have a history of their own).
            string entryId = EntryIdOf(eventId, result);
            HistoryInfo info = entryId == null ? null : historyOf(entryId);
            if (info == null && !string.IsNullOrEmpty(eventId)) info = historyOf(eventId);
            if (info == null) return result;

            string domain = SourceDomain(info);
            if (domain.Length > 0) result[SourceSlot] = "\n" + text(SourceKey, new[] { domain });
            if (notes && !string.IsNullOrEmpty(info.FactKey)) result[NotesSlot] = text(info.FactKey, new string[0]);
            return result;
        }

        /// <summary>The last line of a resolved text, for the checks of the shipped content: the text after its last line break.</summary>
        public static string LastLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string trimmed = text.TrimEnd('\n', '\r', ' ');
            int cut = trimmed.LastIndexOf('\n');
            return cut < 0 ? trimmed : trimmed.Substring(cut + 1);
        }

        /// <summary>The slots a text or a cause names with a placeholder: the numbers after an opening brace, for the checks of the shipped content.</summary>
        public static IReadOnlyList<int> PlaceholdersOf(string text)
        {
            var found = new List<int>();
            if (string.IsNullOrEmpty(text)) return found;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '{') continue;

                int j = i + 1;
                int value = 0;
                bool digits = false;
                while (j < text.Length && text[j] >= '0' && text[j] <= '9' && value < 10000)
                {
                    value = value * 10 + (text[j] - '0');
                    digits = true;
                    j++;
                }
                if (digits && j < text.Length && (text[j] == '}' || text[j] == ',' || text[j] == ':')) found.Add(value);
            }
            return found;
        }
    }
}
