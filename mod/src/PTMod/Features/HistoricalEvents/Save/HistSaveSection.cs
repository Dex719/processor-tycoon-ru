using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The holder of the section "historical-events" of the side save file (FR-18; design.md, Component 11), which the feature registers with
    /// <c>ctx.SaveParticipant</c>. Everything it does is <see cref="HistSaveSectionBase"/> (the part the tests compile); this class only adds the
    /// core's interface and the log. <c>SectionId</c> is <c>historical-events</c>, <c>SchemaVersion</c> is 1, <c>Write()</c> returns the data without
    /// <c>_schema</c> (the core writes the version), and the data has no version field.
    /// </summary>
    public sealed class HistSaveSection : HistSaveSectionBase, IModSaveParticipant
    {
        public HistSaveSection(Func<IReadOnlyList<CalendarEntry>> calendar, Func<int?> today)
            : base(calendar, today, message => ModLog.Warn(HistIds.Feature, message))
        {
        }
    }
}
