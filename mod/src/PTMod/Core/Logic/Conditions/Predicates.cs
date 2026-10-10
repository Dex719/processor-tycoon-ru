using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Conditions
{
    /// <summary>
    /// The predicates of the framework (design.md, "ModConditions: predicates and data sources"): 32 names without a dot, each reading
    /// its data through <see cref="IGameState"/>. <see cref="RegisterCore"/> puts them into a <see cref="PredicateRegistry"/>;
    /// the tree words <c>all</c>, <c>any</c> and <c>not</c> belong to the parser.
    /// <para>
    /// Common rules. The subject is the player unless the node has <c>company</c> (<c>player</c>, <c>ai:&lt;id&gt;</c> or <c>anyAI</c>:
    /// true when at least one non-bankrupt rival satisfies the predicate); global predicates (dates, difficulty, flags, features) have no
    /// <c>company</c>. Unknown parameters are errors. A data source that answers null makes the predicate false.
    /// Parameters beyond the table: <c>flag</c> accepts an optional integer <c>value</c> (the flag must equal it; without it any non-zero
    /// value is true). The table's parameter <c>type</c> of <c>companyType</c> would clash with the node's own <c>type</c>, so it is
    /// called <c>kind</c>.
    /// </para>
    /// </summary>
    public static class Predicates
    {
        /// <summary>Segment names accepted by <c>segmentShareAtLeast</c>, the suffixes of the <c>market.demand.*</c> channels.</summary>
        public static readonly IReadOnlyList<string> SegmentNames = new[] { "highend", "midrange", "lowend", "industries" };

        /// <summary>Company types accepted by <c>companyType</c>.</summary>
        public static readonly IReadOnlyList<string> CompanyTypes = new[] { "idm", "fabless", "foundry" };

        /// <summary>Tolerance of the reputation comparisons: values are stored as floats (79.1 is 79.0999985), thresholds are written in decimals.</summary>
        private const double ReputationTolerance = 1e-4;

        private const double Unbounded = 1e15;

        /// <summary>
        /// Registers every core predicate under the owner <see cref="PredicateRegistry.CoreOwner"/>. Returns how many were accepted;
        /// a refusal (for example a second call on the same registry) is recorded in <see cref="PredicateRegistry.Rejected"/>, nothing is thrown.
        /// </summary>
        public static int RegisterCore(PredicateRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            int added = 0;
            Action<string, Func<JsonObject, List<string>, string, ICondition>> add = (name, factory) =>
            {
                if (registry.Register(name, PredicateRegistry.CoreOwner, factory)) added++;
            };

            // dates
            add("dateAtLeast", Make("dateAtLeast", a => DateTest(a, true)));
            add("dateBefore", Make("dateBefore", a => DateTest(a, false)));
            add("daysSinceStartAtLeast", Make("daysSinceStartAtLeast", DaysSinceStart));

            // money and credit
            add("moneyAtLeast", Number("moneyAtLeast", "amount", false, -Unbounded, Unbounded, true, 0, (s, k) => s.MoneyAmount(k)));
            add("moneyBelow", Number("moneyBelow", "amount", false, -Unbounded, Unbounded, false, 0, (s, k) => s.MoneyAmount(k)));
            add("inDefault", Flag("inDefault", (s, k) => s.IsInDefault(k)));
            add("daysInDefaultAtLeast", Number("daysInDefaultAtLeast", "days", true, 0, int.MaxValue, true, 0, (s, k) => s.DaysInDefault(k)));

            // products and production
            add("cpuCountAtLeast", Make("cpuCountAtLeast", CpuCount));
            add("cpuOnMarket", Flag("cpuOnMarket", (s, k) => s.HasCpuOnMarket(k)));
            add("productionLinesAllocatedAtLeast", Number("productionLinesAllocatedAtLeast", "lines", true, 0, int.MaxValue, true, 0, (s, k) => s.ProductionLinesAllocated(k)));
            add("factoryLinesAtLeast", Number("factoryLinesAtLeast", "lines", true, 0, int.MaxValue, true, 0, (s, k) => s.FactoryLines(k)));

            // contracts
            add("oemContractsActiveAtLeast", Number("oemContractsActiveAtLeast", "count", true, 0, int.MaxValue, true, 0, (s, k) => s.ActiveOemContracts(k)));
            add("oemOfferSent", Flag("oemOfferSent", (s, k) => s.OemOfferSent(k)));
            add("businessContractsAtLeast", Number("businessContractsAtLeast", "count", true, 0, int.MaxValue, true, 0, (s, k) => s.BusinessContractsSigned(k)));

            // market
            add("marketShareAtLeast", Make("marketShareAtLeast", MarketShare));
            add("segmentShareAtLeast", Make("segmentShareAtLeast", SegmentShare));

            // research
            add("techResearched", Make("techResearched", TechResearched));
            add("researchActive", Flag("researchActive", (s, k) => s.IsResearchActive(k)));
            add("technologiesAtLeast", Number("technologiesAtLeast", "count", true, 0, int.MaxValue, true, 0, (s, k) => s.TechnologiesResearched(k)));

            // rivals and company
            add("rivalBankrupt", Make("rivalBankrupt", a => Rival(a, CompanyScope.AiOrAnyAi, true, (s, k) => s.IsBankrupt(k))));
            add("rivalExists", Make("rivalExists", a => Rival(a, CompanyScope.AiKey, false, (s, k) => s.CompanyExists(k))));
            add("companyType", Make("companyType", CompanyTypeTest));
            add("teamsAtLeast", Number("teamsAtLeast", "count", true, 0, int.MaxValue, true, 0, (s, k) => s.TeamCount(k)));

            // game setup
            add("difficultyAtLeast", Make("difficultyAtLeast", DifficultyAtLeast));
            add("cheatsEnabled", Make("cheatsEnabled", a => ctx => ctx.State != null && ctx.State.CheatsEnabled() == true));
            add("pgaEra", Make("pgaEra", a => ctx => ctx.State != null && ctx.State.IsPgaEra() == true));

            // finance and reputation
            add("profitLastMonthAtLeast", Number("profitLastMonthAtLeast", "amount", false, -Unbounded, Unbounded, true, 0, (s, k) => s.ProfitLastMonth(k)));
            add("revenueLastMonthAtLeast", Number("revenueLastMonthAtLeast", "amount", false, -Unbounded, Unbounded, true, 0, (s, k) => s.RevenueLastMonth(k)));
            add("reputationAtLeast", Number("reputationAtLeast", "value", false, 0, 100, true, ReputationTolerance, (s, k) => s.ReputationOf(k)));
            add("reputationBelow", Number("reputationBelow", "value", false, 0, 100, false, ReputationTolerance, (s, k) => s.ReputationOf(k)));

            // state kept by the framework
            add("flag", Make("flag", FlagTest));
            add("featureActive", Make("featureActive", FeatureActive));

            return added;
        }

        /// <summary>A new registry that already holds the core predicates.</summary>
        public static PredicateRegistry CreateRegistry(Func<string, string, bool> namePolicy = null)
        {
            var registry = new PredicateRegistry(namePolicy);
            RegisterCore(registry);
            return registry;
        }

        // ---- factory builders ----

        /// <summary>
        /// Wraps a reader of parameters into a registry factory: the reader gets a <see cref="ConditionArgs"/> and returns the test
        /// (or null when the parameters are wrong); unknown parameters are reported afterwards; the condition is described as <c>name(given parameters)</c>.
        /// </summary>
        private static Func<JsonObject, List<string>, string, ICondition> Make(string name, Func<ConditionArgs, Func<ConditionContext, bool>> read)
        {
            return (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                Func<ConditionContext, bool> test = read(args);
                bool clean = args.Finish();
                if (!clean || test == null) return null;
                return new DelegateCondition(args.Describe(name), test);
            };
        }

        /// <summary>
        /// "Value of the company at least / below a threshold": one numeric parameter, optional <c>company</c>.
        /// <paramref name="tolerance"/> widens the comparison by that much (reputation floats); the source answers null when unavailable.
        /// </summary>
        private static Func<JsonObject, List<string>, string, ICondition> Number(
            string name, string parameter, bool integer, double min, double max, bool atLeast, double tolerance,
            Func<IGameState, string, double?> read)
        {
            return Make(name, a =>
            {
                double threshold = integer ? a.RequireInt(parameter, (int)min, (int)max) : a.RequireNumber(parameter, min, max);
                CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
                Func<IGameState, string, bool?> test = (state, key) =>
                {
                    double? value = read(state, key);
                    if (value == null) return null;
                    return atLeast ? value.Value >= threshold - tolerance : value.Value < threshold - tolerance;
                };
                return ctx => who.Test(ctx, test);
            });
        }

        /// <summary>A yes/no property of the company, optional <c>company</c>.</summary>
        private static Func<JsonObject, List<string>, string, ICondition> Flag(string name, Func<IGameState, string, bool?> read)
        {
            return Make(name, a =>
            {
                CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
                return ctx => who.Test(ctx, read);
            });
        }

        // ---- predicates with their own parameters ----

        private static Func<ConditionContext, bool> DateTest(ConditionArgs a, bool atLeast)
        {
            int year = a.RequireInt("year", 1, 9999);
            int month = a.OptionalInt("month", 1, 1, 12);
            int day = a.OptionalInt("day", 1, 1, 31);
            if (!a.Ok) return null;

            if (day > DateTime.DaysInMonth(year, month))
            {
                a.Fail("day", "there is no day " + day + " in month " + month + " of " + year);
                return null;
            }

            DateTime date = new DateTime(year, month, day);
            if (atLeast) return ctx => ctx.HasDate && ctx.Today.Date >= date;
            return ctx => ctx.HasDate && ctx.Today.Date < date;
        }

        private static Func<ConditionContext, bool> DaysSinceStart(ConditionArgs a)
        {
            int days = a.RequireInt("days", 0, int.MaxValue);
            return ctx =>
            {
                if (ctx.State == null || !ctx.HasDate) return false;
                DateTime? start = ctx.State.GameStartDate();
                return start != null && (ctx.Today.Date - start.Value.Date).TotalDays >= days;
            };
        }

        private static Func<ConditionContext, bool> CpuCount(ConditionArgs a)
        {
            int count = a.RequireInt("count", 0, int.MaxValue);
            bool includeRetired = a.OptionalBool("includeRetired", false);
            CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
            Func<IGameState, string, bool?> test = (state, key) =>
            {
                int? value = state.CpuCount(key, includeRetired);
                if (value == null) return null;
                return value.Value >= count;
            };
            return ctx => who.Test(ctx, test);
        }

        private static Func<ConditionContext, bool> MarketShare(ConditionArgs a)
        {
            double share = a.RequireNumber("share", 0, 1);
            MarketWindow window = ReadWindow(a);
            CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
            Func<IGameState, string, bool?> test = (state, key) =>
            {
                double? value = state.MarketShare(key, window);
                if (value == null) return null;
                return value.Value >= share;
            };
            return ctx => who.Test(ctx, test);
        }

        private static Func<ConditionContext, bool> SegmentShare(ConditionArgs a)
        {
            string segment = a.RequireChoice("segment", ToArray(SegmentNames));
            double share = a.RequireNumber("share", 0, 1);
            MarketWindow window = ReadWindow(a);
            CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
            Func<IGameState, string, bool?> test = (state, key) =>
            {
                double? value = state.SegmentShare(key, segment, window);
                if (value == null) return null;
                return value.Value >= share;
            };
            return ctx => who.Test(ctx, test);
        }

        private static Func<ConditionContext, bool> TechResearched(ConditionArgs a)
        {
            string name = a.RequireString("name");
            CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
            Func<IGameState, string, bool?> test = (state, key) => state.IsTechnologyResearched(key, name);
            return ctx => who.Test(ctx, test);
        }

        private static Func<ConditionContext, bool> Rival(ConditionArgs a, CompanyScope scope, bool includeBankrupt, Func<IGameState, string, bool?> read)
        {
            CompanySelector who = a.Company(scope);
            return ctx => who.Test(ctx, read, includeBankrupt);
        }

        private static Func<ConditionContext, bool> CompanyTypeTest(ConditionArgs a)
        {
            string kind = a.RequireChoice("kind", ToArray(CompanyTypes));
            CompanySelector who = a.Company(CompanyScope.PlayerOrAi);
            Func<IGameState, string, bool?> test = (state, key) =>
            {
                string type = state.CompanyType(key);
                if (type == null) return null;
                return string.Equals(type, kind, StringComparison.OrdinalIgnoreCase);
            };
            return ctx => who.Test(ctx, test);
        }

        private static Func<ConditionContext, bool> DifficultyAtLeast(ConditionArgs a)
        {
            int level = a.RequireInt("level", 0, 5);
            return ctx =>
            {
                if (ctx.State == null) return false;
                int? value = ctx.State.DifficultyLevel();
                return value != null && value.Value >= level;
            };
        }

        private static Func<ConditionContext, bool> FlagTest(ConditionArgs a)
        {
            string feature = a.RequireString("feature");
            string key = a.RequireString("key");
            bool compare = a.Has("value");
            int expected = a.OptionalInt("value", 0, int.MinValue, int.MaxValue);
            return ctx =>
            {
                if (ctx.State == null) return false;
                int? value = ctx.State.FlagValue(feature, key);
                if (value == null) return false;
                return compare ? value.Value == expected : value.Value != 0;
            };
        }

        private static Func<ConditionContext, bool> FeatureActive(ConditionArgs a)
        {
            string id = a.RequireString("id");
            return ctx => ctx.State != null && ctx.State.IsFeatureActive(id) == true;
        }

        private static MarketWindow ReadWindow(ConditionArgs a)
        {
            string window = a.OptionalChoice("window", "day", "day", "month");
            return string.Equals(window, "month", StringComparison.Ordinal) ? MarketWindow.Month : MarketWindow.Day;
        }

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }
    }
}
