using PTMod.Core;
using PTMod.Core.Logic.Companies;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The half of <see cref="ArchRegistry"/> that knows the game's types (the pure half, with the state itself, is in <c>Logic/State/ArchRegistry.cs</c>
    /// and is under the unit tests). A company is found by <c>ICompany.SaveID</c> and a CPU by <c>Cpu.SaveID</c>; nothing of the game is stored.
    /// <see cref="ResolveCpu"/> is the lazy resolution of a CPU without a binding (FR-5 item 3, D7): its default is the seed policy's
    /// (<see cref="ArchSeedPolicy.DefaultForCpu"/>) for the owner and the year of the CPU. <see cref="GetOrSeed"/> sows an AI company that has no state
    /// yet (FR-8 item 4); the player is sown by <c>ArchSeeder</c>, which has to wait for his research sector.
    /// </summary>
    public sealed partial class ArchRegistry
    {
        /// <summary>How deep <see cref="ResolveCpu"/> follows <c>Cpu.Base</c> (a variant of a variant); a longer or cyclic chain stops there.</summary>
        public const int MaxVariantDepth = 8;

        /// <summary>
        /// The family of a CPU (FR-5 item 3): its binding; without one, a variant (<c>Cpu.Base</c> not null) takes the family of its base CPU and any
        /// other CPU the default of its owner and year (<see cref="ArchSeedPolicy.DefaultForCpu"/>). The result is bound to the CPU (when it has an id
        /// and the state has been sown, see <see cref="ResolveOrBind"/>), so it is computed once. Null only for a null CPU. Never throws for a CPU whose
        /// owner is gone.
        /// </summary>
        public string ResolveCpu(GameCpu cpu)
        {
            return ResolveCpu(cpu, 0);
        }

        private string ResolveCpu(GameCpu cpu, int depth)
        {
            if (cpu == null) return null;

            int id = cpu.SaveID;
            string bound;
            if (TryGetCpuArch(id, out bound)) return bound;

            string family = null;
            GameCpu baseCpu = cpu.Base;
            if (baseCpu != null && !ReferenceEquals(baseCpu, cpu) && depth < MaxVariantDepth) family = ResolveCpu(baseCpu, depth + 1);
            if (family == null) family = DefaultFamilyForOwner(cpu.Company, cpu.Year);
            return ResolveOrBind(id, family);
        }

        /// <summary>
        /// The default family of a CPU of <paramref name="owner"/> made in <paramref name="cpuYear"/>: the player's path, or the preference record of the
        /// AI company by its <c>AICompany.UniqueID</c>. A CPU whose owner is gone (null) belongs to the default family.
        /// </summary>
        public string DefaultFamilyForOwner(ICompany owner, int cpuYear)
        {
            if (ReferenceEquals(owner, null)) return ArchCatalog.DefaultId;
            if (owner.IsPlayer) return DefaultFamilyFor(true, -1, cpuYear);
            return DefaultFamilyFor(false, AiUniqueIdOf(owner), cpuYear);
        }

        /// <summary>The <c>AICompany.UniqueID</c> of an AI company (through its key); -1 for the player, an unknown company and null.</summary>
        public static int AiUniqueIdOf(ICompany company)
        {
            int uniqueId;
            return !ReferenceEquals(company, null) && CompanyKey.TryGetAiId(ModCompanies.KeyOf(company), out uniqueId) ? uniqueId : -1;
        }

        /// <summary>
        /// The state of an AI company; a company the seed has not reached yet (spawned after the start, FR-8 item 4) is sown now by its preference record
        /// and the year of <paramref name="today"/> (<see cref="ArchSeedPolicy.SeedAi"/>). The player and a company that is not an AI company are not sown
        /// here (the player needs his research sector, <c>ArchSeeder</c> does it): they give the state they have, or null. Null also when the date is unknown.
        /// </summary>
        public CompanyArchState GetOrSeed(ICompany company)
        {
            return GetOrSeed(company, ModTicks.Today);
        }

        /// <summary><see cref="GetOrSeed(ICompany)"/> for a given date (the seed of a day other than today, tests of the game half).</summary>
        public CompanyArchState GetOrSeed(ICompany company, System.DateTime today)
        {
            if (ReferenceEquals(company, null)) return null;

            CompanyArchState state = GetOrNull(company.SaveID);
            if (state != null) return state;
            if (company.IsPlayer || today == default(System.DateTime)) return null;

            int uniqueId = AiUniqueIdOf(company);
            if (uniqueId < 0) return null;

            state = GetOrCreate(company.SaveID, false);
            SeedResult seed = ArchSeedPolicy.SeedAi(Catalog, PreferenceOf(uniqueId), today.Year, Seed);
            seed.ApplyTo(state);
            NotifyCompanyChanged(state.CompanyId);
            return state;
        }

        /// <summary>The state of the company, or null when it has none (also for a null company).</summary>
        public CompanyArchState GetOrNull(ICompany company)
        {
            return company == null ? null : GetOrNull(company.SaveID);
        }

        /// <summary>The family bound to the CPU, or null when the CPU has no binding (also for a null CPU).</summary>
        public string GetCpuArch(GameCpu cpu)
        {
            return cpu == null ? null : GetCpuArch(cpu.SaveID);
        }
    }
}
