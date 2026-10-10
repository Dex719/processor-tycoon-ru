using System;
using System.Globalization;

namespace PTMod.Core
{
    /// <summary>Who a modifier entry applies to (FR-12 item 4).</summary>
    public enum ScopeKind
    {
        /// <summary>Every query, with or without a company.</summary>
        Global,

        /// <summary>Every query that names a company (the player and all AI companies).</summary>
        AllCompanies,

        /// <summary>Only the company key <c>player</c>.</summary>
        Player,

        /// <summary>Only AI companies: keys that start with <c>ai:</c>.</summary>
        AIOnly,

        /// <summary>One company key.</summary>
        Company,

        /// <summary>One CPU by its SaveID.</summary>
        Cpu
    }

    /// <summary>
    /// Applicability of one entry: a <see cref="ScopeKind"/> and an optional segment filter. Keys are the company keys
    /// of FR-13 (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>), opaque strings here. <see cref="Segment.Any"/> matches every
    /// segment of a query; a specific segment matches only queries of that segment (a query with the segment
    /// <see cref="Segment.Any"/> has no segment to match). <c>default(ModifierScope)</c> is the global scope for any segment.
    /// </summary>
    public readonly struct ModifierScope : IEquatable<ModifierScope>
    {
        private const string PlayerKey = Logic.Companies.CompanyKey.Player;
        private const string AiPrefix = Logic.Companies.CompanyKey.AiPrefix;

        private readonly ScopeKind kind;
        private readonly string companyKey;
        private readonly int cpuSaveId;
        private readonly int segmentPlusOne; // stored shifted by one so that the default value means Segment.Any

        private ModifierScope(ScopeKind kind, string companyKey, int cpuSaveId, Segment segment)
        {
            this.kind = kind;
            this.companyKey = companyKey;
            this.cpuSaveId = cpuSaveId;
            segmentPlusOne = (int)segment + 1;
        }

        public ScopeKind Kind
        {
            get { return kind; }
        }

        /// <summary>The key of a <see cref="ScopeKind.Company"/> scope; null for the others.</summary>
        public string CompanyKey
        {
            get { return kind == ScopeKind.Company ? companyKey : null; }
        }

        /// <summary>The SaveID of a <see cref="ScopeKind.Cpu"/> scope; -1 for the others.</summary>
        public int CpuSaveId
        {
            get { return kind == ScopeKind.Cpu ? cpuSaveId : -1; }
        }

        public Segment Segment
        {
            get { return (Segment)(segmentPlusOne - 1); }
        }

        public static ModifierScope Global(Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.Global, null, -1, segment);
        }

