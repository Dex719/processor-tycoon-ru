using System;
using System.Collections;
using System.Collections.Generic;

namespace PTMod.Core
{
    /// <summary>
    /// One multiplier that took part in a value, as <c>Explain</c> reports it (FR-12 item 9): the source, the reason
    /// and the term. <see cref="EntryId"/> is null for a provider. The multiplier is the one that entered the product:
    /// a zero on a channel with a balance range shows as the lower bound of the range.
    /// </summary>
    public readonly struct AppliedModifier
    {
        public AppliedModifier(string featureId, string entryId, string reasonKey, string[] reasonArgs, float multiplier, DateTime? expiresOn)
        {
            FeatureId = featureId;
            EntryId = entryId;
            ReasonKey = reasonKey;
            ReasonArgs = reasonArgs ?? new string[0];
            Multiplier = multiplier;
            ExpiresOn = expiresOn;
        }

        /// <summary>Id of the feature that registered the entry or the provider (the source shown to the player).</summary>
        public string FeatureId { get; }

        /// <summary>Id of the saved entry, or null when the multiplier comes from a provider.</summary>
        public string EntryId { get; }

        /// <summary>ModL10n key of the reason, or null ("no reason": the UI shows the multiplier and the source only).</summary>
        public string ReasonKey { get; }

        /// <summary>Arguments of <see cref="ReasonKey"/>; never null.</summary>
        public string[] ReasonArgs { get; }

        public float Multiplier { get; }

        /// <summary>First day the multiplier no longer applies; null for providers and unlimited entries.</summary>
        public DateTime? ExpiresOn { get; }

        public bool IsProvider
        {
            get { return EntryId == null; }
        }
    }

    /// <summary>
    /// Breakdown of a channel value for tooltips (<c>research-tree-ux</c>, <c>contract-transparency</c>): the applied
    /// multipliers (a list of <see cref="AppliedModifier"/>, without the ones equal to 1), the product before any
    /// limit (<see cref="Raw"/>), the final value (<see cref="Total"/>, the number <c>Get</c> returns) and whether the balance range
    /// or the fuse changed it. The UI appends the text <see cref="CappedKey"/> after the value when
    /// <see cref="IsBalanceCapped"/> is set.
    /// </summary>
    public sealed class ModifierBreakdown : IReadOnlyList<AppliedModifier>
    {
        /// <summary>ModL10n key of the "(capped)" suffix.</summary>
        public const string CappedKey = "core.explain.capped";

        private readonly AppliedModifier[] items;

        internal ModifierBreakdown(string channel, AppliedModifier[] items, float raw, float total, float balanceMin, float balanceMax,
            bool isBalanceCapped, bool isFuseCapped, float fuseMin, float fuseMax, bool isDeclared)
        {
            Channel = channel;
            this.items = items ?? new AppliedModifier[0];
            Raw = raw;
            Total = total;
            BalanceMin = balanceMin;
            BalanceMax = balanceMax;
            IsBalanceCapped = isBalanceCapped;
            IsFuseCapped = isFuseCapped;
            FuseMin = fuseMin;
            FuseMax = fuseMax;
            IsDeclared = isDeclared;
        }

        public string Channel { get; }

        /// <summary>The product of all multipliers before the balance range and the fuse.</summary>
        public float Raw { get; }

        /// <summary>The value after the balance range and the fuse; equal to what <c>Get</c> returns for the same query.</summary>
        public float Total { get; }

        /// <summary>Bounds of the balance range; 0 and 0 when the channel has none.</summary>
        public float BalanceMin { get; }

        public float BalanceMax { get; }

        public bool HasBalanceRange
        {
            get { return !(BalanceMin == 0f && BalanceMax == 0f); }
        }

        /// <summary>The product left the balance range and was cut: show "(capped)".</summary>
        public bool IsBalanceCapped { get; }

        /// <summary>The value left the fuse range and was cut.</summary>
        public bool IsFuseCapped { get; }

        public float FuseMin { get; }

        public float FuseMax { get; }

        /// <summary>False for a channel that is not declared in the registry (the default fuse was used).</summary>
        public bool IsDeclared { get; }

        public int Count
        {
            get { return items.Length; }
        }

        public AppliedModifier this[int index]
        {
            get { return items[index]; }
        }

        public IEnumerator<AppliedModifier> GetEnumerator()
        {
            return ((IEnumerable<AppliedModifier>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
