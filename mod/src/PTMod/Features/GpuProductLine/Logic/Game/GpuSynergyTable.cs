using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>The two synergy bonuses of a company (F25): <c>SCpu</c> lifts the rating of its CPUs, <c>SGpu</c> the rating of its cards; 0 means none.</summary>
    public struct SynergyBonus
    {
        public float SCpu, SGpu;

        public SynergyBonus(float sCpu, float sGpu)
        {
            SCpu = sCpu;
            SGpu = sGpu;
        }
    }

    /// <summary>
    /// What the synergy providers of the feature answer (design.md, "GpuSynergy и GpuModifierBridge"; FR-15): a table companyKey to bonuses, rebuilt from the
    /// save section when the bonuses change (once a month, after a load, when the switch <c>synergy</c> moves), so a provider costs one dictionary lookup.
    /// A company without an entry, a switched-off synergy and a bonus of 0 all give x1.0. The bonuses are kept in [0, the cap of the balance]
    /// (<c>synergy.cpuMax</c> 0.08, <c>synergy.gpuMax</c> 0.10), so the contribution of the feature to <c>market.rating.cpu</c> is at most x1.08 and to
    /// <c>gpu.rating</c> at most x1.10.
    /// </summary>
    public sealed class GpuSynergyTable
    {
        /// <summary>The reason of the bonus on <c>market.rating.cpu</c> ({0}: percent).</summary>
        public const string CpuReasonKey = "gpu.synergy.cpu";

        /// <summary>The reason of the bonus on <c>gpu.rating</c> ({0}: percent).</summary>
        public const string GpuReasonKey = "gpu.synergy.gpu";

        private readonly Dictionary<string, SynergyBonus> _byKey = new Dictionary<string, SynergyBonus>(StringComparer.Ordinal);

        /// <summary>Companies with a bonus.</summary>
        public int Count
        {
            get { return _byKey.Count; }
        }

        /// <summary>Replaces the table with the entries of the save section; the bonuses are held to [0, cap]. A null list empties the table.</summary>
        public void Rebuild(IEnumerable<GpuSynergyEntry> entries, GpuBalanceDto.SynergyBlock caps)
        {
            _byKey.Clear();
            if (entries == null) return;
            GpuBalanceDto.SynergyBlock rules = caps ?? new GpuBalanceDto.SynergyBlock();
            foreach (GpuSynergyEntry entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.companyKey)) continue;
                var bonus = new SynergyBonus(Clamp(entry.sCpu, rules.cpuMax), Clamp(entry.sGpu, rules.gpuMax));
                if (bonus.SCpu <= 0f && bonus.SGpu <= 0f) continue;
                _byKey[entry.companyKey] = bonus;
            }
        }

        /// <summary>The bonuses of the company, or zeros.</summary>
        public SynergyBonus Lookup(string companyKey)
        {
            SynergyBonus bonus;
            return companyKey != null && _byKey.TryGetValue(companyKey, out bonus) ? bonus : new SynergyBonus();
        }

        /// <summary>The answer of the provider on <c>market.rating.cpu</c>: <c>1 + SCpu</c> with the reason, or x1.0.</summary>
        public static ProviderResult CpuResult(SynergyBonus bonus, bool enabled)
        {
            return Result(enabled ? bonus.SCpu : 0f, CpuReasonKey);
        }

        /// <summary>The answer of the provider on <c>gpu.rating</c>: <c>1 + SGpu</c> with the reason, or x1.0.</summary>
        public static ProviderResult GpuResult(SynergyBonus bonus, bool enabled)
        {
            return Result(enabled ? bonus.SGpu : 0f, GpuReasonKey);
        }

        private static ProviderResult Result(float share, string reasonKey)
        {
            if (!(share > 0f) || float.IsInfinity(share)) return new ProviderResult(1f);
            string percent = (share * 100f).ToString("0.#", CultureInfo.InvariantCulture);
            return new ProviderResult(1f + share, reasonKey, new[] { percent });
        }

        private static float Clamp(float value, float cap)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) return 0f;
            float top = cap > 0f && !float.IsInfinity(cap) ? cap : 0f;
            return Math.Min(value, top);
        }
    }
}
