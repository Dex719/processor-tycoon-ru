using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The state of the feature (design.md, Component 3): the architectures of every company, the family of every CPU (by <c>Cpu.SaveID</c>), the
    /// projects waiting for their CPU, and the lists of stage 2. One object for the session: the services keep the reference, the save section
    /// (<see cref="ArchSaveSection"/>) fills it in place (<see cref="Load"/>, <see cref="Clear"/>) and writes it (<see cref="ToSave"/>).
    /// <para>
    /// The pure half of the class lives here (it is under the unit tests); <c>State/ArchRegistry.cs</c> adds what needs the game's types
    /// (<c>ICompany</c>, <c>Cpu</c>). Loading is forgiving: a record that cannot be used is dropped with a warning, the others are read (NFR-3); a
    /// family the catalog does not know is kept as it is (the user may have removed it from the content, the save must not lose the CPUs).
    /// </para>
    /// </summary>
    public sealed partial class ArchRegistry
    {
        /// <summary>The most CPU-to-family bindings a section is read with (design.md, "Валидация при загрузке").</summary>
        public const int MaxCpuBindings = 5000;

        /// <summary>The most waiting projects; when full, the oldest is dropped.</summary>
        public const int MaxPending = 200;

        public const int MaxCompanies = 1000;
        public const int MaxFamiliesPerCompany = 64;
        public const int MaxOverrides = 1000;
        public const int MaxMilestones = 100;

        private readonly ArchCatalog _catalog;
        private readonly AiPreferenceTable _preferences;
        private readonly SeedConfig _seed;
        private readonly List<CompanyArchState> _companies = new List<CompanyArchState>();                 // ordered by company id
        private readonly Dictionary<int, CompanyArchState> _byCompany = new Dictionary<int, CompanyArchState>();
        private readonly Dictionary<int, string> _cpuArch = new Dictionary<int, string>();
        private readonly List<PendingSave> _pending = new List<PendingSave>();
        private readonly List<AiOverrideSave> _aiOverrides = new List<AiOverrideSave>();
        private readonly List<MilestoneSave> _milestones = new List<MilestoneSave>();

        public ArchRegistry(ArchCatalog catalog)
            : this(catalog, null, null)
        {
        }

        /// <summary>
        /// A registry that knows the preferences of the AI companies and the numbers of the seed, so that it can say which family a CPU without a binding
        /// belongs to (<see cref="DefaultFamilyFor"/>, FR-5 item 3). Without them (null) the built-in default record and the default numbers of the seed apply.
        /// </summary>
        public ArchRegistry(ArchCatalog catalog, AiPreferenceTable preferences, SeedConfig seed)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            _catalog = catalog;
            _preferences = preferences;
            _seed = seed ?? new SeedConfig();
        }

        public ArchCatalog Catalog
        {
            get { return _catalog; }
        }

        /// <summary>The preferences of the AI companies; null when the registry was made without them.</summary>
        public AiPreferenceTable Preferences
        {
            get { return _preferences; }
        }

        /// <summary>The numbers of the seed (<c>balance.json</c>, "seed"); never null.</summary>
        public SeedConfig Seed
        {
            get { return _seed; }
        }

        /// <summary>The preference record of an AI company: its own, otherwise the default one (also for an unknown <paramref name="aiUniqueId"/>). Never null.</summary>
        public AiPreference PreferenceOf(int aiUniqueId)
        {
            return _preferences == null ? AiPreferenceTable.Fallback() : _preferences.Match(aiUniqueId);
        }

        /// <summary>The companies that have a state, ordered by company id (the same object while the set does not change).</summary>
        public IReadOnlyList<CompanyArchState> Companies
        {
            get { return _companies; }
        }

        /// <summary>The state has been sown (new game, migration of an old save).</summary>
        public bool Seeded { get; private set; }

        /// <summary>
        /// The state is empty and nobody has sown it: a save without the section (an old save, a mod installed in the middle of a game, a new game
        /// before its seed). <c>GameReady(Loaded)</c> sows it and migrates the CPUs (FR-8 item 3).
        /// </summary>
        public bool MigrationNeeded
        {
            get { return !Seeded; }
        }

        /// <summary>Marks the state as sown; it is written as <c>seeded</c> (TSK-015).</summary>
        public void MarkSeeded()
        {
            Seeded = true;
        }

        /// <summary>A company's state changed (research started, a family finished, support set): the rating of its CPUs is asked anew (<c>ModModifiers.Invalidate</c>).</summary>
        public event Action<int> OnCompanyStateChanged;

        /// <summary>Tells the subscribers of <see cref="OnCompanyStateChanged"/> that the state of the company changed. A subscriber that throws does not stop the others.</summary>
        public void NotifyCompanyChanged(int companyId)
        {
            Action<int> handlers = OnCompanyStateChanged;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)handler)(companyId);
                }
                catch (Exception)
                {
                    // The handlers of the feature are guarded where they are made; a stray exception must not stop the state change.
                }
            }
        }

        // ---- companies ----

        /// <summary>The state of the company with this <c>ICompany.SaveID</c>, or null.</summary>
        public CompanyArchState GetOrNull(int companyId)
        {
            CompanyArchState state;
            return _byCompany.TryGetValue(companyId, out state) ? state : null;
        }

        /// <summary>The state of the company; an empty one (no families, nothing active) is created when there is none. The seed fills it (TSK-014).</summary>
        public CompanyArchState GetOrCreate(int companyId, bool isPlayer)
        {
            CompanyArchState state = GetOrNull(companyId);
            if (state != null) return state;

            state = new CompanyArchState(companyId, isPlayer);
            Insert(state);
            return state;
        }

        /// <summary>The state of the player's company, or null when it has none.</summary>
        public CompanyArchState Player
        {
            get
            {
                foreach (CompanyArchState state in _companies)
                {
                    if (state.IsPlayer) return state;
                }
                return null;
            }
        }

        /// <summary>Forgets a company's state. False when there was none.</summary>
        public bool Remove(int companyId)
        {
            CompanyArchState state = GetOrNull(companyId);
            if (state == null) return false;

            _byCompany.Remove(companyId);
            _companies.Remove(state);
            return true;
        }

        private void Insert(CompanyArchState state)
        {
            int at = _companies.Count;
            while (at > 0 && _companies[at - 1].CompanyId > state.CompanyId) at--;
            _companies.Insert(at, state);
            _byCompany[state.CompanyId] = state;
        }

        // ---- CPU bindings ----

        public int CpuBindingCount
        {
            get { return _cpuArch.Count; }
        }

        /// <summary>The family bound to the CPU with this <c>Cpu.SaveID</c>; false when there is no binding (the default of the owner and the year applies then).</summary>
        public bool TryGetCpuArch(int cpuSaveId, out string familyId)
        {
            return _cpuArch.TryGetValue(cpuSaveId, out familyId);
        }

        /// <summary>The family bound to the CPU, or null.</summary>
        public string GetCpuArch(int cpuSaveId)
        {
            string familyId;
            return _cpuArch.TryGetValue(cpuSaveId, out familyId) ? familyId : null;
        }

        /// <summary>Binds a CPU to a family (a binding of the CPU is replaced). False for a negative CPU id or an id that is no family id; the family need not be in the catalog.</summary>
        public bool Bind(int cpuSaveId, string familyId)
        {
            if (cpuSaveId < 0 || !ArchIds.IsFamilyId(familyId)) return false;
            _cpuArch[cpuSaveId] = familyId;
            return true;
        }

        /// <summary>
        /// A CPU id the lazy resolution may bind (FR-5 item 3): 1 or more. A <c>Cpu</c> that has not been given its id yet (a project, a design) has
        /// the default 0, and <c>SaveIDHandler</c> hands the low ids to the objects of the scene, so 0 is never a released CPU.
        /// </summary>
        public static bool IsBindableCpuId(int cpuSaveId)
        {
            return cpuSaveId > 0;
        }

        /// <summary>
        /// The family of a CPU: its binding; without one, <paramref name="fallbackFamily"/>, which is bound now so the CPU keeps it (FR-5 item 3:
        /// "определить ... и сохранить её") - when the id is bindable, the fallback is a family id and the state has been sown. Before the seed (an
        /// old save before its migration, a new game before the fast-forward ended) the default of the owner is not known yet: the fallback is
        /// returned but not kept, and the migration (FR-8 item 3) binds the CPU. Null when there is neither.
        /// </summary>
        public string ResolveOrBind(int cpuSaveId, string fallbackFamily)
        {
            string bound;
            if (_cpuArch.TryGetValue(cpuSaveId, out bound)) return bound;
            if (fallbackFamily == null) return null;
            if (Seeded && IsBindableCpuId(cpuSaveId)) Bind(cpuSaveId, fallbackFamily);
            return fallbackFamily;
        }

        /// <summary>
        /// The family a CPU without a binding gets (FR-5 item 3, FR-8 item 5; <see cref="ArchSeedPolicy.DefaultForCpu"/>): for the player the newest
        /// family of <c>seed.playerPath</c> that existed in <paramref name="cpuYear"/>, for an AI company the family with the largest weight of its record
        /// among those that existed then with its lag. It depends on the owner and the year only, not on the state of the company, so a CPU gets the same
        /// family whenever it is asked. <paramref name="aiUniqueId"/> is the <c>AICompany.UniqueID</c> (ignored for the player; an unknown id takes the
        /// default record).
        /// </summary>
        public string DefaultFamilyFor(bool isPlayer, int aiUniqueId, int cpuYear)
        {
            AiPreference pref = isPlayer ? null : PreferenceOf(aiUniqueId);
            return ArchSeedPolicy.DefaultForCpu(_catalog, pref, isPlayer, cpuYear, _seed);
        }

        // ---- CPU projects by fingerprint (FR-5 items 1-2) ----

        /// <summary>
        /// Remembers that the project with this fingerprint was started on <paramref name="familyId"/> (FR-5 item 1). The queue keeps the order of the
        /// projects (FIFO among equal fingerprints) and drops the oldest when it is full. False for an unusable fingerprint or family id.
        /// </summary>
        public bool AddPending(CpuFingerprint fingerprint, string familyId)
        {
            if (!fingerprint.IsUsable) return false;
            return AddPending(fingerprint.ToPending(familyId));
        }

        /// <summary>
        /// The family of the OLDEST waiting project whose fingerprint matches (the IPS within <paramref name="relTolerance"/>), removed from the
        /// queue; null when none matches (FR-5 item 2). Two projects with the same fingerprint come out in the order they were started.
        /// </summary>
        public string TakePending(CpuFingerprint fingerprint, float relTolerance = CpuFingerprint.DefaultTolerance)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                PendingSave pending = _pending[i];
                if (!fingerprint.Matches(pending, relTolerance)) continue;
                _pending.RemoveAt(i);
                return pending.arch;
            }
            return null;
        }

        // ---- projects waiting for their CPU ----

        public IReadOnlyList<PendingSave> Pending
        {
            get { return _pending; }
        }

        /// <summary>Puts a waiting project at the end of the queue; when the queue is full the oldest is dropped. False for an unusable entry.</summary>
        public bool AddPending(PendingSave pending)
        {
            if (pending == null || pending.company < 0 || pending.name == null || !ArchIds.IsFamilyId(pending.arch)) return false;

            while (_pending.Count >= MaxPending) _pending.RemoveAt(0);
            _pending.Add(pending);
            return true;
        }

        /// <summary>Removes and returns the OLDEST waiting project the predicate accepts (FIFO among projects with equal fingerprints), or null.</summary>
        public PendingSave TakePending(Predicate<PendingSave> match)
        {
            if (match == null) return null;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (!match(_pending[i])) continue;
                PendingSave found = _pending[i];
                _pending.RemoveAt(i);
                return found;
            }
            return null;
        }

        // ---- stage 2 lists (kept from stage 1, so that saves round-trip) ----

        /// <summary>The weights set by <c>ArchitectureApi.SetAiWeightOverride</c>. The list is the registry's own; callers add and remove entries.</summary>
        public List<AiOverrideSave> AiOverrides
        {
            get { return _aiOverrides; }
        }

        /// <summary>The ecosystem milestones already announced. The list is the registry's own.</summary>
        public List<MilestoneSave> Milestones
        {
            get { return _milestones; }
        }

        // ---- reset, load, save ----

        /// <summary>Empties everything and marks the state as not sown (a new game, the main menu, a save without the section).</summary>
        public void Clear()
        {
            _companies.Clear();
            _byCompany.Clear();
            _cpuArch.Clear();
            _pending.Clear();
            _aiOverrides.Clear();
            _milestones.Clear();
            Seeded = false;
        }

        /// <summary>Builds a registry from a section (see <see cref="Load"/>).</summary>
        public static ArchRegistry FromSave(ArchSaveData data, ArchCatalog catalog, List<string> warnings = null)
        {
            var registry = new ArchRegistry(catalog);
            registry.Load(data, warnings);
            return registry;
        }

        /// <summary>
        /// Replaces the state by the contents of a section. A value out of its range is brought back into it (progress 0..1, support 0..100), an unknown
        /// word becomes the default, a list longer than its limit is cut, a record without a usable id is dropped - each with a line in
        /// <paramref name="warnings"/>. A family id the catalog does not know is kept and noted once. Never throws.
        /// </summary>
        /// <param name="maxTechnologyId">The largest <c>Technology.ID</c> of the game, for <c>resumeTech</c>; a larger value becomes -1.</param>
        public void Load(ArchSaveData data, List<string> warnings, int maxTechnologyId = int.MaxValue)
        {
            Clear();
            if (data == null) return;
            if (warnings == null) warnings = new List<string>();

            var loader = new SectionLoader(this, warnings, maxTechnologyId);
            Seeded = data.seeded;
            loader.LoadCompanies(data.companies);
            loader.LoadCpuArch(data.cpuArch);
            loader.LoadPending(data.pending);
            loader.LoadOverrides(data.aiOverrides);
            loader.LoadMilestones(data.milestones);
        }

        /// <summary>The section as a DTO: fresh objects, stable order (companies by id, families by catalog order, CPUs by id), nothing shared with the registry.</summary>
        public ArchSaveData ToSave()
        {
            var data = new ArchSaveData { seeded = Seeded };

            foreach (CompanyArchState company in _companies)
            {
                var save = new CompanyArchSave
                {
                    companyId = company.CompanyId,
                    isPlayer = company.IsPlayer,
                    active = company.Active,
                    current = company.CurrentResearch,
                    paused = company.Paused,
                    pauseReason = ArchSaveNames.Of(company.PauseReason),
                    resumeTech = company.ResumeTechnologyId,
                };

                var ids = new List<string>(company.Families.Keys);
                ids.Sort(CompareFamilyIds);
                foreach (string id in ids)
                {
                    FamilyState family = company.Families[id];
                    save.families.Add(new FamilySave
                    {
                        id = id,
                        researched = family.Researched,
                        progress = family.Progress,
                        researchedYear = family.ResearchedYear,
                        researchedMonth = family.ResearchedMonth,
                        support = family.Support,
                        tier = ArchSaveNames.Of(family.Tier),
                    });
                }
                data.companies.Add(save);
            }

            var cpus = new List<int>(_cpuArch.Keys);
            cpus.Sort();
            foreach (int cpu in cpus) data.cpuArch.Add(new CpuArchSave { cpu = cpu, arch = _cpuArch[cpu] });

            foreach (PendingSave pending in _pending)
            {
                data.pending.Add(new PendingSave { company = pending.company, name = pending.name, year = pending.year, ips = pending.ips, arch = pending.arch });
            }
            foreach (AiOverrideSave o in _aiOverrides) data.aiOverrides.Add(new AiOverrideSave { company = o.company, arch = o.arch, weight = o.weight });
            foreach (MilestoneSave m in _milestones) data.milestones.Add(new MilestoneSave { arch = m.arch, threshold = m.threshold });
            return data;
        }

        /// <summary>Catalog order first (the order of the file), then the families the catalog does not know, by name.</summary>
        private int CompareFamilyIds(string a, string b)
        {
            int ia = _catalog.IndexOf(a);
            int ib = _catalog.IndexOf(b);
            if (ia >= 0 && ib >= 0) return ia.CompareTo(ib);
            if (ia >= 0) return -1;
            if (ib >= 0) return 1;
            return string.CompareOrdinal(a, b);
        }

        // ---- loading a section ----

        private sealed class SectionLoader
        {
            private readonly ArchRegistry _registry;
            private readonly List<string> _warnings;
            private readonly int _maxTechnologyId;
            private readonly HashSet<string> _unknown = new HashSet<string>(StringComparer.Ordinal);

            public SectionLoader(ArchRegistry registry, List<string> warnings, int maxTechnologyId)
            {
                _registry = registry;
                _warnings = warnings;
                _maxTechnologyId = maxTechnologyId;
            }

            public void LoadCompanies(List<CompanyArchSave> companies)
            {
                if (companies == null) return;
                for (int i = 0; i < companies.Count; i++)
                {
                    string path = "companies[" + Num(i) + "]";
                    if (i >= MaxCompanies)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxCompanies) + " companies, the rest is dropped");
                        break;
                    }

                    CompanyArchSave save = companies[i];
                    if (save == null) { _warnings.Add(path + ": the record is empty, dropped"); continue; }
                    if (save.companyId < 0) { _warnings.Add(path + ".companyId: " + Num(save.companyId) + " is not a company id, the record is dropped"); continue; }
                    if (_registry._byCompany.ContainsKey(save.companyId)) { _warnings.Add(path + ".companyId: " + Num(save.companyId) + " is listed twice, the later record is dropped"); continue; }

                    var state = new CompanyArchState(save.companyId, save.isPlayer);
                    state.Active = CleanFamilyId(save.active, path + ".active");
                    state.CurrentResearch = CleanFamilyId(save.current, path + ".current");
                    state.Paused = save.paused;

                    PauseReason reason;
                    if (save.pauseReason != null && ArchSaveNames.TryParse(save.pauseReason, out reason)) state.PauseReason = reason;
                    else
                    {
                        _warnings.Add(path + ".pauseReason: '" + Shorten(save.pauseReason) + "' is not none, technology, default, funds or disabled; none is used");
                        state.PauseReason = PauseReason.None;
                    }

                    if (save.resumeTech < -1 || save.resumeTech > _maxTechnologyId)
                    {
                        _warnings.Add(path + ".resumeTech: " + Num(save.resumeTech) + " is outside -1.." + Num(_maxTechnologyId) + "; -1 is used");
                        state.ResumeTechnologyId = -1;
                    }
                    else state.ResumeTechnologyId = save.resumeTech;

                    NoteIfUnknown(state.Active, path + ".active");
                    NoteIfUnknown(state.CurrentResearch, path + ".current");
                    LoadFamilies(state, save.families, path);
                    _registry.Insert(state);
                }
            }

            private void LoadFamilies(CompanyArchState state, List<FamilySave> families, string companyPath)
            {
                if (families == null) return;
                for (int i = 0; i < families.Count; i++)
                {
                    string path = companyPath + ".families[" + Num(i) + "]";
                    if (i >= MaxFamiliesPerCompany)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxFamiliesPerCompany) + " families, the rest is dropped");
                        break;
                    }

                    FamilySave save = families[i];
                    if (save == null) { _warnings.Add(path + ": the record is empty, dropped"); continue; }
                    if (!ArchIds.IsFamilyId(save.id)) { _warnings.Add(path + ".id: '" + Shorten(save.id) + "' is not a family id, the record is dropped"); continue; }
                    if (state.Families.ContainsKey(save.id)) { _warnings.Add(path + ".id: '" + save.id + "' is listed twice, the later record is dropped"); continue; }

                    var family = new FamilyState(save.id)
                    {
                        Researched = save.researched,
                        Progress = Clamp(save.progress, 0f, 1f, path + ".progress"),
                        Support = Clamp(save.support, 0f, 100f, path + ".support"),
                    };

                    if (save.researchedYear == 0 || (save.researchedYear >= 1900 && save.researchedYear <= 2200)) family.ResearchedYear = save.researchedYear;
                    else _warnings.Add(path + ".researchedYear: " + Num(save.researchedYear) + " is not a year; 0 is used");
                    if (save.researchedMonth >= 0 && save.researchedMonth <= 12) family.ResearchedMonth = save.researchedMonth;
                    else _warnings.Add(path + ".researchedMonth: " + Num(save.researchedMonth) + " is not a month; 0 is used");

                    SupportTier tier;
                    if (save.tier != null && ArchSaveNames.TryParse(save.tier, out tier)) family.Tier = tier;
                    else _warnings.Add(path + ".tier: '" + Shorten(save.tier) + "' is not off, low, normal or high; off is used");

                    NoteIfUnknown(family.Id, path + ".id");
                    state.Families[family.Id] = family;
                }
            }

            public void LoadCpuArch(List<CpuArchSave> cpus)
            {
                if (cpus == null) return;
                for (int i = 0; i < cpus.Count; i++)
                {
                    string path = "cpuArch[" + Num(i) + "]";
                    if (i >= MaxCpuBindings)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxCpuBindings) + " CPU bindings, the rest is dropped");
                        break;
                    }

                    CpuArchSave save = cpus[i];
                    if (save == null || save.cpu < 0) { _warnings.Add(path + ".cpu: not a CPU id, the record is dropped"); continue; }
                    if (!ArchIds.IsFamilyId(save.arch)) { _warnings.Add(path + ".arch: '" + Shorten(save.arch) + "' is not a family id, the record is dropped"); continue; }
                    if (_registry._cpuArch.ContainsKey(save.cpu)) { _warnings.Add(path + ".cpu: " + Num(save.cpu) + " is listed twice, the later record is dropped"); continue; }

                    NoteIfUnknown(save.arch, path + ".arch");
                    _registry._cpuArch[save.cpu] = save.arch;
                }
            }

            public void LoadPending(List<PendingSave> pending)
            {
                if (pending == null) return;
                for (int i = 0; i < pending.Count; i++)
                {
                    string path = "pending[" + Num(i) + "]";
                    if (_registry._pending.Count >= MaxPending)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxPending) + " waiting projects, the rest is dropped");
                        break;
                    }

                    PendingSave save = pending[i];
                    if (save == null || save.company < 0 || save.name == null) { _warnings.Add(path + ": no company or no name, the record is dropped"); continue; }
                    if (!ArchIds.IsFamilyId(save.arch)) { _warnings.Add(path + ".arch: '" + Shorten(save.arch) + "' is not a family id, the record is dropped"); continue; }
                    if (float.IsNaN(save.ips) || float.IsInfinity(save.ips) || save.ips < 0f) { _warnings.Add(path + ".ips: not a usable number, the record is dropped"); continue; }

                    NoteIfUnknown(save.arch, path + ".arch");
                    _registry._pending.Add(new PendingSave { company = save.company, name = save.name, year = save.year, ips = save.ips, arch = save.arch });
                }
            }

            public void LoadOverrides(List<AiOverrideSave> overrides)
            {
                if (overrides == null) return;
                for (int i = 0; i < overrides.Count; i++)
                {
                    string path = "aiOverrides[" + Num(i) + "]";
                    if (i >= MaxOverrides)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxOverrides) + " overrides, the rest is dropped");
                        break;
                    }

                    AiOverrideSave save = overrides[i];
                    if (save == null || save.company < 0) { _warnings.Add(path + ".company: not a company id, the record is dropped"); continue; }
                    if (!ArchIds.IsFamilyId(save.arch)) { _warnings.Add(path + ".arch: '" + Shorten(save.arch) + "' is not a family id, the record is dropped"); continue; }
                    if (_registry._aiOverrides.Exists(o => o.company == save.company && o.arch == save.arch))
                    {
                        _warnings.Add(path + ": the company and family are listed twice, the later record is dropped");
                        continue;
                    }

                    NoteIfUnknown(save.arch, path + ".arch");
                    _registry._aiOverrides.Add(new AiOverrideSave { company = save.company, arch = save.arch, weight = Clamp(save.weight, 0f, AiPreference.MaxWeight, path + ".weight") });
                }
            }

            public void LoadMilestones(List<MilestoneSave> milestones)
            {
                if (milestones == null) return;
                for (int i = 0; i < milestones.Count; i++)
                {
                    string path = "milestones[" + Num(i) + "]";
                    if (i >= MaxMilestones)
                    {
                        _warnings.Add(path + ": more than " + Num(MaxMilestones) + " milestones, the rest is dropped");
                        break;
                    }

                    MilestoneSave save = milestones[i];
                    if (save == null || !ArchIds.IsFamilyId(save.arch)) { _warnings.Add(path + ".arch: not a family id, the record is dropped"); continue; }
                    if (save.threshold < 1 || save.threshold > 100) { _warnings.Add(path + ".threshold: " + Num(save.threshold) + " is outside 1..100, the record is dropped"); continue; }
                    if (_registry._milestones.Exists(m => m.arch == save.arch && m.threshold == save.threshold))
                    {
                        _warnings.Add(path + ": the milestone is listed twice, the later record is dropped");
                        continue;
                    }

                    NoteIfUnknown(save.arch, path + ".arch");
                    _registry._milestones.Add(new MilestoneSave { arch = save.arch, threshold = save.threshold });
                }
            }

            /// <summary>The id when it is a family id; null (with a note) for a malformed one, null for none.</summary>
            private string CleanFamilyId(string id, string path)
            {
                if (id == null) return null;
                if (ArchIds.IsFamilyId(id)) return id;
                _warnings.Add(path + ": '" + Shorten(id) + "' is not a family id, ignored");
                return null;
            }

            private void NoteIfUnknown(string id, string path)
            {
                if (id == null || _registry._catalog.Contains(id) || !_unknown.Add(id)) return;
                _warnings.Add(path + ": the family '" + id + "' is not in the catalog; it is kept as it is and counts as x1");
            }

            private float Clamp(float value, float min, float max, string path)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    _warnings.Add(path + ": not a number; " + min.ToString("R", CultureInfo.InvariantCulture) + " is used");
                    return min;
                }
                if (value >= min && value <= max) return value;

                float clamped = value < min ? min : max;
                _warnings.Add(path + ": " + value.ToString("R", CultureInfo.InvariantCulture) + " is outside " + min.ToString("R", CultureInfo.InvariantCulture)
                              + ".." + max.ToString("R", CultureInfo.InvariantCulture) + "; " + clamped.ToString("R", CultureInfo.InvariantCulture) + " is used");
                return clamped;
            }

            private static string Num(int value)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>The text for a warning: at most 40 characters, so that a hostile save cannot flood the log.</summary>
            private static string Shorten(string text)
            {
                if (text == null) return "null";
                return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
            }
        }
    }
}
