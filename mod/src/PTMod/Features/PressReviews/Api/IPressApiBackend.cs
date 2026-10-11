namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// What the implementation of <c>press-reviews</c> provides behind the public <see cref="PressReviewsApi"/>. The feature attaches it when it starts
    /// (<c>PressReviewsApi.AttachBackend</c>, internal); until then, and while the feature is not enabled, the API answers like a null object (no
    /// letter, <c>null</c>, nothing registered). Other features never implement or call this interface, they use <see cref="PressReviewsApi"/>. It names
    /// only types of the folder <c>Api</c>, because the features that use the API are built with that folder alone.
    /// </summary>
    internal interface IPressApiBackend
    {
        /// <summary>Files the article in the log, sends its letter. Does not throw for an article the log cannot take (the caller catches anyway).</summary>
        void PublishArticle(PressArticle article);

        /// <summary>Takes the adjuster (the same id replaces the old one). False when it was refused: no adjuster, no id, too many.</summary>
        bool RegisterAdjuster(IReviewAdjuster adjuster);

        /// <summary>Takes the adjuster with this id out.</summary>
        void UnregisterAdjuster(string id);

        /// <summary>A copy of the review of the CPU, or null.</summary>
        ReviewRecord GetReview(int cpuSaveId);
    }
}
