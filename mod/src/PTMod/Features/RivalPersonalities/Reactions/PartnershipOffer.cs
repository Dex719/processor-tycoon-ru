using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameNegotiationWindow = ProcessorTycoon.CompanySystem.Business.ContractNegotiationWindow;
using GamePlayer = ProcessorTycoon.Player;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using UnityObject = UnityEngine.Object;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The friendly offer of a foundry (design.md, Component 7 "partnership roll"; FR-12, TSK-022): the game side of <see cref="PartnershipOfferCore"/>.
    /// <list type="bullet">
    /// <item><b>The roll</b> runs in the monthly pass (<see cref="MonthlyEvaluatorCore"/>, after the drift of the attitude): a foundry with an attitude of
    /// +40 or more and no contract with the player rolls 0.10 once a month, at most once in six months.</item>
    /// <item><b>The mail</b> is <c>Low</c>, "Partnership offer from &lt;foundry&gt;", tag <c>rival:partner:&lt;yyyy-MM&gt;:&lt;UniqueID&gt;</c>, with the action
    /// "Open negotiations" (<see cref="ReactionTags.ActionOpenNegotiation"/>, repeatable, the mail stays open).</item>
    /// <item><b>The action</b> (<see cref="OnAction"/>, called by <see cref="IntelMail.OnAction"/>) opens the game's window of business negotiations with the
    /// foundry as the provider and the player as the client (<c>ContractNegotiationWindow.Open(foundry, player, false)</c>). The bonus to the interest and the
    /// discount on the markup of this foundry are the negotiation patches' (TSK-017).</item>
    /// <item><b>The record.</b> The offer is a line in the history of the rival ("Partnership offer: started") and <c>ReactionStarted(uid, "Partnership")</c>
    /// of the API.</item>
    /// </list>
    /// Main thread only; the callers guard the calls. The stream of the dice is <c>rival-personalities/partner</c>, so the offers do not move the dice of the
    /// hostile reactions.
    /// </summary>
    internal sealed class PartnershipOffer : IPartnershipMail
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        /// <summary>The stream of <c>ModRandom</c> of the monthly roll.</summary>
        public const string StreamId = "rival-personalities/partner";

        /// <summary>The name of the reaction in the history and in the API (<see cref="ReactionType.Partnership"/>).</summary>
        public static readonly string TypeName = ReactionKinds.NameOf(ReactionType.Partnership);

        private readonly RivalPersonalitiesFeature _feature;
        private readonly PartnershipOfferCore _core;

        public PartnershipOffer(RivalPersonalitiesFeature feature, PersonalityBook book, AttitudeLedger attitudes, ReactionBook reactions)
        {
            _feature = feature;
            _core = new PartnershipOfferCore(book, attitudes, reactions, () => feature.Catalog.Config, this, () => ModRandom.Stream(StreamId),
                line => ModLog.Info(Feature, line));
            _core.Offered += OnOffered;
        }

        /// <summary>The rules (shared with the tests).</summary>
        public PartnershipOfferCore Core
        {
            get { return _core; }
        }

        // ---- IPartnershipMail ----

        /// <inheritdoc />
        public int SendOffer(PartnershipNotice notice)
        {
            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = ReactionTags.Partner(notice.Month, notice.UniqueId),
                TitleKey = "rival.mail.partner.subject",
                BodyKey = "rival.mail.partner.body",
                // {0} the foundry
                Args = new[] { AttitudeNotifier.NameOf(notice.UniqueId) },
                Priority = MailPriority.Low,
                Actions = new List<ModMailAction>
                {
                    new ModMailAction { Id = ReactionTags.ActionOpenNegotiation, LabelKey = "rival.mail.partner.open", Repeatable = true },
                },
            };
            return ModMail.Send(message);
        }

        /// <summary>An offer was sent: a line in the history of the rival and the event of the API.</summary>
        private void OnOffered(int uniqueId)
        {
            _feature.Reactions.AddHistory(uniqueId, ModTicks.Today, TypeName, -1, HistoryOutcomes.Started);
            _feature.Api.OnReactionStarted(uniqueId, TypeName);
        }

        // ---- the action of the mail ----

        /// <summary>
        /// The handler of the action "Open negotiations" (<c>ModMail.RegisterActionHandler</c>, through <see cref="IntelMail.OnAction"/>). Opens the window of
        /// the game on the foundry of the mail. The mail stays open. A failure is logged and the player stays where he was.
        /// </summary>
        internal static void OnAction(ModMailRecord record)
        {
            ModHooks.Guard(Feature, "PartnershipOffer/openNegotiation", () => OpenNegotiation(record));
        }

        private static void OpenNegotiation(ModMailRecord record)
        {
            int uniqueId;
            if (record == null || !ReactionTags.TryParsePartner(record.Tag, out uniqueId))
            {
                ModLog.Warn(Feature, "partnership: the mail has no readable tag, the negotiations were not opened");
                return;
            }

            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            AICompany foundry = feature != null ? feature.Registry.FindRival(uniqueId) : null;
            GamePlayer player = GamePlayer.Instance;
            ICompany playerCompany = player != null ? player.Company : null;
            if (foundry == null || ReferenceEquals(playerCompany, null))
            {
                ModLog.Warn(Feature, "partnership: the foundry ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) + " or the player's company is not in the game");
                return;
            }

            GameNegotiationWindow window = UnityObject.FindFirstObjectByType<GameNegotiationWindow>(UnityEngine.FindObjectsInactive.Include);
            if (window == null)
            {
                ModLog.Warn(Feature, "partnership: the window of the negotiations was not found in the scene");
                return;
            }

            // the foundry is the provider, the player the client; "keep the player as the provider" would turn the window round when it was left that way
            window.Open(foundry, playerCompany, false);
            ModLog.Debug(Feature, "partnership: negotiations with ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) + " opened from the mail");
        }
    }
}
