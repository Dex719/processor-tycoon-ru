namespace PTMod.Features.CpuArchitectures
{
    /// <summary>The level of software support a company pays for (FR-10, stage 2); the numbers of the tiers are in <c>balance.json</c>.</summary>
    public enum SupportTier
    {
        Off = 0,
        Low = 1,
        Normal = 2,
        High = 3,
    }

    /// <summary>
    /// What one company has with one family (FR-2, FR-10, FR-12): researched or not, the progress of the research, the year and month it finished
    /// (for the immature tools of stage 2), the support level and the tier the company pays for. Plain data, changed by the services of the feature.
    /// </summary>
    public sealed class FamilyState
    {
        public FamilyState()
        {
        }

        public FamilyState(string id)
        {
            Id = id;
        }

        public string Id;

        public bool Researched;

        /// <summary>The share of the research done, 0..1; it stays when the research is switched to another family or paused (AC-2.2).</summary>
        public float Progress;

        /// <summary>The date the research finished; 0 when it has not (or when the family was given at the start).</summary>
        public int ResearchedYear;
        public int ResearchedMonth;

        /// <summary>The support level, 0..100. Stage 1 writes it (the seed) but does not use it.</summary>
        public float Support;

        public SupportTier Tier = SupportTier.Off;
    }
}
