namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The numbers of the rating multiplier (<c>balance.json</c>, "rating"; design.md "Формулы и баланс"): the corridor the feature's contribution
    /// is held in (NFR-4) and the shape of the support and ecosystem factors. Fields mirror the JSON; the defaults are the values of the built-in file.
    /// </summary>
    public sealed class RatingConfig
    {
        /// <summary>The lowest contribution of the feature to <c>market.rating.cpu</c> (before the strength knob).</summary>
        public float clampMin = 0.80f;

        /// <summary>The highest contribution of the feature to <c>market.rating.cpu</c> (before the strength knob).</summary>
        public float clampMax = 1.20f;

        /// <summary><c>f_sup = supportBase + supportSpan × S/100</c> (stage 2).</summary>
        public float supportBase = 0.90f;
        public float supportSpan = 0.20f;

        /// <summary><c>f_eco = ecoBase + ecoSpan × √E</c> (stage 2).</summary>
        public float ecoBase = 0.95f;
        public float ecoSpan = 0.10f;

        /// <summary>The support level the owner of a CPU is assumed to have at stage 1, where support is not played yet.</summary>
        public float mvpSupport = 50f;
    }
}
