using Cpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// A correction of the reviews that another feature adds (FR-11 items 3 and 5, <see cref="PressReviewsApi.RegisterAdjuster"/>): the press asks every
    /// registered adjuster before it works out the marks of the outlets of a review, one after the other in the order they were registered, and each gets
    /// the draft as the earlier ones left it.
    /// <para>
    /// Inside <see cref="Adjust"/> the adjuster may change <see cref="ReviewDraft.AxisScores"/> (by at most 30 points per axis, in all), add to
    /// <see cref="ReviewDraft.OverallAdjustment"/> (at most 20 points, in all) and add keys to <see cref="ReviewDraft.Notes"/> (keys of the string tables, they
    /// have the prefix <c>press.</c>: the card of the review shows them as lines). It does not change anything else. Whatever goes beyond the limits is cut, a
    /// key that is not in the tables is dropped, and the draft the adjuster is handed is a copy: only what passes the checks reaches the review.
    /// </para>
    /// <para>
    /// The method must be fast and have no side effects: it may run for every CPU of every company, once per review. An exception is caught: the
    /// correction of that adjuster is skipped, the failure is counted against the feature that owns the adjuster's class, and the review is published.
    /// An adjuster whose feature is not enabled is not asked.
    /// </para>
    /// <code>
    /// internal sealed class FlawPressAdjuster : IReviewAdjuster
    /// {
    ///     public string Id { get { return "design-flaw-recalls"; } }
    ///     public void Adjust(Cpu cpu, ReviewDraft draft)
    ///     {
    ///         draft.OverallAdjustment -= 15f;
    ///         draft.Notes.Add("press.note.knownFlaw");
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IReviewAdjuster
    {
        /// <summary>The id of the adjuster, unique among the adjusters (the id of the feature that registers it will do): registering the same id again replaces the first. Read once, at registration.</summary>
        string Id { get; }

        /// <summary>Corrects the draft of the review of <paramref name="cpu"/> (see the remarks of the interface for what may change).</summary>
        void Adjust(Cpu cpu, ReviewDraft draft);
    }
}
