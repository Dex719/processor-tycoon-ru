using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The two counters the predicates of the feature read (FR-14 item 3): the offers the player has sent and the tenders the player has won in this game. <see cref="ContractLogModel"/> keeps
    /// them and the save carries them (<c>offersSent</c>, <c>tendersWon</c> of the section), so they survive saving and loading; the tests use a plain fake.
    /// </summary>
    public interface IContractCounters
    {
        /// <summary>Offers the player has sent (by hand, by the automation or through the API).</summary>
        int OffersSent { get; }

        /// <summary>Tenders the player has won.</summary>
        int TendersWon { get; }
    }

    /// <summary>The names of the predicates of the feature in the JSON of conditions: <c>ct.&lt;name&gt;</c> (the prefix of the feature in the table of <c>core.json</c>, D28).</summary>
    public static class ContractPredicateNames
    {
        /// <summary><c>{ "type": "ct.offersSent", "atLeast": 1 }</c>: the player has sent at least that many offers.</summary>
        public const string OffersSent = "ct.offersSent";

        /// <summary><c>{ "type": "ct.tendersWon", "atLeast": 1 }</c>: the player has won at least that many tenders.</summary>
        public const string TendersWon = "ct.tendersWon";

        public static readonly string[] All = { OffersSent, TendersWon };
    }

    /// <summary>
    /// The predicates of the feature for the other features (FR-14 item 3, AC-14.1): <c>ct.offersSent</c> and <c>ct.tendersWon</c> with the parameter <c>atLeast</c> (a whole number from 0). The factories read
    /// the parameters with <see cref="ConditionArgs"/>, so a missing or mistyped <c>atLeast</c> and a parameter nobody knows are errors with the path of the node, not a silent false.
    /// <para>
    /// The counters are the player's: a condition whose subject is another company (<c>ConditionContext.SubjectKey</c>) is false. The numbers are read when the condition is evaluated, not when it is
    /// parsed, so a condition parsed early sees the offers sent later. The core wraps a registered predicate: it is false while the feature is not active and an exception it throws is counted against
    /// the feature.
    /// </para>
    /// </summary>
    public static class ContractPredicates
    {
        /// <summary>The factories by name, for <c>ModConditions.Register</c> (or <c>PredicateRegistry.Register</c> in a test).</summary>
        public static IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> CreateFactories(IContractCounters counters)
        {
            if (counters == null) throw new ArgumentNullException("counters");

            var list = new List<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>>();
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add =
                (name, factory) => list.Add(new KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>(name, factory));

            add(ContractPredicateNames.OffersSent, (node, errors, path) => AtLeast(node, errors, path, ContractPredicateNames.OffersSent, () => counters.OffersSent));
            add(ContractPredicateNames.TendersWon, (node, errors, path) => AtLeast(node, errors, path, ContractPredicateNames.TendersWon, () => counters.TendersWon));
            return list;
        }

        private static ICondition AtLeast(JsonObject node, List<string> errors, string path, string name, Func<int> counter)
        {
            var args = new ConditionArgs(node, errors, path);
            int atLeast = args.RequireInt("atLeast", 0, int.MaxValue);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(name), ctx => IsPlayer(ctx) && counter() >= atLeast);
        }

        /// <summary>The counters are the player's company's; the subject of a context is the player unless the caller says otherwise.</summary>
        private static bool IsPlayer(ConditionContext ctx)
        {
            return ctx != null && string.Equals(ctx.SubjectKey, CompanyKey.Player, StringComparison.Ordinal);
        }
    }
}
