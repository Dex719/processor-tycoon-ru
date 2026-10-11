namespace PTMod.Features.EventSystem
{
    /// <summary>What <c>EventBus.Fire</c> was asked to do and with what: the target, the delay and the arguments of the texts.</summary>
    public sealed class EventFireContext
    {
        /// <summary><c>player</c> or <c>ai:&lt;UniqueID&gt;</c>; null - choose the target by the <c>targets</c> of the definition.</summary>
        public string CompanyKey;

        /// <summary>Fire after this many game days (the result is then <see cref="EventFireResult.Scheduled"/>); 0 - now.</summary>
        public int DelayDays;

        /// <summary>The instance this firing continues (a chain); 0 for none.</summary>
        public int ParentInstanceId;

        /// <summary>Skip the <c>conditions</c> of the definition (the caller has already decided that the event happens).</summary>
        public bool IgnoreConditions;

        /// <summary>Positional arguments of the texts of the event (<c>{0}</c>, <c>{1}</c>...).</summary>
        public string[] Args;
    }

    /// <summary>The answer of <c>EventBus.Fire</c>.</summary>
    public enum EventFireResult
    {
        /// <summary>The event fired and has an instance now.</summary>
        Fired,

        /// <summary>The firing is queued for a later day (<see cref="EventFireContext.DelayDays"/> or a limit on pending choices).</summary>
        Scheduled,

        /// <summary>Nothing happened: <c>oncePerGame</c> used, the conditions are false, the target does not exist or is bankrupt.</summary>
        Skipped,

        /// <summary>No event with this id in the registry.</summary>
        UnknownEvent,

        /// <summary>The event system, or the pack of the event, is switched off.</summary>
        Disabled,
    }
}
