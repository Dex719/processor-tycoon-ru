using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>The shape of <c>ModConditions.Register</c> (and of <c>PredicateRegistry.Register</c> in tests): the type, the owner feature, the factory of the node.</summary>
    public delegate bool PredicateRegistrar(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory);

    /// <summary>
    /// The predicates of the engine (FR-6, design.md "Условия"), registered in the shared catalog of <c>ModConditions</c> under the owner <c>event-system</c> with
    /// the prefix <c>es.</c> (D12). The subject of every one is the company the condition is evaluated for (the target of the event):
    /// <list type="table">
    /// <item><c>es.eventFired</c> <c>eventId</c>, <c>withinDays</c>? - the event fired for the subject (in the last N days).</item>
    /// <item><c>es.choiceMade</c> <c>eventId</c>, <c>choiceId</c> - the subject chose that choice.</item>
    /// <item><c>es.eventFlag</c> <c>flag</c> - the subject has the flag of a chain.</item>
    /// <item><c>es.activeInCategory</c> <c>category</c>, <c>scope</c> = <c>subject</c> (default) | <c>any</c> - a pending choice or a running effect of an
    /// event of the category for the subject; with <c>any</c> for any company or a global effect (AC-6.5).</item>
    /// <item><c>es.hasRevenueHistory</c> - three monthly reports and a positive quarterly revenue.</item>
    /// <item><c>es.hasFab</c> - a factory with at least one line, not fabless.</item>
    /// </list>
    /// The parameters are checked when the condition is parsed: a missing or unknown parameter is an error of the content record (the record is skipped).
    /// </summary>
    public static class BuiltinPredicates
    {
        /// <summary>The owner feature of the predicates.</summary>
        public const string Owner = "event-system";

        public const string EventFired = "es.eventFired";
        public const string ChoiceMade = "es.choiceMade";
        public const string EventFlag = "es.eventFlag";
        public const string ActiveInCategory = "es.activeInCategory";
        public const string HasRevenueHistory = "es.hasRevenueHistory";
        public const string HasFab = "es.hasFab";

        /// <summary>The values of <c>scope</c> of <c>es.activeInCategory</c>.</summary>
        public const string ScopeSubject = "subject";
        public const string ScopeAny = "any";

        /// <summary>The longest <c>withinDays</c> accepted (about 270 years).</summary>
        public const int MaxWithinDays = 100000;

        /// <summary>Every predicate of the engine, in the order of registration.</summary>
        public static readonly string[] Names = { EventFired, ChoiceMade, EventFlag, ActiveInCategory, HasRevenueHistory, HasFab };

        /// <summary>
        /// Registers the six predicates through <paramref name="register"/> (<c>ModConditions.Register</c>). Returns how many were accepted; a refused name
        /// is reported by the registry itself.
        /// </summary>
        public static int Register(PredicateRegistrar register, IEventHistory history, IEventWorld world)
        {
            if (register == null) throw new ArgumentNullException("register");
            if (history == null) throw new ArgumentNullException("history");
            if (world == null) throw new ArgumentNullException("world");

            int accepted = 0;

            if (register(EventFired, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    string eventId = args.RequireString("eventId");
                    int within = args.OptionalInt("withinDays", 0, 1, MaxWithinDays);
                    if (!args.Finish()) return null;
                    return new DelegateCondition(args.Describe(EventFired),
                        ctx => history.HasFired(eventId, ctx.SubjectKey, within, TodayOf(ctx, world)));
                })) accepted++;

            if (register(ChoiceMade, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    string eventId = args.RequireString("eventId");
                    string choiceId = args.RequireString("choiceId");
                    if (!args.Finish()) return null;
                    return new DelegateCondition(args.Describe(ChoiceMade), ctx => history.HasChosen(eventId, choiceId, ctx.SubjectKey));
                })) accepted++;

            if (register(EventFlag, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    string flag = args.RequireString("flag");
                    if (!args.Finish()) return null;
                    return new DelegateCondition(args.Describe(EventFlag), ctx => history.HasFlag(flag, ctx.SubjectKey));
                })) accepted++;

            if (register(ActiveInCategory, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    string category = args.RequireString("category");
                    string scope = args.OptionalChoice("scope", ScopeSubject, ScopeSubject, ScopeAny);
                    if (!args.Finish()) return null;
                    bool any = string.Equals(scope, ScopeAny, StringComparison.Ordinal);
                    return new DelegateCondition(args.Describe(ActiveInCategory),
                        ctx => history.IsActiveInCategory(category, any ? null : ctx.SubjectKey));
                })) accepted++;

            if (register(HasRevenueHistory, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    if (!args.Finish()) return null;
                    return new DelegateCondition(HasRevenueHistory, ctx => world.HasRevenueHistory(ctx.SubjectKey));
                })) accepted++;

            if (register(HasFab, Owner, (node, errors, path) =>
                {
                    var args = new ConditionArgs(node, errors, path);
                    if (!args.Finish()) return null;
                    return new DelegateCondition(HasFab, ctx => world.HasFab(ctx.SubjectKey));
                })) accepted++;

            return accepted;
        }

        /// <summary>The day of the evaluation: the date of the context, or the world's today when the context has none.</summary>
        private static int TodayOf(ConditionContext ctx, IEventWorld world)
        {
            return ctx.HasDate ? EventDays.FromDate(ctx.Today) : world.Today;
        }
    }
}
