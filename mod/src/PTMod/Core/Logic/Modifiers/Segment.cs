namespace PTMod.Core
{
    /// <summary>
    /// Market segment of a CPU (FR-12, FR-23). <see cref="Any"/> is the wildcard: as a scope or channel filter it
    /// matches every segment, as the segment of a query it means "unknown or not relevant". The four real segments
    /// are numbered like the game's population segments; the numbers are saved, so they must not change.
    /// </summary>
    public enum Segment
    {
        Any = -1,
        HighEnd = 0,
        MidRange = 1,
        LowEnd = 2,
        Industries = 3
    }
}
