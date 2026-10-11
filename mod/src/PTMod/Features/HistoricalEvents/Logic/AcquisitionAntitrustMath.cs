using System;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The antitrust surcharge on the price of an acquisition (FR-19, design.md formula 11), as pure arithmetic. The regulator makes concentration dearer, it never forbids a
    /// deal (D13): above <c>softShare</c> of the market the price grows by <c>slope</c> per unit of share, an open case against the buyer multiplies it by <c>caseSurcharge</c>,
    /// and the whole is held at <c>maxMultiplier</c> (never above the channel's own limit of 3.0, never below 1.0: the surcharge never lowers a price).
    /// <code>
    /// S' = playerShare + targetShare                           (the share of the buyer after the deal)
    /// S' &lt; softShare and no case:     m = 1
    /// otherwise:                      m = 1 + slope * max(0, S' - softShare), times caseSurcharge while a case is open, cut at maxMultiplier
    /// </code>
    /// </summary>
    public static class AcquisitionAntitrustMath
    {
        /// <summary>The reason shown by <c>ModModifiers.Explain</c> and by the window of the deal: "Antitrust premium: post-deal share {0}%".</summary>
        public const string ReasonKey = "hist.mod.acqAntitrust";

        /// <summary>The channel the provider sits on (owned by <c>rival-acquisitions</c>).</summary>
        public const string Channel = "acquisition.price";

        /// <summary>The balance range of the channel: the contribution of this feature lies in [1.00, 3.00].</summary>
        public const float MinMultiplier = 1f;

        /// <summary>See <see cref="MinMultiplier"/>.</summary>
        public const float MaxChannelMultiplier = 3f;

        /// <summary>The share of the buyer after taking the target over: the two shares added, held in [0, 1] (a share is never negative and never above the whole market).</summary>
        public static float PostDealShare(float playerShare, float targetShare)
        {
            float sum = Share(playerShare) + Share(targetShare);
            return sum > 1f ? 1f : sum;
        }

        /// <summary>
        /// The multiplier of the price (formula 11) for these shares. 1.0 below the soft limit without a case; an open case adds the surcharge even below it. Always in
        /// [1.00, 3.00]. Null parameters are the defaults of the design.
        /// </summary>
        public static float Multiplier(float playerShare, float targetShare, bool caseActive, AcquisitionAntitrustParams p)
        {
            if (p == null) p = new AcquisitionAntitrustParams();

            float after = PostDealShare(playerShare, targetShare);
            if (!caseActive && after < p.SoftShare) return MinMultiplier;

            float over = after - p.SoftShare;
            float m = 1f + p.Slope * (over > 0f ? over : 0f);
            if (caseActive) m *= p.CaseSurcharge;

            float ceiling = Math.Min(p.MaxMultiplier, MaxChannelMultiplier);
            if (m > ceiling) m = ceiling;
            if (float.IsNaN(m) || m < MinMultiplier) m = MinMultiplier;
            return m;
        }

        /// <summary>The post-deal share as a whole number of percent, the argument {0} of <see cref="ReasonKey"/> (0.625 is written as 63).</summary>
        public static string PercentText(float postDealShare)
        {
            int percent = (int)Math.Floor(Share(postDealShare) * 100f + 0.5f);
            return percent.ToString(CultureInfo.InvariantCulture);
        }

        private static float Share(float value)
        {
            if (float.IsNaN(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    /// <summary>
    /// The provider of <c>acquisition.price</c> without the game (design.md, Component 8: <c>AcquisitionAntitrust</c>): the answer for the company being priced. It reads the
    /// shares from the cache of the roles (O(1) once a month's answers are remembered) and asks whether the buyer is the target of an open case; it keeps no state, so the answer
    /// is the same wherever it is asked. Whoever changes what it reads (a monthly rebuild of the shares, a case opening or closing, a category switched) invalidates the channel.
    /// <list type="bullet">
    /// <item>Without the feature or the category <c>antitrust</c> the answer is 1 and carries no reason.</item>
    /// <item>The buyer is always the player (<c>rival-acquisitions</c>); a question about the player's own company, about nobody or about a key that is not a company is 1.</item>
    /// <item>A reason is given only when the multiplier is not 1; its argument is the post-deal share in percent.</item>
    /// </list>
    /// </summary>
    public sealed class AcquisitionAntitrustCore
    {
        private readonly Func<bool> _enabled;
        private readonly Func<AcquisitionAntitrustParams> _params;
        private readonly Func<string, int, float> _share;
        private readonly Func<string, bool> _hasOpenCase;

        /// <param name="enabled">The feature is active and the category <c>antitrust</c> is on.</param>
        /// <param name="parameters">The block <c>acquisitionAntitrust</c> of <c>params.json</c> (asked at every use, the file is read after the registration).</param>
        /// <param name="share">The share of a company over all segments in the last <c>months</c> full months (the share cache of the roles).</param>
        /// <param name="hasOpenCase">The company is the target of a case that is not over (<see cref="CaseCore.HasOpenCase"/>).</param>
        public AcquisitionAntitrustCore(Func<bool> enabled, Func<AcquisitionAntitrustParams> parameters, Func<string, int, float> share, Func<string, bool> hasOpenCase)
        {
            if (enabled == null) throw new ArgumentNullException("enabled");
            if (share == null) throw new ArgumentNullException("share");
            if (hasOpenCase == null) throw new ArgumentNullException("hasOpenCase");
            _enabled = enabled;
            _params = parameters ?? (() => new AcquisitionAntitrustParams());
            _share = share;
            _hasOpenCase = hasOpenCase;
        }

        /// <summary>The contribution to the price of taking over the company with this key: the multiplier in [1.00, 3.00] and, when it is not 1, the reason with the post-deal share.</summary>
        public ProviderResult Provide(string targetKey)
        {
            if (!CompanyKey.IsValid(targetKey) || CompanyKey.IsPlayer(targetKey) || !_enabled()) return new ProviderResult(1f);

            AcquisitionAntitrustParams p = _params() ?? new AcquisitionAntitrustParams();
            float player = _share(CompanyKey.Player, p.ShareMonths);
            float target = _share(targetKey, p.ShareMonths);
            bool caseActive = _hasOpenCase(CompanyKey.Player);

            float multiplier = AcquisitionAntitrustMath.Multiplier(player, target, caseActive, p);
            if (multiplier <= AcquisitionAntitrustMath.MinMultiplier) return new ProviderResult(1f);

            string percent = AcquisitionAntitrustMath.PercentText(AcquisitionAntitrustMath.PostDealShare(player, target));
            return new ProviderResult(multiplier, AcquisitionAntitrustMath.ReasonKey, new[] { percent });
        }
    }
}
