using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>One thing the fake economy was asked to do: a payment, an income, a reputation change or a note.</summary>
    internal sealed class Flow
    {
        public string Kind;       // spend, earn, rep, notify
        public string Company;
        public float Amount;
        public string Origin;     // the expense or income origin, or the key of a reputation reason or a note
        public string[] Args;

        public override string ToString()
        {
            return Kind + " " + Company + " " + Amount + " " + Origin;
        }
    }

    /// <summary>
    /// The books of a world of plain objects for the tests of the services: the clock, who is alive, what each company earns, and a record of every payment, income, reputation
    /// change and note in order. Not a test class and not a logic type.
    /// </summary>
    internal sealed class FakeEconomy : IHistEconomy
    {
        public readonly List<Flow> Flows = new List<Flow>();
        public readonly HashSet<string> Dead = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Unlucky = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Revenue = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Monthly = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>When set, a payment throws (to see how a service survives a failure).</summary>
        public bool ThrowOnSpend;

        public int Today { get; set; } = 14000;

        public bool IsAlive(string companyKey)
        {
            return CompanyKey.IsValid(companyKey) && !Dead.Contains(companyKey);
        }

        public string NameOf(string companyKey)
        {
            string name;
            return Names.TryGetValue(companyKey ?? string.Empty, out name) ? name : companyKey ?? string.Empty;
        }

        public float Revenue12m(string companyKey)
        {
            float value;
            return Revenue.TryGetValue(companyKey ?? string.Empty, out value) ? value : 0f;
        }

        public float MonthlyRevenue(string companyKey)
        {
            float value;
            return Monthly.TryGetValue(companyKey ?? string.Empty, out value) ? value : 0f;
        }

        public void Spend(string companyKey, float amount, HistExpense origin)
        {
            if (ThrowOnSpend) throw new InvalidOperationException("the books are closed");
            Flows.Add(new Flow { Kind = "spend", Company = companyKey, Amount = amount, Origin = origin.ToString() });
        }

        public void Earn(string companyKey, float amount, HistIncome origin)
        {
            Flows.Add(new Flow { Kind = "earn", Company = companyKey, Amount = amount, Origin = origin.ToString() });
        }

        public void AddReputation(string companyKey, float delta, string reasonKey)
        {
            Flows.Add(new Flow { Kind = "rep", Company = companyKey, Amount = delta, Origin = reasonKey });
        }

        public bool IsUnlucky(string companyKey)
        {
            return Unlucky.Contains(companyKey);
        }

        public void Notify(string companyKey, string key, params string[] args)
        {
            Flows.Add(new Flow { Kind = "notify", Company = companyKey, Origin = key, Args = args });
        }

        // ---- questions of the tests ----

        public float Spent(string company, string origin = null)
        {
            return Flows.Where(f => f.Kind == "spend" && f.Company == company && (origin == null || f.Origin == origin)).Sum(f => f.Amount);
        }

        public float Earned(string company, string origin = null)
        {
            return Flows.Where(f => f.Kind == "earn" && f.Company == company && (origin == null || f.Origin == origin)).Sum(f => f.Amount);
        }

        /// <summary>What the company lost in all: payments minus incomes.</summary>
        public float NetLoss(string company)
        {
            return Spent(company) - Earned(company);
        }

        public float Reputation(string company)
        {
            return Flows.Where(f => f.Kind == "rep" && f.Company == company).Sum(f => f.Amount);
        }

        public IEnumerable<Flow> Notes(string company)
        {
            return Flows.Where(f => f.Kind == "notify" && f.Company == company);
        }
    }

    /// <summary>The entries of <c>ModModifiers</c> a service asked for, and the invalidations.</summary>
    internal sealed class FakeModifiers : IHistModifiers
    {
        public sealed class Entry
        {
            public string Channel;
            public string Company;
            public float Multiplier;
            public int Days;
            public string Reason;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<string> Invalidated = new List<string>();

        public string Add(string channel, string companyKey, float multiplier, int days, string reasonKey, string[] reasonArgs)
        {
            Entries.Add(new Entry { Channel = channel, Company = companyKey, Multiplier = multiplier, Days = days, Reason = reasonKey });
            return "entry-" + Entries.Count;
        }

        public void Invalidate(string channel)
        {
            Invalidated.Add(channel);
        }

        public int InvalidationsOf(string channel)
        {
            return Invalidated.Count(c => c == channel);
        }
    }

    /// <summary>The door to <c>EventBus.Fire</c>: records the calls and answers what it is told to.</summary>
    internal sealed class FakeFirer : IDelayedEventFirer
    {
        public sealed class Call
        {
            public string EventId;
            public string Company;
            public string[] Args;
            public int Delay;
        }

        public readonly List<Call> Calls = new List<Call>();

        public FireResult Answer = FireResult.Fired;

        public FireResult Fire(string eventId, string companyKey, string[] args)
        {
            Calls.Add(new Call { EventId = eventId, Company = companyKey, Args = args });
            return Answer;
        }

        public FireResult FireLater(string eventId, string companyKey, int delayDays, params string[] args)
        {
            Calls.Add(new Call { EventId = eventId, Company = companyKey, Args = args, Delay = delayDays });
            return Answer == FireResult.Fired ? FireResult.Scheduled : Answer;
        }
    }

    /// <summary>A generator that gives the numbers it is told to, then repeats the last one.</summary>
    internal sealed class FixedRng : IModRng
    {
        private readonly Queue<float> _values;
        private float _last;

        public FixedRng(params float[] values)
        {
            _values = new Queue<float>(values);
            _last = values.Length > 0 ? values[values.Length - 1] : 0.5f;
        }

        public int Draws { get; private set; }

        public float Value()
        {
            Draws++;
            if (_values.Count > 0) _last = _values.Dequeue();
            return _last;
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            Draws++;
            return minInclusive;
        }

        public bool Chance(float probability)
        {
            return Value() < probability;
        }

        public int WeightedIndex(IList<float> weights)
        {
            Draws++;
            return weights == null || weights.Count == 0 ? -1 : 0;
        }

        public T Pick<T>(IList<T> items)
        {
            Draws++;
            return items == null || items.Count == 0 ? default(T) : items[0];
        }
    }
}
