using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>How often the value of a metric changes, so a check on it has a natural cadence (design.md, table "Метрики").</summary>
    public enum MetricFrequency
    {
        /// <summary>Sampled once per game day.</summary>
        Day,

        /// <summary>Changes when a month closes (<c>TickPhase.LateMonthPassed</c>).</summary>
        Month,

        /// <summary>Changes when something happens (a contract is signed, a rival goes bankrupt).</summary>
        Event,
    }

    /// <summary>One metric the content may name: its id, how often it changes and the range its values naturally lie in.</summary>
    public sealed class MetricInfo
    {
        public MetricInfo(string id, MetricFrequency frequency, double min, double max)
        {
            Id = id;
            Frequency = frequency;
            Min = min;
            Max = max;
        }

        /// <summary>Id as the content writes it (<c>share.total.avg30</c>).</summary>
        public string Id { get; }

        public MetricFrequency Frequency { get; }

        /// <summary>Lowest value the metric can take; <see cref="double.NegativeInfinity"/> for none. A check against a value outside the range is a warning.</summary>
        public double Min { get; }

        /// <summary>Highest value the metric can take; <see cref="double.PositiveInfinity"/> for none.</summary>
        public double Max { get; }

        /// <summary>
        /// The cadence a check on this metric gets when the content does not name one (<see cref="CheckSpec.Cadence"/>): monthly for the
        /// monthly metrics, daily for the rest - a metric that changes on an event is still read once a day, the <c>event</c> cadence is for
        /// <c>event</c>, <c>cpuReleased</c>, <c>race</c> and <c>profile</c> checks.
        /// </summary>
        public string DefaultCadence
        {
            get { return Frequency == MetricFrequency.Month ? CheckSpec.CadenceMonthly : CheckSpec.CadenceDaily; }
        }
    }

    /// <summary>
    /// The closed vocabulary of the content of <c>scenarios-achievements</c> (design.md: "Метрики", "Контент", "Validation Rules"): the metrics, the
    /// events, the allowed years, the kinds of check and the other white lists the validator and the mapper agree on. One place, so the real
    /// validation context of the game and the fake one of the tests answer from the same data. Pure data, no game types.
    /// </summary>
    public static class ContentVocabulary
    {
        /// <summary>The id of the feature, also the folder of its content and the owner of its prefixes.</summary>
        public const string FeatureId = "scenarios-achievements";

        /// <summary>The L10n prefixes of the feature (<c>core.json</c>, row <c>scenarios-achievements</c>): scenarios, challenges, achievements.</summary>
        public static readonly IReadOnlyList<string> KeyPrefixes = new[] { "scn.", "chl.", "ach." };

        /// <summary>Start years of the menu of the game (<c>MenuGameSetup.UpdateStartingYear</c>: 1975 + 5 × index); FR-1 item 5.</summary>
        public static readonly IReadOnlyList<int> StartYears = new[] { 1975, 1980, 1985, 1990, 1995, 2000 };

        /// <summary>Company types of <c>start.companyType</c>, in the order of the game's <c>CompanyType</c> enum (idm 0, fabless 1, foundry 2).</summary>
        public static readonly IReadOnlyList<string> CompanyTypes = new[] { "idm", "fabless", "foundry" };

        /// <summary>Modes of the tutorial a scenario asks for (<c>onboarding</c>).</summary>
        public static readonly IReadOnlyList<string> OnboardingModes = new[] { "allowed", "forcedOn", "forcedOff" };

        /// <summary>Who a modifier applies to (<c>appliesTo</c>): the player, every AI company, everybody.</summary>
        public static readonly IReadOnlyList<string> Scopes = new[] { "player", "ai", "all" };

        public static readonly IReadOnlyList<string> CheckKinds = new[]
        {
            CheckSpec.KindMetric, CheckSpec.KindCpuReleased, CheckSpec.KindRace, CheckSpec.KindEvent,
            CheckSpec.KindCondition, CheckSpec.KindProfile, CheckSpec.KindAll,
        };

        /// <summary>Comparison operators of a metric check.</summary>
        public static readonly IReadOnlyList<string> Operators = new[] { ">=", ">", "<=", "<", "==" };

        public static readonly IReadOnlyList<string> Cadences = new[] { CheckSpec.CadenceDaily, CheckSpec.CadenceMonthly, CheckSpec.CadenceEvent };

        /// <summary>How a progress value is shown in the tracker.</summary>
        public static readonly IReadOnlyList<string> ProgressFormats = new[] { "percent", "money", "count" };

        /// <summary>Categories of the achievements (the tabs of the achievements window).</summary>
        public static readonly IReadOnlyList<string> Categories = new[] { "career", "money", "tech", "market", "business", "meta" };

        /// <summary>Medals from the best to the worst; <c>completed</c> is a won scenario below the bronze threshold.</summary>
        public static readonly IReadOnlyList<string> Medals = new[] { "gold", "silver", "bronze", "completed" };

        /// <summary>Rules of a <c>profile</c> check: <c>allScenarioMedals</c> - every standard scenario has the given medal (or better) in the profile.</summary>
        public static readonly IReadOnlyList<string> ProfileRules = new[] { ProfileSpec.RuleAllScenarioMedals };

        /// <summary>
        /// The internal events of the feature (design.md, "Контент: достижения"). Not the ids of <c>event-system</c> events: no prefix.
        /// </summary>
        public static readonly IReadOnlyList<string> Events = new[]
        {
            "cpu.released.player", "cpu.released.ai", "oem.signed", "foundry.signed", "player.bankrupt", "rival.bankrupt",
            "scenario.won", "scenario.gold", "scenario.lost", "challenge.passed", "challenge.failed",
            "onboarding.completed.clean", "endyear.reached",
        };

        private const double Inf = double.PositiveInfinity;
        private static readonly MetricInfo[] MetricTable =
        {
            new MetricInfo("money", MetricFrequency.Day, -Inf, Inf),
            new MetricInfo("money.min", MetricFrequency.Day, -Inf, Inf),
            new MetricInfo("money.solventStreakDays", MetricFrequency.Day, 0, Inf),
            new MetricInfo("money.negativeStreakDays", MetricFrequency.Day, 0, Inf),
            new MetricInfo("profit.lastMonth", MetricFrequency.Month, -Inf, Inf),
            new MetricInfo("profit.streakMonths", MetricFrequency.Month, 0, Inf),
            new MetricInfo("revenue.last12m", MetricFrequency.Month, 0, Inf),
            new MetricInfo("revenue.clients.last12m", MetricFrequency.Month, 0, Inf),
            new MetricInfo("share.total.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("share.highend.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("share.midrange.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("share.lowend.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("share.industries.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("share.minSegment.avg30", MetricFrequency.Day, 0, 1),
            new MetricInfo("sales.total", MetricFrequency.Day, 0, Inf),
            new MetricInfo("cpu.released.count", MetricFrequency.Day, 0, Inf),
            new MetricInfo("cpu.maxFrequency", MetricFrequency.Day, 0, Inf),
            new MetricInfo("cpu.maxCores", MetricFrequency.Day, 0, Inf),
            new MetricInfo("cpu.hasSmt", MetricFrequency.Day, 0, 1),
            new MetricInfo("popularity.max", MetricFrequency.Day, 0, 1),
            new MetricInfo("oem.active.count", MetricFrequency.Day, 0, Inf),
            new MetricInfo("oem.signed.total", MetricFrequency.Event, 0, Inf),
            new MetricInfo("foundry.clients.active", MetricFrequency.Day, 0, Inf),
            new MetricInfo("foundry.asClient.count", MetricFrequency.Day, 0, Inf),
            new MetricInfo("factory.lines", MetricFrequency.Day, 0, Inf),
            new MetricInfo("missed.zeroStreakDays", MetricFrequency.Day, 0, Inf),
            new MetricInfo("ips.leaderStreakDays", MetricFrequency.Day, 0, Inf),
            new MetricInfo("rivals.bankruptCount", MetricFrequency.Event, 0, Inf),
            new MetricInfo("default.recoveredCount", MetricFrequency.Day, 0, Inf),
            new MetricInfo("run.days", MetricFrequency.Day, 0, Inf),
            new MetricInfo("run.difficulty", MetricFrequency.Day, 0, 5),
        };

        private static readonly Dictionary<string, MetricInfo> MetricById = BuildMetricIndex();
        private static readonly HashSet<string> EventSet = new HashSet<string>(Events, StringComparer.Ordinal);

        /// <summary>The metrics of the feature, in the order of the design table.</summary>
        public static IReadOnlyList<MetricInfo> Metrics
        {
            get { return MetricTable; }
        }

        public static bool TryGetMetric(string id, out MetricInfo metric)
        {
            metric = null;
            return id != null && MetricById.TryGetValue(id, out metric);
        }

        public static bool IsKnownMetric(string id)
        {
            return id != null && MetricById.ContainsKey(id);
        }

        public static bool IsKnownEvent(string id)
        {
            return id != null && EventSet.Contains(id);
        }

        /// <summary><c>idm</c>, <c>fabless</c> or <c>foundry</c> (case-sensitive, as the content writes them).</summary>
        public static bool IsCompanyType(string value)
        {
            return Contains(CompanyTypes, value);
        }

        /// <summary>The value of the game's <c>CompanyType</c> enum for a company type id (idm 0, fabless 1, foundry 2); -1 for an unknown id.</summary>
        public static int CompanyTypeCode(string value)
        {
            for (int i = 0; i < CompanyTypes.Count; i++)
            {
                if (string.Equals(CompanyTypes[i], value, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary>Exact (ordinal) membership of <paramref name="value"/> in a vocabulary list; false for null.</summary>
        public static bool Contains(IReadOnlyList<string> list, string value)
        {
            if (value == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsStartYear(int year)
        {
            for (int i = 0; i < StartYears.Count; i++)
            {
                if (StartYears[i] == year) return true;
            }
            return false;
        }

        /// <summary>Parses a date of the content (<c>yyyy-MM-dd</c>, nothing else); false for anything that is not exactly that.</summary>
        public static bool TryParseDate(string text, out DateTime date)
        {
            date = default(DateTime);
            return !string.IsNullOrEmpty(text)
                   && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>The date as the content writes it (<c>yyyy-MM-dd</c>).</summary>
        public static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, MetricInfo> BuildMetricIndex()
        {
            var index = new Dictionary<string, MetricInfo>(StringComparer.Ordinal);
            foreach (MetricInfo metric in MetricTable) index[metric.Id] = metric;
            return index;
        }
    }
}
