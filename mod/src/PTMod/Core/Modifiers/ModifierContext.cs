using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Modifiers;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMemory = ProcessorTycoon.Hardware.Memory;
using GamePackage = ProcessorTycoon.Hardware.IPackage;
using GameProcessNode = ProcessorTycoon.Hardware.ProcessNode;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// The CPU being designed, for the channel <c>development.cost.design</c> (patch P19 on <c>CpuMath.ProjectCost</c>, FR-12 item 12): the parts the
    /// player (or an AI company) picked and the frequency. Only a context built by P19 carries one; a provider of the channel reads the node, the
    /// package or the memory to price immaturity or a platform generation. Values with a design are never cached.
    /// </summary>
    public sealed class DesignContext
    {
        public GamePackage Package;
        public GameProcessNode Node;
        public GameMemory Memory;
        public float Frequency;
    }

    /// <summary>
    /// What a formula asks a channel about (FR-12): who (<see cref="Company"/> / <see cref="CompanyKey"/>), which CPU, which market segment, which
    /// contract and which CPU design. Every field is optional; a context with nothing set is "global". A patch or a feature fills what it knows:
    /// <c>new ModifierContext { Company = company, Cpu = cpu, Segment = Segment.LowEnd }</c>.
    /// <list type="bullet">
    /// <item><b>The company.</b> Set <see cref="Company"/>; the key (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>, <see cref="ModCompanies.KeyOf"/>) is made from it when
    /// <see cref="CompanyKey"/> is null. A key alone is enough where no game object is at hand (providers then get the company through
    /// <see cref="ModCompanies.Resolve"/>); an explicit key wins over the company's own.</item>
    /// <item><b>The segment.</b> <see cref="Segment"/> is <see cref="PTMod.Core.Segment.Any"/> unless the caller sets it - also for <c>default(ModifierContext)</c>
    /// (the property stores the value shifted by one, so the zeroed struct is "any" and not HighEnd, the segment numbered 0). A scope with a specific
    /// segment never matches a context without one; where a segment applies (the population of the market in P3-P5) the patch passes it.</item>
    /// <item><b>The contract</b> is set only inside patch P8 (<c>contract.signChance</c>); its <c>ContractID</c> is part of the cache key, so two
    /// contracts that share a CPU are cached apart (AC-12.7). Outside P8 it is null.</item>
    /// <item><b>The design</b> is set only by patch P19 (<c>development.cost.design</c>); a context with a design is never cached (AC-12.8).</item>
    /// </list>
    /// A provider receives a copy in which <see cref="CompanyKey"/> is filled (null only when there is no company) and <see cref="Company"/>
    /// is filled when the key can be resolved.
    /// </summary>
    public struct ModifierContext
    {
        public ICompany Company;

        /// <summary><c>player</c> or <c>ai:&lt;UniqueID&gt;</c>; null means "take it from <see cref="Company"/>".</summary>
        public string CompanyKey;

        public GameCpu Cpu;

        /// <summary>Only in P8 (<c>contract.signChance</c>); otherwise null. The ID of the contract goes into the cache key.</summary>
        public GameContract Contract;

        /// <summary>Only in P19 (<c>development.cost.design</c>); otherwise null. Such values are not cached.</summary>
        public DesignContext Design;

        private int _segmentPlusOne;   // shifted by one: the zeroed struct means Segment.Any

        /// <summary>The market segment of the question, or <see cref="PTMod.Core.Segment.Any"/> (the default) when it has none.</summary>
        public Segment Segment
        {
            get { return (Segment)(_segmentPlusOne - 1); }
            set { _segmentPlusOne = (int)value + 1; }
        }

        /// <summary>A context for a company (the player's or an AI company's).</summary>
        public static ModifierContext For(ICompany company)
        {
            return new ModifierContext { Company = company };
        }

        /// <summary>A context for a CPU of a company, optionally in a segment.</summary>
        public static ModifierContext For(ICompany company, GameCpu cpu, Segment segment = Segment.Any)
        {
            return new ModifierContext { Company = company, Cpu = cpu, Segment = segment };
        }

        /// <summary>A context from a company key only (no game object at hand).</summary>
        public static ModifierContext ForKey(string companyKey)
        {
            return new ModifierContext { CompanyKey = companyKey };
        }

        /// <summary>
        /// The game-free question the channel engine understands: the company key (the explicit one, else <see cref="ModCompanies.KeyOf"/> of
        /// <see cref="Company"/>), the CPU's <c>SaveID</c>, the segment, the <c>ContractID</c>, and whether a design is present. The game's objects
        /// are read in <see cref="GameAccess"/> only, so a game that does not match fails there and the caller's catch reports it.
        /// </summary>
        internal ModifierQuery ToQuery()
        {
            string key = CompanyKey ?? ModCompanies.KeyOf(Company);
            int cpuId = ReferenceEquals(Cpu, null) ? ModifierQuery.None : GameAccess.SaveIdOf(Cpu);
            int contractId = ReferenceEquals(Contract, null) ? ModifierQuery.None : GameAccess.IdOf(Contract);
            return new ModifierQuery(key, cpuId, Segment, contractId, !ReferenceEquals(Design, null));
        }

        /// <summary>This context with <see cref="CompanyKey"/> filled in from <see cref="Company"/> when it was not set (no lookup when it was).</summary>
        internal ModifierContext WithKey()
        {
            ModifierContext copy = this;
            if (copy.CompanyKey == null && !ReferenceEquals(copy.Company, null)) copy.CompanyKey = ModCompanies.KeyOf(copy.Company);
            return copy;
        }

        /// <summary>The copy a provider gets: the key as in <see cref="WithKey"/>, and the company looked up from the key when only the key was given.</summary>
        internal ModifierContext ForProvider()
        {
            ModifierContext copy = WithKey();
            if (ReferenceEquals(copy.Company, null) && copy.CompanyKey != null) copy.Company = ModCompanies.Resolve(copy.CompanyKey);
            return copy;
        }

        public override string ToString()
        {
            string text = CompanyKey ?? "(no company)";
            if (!ReferenceEquals(Cpu, null)) text += " cpu";
            if (_segmentPlusOne != 0) text += " " + Segment;
            if (!ReferenceEquals(Contract, null)) text += " contract";
            if (!ReferenceEquals(Design, null)) text += " design";
            return text;
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        private static class GameAccess
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int SaveIdOf(GameCpu cpu)
            {
                return cpu.SaveID;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static int IdOf(GameContract contract)
            {
                return contract.ContractID;
            }
        }
    }
}
