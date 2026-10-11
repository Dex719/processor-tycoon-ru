using System;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The static entry point to the public API of <c>press-reviews</c> for the features that use it softly (FR-7 item 3, FR-11, design.md Component 11):
    /// <list type="bullet">
    /// <item><see cref="ReviewPublished"/>: a review was published and is in the archive;</item>
    /// <item><see cref="PublishArticle"/>: a letter and a line in the tab "Articles";</item>
    /// <item><see cref="RegisterAdjuster"/> / <see cref="UnregisterAdjuster"/>: corrections of the reviews (<see cref="IReviewAdjuster"/>);</item>
    /// <item><see cref="GetReview"/>: the review of a CPU, or <c>null</c>;</item>
    /// <item><see cref="GetReputationLevelKey"/> and <see cref="FormatReputation"/>: the reputation of a brand in words ("68 — Respected").</item>
    /// </list>
    /// Everything works whether the feature is on or off: while it is not enabled <see cref="IsEnabled"/> is <c>false</c> and every call does nothing and
    /// answers like a null object (<c>null</c>, no letter); nothing throws (a failure is counted against <c>press-reviews</c>). A feature that uses the
    /// press only softly checks <c>ModFeatures.IsActive("press-reviews")</c> before it wires itself, and wires itself again on
    /// <c>ModFeatures.StateChanged</c>. The reputation itself is the core's (<c>ModReputation</c>), so its two readers answer whether the feature is on or off.
    /// The press is written for the main thread. The folder <c>Api</c> is what the dependent features are built with, so nothing here may name a type
    /// outside it.
    /// <code>
    /// // another feature
    /// string line = PressReviewsApi.FormatReputation(company);   // "68 — Уважаемый"
    /// PressReviewsApi.ReviewPublished += OnReview;                // unsubscribe when your feature is disabled
    /// PressReviewsApi.RegisterAdjuster(new MyAdjuster());        // when ModFeatures.IsActive("press-reviews")
    /// PressReviewsApi.PublishArticle(new PressArticle { SourceFeature = "my-feature", TitleKey = "my.press.title", Args = new object[] { cpuName } });
    /// </code>
    /// </summary>
    public static partial class PressReviewsApi
    {
        /// <summary>The id of the feature (<c>IModFeature.Id</c>, the key for <c>ModFeatures.IsActive</c> and the owner of its log lines).</summary>
        public const string FeatureId = "press-reviews";

        /// <summary>The site the failures of the API are counted under.</summary>
        private const string Site = "api";

        /// <summary>The thresholds of the levels when the feature has not started (the numbers of the built-in <c>review-config.json</c>): 80, 65, 36, 21.</summary>
        private static readonly int[] DefaultMins = { 80, 65, 36, 21 };

        private static readonly string[] DefaultKeys =
        {
            "press.rep.cult", "press.rep.respected", "press.rep.ordinary", "press.rep.dubious", "press.rep.toxic",
        };

        /// <summary>The store of the feature; null before it started.</summary>
        private static IPressApiBackend backend;

        /// <summary>
        /// Set by the feature at its start: the key of the level of a reputation by the thresholds of its configuration (the player can override them).
        /// Null until then; the built-in thresholds answer.
        /// </summary>
        internal static Func<float, string> LevelKeyProvider;

        /// <summary>The feature is enabled: it reviews, moves the reputation and shows its windows.</summary>
        public static bool IsEnabled
        {
            get { return ModFeatures.IsActive(FeatureId); }
        }

        // ---- reviews, articles, adjusters (FR-11) ----

        /// <summary>
        /// A review was published (FR-11 item 1): it is in the archive, its reputation and hype are in, the letter of the player's CPU is sent. The record
        /// is a copy, read-only for the subscriber (changing it changes nothing in the archive). Raised for every company's CPU, the player's and the AI's;
        /// not for a review that was only queued, not while a save loads. A handler is called through <c>ModHooks.Raise</c>: handlers of features that are
        /// not enabled are skipped, an exception in one is counted against its own feature and the next handler still runs. Subscribe when your feature is
        /// enabled and unsubscribe when it is disabled.
        /// </summary>
        public static event Action<ReviewRecord> ReviewPublished;

        /// <summary>
        /// FR-11 item 2. Files the article in the tab "Articles" of the window "Press" (the last 200 are kept) and sends a letter in the category "Press"
        /// (priority <c>Normal</c>, tag <c>press:article:&lt;n&gt;</c>, the subject is the title). Does nothing when the feature is not enabled, when the
        /// article is <c>null</c> or has no <see cref="PressArticle.TitleKey"/>.
        /// </summary>
        public static void PublishArticle(PressArticle article)
        {
            if (article == null) return;

            IPressApiBackend store = ActiveBackend();
            if (store == null) return;

            try
            {
                store.PublishArticle(article);
            }
            catch (Exception ex)
            {
                Fault("PressReviewsApi.PublishArticle", ex);
            }
        }

        /// <summary>
        /// FR-11 item 3. Adds a correction of the reviews (see <see cref="IReviewAdjuster"/>). The same <see cref="IReviewAdjuster.Id"/> replaces the
        /// earlier adjuster, so registering on every start is safe. Not taken (and logged at debug level) while the feature is not enabled: register again on
        /// <c>ModFeatures.StateChanged</c> when it is back. A <c>null</c> adjuster, an empty id and more than 32 adjusters are refused.
        /// </summary>
        public static void RegisterAdjuster(IReviewAdjuster adjuster)
        {
            if (adjuster == null) return;

            IPressApiBackend store = ActiveBackend();
            if (store == null)
            {
                ModLog.Debug(FeatureId, "api: an adjuster is not taken, the feature is not enabled");
                return;
            }

            try
            {
                if (!store.RegisterAdjuster(adjuster)) ModLog.Warn(FeatureId, "api: an adjuster was refused (no id, or too many adjusters)");
            }
            catch (Exception ex)
            {
                Fault("PressReviewsApi.RegisterAdjuster", ex);
            }
        }

        /// <summary>Takes the adjuster with this id out; nothing happens for an id that is not registered. Works whether the feature is enabled or not.</summary>
        public static void UnregisterAdjuster(string id)
        {
            IPressApiBackend store = backend;
            if (store == null || string.IsNullOrEmpty(id)) return;

            try
            {
                store.UnregisterAdjuster(id);
            }
            catch (Exception ex)
            {
                Fault("PressReviewsApi.UnregisterAdjuster", ex);
            }
        }

        /// <summary>
        /// FR-11 item 4. The review of the CPU (by <c>Cpu.SaveID</c>), a copy of the record of the archive; <c>null</c> when the CPU has no review (not
        /// reviewed yet, a variant, or the feature is not enabled).
        /// </summary>
        public static ReviewRecord GetReview(int cpuSaveId)
        {
            IPressApiBackend store = ActiveBackend();
            if (store == null) return null;

            try
            {
                return store.GetReview(cpuSaveId);
            }
            catch (Exception ex)
            {
                Fault("PressReviewsApi.GetReview", ex);
                return null;
            }
        }

        // ---- the reputation in words (FR-7) ----

        /// <summary>
        /// The key of the level of the company's reputation (<c>press.rep.cult</c>, <c>respected</c>, <c>ordinary</c>, <c>dubious</c>, <c>toxic</c>); the key of
        /// "ordinary" for a company the core knows no reputation of (it starts at 50).
        /// </summary>
        public static string GetReputationLevelKey(ICompany company)
        {
            return LevelKeyOf(ModReputation.Get(company));
        }

        /// <summary>The reputation and its level as a line for the player: <c>68 — Respected</c>, in the current language.</summary>
        public static string FormatReputation(ICompany company)
        {
            float reputation = ModReputation.Get(company);
            int whole = (int)Math.Round(reputation, MidpointRounding.AwayFromZero);
            return ModL10n.L("press.rep.short", whole, ModL10n.L(LevelKeyOf(reputation)));
        }

        // ---- for the implementation of the feature (internal) ----

        /// <summary>Attaches the store of the feature (its <c>Register</c>); <c>null</c> detaches. The public methods delegate to it while the feature is enabled.</summary>
        internal static void AttachBackend(IPressApiBackend store)
        {
            backend = store;
        }

        /// <summary>Raises <see cref="ReviewPublished"/> with the copy of a record made for the subscribers. Never throws.</summary>
        internal static void RaiseReviewPublished(ReviewRecord copy)
        {
            if (copy == null) return;

            try
            {
                ModHooks.Raise("PressReviewsApi.ReviewPublished", ReviewPublished, copy);
            }
            catch (Exception ex)
            {
                Fault("PressReviewsApi.RaiseReviewPublished", ex);
            }
        }

        /// <summary>The key of the level of a reputation value: the feature's own thresholds once it has started, the built-in ones before.</summary>
        internal static string LevelKeyOf(float reputation)
        {
            Func<float, string> provider = LevelKeyProvider;
            if (provider != null)
            {
                try
                {
                    string key = provider(reputation);
                    if (!string.IsNullOrEmpty(key)) return key;
                }
                catch (Exception ex)
                {
                    ModLog.Warn(FeatureId, "api: the levels of the reputation could not be read, the built-in thresholds answer: " + ex.GetType().Name);
                }
            }

            if (float.IsNaN(reputation)) reputation = 50f;
            for (int i = 0; i < DefaultMins.Length; i++)
            {
                if (reputation >= DefaultMins[i]) return DefaultKeys[i];
            }
            return DefaultKeys[DefaultKeys.Length - 1];
        }

        /// <summary>The store, when it is attached and the feature is enabled; null otherwise (the null-object answer).</summary>
        private static IPressApiBackend ActiveBackend()
        {
            IPressApiBackend store = backend;
            return store != null && ModFeatures.IsActive(FeatureId) ? store : null;
        }

        private static void Fault(string where, Exception ex)
        {
            ModHooks.ReportFault(FeatureId, Site + "/" + where, ex);
        }
    }
}
