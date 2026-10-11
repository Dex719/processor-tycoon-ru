namespace PTMod.Features.EventSystem
{
    // The string values the saved data and the content use (design.md, "Data Models"). Kept as constants so no feature spells them by hand.

    /// <summary><see cref="EventInstanceDto.kind"/>.</summary>
    public static class EventInstanceKinds
    {
        /// <summary>A normal firing of a defined event.</summary>
        public const string Event = "Event";

        /// <summary>A service instance made by <c>EventEffects.ApplyDirect</c>; its <c>eventId</c> is the source id given by the caller.</summary>
        public const string Direct = "Direct";
    }

    /// <summary><see cref="EventInstanceDto.state"/>.</summary>
    public static class EventInstanceStates
    {
        public const string AwaitingChoice = "AwaitingChoice";
        public const string Resolved = "Resolved";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
    }

    /// <summary><see cref="AppliedEffect.state"/>.</summary>
    public static class AppliedEffectStates
    {
        /// <summary>Resolved, not applied yet (a variant waiting for the decision).</summary>
        public const string Pending = "Pending";

        /// <summary>Applied and running (a timed effect).</summary>
        public const string Active = "Active";

        /// <summary>Applied and finished.</summary>
        public const string Done = "Done";

        /// <summary>Not applied: the chance roll missed, or there was nothing to apply it to.</summary>
        public const string Skipped = "Skipped";

        /// <summary>Frozen with its pack or with the event system.</summary>
        public const string Suspended = "Suspended";

        /// <summary>The handler failed.</summary>
        public const string Failed = "Failed";

        /// <summary>Applied, but the value was cut by a channel limit.</summary>
        public const string Clamped = "Clamped";
    }

    /// <summary><see cref="EventLogEntry.kind"/>.</summary>
    public static class EventLogKinds
    {
        public const string Fired = "Fired";
        public const string Resolved = "Resolved";
        public const string AutoResolved = "AutoResolved";
        public const string Cancelled = "Cancelled";
        public const string EffectEnded = "EffectEnded";
        public const string EffectFailed = "EffectFailed";
    }

    /// <summary>Company keys that are not a company of the game.</summary>
    public static class EventCompanyKeys
    {
        /// <summary>The <see cref="AppliedEffect.companyKey"/> of a global-scope effect (the whole market).</summary>
        public const string Global = "global";

        /// <summary>The company key of the cooldown of an <c>AllCompanies</c> event (the event as a whole).</summary>
        public const string AllCompaniesCooldown = "*";
    }

    /// <summary>The <c>params</c> keys every effect has; the engine reads them, a handler does not.</summary>
    public static class EffectParamKeys
    {
        /// <summary><c>"Target"</c> (the target company, default) or <c>"Global"</c> (the whole market).</summary>
        public const string Scope = "scope";

        /// <summary>The chance of the effect, 0..1 (default 1), rolled once at <c>Resolve</c>.</summary>
        public const string Chance = "chance";

        /// <summary>Effects of one choice with the same group exclude each other: one of them fires, by chance.</summary>
        public const string ExclusiveGroup = "exclusiveGroup";

        /// <summary>The deviation of the effect is scaled by <see cref="PackOptions.Severity"/>.</summary>
        public const string Scalable = "scalable";
    }

    /// <summary>The values of <see cref="EffectParamKeys.Scope"/>.</summary>
    public static class EffectScopes
    {
        public const string Target = "Target";
        public const string Global = "Global";
    }
}
