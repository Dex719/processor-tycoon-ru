using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The source of the multipliers of the behaviour patches (design.md, Component 4; FR-3 items 1-3, 5, 7, 8; FR-7 item 4): the numbers of the
    /// archetype applied to the company times the boosts of the reactions in effect (<see cref="ReactionBoost"/>, from <c>CounterLaunch</c>). The
    /// numbers and the rules are <see cref="BehaviourModel"/>'s; this class reads the company and the moment of the game:
    /// <list type="bullet">
    /// <item>1 during the fast-forward of a new game (<see cref="ModTicks.IsFastForwarding"/>) and before the character of the company was applied;</item>
    /// <item>1 for a foundry (FR-3 item 5: only the attitude and the negotiations act on it) and for a dormant company (its character is not
    /// applied);</item>
    /// <item>the same on every level of difficulty (FR-3 item 8).</item>
    /// </list>
    /// Called by the patches every game day: nothing here allocates. Main thread only.
    /// </summary>
    internal sealed class BehaviourModifiers
    {
        private readonly BehaviourModel _model;

        public BehaviourModifiers(BehaviourModel model)
        {
            _model = model;
        }

        /// <summary>The model behind (shared with <see cref="PersonalityRegistry"/>).</summary>
        public BehaviourModel Model
        {
            get { return _model; }
        }

        /// <summary>The multiplier of the design rating of a designer of <paramref name="company"/> that aims at <paramref name="marketFocus"/> (<c>AIBehaviourCreation.MarketFocus</c> as a <see cref="Segment"/>).</summary>
        public float ApprovalMultiplier(AICompany company, Segment marketFocus)
        {
            if (!Applies(company)) return 1f;
            return _model.ApprovalMultiplier(company.UniqueID, marketFocus, ModTicks.IsFastForwarding);
        }

        /// <summary>The multiplier of the research funding cap of <paramref name="company"/>.</summary>
        public float ResearchFundingMultiplier(AICompany company)
        {
            if (!Applies(company)) return 1f;
            return _model.ResearchFundingMultiplier(company.UniqueID, ModTicks.IsFastForwarding);
        }

        /// <summary>The multiplier of the launch price of a new CPU of <paramref name="company"/>.</summary>
        public float LaunchPriceMultiplier(AICompany company)
        {
            if (!Applies(company)) return 1f;
            return _model.LaunchPriceMultiplier(company.UniqueID, ModTicks.IsFastForwarding);
        }

        /// <summary>A reaction boosts the rival (from <c>CounterLaunch</c>, TSK-014). A boost with the same reaction id is replaced.</summary>
        public bool AddBoost(ReactionBoost boost)
        {
            return _model.AddBoost(boost);
        }

        /// <summary>The boost of the reaction ends. True when there was one.</summary>
        public bool RemoveBoost(string reactionId)
        {
            return _model.RemoveBoost(reactionId);
        }

        /// <summary>The company can take a multiplier at all: a live AI company that is no foundry and whose character is applied.</summary>
        private bool Applies(AICompany company)
        {
            if (ReferenceEquals(company, null)) return false;
            if (!_model.IsApplied(company.UniqueID)) return false;
            return !company.IsFoundry;
        }
    }
}
