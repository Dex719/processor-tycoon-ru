using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>How a synthetic company picks the socket of its next CPU.</summary>
    internal enum SimStyle
    {
        /// <summary>Moves to the standard socket of the newest generation <c>LagMonths</c> after its announcement.</summary>
        Follower,

        /// <summary>Owns a custom socket on the base of the newest generation and builds a new one every <c>CycleMonths</c>.</summary>
        Custom,

        /// <summary>Goes through the standard packages one after another, a new one every <c>CycleMonths</c>: the stress test of the penalty for changing a socket.</summary>
        Hopper,

        /// <summary>Stays on PGA 1985 for good.</summary>
        Laggard,
    }

    /// <summary>The running figures of one number: how often, the smallest, the largest, the sum.</summary>
    internal sealed class SimRange
    {
        public long Count;
        public double Min = double.MaxValue;
        public double Max = double.MinValue;
        public double Sum;

        public double Average
        {
            get { return Count == 0 ? 0 : Sum / Count; }
        }

        public void Add(double value)
        {
            Count++;
            Sum += value;
            if (value < Min) Min = value;
            if (value > Max) Max = value;
        }

        public override string ToString()
        {
            return Count == 0 ? "no data" : "min " + Min.ToString("0.0000", CultureInfo.InvariantCulture) + ", max " + Max.ToString("0.0000", CultureInfo.InvariantCulture)
                                           + ", avg " + Average.ToString("0.0000", CultureInfo.InvariantCulture) + " (" + Count + " values)";
        }
    }

    internal sealed class SimCpu
    {
        public int Id;
        public SimCompany Owner;
        public SocketId Socket;
        public GenerationDef Gen;
        public string Memory;
        public bool FullPlatform;
        public int ReleaseMonth;
        public int RetiredAt = int.MaxValue;   // the company stopped selling it (it left the platform)
        public int[] Sold;
        public int[] ByContracts;
    }

    internal sealed class SimCompany
    {
        public string Key;
        public SimStyle Style;
        public int Lag;
        public int Cycle;
        public double Weight;
        public int FoundedYear;
        public int Slot;         // the month of the year when it ships (0..11)
        public int Every = 12;   // months between two releases
        public bool Abandon;     // a move to a new socket takes the CPUs of the old one off the market at once
        public int Index;

        // The state of the plan.
        public bool HasSocket;
        public SocketId Socket;
        public GenerationDef Gen;
        public int SocketSince;
        public int NextCustomId;
        public int HopIndex = -1;
        public readonly List<SimCpu> Cpus = new List<SimCpu>();

        // The results.
        public int Releases;
        public int EarlyAdopterReleases;
        public int NewSockets;
        public int Penalties;
        public float WorstSeverity;
        public readonly SimRange Rating = new SimRange();
        public readonly SimRange Industries = new SimRange();
        public readonly SimRange Chance = new SimRange();
        public readonly SimRange Churn = new SimRange();
        public readonly SimRange EffShare = new SimRange();
        public readonly SimRange Bib = new SimRange();
    }

    /// <summary>The multipliers of one CPU in one month, kept so that the test of the combination with other features can replay them.</summary>
    internal struct SimSample
    {
        public int Month;
        public int CpuId;
        public string Company;
        public float Consumer;
        public float Industries;
    }

    /// <summary>
    /// The offline simulation of the balance (TSK-022; NFR-5, NFR-7): 14 synthetic companies play 1971-2030 month by month through the real models of the
    /// feature - the installed base from the monthly sales, the chronology of the generations (announcement, launch, early adopters, the first CPU on a
    /// socket and its penalty), the multipliers of the rating and of the signing chance with the preferences of the contracts - with no game. The sales
    /// are a plain open-loop scenario (the market grows 4.5 % a year, a company's share of it is its fixed weight, a CPU sells less as it ages, a fifth of
    /// the units go through contracts), so the figures show what the formulas give, not what a player would do. Deterministic: the same run every time.
    /// </summary>
    internal sealed class PlatformSimulator
    {
        public const int StartYear = 1971;
        public const int Months = 720;                 // 1971-01 ... 2030-12
        public const int CpuLifeMonths = 96;           // a CPU is on the market for eight years
        public const uint WorldSeed = 987654321u;
        public const int ContractsPerMonth = 6;
        public const int ContractsAskedPerMonth = 4;   // the contracts each CPU is asked about every month

        private readonly GenerationCatalog _catalog;
        private readonly PlatformBalance _balance;
        private readonly InstalledBaseLedger _ib = new InstalledBaseLedger();
        private readonly PlatformTimeline _timeline = new PlatformTimeline();
        private readonly PlatformEffectEngine _engine = new PlatformEffectEngine();
        private readonly OemPreferences _prefs = new OemPreferences();
        private readonly List<SimCompany> _companies = new List<SimCompany>();
        private readonly List<SimCpu> _cpus = new List<SimCpu>();
        private readonly List<GenAdoptionInput> _inputs = new List<GenAdoptionInput>();
        private readonly List<SimContract> _open = new List<SimContract>();
        private readonly List<string> _hopSequence = new List<string>();
        private int _nextCpuId = 1;

        private sealed class SimContract
        {
            public string Key;
            public int Type;
            public int Created;
        }

        public PlatformSimulator(GenerationCatalog catalog, PlatformBalance balance, string playerKey)
        {
            _catalog = catalog;
            _balance = balance;
            BuildCompanies(playerKey);
            BuildHopSequence();
        }

        // ---- the results ----

        public IReadOnlyList<SimCompany> Companies
        {
            get { return _companies; }
        }

        public InstalledBaseLedger Base
        {
            get { return _ib; }
        }

        public PlatformTimeline Timeline
        {
            get { return _timeline; }
        }

        public readonly SimRange Consumer = new SimRange();
        public readonly SimRange Industries = new SimRange();
        public readonly SimRange Chance = new SimRange();
        public readonly SimRange ChanceMarket = new SimRange();
        public readonly SimRange EffShare = new SimRange();
        public readonly List<SimSample> Samples = new List<SimSample>();

        /// <summary>How many answers of the signing chance came with each reason (null reason = no change).</summary>
        public readonly Dictionary<string, int> Reasons = new Dictionary<string, int>(StringComparer.Ordinal);

        public int CpusReleased
        {
            get { return _cpus.Count; }
        }

        public int ContractsMade { get; private set; }

        /// <summary>A fingerprint of every multiplier of the first company, month by month: equal for two runs that played the same.</summary>
        public ulong FirstCompanyFingerprint { get; private set; }

        // ---- the world ----

        private void BuildCompanies(string playerKey)
        {
            // key, style, lag, cycle, weight, founded, slot, every
            Add(playerKey, SimStyle.Follower, 6, 0, 8, 1971, 0, 12);
            Add("ai:1", SimStyle.Follower, 0, 0, 12, 1971, 1, 12);
            Add("ai:2", SimStyle.Follower, 3, 0, 10, 1971, 2, 12);
            Add("ai:3", SimStyle.Follower, 6, 0, 9, 1971, 3, 12);      // plays like the first company: the same plan, another key
            Add("ai:4", SimStyle.Follower, 12, 0, 7, 1975, 4, 12);
            Add("ai:5", SimStyle.Follower, 12, 0, 6, 1980, 5, 12);
            Add("ai:6", SimStyle.Follower, 24, 0, 5, 1980, 6, 12);
            Add("ai:7", SimStyle.Follower, 36, 0, 4, 1985, 7, 12);
            Add("ai:8", SimStyle.Custom, 0, 120, 55, 1980, 8, 12);     // the closed platform with a big share
            Add("ai:9", SimStyle.Custom, 0, 60, 6, 1975, 9, 12);       // its own socket on the newest base every five years, the old CPUs withdrawn
            Add("ai:10", SimStyle.Hopper, 0, 12, 14, 1990, 10, 6);     // a new socket every year, the old CPUs withdrawn; big enough (churnMinBaseShare) to be fined
            Add("ai:11", SimStyle.Laggard, 0, 0, 3, 1985, 11, 12);
            Add("ai:12", SimStyle.Follower, 6, 0, 4, 1995, 0, 12);     // founded late
            Add("ai:13", SimStyle.Follower, 18, 0, 6, 1990, 1, 12);
        }

        private void Add(string key, SimStyle style, int lag, int cycle, double weight, int founded, int slot, int every)
        {
            bool abandon = key == "ai:9" || key == "ai:10";
            _companies.Add(new SimCompany
            {
                Key = key,
                Style = style,
                Lag = lag,
                Cycle = cycle,
                Weight = weight,
                FoundedYear = founded,
                Slot = slot,
                Every = every,
                Abandon = abandon,
                Index = _companies.Count,
                NextCustomId = 100 + _companies.Count * 100,
            });
        }

        /// <summary>The standard packages of all the generations, in the order of the generations.</summary>
        private void BuildHopSequence()
        {
            IReadOnlyList<GenerationDef> generations = _catalog.Generations;
            for (int i = 0; i < generations.Count; i++)
            {
                foreach (string package in generations[i].sockets) _hopSequence.Add(package);
            }
        }

        // ---- the run ----

        public void Run()
        {
            ulong fingerprint = 14695981039346656037UL;
            for (int month = 0; month < Months; month++)
            {
                GameDate today = DateOf(month);
                if (today.Month == 1) AnnounceGenerations(today);

                Release(month, today);
                Sell(month);
                MakeContracts(month, today);
                _ib.ApplyMonth(Views(), month, _balance);

                GameDate evaluated = DateOf(month + 1);
                OemAdoptionModel.BuildInputs(_catalog, _timeline, _ib, evaluated, _inputs);
                fingerprint = Evaluate(month, evaluated, fingerprint);
            }
            FirstCompanyFingerprint = fingerprint;
        }

        private static GameDate DateOf(int month)
        {
            return new GameDate(StartYear + month / 12, 1 + month % 12, 1);
        }

        private void AnnounceGenerations(GameDate today)
        {
            foreach (GenerationDef gen in _catalog.Generations)
            {
                if (gen.announceYear == today.Year) _timeline.Announce(gen.id, today);
            }
        }

        // ---- releases ----

        private void Release(int month, GameDate today)
        {
            foreach (SimCompany company in _companies)
            {
                if (today.Year < company.FoundedYear || month % company.Every != company.Slot % company.Every) continue;

                SocketId socket;
                GenerationDef gen;
                if (!ChooseSocket(company, month, today, out socket, out gen)) continue;

                int id = _nextCpuId++;
                string memory = MemoryFor(gen, id);
                ReleaseOutcome outcome = _timeline.OnCpuReleased(company.Key, socket, gen, today, _ib, _catalog, _balance);

                company.Releases++;
                if (outcome.EarlyAdopter) company.EarlyAdopterReleases++;
                if (outcome.NewSocket) company.NewSockets++;
                if (outcome.NewSocket && outcome.ChurnSeverity > 0)
                {
                    company.Penalties++;
                    if (outcome.ChurnSeverity > company.WorstSeverity) company.WorstSeverity = outcome.ChurnSeverity;
                }

                var cpu = new SimCpu
                {
                    Id = id,
                    Owner = company,
                    Socket = socket,
                    Gen = gen,
                    Memory = memory,
                    FullPlatform = _catalog.IsFullPlatform(gen, memory),
                    ReleaseMonth = month,
                    Sold = new int[Months],
                    ByContracts = new int[Months],
                };
                company.Cpus.Add(cpu);
                _cpus.Add(cpu);
            }
        }

        /// <summary>Every third CPU of a company uses the old SDR memory (a platform that is not "full" from 2000 on), the rest the memory of the generation.</summary>
        private static string MemoryFor(GenerationDef gen, int cpuId)
        {
            if (cpuId % 3 == 0) return "SDR";
            return gen.memories[cpuId % gen.memories.Count];
        }

        /// <summary>The newest generation announced by <paramref name="today"/> plus the company's lag that is not obsolete; null when there is none.</summary>
        private GenerationDef Newest(GameDate today, int lagMonths)
        {
            IReadOnlyList<GenerationDef> generations = _catalog.Generations;
            for (int i = generations.Count - 1; i >= 0; i--)
            {
                GenerationDef gen = generations[i];
                if (gen.obsoleteYear > 0 && today.Year >= gen.obsoleteYear) continue;
                if (new GameDate(gen.announceYear, 1, 1).AddMonths(lagMonths) <= today) return gen;
            }
            return null;
        }

        private bool ChooseSocket(SimCompany company, int month, GameDate today, out SocketId socket, out GenerationDef gen)
        {
            socket = default(SocketId);
            gen = null;

            switch (company.Style)
            {
                case SimStyle.Follower:
                {
                    GenerationDef newest = Newest(today, company.Lag);
                    if (newest == null) return false;
                    if (!company.HasSocket || newest.order > company.Gen.order)
                    {
                        string package = newest.sockets[company.Index % newest.sockets.Count];
                        Switch(company, SocketIdentity.Standard(package), newest, month);
                    }
                    break;
                }
                case SimStyle.Custom:
                {
                    if (!company.HasSocket || month - company.SocketSince >= company.Cycle)
                    {
                        GenerationDef newest = Newest(today, 0);
                        if (newest == null) return false;
                        Switch(company, SocketIdentity.Custom(company.NextCustomId++, newest.sockets[0]), newest, month);
                    }
                    break;
                }
                case SimStyle.Hopper:
                {
                    if (!company.HasSocket || month - company.SocketSince >= company.Cycle)
                    {
                        string next = company.HopIndex + 1 < _hopSequence.Count ? _hopSequence[company.HopIndex + 1] : null;
                        GenerationDef nextGen = next == null ? null : _catalog.ByBasePackage(next);
                        if (nextGen != null && new GameDate(nextGen.announceYear, 1, 1) <= today)
                        {
                            company.HopIndex++;
                            Switch(company, SocketIdentity.Standard(next), nextGen, month);
                        }
                    }
                    break;
                }
                default:
                {
                    if (!company.HasSocket)
                    {
                        GenerationDef pga = _catalog.ByBasePackage("PGA 1985");
                        if (pga == null || new GameDate(pga.announceYear, 1, 1) > today) return false;
                        Switch(company, SocketIdentity.Standard("PGA 1985"), pga, month);
                    }
                    break;
                }
            }

            if (!company.HasSocket) return false;
            socket = company.Socket;
            gen = company.Gen;
            return true;
        }

        private static void Switch(SimCompany company, SocketId socket, GenerationDef gen, int month)
        {
            if (company.Abandon)
            {
                foreach (SimCpu old in company.Cpus)
                {
                    if (old.RetiredAt == int.MaxValue) old.RetiredAt = month;
                }
            }
            company.HasSocket = true;
            company.Socket = socket;
            company.Gen = gen;
            company.SocketSince = month;
        }

        // ---- sales ----

        private static bool OnMarket(SimCpu cpu, int month)
        {
            int age = month - cpu.ReleaseMonth;
            return age >= 0 && age < CpuLifeMonths && month < cpu.RetiredAt;
        }

        private void Sell(int month)
        {
            double total = 12000.0 * Math.Pow(1.045, month / 12.0);

            double weightSum = 0;
            foreach (SimCompany company in _companies)
            {
                if (HasActive(company, month)) weightSum += company.Weight;
            }
            if (weightSum <= 0) return;

            foreach (SimCompany company in _companies)
            {
                if (!HasActive(company, month)) continue;

                double units = total * company.Weight / weightSum;
                double cpuWeightSum = 0;
                foreach (SimCpu cpu in company.Cpus)
                {
                    if (OnMarket(cpu, month)) cpuWeightSum += CpuWeight(cpu, month);
                }

                foreach (SimCpu cpu in company.Cpus)
                {
                    if (!OnMarket(cpu, month)) continue;
                    int sold = (int)Math.Round(units * CpuWeight(cpu, month) / cpuWeightSum);
                    cpu.Sold[month] = sold;
                    cpu.ByContracts[month] = (int)Math.Round(sold * 0.2);
                }
            }
        }

        private static double CpuWeight(SimCpu cpu, int month)
        {
            return Math.Max(0.1, 1.0 - (month - cpu.ReleaseMonth) / (double)CpuLifeMonths);
        }

        private static bool HasActive(SimCompany company, int month)
        {
            foreach (SimCpu cpu in company.Cpus)
            {
                if (OnMarket(cpu, month)) return true;
            }
            return false;
        }

        private List<CpuLedgerView> Views()
        {
            var views = new List<CpuLedgerView>(_cpus.Count);
            foreach (SimCpu cpu in _cpus)
            {
                views.Add(new CpuLedgerView
                {
                    CpuId = cpu.Id,
                    CompanyKey = cpu.Owner.Key,
                    SocketKey = cpu.Socket.Key,
                    BasePackageName = cpu.Socket.BasePackageName,
                    GenId = cpu.Gen.id,
                    Sold = cpu.Sold,
                    SoldByContracts = cpu.ByContracts,
                });
            }
            return views;
        }

        // ---- contracts ----

        private static readonly string[] Clients = { "Dell", "Sony", "Acer", "Apple", "Compaq", "Packard Bell", "Toshiba", "Gateway" };

        /// <summary>Six new contracts a month (types 1, 2 and now and then 0, the calculator that the feature leaves alone); the preference is decided at once, as in the game.</summary>
        private void MakeContracts(int month, GameDate today)
        {
            _open.RemoveAll(c => month - c.Created >= 12);

            OemAdoptionModel.BuildInputs(_catalog, _timeline, _ib, today, _inputs);
            GameDate review = today.AddMonths(12);
            for (int i = 0; i < ContractsPerMonth; i++)
            {
                int type = i % 6 == 5 ? 0 : 1 + i % 2;
                string client = Clients[(month + i) % Clients.Length];
                int units = 5000 + ((month * 31 + i * 977) % 60000);
                float payment = 40f + (month * 13 + i * 71) % 400;
                string key = ContractKeyBuilder.Build(client, review.Year, review.Month, units, payment, type);

                _open.Add(new SimContract { Key = key, Type = type, Created = month });
                ContractsMade++;
                if (_balance.Oem.IncludesContractType(type)) _prefs.Assign(key, OemAdoptionModel.AssignPreference(key, WorldSeed, _inputs));
            }
        }

        // ---- the multipliers ----

        private ulong Evaluate(int month, GameDate evaluated, ulong fingerprint)
        {
            foreach (SimCompany company in _companies)
            {
                foreach (SimCpu cpu in company.Cpus)
                {
                    if (!OnMarket(cpu, month)) continue;

                    RatingBreakdown rating = _engine.RatingFor(company.Key, cpu.Socket, cpu.Gen.id, _ib, _timeline, evaluated, _balance);
                    Consumer.Add(rating.ConsumerMult);
                    Industries.Add(rating.IndustriesMult);
                    EffShare.Add(rating.EffShare);
                    company.Rating.Add(rating.ConsumerMult);
                    company.Industries.Add(rating.IndustriesMult);
                    company.EffShare.Add(rating.EffShare);
                    company.Bib.Add(rating.Bib);
                    company.Churn.Add(rating.Pchurn);
                    Samples.Add(new SimSample { Month = month, CpuId = cpu.Id, Company = company.Key, Consumer = rating.ConsumerMult, Industries = rating.IndustriesMult });
                    if (company.Index == 0) fingerprint = Mix(fingerprint, (ulong)BitConverter.ToUInt32(BitConverter.GetBytes(rating.ConsumerMult), 0), month);

                    var info = new EffectCpu { GenOrder = cpu.Gen.order, GenId = cpu.Gen.id, FullPlatform = cpu.FullPlatform, EffShare = rating.EffShare };

                    ChanceResult market = _engine.Chance(info, new EffectContract(), OemMode.MarketAverage, _prefs, _inputs, _balance);
                    ChanceMarket.Add(market.Multiplier);
                    Count(market);

                    for (int k = 0; k < ContractsAskedPerMonth && _open.Count > 0; k++)
                    {
                        SimContract contract = _open[(month * 7 + k * 5 + cpu.Id) % _open.Count];
                        var asked = new EffectContract { Present = true, Key = contract.Key, ContractType = contract.Type };
                        ChanceResult result = _engine.Chance(info, asked, OemMode.Auto, _prefs, _inputs, _balance);
                        Chance.Add(result.Multiplier);
                        company.Chance.Add(result.Multiplier);
                        Count(result);
                        if (company.Index == 0) fingerprint = Mix(fingerprint, (ulong)BitConverter.ToUInt32(BitConverter.GetBytes(result.Multiplier), 0), month);
                    }
                }
            }
            return fingerprint;
        }

        private void Count(ChanceResult result)
        {
            string reason = result.ReasonKey ?? "(none)";
            int count;
            Reasons.TryGetValue(reason, out count);
            Reasons[reason] = count + 1;
        }

        private static ulong Mix(ulong hash, ulong value, int month)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            hash ^= (ulong)month;
            hash *= 1099511628211UL;
            return hash;
        }

        // ---- the report for the owner ----

        public string Report()
        {
            var text = new StringBuilder();
            text.AppendLine("platform-generations balance report (14 synthetic companies, 1971-2030, " + CpusReleased + " CPUs, " + ContractsMade + " contracts, defaults of balance.json)");
            text.AppendLine("  rating, consumer segments: " + Consumer + "  (design: 0.90 ... 1.16)");
            text.AppendLine("  rating, Industries:        " + Industries + "  (design: 0.95 ... 1.08)");
            text.AppendLine("  signing chance, per contract: " + Chance + "  (design: 0.50 ... 1.375, guard 0.40 ... 1.40)");
            text.AppendLine("  signing chance, market mode:  " + ChanceMarket);
            text.AppendLine("  share of the installed base behind the CPUs (effShare): " + EffShare);
            text.AppendLine("  answers of the chance by reason: " + ReasonsText());
            text.AppendLine("  company                  style     CPUs  early  sockets  penalties  worst  rating (min / max / avg)        chance (min / max / avg)");
            foreach (SimCompany c in _companies)
            {
                text.AppendLine("  " + c.Key.PadRight(10) + " " + (c.Style + (c.Style == SimStyle.Follower ? " +" + c.Lag + "m" : c.Style == SimStyle.Laggard ? string.Empty : " " + c.Cycle + "m")).PadRight(18)
                                + c.Releases.ToString().PadLeft(5) + c.EarlyAdopterReleases.ToString().PadLeft(7) + c.NewSockets.ToString().PadLeft(9)
                                + c.Penalties.ToString().PadLeft(11) + c.WorstSeverity.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(7)
                                + "  " + Triple(c.Rating) + "   " + Triple(c.Chance));
            }
            return text.ToString();
        }

        private string ReasonsText()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in Reasons) parts.Add(pair.Key + " " + pair.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts.ToArray());
        }

        private static string Triple(SimRange range)
        {
            if (range.Count == 0) return "-".PadRight(27);
            return (range.Min.ToString("0.000", CultureInfo.InvariantCulture) + " / " + range.Max.ToString("0.000", CultureInfo.InvariantCulture)
                    + " / " + range.Average.ToString("0.000", CultureInfo.InvariantCulture)).PadRight(27);
        }
    }
}