        public static ModifierScope AllCompanies(Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.AllCompanies, null, -1, segment);
        }

        public static ModifierScope Player(Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.Player, null, -1, segment);
        }

        public static ModifierScope AIOnly(Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.AIOnly, null, -1, segment);
        }

        public static ModifierScope Company(string companyKey, Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.Company, companyKey, -1, segment);
        }

        public static ModifierScope Cpu(int cpuSaveId, Segment segment = Segment.Any)
        {
            return new ModifierScope(ScopeKind.Cpu, null, cpuSaveId, segment);
        }

        /// <summary>
        /// Builds a scope from the fields of a saved entry (<c>scope</c>, <c>company</c>, <c>cpu</c>, <c>segment</c> of
        /// the side file; -1 stands for "none" in the last two). The kind name is case-insensitive. Fields that the
        /// kind does not use are ignored. Returns false and an English reason when the fields do not describe a valid scope.
        /// </summary>
        public static bool TryCreate(string kindName, string companyKey, int cpuSaveId, int segment, out ModifierScope scope, out string error)
        {
            scope = default(ModifierScope);

            ScopeKind parsed;
            if (string.IsNullOrEmpty(kindName) || !char.IsLetter(kindName[0])
                || !Enum.TryParse(kindName, true, out parsed) || !Enum.IsDefined(typeof(ScopeKind), parsed))
            {
                error = "unknown scope kind '" + kindName + "'";
                return false;
            }
            if (segment < (int)Segment.Any || segment > (int)Segment.Industries)
            {
                error = "unknown segment " + segment.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            var created = new ModifierScope(parsed, parsed == ScopeKind.Company ? companyKey : null,
                parsed == ScopeKind.Cpu ? cpuSaveId : -1, (Segment)segment);
            if (!created.Validate(out error)) return false;

            scope = created;
            return true;
        }

        /// <summary>
        /// Checks what the factory methods do not: a Company scope needs a valid key (FR-13: <c>player</c> or <c>ai:&lt;id&gt;</c>), a Cpu scope a SaveID of 0 or more,
        /// and the segment has to be one of the five values.
        /// </summary>
        public bool Validate(out string error)
        {
            if (segmentPlusOne < 0 || segmentPlusOne > (int)Segment.Industries + 1)
            {
                error = "unknown segment " + ((int)Segment).ToString(CultureInfo.InvariantCulture);
                return false;
            }
            if (kind == ScopeKind.Company && !Logic.Companies.CompanyKey.IsValid(companyKey))
            {
                error = "a Company scope needs a company key (player or ai:<id>), got '" + companyKey + "'";
                return false;
            }
            if (kind == ScopeKind.Cpu && cpuSaveId < 0)
            {
                error = "a Cpu scope needs a CPU SaveID of 0 or more";
                return false;
            }
            if (!Enum.IsDefined(typeof(ScopeKind), kind))
            {
                error = "unknown scope kind " + ((int)kind).ToString(CultureInfo.InvariantCulture);
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Rules of FR-12: Global is always true; AllCompanies needs a company key; Player is the key "player"; AIOnly is a key
        /// that starts with "ai:"; Company is the same key; Cpu is the same SaveID. Then the segment filter.
        /// </summary>
        public bool Matches(ModifierQuery query)
        {
            if (segmentPlusOne != 0 && segmentPlusOne - 1 != (int)query.Segment) return false;

            string key = query.CompanyKey;
            switch (kind)
            {
                case ScopeKind.Global:
                    return true;
                case ScopeKind.AllCompanies:
                    return key != null;
                case ScopeKind.Player:
                    return string.Equals(key, PlayerKey, StringComparison.Ordinal);
                case ScopeKind.AIOnly:
                    return key != null && key.StartsWith(AiPrefix, StringComparison.Ordinal);
                case ScopeKind.Company:
                    return companyKey != null && string.Equals(key, companyKey, StringComparison.Ordinal);
                case ScopeKind.Cpu:
                    return cpuSaveId >= 0 && query.CpuSaveId == cpuSaveId;
                default:
                    return false;
            }
        }

        public bool Equals(ModifierScope other)
        {
            // Through the properties: unused fields (a company key on a Player scope, the 0 of a default struct) do not count.
            return kind == other.kind
                   && CpuSaveId == other.CpuSaveId
                   && segmentPlusOne == other.segmentPlusOne
                   && string.Equals(CompanyKey, other.CompanyKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ModifierScope && Equals((ModifierScope)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)kind;
                hash = (hash * 397) ^ CpuSaveId;
                hash = (hash * 397) ^ segmentPlusOne;
                string key = CompanyKey;
                hash = (hash * 397) ^ (key == null ? 0 : key.GetHashCode());
                return hash;
            }
        }

        /// <summary>For logs and diagnostics: "Player", "Company(ai:3)", "Cpu(17)[LowEnd]".</summary>
        public override string ToString()
        {
            string text = kind.ToString();
            if (kind == ScopeKind.Company) text += "(" + companyKey + ")";
            else if (kind == ScopeKind.Cpu) text += "(" + cpuSaveId.ToString(CultureInfo.InvariantCulture) + ")";
            if (segmentPlusOne != 0) text += "[" + Segment + "]";
            return text;
        }
    }

    /// <summary>
    /// What a formula asks the registry about (the game-free part of <c>ModifierContext</c>): the company key
    /// (<c>player</c>, <c>ai:&lt;id&gt;</c>, or null when the formula has no company), the CPU SaveID, the segment, the id of
    /// the contract of patch P8, and whether the value belongs to a CPU design (patch P19; such values are never
    /// cached). Everything but <see cref="Context"/> is part of the cache key (except <see cref="IsDesign"/>, which
    /// turns the cache off). <c>default(ModifierQuery)</c> is "no company, no CPU, any segment, no contract".
    /// </summary>
    public readonly struct ModifierQuery
    {
        /// <summary>The value of "no CPU" and "no contract".</summary>
        public const int None = -1;

        private readonly string companyKey;
        private readonly int cpuPlusOne;
        private readonly int segmentPlusOne;
        private readonly int contractPlusOne;
        private readonly bool isDesign;
        private readonly object context;

        /// <param name="companyKey">Company key or null.</param>
        /// <param name="cpuSaveId">SaveID of the CPU, or <see cref="None"/> (any negative number).</param>
        /// <param name="segment">Segment of the CPU, or <see cref="Segment.Any"/> when unknown.</param>
        /// <param name="contractId">ContractID inside patch P8, or <see cref="None"/>. Zero is a valid id.</param>
        /// <param name="isDesign">True inside patch P19 (CpuMath.ProjectCost): the value is not cached.</param>
        /// <param name="context">Opaque object of the host (the game wrapper puts its ModifierContext here); handed to providers untouched.</param>
        public ModifierQuery(string companyKey, int cpuSaveId = None, Segment segment = Segment.Any, int contractId = None,
            bool isDesign = false, object context = null)
        {
            this.companyKey = companyKey;
            cpuPlusOne = Shift(cpuSaveId);
            segmentPlusOne = (int)segment + 1;
            contractPlusOne = Shift(contractId);
            this.isDesign = isDesign;
            this.context = context;
        }

        public string CompanyKey
        {
            get { return companyKey; }
        }

        /// <summary>SaveID of the CPU, or <see cref="None"/>.</summary>
        public int CpuSaveId
        {
            get { return cpuPlusOne - 1; }
        }

        public Segment Segment
        {
            get { return (Segment)(segmentPlusOne - 1); }
        }

        /// <summary>ContractID, or <see cref="None"/> outside patch P8.</summary>
        public int ContractId
        {
            get { return contractPlusOne - 1; }
        }

        public bool IsDesign
        {
            get { return isDesign; }
        }

        public object Context
        {
            get { return context; }
        }

        public bool HasCpu
        {
            get { return cpuPlusOne != 0; }
        }

        public bool HasContract
        {
            get { return contractPlusOne != 0; }
        }

        public ModifierQuery WithCompany(string key)
        {
            return new ModifierQuery(key, CpuSaveId, Segment, ContractId, isDesign, context);
        }

        public ModifierQuery WithCpu(int cpuSaveId)
        {
            return new ModifierQuery(companyKey, cpuSaveId, Segment, ContractId, isDesign, context);
        }

        public ModifierQuery WithSegment(Segment segment)
        {
            return new ModifierQuery(companyKey, CpuSaveId, segment, ContractId, isDesign, context);
        }

        public ModifierQuery WithContract(int contractId)
        {
            return new ModifierQuery(companyKey, CpuSaveId, Segment, contractId, isDesign, context);
        }

        public ModifierQuery AsDesign()
        {
            return new ModifierQuery(companyKey, CpuSaveId, Segment, ContractId, true, context);
        }

        public ModifierQuery WithContext(object hostContext)
        {
            return new ModifierQuery(companyKey, CpuSaveId, Segment, ContractId, isDesign, hostContext);
        }

        private static int Shift(int id)
        {
            return id < 0 || id == int.MaxValue ? 0 : id + 1;
        }

        public override string ToString()
        {
            string text = companyKey ?? "(no company)";
            if (HasCpu) text += " cpu=" + CpuSaveId.ToString(CultureInfo.InvariantCulture);
            if (segmentPlusOne != 0) text += " " + Segment;
            if (HasContract) text += " contract=" + ContractId.ToString(CultureInfo.InvariantCulture);
            if (isDesign) text += " design";
            return text;
        }
    }
}
