namespace PTMod.Features.PlatformGenerations
{
    /// <summary>Where a generation stands on a given day (the window "Платформы", FR-9; <see cref="GenerationCatalog.StatusAt"/>).</summary>
    public enum GenerationStatus
    {
        /// <summary>Not announced yet: the announce year has not come.</summary>
        Expected,

        /// <summary>Announced, no company has shipped a CPU on it yet.</summary>
        Announced,

        /// <summary>Launched, and the early adopter window is still open.</summary>
        EarlyWindow,

        /// <summary>Launched, the early adopter window is over.</summary>
        Mature,

        /// <summary>The obsolete year has come (from 1 January of <c>obsoleteYear</c>).</summary>
        Obsolete,
    }

    /// <summary>
    /// What the chronology knows about generations: announced or not, launched on which day by which company. The shape of the read side of
    /// <c>PlatformTimeline</c> (FR-2, FR-3), which implements it; the catalog needs only this to tell a status, so it does not depend on the
    /// timeline.
    /// </summary>
    public interface IPlatformHistory
    {
        /// <summary>The announcement of the generation has been recorded.</summary>
        bool IsAnnounced(string genId);

        /// <summary>The first CPU of the generation has been released: when, and by which company (<c>player</c> or <c>ai:&lt;id&gt;</c>).</summary>
        bool IsLaunched(string genId, out GameDate date, out string byCompanyKey);
    }
}
