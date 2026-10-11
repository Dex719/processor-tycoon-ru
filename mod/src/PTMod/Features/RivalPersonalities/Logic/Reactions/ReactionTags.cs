using System;
using System.Globalization;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The names the reactions are known by outside the feature (FR-7): the ids of the events of the pack, the types of the effects the feature
    /// registers in the event system, the tags of its mails and the id of the mail action. Pure; the spelling is part of the content
    /// (<c>events.json</c>) and of the saved mails, so it must not change.
    /// </summary>
    public static class ReactionTags
    {
        /// <summary>The tag of the intelligence mail of a planned reaction: <c>rival:intel:&lt;reactionId&gt;</c> (FR-7 item 1).</summary>
        public const string IntelPrefix = "rival:intel:";

        /// <summary>The tag of the mail "Counter-strike" about a released CPU: <c>rival:counter:&lt;reactionId&gt;</c> (FR-7 item 4).</summary>
        public const string CounterPrefix = "rival:counter:";

        /// <summary>The tag of the mail "Partnership offer" of a foundry: <c>rival:partner:&lt;yyyy-MM&gt;:&lt;UniqueID&gt;</c> (FR-12 item 1).</summary>
        public const string PartnerPrefix = "rival:partner:";

        /// <summary>The id of the action "Open intel" of the mails of the feature (<c>ModMailAction.Id</c>).</summary>
        public const string ActionOpenIntel = "openIntel";

        /// <summary>The id of the action "Open negotiations" of the mail "Partnership offer" (<c>ModMailAction.Id</c>, FR-12 item 1).</summary>
        public const string ActionOpenNegotiation = "openNegotiation";

        public const string EventPriceCut = "rival.priceCut";
        public const string EventCounterLaunch = "rival.counterLaunch";
        public const string EventOemPush = "rival.oemPush";
        public const string EventPoach = "rival.poach";
        public const string EventSmear = "rival.smear";

        /// <summary>The id of the pack of the feature in the event system; also the folder of its content and the switch that freezes it.</summary>
        public const string PackId = "rival-personalities";

        /// <summary>
        /// The config file of the pack for the event system (<c>Content/rival-personalities/pack.json</c>: the category <c>rival</c>). It is not <c>config.json</c>: that
        /// file holds the numbers of the feature itself, and the engine would read them as the config of the pack.
        /// </summary>
        public const string PackConfigFile = "pack";

        /// <summary>The prefix of every event id of the pack (<c>ModIds.IsEventIdAllowed</c>).</summary>
        public const string EventPrefix = "rival.";

        public const string EffectPriceCut = "RivalPriceCut";
        public const string EffectCounterLaunch = "RivalCounterLaunch";
        public const string EffectResearchBoost = "RivalResearchBoost";
        public const string EffectPoachCounterOffer = "RivalPoachCounterOffer";
        public const string EffectAttitude = "RivalAttitude";

        /// <summary>The five events of the pack, in the order of <c>events.json</c> (the self-check looks for them in the event system).</summary>
        public static readonly string[] EventIds = { EventPriceCut, EventCounterLaunch, EventOemPush, EventPoach, EventSmear };

        /// <summary>The five effect types the feature registers in the event system (the self-check looks for them).</summary>
        public static readonly string[] EffectTypes = { EffectPriceCut, EffectCounterLaunch, EffectResearchBoost, EffectPoachCounterOffer, EffectAttitude };

        /// <summary>The choice of <c>rival.poach</c> that pays the engineers.</summary>
        public const string ChoiceCounterOffer = "counterOffer";

        /// <summary>The choice of <c>rival.poach</c> that lets them go (the default when the timer runs out).</summary>
        public const string ChoiceLetGo = "letGo";

        /// <summary>The tag of the intelligence mail of a reaction.</summary>
        public static string Intel(string reactionId)
        {
            return IntelPrefix + (reactionId ?? string.Empty);
        }

        /// <summary>The tag of the counter-strike mail of a reaction.</summary>
        public static string Counter(string reactionId)
        {
            return CounterPrefix + (reactionId ?? string.Empty);
        }

        /// <summary>The tag of the mail "Partnership offer": <c>rival:partner:1990-05:9</c> for the offer of the company 9 in May 1990.</summary>
        public static string Partner(DateTime month, int uniqueId)
        {
            return PartnerPrefix + month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ":" + uniqueId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The <c>UniqueID</c> of the foundry of a partnership mail tag. False for another tag, a month that is not <c>yyyy-MM</c> and an id that is not a whole number.</summary>
        public static bool TryParsePartner(string tag, out int uniqueId)
        {
            uniqueId = -1;
            if (tag == null || tag.Length <= PartnerPrefix.Length || !tag.StartsWith(PartnerPrefix, StringComparison.Ordinal)) return false;

            string rest = tag.Substring(PartnerPrefix.Length);
            int colon = rest.IndexOf(':');
            if (colon != 7) return false;

            DateTime month;
            if (!DateTime.TryParseExact(rest.Substring(0, colon), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month)) return false;

            int value;
            if (!int.TryParse(rest.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out value)) return false;
            uniqueId = value;
            return true;
        }

        /// <summary>The reaction id of an intelligence mail tag. False for another tag and for an empty id.</summary>
        public static bool TryParseIntel(string tag, out string reactionId)
        {
            return TryParse(tag, IntelPrefix, out reactionId);
        }

        /// <summary>The reaction id of a counter-strike mail tag. False for another tag and for an empty id.</summary>
        public static bool TryParseCounter(string tag, out string reactionId)
        {
            return TryParse(tag, CounterPrefix, out reactionId);
        }

        /// <summary>The id of the event of the pack that carries the reaction out; null for a reaction that is no event (<see cref="ReactionType.Partnership"/>).</summary>
        public static string EventIdOf(ReactionType type)
        {
            switch (type)
            {
                case ReactionType.PriceCut: return EventPriceCut;
                case ReactionType.CounterLaunch: return EventCounterLaunch;
                case ReactionType.OemPush: return EventOemPush;
                case ReactionType.Poach: return EventPoach;
                case ReactionType.Smear: return EventSmear;
                default: return null;
            }
        }

        /// <summary>The reaction that an event of the pack stands for. False for any other event id.</summary>
        public static bool TryReactionOfEvent(string eventId, out ReactionType type)
        {
            for (int i = 0; i < 5; i++)
            {
                var candidate = (ReactionType)i;
                if (string.Equals(EventIdOf(candidate), eventId, StringComparison.Ordinal))
                {
                    type = candidate;
                    return true;
                }
            }
            type = ReactionType.PriceCut;
            return false;
        }

        /// <summary>
        /// The event is aimed at the player (<c>Poach</c>, <c>Smear</c>: the rival is passed in the arguments); the others are aimed at the rival
        /// (<c>EventFireContext.CompanyKey</c> is <c>ai:&lt;UniqueID&gt;</c>).
        /// </summary>
        public static bool TargetsPlayer(ReactionType type)
        {
            return type == ReactionType.Poach || type == ReactionType.Smear;
        }

        /// <summary>
        /// The reaction starts in the code of one of the feature's own effect handlers (<c>Apply</c> reports the start with its numbers): the
        /// others (<c>OemPush</c> and <c>Smear</c>) are carried out by standard effects of the event system and start when the event fires.
        /// </summary>
        public static bool StartsInOwnHandler(ReactionType type)
        {
            return type == ReactionType.PriceCut || type == ReactionType.CounterLaunch || type == ReactionType.Poach;
        }

        /// <summary>
        /// The reaction holds something in the game that only the feature's own handler gives back (the target prices of the price book, the
        /// boosts of the behaviour); the end of the others is the event system's business.
        /// </summary>
        public static bool HoldsState(ReactionType type)
        {
            return type == ReactionType.PriceCut || type == ReactionType.CounterLaunch;
        }

        private static bool TryParse(string tag, string prefix, out string reactionId)
        {
            reactionId = null;
            if (tag == null || tag.Length <= prefix.Length || !tag.StartsWith(prefix, StringComparison.Ordinal)) return false;
            reactionId = tag.Substring(prefix.Length);
            return true;
        }
    }
}
