using System;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Firing events from code, and the notifications about what happened to them. Subscribers are called one by one through
    /// <c>ModHooks.Raise</c>: a subscriber of a feature that is not Enabled is skipped, an exception of a subscriber is counted against its own
    /// feature and logged, and the next subscriber still runs; the effect of the choice is already applied by then. Main thread only.
    /// </summary>
    public static class EventBus
    {
        private const string FiredEvent = "EventBus.OnEventFired";
        private const string ResolvedEvent = "EventBus.OnChoiceResolved";
        private const string ExpiredEvent = "EventBus.OnEffectExpired";

        /// <summary>An event fired and its instance exists (also for events started by a chain or by another feature).</summary>
        public static event Action<EventInstance> OnEventFired;

        /// <summary>A choice was made: the instance, the id of the chosen choice, and whether it was automatic (the timer or an AI company) rather than a click.</summary>
        public static event Action<EventInstance, string, bool> OnChoiceResolved;

        /// <summary>A timed effect ended (it expired, was cancelled or was removed).</summary>
        public static event Action<AppliedEffect> OnEffectExpired;

        /// <summary>
        /// Fires the event <paramref name="eventId"/> for the target of <paramref name="context"/> (a null context means "by the definition's
        /// <c>targets</c>, now"). Respects <c>oncePerGame</c> and, unless <see cref="EventFireContext.IgnoreConditions"/>, the event's conditions. A firing without
        /// a choice and without a delay is applied before <c>Fire</c> returns. Answers <see cref="EventFireResult.Disabled"/> when the event
        /// system (or the pack of the event) is off, and never throws.
        /// </summary>
        public static EventFireResult Fire(string eventId, EventFireContext context)
        {
            if (string.IsNullOrEmpty(eventId)) return EventFireResult.UnknownEvent;
            EventFireContext ctx = context ?? new EventFireContext();
            return EventSystemApi.Run(EventSystemSite.Fire, api => api.Fire(eventId, ctx), EventFireResult.Disabled);
        }

        // ---- raised by the engine (same assembly) ----

        internal static void RaiseEventFired(EventInstance instance)
        {
            ModHooks.Raise(FiredEvent, OnEventFired, instance);
        }

        internal static void RaiseChoiceResolved(EventInstance instance, string choiceId, bool auto)
        {
            ModHooks.Raise(ResolvedEvent, OnChoiceResolved, instance, choiceId, auto);
        }

        internal static void RaiseEffectExpired(AppliedEffect effect)
        {
            ModHooks.Raise(ExpiredEvent, OnEffectExpired, effect);
        }
    }
}
