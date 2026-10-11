using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;
using GameConditionEvaluator = PTMod.Core.Logic.Conditions.ConditionEvaluator;
using HistConditionEvaluator = PTMod.Features.HistoricalEvents.ConditionEvaluator;

namespace PTMod.Tests.Features.HistoricalEvents
{
    // The synthetic worlds of TSK-026: whole games of 1975-2030 made of numbers, played through the real scheduler, role resolver, conditions, dispatcher and share cache
    // of the feature, with the calendar and the pack that ship. Nothing here is a logic type of the feature and nothing is covered by [Covers]: it is test equipment.

    /// <summary>The kinds of world the metrics of the calendar are measured in.</summary>
    internal enum WorldKind
    {
        /// <summary>
        /// An ordinary game: a leader of a fifth to two fifths of the market, or, in about a third of the games, a giant that holds well over half of it for an era (as the
        /// historical companies of the game do), and a player of a few percent to a fifth.
        /// </summary>
        Typical,

        /// <summary>The middle of the ordinary games, made by hand: a leader of 36 percent all the way, a player of about 10 percent, every named company there, nobody dominates.</summary>
        Average,

        /// <summary>The player has more than 0.6 of the market from 1995 on (the metric "the player dominates").</summary>
        Dominating,

        /// <summary>The player never has 5 percent (the metric "the player is weak").</summary>
        Weak,
    }

    /// <summary>A small deterministic source of numbers for building worlds (xoshiro128 of the core behind a mixed seed). The same seed always gives the same numbers.</summary>
    internal sealed class Rnd
    {
        private readonly Xoshiro128 _rng;

        public Rnd(uint seed)
        {
            _rng = new Xoshiro128(Mix(seed));
        }

        private static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
        }

        public float Next()
        {
            return _rng.Value();
        }

        public float Between(float low, float high)
        {
            return low + (high - low) * _rng.Value();
        }

        public int Int(int lowInclusive, int highInclusive)
        {
            int value = lowInclusive + (int)(_rng.Value() * (highInclusive - lowInclusive + 1));
            return value > highInclusive ? highInclusive : value;
        }

