using System.Collections.Generic;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The extension point for effect types (<c>EventEffects.Register</c>). The engine calls a handler in this order for every effect of a
    /// firing: <see cref="Validate"/> when content is loaded, <see cref="Resolve"/> once when the instance is created (for every choice, so the
    /// player sees the exact numbers before deciding), <see cref="Apply"/> when the effect takes place, <see cref="Describe"/> whenever a text is built.
    /// Every call is made inside a try/catch: an exception marks the effect Failed and the day goes on. A handler keeps no state of its own that a
    /// save must carry - the numbers live in the <see cref="AppliedEffect"/> (<c>value</c>, <c>curve</c>, <c>textArgs</c>).
    /// </summary>
    public interface IEventEffectHandler
    {
        /// <summary>The type name as it is written in the <c>type</c> of an effect in JSON.</summary>
        string Type { get; }

        /// <summary>Checks the parameters of an effect when content is loaded; a message in <paramref name="errors"/> rejects the event. True - valid.</summary>
        bool Validate(EventEffect spec, IList<string> errors);

        /// <summary>All the rolls and the formulas, done once: the returned effect carries the final numbers. Does not change the game.</summary>
        AppliedEffect Resolve(EventEffect spec, EventContext ctx);

        /// <summary>The one-off action, or the start of a timed effect. Takes the numbers from <paramref name="effect"/> and recomputes nothing.</summary>
        void Apply(AppliedEffect effect, EventContext ctx);

        /// <summary>The L10n key and the arguments of the effect's line, from the numbers saved in <paramref name="effect"/> only.</summary>
        EffectText Describe(AppliedEffect effect);
    }

    /// <summary>A handler of an effect that lasts: the engine ends it, freezes it and brings it back.</summary>
    public interface ITimedEffectHandler : IEventEffectHandler
    {
        /// <summary>Every game day while the effect is active (curves change their contribution here).</summary>
        void Tick(AppliedEffect effect, EventContext ctx);

        /// <summary>The effect ends: it expired, was cancelled, or its pack was removed. Undoes what <see cref="IEventEffectHandler.Apply"/> did.</summary>
        void Remove(AppliedEffect effect, EventContext ctx);

        /// <summary>After a load, or when the pack or the event system is switched on again: puts the effect back into the game from the saved numbers.</summary>
        void Restore(AppliedEffect effect, EventContext ctx);
    }

    /// <summary>Optional: a handler whose value changes over time reports its current value for the "Active effects" tab.</summary>
    public interface ICurveEffectHandler
    {
        /// <summary>The current value of the effect on <paramref name="today"/> (days from 1971-01-01), from its saved curve.</summary>
        float ValueAt(AppliedEffect effect, int today);
    }
}
