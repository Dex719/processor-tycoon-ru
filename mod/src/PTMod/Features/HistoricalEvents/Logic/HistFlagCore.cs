using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The members of the service effect <c>HistFlag</c> (design.md, "Новые типы эффектов"): <c>entry</c> (the calendar entry whose flag is set; the entry that fired the event when the effect
    /// does not name one), <c>flag</c> (the name a <c>skipIfFlag</c> of a stage asks for) and, optionally, <c>textKey</c> (the line the effect shows in the letter, a key of this feature).
    /// </summary>
    public sealed class HistFlagParams
    {
        /// <summary>The line of the effect when the content gives none.</summary>
        public const string DefaultTextKey = "hist.fx.flag";

        public string Entry = string.Empty;
        public string Flag = string.Empty;
        public string TextKey = DefaultTextKey;

        public static HistFlagParams Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "HistFlag", problems);
            var p = new HistFlagParams();

            string entry = r.Str("entry", string.Empty);
            if (!string.IsNullOrEmpty(entry))
            {
                if (!entry.StartsWith(HistIds.Prefix, StringComparison.Ordinal) || !ForceFlawParams.IsName(entry, 100))
                    r.Fail("entry", "\"" + entry + "\" is not the id of a calendar entry (it starts with \"" + HistIds.Prefix + "\")");
                else
                    p.Entry = entry;
            }

            string flag = r.Str("flag", string.Empty);
            if (string.IsNullOrEmpty(flag)) r.Fail("flag", "is required");
            else if (!CalendarValidator.IsFlagName(flag)) r.Fail("flag", "\"" + flag + "\" is not a flag name (letters, digits, '.', '_' and '-', up to " + CalendarValidator.MaxFlagLength + ")");
            else p.Flag = flag;

            string textKey = r.Str("textKey", DefaultTextKey);
            if (!textKey.StartsWith(HistIds.Prefix, StringComparison.Ordinal) || !ForceFlawParams.IsName(textKey, 100))
                r.Fail("textKey", "\"" + textKey + "\" is not a string key of this feature (it starts with \"" + HistIds.Prefix + "\")");
            else
                p.TextKey = textKey;

            r.WarnUnknown();
            return p;
        }
    }

    /// <summary>
    /// The act of the effect <c>HistFlag</c>: puts the flag on the calendar entry through the scheduler (<c>SchedulerCore.SetFlag</c>), where a stage with the same <c>skipIfFlag</c> will find it
    /// and not play. The flag is saved with the entry. A flag on an entry the calendar does not have is refused (and said in the log), not made up.
    /// </summary>
    public static class HistFlagCore
    {
        /// <param name="p">The members of the effect.</param>
        /// <param name="args">The positional arguments of the firing: <c>[0]</c> is the entry that fired the event (<see cref="HistArgs.EntryIndex"/>).</param>
        /// <param name="setFlag">The scheduler's <c>SetFlag(entryId, flag)</c>: false for an unknown entry.</param>
        /// <param name="log">Receives a line when the flag could not be set; may be null.</param>
        /// <returns>The flag is on the entry now.</returns>
        public static bool Apply(HistFlagParams p, string[] args, Func<string, string, bool> setFlag, Action<string> log)
        {
            if (p == null || setFlag == null || string.IsNullOrEmpty(p.Flag)) return false;

            string entry = string.IsNullOrEmpty(p.Entry) ? HistArgs.At(args, HistArgs.EntryIndex) : p.Entry;
            if (string.IsNullOrEmpty(entry))
            {
                if (log != null) log("HistFlag: the flag " + p.Flag + " names no entry and the firing carries none; nothing is set");
                return false;
            }

            bool set = setFlag(entry, p.Flag);
            if (!set && log != null) log("HistFlag: the entry " + entry + " is not in the calendar; the flag " + p.Flag + " was not set");
            return set;
        }
    }
}