        public bool Chance(float probability)
        {
            return _rng.Value() < probability;
        }
    }

    /// <summary>
    /// The shape of one synthetic world: who leads the market and how much of it, how much the player has, which of the named companies of the calendar exist, and the switches
    /// of the game around it. Drawn from a seed; <see cref="Draw"/> is deterministic. Shares are given at the years of <see cref="AnchorYears"/> and run in straight lines between them.
    /// </summary>
    internal sealed class WorldProfile
    {
        public static readonly int[] AnchorYears = { 1975, 1985, 1995, 2005, 2015, 2025, 2030 };

        public uint Seed;
        public WorldKind Kind;

        /// <summary>The company that holds the leader's share (0 is Intel); -1 is the player, in the world where the player dominates.</summary>
        public int LeaderUid;

        /// <summary>The share of the leader's slot (over all segments) at the anchor years.</summary>
        public float[] LeaderShare = new float[7];

        /// <summary>The share of the player at the anchor years (not used where the player holds the leader's slot).</summary>
        public float[] PlayerShare = new float[7];

        public bool HasAmd = true;
        public bool HasToshiba = true;
        public bool HasFoundry = true;
        public bool HasCyrix = true;
        public bool CyrixFails = true;

        /// <summary>
        /// The line of GPUs (<c>gpu-product-line</c>) is on: the two crypto booms need it. Off in the metrics of the calendar: that feature is a separate piece of the mod, which a
        /// default install may not have, and the counts of the design (27 to 35 entries) are those of a game without it.
        /// </summary>
        public bool GpuLineActive;

        public bool PlayerFabless;
        public bool PlayerHasFactory = true;

        /// <summary>The player has an OEM contract in 2020 (<c>hist.decide.insourcing</c> asks for one).</summary>
        public bool PlayerHasOemContracts = true;

        /// <summary>The month (from 1975-01) the player's first CPU is released.</summary>
        public int PlayerFirstCpuMonth = 10;

        /// <summary>A name for a failure message.</summary>
        public string Describe()
        {
            return Kind + " seed " + Seed + " (leader uid " + LeaderUid + ", leader " + string.Join("/", LeaderShare.Select(v => v.ToString("0.00"))) + ", player " +
                   string.Join("/", PlayerShare.Select(v => v.ToString("0.00"))) + ", gpu " + GpuLineActive + ")";
        }

        /// <summary>The profile of a world of this kind for this seed.</summary>
        public static WorldProfile Draw(uint seed, WorldKind kind)
        {
            var rnd = new Rnd(seed * 2654435761u + (uint)kind * 97u + 1u);
            var p = new WorldProfile { Seed = seed, Kind = kind };

            p.HasAmd = rnd.Chance(0.95f);
            p.HasToshiba = rnd.Chance(0.90f);
            p.HasFoundry = rnd.Chance(0.90f);
            p.HasCyrix = rnd.Chance(0.85f);
            p.CyrixFails = rnd.Chance(0.70f);
            rnd.Next();   // (keeps the numbers of the later draws where they were when this switch was drawn too)
            p.PlayerFabless = rnd.Chance(0.15f);
            p.PlayerHasFactory = !p.PlayerFabless || rnd.Chance(0.2f);
            p.PlayerHasOemContracts = rnd.Chance(0.75f);
            p.PlayerFirstCpuMonth = rnd.Int(4, 18);
            p.LeaderUid = p.HasAmd && rnd.Chance(0.08f) ? 1 : 0;

            // The leader: a level for the whole game and a little movement around it ...
            float level = rnd.Between(0.18f, 0.42f);
            for (int i = 0; i < p.LeaderShare.Length; i++) p.LeaderShare[i] = Clamp(level * rnd.Between(0.86f, 1.14f), 0.15f, 0.5f);

            // ... unless the game has a giant: one company with well over half of the market for an era.
            if (kind == WorldKind.Typical)
            {
                float regime = rnd.Next();
                if (regime >= 0.45f && regime < 0.65f)
                {
                    // the giant of the nineties, who is only one of many again in the 2000s
                    p.LeaderShare[2] = rnd.Between(0.62f, 0.70f);
                    p.LeaderShare[3] = Math.Min(p.LeaderShare[3], 0.45f);
                }
                else if (regime >= 0.65f && regime < 0.80f)
                {
                    // the giant of the 2000s
                    p.LeaderShare[3] = rnd.Between(0.58f, 0.68f);
                    p.LeaderShare[4] = rnd.Between(0.52f, 0.60f);
                }
                else if (regime >= 0.80f)
                {
                    // a giant from the nineties to the 2010s
                    for (int i = 2; i <= 4; i++) p.LeaderShare[i] = rnd.Between(0.58f, 0.70f);
                }
            }

            // The player: grows for years, then holds.
            float peak = rnd.Between(0.04f, 0.21f);
            float[] growth = { 0f, rnd.Between(0.10f, 0.40f), rnd.Between(0.5f, 1.0f), rnd.Between(0.7f, 1.15f), rnd.Between(0.6f, 1.15f), rnd.Between(0.5f, 1.10f), 0f };
            for (int i = 0; i < p.PlayerShare.Length; i++) p.PlayerShare[i] = peak * growth[i];
            p.PlayerShare[6] = p.PlayerShare[5];

            switch (kind)
            {
                case WorldKind.Average:
                    p.HasAmd = p.HasToshiba = p.HasFoundry = p.HasCyrix = p.CyrixFails = true;
                    p.LeaderUid = 0;
                    p.LeaderShare = new[] { 0.36f, 0.36f, 0.36f, 0.36f, 0.36f, 0.36f, 0.36f };
                    p.PlayerShare = new[] { 0f, 0.02f, 0.08f, 0.11f, 0.10f, 0.10f, 0.10f };
                    p.PlayerFabless = false;
                    p.PlayerHasFactory = true;
                    p.PlayerHasOemContracts = true;
                    p.PlayerFirstCpuMonth = 8;
                    break;

                case WorldKind.Dominating:
                    // The player has more than 0.6 from 1995 on; Intel is only a big company among the others.
                    p.LeaderUid = -1;
                    p.LeaderShare = new[] { 0.03f, 0.25f, 0.64f, 0.68f, 0.70f, 0.70f, 0.70f };
                    p.PlayerShare = new float[7];
                    p.PlayerFirstCpuMonth = 6;
                    p.PlayerFabless = false;
                    p.PlayerHasFactory = true;
                    break;

                case WorldKind.Weak:
                    // Under five percent all the way, while the leader is an ordinary one.
                    for (int i = 0; i < p.PlayerShare.Length; i++) p.PlayerShare[i] = Math.Min(p.PlayerShare[i], 0.04f) * (i == 0 ? 0f : 1f);
                    for (int i = 0; i < p.LeaderShare.Length; i++) p.LeaderShare[i] = Math.Min(p.LeaderShare[i], 0.45f);
                    break;
            }

            // The player never has more than the leader.
            if (p.LeaderUid >= 0)
            {
                for (int i = 0; i < p.PlayerShare.Length; i++) p.PlayerShare[i] = Math.Min(p.PlayerShare[i], p.LeaderShare[i] * 0.7f);
            }
            return p;
        }

        private static float Clamp(float value, float low, float high)
        {
            return value < low ? low : value > high ? high : value;
        }

        /// <summary>The value of a series of the anchor years at a (fractional) year.</summary>
        public static float At(float[] series, float year)
        {
            if (year <= AnchorYears[0]) return series[0];
            for (int i = 1; i < AnchorYears.Length; i++)
            {
                if (year <= AnchorYears[i])
                {
                    float t = (year - AnchorYears[i - 1]) / (AnchorYears[i] - AnchorYears[i - 1]);
                    return series[i - 1] + (series[i] - series[i - 1]) * t;
                }
            }
            return series[series.Length - 1];
        }
    }

    /// <summary>
    /// A game of 1975-2030 as numbers: about fifteen companies that are born, grow and sometimes fail, a CPU every year and a half or two for each, and the monthly journals of what each
    /// CPU sold. The world is built whole from its profile at the start and shows only what has happened by the day it was last advanced to: companies and CPUs appear when they are
    /// founded and released, the journals grow one month at a time with the last element the month that is not over (right-aligned lists, like the game's own), companies
    /// that fail are marked bankrupt. It is an <see cref="IWorldView"/>, so the roles, the conditions and the share cache see it as they see the game.
    /// </summary>
    internal sealed class SyntheticWorld : IWorldView
    {
        public const int FirstYear = 1975;
        public const int LastYear = 2030;
        public const int Months = (LastYear - FirstYear + 1) * 12;

        /// <summary>How long a CPU that left the market stays in the list of CPUs.</summary>
        private const int RetentionMonths = 40;

        private sealed class Clock
        {
            public int Month = -1;
        }

        /// <summary>The monthly journal of a CPU: its sales from the month of its release up to the current month (so the last element is the month that is not over). Null data is a journal of zeros.</summary>
        private sealed class Journal : IReadOnlyList<int>
        {
            private readonly int[] _data;
            private readonly int _offset;
            private readonly Clock _clock;

            public Journal(int[] data, int offset, Clock clock)
            {
                _data = data;
                _offset = offset;
                _clock = clock;
            }

            public int Count
            {
                get
                {
                    int count = _clock.Month - _offset + 1;
                    return count < 0 ? 0 : count;
                }
            }

            public int this[int index]
            {
                get { return _data == null ? 0 : _data[_offset + index]; }
            }

            public IEnumerator<int> GetEnumerator()
            {
                int count = Count;
                for (int i = 0; i < count; i++) yield return this[i];
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class SynthCpu
        {
            public CpuInfo Info;
            public int Release;
            public int Retire;
            public float Weight;
            public int[] Sales;
            public bool Listed;
        }

        private sealed class SynthCompany
        {
            public CompanyInfo Info;
            public int Birth;
            public int Death = int.MaxValue;
            public float Weight;
            public bool IsPlayer;
            public bool IsLeaderSlot;
            public bool DesignsCpus = true;
            public bool Listed;
            public float[] Noise;
            public readonly List<SynthCpu> Cpus = new List<SynthCpu>();
        }

        private readonly Clock _clock = new Clock();
        private readonly List<SynthCompany> _all = new List<SynthCompany>();
        private readonly List<SynthCpu> _cpus = new List<SynthCpu>();
        private readonly List<CompanyInfo> _companies = new List<CompanyInfo>();
        private readonly List<CpuInfo> _visibleCpus = new List<CpuInfo>();
        private readonly List<SynthCpu> _visible = new List<SynthCpu>();
        private int _nextSaveId = 1;

        public SyntheticWorld(WorldProfile profile)
        {
            Profile = profile;
            var rnd = new Rnd(profile.Seed * 7919u + 13u + (uint)profile.Kind * 31u);
            BuildRoster(rnd);
            BuildCpuSchedules(rnd);
            BuildSales(rnd);
            MonthsSinceStart = 0;
        }

        public WorldProfile Profile { get; private set; }

        public IReadOnlyList<CompanyInfo> Companies
        {
            get { return _companies; }
        }

        public IReadOnlyList<CpuInfo> Cpus
        {
            get { return _visibleCpus; }
        }

        public int MonthsSinceStart { get; private set; }

        public SimpleDate Today { get; private set; }

        public float AverageIps
        {
            get { return IpsBase(FirstYear + MonthsSinceStart / 12f) * 0.6f; }
        }

        public float Revenue12m(string companyKey)
        {
            return 0f;
        }

        public float CashHeadroom(string companyKey)
        {
            return 0f;
        }

        public bool IsFeatureActive(string featureId)
        {
            return featureId == "gpu-product-line" && Profile.GpuLineActive;
        }

        // ---- time ----

        /// <summary>Moves the world to this day (days from 1971-01-01). Forward only; a call for the same month changes nothing but the date.</summary>
        public void AdvanceTo(int day)
        {
            SimpleDate date = SimpleDate.FromDays(day);
            Today = date;

            int month = (date.Year - FirstYear) * 12 + date.Month - 1;
            if (month < 0) month = 0;
            if (month >= Months) month = Months - 1;
            if (month != _clock.Month) AdvanceMonth(month);
        }

        private void AdvanceMonth(int month)
        {
            _clock.Month = month;
            MonthsSinceStart = month;

            bool changed = false;
            foreach (SynthCompany company in _all)
            {
                if (!company.Listed && company.Birth <= month)
                {
                    company.Listed = true;
                    _companies.Add(company.Info);
                    changed = true;
                }
                if (company.Death <= month) company.Info.IsBankrupt = true;
            }
            if (changed) _companies.Sort((a, b) => a.IsPlayer != b.IsPlayer ? (a.IsPlayer ? -1 : 1) : a.UniqueId.CompareTo(b.UniqueId));

            // The CPUs of the game stay in its list for good. This view lets one go 40 months after it left the market: nothing the calendar asks can still depend on it (the
            // widest window of shares is 36 months, a role looks at CPUs of the last 36 months or on sale), and the share cache reads the whole list every time it is rebuilt.
            bool listChanged = false;
            foreach (SynthCpu cpu in _cpus)
            {
                if (cpu.Release > month) break;   // sorted by release
                if (!cpu.Listed)
                {
                    cpu.Listed = true;
                    cpu.Info.Released = true;
                    cpu.Info.OnMarket = true;
                    _visible.Add(cpu);
                    listChanged = true;
                }
            }
            foreach (SynthCpu cpu in _visible)
            {
                if (cpu.Retire <= month && !cpu.Info.Retired)
                {
                    cpu.Info.Retired = true;
                    cpu.Info.OnMarket = false;
                }
            }
            if (_visible.RemoveAll(c => c.Retire + RetentionMonths <= month) > 0) listChanged = true;
            if (listChanged)
            {
                _visibleCpus.Clear();
                foreach (SynthCpu cpu in _visible) _visibleCpus.Add(cpu.Info);
            }
        }

        // ---- building ----

        private struct Template
        {
            public int Uid;
            public string Name;
            public int FoundedYear;
            public bool Fabless;
            public bool Foundry;
            public int Capacity;
            public float Weight;
        }

        private static readonly Template[] Roster =
        {
            new Template { Uid = 0, Name = "Intel", FoundedYear = 1968, Capacity = 12, Weight = 2.0f },
            new Template { Uid = 1, Name = "AMD", FoundedYear = 1969, Capacity = 8, Weight = 1.2f },
            new Template { Uid = 2, Name = "Motorola", FoundedYear = 1928, Capacity = 8, Weight = 1.5f },
            new Template { Uid = 3, Name = "Zilog", FoundedYear = 1974, Capacity = 3, Weight = 0.6f },
            new Template { Uid = 4, Name = "Hitachi", FoundedYear = 1910, Capacity = 6, Weight = 0.8f },
            new Template { Uid = 5, Name = "National", FoundedYear = 1959, Capacity = 4, Weight = 0.6f },
            new Template { Uid = 6, Name = "Toshiba", FoundedYear = 1875, Capacity = 8, Weight = 1.0f },
            new Template { Uid = 7, Name = "Fujitsu", FoundedYear = 1935, Capacity = 6, Weight = 0.7f },
            new Template { Uid = 9, Name = "TSMC", FoundedYear = 1987, Foundry = true, Capacity = 100, Weight = 0f },
            new Template { Uid = 10, Name = "Cyrix", FoundedYear = 1988, Fabless = true, Capacity = 0, Weight = 0.5f },
            new Template { Uid = 11, Name = "NVIDIA", FoundedYear = 1993, Fabless = true, Capacity = 0, Weight = 0.7f },
            new Template { Uid = 12, Name = "Qualcomm", FoundedYear = 1985, Fabless = true, Capacity = 0, Weight = 0.8f },
            new Template { Uid = 13, Name = "Apple", FoundedYear = 2008, Fabless = true, Capacity = 0, Weight = 1.5f },
            new Template { Uid = 14, Name = "Samsung", FoundedYear = 1978, Capacity = 9, Weight = 0.9f },
            new Template { Uid = 15, Name = "Newcomer", FoundedYear = 2000, Fabless = true, Capacity = 0, Weight = 0.6f },
        };

        private void BuildRoster(Rnd rnd)
        {
            WorldProfile p = Profile;

            // The player: a company of the first month, with or without a factory.
            var player = new SynthCompany
            {
                IsPlayer = true,
                Birth = 0,
                Weight = 0f,
                Info = new CompanyInfo
                {
                    Key = CompanyKey.Player,
                    UniqueId = -1,
                    Name = "Player Co",
                    IsPlayer = true,
                    IsCpuCompany = true,
                    IsFabless = p.PlayerFabless,
                    HasManufacturing = p.PlayerHasFactory,
                    Capacity = p.PlayerHasFactory ? 4 : 0,
                    FoundedYear = FirstYear,
                    FoundedMonth = 1,
                },
            };
            player.IsLeaderSlot = p.LeaderUid < 0;
            _all.Add(player);

            foreach (Template t in Roster)
            {
                if (t.Uid == 1 && !p.HasAmd) continue;
                if (t.Uid == 6 && !p.HasToshiba) continue;
                if (t.Uid == 9 && !p.HasFoundry) continue;
                if (t.Uid == 10 && !p.HasCyrix) continue;

                int birth = Math.Max(0, (t.FoundedYear - FirstYear) * 12 + rnd.Int(0, 11));
                int death = int.MaxValue;
                switch (t.Uid)
                {
                    case 2: death = Fails(rnd, 0.70f, 1995, 2008); break;
                    case 3: death = Fails(rnd, 0.80f, 1988, 1998); break;
                    case 4: death = Fails(rnd, 0.50f, 2003, 2012); break;
                    case 5: death = Fails(rnd, 0.60f, 1990, 2000); break;
                    case 6: death = Fails(rnd, 0.15f, 2012, 2020); break;
                    case 7: death = Fails(rnd, 0.40f, 2005, 2015); break;
                    case 10: death = p.CyrixFails ? Fails(rnd, 1f, 1997, 2000) : int.MaxValue; break;
                }

                bool isLeaderSlot = t.Uid == p.LeaderUid;
                float weight = t.Weight;
                if (p.Kind == WorldKind.Dominating && t.Uid == 0) weight = 6f;   // the biggest of the others

                _all.Add(new SynthCompany
                {
                    Birth = birth,
                    Death = death,
                    Weight = weight,
                    IsLeaderSlot = isLeaderSlot,
                    DesignsCpus = !t.Foundry,
                    Info = new CompanyInfo
                    {
                        Key = CompanyKey.ForAi(t.Uid),
                        UniqueId = t.Uid,
                        Name = t.Name,
                        IsFabless = t.Fabless,
                        IsFoundry = t.Foundry,
                        IsCpuCompany = !t.Foundry,
                        HasManufacturing = !t.Fabless,
                        Capacity = t.Capacity,
                        FoundedYear = Math.Max(t.FoundedYear, 1),
                        FoundedMonth = 1 + (birth % 12),
                    },
                });
            }

            foreach (SynthCompany company in _all)
            {
                company.Noise = new float[Months];
                float level = 0f;
                for (int m = 0; m < Months; m++)
                {
                    level += (rnd.Next() - 0.5f) * 0.10f;
                    level = level * 0.98f;
                    company.Noise[m] = level;
                }
            }
        }

        private static int Fails(Rnd rnd, float probability, int fromYear, int toYear)
        {
            if (!rnd.Chance(probability)) return int.MaxValue;
            return (rnd.Int(fromYear, toYear) - FirstYear) * 12 + rnd.Int(0, 11);
        }

        private void BuildCpuSchedules(Rnd rnd)
        {
            foreach (SynthCompany company in _all)
            {
                if (!company.DesignsCpus) continue;

                int t = company.IsPlayer
                    ? Profile.PlayerFirstCpuMonth
                    : company.Birth <= 0 ? rnd.Int(0, 12) : company.Birth + rnd.Int(3, 12);

                int turn = rnd.Int(0, 2);
                while (t < Months && t < company.Death)
                {
                    int interval = rnd.Int(15, 30);
                    int life = interval + rnd.Int(12, 36);

                    var cpu = new SynthCpu { Release = t, Retire = Math.Min(t + life, company.Death), Weight = rnd.Between(0.6f, 1.4f) };
                    Segment segment = SegmentFor(company, turn++, rnd);
                    float year = FirstYear + t / 12f;
                    cpu.Info = new CpuInfo
                    {
                        SaveId = _nextSaveId++,
                        CompanyKey = company.Info.Key,
                        Year = FirstYear + t / 12,
                        Ips = IpsBase(year) * rnd.Between(0.75f, 1.30f),
                        Frequency = 1f * (float)Math.Pow(1.25, year - FirstYear) * rnd.Between(0.8f, 1.2f),
                        Cores = 1 + (int)((year - FirstYear) / 6f),
                        Price = 100f * rnd.Between(0.5f, 3f),
                        Segment = segment,
                    };
                    cpu.Info.Power = cpu.Info.Ips / (rnd.Between(0.6f, 1.8f) * (float)Math.Pow(1.1, year - FirstYear));
                    company.Cpus.Add(cpu);
                    _cpus.Add(cpu);
                    t += interval;
                }
            }
            _cpus.Sort((a, b) => a.Release != b.Release ? a.Release.CompareTo(b.Release) : a.Info.SaveId.CompareTo(b.Info.SaveId));
        }

        private static Segment SegmentFor(SynthCompany company, int turn, Rnd rnd)
        {
            if (company.IsLeaderSlot)
            {
                Segment[] cycle = { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.HighEnd };
                return cycle[turn % cycle.Length];
            }
            if (company.IsPlayer)
            {
                Segment[] cycle = { Segment.MidRange, Segment.LowEnd, Segment.HighEnd };
                return cycle[turn % cycle.Length];
            }

            float roll = rnd.Next();
            return roll < 0.20f ? Segment.HighEnd : roll < 0.55f ? Segment.MidRange : roll < 0.90f ? Segment.LowEnd : Segment.Industries;
        }

        /// <summary>The market in units a month: it grows about twelve percent a year.</summary>
        private static float MarketSize(int month)
        {
            return 3000f * (float)Math.Pow(1.12, month / 12.0);
        }

        /// <summary>The performance of a typical new CPU of a year.</summary>
        public static float IpsBase(float year)
        {
            return 0.5f * (float)Math.Pow(1.38, year - FirstYear);
        }

        private void BuildSales(Rnd rnd)
        {
            WorldProfile p = Profile;
            SynthCompany player = _all[0];
            SynthCompany leader = _all.FirstOrDefault(c => c.IsLeaderSlot);
            foreach (SynthCpu cpu in _cpus) cpu.Sales = new int[Months];

            var live = new List<SynthCpu>();
            for (int m = 0; m < Months; m++)
            {
                float year = FirstYear + m / 12f;
                float units = MarketSize(m);
                float leaderShare = WorldProfile.At(p.LeaderShare, year);
                float playerShare = leader == player ? 0f : WorldProfile.At(p.PlayerShare, year);

                // The companies with something to sell this month.
                float weights = 0f;
                var others = new List<KeyValuePair<SynthCompany, float>>();
                foreach (SynthCompany company in _all)
                {
                    if (!HasLiveCpu(company, m) || company.IsPlayer || company == leader) continue;

                    float ramp = Math.Min(1f, 0.3f + (m - company.Birth) / 36f);
                    float taper = company.Death == int.MaxValue ? 1f : Math.Max(0.1f, Math.Min(1f, (company.Death - m) / 24f));
                    float weight = company.Weight * ramp * taper * (float)Math.Exp(company.Noise[m]);
                    others.Add(new KeyValuePair<SynthCompany, float>(company, weight));
                    weights += weight;
                }

                float rest = Math.Max(0f, 1f - leaderShare - playerShare);
                foreach (SynthCompany company in _all)
                {
                    if (!HasLiveCpu(company, m)) continue;

                    float share = 0f;
                    if (company == leader) share = leaderShare;
                    else if (company.IsPlayer) share = playerShare;
                    else if (weights > 0f) share = rest * others.First(o => o.Key == company).Value / weights;
                    if (share <= 0f) continue;

                    live.Clear();
                    float total = 0f;
                    foreach (SynthCpu cpu in company.Cpus)
                    {
                        if (cpu.Release <= m && m < cpu.Retire)
                        {
                            live.Add(cpu);
                            total += cpu.Weight;
                        }
                    }

                    int sold = (int)Math.Round(units * share);
                    int given = 0;
                    for (int i = 0; i < live.Count; i++)
                    {
                        int part = i == live.Count - 1 ? sold - given : (int)(sold * (live[i].Weight / total));
                        live[i].Sales[m] = part;
                        given += part;
                    }
                }
            }

            foreach (SynthCpu cpu in _cpus)
            {
                var journal = new Journal(cpu.Sales, cpu.Release, _clock);
                var zeros = new Journal(null, cpu.Release, _clock);
                CpuInfo info = cpu.Info;
                info.Sold = journal;
                info.SoldByContracts = zeros;
                info.SoldByHighEnd = info.Segment == Segment.HighEnd ? journal : zeros;
                info.SoldByMidRange = info.Segment == Segment.MidRange ? journal : zeros;
                info.SoldByLowEnd = info.Segment == Segment.LowEnd ? journal : zeros;
                info.SoldByIndustries = info.Segment == Segment.Industries ? journal : zeros;
            }
        }

        private static bool HasLiveCpu(SynthCompany company, int month)
        {
            if (!company.DesignsCpus || company.Birth > month || company.Death <= month) return false;
            foreach (SynthCpu cpu in company.Cpus)
            {
                if (cpu.Release <= month && month < cpu.Retire) return true;
            }
            return false;
        }
    }

    /// <summary>The calendar and the pack that ship, read once and mapped with the real predicates of the feature.</summary>
    internal static class ShippedContent
    {
        private static List<CalendarEntry> _entries;
        private static HashSet<string> _packEvents;
        private static HashSet<string> _decisionEvents;

        /// <summary>The 42 entries of <c>calendar.json</c>, in the order of the file. Shared: the runs only read them.</summary>
        public static IReadOnlyList<CalendarEntry> Entries
        {
            get
            {
                Load();
                return _entries;
            }
        }

        /// <summary>The ids of the events of <c>events.json</c>.</summary>
        public static HashSet<string> PackEvents
        {
            get
            {
                Load();
                return _packEvents;
            }
        }

        /// <summary>The events that ask for a decision: the ones with buttons.</summary>
        public static HashSet<string> DecisionEvents
        {
            get
            {
                Load();
                return _decisionEvents;
            }
        }

        private static void Load()
        {
            if (_entries != null) return;

            string folder = System.IO.Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events");
            JsonObject calendar = HistFixture.ObjFromFile(SourceScan.ReadText(System.IO.Path.Combine(folder, "calendar.json")));
            JsonObject pack = HistFixture.ObjFromFile(SourceScan.ReadText(System.IO.Path.Combine(folder, "events.json")));

            PredicateRegistry registry = Predicates.CreateRegistry();
            foreach (string name in HistPredicates.Names)
            {
                Assert.True(registry.Register(name, HistIds.Feature, HistPredicates.Factory(name, () => HistRoleScope.Current)), name);
            }
            var parser = new ConditionParser(registry);

            var entries = new List<CalendarEntry>();
            foreach (JsonNode node in calendar.GetArray("records"))
            {
                var messages = new List<string>();
                CalendarEntry entry = CalendarEntry.FromJson((JsonObject)node, messages, parser.Parse);
                Assert.NotNull(entry, "calendar.json must map: " + string.Join(" | ", messages));
                entries.Add(entry);
            }

            var events = new HashSet<string>(StringComparer.Ordinal);
            var decisions = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode node in pack.GetArray("records"))
            {
                var ev = (JsonObject)node;
                string id = ev.GetString("id", string.Empty);
                events.Add(id);
                JsonArray choices = ev.GetArray("choices");
                if (choices != null && choices.Count > 0) decisions.Add(id);
            }

            _packEvents = events;
            _decisionEvents = decisions;
            _entries = entries;
        }
    }

    /// <summary>
    /// A whole game of the calendar over a synthetic world: the real scheduler (<see cref="SchedulerCore"/>), role resolver, conditions (the predicates of the core and the
    /// <c>hist.*</c> ones), dispatcher and share cache, a fake engine that records every event it is asked to fire and gives the few facts the calendar asks the services about
    /// (an antitrust case is open once its event has fired, an export rule has a threshold once its event has fired), and the clock of the game. It is the wiring of
    /// <c>CalendarScheduler</c> and <c>HistRuntime</c> in a test, with the day loop of the game: every day <c>OnDay</c>, on the first of the month <c>OnMonth</c>.
    /// </summary>
    internal sealed class CalendarRun : ISchedulerHost, IEventFirer
    {
        public sealed class Fired
        {
            public int Day;
            public string EventId;
            public string Company;
            public string[] Args;

            public string EntryId
            {
                get { return Args != null && Args.Length > 0 ? Args[0] : string.Empty; }
            }
        }

        private readonly HashSet<string> _enabled;
        private readonly Dictionary<string, string> _categoryOf = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HistSaveData _data = new HistSaveData();
        private readonly Xoshiro128 _schedulerRng;
        private readonly Xoshiro128 _roleRng;
        private readonly MarketShareCache _shares = new MarketShareCache();
        private readonly RoleResolver _resolver = new RoleResolver();
        private readonly HistState _state = new HistState();
        private readonly HistConditionEvaluator _evaluator;
        private readonly DispatcherCore _dispatcher;
        private readonly FakeGameState _gameState = new FakeGameState();
        private readonly Dictionary<string, string> _caseTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        private float? _exportThreshold;

        public CalendarRun(WorldProfile profile, bool disaster)
        {
            Profile = profile;
            World = new SyntheticWorld(profile);
            Disaster = disaster;
            _enabled = new HashSet<string>(HistCategories.All.Where(c => disaster || HistCategories.DefaultEnabled(c)), StringComparer.Ordinal);
            foreach (CalendarEntry entry in ShippedContent.Entries) _categoryOf[entry.Id] = entry.Category;

            _schedulerRng = new Xoshiro128(profile.Seed * 31u + 7u);
            _roleRng = new Xoshiro128(profile.Seed * 17u + 3u);

            _gameState.SetFeatureActive("gpu-product-line", profile.GpuLineActive);
            _gameState.Player.ActiveOemContracts = profile.PlayerHasOemContracts ? 1 : 0;
            for (int uid = 0; uid <= 15; uid++) _gameState.Ai(uid).ActiveOemContracts = 1;

            _state.Warn = message => Warnings.Add(message);
            _state.CaseActive = (caseId, targetNotPlayer) =>
            {
                string target;
                return _caseTargets.TryGetValue(caseId, out target) && (!targetNotPlayer || !CompanyKey.IsPlayer(target));
            };
            _state.ExportThreshold = ruleId => _exportThreshold;

            _evaluator = new HistConditionEvaluator(
                (condition, subject) => GameConditionEvaluator.Evaluate(condition, new ConditionContext(_gameState, World.Today.ToDateTime(), subject), message => Warnings.Add(message)),
                message => Warnings.Add(message));
            _dispatcher = new DispatcherCore(this, World, _shares, _evaluator, _state, key => key, message => Warnings.Add(message));
            Core = new SchedulerCore(() => ShippedContent.Entries, () => _data, this, () => _schedulerRng, () => 45);
        }

        public WorldProfile Profile { get; private set; }

        public SyntheticWorld World { get; private set; }

        public bool Disaster { get; private set; }

        public SchedulerCore Core { get; private set; }

        public readonly List<Fired> Fires = new List<Fired>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public int ChecksOfRoles;

        public static int Day(string iso)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(iso, out date), iso);
            return date.Days;
        }

        /// <summary>Plays the whole game: from the first day of 1975 to the last day of 2030.</summary>
        public CalendarRun Execute()
        {
            int start = Day("1975-01-01");
            int end = Day("2030-12-31");

            HistRoleScope.Reset();
            try
            {
                World.AdvanceTo(start);
                Core.Initialize(start);

                // The game calls OnDay every day and OnMonth on the first. A day on which nothing is due is a call that changes nothing (the scheduler answers it with one
                // comparison), so the loop visits the days with something due and the firsts of the months: the same game, without the 20 000 empty calls.
                int day = start;
                while (day <= end)
                {
                    World.AdvanceTo(day);
                    Core.OnDay(day);
                    if (World.Today.Day == 1) Core.OnMonth(day);

                    int next = SchedulerCore.FirstOfNextMonth(day);
                    int due = Core.NextDueDay;
                    if (due > day && due < next) next = due;
                    day = next > day ? next : day + 1;
                }
            }
            finally
            {
                HistRoleScope.Reset();
            }
            return this;
        }

        // ---- the results ----

        public EntryStatus StatusOf(string entryId)
        {
            return Core.GetStatus(entryId);
        }

        /// <summary>The entries that fired, the earliest first.</summary>
        public IReadOnlyList<FiredEntryView> FiredEntries
        {
            get { return Core.GetFired(); }
        }

        /// <summary>The decisions the player was asked for: events with buttons that were fired at the player.</summary>
        public int PlayerDecisions
        {
            get { return Fires.Count(f => f.Company == CompanyKey.Player && ShippedContent.DecisionEvents.Contains(f.EventId)); }
        }

        /// <summary>The antitrust entries that fired at the player as the company the regulator acts against.</summary>
        public IReadOnlyList<string> AntitrustAgainstPlayer
        {
            get
            {
                return Fires
                    .Where(f => f.Company == CompanyKey.Player && f.EventId != "hist.decide.settlement" && CategoryOf(f.EntryId) == HistCategories.Antitrust)
                    .Select(f => f.EntryId).Distinct(StringComparer.Ordinal).ToList();
            }
        }

        public string CategoryOf(string entryId)
        {
            string category;
            return _categoryOf.TryGetValue(entryId ?? string.Empty, out category) ? category : string.Empty;
        }

        /// <summary>The days the <c>major</c> entries fired, in order.</summary>
        public IReadOnlyList<int> MajorDays
        {
            get
            {
                var days = new List<int>();
                foreach (CalendarEntry entry in ShippedContent.Entries)
                {
                    EntryStateDto state = Core.StateOf(entry.Id);
                    if (entry.Major && state != null && state.status == (int)EntryStatus.Fired) days.Add(state.firedDay);
                }
                days.Sort();
                return days;
            }
        }

        /// <summary>A fingerprint of everything that happened: every event fired, in order, with its day and company, and the final status of every entry.</summary>
        public string Digest()
        {
            ulong hash = 1469598103934665603UL;
            var text = new StringBuilder();
            foreach (Fired fire in Fires) text.Append(fire.Day).Append('|').Append(fire.EventId).Append('|').Append(fire.Company).Append(';');
            foreach (CalendarEntry entry in ShippedContent.Entries) text.Append(entry.Id).Append('=').Append(Core.GetStatus(entry.Id)).Append(';');
            unchecked
            {
                foreach (char c in text.ToString())
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16");
        }

        // ---- ISchedulerHost ----

        public bool IsCategoryEnabled(string category)
        {
            return _enabled.Contains(category);
        }

        public RoleResult ResolveRoles(CalendarEntry entry)
        {
            ChecksOfRoles++;
            _shares.EnsureFresh(World, 36);
            return _resolver.Resolve(entry, World, _shares, new RoleOptions(), _roleRng);
        }

        public bool ConditionsHold(CalendarEntry entry, RoleResult roles)
        {
            _shares.EnsureFresh(World, 36);
            return _evaluator.EvaluateAll(entry.Conditions, roles, World, _shares, _state);
        }

        public FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
        {
            return _dispatcher.FireStage(entry, stageIndex, roles).Outcome;
        }

        public void FireFallback(CalendarEntry entry)
        {
            bool needsRoles = entry.FallbackFires != null && entry.FallbackFires.Count > 0;
            _dispatcher.FireFallback(entry, needsRoles ? ResolveRoles(entry) : new RoleResult());
        }

        void ISchedulerHost.Log(string level, string message)
        {
            if (level == "error") Errors.Add(message);
            else if (level == "warn") Warnings.Add(message);
        }

        public void Fault(CalendarEntry entry, Exception ex)
        {
            Errors.Add("fault in " + entry.Id + ": " + ex.GetType().Name + ": " + ex.Message);
        }

        // ---- IEventFirer: the engine ----

        public FireResult Fire(string eventId, string companyKey, string[] args)
        {
            if (!ShippedContent.PackEvents.Contains(eventId)) return FireResult.UnknownEvent;

            Fires.Add(new Fired { Day = World.Today.Days, EventId = eventId, Company = companyKey, Args = args });

            // What the services of the feature would have in their books once the engine had run the effects of the event.
            switch (eventId)
            {
                case "hist.decide.antitrust.fine":
                case "hist.decide.antitrust.consent":
                case "hist.2005.jftc":
                    if (args != null && args.Length > 4) _caseTargets[args[4]] = companyKey;
                    break;
                case "hist.2022.export.rule":
                    _exportThreshold = ExportThresholdNow();
                    break;
            }
            return FireResult.Fired;
        }

        /// <summary>The 90th percentile of the Ips of the CPUs on sale, the way the export rule of the design sets its threshold.</summary>
        private float ExportThresholdNow()
        {
            List<float> ips = World.Cpus.Where(WorldMath.IsOnMarket).Select(c => c.Ips).OrderBy(v => v).ToList();
            if (ips.Count == 0) return float.MaxValue;
            return ips[Math.Min(ips.Count - 1, (int)(ips.Count * 0.9f))];
        }
    }
}
