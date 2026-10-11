using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What the predicates of the feature ask about the rivals (FR-13 item 4); the game side answers from the books of the feature.</summary>
    public interface IRivalFacts
    {
        /// <summary>
        /// The attitude of the rival to the player, -100..+100; null for a key that is not an AI company or a company without a character (the
        /// predicate is then false).
        /// </summary>
        float? AttitudeOf(string companyKey);

        /// <summary>The reactions the rival has carried out so far; null for a key that is not an AI company or a company without a character.</summary>
        int? ReactionsExecutedBy(string companyKey);

        /// <summary>The reactions all rivals have carried out so far.</summary>
        int ReactionsExecutedTotal();
    }

    /// <summary>The names of the predicates of the feature in the JSON of conditions: <c>rival.&lt;name&gt;</c> (the prefix of the feature, D28).</summary>
    public static class RivalPredicateNames
    {
        /// <summary><c>{ "type": "rival.attitudeAtMost", "company": "ai:1", "value": -40 }</c></summary>
        public const string AttitudeAtMost = "rival.attitudeAtMost";

        /// <summary><c>{ "type": "rival.attitudeAtLeast", "company": "ai:1", "value": 60 }</c></summary>
        public const string AttitudeAtLeast = "rival.attitudeAtLeast";

        /// <summary><c>{ "type": "rival.reactionsExecuted", "count": 3 }</c>, or with <c>"company": "ai:1"</c> / <c>"anyAI"</c></summary>
        public const string ReactionsExecuted = "rival.reactionsExecuted";

        public static readonly string[] All = { AttitudeAtMost, AttitudeAtLeast, ReactionsExecuted };
    }

    /// <summary>
    /// The predicates of the feature for scenarios, events and achievements (FR-13 item 4, TSK-020). The factories read the parameters with
    /// <see cref="ConditionArgs"/> (a typo or a missing parameter is an error with the path of the node, not a silent false) and answer from
    /// <see cref="IRivalFacts"/>. The core wraps what is registered, so a predicate is false while the feature is not active.
    /// <list type="bullet">
    /// <item><c>rival.attitudeAtMost</c> and <c>rival.attitudeAtLeast</c>: <c>company</c> is <c>ai:&lt;UniqueID&gt;</c> or <c>anyAI</c> (absent means
    /// <c>anyAI</c>: true when at least one living rival satisfies it), <c>value</c> is -100..100; the attitude is compared with <c>&lt;=</c> and <c>&gt;=</c>.</item>
    /// <item><c>rival.reactionsExecuted</c>: <c>count</c> is 0..100000. Without <c>company</c> it is true when all rivals together have carried out at least
    /// that many reactions; with <c>ai:&lt;UniqueID&gt;</c> when that rival has; with <c>anyAI</c> when some living rival has.</item>
    /// </list>
    /// </summary>
    public static class RivalPredicates
    {
        /// <summary>The largest <c>count</c> a predicate accepts.</summary>
        public const int MaxCount = 100000;

        /// <summary>The three factories by name, for <c>ModConditions.Register</c> (or <c>PredicateRegistry.Register</c> in a test).</summary>
        public static IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> CreateFactories(IRivalFacts facts)
        {
            if (facts == null) throw new ArgumentNullException("facts");

            var list = new List<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>>();
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add =
                (name, factory) => list.Add(new KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>(name, factory));

            add(RivalPredicateNames.AttitudeAtMost, (node, errors, path) => Attitude(node, errors, path, facts, RivalPredicateNames.AttitudeAtMost, atMost: true));
            add(RivalPredicateNames.AttitudeAtLeast, (node, errors, path) => Attitude(node, errors, path, facts, RivalPredicateNames.AttitudeAtLeast, atMost: false));
            add(RivalPredicateNames.ReactionsExecuted, (node, errors, path) => ReactionsExecuted(node, errors, path, facts));
            return list;
        }

        private static ICondition Attitude(JsonObject node, List<string> errors, string path, IRivalFacts facts, string name, bool atMost)
        {
            var args = new ConditionArgs(node, errors, path);
            CompanySelector company = args.Company(CompanyScope.AiOrAnyAi);
            double value = args.RequireNumber("value", -100, 100);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(name), ctx => company.Test(ctx, (state, key) =>
            {
                float? attitude = facts.AttitudeOf(key);
                if (!attitude.HasValue) return null;
                return atMost ? attitude.Value <= value : attitude.Value >= value;
            }));
        }

        private static ICondition ReactionsExecuted(JsonObject node, List<string> errors, string path, IRivalFacts facts)
        {
            var args = new ConditionArgs(node, errors, path);
            int count = args.RequireInt("count", 0, MaxCount);
            bool hasCompany = args.Has("company");
            CompanySelector company = hasCompany ? args.Company(CompanyScope.AiOrAnyAi) : null;
            if (!args.Finish()) return null;

            string description = args.Describe(RivalPredicateNames.ReactionsExecuted);
            if (company == null) return new DelegateCondition(description, ctx => facts.ReactionsExecutedTotal() >= count);

            return new DelegateCondition(description, ctx => company.Test(ctx, (state, key) =>
            {
                int? executed = facts.ReactionsExecutedBy(key);
                if (!executed.HasValue) return null;
                return executed.Value >= count;
            }));
        }
    }
}
