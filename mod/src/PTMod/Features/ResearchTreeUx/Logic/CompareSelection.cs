using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A processor chosen in the compare window, by identity and not by object: the key of its company (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>)
    /// and its <c>SaveID</c>. The game's <c>Cpu</c> never lives in the selection, so a company that went bankrupt or a processor the game dropped
    /// cannot leave a dead reference behind (AC-6.3); the window looks the pair up again every time it draws.
    /// </summary>
    public readonly struct CpuRef : IEquatable<CpuRef>
    {
        /// <summary>The side that has no processor chosen.</summary>
        public static readonly CpuRef None = new CpuRef(null, -1);

        public readonly string CompanyKey;
        public readonly int SaveId;

        public CpuRef(string companyKey, int saveId)
        {
            CompanyKey = companyKey;
            SaveId = saveId;
        }

        /// <summary>A company and a processor are chosen.</summary>
        public bool IsSet
        {
            get { return !string.IsNullOrEmpty(CompanyKey) && SaveId >= 0; }
        }

        public bool Equals(CpuRef other)
        {
            return SaveId == other.SaveId && string.Equals(CompanyKey, other.CompanyKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is CpuRef && Equals((CpuRef)obj);
        }

        public override int GetHashCode()
        {
            return (CompanyKey == null ? 0 : CompanyKey.GetHashCode()) * 31 + SaveId;
        }
    }

    /// <summary>
    /// The two sides of the compare window (FR-6 items 2, 6 and 7): which processor is A and which is B, the swap ("Swap") and the check that
    /// each side still exists. A side that stopped existing is reset to "none" without an exception (AC-6.3): the window gives the check two
    /// questions it can answer from the game (is the company still there and not bankrupt, does it still have a processor with that id).
    /// </summary>
    public sealed class CompareSelection
    {
        private CpuRef _a = CpuRef.None;
        private CpuRef _b = CpuRef.None;

        public CpuRef A
        {
            get { return _a; }
        }

        public CpuRef B
        {
            get { return _b; }
        }

        public void SetA(string companyKey, int saveId)
        {
            _a = Make(companyKey, saveId);
        }

        public void SetB(string companyKey, int saveId)
        {
            _b = Make(companyKey, saveId);
        }

        public void ClearA()
        {
            _a = CpuRef.None;
        }

        public void ClearB()
        {
            _b = CpuRef.None;
        }

        /// <summary>"Swap": A becomes B and B becomes A (FR-6 item 6).</summary>
        public void Swap()
        {
            CpuRef a = _a;
            _a = _b;
            _b = a;
        }

        /// <summary>
        /// Resets every side that does not exist any more: <paramref name="companyExists"/> says whether the company is still in the game (and not
        /// bankrupt), <paramref name="cpuExists"/> whether it still has the processor. A function that is null counts as "everything exists". A
        /// function that throws counts as "does not exist": the side is reset, the exception does not leave. True when a side was reset.
        /// </summary>
        public bool Validate(Func<string, bool> companyExists, Func<string, int, bool> cpuExists)
        {
            bool resetA = !StillThere(_a, companyExists, cpuExists);
            bool resetB = !StillThere(_b, companyExists, cpuExists);
            if (resetA) _a = CpuRef.None;
            if (resetB) _b = CpuRef.None;
            return resetA || resetB;
        }

        private static CpuRef Make(string companyKey, int saveId)
        {
            CpuRef side = new CpuRef(companyKey, saveId);
            return side.IsSet ? side : CpuRef.None;
        }

        private static bool StillThere(CpuRef side, Func<string, bool> companyExists, Func<string, int, bool> cpuExists)
        {
            if (!side.IsSet) return true;   // nothing chosen, nothing to lose

            try
            {
                if (companyExists != null && !companyExists(side.CompanyKey)) return false;
                if (cpuExists != null && !cpuExists(side.CompanyKey, side.SaveId)) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
