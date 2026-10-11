using System;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The runtime wrapper of a saved <see cref="EventInstanceDto"/>: the data and the definition it came from. What <c>EventBus</c> events and
    /// <c>IEventAiChooser</c> hand out; a feature reads it and never changes it.
    /// </summary>
    public sealed class EventInstance
    {
        public EventInstanceDto Data;

        /// <summary>The definition of <c>Data.eventId</c>; null for a <c>Direct</c> instance or when the event is no longer in the registry.</summary>
        public EventDefinition Definition;

        public EventInstance()
        {
        }

        public EventInstance(EventInstanceDto data, EventDefinition definition)
        {
            Data = data;
            Definition = definition;
        }

        /// <summary>The instance belongs to the player's company.</summary>
        public bool IsPlayer
        {
            get { return Data != null && string.Equals(Data.companyKey, CompanyKey.Player, StringComparison.Ordinal); }
        }

        /// <summary>
        /// Days left to decide on <paramref name="today"/> (days from 1971-01-01): <c>deadlineDay - today</c>, never below 0; 0 also when there is
        /// nothing to decide (<c>deadlineDay = 0</c>).
        /// </summary>
        public int DaysLeft(int today)
        {
            if (Data == null || Data.deadlineDay <= 0) return 0;
            int left = Data.deadlineDay - today;
            return left > 0 ? left : 0;
        }
    }
}
