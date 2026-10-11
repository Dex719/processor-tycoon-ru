namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The engine's aggregator of modifier contributions as an effect handler of any pack sees it (<c>EventContext.Contributions</c>). A handler of a
    /// timed modifier effect sets the effect's current multiplier in <c>Resolve</c>/<c>Apply</c>/<c>Tick</c>/<c>Restore</c> and clears it in <c>Remove</c>;
    /// the aggregator multiplies the contributions of all effects, applies the limits of the channel and hands the product to <c>ModModifiers</c>
    /// through one provider per channel.
    /// </summary>
    public interface IEffectModifiers
    {
        /// <summary>
        /// Sets the current contribution of <paramref name="effect"/> to <paramref name="channel"/> for the company (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>,
        /// <c>global</c>). Setting it again replaces the old value (a curve changes it every day). The channel cache of <c>ModModifiers</c> is
        /// invalidated on every change, so the effect is felt at once.
        /// </summary>
        void Set(AppliedEffect effect, string channel, string companyKey, float multiplier);

        /// <summary>Removes every contribution of <paramref name="effect"/>.</summary>
        void Clear(AppliedEffect effect);

        /// <summary>The product on this channel for this company was cut by the limits of the channel.</summary>
        bool WasClamped(string channel, string companyKey);

        /// <summary>The lower limit of the channel for the contribution of events (the narrowest of the <c>channelLimits</c> of the packs).</summary>
        float LimitMin(string channel);

        /// <summary>The upper limit of the channel; a handler clamps its resolved value with it, so the shown number is the applied one (D15).</summary>
        float LimitMax(string channel);
    }
}
