using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// A boost of the behaviour of a rival that a reaction gives for its time (design.md, Component 4; FR-7 item 4: <c>CounterLaunch</c> multiplies
    /// the design rating of the designer that aims at <see cref="Segment"/> by <see cref="ApprovalBoost"/> and the research funding cap of the
    /// company by <see cref="FundingBoost"/>). Runtime only: the event system gives it back after a load (<c>ITimedEffectHandler.Restore</c>).
    /// </summary>
    public struct ReactionBoost
    {
        /// <summary>The reaction that gives the boost (<c>r-&lt;n&gt;</c>); the key of <see cref="BehaviourModel.RemoveBoost"/>.</summary>
        public string ReactionId;

        /// <summary>The <c>UniqueID</c> of the rival.</summary>
        public int UniqueId;

        /// <summary>The segment of the designers whose rating is boosted; <see cref="Segment.Any"/> boosts every designer of the rival.</summary>
        public Segment Segment;

        /// <summary>The multiplier of the design rating (1.35 for <c>CounterLaunch</c>); 1 for none.</summary>
        public float ApprovalBoost;

        /// <summary>The multiplier of the research funding cap (1.25 for <c>CounterLaunch</c>); 1 for none.</summary>
        public float FundingBoost;

        public ReactionBoost(string reactionId, int uniqueId, Segment segment, float approvalBoost, float fundingBoost)
        {
            ReactionId = reactionId;
            UniqueId = uniqueId;
            Segment = segment;
            ApprovalBoost = approvalBoost;
            FundingBoost = fundingBoost;
        }
    }

    /// <summary>
    /// The multipliers of the behaviour of the AI (design.md, Component 4, <c>BehaviourModifiers</c>; FR-3 items 1-3 and 7): the numbers of the
    /// archetype that <c>PersonalityRegistry</c> applied to a company, times the boosts of the reactions in effect.
    /// <list type="bullet">
    /// <item>A company whose character was not applied yet, and every company during the fast-forward of a new game, gets 1 (the vanilla values).</item>
    /// <item>The design rating takes the boosts of the segment of the designer (and those of <see cref="Segment.Any"/>); the research funding cap takes
    /// every boost of the company; the launch price takes no boost.</item>
    /// </list>
    /// Pure and keyed by <c>UniqueID</c>; the lookups do not allocate (they run every game day in the patches). Main thread only.
    /// </summary>
    public sealed class BehaviourModel
    {
        /// <summary>The lowest and highest boost a reaction can give; a value outside is clipped, NaN is 1.</summary>
        public const float MinBoost = 0.1f;

        public const float MaxBoost = 10f;

        private sealed class Applied
        {
            public float Approval = 1f;
            public float Funding = 1f;
            public float Launch = 1f;
        }

        private readonly Dictionary<int, Applied> _applied = new Dictionary<int, Applied>();
        private readonly List<ReactionBoost> _boosts = new List<ReactionBoost>();

        // ---- the characters ----

        /// <summary>
        /// The character of the company is in effect with these numbers (<c>approvalBias</c>, <c>researchFundingMult</c>, <c>launchPriceMult</c>).
        /// A number that is not a positive finite value counts as 1. Calling it again replaces the numbers.
        /// </summary>
        public void SetApplied(int uniqueId, float approvalBias, float researchFundingMult, float launchPriceMult)
        {
            Applied entry;
            if (!_applied.TryGetValue(uniqueId, out entry))
            {
                entry = new Applied();
                _applied[uniqueId] = entry;
            }
            entry.Approval = OneIfUnusable(approvalBias);
            entry.Funding = OneIfUnusable(researchFundingMult);
            entry.Launch = OneIfUnusable(launchPriceMult);
        }

        /// <summary>The character of the company is in effect with the numbers of <paramref name="behaviour"/> (null: all 1).</summary>
        public void SetApplied(int uniqueId, ArchetypeBehaviour behaviour)
        {
            if (behaviour == null)
            {
                SetApplied(uniqueId, 1f, 1f, 1f);
                return;
            }
            SetApplied(uniqueId, behaviour.approvalBias, behaviour.researchFundingMult, behaviour.launchPriceMult);
        }

        /// <summary>The character of the company is no longer in effect (reverted, dormant). True when it was.</summary>
        public bool ClearApplied(int uniqueId)
        {
            return _applied.Remove(uniqueId);
        }

        /// <summary>No character is in effect (a scene goes, the feature is switched off). The boosts stay.</summary>
        public void ClearAllApplied()
        {
            _applied.Clear();
        }

        /// <summary>The character of the company was applied (FR-3 item 7: before that every multiplier is 1).</summary>
        public bool IsApplied(int uniqueId)
        {
            return _applied.ContainsKey(uniqueId);
        }

        /// <summary>How many companies have their character in effect.</summary>
        public int AppliedCount
        {
            get { return _applied.Count; }
        }

        // ---- the boosts ----

        /// <summary>
        /// Adds a boost; a boost with the same <see cref="ReactionBoost.ReactionId"/> is replaced. False for a boost without an id. The numbers
        /// are clipped to <see cref="MinBoost"/>..<see cref="MaxBoost"/> (NaN is 1).
        /// </summary>
        public bool AddBoost(ReactionBoost boost)
        {
            if (string.IsNullOrEmpty(boost.ReactionId)) return false;

            boost.ApprovalBoost = ClipBoost(boost.ApprovalBoost);
            boost.FundingBoost = ClipBoost(boost.FundingBoost);
            int index = IndexOfBoost(boost.ReactionId);
            if (index >= 0) _boosts[index] = boost;
            else _boosts.Add(boost);
            return true;
        }

        /// <summary>Removes the boost of the reaction. True when there was one.</summary>
        public bool RemoveBoost(string reactionId)
        {
            int index = IndexOfBoost(reactionId);
            if (index < 0) return false;
            _boosts.RemoveAt(index);
            return true;
        }

        /// <summary>Removes every boost of the company (dormant, bankrupt). Returns how many.</summary>
        public int RemoveBoostsOf(int uniqueId)
        {
            int removed = 0;
            for (int i = _boosts.Count - 1; i >= 0; i--)
            {
                if (_boosts[i].UniqueId != uniqueId) continue;
                _boosts.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>Removes every boost.</summary>
        public void ClearBoosts()
        {
            _boosts.Clear();
        }

        /// <summary>How many boosts are in effect.</summary>
        public int BoostCount
        {
            get { return _boosts.Count; }
        }

        /// <summary>The boost of the reaction is in effect.</summary>
        public bool HasBoost(string reactionId)
        {
            return IndexOfBoost(reactionId) >= 0;
        }

        // ---- the multipliers (the daily path: no allocation) ----

        /// <summary>
        /// The multiplier of the design rating of a designer of the company that aims at <paramref name="marketFocus"/> (formula 1):
        /// <c>approvalBias × Π approvalBoost</c> of the boosts of that segment and of <see cref="Segment.Any"/>. 1 during the fast-forward and before
        /// the character was applied.
        /// </summary>
        public float ApprovalMultiplier(int uniqueId, Segment marketFocus, bool fastForwarding)
        {
            Applied entry;
            if (fastForwarding || !_applied.TryGetValue(uniqueId, out entry)) return 1f;

            float result = entry.Approval;
            for (int i = 0; i < _boosts.Count; i++)
            {
                ReactionBoost boost = _boosts[i];
                if (boost.UniqueId != uniqueId) continue;
                if (boost.Segment != Segment.Any && boost.Segment != marketFocus) continue;
                result *= boost.ApprovalBoost;
            }
            return result;
        }

        /// <summary>
        /// The multiplier of the research funding cap of the company (formula 2): <c>researchFundingMult × Π fundingBoost</c> of all its boosts.
        /// 1 during the fast-forward and before the character was applied.
        /// </summary>
        public float ResearchFundingMultiplier(int uniqueId, bool fastForwarding)
        {
            Applied entry;
            if (fastForwarding || !_applied.TryGetValue(uniqueId, out entry)) return 1f;

            float result = entry.Funding;
            for (int i = 0; i < _boosts.Count; i++)
            {
                if (_boosts[i].UniqueId == uniqueId) result *= _boosts[i].FundingBoost;
            }
            return result;
        }

        /// <summary>The multiplier of the launch price of a new CPU of the company (formula 3): <c>launchPriceMult</c>; 1 during the fast-forward and before the character was applied.</summary>
        public float LaunchPriceMultiplier(int uniqueId, bool fastForwarding)
        {
            Applied entry;
            if (fastForwarding || !_applied.TryGetValue(uniqueId, out entry)) return 1f;
            return entry.Launch;
        }

        // ---- internals ----

        private int IndexOfBoost(string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId)) return -1;
            for (int i = 0; i < _boosts.Count; i++)
            {
                if (string.Equals(_boosts[i].ReactionId, reactionId, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static float OneIfUnusable(float value)
        {
            return BehaviourMath.IsMultiplier(value) ? value : 1f;
        }

        private static float ClipBoost(float value)
        {
            if (float.IsNaN(value)) return 1f;
            if (value < MinBoost) return MinBoost;
            return value > MaxBoost ? MaxBoost : value;
        }
    }
}
