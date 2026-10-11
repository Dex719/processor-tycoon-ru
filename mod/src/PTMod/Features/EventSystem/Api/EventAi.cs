using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Decides for an AI company which choice of an event it takes. Registered with <see cref="EventAi.AddChooser"/>; the engine asks the choosers in
    /// the order of descending priority and the first non-null answer wins.
    /// </summary>
    public interface IEventAiChooser
    {
        /// <summary>
        /// The id of the choice the company takes, or null: "not my event / I do not decide" - the next chooser, and at the end the default policy,
        /// decides. An answer that names an unavailable or unknown choice counts as null. An exception counts as null too and is counted against the
        /// feature of the chooser. <paramref name="rng"/> is the generator derived for this decision (always the same count of draws, D5).
        /// </summary>
        string Choose(EventInstance instance, ChoiceOption[] options, ICompany company, IModRng rng);
    }

    /// <summary>Optional, implemented next to <see cref="IEventAiChooser"/>: lets the engine skip a chooser quickly for events that are not its own.</summary>
    public interface IEventAiChooserFilter
    {
        /// <summary>False - the chooser does not handle this event, <see cref="IEventAiChooser.Choose"/> is not called.</summary>
        bool Handles(EventInstance instance);
    }

    /// <summary>
    /// The chain of AI choosers. The list is ordered by descending priority (a larger number goes first), equal priorities keep the order of
    /// registration. The default policy (<c>aiWeight</c> among the available choices, with <c>aiMinCashMultiple</c>) works when every chooser answers null.
    /// Usual priorities: 100 - rival-personalities, 10 - historical-events. Works while the event system is off too: the registration is kept.
    /// </summary>
    public static class EventAi
    {
        /// <summary>Adds a chooser to the chain. A null chooser is ignored with a warning.</summary>
        public static void AddChooser(IEventAiChooser chooser, int priority)
        {
            if (chooser == null)
            {
                ModLog.Warn(EventSystemApi.FeatureId, "api: EventAi.AddChooser ignored, the chooser is null");
                return;
            }

            EventSystemApi.Register(api => api.AddChooser(chooser, priority));
        }

        /// <summary>Removes a chooser from the chain; a chooser that is not there is ignored.</summary>
        public static void RemoveChooser(IEventAiChooser chooser)
        {
            if (chooser == null) return;
            EventSystemApi.Register(api => api.RemoveChooser(chooser));
        }
    }
}
