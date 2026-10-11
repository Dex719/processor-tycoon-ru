namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The static entry of the API of <c>rival-personalities</c> exactly as the specs spell it (FR-13, Component 12):
    /// <c>RivalPersonalities.Api.ApplyPriceCut(...)</c>. The same object is <see cref="RivalPersonalitiesApi.Instance"/>.
    /// <para>
    /// Name clash: this class has the name of its namespace. Inside <c>PTMod.Features.RivalPersonalities</c> the plain name <c>RivalPersonalities</c>
    /// is this class, but in a sibling namespace (<c>PTMod.Features.HistoricalEvents</c>, ...) it is the namespace, and <c>RivalPersonalities.Api</c>
    /// does not compile there. Use <c>RivalPersonalitiesApi.Instance</c>, or put
    /// <c>using RivalPersonalities = PTMod.Features.RivalPersonalities.RivalPersonalities;</c> inside the namespace block of the file, or spell
    /// the full name <c>PTMod.Features.RivalPersonalities.RivalPersonalities.Api</c>.
    /// </para>
    /// </summary>
    public static class RivalPersonalities
    {
        /// <summary>
        /// The API for other features: never null; the null-object while the feature is off (<see cref="IRivalPersonalitiesApi.IsEnabled"/> is
        /// false, reads give null, <c>ApplyPriceCut</c> gives 0). See <see cref="IRivalPersonalitiesApi"/>.
        /// </summary>
        public static IRivalPersonalitiesApi Api
        {
            get { return RivalPersonalitiesApi.Instance; }
        }
    }
}
