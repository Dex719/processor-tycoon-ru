using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The numbers and decisions of the link with <c>rival-acquisitions</c> (design.md, Component 14; FR-15, TSK-027): the price multiplier for a hostile
    /// company and the way a finished deal touches the other rivals. Pure. The deal types are the names of <c>DealType</c> of that feature
    /// (<c>DealRecord.type</c>): the contract file is not part of the pure build, so they are compared as text.
    /// </summary>
    public static class AcquisitionRules
    {
        /// <summary>The reason written to the history of the attitude of the other rivals when the player took a company over against its will.</summary>
        public const string HostileDealReason = "hostile acquisition";

        public const string HostileDeal = "Hostile";
        public const string DistressedDeal = "Distressed";
        public const string BankruptAssetsDeal = "BankruptAssets";

        /// <summary>The divisor of <c>config.json</c> when the config has none (<c>acquisition.hostilePriceDivisor</c>).</summary>
        public const int DefaultDivisor = 200;

        /// <summary>
        /// The multiplier of the price of taking the company over, from the attitude of the company to the player (formula 17):
        /// <c>1 + max(0, -a) / divisor</c>, from 1.0 (the attitude is zero or positive) to 1.5 (-100). The attitude is held to -100..+100; NaN counts as 0,
        /// a divisor below 1 as <see cref="DefaultDivisor"/>. <c>-60</c> gives 1.3 and <c>+40</c> gives 1.0.
        /// </summary>
        public static float PriceMultiplier(float attitude, int divisor)
        {
            if (float.IsNaN(attitude)) attitude = 0f;
            float a = AttitudeMath.Clamp(attitude);
            int d = divisor < 1 ? DefaultDivisor : divisor;
            return 1f + Math.Max(0f, -a) / d;
        }

        /// <summary>The deal is a takeover against the will of the company (<c>Hostile</c>): the other rivals take note.</summary>
        public static bool IsHostile(string dealType)
        {
            return string.Equals(dealType, HostileDeal, StringComparison.Ordinal);
        }

        /// <summary>
        /// The company left the game because of the deal: <paramref name="absorbedByPlayer"/> (<c>RivalAcquisitionsApi.IsAbsorbedByPlayer</c>) or a deal
        /// that buys the whole company (<c>BankruptAssets</c>, <c>Distressed</c>, <c>Hostile</c>). A stake or a rescue order leaves it in the game.
        /// </summary>
        public static bool RemovesTarget(string dealType, bool absorbedByPlayer)
        {
            if (absorbedByPlayer) return true;
            return string.Equals(dealType, HostileDeal, StringComparison.Ordinal)
                   || string.Equals(dealType, DistressedDeal, StringComparison.Ordinal)
                   || string.Equals(dealType, BankruptAssetsDeal, StringComparison.Ordinal);
        }
    }

    /// <summary>What <see cref="AcquisitionLinkCore.OnDeal"/> did.</summary>
    public struct DealEffect
    {
        /// <summary>The target was put aside (<c>SetDormant(true)</c> changed its state).</summary>
        public bool TargetDormant;

        /// <summary>The number of other rivals whose attitude changed.</summary>
        public int Alarmed;
    }

    /// <summary>
    /// What a finished deal of <c>rival-acquisitions</c> does to the rivals (FR-15 items 2 and 3, TSK-027): a <c>Hostile</c> deal lowers the attitude of every
    /// other living rival by <c>acquisition.hostileDealAttitude</c> (-10, times the sensitivity of the archetype: the ledger applies it), and a target
    /// that left the game is put aside (<see cref="RivalApiCore.SetDormant"/>). Pure; the game side (<c>AcquisitionLink</c>) subscribes to the deals.
    /// </summary>
    public sealed class AcquisitionLinkCore
    {
        private readonly PersonalityBook _book;
        private readonly AttitudeLedger _attitudes;
        private readonly RivalApiCore _api;
        private readonly Func<RivalConfig> _config;

        public AcquisitionLinkCore(PersonalityBook book, AttitudeLedger attitudes, RivalApiCore api, Func<RivalConfig> config)
        {
            _book = book;
            _attitudes = attitudes;
            _api = api;
            _config = config;
        }

        /// <summary>
        /// A deal was completed. <paramref name="dealType"/> is <c>DealRecord.type</c>, <paramref name="targetUniqueId"/> its target,
        /// <paramref name="targetAbsorbed"/> the answer of <c>RivalAcquisitionsApi.IsAbsorbedByPlayer</c> for the target. The other rivals are the companies
        /// with a character that are not the target, not bankrupt and not dormant.
        /// </summary>
        public DealEffect OnDeal(string dealType, int targetUniqueId, bool targetAbsorbed)
        {
            var effect = new DealEffect();

            if (AcquisitionRules.RemovesTarget(dealType, targetAbsorbed))
            {
                effect.TargetDormant = _api.SetDormant(targetUniqueId, true);
            }

            if (AcquisitionRules.IsHostile(dealType))
            {
                RivalConfig config = _config != null ? _config() : null;
                float delta = config != null && config.acquisition != null ? config.acquisition.hostileDealAttitude : RivalConfig.Defaults().acquisition.hostileDealAttitude;
                foreach (int uid in _book.AssignedUids())
                {
                    if (uid == targetUniqueId || _book.IsDormant(uid) || _attitudes.IsFrozen(uid)) continue;
                    if (_attitudes.Change(uid, delta, AcquisitionRules.HostileDealReason)) effect.Alarmed++;
                }
            }
            return effect;
        }

        /// <summary>
        /// After a load: the targets of the deals that left the game are put aside if they are not yet (the record may be missing in a save made before the
        /// link existed). Returns how many were put aside.
        /// </summary>
        public int SyncTargets(IEnumerable<KeyValuePair<string, int>> deals, Func<int, bool> isAbsorbed)
        {
            if (deals == null) return 0;

            int count = 0;
            foreach (KeyValuePair<string, int> deal in deals)
            {
                bool absorbed = isAbsorbed != null && isAbsorbed(deal.Value);
                if (!AcquisitionRules.RemovesTarget(deal.Key, absorbed) || _book.IsDormant(deal.Value)) continue;
                if (_api.SetDormant(deal.Value, true)) count++;
            }
            return count;
        }
    }
}
