using System;
using System.Collections.Generic;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// Builders shared by the tests of the pure logic of rival-acquisitions (TSK-005…TSK-009): the target and the player of the 1985 example of
    /// design.md ("Пример (1985, D ≈ 111,3 млн…)"), and small helpers. Internal, so the runner never discovers it as a test class.
    /// </summary>
    internal static class RivalTestData
    {
        /// <summary>The day of the example: all dates of the tests are relative to it.</summary>
        public static readonly DateTime Today = new DateTime(1985, 3, 1);

        public const int WaferOld = 46;      // 100 mm
        public const int WaferNew = 47;      // 150 mm
        public const int WaferSmall = 45;    // 75 mm
        public const double DebtLimit = 111000000.0;   // E_min = 27.75 million

        public static AcquisitionsBalance Balance()
        {
            return AcquisitionsBalance.Defaults();
        }

        public static PlayerSnapshot Player(double money = 5000000000.0)
        {
            var p = new PlayerSnapshot
            {
                SaveId = 1,
                Money = money,
                CreditLimit = 100000000.0,
                MarketShare = 0.20,
                IsCpuCompany = true,
                Capacity = 10,
                Today = Today,
                OperatingCostPerMonth = 3000000.0,
                OperatingCostPerLine = 358884.0,
            };
            p.Wafers.Add(Wafer(WaferSmall, 75, 0, 1.0));
            p.Wafers.Add(Wafer(WaferOld, 100, 10, 1.0));
            return p;
        }

        public static WaferLinesSnapshot Wafer(int id, double mm, int lines, double maintenance = 1.0, double saleCost = 0.0)
        {
            return new WaferLinesSnapshot { WaferId = id, Value = mm, Lines = lines, SaleCost = saleCost, MaintenanceMultiplier = maintenance };
        }

        public static TechSnapshot Tech(int id, double cost = 400000.0, double time = 600.0, int depth = 0, int type = 0)
        {
            return new TechSnapshot { Id = id, Name = "T" + id, Cost = cost, Time = time, Depth = depth, Type = type };
        }

        /// <summary>A CPU of the example: 120 million of income in 12 months at a margin of 40% (price 100, cost 60).</summary>
        public static CpuSnapshot Cpu(int saveId, int baseSaveId = -1, bool retired = false, double income = 120000000.0)
        {
            return new CpuSnapshot
            {
                SaveId = saveId,
                BaseSaveId = baseSaveId,
                Name = "C" + saveId,
                IsRetired = retired,
                MadeInHouseByTarget = true,
                Price = 100.0,
                RealUnitCost = 60.0,
                Income12 = income,
            };
        }

        /// <summary>
        /// The 1985 example: 40 lines on 100 mm (sale cost 147.2 million), three technologies of 8 million each, two CPUs of 48 million of margin income each (96 million),
        /// a market share of 8%. Healthy, with 30 million in cash. <c>E = 434.592 million</c>, <c>E_min = 27.75 million</c>.
        /// </summary>
        public static CompanySnapshot ExampleTarget()
        {
            var t = new CompanySnapshot
            {
                UniqueId = 3,
                SaveId = 412,
                Name = "Texas Instruments",
                Status = CompanyStatus.Healthy,
                Money = 30000000.0,
                CreditLimit = DebtLimit,
                ManufacturingDivisionValue = 147200000.0,
                MarketShare = 0.08,
            };
            t.Lines.Add(Wafer(WaferOld, 100, 40, 1.0, 147200000.0));
            t.TechGap.Add(Tech(100));
            t.TechGap.Add(Tech(101));
            t.TechGap.Add(Tech(102));
            t.Cpus.Add(Cpu(1201));
            t.Cpus.Add(Cpu(1207, 1201));
            return t;
        }

        /// <summary>A snapshot with only the numbers of the valuation: the factory sale value is <paramref name="enterprise"/> / kFab, nothing else.</summary>
        public static CompanySnapshot Plain(double enterprise, double cash, double creditLimit, CompanyStatus status = CompanyStatus.Healthy)
        {
            return new CompanySnapshot
            {
                UniqueId = 5,
                SaveId = 500,
                Name = "Plain",
                Status = status,
                Money = cash,
                CreditLimit = creditLimit,
                ManufacturingDivisionValue = enterprise / 2.0,   // kFab = 2
            };
        }

        public static AuctionData Auction(CompanySnapshot t, string until, bool closed = false)
        {
            return new AuctionData { targetUniqueId = t.UniqueId, targetSaveId = t.SaveId, bankruptOn = "1984-01-01", until = until, closed = closed };
        }

        public static StakeData Stake(CompanySnapshot t, double share)
        {
            return new StakeData { targetUniqueId = t.UniqueId, targetSaveId = t.SaveId, share = share, invested = 1000.0, since = "1984-01-01" };
        }

        public static ContractSnapshot Contract(int id, int provider, int client, bool exclusive = false, string providerName = "Foundry", string clientName = "Client")
        {
            return new ContractSnapshot
            {
                Id = id,
                ProviderId = provider,
                ClientId = client,
                ProviderName = providerName,
                ClientName = clientName,
                IsExclusive = exclusive,
                CapacityPercent = 30,
                MarkupPercent = 40,
                ProviderFines = 1000000.0,
                TerminatesOn = "1990-03",
            };
        }

        public static DealQuote Quote(DealType type, CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s = null, double channel = 1.0)
        {
            return DealRules.Quote(type, t, p, s ?? new AcquisitionsState(), Balance(), channel);
        }

        public static bool Has(List<string> list, string text)
        {
            return list.Contains(text);
        }

        public static bool HasNote(List<PlanNote> notes, string key)
        {
            foreach (PlanNote note in notes)
            {
                if (note.Key == key) return true;
            }
            return false;
        }
    }
}
