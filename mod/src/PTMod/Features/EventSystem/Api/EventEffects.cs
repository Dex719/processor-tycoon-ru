using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The table of effect types. The engine registers its standard types (<c>Money</c>, <c>ModifierTimed</c>, <c>FactoryOutput</c>, <c>MarketDemand</c>,
    /// <c>Popularity</c>, <c>Reputation</c>, <c>ResearchProgress</c>, <c>SendMail</c>, <c>FireEvent</c>) the same way a pack does its own types in
    /// <c>IModFeature.Register</c>. A type works in every pack. Works while the event system is off too: the registration is kept.
    /// </summary>
    public static class EventEffects
    {
        /// <summary>
        /// Registers the handler of an effect type. A second registration of the same type keeps the first handler and logs a warning.
        /// The type name is the <c>type</c> of an effect in JSON.
        /// </summary>
        public static void Register(string type, IEventEffectHandler handler)
        {
            if (string.IsNullOrEmpty(type) || handler == null)
            {
                ModLog.Warn(EventSystemApi.FeatureId, "api: EventEffects.Register ignored, the type or the handler is missing");
                return;
            }

            EventSystemApi.Register(api => api.RegisterEffectHandler(type, handler));
        }

        /// <summary>The handler of an effect type, or null for an unknown type.</summary>
        public static IEventEffectHandler Get(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            return EventSystemApi.Query(EventSystemSite.Handler, api => api.GetEffectHandler(type), (IEventEffectHandler)null);
        }

        /// <summary>The registered effect types in the order of registration.</summary>
        public static IList<string> RegisteredTypes
        {
            get
            {
                IList<string> types = EventSystemApi.Query(EventSystemSite.Types, api => api.GetEffectTypes(), (IList<string>)null);
                return types ?? new string[0];
            }
        }

        /// <summary>
        /// Applies an effect outside an event (the stages of a scenario without a choice, the decisions of an AI company in another feature). A service
        /// instance of kind <c>Direct</c> is created, with <paramref name="sourceId"/> as its <c>eventId</c>: the effect is saved, expires and appears in the
        /// journal like any other. <paramref name="args"/> are the positional arguments of its texts. Returns the effect with its final numbers,
        /// or null when the event system is off or the effect could not be resolved.
        /// </summary>
        public static AppliedEffect ApplyDirect(EventEffect spec, string companyKey, string sourceId, string[] args)
        {
            if (spec == null || string.IsNullOrEmpty(companyKey)) return null;
            return EventSystemApi.Run(EventSystemSite.ApplyDirect, api => api.ApplyDirect(spec, companyKey, sourceId, args), (AppliedEffect)null);
        }
    }
}
