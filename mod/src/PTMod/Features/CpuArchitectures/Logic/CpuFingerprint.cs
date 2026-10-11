using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The "fingerprint" of a CPU project (D4, FR-5 items 1-2): the company (<c>ICompany.SaveID</c>), the name, the year of release and the IPS. A
    /// project in progress has no stable object (a load re-creates it) and its CPU gets its <c>SaveID</c> only when it is finished, so the family
    /// chosen at the start waits in <c>ArchRegistry.Pending</c> under this key and is bound to the CPU whose fingerprint matches when it comes out.
    /// The IPS is compared with a relative tolerance (<see cref="DefaultTolerance"/>): it goes through the game's save and the mod's JSON on the way.
    /// <para>
    /// Pure: the binder builds fingerprints from <c>SaveObject.CpuProject</c> and <c>Cpu</c> (<c>CpuArchBinder.FingerprintOf</c>).
    /// </para>
    /// </summary>
    public readonly struct CpuFingerprint : IEquatable<CpuFingerprint>
    {
        /// <summary>The relative tolerance of the IPS (1e-4): a float that went through text and back is far closer than that.</summary>
        public const float DefaultTolerance = 1e-4f;

        public CpuFingerprint(int companyId, string name, int year, float ips)
        {
            CompanyId = companyId;
            Name = name;
            Year = year;
            Ips = ips;
        }

        /// <summary><c>ICompany.SaveID</c> of the company that designs the CPU (<c>SaveObject.Cpu.CompanyID</c> of the project).</summary>
        public int CompanyId { get; }

        /// <summary>The name of the CPU; compared exactly.</summary>
        public string Name { get; }

        /// <summary>The year of release (<c>DateController.YearOnRelease</c> at the start of the project).</summary>
        public int Year { get; }

        /// <summary>The IPS of the design.</summary>
        public float Ips { get; }

        /// <summary>A fingerprint that can be stored: a company id of 0 or more and a name.</summary>
        public bool IsUsable
        {
            get { return CompanyId >= 0 && Name != null && !float.IsNaN(Ips) && !float.IsInfinity(Ips) && Ips >= 0f; }
        }

        /// <summary>
        /// The same project: the same company, name and year, and IPS equal within <paramref name="relTolerance"/> of the larger of the two
        /// (relative, so that 50 IPS and 5·10^9 IPS are judged alike).
        /// </summary>
        public bool Matches(CpuFingerprint other, float relTolerance = DefaultTolerance)
        {
            return CompanyId == other.CompanyId && Year == other.Year && string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && IpsClose(Ips, other.Ips, relTolerance);
        }

        /// <summary><see cref="Matches(CpuFingerprint, float)"/> against a waiting project of the save; false for null.</summary>
        public bool Matches(PendingSave pending, float relTolerance = DefaultTolerance)
        {
            return pending != null && Matches(FromPending(pending), relTolerance);
        }

        /// <summary>Two IPS values are the same within a relative tolerance; NaN never is.</summary>
        public static bool IpsClose(float a, float b, float relTolerance = DefaultTolerance)
        {
            if (a == b) return true;
            if (float.IsNaN(a) || float.IsNaN(b) || float.IsInfinity(a) || float.IsInfinity(b)) return false;
            double tolerance = Math.Max(0.0, float.IsNaN(relTolerance) ? DefaultTolerance : relTolerance);
            double scale = Math.Max(Math.Abs((double)a), Math.Abs((double)b));
            return Math.Abs((double)a - b) <= tolerance * scale;
        }

        /// <summary>The waiting project of the save for this fingerprint and family.</summary>
        public PendingSave ToPending(string familyId)
        {
            return new PendingSave { company = CompanyId, name = Name, year = Year, ips = Ips, arch = familyId };
        }

        /// <summary>The fingerprint of a waiting project of the save.</summary>
        public static CpuFingerprint FromPending(PendingSave pending)
        {
            if (pending == null) throw new ArgumentNullException("pending");
            return new CpuFingerprint(pending.company, pending.name, pending.year, pending.ips);
        }

        /// <summary>Exact equality (the IPS bit for bit); use <see cref="Matches(CpuFingerprint, float)"/> to find a project.</summary>
        public bool Equals(CpuFingerprint other)
        {
            return CompanyId == other.CompanyId && Year == other.Year && string.Equals(Name, other.Name, StringComparison.Ordinal) && Ips.Equals(other.Ips);
        }

        public override bool Equals(object obj)
        {
            return obj is CpuFingerprint && Equals((CpuFingerprint)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CompanyId;
                hash = hash * 31 + Year;
                hash = hash * 31 + (Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name));
                return hash * 31 + Ips.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "CpuFingerprint(" + CompanyId + ", '" + Name + "', " + Year + ", " + Ips.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
