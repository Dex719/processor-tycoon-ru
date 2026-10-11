using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// What the feature contributes to the channel <c>popularity.gain</c> (FR-5, FR-7, design.md D7/D12): for a CPU of a company the effect of the
    /// company's reputation <c>R</c> times the hype <c>H</c> of the CPU's review; for a CPU nobody reviewed, or no CPU at all, only <c>R</c>. The
    /// game is reached through two small seams (the reputation of a company by its key, and the parameter "reputation affects the AI"), so the
    /// model is checked without the game. <c>R</c> is kept for the day per company: <see cref="Invalidate"/> drops one company's value (its
    /// reputation changed), <see cref="InvalidateAll"/> all of them (a new day, a monthly pull of the reputations, a parameter). No bound of its own is
    /// put on the product: the balance range of the channel is the core's (D12). Pure; main thread only.
    /// </summary>
    public sealed class PopularityModel
    {
        /// <summary>The reason of a contribution that includes a review: <c>Reputation of the brand and the review of the CPU ({0}/100)</c>.</summary>
        public const string ReviewReasonKey = "press.review";

        /// <summary>The reason of a contribution that is the brand's reputation alone.</summary>
        public const string BrandReasonKey = "press.review.brand";

        /// <summary>The key of the player's company; its reputation always counts, whatever the parameter for the AI says.</summary>
        public const string PlayerKey = "player";

        private readonly Func<ReviewConfig> config;
        private readonly HypeBook hype;
        private readonly Func<string, float> reputationOf;
        private readonly Func<bool> affectsAi;
        private readonly Dictionary<string, float> reputationEffects = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly string[][] metascoreArgs = new string[101][];   // the arguments of the reason, one array per metascore: the channel asks thousands of times a day

        /// <param name="config">The numbers of the effects; read on every use, so a reloaded configuration applies at once.</param>
        /// <param name="hype">The hype of the reviewed CPUs.</param>
        /// <param name="reputationOf">The reputation 0…100 of a company by its key (<c>ModReputation.Get</c>).</param>
        /// <param name="affectsAi">The parameter "reputation affects the AI"; off makes the effect of an AI company's reputation 1.</param>
        public PopularityModel(Func<ReviewConfig> config, HypeBook hype, Func<string, float> reputationOf, Func<bool> affectsAi)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (hype == null) throw new ArgumentNullException("hype");
            if (reputationOf == null) throw new ArgumentNullException("reputationOf");
            if (affectsAi == null) throw new ArgumentNullException("affectsAi");
            this.config = config;
            this.hype = hype;
            this.reputationOf = reputationOf;
            this.affectsAi = affectsAi;
        }

        /// <summary>The hype of the reviewed CPUs this model reads.</summary>
        public HypeBook Hype
        {
            get { return hype; }
        }

        /// <summary>
        /// <c>R</c>: the effect of a company's reputation, <c>1 + 0.3 × (rep − 50)/50</c>; 1 for a company without a key and for an AI company when the
        /// parameter is off. Kept until the company is invalidated.
        /// </summary>
        public float ReputationMultiplier(string companyKey)
        {
            if (string.IsNullOrEmpty(companyKey)) return 1f;

            float cached;
            if (reputationEffects.TryGetValue(companyKey, out cached)) return cached;

            float value = 1f;
            if (string.Equals(companyKey, PlayerKey, StringComparison.Ordinal) || affectsAi())
                value = EffectMath.ReputationEffect(reputationOf(companyKey), config().ReputationEffect);

            reputationEffects[companyKey] = value;
            return value;
        }

        /// <summary>
        /// The contribution to <c>popularity.gain</c> for a company and a CPU: <c>R × H</c> when the CPU (<paramref name="reviewedCpuSaveId"/> is the
        /// <c>SaveID</c> of the CPU whose review counts, the base design for a variant) has a hype, otherwise <c>R</c>. A negative id means no CPU.
        /// The reason names the review (its metascore) when there is one.
        /// </summary>
        public ProviderResult Evaluate(string companyKey, int reviewedCpuSaveId)
        {
            float r = ReputationMultiplier(companyKey);

            float h;
            int metascore;
            if (reviewedCpuSaveId >= 0 && hype.TryGet(reviewedCpuSaveId, out h, out metascore))
            {
                return new ProviderResult(EffectMath.Combine(r, h), ReviewReasonKey, ReasonArgsOf(metascore));
            }
            return new ProviderResult(r, BrandReasonKey);
        }

        private string[] ReasonArgsOf(int metascore)
        {
            if (metascore < 0 || metascore >= metascoreArgs.Length) return new[] { metascore.ToString(CultureInfo.InvariantCulture) };
            return metascoreArgs[metascore] ?? (metascoreArgs[metascore] = new[] { metascore.ToString(CultureInfo.InvariantCulture) });
        }

        /// <summary>The reputation of one company changed: its <c>R</c> is worked out again at the next question.</summary>
        public void Invalidate(string companyKey)
        {
            if (companyKey != null) reputationEffects.Remove(companyKey);
        }

        /// <summary>Every <c>R</c> is worked out again at the next question.</summary>
        public void InvalidateAll()
        {
            reputationEffects.Clear();
        }
    }
}
