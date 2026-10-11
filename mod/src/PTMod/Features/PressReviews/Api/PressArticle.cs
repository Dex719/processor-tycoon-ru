namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// An article of the press that another feature publishes (<see cref="PressReviewsApi.PublishArticle"/>, FR-11 item 2): a letter in the category "Press"
    /// and a line in the tab "Articles" of the window "Press". The texts are keys of the string tables (the feature's own, with its prefix); the arguments
    /// are already formatted values for <c>{0}</c>, <c>{1}</c> … of both texts.
    /// <code>
    /// PressReviewsApi.PublishArticle(new PressArticle
    /// {
    ///     SourceFeature = "design-flaw-recalls",
    ///     TitleKey = "dfr.press.flaw.title",
    ///     BodyKey = "dfr.press.flaw.body",
    ///     Args = new object[] { "Intel P5-90" },
    /// });
    /// </code>
    /// </summary>
    public sealed class PressArticle
    {
        /// <summary>The id of the feature that publishes the article (<c>design-flaw-recalls</c>); shown as "Source: …". May be empty.</summary>
        public string SourceFeature;

        /// <summary>The key of the title (required): also the subject of the letter.</summary>
        public string TitleKey;

        /// <summary>The key of the text; the title serves as the text when it is empty.</summary>
        public string BodyKey;

        /// <summary>The values of <c>{0}</c> … of the title and the text: text as it is, numbers and dates in the invariant culture. At most 8; null for none.</summary>
        public object[] Args;

        /// <summary>The key of the company the article is about (<c>player</c>, <c>ai:3</c>); optional, goes to the log of the press.</summary>
        public string CompanyKey;
    }
}
