using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>The names of the predicates of the feature in the JSON of conditions: <c>gpu.&lt;name&gt;</c> (the prefix of the feature in the table of <c>core.json</c>, D28).</summary>
    public static class GpuPredicateNames
    {
        /// <summary>The prefix of the feature in the table of <c>core.json</c>; the names are put together from it, so the scan of the text keys (which takes every <c>"gpu.xxx"</c> for one) leaves them alone.</summary>
        public const string Prefix = "gpu.";

        /// <summary><c>{ "type": "gpu.divisionFounded" }</c>, optionally with <c>company</c>: the company has a GPU division.</summary>
        public const string DivisionFounded = Prefix + "divisionFounded";

        /// <summary><c>{ "type": "gpu.hasProductOnMarket" }</c>, optionally with <c>company</c>: the company has a card on sale.</summary>
        public const string HasProductOnMarket = Prefix + "hasProductOnMarket";

        /// <summary><c>{ "type": "gpu.marketShareAtLeast", "share": 0.25 }</c>, optionally with <c>company</c>: the share of the GPU market over 30 days is at least that.</summary>
        public const string MarketShareAtLeast = Prefix + "marketShareAtLeast";

        /// <summary><c>{ "type": "gpu.researched", "node": "arch.3d" }</c>: the player has researched that node of the GPU tree.</summary>
        public const string Researched = Prefix + "researched";

        public static readonly string[] All = { DivisionFounded, HasProductOnMarket, MarketShareAtLeast, Researched };
    }

    /// <summary>
    /// The predicates of the feature for the other features and the scenarios (design.md, "GpuApi"; FR-18 item 4, AC-18.2): <c>gpu.divisionFounded</c>, <c>gpu.hasProductOnMarket</c>,
    /// <c>gpu.marketShareAtLeast</c> (<c>share</c> from 0 to 1) and <c>gpu.researched</c> (<c>node</c>). Thin wrappers over <see cref="IGpuQueries"/> - the answers of <c>GpuApi</c> - read
    /// with <see cref="ConditionArgs"/>, so a parameter that is missing, mistyped or unknown is an error with the path of the node, not a silent false. The first three take the
    /// parameter <c>company</c> of the core (<c>player</c>, <c>ai:&lt;id&gt;</c>, <c>anyAI</c>; the subject of the context when absent, the player by default). The numbers are read when the
    /// condition is evaluated, not when it is parsed. With the feature off, or before it has registered its services, every predicate is false. The names follow the convention
    /// of the core (<c>ModIds.IsPredicateNameAllowed</c>) and do not meet the channel <c>gpu.rating</c>: the spaces of predicates and channels are apart.
    /// </summary>
    public static class GpuPredicates
    {
        /// <summary>The window of the share a predicate asks about, days (FR-18: the share is the one of 30 days).</summary>
        public const int ShareDays = 30;

        /// <summary>The factories by name, for <c>ModConditions.Register</c> (or <c>PredicateRegistry.Register</c> in a test). <paramref name="source"/> gives the answers when the condition is evaluated (null: none yet).</summary>
        public static IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> CreateFactories(Func<IGpuQueries> source)
        {
            if (source == null) throw new ArgumentNullException("source");

            var list = new List<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>>();
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add =
                (name, factory) => list.Add(new KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>(name, factory));

            add(GpuPredicateNames.DivisionFounded, (node, errors, path) => OfCompany(node, errors, path, GpuPredicateNames.DivisionFounded, source, (q, key) => q.IsDivisionFounded(key)));
            add(GpuPredicateNames.HasProductOnMarket, (node, errors, path) => OfCompany(node, errors, path, GpuPredicateNames.HasProductOnMarket, source, (q, key) => q.HasProductOnMarket(key)));
            add(GpuPredicateNames.MarketShareAtLeast, (node, errors, path) => ShareAtLeast(node, errors, path, source));
            add(GpuPredicateNames.Researched, (node, errors, path) => Researched(node, errors, path, source));
            return list;
        }

        private static ICondition OfCompany(JsonObject node, List<string> errors, string path, string name, Func<IGpuQueries> source, Func<IGpuQueries, string, bool> test)
        {
            var args = new ConditionArgs(node, errors, path);
            CompanySelector company = args.Company(CompanyScope.PlayerOrAi);
            if (!args.Finish()) return null;

            return new DelegateCondition(args.Describe(name), ctx => company.Test(ctx, (state, key) => Ask(source, key, test)));
        }

        private static ICondition ShareAtLeast(JsonObject node, List<string> errors, string path, Func<IGpuQueries> source)
        {
            var args = new ConditionArgs(node, errors, path);
            CompanySelector company = args.Company(CompanyScope.PlayerOrAi);
            float share = (float)args.RequireNumber("share", 0.0, 1.0);
            if (!args.Finish()) return null;

            // compared as single-precision numbers like the share itself: 0.3 in the JSON and the 0.3 of the game are the same number
            return new DelegateCondition(args.Describe(GpuPredicateNames.MarketShareAtLeast),
                ctx => company.Test(ctx, (state, key) => Ask(source, key, (q, k) => q.MarketShare(k, ShareDays) >= share)));
        }

        private static ICondition Researched(JsonObject node, List<string> errors, string path, Func<IGpuQueries> source)
        {
            var args = new ConditionArgs(node, errors, path);
            string nodeId = args.RequireString("node");
            if (!args.Finish()) return null;

            // the research tree is the player's: a condition about another company is false
            return new DelegateCondition(args.Describe(GpuPredicateNames.Researched),
                ctx => ctx != null && string.Equals(ctx.SubjectKey, CompanyKey.Player, StringComparison.Ordinal) && Ask(source, ctx.SubjectKey, (q, key) => q.IsResearched(nodeId)));
        }

        // The answer of the services, false while there are none or the feature is off.
        private static bool Ask(Func<IGpuQueries> source, string key, Func<IGpuQueries, string, bool> test)
        {
            IGpuQueries queries = source();
            return queries != null && queries.IsAvailable && !string.IsNullOrEmpty(key) && test(queries, key);
        }
    }
}
