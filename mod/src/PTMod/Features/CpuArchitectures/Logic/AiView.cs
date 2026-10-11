using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The snapshot of one AI company the policy decides on (design.md Component 9): numbers the service read from the game, so that
    /// <see cref="ArchAiPolicy"/> stays pure. Plain data; the service fills a new one for every company every month.
    /// </summary>
    public sealed class AiView
    {
        /// <summary><c>ICompany.SaveID</c>.</summary>
        public int CompanyId;

        /// <summary><c>AICompany.UniqueID</c>: the index of the historical company, the key of the preference record.</summary>
        public int UniqueId = -1;

        /// <summary>Foundries make no CPUs of their own and are skipped (FR-7 item 6).</summary>
        public bool IsFoundry;

        public bool IsBankrupt;

        /// <summary><c>ICompany.MoneyAmount</c>.</summary>
        public float Money;

        /// <summary><c>Modifiers.GetModifier(ResearchCost, company)</c>: M4 of the game (6 for the "unlucky" company).</summary>
        public float CostModifier = 1f;

        /// <summary><c>Player.DifficultyLevel</c> 0…4.</summary>
        public int Difficulty = 2;

        /// <summary>The year of the game date.</summary>
        public int Year;

        /// <summary>
        /// Stage 2: the shares of the sales of the last three months by segment, in the order of <c>Segment</c> (<see cref="SegmentShares"/>): from the sales of the
        /// company's CPUs, else from the segment of the price of each CPU, else a quarter each. Null at stage 1 and when not known (a quarter each is used).
        /// </summary>
        public float[] SegmentShare;

        /// <summary>Stage 2: <c>InflationHandler.Inflation</c>, the price level of the upkeep of the support ("normal" tier of the active family is chosen against twelve months of it).</summary>
        public float Inflation = 1f;

        /// <summary>Days left of the technology in the lab of the company (<c>ResearchSector.TimeLeftOf</c>); -1 when the lab holds none.</summary>
        public int TechnologyDaysLeft = -1;

        /// <summary>The architecture state of the company; null for a company the seed has not reached.</summary>
        public CompanyArchState State;
    }

    /// <summary>
    /// What the policy decided for one AI company this month. Every field is "nothing" by default: no start, no pause, no change of the active family.
    /// </summary>
    public sealed class AiDecision
    {
        /// <summary>The family to start researching now; null for none.</summary>
        public string StartResearch;

        /// <summary>The <c>ResearchSector.Funding</c> to give the research that starts (<see cref="StartResearch"/> set), 0 otherwise.</summary>
        public float Funding;

        /// <summary>The current research of the company has to pause: its money is negative (FR-7 item 4).</summary>
        public bool Pause;

        /// <summary>A research would start, but the technology in the lab is too close to its end to be interrupted: the decision waits a month (FR-7 item 1).</summary>
        public bool DeferInterrupt;

        /// <summary>The family the company designs on from now; null to keep the current one.</summary>
        public string NewActive;

        /// <summary>Stage 2: the support tiers to set; null at stage 1.</summary>
        public Dictionary<string, SupportTier> Tiers;

        /// <summary>The policy asks for nothing.</summary>
        public bool IsEmpty
        {
            get { return StartResearch == null && !Pause && !DeferInterrupt && NewActive == null && (Tiers == null || Tiers.Count == 0); }
        }
    }
}
