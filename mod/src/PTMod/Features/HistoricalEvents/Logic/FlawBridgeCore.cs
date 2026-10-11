using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The rules that choose the CPU a historical flaw is forced on (<c>pick</c> of the effect <c>ForceDesignFlaw</c>, FR-12 item 1).</summary>
    public static class FlawPicks
    {
        /// <summary>The best-selling HighEnd CPU of the company over the last 24 months (the Pentium FDIV bug).</summary>
        public const string TopSellingHighEnd24m = "topSellingHighEnd24m";

        /// <summary>One CPU per company: released within the last 60 months and not slower than <c>Market.AverageIps</c> (Meltdown and Spectre).</summary>
        public const string PerCompanyAboveAvgIps60m = "perCompanyAboveAvgIps60m";

        public static readonly string[] All = { TopSellingHighEnd24m, PerCompanyAboveAvgIps60m };

        /// <summary>The window of sales of <see cref="TopSellingHighEnd24m"/>.</summary>
        public const int HighEndSalesMonths = 24;

        /// <summary>How recent a CPU of <see cref="PerCompanyAboveAvgIps60m"/> must be.</summary>
        public const int RecentMonths = 60;

        /// <summary>The window of sales that orders the candidates of <see cref="PerCompanyAboveAvgIps60m"/> (the one that sells most is the one the flaw hurts most).</summary>
        public const int RankingSalesMonths = 12;
    }

    /// <summary>The severities of a forced flaw: the names of the effect, mapped by the game side to <c>FlawSeverity</c> of design-flaw-recalls.</summary>
    public static class FlawSeverities
    {
        public const string Minor = "Minor";
        public const string Major = "Major";
        public const string Critical = "Critical";

        /// <summary>From the lightest to the heaviest; the index is the code kept in the numbers of the applied effect.</summary>
        public static readonly string[] All = { Minor, Major, Critical };

        public static int CodeOf(string severity)
        {
            int index = Array.IndexOf(All, severity);
            return index < 0 ? 1 : index;
        }

        public static string FromCode(int code)
        {
            return code >= 0 && code < All.Length ? All[code] : Major;
        }
    }

    /// <summary>How a forced flaw comes to light: the names of the effect, mapped by the game side to <c>DiscoverySource</c> of design-flaw-recalls.</summary>
    public static class FlawDiscoveries
    {
        /// <summary>Press and buyers find it: the flaw is public at once and the window of the decision opens for the player.</summary>
        public const string External = "External";

        /// <summary>The company's own engineers find it.</summary>
        public const string Internal = "Internal";

        /// <summary>The caller has no better word (design-flaw-recalls' own <c>Api</c> source).</summary>
        public const string Api = "Api";

        public static readonly string[] All = { External, Internal, Api };

        public static int CodeOf(string discovery)
        {
            int index = Array.IndexOf(All, discovery);
            return index < 0 ? 0 : index;
        }

        public static string FromCode(int code)
        {
            return code >= 0 && code < All.Length ? All[code] : External;
        }
    }

    /// <summary>The built-in flaw types by name, only to show them (the code of a type in the numbers of the applied effect). Others are possible: a code outside the list.</summary>
    public static class FlawTypeNames
    {
        public static readonly string[] BuiltIn = { "arith", "stability", "thermal", "security" };

        /// <summary>The index of a built-in type; <c>BuiltIn.Length</c> for any other.</summary>
        public static int CodeOf(string typeId)
        {
            int index = Array.IndexOf(BuiltIn, typeId);
            return index < 0 ? BuiltIn.Length : index;
        }

        /// <summary>The type of a code; empty for the code of "other".</summary>
        public static string FromCode(int code)
        {
            return code >= 0 && code < BuiltIn.Length ? BuiltIn[code] : string.Empty;
        }
    }

    /// <summary>
    /// The members of the effect <c>ForceDesignFlaw</c> (design.md, Component 8; FR-12): <c>pick</c>, <c>typeId</c>, <c>severityLeader</c>, <c>severityOthers</c>, <c>discovery</c>,
    /// <c>maxPerCompany</c> and <c>fallbackEventId</c>, plus <c>leaderMonths</c> (the window that decides who the leader of HighEnd is, 24 months like the roles of the entries).
    /// <c>severityOthers</c> reads as <c>severityLeader</c> when the effect does not name it: an effect for one company needs only one severity.
    /// </summary>
    public sealed class ForceFlawParams
    {
        public string Pick = FlawPicks.TopSellingHighEnd24m;
        public string TypeId = string.Empty;
        public string SeverityLeader = FlawSeverities.Major;
        public string SeverityOthers = FlawSeverities.Major;
        public string Discovery = FlawDiscoveries.External;
        public int MaxPerCompany = 1;
        public int LeaderMonths = 24;
        public string FallbackEventId = string.Empty;

        /// <summary>Reads the members; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static ForceFlawParams Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "ForceDesignFlaw", problems);
            var p = new ForceFlawParams();

            p.Pick = r.Choice("pick", p.Pick, FlawPicks.All);

            string typeId = r.Str("typeId", string.Empty);
            if (string.IsNullOrEmpty(typeId)) r.Fail("typeId", "is required (the id of a flaw type of design-flaw-recalls)");
            else if (!IsName(typeId, 40)) r.Fail("typeId", "\"" + typeId + "\" is not an id (letters, digits, '.', '_' and '-', up to 40)");
            else p.TypeId = typeId;

            p.SeverityLeader = r.Choice("severityLeader", p.SeverityLeader, FlawSeverities.All);
            p.SeverityOthers = r.Has("severityOthers") ? r.Choice("severityOthers", p.SeverityLeader, FlawSeverities.All) : p.SeverityLeader;
            p.Discovery = r.Choice("discovery", p.Discovery, FlawDiscoveries.All);
            p.MaxPerCompany = r.Int("maxPerCompany", p.MaxPerCompany, 1, 5);
            p.LeaderMonths = r.Int("leaderMonths", p.LeaderMonths, 1, CalendarValidator.MaxMonths);

            string fallback = r.Str("fallbackEventId", string.Empty);
            if (!string.IsNullOrEmpty(fallback))
            {
                if (!fallback.StartsWith(HistIds.Prefix, StringComparison.Ordinal) || !IsName(fallback, 100))
                    r.Fail("fallbackEventId", "\"" + fallback + "\" is not an event id of this feature (it starts with \"" + HistIds.Prefix + "\")");
                else
                    p.FallbackEventId = fallback;
            }

            r.WarnUnknown();
            return p;
        }

        /// <summary>Letters, digits, '.', '_' and '-' only: an id that can sit in a log line and a key of the save.</summary>
        internal static bool IsName(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length > maxLength) return false;
            foreach (char ch in text)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-';
                if (!ok) return false;
            }
            return true;
        }
    }

    /// <summary>The choice of the CPUs of a company that a flaw is forced on, without the game.</summary>
    public static class FlawPicker
    {
        /// <summary>
        /// The CPUs of the company the rule picks, the best first, at most <paramref name="max"/>. Only released CPUs that are not retired are candidates. A tie goes to the faster
        /// CPU (<c>Ips</c>), then to the smaller <c>SaveId</c>, so the answer never depends on the order of the list.
        /// <list type="bullet">
        /// <item><see cref="FlawPicks.TopSellingHighEnd24m"/>: candidates sold in HighEnd in the last 24 full months; the most sold first.</item>
        /// <item><see cref="FlawPicks.PerCompanyAboveAvgIps60m"/>: candidates designed within the last 60 months with <c>Ips</c> at or above the market average; the best seller of
        /// the last 12 full months first (a CPU that sold nothing is still a candidate: the flaw is in the design).</item>
        /// </list>
        /// </summary>
        public static List<int> Pick(IReadOnlyList<CpuInfo> cpus, string companyKey, string rule, SimpleDate today, float averageIps, int max)
        {
            var picked = new List<int>();
            if (cpus == null || string.IsNullOrEmpty(companyKey) || max <= 0) return picked;

            var candidates = new List<Candidate>();
            for (int i = 0; i < cpus.Count; i++)
            {
                CpuInfo cpu = cpus[i];
                if (cpu == null || !cpu.Released || cpu.Retired || !string.Equals(cpu.CompanyKey, companyKey, StringComparison.Ordinal)) continue;

                if (string.Equals(rule, FlawPicks.TopSellingHighEnd24m, StringComparison.Ordinal))
                {
                    long sold = RecentSales(cpu.SoldByHighEnd, FlawPicks.HighEndSalesMonths);
                    if (sold > 0) candidates.Add(new Candidate(cpu, sold));
                }
                else if (string.Equals(rule, FlawPicks.PerCompanyAboveAvgIps60m, StringComparison.Ordinal))
                {
                    if (!(cpu.Ips >= averageIps) || !WorldMath.IsRecent(cpu, today, FlawPicks.RecentMonths)) continue;
                    long sold = RecentSales(cpu.Sold, FlawPicks.RankingSalesMonths) + RecentSales(cpu.SoldByContracts, FlawPicks.RankingSalesMonths);
                    candidates.Add(new Candidate(cpu, sold));
                }
            }

            candidates.Sort((a, b) =>
            {
                int bySales = b.Sold.CompareTo(a.Sold);
                if (bySales != 0) return bySales;
                int byIps = b.Cpu.Ips.CompareTo(a.Cpu.Ips);
                return byIps != 0 ? byIps : a.Cpu.SaveId.CompareTo(b.Cpu.SaveId);
            });

            for (int i = 0; i < candidates.Count && picked.Count < max; i++) picked.Add(candidates[i].Cpu.SaveId);
            return picked;
        }

        /// <summary>
        /// The units in the last <paramref name="months"/> full months of a monthly journal: the indices <c>Count-1-months .. Count-2</c> (the last element is the month that is not
        /// over, as everywhere in the feature). 0 for a missing journal.
        /// </summary>
        public static long RecentSales(IReadOnlyList<int> journal, int months)
        {
            if (journal == null || months <= 0) return 0L;

            long sum = 0;
            int last = journal.Count - 2;
            int first = Math.Max(0, journal.Count - 1 - months);
            for (int i = first; i <= last; i++) sum += journal[i];
            return sum;
        }

        private struct Candidate
        {
            public readonly CpuInfo Cpu;
            public readonly long Sold;

            public Candidate(CpuInfo cpu, long sold)
            {
                Cpu = cpu;
                Sold = sold;
            }
        }
    }

    /// <summary>The door to the flaw mechanic of design-flaw-recalls: in the game <c>GameFlawGate</c> (<c>FlawRegistry.ForceFlaw</c>), in the tests a recording fake.</summary>
    public interface IFlawGate
    {
        /// <summary>The feature is enabled and its store is attached: <see cref="Force"/> does real work. False is not an error: the effect then uses its fallback.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Creates the flaw of the CPU and starts its discovery (<c>FlawRegistry.ForceFlaw</c>). The names are those of <see cref="FlawSeverities"/> and
        /// <see cref="FlawDiscoveries"/>. False: the CPU cannot get a flaw (or the feature is off).
        /// </summary>
        bool Force(int cpuSaveId, string typeId, string severity, string discovery);
    }

    /// <summary>What <see cref="FlawBridgeCore.Plan"/> works out for one company: who gets the flaw, how bad, and whether the mechanic is there.</summary>
    public sealed class FlawPlan
    {
        /// <summary>The <c>SaveId</c>s the rule picked, the first is the one that matters most.</summary>
        public List<int> Cpus = new List<int>();

        /// <summary>The company leads HighEnd (the severity of the leader applies).</summary>
        public bool IsLeader;

        /// <summary>The severity (<see cref="FlawSeverities"/>).</summary>
        public string Severity = FlawSeverities.Major;

        /// <summary>design-flaw-recalls is enabled now.</summary>
        public bool RegistryAvailable;

        /// <summary>The flaw cannot be forced (no mechanic, or no CPU to force it on): the fallback event is what happens.</summary>
        public bool NeedsFallback
        {
            get { return !RegistryAvailable || Cpus.Count == 0; }
        }
    }

    /// <summary>What <see cref="FlawBridgeCore.Run"/> did for one company.</summary>
    public sealed class FlawResult
    {
        public FlawPlan Plan;

        /// <summary>How many CPUs the flaw mechanic accepted.</summary>
        public int Forced;

        /// <summary>The fallback event was asked for (nothing was forced and the effect names one).</summary>
        public bool FallbackAsked;

        /// <summary>The answer of the event system to the fallback; meaningful when <see cref="FallbackAsked"/>.</summary>
        public FireResult Fallback;
    }

    /// <summary>
    /// The bridge to the flaw mechanic (design.md, Component 8; FR-12): the rules that pick the CPU of a company, the severity (the leader of HighEnd gets <c>severityLeader</c>, everybody else
    /// <c>severityOthers</c>), the call of <see cref="IFlawGate.Force"/> for each picked CPU and, when nothing was forced - the mechanic is off, or the company has no CPU for the rule -
    /// the <c>fallbackEventId</c> fired for the same company the next day (<see cref="FallbackDelayDays"/>: a firing is not started from inside the application of another event).
    /// The window of the decision, the press and the recall are design-flaw-recalls' own.
    /// </summary>
    public sealed class FlawBridgeCore
    {
        /// <summary>The fallback event is fired this many days later.</summary>
        public const int FallbackDelayDays = 1;

        private readonly IWorldView _world;
        private readonly Func<int, string> _leaderOf;
        private readonly IFlawGate _gate;
        private readonly IDelayedEventFirer _firer;
        private readonly Action<string> _log;

        /// <param name="leaderOf">The key of the company that leads HighEnd over the last N months (the best share, ties as everywhere); null when there is none.</param>
        public FlawBridgeCore(IWorldView world, Func<int, string> leaderOf, IFlawGate gate, IDelayedEventFirer firer, Action<string> log = null)
        {
            if (world == null) throw new ArgumentNullException("world");
            if (leaderOf == null) throw new ArgumentNullException("leaderOf");
            if (gate == null) throw new ArgumentNullException("gate");
            if (firer == null) throw new ArgumentNullException("firer");
            _world = world;
            _leaderOf = leaderOf;
            _gate = gate;
            _firer = firer;
            _log = log;
        }

        /// <summary>What would be forced for this company now. Changes nothing.</summary>
        public FlawPlan Plan(ForceFlawParams p, string companyKey)
        {
            if (p == null) throw new ArgumentNullException("p");

            string leader = _leaderOf(p.LeaderMonths);
            bool isLeader = !string.IsNullOrEmpty(leader) && string.Equals(leader, companyKey, StringComparison.Ordinal);
            return new FlawPlan
            {
                IsLeader = isLeader,
                Severity = isLeader ? p.SeverityLeader : p.SeverityOthers,
                RegistryAvailable = _gate.IsAvailable,
                Cpus = FlawPicker.Pick(_world.Cpus, companyKey, p.Pick, _world.Today, _world.AverageIps, p.MaxPerCompany),
            };
        }

        /// <summary>
        /// Forces the flaw on the CPUs of the company, or fires the fallback. <paramref name="args"/> are the positional arguments of the firing that reached the effect (the fallback
        /// event is told the same ones); <paramref name="ownEventId"/> is the event of the effect (a fallback that names it is refused: it would repeat itself).
        /// </summary>
        public FlawResult Run(ForceFlawParams p, string companyKey, string[] args, string ownEventId)
        {
            FlawPlan plan = Plan(p, companyKey);
            var result = new FlawResult { Plan = plan };

            if (plan.RegistryAvailable)
            {
                foreach (int cpuSaveId in plan.Cpus)
                {
                    if (Force(cpuSaveId, p, plan.Severity)) result.Forced++;
                }
            }

            if (result.Forced > 0) return result;

            string why = !plan.RegistryAvailable ? "the flaw mechanic (design-flaw-recalls) is off" : plan.Cpus.Count == 0 ? "the company has no CPU for the rule " + p.Pick : "the flaw mechanic refused every CPU";
            if (string.IsNullOrEmpty(p.FallbackEventId))
            {
                Log("ForceDesignFlaw for " + companyKey + ": " + why + " and the effect names no fallbackEventId; nothing happens");
                return result;
            }
            if (string.Equals(p.FallbackEventId, ownEventId, StringComparison.Ordinal))
            {
                Log("ForceDesignFlaw for " + companyKey + ": " + why + ", but the fallbackEventId is the event itself; nothing happens");
                return result;
            }

            result.FallbackAsked = true;
            result.Fallback = _firer.FireLater(p.FallbackEventId, companyKey, FallbackDelayDays, args ?? new string[0]);
            if (result.Fallback == FireResult.UnknownEvent) Log("ForceDesignFlaw for " + companyKey + ": the fallback event " + p.FallbackEventId + " is not in the pack");
            return result;
        }

        private bool Force(int cpuSaveId, ForceFlawParams p, string severity)
        {
            try
            {
                return _gate.Force(cpuSaveId, p.TypeId, severity, p.Discovery);
            }
            catch (Exception ex)
            {
                Log("ForceDesignFlaw: the flaw mechanic failed for cpu " + cpuSaveId + " (" + ex.Message + ")");
                return false;
            }
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
