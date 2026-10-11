namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The game-side half of <c>FlawViews</c> (the pure half, <c>Of</c> and <c>Known</c>, is in <c>Logic/FlawViews.cs</c>): tells the subscribers of the registry
    /// about a record that changed its state or was answered.
    /// </summary>
    public static partial class FlawViews
    {
        /// <summary>
        /// Tells the subscribers of <c>FlawRegistry.OnFlawStateChanged</c> that the record changed its state or was answered. A hidden record is not announced
        /// (nobody outside may know about it); nothing is raised for <c>null</c>.
        /// </summary>
        internal static void Announce(FlawRecord record, int today)
        {
            if (record == null || record.State == FlawState.Hidden) return;
            FlawRegistry.Shared.RaiseFlawStateChanged(Of(record, today));
        }
    }
}
