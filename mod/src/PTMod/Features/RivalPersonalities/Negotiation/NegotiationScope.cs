using System;
using PTMod.Core;
using UnityEngine;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The scope of a negotiation with the player on the game side (design.md, Component 10; FR-8 item 3): opened by the prefix of
    /// <c>ContractNegotiationWindow.UpdateScores</c> for the AI company of that window, closed by its finalizer. While it is open the postfixes of
    /// <c>AIMath.GetProviderScores</c> and <c>AIMath.GetClientScores</c> add what the attitude gives to the scores of that company
    /// (<see cref="NegotiationRules"/>); everywhere else - the AI-AI deals of <c>AIBehaviourBusiness</c> - the scope is closed and the scores are the game's.
    /// The state is <see cref="NegotiationScopeCore"/>; this class reads the companies and the frame. Main thread only.
    /// </summary>
    internal static class NegotiationScope
    {
        private static readonly NegotiationScopeCore Core = new NegotiationScopeCore();

        /// <summary>The scores of an AI company are being counted for the negotiation window of the player.</summary>
        public static bool Active
        {
            get { return Core.Active; }
        }

        /// <summary>The <c>UniqueID</c> of the AI company of the open scope, -1 when it is closed.</summary>
        public static int AiUniqueId
        {
            get { return Core.AiUniqueId; }
        }

        /// <summary>The state, for the diagnostics and the tests.</summary>
        internal static NegotiationScopeCore State
        {
            get { return Core; }
        }

        /// <summary>Opens the scope for the two companies of the window (the prefix of <c>UpdateScores</c>).</summary>
        public static void Enter(ICompany provider, ICompany client)
        {
            Core.Enter(PartyOf(provider), PartyOf(client), Stamp());
        }

        /// <summary>Closes the scope (the finalizer of <c>UpdateScores</c>; also when the window threw).</summary>
        public static void Exit()
        {
            Core.Exit();
        }

        /// <summary>
        /// The postfix of a score function: when <paramref name="company"/> is the AI side of the open scope in this role, adds the bonus of its
        /// attitude to the scores. Changes nothing when the scope is closed, the company is another one, the feature is not active, the company has
        /// no character or is dormant.
        /// </summary>
        /// <param name="asProvider">true for <c>GetProviderScores</c>, false for <c>GetClientScores</c>.</param>
        public static void Adjust(bool asProvider, ICompany company, ref int interest, ref int price)
        {
            if (!Core.Active) return;

            int uniqueId = AiUniqueIdOf(company);
            if (uniqueId < 0) return;

            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null || !ModFeatures.IsActive(RivalPersonalitiesFeature.FeatureId)) return;

            Core.Apply(asProvider, uniqueId, Stamp(), BonusOf, ref interest, ref price);
        }

        /// <summary>
        /// What the attitude of the rival adds to its scores in a negotiation with the player now, for the panel of the intelligence window and for the
        /// patches: zero for a company without a character and for a dormant one.
        /// </summary>
        public static NegotiationBonus BonusFor(RivalPersonalitiesFeature feature, int uniqueId, bool asProvider)
        {
            if (feature == null || feature.Book.IsDormant(uniqueId)) return default(NegotiationBonus);

            Archetype archetype = feature.Book.ArchetypeOf(uniqueId);
            if (archetype == null) return default(NegotiationBonus);

            bool discount = archetype.behaviour != null && archetype.behaviour.negotiationDiscount;
            return NegotiationRules.Bonus(asProvider, feature.Attitudes.Get(uniqueId), discount, NegotiationNumbers.From(feature.Catalog.Config));
        }

        private static NegotiationBonus BonusOf(int uniqueId, bool asProvider)
        {
            return BonusFor(RivalPersonalitiesFeature.Instance, uniqueId, asProvider);
        }

        private static NegotiationParty PartyOf(ICompany company)
        {
            if (ReferenceEquals(company, null) || company == null) return NegotiationParty.Unknown;
            if (ModCompanies.IsPlayer(company)) return NegotiationParty.Player;
            return NegotiationParty.Ai(AiUniqueIdOf(company));
        }

        private static int AiUniqueIdOf(ICompany company)
        {
            if (ReferenceEquals(company, null)) return -1;
            var ai = company as AICompany;
            return !ReferenceEquals(ai, null) && ai != null ? ai.UniqueID : -1;
        }

        private static long Stamp()
        {
            return Time.frameCount;
        }
    }
}
