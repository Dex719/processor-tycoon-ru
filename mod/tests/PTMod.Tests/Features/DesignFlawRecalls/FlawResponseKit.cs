using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// A scripted <see cref="IFlawResponseHost"/>: the day and the numbers are set by the test, every effect on the game (money, reputation, popularity,
    /// contracts, announcements) is recorded in the order it happened. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeResponseHost : IFlawResponseHost
    {
        public int Day = FlawCycleKit.Day1990 + 100;
        public FlawBalance Balance_ = FlawBalance.Defaults();
        public IModRng Rng_ = new FakeRng(0.5f);

        public sealed class Spending
        {
            public int RecordId;
            public float Amount;
            public FlawSpendKind Kind;
        }

        public sealed class ReputationChange
        {
            public int RecordId;
            public int Delta;
            public string Reason;
        }

        public readonly List<Spending> Spent = new List<Spending>();
        public readonly List<ReputationChange> Reputation = new List<ReputationChange>();
        public readonly List<float> PopularityHits = new List<float>();
        public readonly List<float> CancelChances = new List<float>();
        public readonly List<string> Announced = new List<string>();
        public readonly List<string> Logs = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>How many contracts <see cref="CancelContracts"/> says it ended.</summary>
        public int ContractsEnded;

        public bool ThrowOnSpend;
        public bool ThrowOnReputation;
        public bool ThrowOnPopularity;

        public int Today
        {
            get { return Day; }
        }

        public FlawBalance Balance
        {
            get { return Balance_; }
        }

        public IModRng Rng
        {
            get { return Rng_; }
        }

        public void Spend(FlawRecord record, float amount, FlawSpendKind kind)
        {
            if (ThrowOnSpend) throw new InvalidOperationException("the owner cannot pay");
            Spent.Add(new Spending { RecordId = record.Id, Amount = amount, Kind = kind });
        }

        public void AddReputation(FlawRecord record, int delta, string reasonKey)
        {
            if (ThrowOnReputation) throw new InvalidOperationException("the reputation is not there");
            Reputation.Add(new ReputationChange { RecordId = record.Id, Delta = delta, Reason = reasonKey });
        }

        public void HitPopularity(FlawRecord record, float hit)
        {
            if (ThrowOnPopularity) throw new InvalidOperationException("the popularity is not there");
            PopularityHits.Add(hit);
        }

        public int CancelContracts(FlawRecord record, float chance)
        {
            CancelChances.Add(chance);
            return ContractsEnded;
        }

        public void StateChanged(FlawRecord record, FlawState previous)
        {
            Announced.Add("#" + record.Id + ":" + previous + ">" + record.State + "/" + record.Response);
        }

        public void Log(string line)
        {
            Logs.Add(line);
        }

        public void Warn(string line)
        {
            Warnings.Add(line);
        }

        public void Error(string line, Exception exception)
        {
            Errors.Add(line);
        }

        /// <summary>The money taken, summed over the spendings of one kind.</summary>
        public float Total(FlawSpendKind kind)
        {
            float total = 0f;
            foreach (Spending s in Spent)
            {
                if (s.Kind == kind) total += s.Amount;
            }
            return total;
        }

        /// <summary>The sum of the reputation changes with a reason (all of them when the reason is <c>null</c>).</summary>
        public int ReputationSum(string reason = null)
        {
            int sum = 0;
            foreach (ReputationChange c in Reputation)
            {
                if (reason == null || c.Reason == reason) sum += c.Delta;
            }
            return sum;
        }
    }

    /// <summary>
    /// A fake of <c>ModModifiers</c> (<see cref="IFlawModifierGateway"/>): it hands out ids, knows the term of every entry on a calendar of its own and answers
    /// the question "what applies" with the real rules of <see cref="ModifierScope"/>. It counts the calls, so a test can see that nothing was made twice and that
    /// nothing was removed. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeModifierGateway : IFlawModifierGateway
    {
        public sealed class Entry
        {
            public string Id;
            public string Channel;
            public ModifierScope Scope;
            public float Multiplier;
            public int DurationDays;
            public int CreatedDay;
            public string ReasonKey;
            public string[] ReasonArgs;

            /// <summary>The first day the entry no longer applies.</summary>
            public int EndDay
            {
                get { return DurationDays > 0 ? CreatedDay + DurationDays : int.MaxValue; }
            }
        }

        private int _next = 1;

        /// <summary>The day of the fake calendar (what <c>ModTicks.Today</c> is for the real core).</summary>
        public int Today = FlawCycleKit.Day1990 + 100;

        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public readonly Dictionary<int, string> CpuNames = new Dictionary<int, string>();
        public int AddCount;
        public int RemoveCount;
        public bool RefuseAdd;
        public bool ThrowOnRemove;

        public string Add(string channel, ModifierScope scope, float multiplier, int durationDays, string reasonKey, string[] reasonArgs)
        {
            AddCount++;
            if (RefuseAdd) return null;

            string id = "m-" + (_next++).ToString("000000");
            Entries[id] = new Entry
            {
                Id = id,
                Channel = channel,
                Scope = scope,
                Multiplier = multiplier,
                DurationDays = durationDays,
                CreatedDay = Today,
                ReasonKey = reasonKey,
                ReasonArgs = reasonArgs,
            };
            return id;
        }

        public bool Remove(string entryId)
        {
            RemoveCount++;
            if (ThrowOnRemove) throw new InvalidOperationException("the core cannot remove");
            return Entries.Remove(entryId);
        }

        public IList<FlawLiveEntry> LiveEntries(string channel, string companyKey, int cpuSaveId)
        {
            var live = new List<FlawLiveEntry>();
            var query = new ModifierQuery(companyKey, cpuSaveId < 0 ? ModifierQuery.None : cpuSaveId);
            foreach (Entry e in Entries.Values)
            {
                if (e.Channel != channel || Today >= e.EndDay || !e.Scope.Matches(query)) continue;
                live.Add(new FlawLiveEntry(e.Id, e.Channel, e.ReasonKey));
            }
            return live;
        }

        public string CpuName(int cpuSaveId)
        {
            string name;
            return CpuNames.TryGetValue(cpuSaveId, out name) ? name : null;
        }

        /// <summary>The entries on a channel, in the order of their ids.</summary>
        public List<Entry> On(string channel)
        {
            var list = new List<Entry>();
            foreach (Entry e in Entries.Values)
            {
                if (e.Channel == channel) list.Add(e);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        /// <summary>The number of entries with this reason.</summary>
        public int CountReason(string reasonKey)
        {
            int count = 0;
            foreach (Entry e in Entries.Values)
            {
                if (e.ReasonKey == reasonKey) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// The pieces of the reactions wired together over a store, as the feature wires them: the fake core, the channel book, the response engine and its fake
    /// host. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class ResponseWorld
    {
        public readonly FlawStore Store;
        public readonly FakeModifierGateway Gateway = new FakeModifierGateway();
        public readonly FakeResponseHost Host = new FakeResponseHost();
        public readonly List<string> ChannelWarnings = new List<string>();
        public readonly FlawChannelBook Book;
        public readonly FlawResponseEngine Engine;

        public ResponseWorld(params FlawRecord[] records)
        {
            Store = FlawCycleKit.Store(records);
            Book = new FlawChannelBook(Store, Gateway, ChannelWarnings.Add);
            Engine = new FlawResponseEngine(Book, Host);
            SetDay(Host.Day);
        }

        /// <summary>Moves the calendar of the fake host and of the fake core to the same day.</summary>
        public void SetDay(int day)
        {
            Host.Day = day;
            Gateway.Today = day;
        }

        /// <summary>The 1994 example of design.md, formula 4 (Major): the quote of the CPU of the example, computed by the real calculator.</summary>
        public static FlawQuote ExampleQuote(FlawSeverity severity = FlawSeverity.Major, string typeId = "arith", int year = 1994, bool quiet = false, float revenue12m = 0f)
        {
            FlawBalance b = FlawBalance.Defaults();
            return ResponseCostCalculator.Quote(new QuoteInput
            {
                Severity = severity,
                Type = b.FindType(typeId),
                AffectedUnits = 2000000,
                StockUnits = 0,
                RealUnitCost = 60f,
                Price = 400f,
                DevelopmentCost = 80000000f,
                InflationNow = 3.0f,
                InflationAtRelease = 2.8f,
                Revenue12m = revenue12m,
                Year = year,
                Quiet = quiet,
            }, b);
        }

        /// <summary>A record in <paramref name="state"/> with the example quote saved in it (the owner is the player, the CPUs 100 and 101).</summary>
        public static FlawRecord Known(FlawState state, int id = 1, FlawSeverity severity = FlawSeverity.Major, string typeId = "arith", string company = "player",
            int year = 1994, bool coverUp = false)
        {
            FlawRecord record = FlawCycleKit.Record(state, id, baseCpu: 100, company: company, severity: severity);
            record.Data.typeId = typeId;
            record.Data.cpuIds = new List<int> { 100, 101 };
            record.Data.quote = ExampleQuote(severity, typeId, year, state == FlawState.InternalKnown).ToDto();
            record.Data.coverUp = coverUp;
            record.Data.discoveredDay = FlawCycleKit.Day1990 + 90;
            if (state == FlawState.Public)
            {
                record.Data.publicSinceDay = FlawCycleKit.Day1990 + 90;
                record.Source = DiscoverySource.External;
            }
            else if (state == FlawState.InternalKnown)
            {
                record.Source = DiscoverySource.Internal;
            }
            return record;
        }
    }

    /// <summary>A plain formatter whose output shows the number it was given: <c>$66000000</c>, <c>12</c> (a percent without the sign), <c>2000000</c>.</summary>
    internal sealed class TestFormat : IFlawFormat
    {
        public static readonly TestFormat Instance = new TestFormat();

        public string Money(float amount)
        {
            return "$" + Math.Round(amount).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public string Count(long units)
        {
            return units.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public string Percent(float fraction)
        {
            return Math.Round(fraction * 100f, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The cycle host the feature has (<c>FlawMonitor</c>), with the game replaced by the fakes of <see cref="ResponseWorld"/>: the money of a revision goes to the
    /// fake host as a <c>Development</c> expense, and the consequences of the cycle (discovery, leak, end of a revision or a patch, drain, lawsuit) go to the response
    /// engine, as <c>FlawMonitor</c> sends them to <c>FlawResponseApplier</c>. The facts about the CPUs are set by the test.
    /// </summary>
    internal sealed class WiredCycleHost : IFlawCycleHost
    {
        private readonly ResponseWorld _world;

        public IModRng Rng_ = FakeRng.Always(0.5f);
        public readonly Dictionary<int, FlawFacts> Facts = new Dictionary<int, FlawFacts>();
        public readonly HashSet<string> DeadOwners = new HashSet<string>();
        public readonly List<string> Events = new List<string>();

        public WiredCycleHost(ResponseWorld world)
        {
            _world = world;
        }

        public int Today
        {
            get { return _world.Host.Day; }
        }

        public FlawBalance Balance
        {
            get { return _world.Host.Balance; }
        }

        public IModRng Rng
        {
            get { return Rng_; }
        }

        public void BeginPass()
        {
        }

        public void EndPass()
        {
        }

        public bool OwnerIsAlive(FlawRecord record)
        {
            return !DeadOwners.Contains(record.CompanyKey);
        }

        public FlawFacts ReadFacts(FlawRecord record, bool forQuote)
        {
            FlawFacts facts;
            return Facts.TryGetValue(record.BaseCpuId, out facts) ? facts : new FlawFacts { Found = false };
        }

        public void ChargeStepping(FlawRecord record, float amount)
        {
            _world.Host.Spend(record, amount, FlawSpendKind.Development);
        }

        public void Discovered(FlawRecord record)
        {
            Events.Add("discovered#" + record.Id);
            _world.Engine.OnDiscovered(record);
        }

        public void Leaked(FlawRecord record)
        {
            Events.Add("leaked#" + record.Id);
            _world.Engine.OnLeak(record);
        }

        public void SteppingCompleted(FlawRecord record)
        {
            Events.Add("steppingCompleted#" + record.Id);
            _world.Engine.CompleteStepping(record);
        }

        public void MitigationExpired(FlawRecord record)
        {
            Events.Add("mitigationExpired#" + record.Id);
            _world.Engine.ExpireMitigation(record);
        }

        public void IgnoreDrain(FlawRecord record)
        {
            Events.Add("drain#" + record.Id);
            _world.Engine.IgnoreDrain(record);
        }

        public void ClassActionFiled(FlawRecord record)
        {
            Events.Add("suit#" + record.Id);
            _world.Engine.ClassActionFiled(record);
        }

        public void StateChanged(FlawRecord record, FlawState previous)
        {
            Events.Add("state#" + record.Id + ":" + previous + ">" + record.State);
        }

        public void Log(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
            _world.Host.Errors.Add(message);
        }

        /// <summary>The facts of the CPU of the 1994 example of design.md: 2 000 000 units sold, 60 of unit cost, a price of 400, a development cost of 80 million.</summary>
        public static FlawFacts ExampleFacts()
        {
            return new FlawFacts
            {
                Found = true,
                Units = 2000000L,
                UnitsInNoSalesWindow = 1000000L,
                RealUnitCost = 60f,
                Price = 400f,
                DevelopmentCost = 80000000f,
                Inflation = 3.0f,
                Revenue12m = 0f,
                Year = 1994,
            };
        }
    }
}
