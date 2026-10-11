using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The letters of press-reviews (FR-4, design.md Component 10): all of them go through <c>ModMail.Send</c> with the feature's id and a tag
    /// <c>press:&lt;kind&gt;:&lt;key&gt;</c>, and their buttons are <c>ModMailAction</c>s answered by <see cref="OnAction"/> (registered once in <c>Register</c>).
    /// This part is the letter about a review of the player's CPU: priority <c>Normal</c>, the title "Reviews: X — 84/100", the verdict, the marks of
    /// the outlets with one quote each and the effects in words, and the action "Open the review" that opens the card and leaves the letter open. A letter
    /// does not open a window by itself and does not stop the game. The reviews of the rivals send no letter and no notification of their own: they come
    /// monthly in the digest. The id the core returns is kept in <see cref="ReviewRecord.mailId"/>; there are no blocks of ids, the letters are found by tag.
    /// The letter about the awards of a year (<c>Normal</c>, tag <c>press:awards:&lt;year&gt;</c>, the action "Open the awards"), the quarterly notes
    /// (<c>Low</c>, tag <c>press:notes:&lt;yyyy-MM&gt;</c>) and the articles of other features (<c>Normal</c>, tag <c>press:article:&lt;n&gt;</c>, the subject is
    /// the title of the article) are here too, and so is the monthly digest of the rivals' reviews (<c>Low</c>, tag <c>press:digest:&lt;yyyy-MM&gt;</c>, no buttons).
    /// </summary>
    internal static class PressMail
    {
        /// <summary>The id of the action "Open the review".</summary>
        internal const string ActionOpenReview = "openReview";

        /// <summary>The id of the action "Open the awards".</summary>
        internal const string ActionOpenAwards = "openAwards";

        private const string Feature = PressReviewsFeature.FeatureId;

        /// <summary>
        /// <c>ReviewScheduler.Published</c>: a review was filed in the archive and its effects are in. Only a CPU of the player gets a letter; the
        /// reviews of the rivals are for the digest. Called by <c>ModHooks.Raise</c>, so a failure here never reaches the scheduler.
        /// </summary>
        internal static void OnReviewPublished(ReviewRecord record)
        {
            if (record == null || !string.Equals(record.companyKey, ModCompanies.PlayerKey, StringComparison.Ordinal)) return;

            SendReview(record);
        }

        /// <summary>
        /// Sends the letter about a review (design.md: <c>SendReview</c>). The tag <c>press:review:&lt;cpuSaveId&gt;</c> makes it once per CPU: a letter that
        /// is already there is not sent again. Returns the id of the letter, or -1 when the core did not take it.
        /// </summary>
        internal static int SendReview(ReviewRecord record)
        {
            if (record == null) return -1;

            string tag = ReviewMailText.TagOf(record.cpuSaveId);
            ModMailRecord sent = ModMail.Find(Feature, tag);
            if (sent != null)
            {
                if (record.mailId == 0) record.mailId = sent.MailId;
                return sent.MailId;
            }

            ReviewMailContent content = ReviewMailText.Build(record, PressUi.CardContext(record));
            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = content.Tag,
                TitleKey = "press.mail.review.subject",
                BodyKey = "press.mail.review.body",
                Args = content.Args,
                Priority = MailPriority.Normal,
                Actions = new List<ModMailAction>
                {
                    new ModMailAction
                    {
                        Id = ActionOpenReview,
                        LabelKey = "press.mail.review.open",
                        TooltipKey = "press.mail.review.open.tip",
                        Repeatable = true,
                    },
                },
            };

            int id = ModMail.Send(message);
            if (id >= ModMail.FirstModMailId)
            {
                record.mailId = id;
                ModLog.Debug(Feature, "mail: the review of '" + record.cpuName + "' is mail #" + id);
            }
            else
            {
                ModLog.Warn(Feature, "mail: the letter about the review of '" + record.cpuName + "' was not accepted by the mail service; the review is in the archive");
            }
            return id;
        }

        /// <summary>
        /// <c>ModMail.RegisterActionHandler</c>: a click on a button of a letter of the feature. "Open the review" opens the card of the CPU the tag names; the
        /// letter stays open (the core does not close a letter on an action). A tag of another kind is ignored.
        /// </summary>
        internal static void OnAction(ModMailRecord mail, string actionId)
        {
            if (mail == null) return;

            ModHooks.Guard(Feature, "mail/action", () =>
            {
                if (actionId == ActionOpenReview)
                {
                    int cpuSaveId;
                    if (ReviewMailText.TryParseTag(mail.Tag, out cpuSaveId)) ReviewCardWindow.Open(cpuSaveId);
                    else ModLog.Warn(Feature, "mail: the tag '" + mail.Tag + "' of the letter does not name a review");
                }
                else if (actionId == ActionOpenAwards)
                {
                    PressWindow.Open(PressWindow.PressTab.Awards);
                }
            });
        }

        // ---- the awards of the year (FR-8) ----

        /// <summary>
        /// Sends the letter "Press awards 1995" (<c>Normal</c>, tag <c>press:awards:&lt;year&gt;</c>): the winners as the tab shows them and the change of the
        /// player's reputation; the action "Open the awards" opens the tab. A year that has its letter is not sent again. Returns the id of the letter, or -1.
        /// </summary>
        internal static int SendAwards(YearAwards awards, int playerDelta)
        {
            if (awards == null) return -1;

            ModMailRecord sent = ModMail.Find(Feature, AwardsMailText.TagOf(awards.year));
            if (sent != null) return sent.MailId;

            AwardsMailContent content = AwardsMailText.Build(awards, playerDelta, PressUi.AwardsLookups(), PressUi.Words);
            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = content.Tag,
                TitleKey = "press.awards.subject",
                BodyKey = "press.mail.awards.body",
                Args = content.Args,
                Priority = MailPriority.Normal,
                Actions = new List<ModMailAction>
                {
                    new ModMailAction
                    {
                        Id = ActionOpenAwards,
                        LabelKey = "press.mail.awards.open",
                        TooltipKey = "press.mail.awards.open.tip",
                        Repeatable = true,
                    },
                },
            };
            return Accepted(ModMail.Send(message), "the awards of " + awards.year);
        }

        // ---- the notes of the quarter (FR-10) ----

        /// <summary>Sends the letter "Press: market review" (<c>Low</c>, tag <c>press:notes:&lt;yyyy-MM&gt;</c>) with the notes of the quarter, a paragraph each.</summary>
        internal static int SendNotes(DateTime today, IList<string> lines)
        {
            NotesMailContent content = NotesMailText.Build(today, lines);
            if (content.Args.Length == 0 || content.Args[0].Length == 0) return -1;

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = content.Tag,
                TitleKey = "press.mail.notes.subject",
                BodyKey = "press.mail.notes.body",
                Args = content.Args,
                Priority = MailPriority.Low,
            };
            return Accepted(ModMail.Send(message), "the notes of " + PressDates.ToMonthText(today));
        }

        // ---- the digest of the rivals' reviews (FR-4) ----

        /// <summary>
        /// Sends the letter "Press: rival reviews" (<c>Low</c>, tag <c>press:digest:&lt;yyyy-MM&gt;</c>, no buttons): a line for each notable review of the
        /// month before and, when some were left out, "and N more reviews in the archive". An empty digest is not sent (returns -1). A month that has its
        /// letter is not sent again. The core holds the letter back during a batch run of a month.
        /// </summary>
        internal static int SendDigest(DateTime today, DigestContent content)
        {
            if (content == null || content.IsEmpty) return -1;

            DigestMailContent mail = DigestMailText.Build(content, today, PressUi.Words, MoreReviews, PressContent.Current.Config);
            if (mail.Args.Length == 0 || mail.Args[0].Length == 0) return -1;

            ModMailRecord sent = ModMail.Find(Feature, mail.Tag);
            if (sent != null) return sent.MailId;

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = mail.Tag,
                TitleKey = "press.mail.digest.subject",
                BodyKey = "press.mail.digest.body",
                Args = mail.Args,
                Priority = MailPriority.Low,
            };
            return Accepted(ModMail.Send(message), "the digest of " + PressDates.ToMonthText(today));
        }

        /// <summary>"…and N more reviews in the archive" in the form that fits N (ru: one / few / many, en: one / other).</summary>
        private static string MoreReviews(int count)
        {
            return ModL10n.Plural("press.mail.digest.more", count, count);
        }

        // ---- the articles of other features (FR-11) ----

        /// <summary>
        /// Sends the letter of an article (<c>Normal</c>, tag <c>press:article:&lt;n&gt;</c>, the number is one more than the highest of the letters sent so
        /// far): the subject is the title of the article, the text its body (the title when it has none). The arguments are the article's.
        /// </summary>
        internal static int SendArticle(ArticleRecord article)
        {
            if (article == null || !ArticleLog.IsValid(article.titleKey)) return -1;

            var tags = new List<string>();
            IReadOnlyList<ModMailRecord> sent = ModMail.FindAll(Feature, ArticleLog.TagPrefix);
            for (int i = 0; i < sent.Count; i++) tags.Add(sent[i].Tag);

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = ArticleLog.TagOf(ArticleLog.NextNumber(tags)),
                TitleKey = article.titleKey,
                BodyKey = string.IsNullOrEmpty(article.bodyKey) ? article.titleKey : article.bodyKey,
                Args = article.args == null ? new string[0] : article.args.ToArray(),
                Priority = MailPriority.Normal,
            };
            return Accepted(ModMail.Send(message), "the article '" + article.titleKey + "'");
        }

        private static int Accepted(int id, string what)
        {
            if (id >= ModMail.FirstModMailId) ModLog.Debug(Feature, "mail: " + what + " is mail #" + id);
            else ModLog.Warn(Feature, "mail: the letter about " + what + " was not accepted by the mail service");
            return id;
        }
    }
}
