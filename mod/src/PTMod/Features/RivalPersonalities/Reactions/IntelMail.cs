using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The mails of the feature (design.md, Component 8; FR-7 items 1 and 4): the intelligence letter that warns about a planned reaction, and the
    /// letter "Counter-strike" about the CPU a rival released as its answer. Both are <c>Normal</c> (the count of the warning starts at the delivery,
    /// so the daily limit of the core cannot shorten it, D12), carry the tag <c>rival:intel:&lt;reactionId&gt;</c> / <c>rival:counter:&lt;reactionId&gt;</c>
    /// and the action "Open intel" (<see cref="ReactionTags.ActionOpenIntel"/>): it opens the intelligence window and leaves the mail open. The
    /// action handler is one for the whole feature (<c>ModMail.RegisterActionHandler</c>, in <c>Register</c>); the window itself is TSK-018's, it
    /// plugs in through <see cref="OpenIntel"/>.
    /// </summary>
    internal sealed class IntelMail : IIntelOutput
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        /// <summary>
        /// Opens the intelligence window on a company (null: no company selected). Set by the window when it is built (TSK-018); null until then,
        /// so the action is only logged.
        /// </summary>
        public static Action<int?> OpenIntel { get; set; }

        /// <inheritdoc />
        public int SendIntel(IntelNotice notice)
        {
            string weeks = notice.WeeksFrom == notice.WeeksTo
                ? notice.WeeksFrom.ToString(CultureInfo.InvariantCulture)
                : notice.WeeksFrom.ToString(CultureInfo.InvariantCulture) + "–" + notice.WeeksTo.ToString(CultureInfo.InvariantCulture);

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = ReactionTags.Intel(notice.ReactionId),
                TitleKey = "rival.mail.intel.subject",
                BodyKey = "rival.mail.intel." + ReactionExecutor.KeyName(notice.Type),
                // {0} the rival, {1} the segment (empty for a reaction that has none), {2} the range of weeks
                Args = new[] { AttitudeNotifier.NameOf(notice.UniqueId), ReactionExecutor.SegmentName((int)notice.Segment), weeks },
                Priority = MailPriority.Normal,
                Actions = new List<ModMailAction> { OpenAction() },
            };
            int id = ModMail.Send(message);
            ModLog.Debug(Feature, "mail: intel " + notice.ReactionId + " sent (" + id.ToString(CultureInfo.InvariantCulture) + ")");
            return id;
        }

        /// <summary>
        /// "Counter-strike" (FR-7 item 4): the rival released a CPU in the segment of its <c>CounterLaunch</c>. Returns the id of the mail, or 0 or
        /// less when it was not accepted.
        /// </summary>
        public static int SendCounterStrike(ActiveReaction reaction, string cpuName, string segmentName)
        {
            if (reaction == null) return 0;

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = ReactionTags.Counter(reaction.id),
                TitleKey = "rival.mail.counter.subject",
                BodyKey = "rival.mail.counterLaunchDone",
                // {0} the rival, {1} the CPU, {2} the segment
                Args = new[] { AttitudeNotifier.NameOf(reaction.uid), cpuName ?? string.Empty, segmentName ?? string.Empty },
                Priority = MailPriority.Normal,
                Actions = new List<ModMailAction> { OpenAction() },
            };
            int id = ModMail.Send(message);
            ModLog.Debug(Feature, "mail: counter-strike " + reaction.id + " sent (" + id.ToString(CultureInfo.InvariantCulture) + ")");
            return id;
        }

        /// <summary>
        /// The handler of the actions of the feature's mails (<c>ModMail.RegisterActionHandler</c>): "Open intel" opens the window on the company of
        /// the mail when it can be told (the reaction is still planned or in effect), otherwise on no company; "Open negotiations" of the friendly offer of
        /// a foundry (<see cref="PartnershipOffer.OnAction"/>) opens the negotiations of the game. The mail stays open.
        /// </summary>
        internal static void OnAction(ModMailRecord record, string actionId)
        {
            if (string.Equals(actionId, ReactionTags.ActionOpenNegotiation, StringComparison.Ordinal))
            {
                PartnershipOffer.OnAction(record);
                return;
            }
            if (!string.Equals(actionId, ReactionTags.ActionOpenIntel, StringComparison.Ordinal)) return;

            Action<int?> open = OpenIntel;
            if (open == null)
            {
                ModLog.Info(Feature, "mail action '" + actionId + "': the intelligence window is not available yet");
                return;
            }
            open(record != null ? UniqueIdOf(record.Tag) : null);
        }

        /// <summary>The company of a mail of the feature by its tag: the rival of the reaction, while the reaction is planned or in effect.</summary>
        internal static int? UniqueIdOf(string tag)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null) return null;

            string reactionId;
            if (!ReactionTags.TryParseIntel(tag, out reactionId) && !ReactionTags.TryParseCounter(tag, out reactionId)) return null;

            PendingReaction pending = feature.Reactions.FindPending(reactionId);
            if (pending != null) return pending.uid;
            ActiveReaction active = feature.Reactions.FindActive(reactionId);
            return active != null ? active.uid : (int?)null;
        }

        private static ModMailAction OpenAction()
        {
            return new ModMailAction { Id = ReactionTags.ActionOpenIntel, LabelKey = "rival.mail.open", Repeatable = true };
        }
    }
}
