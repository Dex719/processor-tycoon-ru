using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>What the predicates of the feature ask about the platforms of the game (FR-11 item 1); the game side answers from the chronology and the installed base.</summary>
    public interface IPlatformFacts
    {
        /// <summary>The generation has been launched (the first CPU on it was released).</summary>
        bool IsLaunched(string genId);

        /// <summary>The company is an early adopter of the generation.</summary>
        bool IsEarlyAdopter(string companyKey, string genId);

        /// <summary>The share of the whole installed base that the company's best socket holds, 0..1.</summary>
        double BestSocketShare(string companyKey);
    }

    /// <summary>The names of the predicates of the feature in the JSON of conditions: <c>pg.&lt;name&gt;</c> (the prefix of the feature, D28).</summary>
    public static class PlatformPredicateNames
    {
        /// <summary><c>{ "type": "pg.generationLaunched", "gen": "gen-2000" }</c></summary>
        public const string GenerationLaunched = "pg.generationLaunched";

        /// <summary><c>{ "type": "pg.earlyAdopter", "gen": "gen-1995-pga", "company": "player" }</c></summary>
        public const string EarlyAdopter = "pg.earlyAdopter";

        /// <summary><c>{ "type": "pg.installedBaseShare", "atLeast": 0.25, "company": "player" }</c></summary>
        public const string InstalledBaseShare = "pg.installedBaseShare";

        public static readonly string[] All = { GenerationLaunched, EarlyAdopter, InstalledBaseShare };
    }

    /// <summary>
    /// The predicates of the feature for scenarios and achievements (FR-11 item 1): the generation has been launched, the company is an early
    /// adopter of it, the company's best socket holds a share of the installed base. The factories read the parameters with <see cref="ConditionArgs"/>
    /// (a typo or a missing parameter is an error with the path of the node, not a silent false) and answer from <see cref="IPlatformFacts"/>. A company
    /// parameter is <c>player</c>, <c>ai:&lt;id&gt;</c> or <c>anyAI</c>; without it the subject of the condition (the player unless the caller says otherwise).
    /// </summary>
    public static class PlatformPredicates
    {
        /// <summary>The three factories by name, for <c>ModConditions.Register</c>.</summary>
        public static IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> CreateFactories(IPlatformFacts facts)
        {
            if (facts == null) throw new ArgumentNullException("facts");

            var list = new List<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>>();
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add =
                (name, factory) => list.Add(new KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>(name, factory));

            add(PlatformPredicateNames.GenerationLaunched, (node, errors, path) => GenerationLaunched(node, errors, path, facts));
            add(PlatformPredicateNames.EarlyAdopter, (node, errors, path) => EarlyAdopter(node, errors, path, facts));
            add(PlatformPredicateNames.InstalledBaseShare, (node, errors, path) => InstalledBaseShare(node, errors, path, facts));
            return list;
        }

        private static ICondition GenerationLaunched(JsonObject node, List<string> errors, string path, IPlatformFacts facts)
        {
            var args = new ConditionArgs(node, errors, path);
            string gen = args.RequireString("gen");
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(PlatformPredicateNames.GenerationLaunched), ctx => facts.IsLaunched(gen));
        }

        private static ICondition EarlyAdopter(JsonObject node, List<string> errors, string path, IPlatformFacts facts)
        {
            var args = new ConditionArgs(node, errors, path);
            string gen = args.RequireString("gen");
            CompanySelector company = args.Company(CompanyScope.PlayerOrAi);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(PlatformPredicateNames.EarlyAdopter),
                ctx => company.Test(ctx, (state, key) => facts.IsEarlyAdopter(key, gen)));
        }

        private static ICondition InstalledBaseShare(JsonObject node, List<string> errors, string path, IPlatformFacts facts)
        {
            var args = new ConditionArgs(node, errors, path);
            double atLeast = args.RequireNumber("atLeast", 0, 1);
            CompanySelector company = args.Company(CompanyScope.PlayerOrAi);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(PlatformPredicateNames.InstalledBaseShare),
                ctx => company.Test(ctx, (state, key) => facts.BestSocketShare(key) >= atLeast));
        }
    }
}
