using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The answer of the sources of lab occupancy: is the lab busy with the research of another feature, and what to say about it.</summary>
    public struct LabState
    {
        /// <summary>Some source says the lab is busy (the lab does not stand idle, although <c>ResearchSector.CurrentResearch()</c> is null).</summary>
        public bool Busy;

        /// <summary>The text of the first busy source, already localized; null when the lab is free, when the text was not asked or the source gave none.</summary>
        public string Text;

        public static readonly LabState Free = new LabState();
    }

    /// <summary>
    /// The poll of the sources of the "lab is busy" state (FR-7 item 7, decision D10, AC-2.4 and AC-9.2): the research list asks for the text, the
    /// reminder of an idle lab only for the yes or no. The sources are asked in order; the first one that says "busy" decides, and its text is
    /// asked only then (and only when the caller wants it). A source that throws is switched off by <see cref="ProviderCalls"/> and the rest are
    /// still asked, so one broken source never hides the state of the others. A source of a feature that is not active is not asked.
    /// </summary>
    public static class LabOccupancy
    {
        /// <summary>
        /// Asks the sources. <paramref name="busyText"/> may be null when only the yes or no is needed. <paramref name="ownerActive"/> may be null (all
        /// owners count as active). <paramref name="report"/> receives the failures (owner, site, exception).
        /// </summary>
        public static LabState Poll<T>(ProviderBook<T> book, Func<string, bool> ownerActive, Func<T, bool> isBusy, Func<T, string> busyText,
            Action<string, string, Exception> report) where T : class
        {
            if (book == null || isBusy == null) return LabState.Free;

            var entries = new List<ProviderEntry<T>>();
            book.Collect(ownerActive, entries);
            for (int i = 0; i < entries.Count; i++)
            {
                ProviderEntry<T> entry = entries[i];
                string name = entry.Provider.GetType().Name;

                bool busy = ProviderCalls.Run(book, entry, "ILabOccupancySource.IsLabBusy " + name, isBusy, false, report);
                if (!busy) continue;

                string text = null;
                if (busyText != null)
                {
                    text = ProviderCalls.Run(book, entry, "ILabOccupancySource.BusyText " + name, busyText, (string)null, report);
                    if (text != null && text.Trim().Length == 0) text = null;
                }
                return new LabState { Busy = true, Text = text };
            }
            return LabState.Free;
        }
    }
}
