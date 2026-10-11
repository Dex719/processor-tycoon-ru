using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// What the tooltip of a technology needs to know about the company and the moment besides the technology itself (design: Component 6):
    /// the year, the funding, the speed and cost multipliers, the company type and the rules of the game's research time by year. The adapter
    /// (<c>TechSnapshotReader.ReadContext</c>) fills it from the game when the cursor comes onto a button; the builder never reads the game.
    /// Plain fields so a test sets what it needs.
    /// </summary>
    public sealed class ResearchContext
    {
        /// <summary>The funding below which the time left is "never" (the game divides by the speed, which is zero there).</summary>
        public const float MinFunding = 0.0005f;

        /// <summary>The year of the game's date; zero or less when it is not known (the penalty and the bonus are then left out).</summary>
        public int CurrentYear;

        /// <summary><c>ResearchSector.Funding</c>: 1 is 100 %.</summary>
        public float Funding = 1f;

        /// <summary><c>ResearchSector.InnovationEffort</c>: the funding slider goes up to 500 %.</summary>
        public bool InnovationEnabled;

        /// <summary><c>ResearchMath.ResearchSpeed(Funding, company)</c>: the speed with everything that applies (used by the modifiers panel).</summary>
        public float SpeedTotal = 1f;

        /// <summary><c>Modifiers.GetModifier(ModifierType.ResearchCost, company)</c>: what the monthly cost of the team is multiplied by.</summary>
        public float CostMultiplierTotal = 1f;

        /// <summary>Penalty per year ahead as a fraction (3.5 is +350 %); <see cref="UiTuning.PenaltyPerYear"/>.</summary>
        public float PenaltyPerYear = 3.5f;

        /// <summary>Bonus per year late as a fraction (0.15 is 15 %); <see cref="UiTuning.BonusPerYear"/>.</summary>
        public float BonusPerYear = 0.15f;

        /// <summary>The company has no fab of its own (<c>ICompany.IsFabless</c>).</summary>
        public bool IsFabless;

        /// <summary>The company only builds chips for others (<c>ICompany.IsFoundry</c>).</summary>
        public bool IsFoundry;

        /// <summary>The fundings (1 is 100 %) the tooltip shows the time left at besides the present one. Never null.</summary>
        public float[] TooltipFundings = { 0.5f, 2.0f, 5.0f };

        /// <summary>Colour of good news in TextMeshPro form (<c>#RRGGBB</c>), or null for the colour of the text.</summary>
        public string PositiveColor;

        /// <summary>Colour of bad news (<c>#RRGGBB</c>), or null for the colour of the text.</summary>
        public string NegativeColor;

        /// <summary>Colour of a warning (<c>#RRGGBB</c>), or null for the colour of the text.</summary>
        public string WarningColor;

        /// <summary>The funding is so low that nothing gets researched (<see cref="MinFunding"/>).</summary>
        public bool HasNoFunding
        {
            get { return float.IsNaN(Funding) || Funding < MinFunding; }
        }
    }
}
