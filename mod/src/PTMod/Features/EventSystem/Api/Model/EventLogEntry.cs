using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>One line of the event journal (<c>EventLog.Entries</c>, the section "event-system" of the side save file).</summary>
    public sealed class EventLogEntry
    {
        /// <summary>Game day (days from 1971-01-01).</summary>
        public int day;

        public int instanceId;
        public string eventId;
        public string companyKey;

        /// <summary>One of <see cref="EventLogKinds"/>.</summary>
        public string kind;

        public string choiceId;
        public List<EffectRecord> effects = new List<EffectRecord>();
    }

    /// <summary>What an effect did, as the journal keeps it.</summary>
    public sealed class EffectRecord
    {
        public string type;
        public string channel;
        public float value;
        public int durationDays;
        public bool hit;

        /// <summary>The value was cut by a channel limit.</summary>
        public bool clamped;

        /// <summary>
        /// The L10n key and the arguments of the effect's line, copied from the <see cref="AppliedEffect"/> when the entry was made: the journal builds its text
        /// from them, so a change of language redraws the line and the numbers stay the ones that were shown and applied. Empty in entries of older saves.
        /// </summary>
        public string textKey;
        public string[] textArgs = new string[0];
    }
}
