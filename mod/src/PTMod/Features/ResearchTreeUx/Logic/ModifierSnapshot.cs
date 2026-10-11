using System;
using System.Collections.Generic;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The facts about a company that decide the permanent perks of the game's table <c>Modifiers.GetModifier</c> (FR-5 item 6): the
    /// company has its own fab and its own development (<see cref="IsIdm"/>), only builds chips for others (<see cref="IsFoundry"/>), has no fab
    /// of its own (<see cref="IsFabless"/>), is the rival TCMS (<see cref="IsTcms"/>) or is the rival that lost its x86 licence and lacks patents
    /// (<see cref="IsUnlucky"/>). The adapter (<c>ModifierSnapshotReader</c>) reads them from <c>ICompany</c> and <c>CompanyHelper</c>;
    /// <see cref="VanillaModifierTable"/> turns them into the numbers.
    /// </summary>
    public readonly struct CompanyFacts
    {
        public readonly bool IsIdm;
        public readonly bool IsFoundry;
        public readonly bool IsFabless;
        public readonly bool IsTcms;
        public readonly bool IsUnlucky;

        public CompanyFacts(bool isIdm, bool isFoundry, bool isFabless, bool isTcms, bool isUnlucky)
        {
            IsIdm = isIdm;
            IsFoundry = isFoundry;
            IsFabless = isFabless;
            IsTcms = isTcms;
            IsUnlucky = isUnlucky;
        }
    }

    /// <summary>
    /// One multiplier that took part in the value of a channel, as <c>ModModifiers.Explain</c> reported it (a copy of <c>AppliedModifier</c>
    /// without the game's date type): the feature that registered it, the key of the reason in that feature's string table with its arguments,
    /// the factor, and the days left before it ends.
    /// </summary>
    public sealed class ExplainRow
    {
        /// <summary>Id of the feature that registered the entry or the provider (<c>event-system</c>).</summary>
        public string FeatureId;

        /// <summary>Key of the reason in the string tables, or null for "no reason".</summary>
        public string ReasonKey;

        /// <summary>Arguments of the reason, or null.</summary>
        public string[] ReasonArgs;

        public float Multiplier = 1f;

        /// <summary>Days before the multiplier stops applying; null for a provider and for an unlimited entry.</summary>
        public int? ExpiresInDays;
    }

    /// <summary>
    /// The breakdown of one channel for the player (a copy of <c>ModifierBreakdown</c>): the product before the limits (<see cref="Raw"/>),
    /// the value the game uses (<see cref="Total"/>), whether the balance range or the fuse cut it, and the rows.
    /// </summary>
    public sealed class ExplainData
    {
        public float Raw = 1f;
        public float Total = 1f;
        public bool IsBalanceCapped;
        public bool IsFuseCapped;
        public List<ExplainRow> Rows = new List<ExplainRow>();

        /// <summary>The value was cut by one of the limits: "(capped)" follows the line (FR-5 item 7).</summary>
        public bool IsCapped
        {
            get { return IsBalanceCapped || IsFuseCapped; }
        }
    }

    /// <summary>
    /// A research project of the player that is still running (FR-5 item 5): what it adds when it is done and when. A project adds one of the
    /// three; the others are zero. <see cref="Consumption"/> and <see cref="Density"/> are increments of the multiplier (0.05 is +5 %), not the
    /// multiplier itself.
    /// </summary>
    public sealed class PendingProject
    {
        /// <summary>Increase of the maximum frequency, in the units of the game.</summary>
        public float Frequency;

        /// <summary>Increase of the consumption divisor (0.05 is +5 %).</summary>
        public float Consumption;

        /// <summary>Increase of the transistor density (0.01 is +1 %).</summary>
        public float Density;

        /// <summary>Days before the project is done; below zero when it is not known.</summary>
        public int EtaDays = -1;

        /// <summary>The project is paused: it does not move until the player resumes it.</summary>
        public bool IsPaused;
    }

    /// <summary>
    /// What the modifiers panel and its tooltip need to know about the player's company, read from the game once per build (design: Component 7).
    /// The report builder never reads the game; a test fills the fields it needs. The arrays are indexed by the value of the game's
    /// <c>ModifierType</c> (0 research speed, 1 factory upkeep, 2 construction, 3 development, 4 research cost, 5 taxes, 6 interest).
    /// </summary>
    public sealed class ModifierSnapshot
    {
        public const int TypeCount = 7;

        public CompanyFacts Facts;

        /// <summary><c>ResearchSector.Funding</c>: 1 is 100 %.</summary>
        public float Funding = 1f;

        /// <summary><c>ResearchMath.ResearchSpeed(Funding, company)</c>: the speed with everything that applies, mods included.</summary>
        public float SpeedTotal = 1f;

        /// <summary><c>Modifiers.GetModifier(type, company)</c> for each type: the perks of the game with the mods already multiplied in (patch P1 of the core).</summary>
        public float[] TotalByType = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };

        /// <summary><c>Company.ResearchModifiers.FrequencyIncrease</c>: added to the maximum frequency.</summary>
        public float FrequencyIncrease;

        /// <summary><c>Company.ResearchModifiers.ConsumptionReduction</c>: the divisor of the power formula, 1 or more.</summary>
        public float ConsumptionReduction = 1f;

        /// <summary><c>Company.ResearchModifiers.DensityIncrease</c>: multiplies the transistor count, 1 or more.</summary>
        public float DensityIncrease = 1f;

        /// <summary>The name of the technology being researched, or null when the lab is idle.</summary>
        public string CurrentTechName;

        /// <summary><c>Technology.Cost</c> of the technology being researched (per month, before the cost multiplier and the funding); 0 without one.</summary>
        public float CurrentTechMonthlyCost;

        /// <summary>The research projects of the player in progress; never null.</summary>
        public List<PendingProject> Pending = new List<PendingProject>();

        /// <summary>
        /// The breakdown of the mods per type, from <c>ModModifiers.Explain</c>; a null element means that <c>Explain</c> is not available for that
        /// type (it threw, or the channel is not declared), and the line then shows "Mods: ×k" with k worked out from the totals.
        /// </summary>
        public ExplainData[] ExplainByType = new ExplainData[TypeCount];

        /// <summary>The breakdown of a type, or null when there is none (also for a type outside 0...6).</summary>
        public ExplainData ExplainFor(int type)
        {
            return ExplainByType != null && type >= 0 && type < ExplainByType.Length ? ExplainByType[type] : null;
        }

        /// <summary>The value of <c>GetModifier</c> for a type, 1 for a type outside 0...6 or a missing array.</summary>
        public float TotalFor(int type)
        {
            return TotalByType != null && type >= 0 && type < TotalByType.Length ? TotalByType[type] : 1f;
        }
    }
}
