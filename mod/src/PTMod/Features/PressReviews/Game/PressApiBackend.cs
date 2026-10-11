using System;
using System.Collections.Generic;
using PTMod.Core;
using Cpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The store behind the public <see cref="PressReviewsApi"/> (FR-11, design.md Component 11): it files the articles other features publish and sends
    /// their letters, keeps the adjusters of the reviews (<see cref="AdjusterBook{T}"/>) and hands out copies of the reviews of the archive. The
    /// <see cref="PressReviewsApi"/> checks that the feature is enabled and catches what this class throws; the rules are the pure
    /// <see cref="ArticleLog"/>, <see cref="AdjusterBook{T}"/>, <see cref="AdjusterPipeline"/> and <see cref="ReviewRecordCopy"/>.
    /// The feature attaches it once in <c>Register</c> and never detaches it: the API stands in for it while the feature is off.
    /// </summary>
    internal sealed class PressApiBackend : IPressApiBackend
    {
        private const string Feature = PressReviewsFeature.FeatureId;

        private readonly Func<PressState> stateOf;
        private readonly AdjusterBook<IReviewAdjuster> adjusters = new AdjusterBook<IReviewAdjuster>(adjuster => adjuster.Id);

        internal PressApiBackend(Func<PressState> stateOf)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            this.stateOf = stateOf;
        }

        // ---- articles ----

        public void PublishArticle(PressArticle article)
        {
            if (article == null) return;

            if (!ArticleLog.IsValid(article.TitleKey))
            {
                ModLog.Warn(Feature, "api: an article of '" + (article.SourceFeature ?? string.Empty) + "' has no title key and is ignored");
                return;
            }
            if (!string.IsNullOrEmpty(article.CompanyKey)) ModLog.Debug(Feature, "api: an article of '" + article.SourceFeature + "' is about " + article.CompanyKey);

            Publish(article.SourceFeature, article.TitleKey, article.BodyKey, ArticleLog.Stringify(article.Args));
        }

        /// <summary>Files an article in the log and sends its letter; the press uses it for its own articles too (the flaw bridge). False when there was no date to file it under.</summary>
        internal bool Publish(string source, string titleKey, string bodyKey, IList<string> args)
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime))
            {
                ModLog.Warn(Feature, "api: an article '" + titleKey + "' was published when the game has no date; it is dropped");
                return false;
            }

            ArticleRecord record = ArticleLog.Add(stateOf(), today, source, titleKey, bodyKey, args);
            if (record == null) return false;

            if (!ModL10n.Has(titleKey)) ModLog.Warn(Feature, "api: the title key '" + titleKey + "' of an article is in no string table");
            PressMail.SendArticle(record);
            return true;
        }

        // ---- adjusters ----

        public bool RegisterAdjuster(IReviewAdjuster adjuster)
        {
            AdjusterRegistration result = adjusters.Register(adjuster);
            if (result != AdjusterRegistration.Rejected) ModLog.Debug(Feature, "api: adjuster '" + adjuster.Id + "' is " + (result == AdjusterRegistration.Added ? "registered" : "replaced"));
            return result != AdjusterRegistration.Rejected;
        }

        public void UnregisterAdjuster(string id)
        {
            if (adjusters.Unregister(id)) ModLog.Debug(Feature, "api: adjuster '" + id + "' is unregistered");
        }

        /// <summary>
        /// The adjusters to ask about the review of <paramref name="cpu"/>, in the order of registration; null when there are none. An adjuster of a feature
        /// that is not enabled is not asked. A failure of one is counted against the feature that owns its class (the press itself when the class is not in a
        /// feature), its correction is skipped (<see cref="AdjusterPipeline"/>) and the review goes on.
        /// </summary>
        internal AdjusterSet AdjustersFor(Cpu cpu)
        {
            List<KeyValuePair<string, IReviewAdjuster>> registered = adjusters.Snapshot();
            if (registered.Count == 0) return null;

            var steps = new List<AdjusterStep>(registered.Count);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < registered.Count; i++)
            {
                string id = registered[i].Key;
                IReviewAdjuster adjuster = registered[i].Value;
                string owner = OwnerOf(adjuster);
                if (!ModFeatures.IsActive(owner)) continue;

                owners[id] = owner;
                steps.Add(new AdjusterStep { Id = id, Run = draft => adjuster.Adjust(cpu, draft) });
            }
            if (steps.Count == 0) return null;

            return new AdjusterSet
            {
                Steps = steps,
                IsNoteKnown = ModL10n.Has,
                OnError = (id, ex) =>
                {
                    string owner;
                    if (!owners.TryGetValue(id, out owner)) owner = Feature;
                    ModLog.Warn(Feature, "api: adjuster '" + id + "' failed, its correction is skipped: " + ex.GetType().Name + ": " + ex.Message);
                    ModHooks.ReportFault(owner, "press-reviews/adjuster/" + id, ex);
                },
            };
        }

        private static string OwnerOf(IReviewAdjuster adjuster)
        {
            string owner = ModFeatures.FeatureOfType(adjuster.GetType());
            return string.IsNullOrEmpty(owner) ? Feature : owner;
        }

        // ---- reviews ----

        public ReviewRecord GetReview(int cpuSaveId)
        {
            return ReviewRecordCopy.Of(ReviewArchive.Find(stateOf(), cpuSaveId));
        }

        /// <summary>
        /// <c>ReviewScheduler.Published</c>: the review is in the archive, its effects are in and the letter is sent. The subscribers of
        /// <see cref="PressReviewsApi.ReviewPublished"/> get a copy.
        /// </summary>
        internal void OnReviewPublished(ReviewRecord record)
        {
            if (record == null) return;
            PressReviewsApi.RaiseReviewPublished(ReviewRecordCopy.Of(record));
        }
    }
}
