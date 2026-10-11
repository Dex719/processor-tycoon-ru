using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// A predicate of a pack. <paramref name="args"/> is the JSON object of the node (it holds <c>type</c> and the parameters, at the top level);
    /// <paramref name="ctx"/> is the evaluation context, its <c>SubjectKey</c> is the target company. Returns false when the data it needs is not there;
    /// an exception makes the whole condition tree false and is counted against the owner feature.
    /// </summary>
    public delegate bool EventPredicate(JsonObject args, ConditionContext ctx);

    /// <summary>
    /// The facade of the engine over <c>ModConditions</c>: the conditions of <c>conditions</c>, <c>targets.filter</c> and <c>requires</c> use the syntax
    /// of <c>ModConditions</c> (<c>type</c> with parameters, or <c>all</c> / <c>any</c> / <c>not</c>). Needs nothing from the engine itself, so it works
    /// while the event system is off too.
    /// </summary>
    public static class EventConditions
    {
        /// <summary>
        /// Adds a predicate to the shared catalog of <c>ModConditions</c>. <paramref name="type"/> is <c>&lt;prefix&gt;.&lt;name&gt;</c> with a prefix of
        /// <paramref name="ownerFeature"/> (<c>es.</c> for the engine, <c>ms.</c> for market-shocks...); <c>ModConditions.Register</c> refuses any other
        /// name and a name already taken (a warning is logged). The predicate is false while its owner feature is not active. Any parameters are accepted:
        /// the predicate checks its own.
        /// </summary>
        public static void Register(string type, string ownerFeature, EventPredicate predicate)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(ownerFeature) || predicate == null)
            {
                ModLog.Warn(string.IsNullOrEmpty(ownerFeature) ? EventSystemApi.FeatureId : ownerFeature,
                    "api: EventConditions.Register ignored, the type, the owner or the predicate is missing");
                return;
            }

            ModConditions.Register(type, ownerFeature, (node, errors, path) => new DelegateCondition(type, ctx => predicate(node, ctx)));
        }

        /// <summary>Parses a condition node (<c>ModConditions.Parse</c>); errors are added to <paramref name="errors"/>, the result is null when there is any.</summary>
        public static ICondition Parse(JsonNode node, List<string> errors, string path = "$")
        {
            return ModConditions.Parse(node, errors, path);
        }

        /// <summary>Evaluates a condition for <paramref name="subject"/>; a null condition ("no condition") is true. Never throws.</summary>
        public static bool Evaluate(ICondition condition, ICompany subject)
        {
            if (condition == null) return true;
            return ModConditions.Evaluate(condition, subject);
        }
    }
}
