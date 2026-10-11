using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>What the player decided about the history of the game that changes how roles are found (FR-17: the mode; the debug flag of the chronicle page).</summary>
    public sealed class RoleOptions
    {
        /// <summary><see cref="HistMode.Strict"/>: a <c>named</c> role does not use its fallback chain.</summary>
        public HistMode Mode { get; set; } = HistMode.Adaptive;

        /// <summary>
        /// <c>debugForceLeader</c> (design.md, Component 4): the player is the first candidate of every <c>leader</c> and <c>challenger</c> role and passes
        /// the share checks of those roles. Only the debug tools of the chronicle set it.
        /// </summary>
        public bool ForceLeader { get; set; }

        public static RoleOptions Adaptive
        {
            get { return new RoleOptions(); }
        }
    }

    /// <summary>
    /// The companies found for the roles of an entry (design.md, Component 4): <see cref="Ok"/> when every role was recognized, otherwise
    /// <see cref="FailedRole"/> names the first that was not. <see cref="Targets"/> maps a role name to the company keys in the order they were found
    /// (one key for a single company, several for a group, none for <c>global</c>). Also the form in which the roles are kept between the stages of an
    /// entry (<see cref="FromTargets"/>, <see cref="ToTargets"/>).
    /// </summary>
    public sealed class RoleResult
    {
        private static readonly string[] NoKeys = new string[0];

        public bool Ok { get; set; } = true;

        /// <summary>The first role that could not be found; null when <see cref="Ok"/>.</summary>
        public string FailedRole { get; set; }

        public Dictionary<string, List<string>> Targets { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>The companies of a role; an empty list for a role that is not there.</summary>
        public IReadOnlyList<string> KeysOf(string role)
        {
            List<string> keys;
            return role != null && Targets.TryGetValue(role, out keys) ? keys : (IReadOnlyList<string>)NoKeys;
        }

        /// <summary>The first company of a role, or null.</summary>
        public string FirstKey(string role)
        {
            IReadOnlyList<string> keys = KeysOf(role);
            return keys.Count == 0 ? null : keys[0];
        }

        /// <summary>The role is known to this result (found, even if it holds no company, like <c>global</c>).</summary>
        public bool Has(string role)
        {
            return role != null && Targets.ContainsKey(role);
        }

        public bool Contains(string role, string companyKey)
        {
            IReadOnlyList<string> keys = KeysOf(role);
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], companyKey, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>A result built from what an entry kept in the save; always <see cref="Ok"/>.</summary>
        public static RoleResult FromTargets(IEnumerable<RoleTargetsDto> targets)
        {
            var result = new RoleResult();
            if (targets == null) return result;

            foreach (RoleTargetsDto target in targets)
            {
                if (target == null || string.IsNullOrEmpty(target.role)) continue;
                result.Targets[target.role] = target.companyKeys == null ? new List<string>() : new List<string>(target.companyKeys);
            }
            return result;
        }

        /// <summary>The roles in the form of the save, in the order of the names (the same result always gives the same list).</summary>
        public List<RoleTargetsDto> ToTargets()
        {
            var names = new List<string>(Targets.Keys);
            names.Sort(StringComparer.Ordinal);

            var list = new List<RoleTargetsDto>(names.Count);
            foreach (string name in names) list.Add(new RoleTargetsDto { role = name, companyKeys = new List<string>(Targets[name]) });
            return list;
        }

        public override string ToString()
        {
            if (!Ok) return "roles failed at " + FailedRole;

            var parts = new List<string>();
            foreach (KeyValuePair<string, List<string>> pair in Targets) parts.Add(pair.Key + "=" + string.Join("+", pair.Value));
            return "roles " + (parts.Count == 0 ? "(none)" : string.Join(", ", parts));
        }
    }

    /// <summary>
    /// Finds the companies that play the roles of an entry in the game that is being played (FR-3; design.md, Component 4). The kinds of role are
    /// the table of the design; the rules around them:
    /// <list type="bullet">
    /// <item><b>minShare.</b> A single company is accepted only with a share (in the role's segments, over the role's months) of at least
    /// <c>MinShare</c>; below it the role is not recognized. A group keeps only the members that pass.</item>
    /// <item><b>fallback.</b> A role that is not recognized tries its <c>Fallback</c> chain in order, each link with its own kind, segments and share.
    /// In the <see cref="HistMode.Strict"/> mode a <c>named</c> role does not use the chain: the event does not happen without that company.</item>
    /// <item><b>One company, one role.</b> A company that already plays a role of the entry is not taken for another single-company role: the next candidate
    /// is (the plaintiff is never the defendant); with no next candidate the role is not recognized. Groups do not take companies away from single roles, and neither
    /// does the built-in role <c>player</c> that a fire names without declaring it: the player is the player whatever else the entry made of him (the dispatcher then
    /// does not send him a second window of the same entry, see <see cref="DispatcherCore"/>).</item>
    /// <item><b>Determinism.</b> Ties go to the player, then to the smaller <c>UniqueID</c>; only <c>randomManufacturer</c> draws a random number, from the
    /// generator it is given, once, and only when it has candidates. Bankrupt companies play nothing.</item>
    /// </list>
    /// Roles that fires and conditions use without declaring them (<c>player</c>, <c>allCpuCompanies</c>, <c>allManufacturers</c>, <c>allFabless</c>) are found
    /// too and stored under their own names; a group that is empty makes the result not Ok (nobody to fire for). Pure logic, no game types.
    /// </summary>
    public sealed class RoleResolver
    {
        private const int MaxFallbackDepth = 8;
        private const int PerfPerWattMonths = 36;
        private const float MinPowerForRatio = 0.5f;
        private const float ShareTolerance = 1e-6f;

        /// <summary>
        /// Finds the companies for every role of <paramref name="entry"/> (declared ones in order, then the built-in roles its fires and conditions use).
        /// The share cache is brought up to date by the caller. Never throws for a damaged world (null lists count as empty).
        /// </summary>
        public RoleResult Resolve(CalendarEntry entry, IWorldView world, MarketShareCache shares, RoleOptions options, IModRng rng)
        {
            var result = new RoleResult();
            if (entry == null || world == null) return result;

            var run = new Run(world, shares ?? new MarketShareCache(), options ?? new RoleOptions(), rng);

            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (entry.Roles != null)
            {
                foreach (RoleSpec spec in entry.Roles)
                {
                    if (spec == null || string.IsNullOrEmpty(spec.Name)) continue;
                    declared.Add(spec.Name);

                    List<string> keys = ResolveSpec(spec, run, 0);
                    if (keys == null) return Fail(result, spec.Name);

                    result.Targets[spec.Name] = keys;
                    if (!run.LastWasGroup) run.Assigned.UnionWith(keys);
                }
            }

            foreach (string name in BuiltInRolesOf(entry))
            {
                if (declared.Contains(name) || result.Targets.ContainsKey(name)) continue;

                List<string> keys = ResolveBuiltIn(name, run);
                if (keys == null) return Fail(result, name);
                result.Targets[name] = keys;
            }
            return result;
        }

        /// <summary>
        /// The built-in role names an entry uses without declaring them, other than <c>global</c> (which needs no company): the roles of its fires,
        /// fallback fires, <c>onlyIf</c> conditions and conditions. In the order of first use.
        /// </summary>
        public static IReadOnlyList<string> BuiltInRolesOf(CalendarEntry entry)
        {
            var names = new List<string>();
            if (entry == null) return names;

            if (entry.Stages != null)
            {
                foreach (StageSpec stage in entry.Stages)
                {
                    if (stage == null || stage.Fires == null) continue;
                    foreach (FireSpec fire in stage.Fires) AddFire(fire, names);
                }
            }
            if (entry.FallbackFires != null)
            {
                foreach (FireSpec fire in entry.FallbackFires) AddFire(fire, names);
            }
            if (entry.ConditionUse != null) AddNames(entry.ConditionUse.Roles, names);
            return names;
        }

        private static void AddFire(FireSpec fire, List<string> names)
        {
            if (fire == null) return;
            AddName(fire.Role, names);
            if (fire.OnlyIfUse != null) AddNames(fire.OnlyIfUse.Roles, names);
        }

        private static void AddNames(IEnumerable<string> roles, List<string> names)
        {
            if (roles == null) return;
            foreach (string role in roles) AddName(role, names);
        }

        private static void AddName(string role, List<string> names)
        {
            if (role == null || role == RoleKinds.Global || !RoleKinds.IsImplicitRole(role) || names.Contains(role)) return;
            names.Add(role);
        }

        private static RoleResult Fail(RoleResult result, string role)
        {
            result.Ok = false;
            result.FailedRole = role;
            return result;
        }

        // ---- one role ----

        private List<string> ResolveSpec(RoleSpec spec, Run run, int depth)
        {
            List<string> keys = ResolveOne(spec, run);
            if (keys != null) return keys;
            if (run.Options.Mode == HistMode.Strict && spec.Kind == RoleKinds.Named) return null;
            if (spec.Fallback == null || depth >= MaxFallbackDepth) return null;

            foreach (RoleSpec link in spec.Fallback)
            {
                if (link == null) continue;
                keys = ResolveSpec(link, run, depth + 1);
                if (keys != null) return keys;
            }
            return null;
        }

        private List<string> ResolveOne(RoleSpec spec, Run run)
        {
            run.LastWasGroup = false;
            switch (spec.Kind)
            {
                case RoleKinds.Leader: return One(PickRanked(spec, run, false));
                case RoleKinds.Challenger: return One(PickRanked(spec, run, true));
                case RoleKinds.Named: return One(PickNamed(spec, run));
                case RoleKinds.FoundryLeader: return One(PickFoundryLeader(spec, run));
                case RoleKinds.BestPerfPerWatt: return One(PickBestPerfPerWatt(spec, run));
                case RoleKinds.NewestEntrant: return One(PickNewestEntrant(spec, run));
                case RoleKinds.RandomManufacturer: return One(PickRandomManufacturer(spec, run));
                case RoleKinds.Player: return One(PickPlayer(run));
                case RoleKinds.Global:
                    run.LastWasGroup = true;
                    return new List<string>();
                case RoleKinds.AllCpuCompanies:
                case RoleKinds.AllManufacturers:
                case RoleKinds.AllFabless:
                    run.LastWasGroup = true;
                    return Group(spec.Kind, spec, run);
                default:
                    return null;
            }
        }

        /// <summary>A role that fires and conditions name without declaring it: <c>player</c> or one of the groups, with no share requirement.</summary>
        private List<string> ResolveBuiltIn(string name, Run run)
        {
            run.LastWasGroup = true;

            // Not PickPlayer: that one refuses a player who already plays a declared role, and a fire that names "player" (the follower of a price war, the partner of a
            // brand programme) is meant for the player also when he is the aggressor or the leader. Refusing would make the whole entry impossible for a player who leads.
            if (name == RoleKinds.Player) return One(run.Player ?? new CompanyInfo { Key = CompanyKey.Player, IsPlayer = true, Name = CompanyKey.Player });
            return Group(name, new RoleSpec { Name = name, Kind = name }, run);
        }

        private static List<string> One(CompanyInfo company)
        {
            return company == null ? null : new List<string> { company.Key };
        }

        // ---- the kinds ----

        private CompanyInfo PickRanked(RoleSpec spec, Run run, bool challenger)
        {
            SegmentMask mask = WorldMath.MaskOf(spec.Segments);
            int months = Months(spec, run);
            IReadOnlyList<string> ranking = run.Shares.Ranking(mask, months);

            var candidates = new List<string>(ranking.Count + 1);
            if (run.Options.ForceLeader && run.Player != null) candidates.Add(run.Player.Key);
            for (int i = challenger ? 1 : 0; i < ranking.Count; i++)
            {
                if (run.Options.ForceLeader && run.Player != null && ranking[i] == run.Player.Key) continue;
                candidates.Add(ranking[i]);
            }

            // The first candidate nobody else plays: the one who counts (the share of a later one is never higher).
            foreach (string key in candidates)
            {
                CompanyInfo company = run.Find(key);
                if (company == null || company.IsBankrupt || run.Assigned.Contains(key)) continue;
                return ShareOk(company, spec, run) ? company : null;
            }
            return null;
        }

        private CompanyInfo PickNamed(RoleSpec spec, Run run)
        {
            foreach (CompanyInfo company in run.Companies)
            {
                if (company == null || company.IsPlayer || company.UniqueId != spec.UniqueId) continue;
                if (company.IsBankrupt || run.Assigned.Contains(company.Key)) return null;
                if (spec.RequireFoundry && !company.IsFoundry) return null;
                if (spec.RequireCapacity && company.Capacity <= 0) return null;
                return ShareOk(company, spec, run) ? company : null;
            }
            return null;
        }

        private CompanyInfo PickFoundryLeader(RoleSpec spec, Run run)
        {
            CompanyInfo best = null;
            foreach (CompanyInfo company in run.Companies)
            {
                if (company == null || company.IsBankrupt || !company.IsFoundry || company.Capacity <= 0 || run.Assigned.Contains(company.Key)) continue;
                if (best == null || company.Capacity > best.Capacity || (company.Capacity == best.Capacity && company.UniqueId < best.UniqueId)) best = company;
            }
            return best != null && ShareOk(best, spec, run) ? best : null;
        }

        private CompanyInfo PickBestPerfPerWatt(RoleSpec spec, Run run)
        {
            SimpleDate today = run.World.Today;
            CompanyInfo bestOwner = null;
            CpuInfo best = null;
            double bestScore = double.NegativeInfinity;

            IReadOnlyList<CpuInfo> cpus = run.World.Cpus;
            for (int i = 0; cpus != null && i < cpus.Count; i++)
            {
                CpuInfo cpu = cpus[i];
                if (!WorldMath.IsOnMarket(cpu) || !WorldMath.IsRecent(cpu, today, PerfPerWattMonths)) continue;

                CompanyInfo owner = run.Find(cpu.CompanyKey);
                if (owner == null || owner.IsBankrupt || run.Assigned.Contains(owner.Key)) continue;

                double score = cpu.Ips / Math.Max(cpu.Power, MinPowerForRatio);
                if (double.IsNaN(score)) continue;
                if (best == null || score > bestScore || (score == bestScore && cpu.SaveId < best.SaveId))
                {
                    best = cpu;
                    bestOwner = owner;
                    bestScore = score;
                }
            }
            return bestOwner != null && ShareOk(bestOwner, spec, run) ? bestOwner : null;
        }

        private CompanyInfo PickNewestEntrant(RoleSpec spec, Run run)
        {
            var released = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<CpuInfo> cpus = run.World.Cpus;
            for (int i = 0; cpus != null && i < cpus.Count; i++)
            {
                if (cpus[i] != null && cpus[i].Released && cpus[i].CompanyKey != null) released.Add(cpus[i].CompanyKey);
            }

            CompanyInfo best = null;
            foreach (CompanyInfo company in run.Companies)
            {
                if (company == null || company.IsBankrupt || !released.Contains(company.Key) || run.Assigned.Contains(company.Key)) continue;
                if (best == null) { best = company; continue; }

                int founded = WorldMath.FoundedIndex(company);
                int bestFounded = WorldMath.FoundedIndex(best);
                if (founded > bestFounded || (founded == bestFounded && company.UniqueId < best.UniqueId)) best = company;
            }
            return best != null && ShareOk(best, spec, run) ? best : null;
        }

        private CompanyInfo PickRandomManufacturer(RoleSpec spec, Run run)
        {
            var candidates = new List<CompanyInfo>();
            foreach (CompanyInfo company in run.Companies)
            {
                if (company == null || company.IsPlayer || company.IsBankrupt || company.IsFabless || company.Capacity <= 0) continue;
                if (run.Assigned.Contains(company.Key) || !ShareOk(company, spec, run)) continue;
                candidates.Add(company);
            }
            if (candidates.Count == 0) return null;
            if (run.Rng == null) return candidates[0];
            return candidates[run.Rng.Range(0, candidates.Count)];
        }

        private static CompanyInfo PickPlayer(Run run)
        {
            if (run.Assigned.Contains(CompanyKey.Player)) return null;
            return run.Player ?? new CompanyInfo { Key = CompanyKey.Player, IsPlayer = true, Name = CompanyKey.Player };
        }

        private List<string> Group(string kind, RoleSpec spec, Run run)
        {
            var keys = new List<string>();
            foreach (CompanyInfo company in run.Companies)
            {
                if (company == null || company.IsBankrupt) continue;

                bool member = kind == RoleKinds.AllCpuCompanies ? company.IsCpuCompany
                    : kind == RoleKinds.AllManufacturers ? company.HasManufacturing
                    : company.IsFabless;
                if (member && ShareOk(company, spec, run)) keys.Add(company.Key);
            }
            return keys.Count == 0 ? null : keys;
        }

        // ---- shares ----

        private static int Months(RoleSpec spec, Run run)
        {
            int months = spec.Months < 1 ? RoleSpec.DefaultMonths : spec.Months;
            return months > run.Shares.MaxMonths && run.Shares.MaxMonths > 0 ? run.Shares.MaxMonths : months;
        }

        /// <summary>The company has at least the share the role asks for (a role without a requirement accepts everybody).</summary>
        private static bool ShareOk(CompanyInfo company, RoleSpec spec, Run run)
        {
            if (spec.MinShare <= 0f) return true;
            if (run.Options.ForceLeader && company.IsPlayer && (spec.Kind == RoleKinds.Leader || spec.Kind == RoleKinds.Challenger)) return true;

            float share = run.Shares.Share(company.Key, WorldMath.MaskOf(spec.Segments), Months(spec, run));
            return share + ShareTolerance >= spec.MinShare;
        }

        /// <summary>The state of one call of <see cref="Resolve"/>.</summary>
        private sealed class Run
        {
            private readonly Dictionary<string, CompanyInfo> _byKey = new Dictionary<string, CompanyInfo>(StringComparer.Ordinal);

            public Run(IWorldView world, MarketShareCache shares, RoleOptions options, IModRng rng)
            {
                World = world;
                Shares = shares;
                Options = options;
                Rng = rng;

                IReadOnlyList<CompanyInfo> companies = world.Companies;
                Companies = companies ?? new CompanyInfo[0];
                foreach (CompanyInfo company in Companies)
                {
                    if (company == null || string.IsNullOrEmpty(company.Key) || _byKey.ContainsKey(company.Key)) continue;
                    _byKey.Add(company.Key, company);
                    if (company.IsPlayer || company.Key == CompanyKey.Player) Player = company;
                }
            }

            public IWorldView World { get; private set; }
            public MarketShareCache Shares { get; private set; }
            public RoleOptions Options { get; private set; }
            public IModRng Rng { get; private set; }
            public IReadOnlyList<CompanyInfo> Companies { get; private set; }
            public CompanyInfo Player { get; private set; }
            public HashSet<string> Assigned { get; } = new HashSet<string>(StringComparer.Ordinal);
            public bool LastWasGroup { get; set; }

            public CompanyInfo Find(string key)
            {
                CompanyInfo company;
                return key != null && _byKey.TryGetValue(key, out company) ? company : null;
            }
        }
    }
}
