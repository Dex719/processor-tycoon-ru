using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>One AI designer of the simulation: who it is, how big, when it appears and in which segments it sells (its presence).</summary>
    internal sealed class SimCompanyDef
    {
        public string Key;                      // the id of its record in ai-preferences.json
        public string Name;
        public int UniqueId;
        public double Size;                     // weight of the company in every segment it sells in
        public int SpawnYear;                   // the year it appears (the first month of the simulation for the old ones)
        public float CostModifier = 1f;         // M4 of the game: 6 for the "unlucky" Cyrix
        public double[] Presence;               // High, Mid, Low, Industries: how much of its weight goes to each segment
        public bool IsPc;                       // one of the five "PC companies" of the metric
        public bool IsEmbedded;                 // one of the three "embedded companies" of the metric
    }

    /// <summary>The knobs of the simulated market. They are not game data: the simulation checks the formulas and the policy of the feature, not the economy of the game.</summary>
    internal sealed class SimParams
    {
        public int StartYear = 1975;
        public int EndYear = 2025;

        /// <summary>Growth of the number of CPUs sold, per year.</summary>
        public double MarketGrowth = 1.12;

        /// <summary>The share of the revenue a company keeps as free money (the rest is the cost of everything the simulation does not model).</summary>
        public double Margin = 0.15;

        /// <summary>A company starts with this many months of the profit it makes at the start.</summary>
        public double StartMonthsOfProfit = 24;

        /// <summary>The line-up of a company turns over with this time constant, in months: the share of the CPUs of a family in its sales follows the active family with this delay.</summary>
        public double LineupMonths = 18;

        /// <summary>The price level the support upkeep is multiplied by: this much more every year after 1971.</summary>
        public double InflationPerYear = 1.035;

        public int Difficulty = 2;

        /// <summary>CPUs sold per month in 1975 by segment (High, Mid, Low, Industries).</summary>
        public double[] UnitsPerMonth = { 21000, 84000, 250000, 75000 };

        /// <summary>Price of one CPU by segment.</summary>
        public double[] Price = { 700, 250, 90, 120 };

        /// <summary>Scales the weights of the AI preferences of one family for one company: a hook for the tests that move a weight.</summary>
        public Func<string, string, float> WeightScale;
    }

    /// <summary>A change of the active family or a finished research, for the report.</summary>
    internal sealed class SimEvent
    {
        public DateTime Date;
        public string Company;
        public string Kind;      // "researched" or "active"
        public string Family;
        public string From;
    }

    /// <summary>The state of the world at the start of a year (after the monthly cycle of January).</summary>
    internal sealed class SimYear
    {
        public int Year;
        public readonly Dictionary<string, string> Active = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Ecosystem = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Support = new Dictionary<string, float>(StringComparer.Ordinal);   // of the active family
        public double MinM = double.MaxValue;
        public double MaxM = double.MinValue;
        public int PcOnX86;
        public int PcAlive;
        public int EmbeddedOnArm;
        public int EmbeddedAlive;
        public double TotalUpkeep;                // the support upkeep of all companies over the previous 12 months
    }

    /// <summary>What a run found out.</summary>
    internal sealed class SimResult
    {
        public readonly List<SimYear> Years = new List<SimYear>();
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public readonly List<string> Keys = new List<string>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The lowest and highest contribution m of any family, any segment, any company, any month (the clamp keeps it in [0.80; 1.20]).</summary>
        public double MinM = double.MaxValue;
        public double MaxM = double.MinValue;
        public long Evaluations;

        /// <summary>Before 1985: the biggest m of any family but the default one.</summary>
        public double MaxFamilyMBefore1985;

        /// <summary>Before 1985: the biggest ratio of m(family) to m(default family) in the same segment for the same company, with the supports and ecosystems the simulation had.</summary>
        public double MaxAdvantageBefore1985;
        public string MaxAdvantageWhere;

        /// <summary>
        /// Before 1985: the biggest advantage a family has over the default one by itself - the same support and the same ecosystem for both, so only the affinity and the ageing
        /// differ. This is what "the family gives" means; the number above also counts the support of the abandoned ISA that has run dry.
        /// </summary>
        public double PureAdvantageBefore1985;

        public int MonthsRun;
        public int Negative;     // company-months with negative money

        /// <summary>The month a company moved to a family (the first time), or null.</summary>
        public DateTime? MovedTo(string company, string family)
        {
            foreach (SimEvent e in Events)
            {
                if (e.Kind == "active" && e.Company == company && e.Family == family) return e.Date;
            }
            return null;
        }

        /// <summary>How many times a company changed its active family.</summary>
        public int Moves(string company)
        {
            int count = 0;
            foreach (SimEvent e in Events)
            {
                if (e.Kind == "active" && e.Company == company) count++;
            }
            return count;
        }

        public SimYear YearOf(int year)
        {
            foreach (SimYear row in Years)
            {
                if (row.Year == year) return row;
            }
            return null;
        }

        /// <summary>The active family of a company at the start of a year; null for a company that has not appeared by then.</summary>
        public string ActiveAt(int year, string key)
        {
            SimYear row = YearOf(year);
            string family;
            return row != null && row.Active.TryGetValue(key, out family) ? family : null;
        }
    }

    /// <summary>
    /// The offline run of the pure logic of the feature through 1975-2025, month by month, for the eleven AI designers of the content (TSK-029, design.md, "Балансовая
    /// симуляция"). It does what the services do once a month and once a day, with the same pure functions: the ecosystem (<see cref="EcosystemMath.Shares"/> over the
    /// sales of the last twelve months), the upkeep and the levels of the support (<see cref="SupportCycle.RunMonth"/>), the decision of the company
    /// (<see cref="ArchAiPolicy.DecideFull"/> on an <see cref="AiView"/>), the research (<see cref="ArchResearchMath"/>, the year factor, the experience of the ISA, the cost with
    /// M4), the seed of a company that appears later (<see cref="ArchSeedPolicy.SeedAi"/>) and the rating (<see cref="RatingForecast.One"/>, the function of the provider).
    /// <para>
    /// The sales are the simple model of the spec: the market of a segment is a number of CPUs that grows every year, and the share of a company in it is proportional to
    /// <c>size × presence × m</c>, where m is the rating multiplier of the architectures in the line-up of the company (a line-up follows the active family with a delay of
    /// <see cref="SimParams.LineupMonths"/> months, as CPUs stay on the market for years). The CPUs sold are attributed to the families of the line-up; that is the ecosystem.
    /// What the model leaves out on purpose: the shared lab (the technologies of the AI stand while an architecture is researched - checked by hand, M13), the player, the
    /// royalty, the contracts and every other effect on the sales. The simulation is deterministic.
    /// </para>
    /// </summary>
    internal sealed class BalanceSimulator
    {
        private sealed class Company
        {
            public SimCompanyDef Def;
            public AiPreference Pref;
            public CompanyArchState State;
            public double Money;
            public float Funding = 1f;
            public double[] Lineup;                       // by family index of the catalog; sums to 1
            public readonly List<double[]> SegmentUnits = new List<double[]>();   // by month: the units sold in the four segments
            public bool Alive;
        }

        private readonly ArchCatalog _catalog;
        private readonly AiPreferenceTable _preferences;
        private readonly BalanceConfig _balance;
        private readonly SimParams _p;
        private readonly List<SimCompanyDef> _defs;
        private readonly List<Company> _companies = new List<Company>();
        private readonly List<double[]> _familyUnits = new List<double[]>();      // by month: the units sold per family (catalog order)
        private readonly SimResult _result = new SimResult();
        private readonly RatingInputs _inputs = new RatingInputs();
        private double _upkeepYear;

        public BalanceSimulator(ArchCatalog catalog, AiPreferenceTable preferences, BalanceConfig balance, SimParams parameters, List<SimCompanyDef> companies)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (preferences == null) throw new ArgumentNullException("preferences");
            if (balance == null) throw new ArgumentNullException("balance");
            _catalog = catalog;
            _preferences = preferences;
            _balance = balance;
            _p = parameters ?? new SimParams();
            _defs = companies ?? StandardCompanies();
        }

        /// <summary>The eleven AI designers of the history table (UniqueID as in the spec); Cyrix and VIA appear in 1987 and 1988.</summary>
        public static List<SimCompanyDef> StandardCompanies()
        {
            return new List<SimCompanyDef>
            {
                new SimCompanyDef { Key = "intel", Name = "Intel", UniqueId = 0, Size = 1.00, SpawnYear = 0, IsPc = true, Presence = new[] { 0.25, 0.40, 0.30, 0.05 } },
                new SimCompanyDef { Key = "amd", Name = "AMD", UniqueId = 1, Size = 0.35, SpawnYear = 0, IsPc = true, Presence = new[] { 0.20, 0.40, 0.35, 0.05 } },
                new SimCompanyDef { Key = "zilog", Name = "Zilog", UniqueId = 2, Size = 0.10, SpawnYear = 0, IsEmbedded = true, Presence = new[] { 0.00, 0.00, 0.35, 0.65 } },
                new SimCompanyDef { Key = "texas-instruments", Name = "Texas Instruments", UniqueId = 3, Size = 0.25, SpawnYear = 0, IsEmbedded = true, Presence = new[] { 0.00, 0.05, 0.30, 0.65 } },
                new SimCompanyDef { Key = "ibm", Name = "IBM", UniqueId = 4, Size = 0.60, SpawnYear = 0, IsPc = true, Presence = new[] { 0.45, 0.35, 0.05, 0.15 } },
                new SimCompanyDef { Key = "fairchild", Name = "Fairchild", UniqueId = 5, Size = 0.15, SpawnYear = 0, Presence = new[] { 0.10, 0.30, 0.30, 0.30 } },
                new SimCompanyDef { Key = "toshiba", Name = "Toshiba", UniqueId = 6, Size = 0.20, SpawnYear = 0, IsEmbedded = true, Presence = new[] { 0.00, 0.10, 0.35, 0.55 } },
                new SimCompanyDef { Key = "motorola", Name = "Motorola", UniqueId = 7, Size = 0.30, SpawnYear = 0, Presence = new[] { 0.45, 0.15, 0.05, 0.35 } },
                new SimCompanyDef { Key = "cyrix", Name = "Cyrix", UniqueId = 10, Size = 0.08, SpawnYear = 1987, CostModifier = 6f, IsPc = true, Presence = new[] { 0.00, 0.40, 0.60, 0.00 } },
                new SimCompanyDef { Key = "via", Name = "VIA", UniqueId = 11, Size = 0.08, SpawnYear = 1988, IsPc = true, Presence = new[] { 0.00, 0.30, 0.60, 0.10 } },
                new SimCompanyDef { Key = "digital-devices", Name = "Digital Devices", UniqueId = 12, Size = 0.15, SpawnYear = 0, Presence = new[] { 0.60, 0.10, 0.00, 0.30 } },
            };
        }

        // ---- the run ----

        public SimResult Run()
        {
            foreach (SimCompanyDef def in _defs)
            {
                _result.Keys.Add(def.Key);
                _result.Names[def.Key] = def.Name;
            }

            for (int year = _p.StartYear; year <= _p.EndYear; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    int t = (year - _p.StartYear) * 12 + (month - 1);
                    Month(t, year, month);
                }
            }
            _result.PureAdvantageBefore1985 = PureAdvantage(_catalog, _balance, 1985);
            return _result;
        }

        /// <summary>
        /// The biggest ratio of a family's contribution to the default family's before <paramref name="beforeYear"/>, with the same support and ecosystem for both and over a grid of them
        /// (the clamp of the corridor is in): the share of the rating the family brings by itself.
        /// </summary>
        public static double PureAdvantage(ArchCatalog catalog, BalanceConfig balance, int beforeYear)
        {
            var inputs = new RatingInputs();
            ArchFamily proprietary = catalog.Default;
            double best = 0;
            foreach (ArchFamily family in catalog.Families)
            {
                if (ReferenceEquals(family, proprietary)) continue;
                for (int year = 1971; year < beforeYear; year++)
                {
                    for (int s = 0; s < 4; s++)
                    {
                        foreach (float support in new[] { 0f, 30f, 60f, 95f })
                        {
                            foreach (float eco in new[] { 0f, 0.2f, 0.5f, 1f })
                            {
                                float a = RatingForecast.One(inputs, family, (Segment)s, year, balance, support, eco, 1, false, 1f);
                                float p = RatingForecast.One(inputs, proprietary, (Segment)s, year, balance, support, eco, 1, false, 1f);
                                if (p > 0f) best = Math.Max(best, a / p);
                            }
                        }
                    }
                }
            }
            return best;
        }

        private void Month(int t, int year, int month)
        {
            var today = new DateTime(year, month, 1);
            SpawnDue(t, year);

            // 1. The ecosystem of the last full months (EcosystemService, the first step of the monthly handler).
            Dictionary<string, float> eco = MeasureEcosystem(t);
            float inflation = (float)Math.Pow(_p.InflationPerYear, year - 1971 + (month - 1) / 12.0);

            // 2. The support: the upkeep and the levels (SupportService). An AI company pays only what it has.
            foreach (Company c in _companies)
            {
                if (!c.Alive) continue;
                SupportMonthResult r = SupportCycle.RunMonth(c.State, _catalog, _balance.support, year, inflation, (float)Math.Max(0.0, c.Money));
                c.Money -= r.Upkeep;
                _upkeepYear += r.Upkeep;
            }

            // 3. The decision of every company (ArchitectureAi).
            foreach (Company c in _companies)
            {
                if (!c.Alive) continue;
                AiView view = ViewOf(c, t, year, inflation);
                AiDecision decision = ArchAiPolicy.DecideFull(view, c.Pref, _catalog, _balance.ai, eco, _balance.rating, _balance.support);
                Apply(c, decision, today);
            }

            // 4. The month itself: the daily research, then the sales.
            foreach (Company c in _companies)
            {
                if (c.Alive) Research(c, year, month);
            }
            Market(t, year, eco);

            // 5. The line-ups follow the active families.
            double keep = Math.Exp(-1.0 / Math.Max(1.0, _p.LineupMonths));
            foreach (Company c in _companies)
            {
                if (!c.Alive) continue;
                int active = _catalog.IndexOf(c.State.Active);
                for (int f = 0; f < c.Lineup.Length; f++) c.Lineup[f] *= keep;
                if (active >= 0) c.Lineup[active] += 1.0 - keep;
            }

            _result.MonthsRun++;
            if (month == 1) Snapshot(year, eco);
        }

        // ---- the companies ----

        private void SpawnDue(int t, int year)
        {
            foreach (SimCompanyDef def in _defs)
            {
                bool spawn = (def.SpawnYear <= 0 && t == 0) || (def.SpawnYear > 0 && def.SpawnYear == year && !Has(def));
                if (!spawn) continue;
                if (def.SpawnYear > 0 && year < _p.StartYear) continue;

                AiPreference pref = _preferences.Match(def.UniqueId);
                if (_p.WeightScale != null) pref = Scaled(pref, def.Key);

                var state = new CompanyArchState(def.UniqueId + 1000, false);
                SeedResult seed = ArchSeedPolicy.SeedAi(_catalog, pref, year, _balance.seed);
                seed.ApplyTo(state);

                var company = new Company { Def = def, Pref = pref, State = state, Alive = true, Lineup = new double[_catalog.Count] };
                company.Funding = LabRules.AiFunding(_p.Difficulty, _balance.ai.fundingByDifficulty);
                int active = _catalog.IndexOf(state.Active);
                if (active >= 0) company.Lineup[active] = 1.0;
                company.Money = _p.StartMonthsOfProfit * MonthlyProfitAtStart(def, year);
                while (company.SegmentUnits.Count < t) company.SegmentUnits.Add(new double[4]);   // no sales before it appeared
                _companies.Add(company);
            }
        }

        private bool Has(SimCompanyDef def)
        {
            foreach (Company c in _companies)
            {
                if (ReferenceEquals(c.Def, def)) return true;
            }
            return false;
        }

        private AiPreference Scaled(AiPreference pref, string key)
        {
            var copy = new AiPreference { Id = pref.Id, UniqueId = pref.UniqueId, IsDefault = pref.IsDefault, LagYears = pref.LagYears };
            foreach (KeyValuePair<string, float> pair in pref.Weights) copy.Weights[pair.Key] = pair.Value * _p.WeightScale(key, pair.Key);
            return copy;
        }

        /// <summary>The profit a company makes in a month of the year it appears: its share of the market revenue times the margin.</summary>
        private double MonthlyProfitAtStart(SimCompanyDef def, int year)
        {
            double totalWeight = 0;
            foreach (SimCompanyDef other in _defs)
            {
                if (other.SpawnYear <= 0 || other.SpawnYear <= year) totalWeight += other.Size;
            }
            double revenue = 0;
            for (int s = 0; s < 4; s++) revenue += Units(s, (year - _p.StartYear) * 12) * _p.Price[s];
            return revenue * (def.Size / Math.Max(0.001, totalWeight)) * _p.Margin;
        }

        private AiView ViewOf(Company c, int t, int year, float inflation)
        {
            return new AiView
            {
                CompanyId = c.State.CompanyId,
                UniqueId = c.Def.UniqueId,
                Money = (float)c.Money,
                CostModifier = c.Def.CostModifier,
                Difficulty = _p.Difficulty,
                Year = year,
                Inflation = inflation,
                TechnologyDaysLeft = -1,
                SegmentShare = SegmentShareOf(c, t),
                State = c.State,
            };
        }

        /// <summary>The shares of the sales of the last months by segment; with no sales yet the segments the company sells in (what the votes of the price segments of its CPUs would say).</summary>
        private float[] SegmentShareOf(Company c, int t)
        {
            var sum = new double[4];
            int months = Math.Max(1, _balance.ai.salesWindowMonths);
            for (int k = 1; k <= months; k++)
            {
                int index = t - k;
                if (index < 0 || index >= c.SegmentUnits.Count) continue;
                for (int s = 0; s < 4; s++) sum[s] += c.SegmentUnits[index][s];
            }
            float[] shares = SegmentShares.FromSales((long)sum[0], (long)sum[1], (long)sum[2], (long)sum[3]);
            if (shares != null) return shares;

            var votes = new int[4];
            for (int s = 0; s < 4; s++) votes[s] = (int)Math.Round(c.Def.Presence[s] * 100.0);
            return SegmentShares.FromVotes(votes) ?? SegmentShares.Equal();
        }

        /// <summary>What ArchitectureAi.Apply does with a decision, without the game.</summary>
        private void Apply(Company c, AiDecision decision, DateTime today)
        {
            CompanyArchState state = c.State;
            if (decision.Pause)
            {
                state.Paused = true;
                state.PauseReason = PauseReason.Default;
            }
            if (decision.StartResearch != null)
            {
                state.CurrentResearch = decision.StartResearch;
                state.Paused = false;
                state.PauseReason = PauseReason.None;
                state.GetOrAddFamily(decision.StartResearch);
                if (decision.Funding > 0f) c.Funding = decision.Funding;
            }
            if (decision.NewActive != null)
            {
                string previous = state.Active;
                state.Active = decision.NewActive;
                _result.Events.Add(new SimEvent { Date = today, Company = c.Def.Key, Kind = "active", Family = decision.NewActive, From = previous });
            }
            if (decision.Tiers != null)
            {
                foreach (KeyValuePair<string, SupportTier> pair in decision.Tiers) SupportCycle.SetTier(state, _catalog, pair.Key, pair.Value, false);
            }
        }

        /// <summary>The daily tick of ArchResearchService over one month.</summary>
        private void Research(Company c, int year, int month)
        {
            CompanyArchState state = c.State;
            if (state.CurrentResearch == null) return;

            if (c.Money < 0)
            {
                state.Paused = true;
                state.PauseReason = PauseReason.Default;
                _result.Negative++;
            }
            else if (state.Paused && LabRules.AutoResumes(state.PauseReason))
            {
                state.Paused = false;
                state.PauseReason = PauseReason.None;
            }
            if (state.Paused) return;

            ArchFamily family = _catalog.Get(state.CurrentResearch);
            FamilyState progress = state.GetOrAddFamily(family.Id);

            int days = DateTime.DaysInMonth(year, month);
            float yearFactor = ArchResearchMath.YearFactor(family.Year, new DateTime(year, month, 15), _balance.research);
            float isa = ArchResearchMath.IsaFactor(family, state.Families, _catalog, _balance.research.isaExperienceFactor, true);
            float effective = ArchResearchMath.EffectiveDays(family.Research.BaseDays, yearFactor, isa);
            float daily = ArchResearchMath.DailyProgress((float)Math.Sqrt(c.Funding), effective);
            float dailyCost = ArchResearchMath.DailyCost(family.Research.MonthlyCost, c.Def.CostModifier, c.Funding);

            int used = days;
            if (daily > 0f)
            {
                int needed = (int)Math.Ceiling((1f - progress.Progress) / daily);
                used = Math.Max(1, Math.Min(days, needed));
            }
            progress.Progress = Math.Min(1f, progress.Progress + daily * used);
            c.Money -= dailyCost * used;

            if (progress.Progress >= 1f) Complete(c, family, progress, year, month);
        }

        private void Complete(Company c, ArchFamily family, FamilyState progress, int year, int month)
        {
            CompanyArchState state = c.State;
            progress.Researched = true;
            progress.Progress = 1f;
            progress.ResearchedYear = year;
            progress.ResearchedMonth = month;
            float start = SupportMath.StartSupport(family, state.Families, _catalog, _balance.support);
            if (progress.Support < start) progress.Support = start;

            state.CurrentResearch = null;
            state.Paused = false;
            state.PauseReason = PauseReason.None;
            _result.Events.Add(new SimEvent { Date = new DateTime(year, month, 28), Company = c.Def.Key, Kind = "researched", Family = family.Id });
        }

        // ---- the market ----

        private double Units(int segment, int t)
        {
            return _p.UnitsPerMonth[segment] * Math.Pow(_p.MarketGrowth, t / 12.0);
        }

        private Dictionary<string, float> MeasureEcosystem(int t)
        {
            var sales = new Dictionary<string, long>(StringComparer.Ordinal);
            int months = Math.Max(1, _balance.ecosystem.windowMonths);
            for (int k = 1; k <= months; k++)
            {
                int index = t - k;
                if (index < 0 || index >= _familyUnits.Count) continue;
                for (int f = 0; f < _catalog.Count; f++)
                {
                    long units = (long)_familyUnits[index][f];
                    if (units <= 0) continue;
                    string id = _catalog.Families[f].Id;
                    long have;
                    sales.TryGetValue(id, out have);
                    sales[id] = have + units;
                }
            }
            return EcosystemMath.Shares(sales, _balance.ecosystem.fallbackFamily);
        }

        /// <summary>The contribution m of a family to the rating of a CPU in a segment this year: the function of the provider, with the support of the owner and the ecosystem of the family.</summary>
        private float Contribution(ArchFamily family, Segment segment, int year, CompanyArchState state, Dictionary<string, float> eco)
        {
            FamilyState own = state.GetFamily(family.Id);
            float support = own == null ? 0f : own.Support;
            float share;
            if (!eco.TryGetValue(family.Id, out share)) share = 0f;
            return RatingForecast.One(_inputs, family, segment, year, _balance, support, share, CoreCount(year, segment), Smt(year, segment), 1f);
        }

        private static int CoreCount(int year, Segment segment)
        {
            switch (segment)
            {
                case Segment.HighEnd:
                case Segment.MidRange:
                    return year < 2005 ? 1 : (year < 2008 ? 2 : (year < 2013 ? 4 : 8));
                case Segment.LowEnd:
                    return year < 2008 ? 1 : (year < 2014 ? 2 : 4);
                default:
                    return 1;
            }
        }

        private static bool Smt(int year, Segment segment)
        {
            return year >= 2002 && (segment == Segment.HighEnd || segment == Segment.MidRange);
        }

        private void Market(int t, int year, Dictionary<string, float> eco)
        {
            var familyUnits = new double[_catalog.Count];
            foreach (Company c in _companies) c.SegmentUnits.Add(new double[4]);

            for (int s = 0; s < 4; s++)
            {
                var segment = (Segment)s;
                double total = 0;
                var weight = new double[_companies.Count];
                var mean = new double[_companies.Count];
                for (int i = 0; i < _companies.Count; i++)
                {
                    Company c = _companies[i];
                    if (!c.Alive || c.Def.Presence[s] <= 0) continue;

                    double m = 0;
                    for (int f = 0; f < c.Lineup.Length; f++)
                    {
                        if (c.Lineup[f] < 1e-6) continue;
                        float value = Contribution(_catalog.Families[f], segment, year, c.State, eco);
                        Track(value, year);
                        m += c.Lineup[f] * value;
                    }
                    mean[i] = m;
                    weight[i] = c.Def.Size * c.Def.Presence[s] * m;
                    total += weight[i];
                }
                if (total <= 0) continue;

                double units = Units(s, t);
                for (int i = 0; i < _companies.Count; i++)
                {
                    if (weight[i] <= 0) continue;
                    Company c = _companies[i];
                    double sold = units * weight[i] / total;
                    c.SegmentUnits[c.SegmentUnits.Count - 1][s] = sold;
                    c.Money += sold * _p.Price[s] * _p.Margin;
                    for (int f = 0; f < c.Lineup.Length; f++) familyUnits[f] += sold * c.Lineup[f];
                }
            }
            _familyUnits.Add(familyUnits);

            if (year < 1985) CheckEarlyAdvantage(year, eco);
        }

        private void Track(float value, int year)
        {
            _result.Evaluations++;
            if (value < _result.MinM) _result.MinM = value;
            if (value > _result.MaxM) _result.MaxM = value;
        }

        /// <summary>Before 1985 no family may give a CPU more than +20 % over the default family: the biggest m and the biggest ratio to the default family, in the state the simulation is in.</summary>
        private void CheckEarlyAdvantage(int year, Dictionary<string, float> eco)
        {
            ArchFamily proprietary = _catalog.Default;
            foreach (Company c in _companies)
            {
                if (!c.Alive) continue;
                for (int s = 0; s < 4; s++)
                {
                    var segment = (Segment)s;
                    float baseline = Contribution(proprietary, segment, year, c.State, eco);
                    foreach (ArchFamily family in _catalog.Families)
                    {
                        if (ReferenceEquals(family, proprietary)) continue;
                        FamilyState own = c.State.GetFamily(family.Id);
                        if (own == null || !own.Researched) continue;
                        float value = Contribution(family, segment, year, c.State, eco);
                        if (value > _result.MaxFamilyMBefore1985) _result.MaxFamilyMBefore1985 = value;
                        double ratio = baseline > 0f ? value / baseline : 0.0;
                        if (ratio > _result.MaxAdvantageBefore1985)
                        {
                            _result.MaxAdvantageBefore1985 = ratio;
                            _result.MaxAdvantageWhere = c.Def.Name + ", " + family.Id + ", " + segment + ", " + year;
                        }
                    }
                }
            }
        }

        // ---- the report ----

        private void Snapshot(int year, Dictionary<string, float> eco)
        {
            var row = new SimYear { Year = year, TotalUpkeep = _upkeepYear };
            _upkeepYear = 0;
            foreach (KeyValuePair<string, float> pair in eco) row.Ecosystem[pair.Key] = pair.Value;
            row.MinM = _result.MinM;
            row.MaxM = _result.MaxM;

            foreach (Company c in _companies)
            {
                if (!c.Alive) continue;
                row.Active[c.Def.Key] = c.State.Active;
                FamilyState active = c.State.GetFamily(c.State.Active);
                row.Support[c.Def.Key] = active == null ? 0f : active.Support;
                if (c.Def.IsPc)
                {
                    row.PcAlive++;
                    if (string.Equals(_catalog.Get(c.State.Active) == null ? null : _catalog.Get(c.State.Active).Isa, "x86", StringComparison.Ordinal)) row.PcOnX86++;
                }
                if (c.Def.IsEmbedded)
                {
                    row.EmbeddedAlive++;
                    if (string.Equals(_catalog.Get(c.State.Active) == null ? null : _catalog.Get(c.State.Active).Isa, "arm", StringComparison.Ordinal)) row.EmbeddedOnArm++;
                }
            }
            _result.Years.Add(row);
        }

        /// <summary>
        /// The report for the owner: the active family of every company at the start of the year (a table by year), the ecosystem shares, the range of m and the metrics.
        /// <paramref name="step"/> is how many years between the rows.
        /// </summary>
        public static string Report(SimResult result, ArchCatalog catalog, int step)
        {
            var sb = new StringBuilder();
            string[] short1 = new string[catalog.Count];
            for (int i = 0; i < catalog.Count; i++) short1[i] = Short(catalog.Families[i].Id);

            sb.AppendLine("Балансовая симуляция чистой логики cpu-architectures: помесячный прогон, 11 ИИ-проектировщиков, продажи ∝ m.");
            sb.AppendLine("Состояние на начало года (после январского цикла). Сокращения: own = собственная ISA, x86, risc, arm, x64 = x86-64, a64 = ARM64, rv = RISC-V.");
            sb.AppendLine();

            // the header
            sb.Append("Год  ");
            foreach (string key in result.Keys) sb.Append(Pad(Abbrev(result.Names[key]), 6));
            sb.Append(" | ПК x86 | встр. ARM | ");
            foreach (string s in short1) sb.Append(Pad(s, 5));
            sb.AppendLine("| поддержка, млн/год");

            foreach (SimYear row in result.Years)
            {
                if (step > 1 && (row.Year - result.Years[0].Year) % step != 0 && row.Year != 1995 && row.Year != 2015 && row.Year != result.Years[result.Years.Count - 1].Year) continue;

                sb.Append(row.Year.ToString(CultureInfo.InvariantCulture)).Append("  ");
                foreach (string key in result.Keys)
                {
                    string family;
                    sb.Append(Pad(row.Active.TryGetValue(key, out family) ? Short(family) : "-", 6));
                }
                sb.Append(" | ").Append(Pad(row.PcOnX86 + "/" + row.PcAlive, 6));
                sb.Append(" | ").Append(Pad(row.EmbeddedOnArm + "/" + row.EmbeddedAlive, 9)).Append(" | ");
                foreach (ArchFamily family in catalog.Families)
                {
                    float share;
                    row.Ecosystem.TryGetValue(family.Id, out share);
                    sb.Append(Pad(Math.Round(share * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%", 5));
                }
                sb.Append("| ").AppendLine((row.TotalUpkeep / 1e6).ToString("0.0", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine("Смены активной архитектуры и завершённые исследования:");
            foreach (SimEvent e in result.Events)
            {
                sb.Append("  ").Append(e.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Append(' ').Append(Pad(result.Names[e.Company], 18));
                if (e.Kind == "active") sb.AppendLine("переходит: " + (e.From == null ? "-" : e.From) + " -> " + e.Family);
                else sb.AppendLine("исследовала " + e.Family);
            }

            sb.AppendLine();
            sb.AppendLine("Вклад m в рейтинг за весь прогон: min " + result.MinM.ToString("0.000", CultureInfo.InvariantCulture) + ", max " + result.MaxM.ToString("0.000", CultureInfo.InvariantCulture)
                          + " (" + result.Evaluations + " вычислений)");
            sb.AppendLine("До 1985: наибольший m любого семейства, кроме собственной ISA: " + result.MaxFamilyMBefore1985.ToString("0.000", CultureInfo.InvariantCulture)
                          + "; собственное преимущество семейства над собственной ISA (одинаковые поддержка и экосистема): " + result.PureAdvantageBefore1985.ToString("0.000", CultureInfo.InvariantCulture)
                          + "; с учётом того, что брошенная ISA осталась без поддержки: " + result.MaxAdvantageBefore1985.ToString("0.000", CultureInfo.InvariantCulture)
                          + " (" + result.MaxAdvantageWhere + ")");
            sb.AppendLine("Месяцев: " + result.MonthsRun + ", компания-месяцев с отрицательными деньгами: " + result.Negative);
            return sb.ToString();
        }

        private static string Short(string familyId)
        {
            switch (familyId)
            {
                case "proprietary": return "own";
                case "x86_64": return "x64";
                case "arm64": return "a64";
                case "riscv": return "rv";
                default: return familyId ?? "-";
            }
        }

        private static string Abbrev(string name)
        {
            switch (name)
            {
                case "Texas Instruments": return "TI";
                case "Digital Devices": return "DD";
                case "Fairchild": return "Fchld";
                case "Motorola": return "Motor";
                case "Toshiba": return "Tosh";
                default: return name.Length > 5 ? name.Substring(0, 5) : name;
            }
        }

        private static string Pad(string text, int width)
        {
            return text.Length >= width ? text + " " : text.PadRight(width);
        }
    }
}
