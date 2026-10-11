namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The access point and the registration of the API of <c>rival-personalities</c> (FR-13). Callers read <see cref="Instance"/> (or, the same
    /// object, <see cref="RivalPersonalities.Api"/>); the feature registers its implementation of
    /// <see cref="IRivalPersonalitiesApi"/> once, in <c>Register</c> or <c>OnEnabled</c>, with <see cref="Register"/>.
    /// <para>
    /// Until an implementation is registered, and whenever the feature is not active, <see cref="Instance"/> behaves as the null-object
    /// (<see cref="IRivalPersonalitiesApi.IsEnabled"/> false, reads give null, <c>ApplyPriceCut</c> gives 0), so callers need no checks of their
    /// own and a feature that is off needs no cleanup of the registration.
    /// </para>
    /// <para>
    /// Why two names: the static class <see cref="RivalPersonalities"/> is the spelling of the specs (<c>RivalPersonalities.Api</c>), but it has
    /// the name of the namespace <c>PTMod.Features.RivalPersonalities</c> too. Code in a sibling namespace such as
    /// <c>PTMod.Features.HistoricalEvents</c> finds the namespace first when it writes <c>RivalPersonalities.Api</c> (CS0234). From there use
    /// <c>RivalPersonalitiesApi.Instance</c> (with <c>using PTMod.Features.RivalPersonalities;</c>), or write
    /// <c>using RivalPersonalities = PTMod.Features.RivalPersonalities.RivalPersonalities;</c> <i>inside</i> the namespace block, or spell the
    /// full name.
    /// </para>
    /// <para>
    /// This folder (<c>Api</c>) is compiled into every feature that depends on <c>rival-personalities</c> without the rest of the feature, so
    /// nothing in it may use a type of another folder of the feature; the implementation lives elsewhere and is handed over through
    /// <see cref="Register"/>.
    /// </para>
    /// </summary>
    public static class RivalPersonalitiesApi
    {
        /// <summary>The feature id; the same text as <c>RivalPersonalitiesFeature.FeatureId</c>.</summary>
        internal const string FeatureId = "rival-personalities";

        private static readonly RivalPersonalitiesApiHost Host = new RivalPersonalitiesApiHost();

        /// <summary>
        /// The API of the feature (FR-13). Never null and never replaced: keep it, subscribe to its event once. It is the null-object while the
        /// feature is off (<see cref="IRivalPersonalitiesApi.IsEnabled"/> is false).
        /// </summary>
        public static IRivalPersonalitiesApi Instance
        {
            get { return Host; }
        }

        /// <summary>
        /// For the feature itself: registers the implementation behind <see cref="Instance"/>. Replaces an earlier one; null is ignored. The
        /// implementation raises its own <see cref="IRivalPersonalitiesApi.ReactionStarted"/>, the host forwards it. The implementation need
        /// not check whether the feature is on: the host does not call it while the feature is not active.
        /// </summary>
        internal static void Register(IRivalPersonalitiesApi implementation)
        {
            Host.Attach(implementation);
        }

        /// <summary>For the feature itself: takes the implementation back (only if it is the registered one). Optional on switching off.</summary>
        internal static void Unregister(IRivalPersonalitiesApi implementation)
        {
            Host.Detach(implementation);
        }

        /// <summary>True when an implementation is registered, active or not (for the self-check and the Diagnostics tab).</summary>
        internal static bool IsRegistered
        {
            get { return Host.HasImplementation; }
        }
    }
}
