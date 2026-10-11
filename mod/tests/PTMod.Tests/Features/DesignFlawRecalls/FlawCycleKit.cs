using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// Helpers for the tests of the records and of the monthly and daily cycle: records in a given state, and a scripted host that stands for the game.
    /// Internal, so the runner never discovers it.
    /// </summary>
    internal static class FlawCycleKit
    {
        /// <summary>A day number that is well after the start of the calendar: 1990-01-01.</summary>
        public static readonly int Day1990 = FlawDays.FromDate(new DateTime(1990, 1, 1));

        /// <summary>A record in the given state with the usual fields filled (the owner is the player, one CPU, a Major <c>arith</c> flaw).</summary>
        public static FlawRecord Record(FlawState state, int id = 1, int baseCpu = 100, string company = "player", FlawSeverity severity = FlawSeverity.Major,
            FlawResponse response = FlawResponse.None, int releaseDay = 0)
        {
            return new FlawRecord(new FlawRecordDto
            {
                id = id,
                companyKey = company,
                baseCpuId = baseCpu,
                cpuIds = new List<int> { baseCpu },
                typeId = "arith",
                severity = (int)severity,
                state = (int)state,
                response = (int)response,
                riskAtRelease = 0.1f,
                inflationAtRelease = 1.0f,
                releaseDay = releaseDay > 0 ? releaseDay : Day1990,
            });
        }

        /// <summary>A store over fresh data with the given records added to it.</summary>
        public static FlawStore Store(params FlawRecord[] records)
        {
            var data = new FlawSaveData();
            foreach (FlawRecord record in records) data.records.Add(record.Data);
            var store = new FlawStore(() => data);
            return store;
        }

        /// <summary>The first day of the month <paramref name="monthsAfter"/> months after 1990-01-01 (the day of a monthly tick).</summary>
        public static int MonthStart(int monthsAfter)
        {
            return FlawDays.FromDate(new DateTime(1990, 1, 1).AddMonths(monthsAfter));
        }
    }

    /// <summary>
    /// A scripted <see cref="IFlawCycleHost"/>: the day, the random numbers and the facts about the CPUs are set by the test; every effect is counted and
    /// listed in <see cref="Events"/> in the order it happened.
    /// </summary>
    internal sealed class FakeCycleHost : IFlawCycleHost
    {
        public int Day;
        public FlawBalance Balance_ = FlawBalance.Defaults();
        public IModRng Rng_ = new FakeRng(0.5f);

        /// <summary>The facts per base CPU id; a missing entry means "the CPU is not found".</summary>
        public readonly Dictionary<int, FlawFacts> Facts = new Dictionary<int, FlawFacts>();

        /// <summary>Companies that are gone or bankrupt.</summary>
        public readonly HashSet<string> DeadOwners = new HashSet<string>();

        /// <summary>Record ids for which <see cref="ReadFacts"/> throws.</summary>
        public readonly HashSet<int> ThrowOnRead = new HashSet<int>();

        /// <summary>Set to make <see cref="ChargeStepping"/> throw.</summary>
        public bool ThrowOnCharge;

        public readonly List<string> Events = new List<string>();
        public readonly List<string> Logs = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public float Charged;
        public int Passes;

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

        public void BeginPass()
        {
            Passes++;
        }

        public void EndPass()
        {
            Events.Add("endpass");
        }

        public bool OwnerIsAlive(FlawRecord record)
        {
            return !DeadOwners.Contains(record.CompanyKey);
        }

        public FlawFacts ReadFacts(FlawRecord record, bool forQuote)
        {
            if (ThrowOnRead.Contains(record.Id)) throw new InvalidOperationException("the facts of record " + record.Id + " cannot be read");

            FlawFacts facts;
            if (!Facts.TryGetValue(record.BaseCpuId, out facts)) return new FlawFacts { Found = false };
            return facts;
        }

        public void ChargeStepping(FlawRecord record, float amount)
        {
            if (ThrowOnCharge) throw new InvalidOperationException("the charge failed");
            Charged += amount;
            Events.Add("charge#" + record.Id + ":" + (long)Math.Round(amount));
        }

        public void Discovered(FlawRecord record)
        {
            Events.Add("discovered#" + record.Id);
        }

        public void Leaked(FlawRecord record)
        {
            Events.Add("leaked#" + record.Id);
        }

        public void SteppingCompleted(FlawRecord record)
        {
            Events.Add("steppingCompleted#" + record.Id);
        }

        public void MitigationExpired(FlawRecord record)
        {
            Events.Add("mitigationExpired#" + record.Id);
        }

        public void IgnoreDrain(FlawRecord record)
        {
            Events.Add("drain#" + record.Id);
        }

        public void ClassActionFiled(FlawRecord record)
        {
            Events.Add("suit#" + record.Id);
        }

        public void StateChanged(FlawRecord record, FlawState previous)
        {
            Events.Add("state#" + record.Id + ":" + previous + ">" + record.State);
        }

        public void Log(string message)
        {
            Logs.Add(message);
        }

        public void Error(string message, Exception exception)
        {
            Errors.Add(message);
        }

        public int Count(string prefix)
        {
            int count = 0;
            foreach (string e in Events)
            {
                if (e.StartsWith(prefix, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        /// <summary>A CPU that sold <paramref name="units"/> units, is still on the market and has sold something in the last two years.</summary>
        public static FlawFacts Selling(long units)
        {
            return new FlawFacts
            {
                Found = true,
                Units = units,
                UnitsInNoSalesWindow = units > 0 ? units / 2 + 1 : 0,
                EmptyMonths = 0,
                Retired = false,
                RealUnitCost = 60f,
                Price = 400f,
                DevelopmentCost = 80000000f,
                Inflation = 1.0f,
                Revenue12m = 1000000000f,
                Year = 1995,
            };
        }
    }
}
