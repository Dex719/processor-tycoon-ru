using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Registers the predicates <c>hist.*</c> (design.md, Component 5; TSK-005) with <c>EventConditions.Register</c>, the facade of the event system
    /// over <c>ModConditions</c>. The names carry the prefix of the feature (<c>hist.</c>), which <c>ModConditions.Register</c> demands; a predicate is false
    /// while its feature is not active. The function is <see cref="HistPredicates.Evaluate"/>, the one the tests call; the roles of the entry being judged
    /// come from <see cref="HistRoleScope.Current"/>. Call it from <c>Register</c>, before the calendar is read: the parser refuses an entry that names
    /// a predicate it does not know.
    /// </summary>
    internal static class HistConditions
    {
        /// <summary>Registers the five predicates. A name that is taken or refused is logged by the core; the rest still register.</summary>
        internal static void Register()
        {
            foreach (string name in HistPredicates.Names)
            {
                string type = name;
                EventConditions.Register(type, HistIds.Feature, (args, ctx) => HistPredicates.Evaluate(type, args, ctx, HistRoleScope.Current));
            }
        }
    }
}
