using PTMod.Features.EventSystem;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// <see cref="IContributionSink"/> over the aggregator of <c>event-system</c> (<see cref="IEffectModifiers"/>): the multiplier of one effect on its channel
    /// for its company (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c> or <c>global</c>). A handler owns one and binds it for the length of a call
    /// (<see cref="Bind"/>, <see cref="Release"/>), so a day's <c>Tick</c> allocates nothing. Main thread only, not re-entrant, like the engine.
    /// </summary>
    internal sealed class ContributionSink : IContributionSink
    {
        private IEffectModifiers _modifiers;
        private AppliedEffect _effect;
        private string _companyKey;

        /// <summary>Points the sink at an effect. The company key of the effect is used; <paramref name="fallbackCompanyKey"/> stands in for an effect without one.</summary>
        public ContributionSink Bind(IEffectModifiers modifiers, AppliedEffect effect, string fallbackCompanyKey)
        {
            _modifiers = modifiers;
            _effect = effect;
            _companyKey = effect == null || string.IsNullOrEmpty(effect.companyKey)
                ? (string.IsNullOrEmpty(fallbackCompanyKey) ? EventCompanyKeys.Global : fallbackCompanyKey)
                : effect.companyKey;
            return this;
        }

        /// <summary>Lets go of the aggregator and the effect after the call.</summary>
        public void Release()
        {
            _modifiers = null;
            _effect = null;
            _companyKey = null;
        }

        public void Set(float multiplier)
        {
            if (_modifiers != null && _effect != null) _modifiers.Set(_effect, _effect.channel, _companyKey, multiplier);
        }

        public void Clear()
        {
            if (_modifiers != null && _effect != null) _modifiers.Clear(_effect);
        }
    }
}
