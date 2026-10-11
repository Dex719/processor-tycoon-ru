using System;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What goes into the mail "Partnership offer" (FR-12 item 1): which foundry and the month of the offer (it is in the tag of the mail).</summary>
    public struct PartnershipNotice
    {
        public PartnershipNotice(int uniqueId, DateTime month)
        {
            UniqueId = uniqueId;
            Month = month;
        }

        public int UniqueId;

        /// <summary>The day of the offer; the tag of the mail takes the year and the month of it.</summary>
        public DateTime Month;
    }

    /// <summary>Sends the mail "Partnership offer" (<c>ModMail.Send</c> in the game).</summary>
    public interface IPartnershipMail
    {
        /// <summary>Sends the mail and returns its id (<c>ModMail.Send</c>), or 0 or less when the mail was not accepted.</summary>
        int SendOffer(PartnershipNotice notice);
    }

    /// <summary>What one monthly roll of the friendly offers did (for the log and the tests).</summary>
    public struct PartnershipResult
    {
        /// <summary>The foundries that passed every condition and rolled the dice.</summary>
        public int Rolled;

        /// <summary>The offers that were sent.</summary>
        public int Sent;

        /// <summary>The rolls that succeeded and whose mail was not accepted (nothing was recorded; the next month rolls again).</summary>
        public int Refused;
    }

    /// <summary>
    /// The friendly offer of a foundry (design.md, Component 7 "partnership roll"; FR-12, TSK-022). On the first day of a month (the monthly pass, after the
    /// drift of the attitude) every foundry that
    /// <list type="bullet">
    /// <item>is not bankrupt or dormant and has a character, with the attitude to the player at least <c>reactions.Partnership.minAttitude</c> (+40),</item>
    /// <item>has no contract with the player (it is not among <see cref="MonthlyInput.ContractRivals"/>),</item>
    /// <item>sent no offer in the last <c>repeatMonths</c> (6) calendar months (<see cref="ReactionBook.MonthsSincePartnerOffer"/>),</item>
    /// </list>
    /// rolls <c>monthlyChance</c> (0.10) once. A success sends the mail <see cref="PartnershipMail"/> (<c>Low</c>, tag <c>rival:partner:&lt;yyyy-MM&gt;:&lt;UniqueID&gt;</c>,
    /// the action "Open negotiations") and records the day, so the next offer of this foundry waits for the cooldown whether or not the player took it up.
    /// A condition that fails costs no draw of the stream, so the dice do not shift because of who is eligible. Pure; the game side is <c>PartnershipOffer</c>.
    /// </summary>
    public sealed class PartnershipOfferCore
    {
        private readonly PersonalityBook _book;
        private readonly AttitudeLedger _attitudes;
        private readonly ReactionBook _reactions;
        private readonly Func<RivalConfig> _config;
        private readonly IPartnershipMail _mail;
        private readonly Func<IModRng> _rng;
        private readonly Action<string> _log;

        public PartnershipOfferCore(PersonalityBook book, AttitudeLedger attitudes, ReactionBook reactions, Func<RivalConfig> config,
                                    IPartnershipMail mail, Func<IModRng> rng, Action<string> log = null)
        {
            _book = book;
            _attitudes = attitudes;
            _reactions = reactions;
            _config = config;
            _mail = mail;
            _rng = rng;
            _log = log;
        }

        /// <summary>An offer was sent: <c>UniqueID</c> of the foundry (the API raises <c>ReactionStarted(uid, "Partnership")</c>). A handler that throws is logged and the others still run.</summary>
        public event Action<int> Offered;

        /// <summary>
        /// The roll of one month; the rivals are taken in the order of <paramref name="input"/>. An exception of the mail service or of a handler is
        /// logged and does not stop the roll of the other foundries.
        /// </summary>
        public PartnershipResult Run(MonthlyInput input)
        {
            var result = new PartnershipResult();
            if (input == null || input.Rivals == null || _mail == null) return result;

            RivalConfig config = _config != null ? _config() : null;
            RivalConfig.PartnershipReaction rules = config != null && config.reactions != null && config.reactions.Partnership != null
                ? config.reactions.Partnership
                : RivalConfig.Defaults().reactions.Partnership;
            DateTime today = input.Today;
            if (today == default(DateTime)) return result;

            IModRng rng = null;
            foreach (RivalMarketView rival in input.Rivals)
            {
                if (!IsEligible(rival, input, rules, today)) continue;

                if (rng == null) rng = _rng != null ? _rng() : null;
                if (rng == null) return result;

                result.Rolled++;
                if (!rng.Chance(rules.monthlyChance)) continue;

                int id = Send(rival.UniqueId, today);
                if (id <= 0)
                {
                    result.Refused++;
                    Say("the offer of ai:" + rival.UniqueId.ToString(CultureInfo.InvariantCulture) + " was not accepted by the mail service; it rolls again next month");
                    continue;
                }

                _reactions.MarkPartnerOffer(rival.UniqueId, today);
                result.Sent++;
                Say("partnership offer of ai:" + rival.UniqueId.ToString(CultureInfo.InvariantCulture) + " sent (mail " + id.ToString(CultureInfo.InvariantCulture) + ")");
                RaiseOffered(rival.UniqueId);
            }
            return result;
        }

        /// <summary>Sends the mail; an exception of the mail service is an offer that was not accepted (0), so one foundry cannot stop the other.</summary>
        private int Send(int uniqueId, DateTime today)
        {
            try
            {
                return _mail.SendOffer(new PartnershipNotice(uniqueId, today));
            }
            catch (Exception e)
            {
                Say("the mail service failed for ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) + " (" + e.GetType().Name + ": " + e.Message + ")");
                return 0;
            }
        }

        /// <summary>Raises <see cref="Offered"/> handler by handler: one that throws is logged and does not stop the others or the roll.</summary>
        private void RaiseOffered(int uniqueId)
        {
            Action<int> handlers = Offered;
            if (handlers == null) return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)handler)(uniqueId);
                }
                catch (Exception e)
                {
                    Say("a handler of Offered failed (" + e.GetType().Name + ": " + e.Message + ")");
                }
            }
        }

        /// <summary>The foundry may roll this month (every condition of FR-12 holds); does not draw anything.</summary>
        public bool IsEligible(RivalMarketView rival, MonthlyInput input, RivalConfig.PartnershipReaction rules, DateTime today)
        {
            if (rival == null || !rival.IsFoundry || rival.IsBankrupt || rival.UniqueId < 0) return false;
            if (_book.IsDormant(rival.UniqueId) || !_attitudes.IsKnown(rival.UniqueId)) return false;
            if (_attitudes.Get(rival.UniqueId) < rules.minAttitude) return false;
            if (input.ContractRivals != null && input.ContractRivals.Contains(rival.UniqueId)) return false;
            return _reactions.MonthsSincePartnerOffer(rival.UniqueId, today) >= rules.repeatMonths;
        }

        private void Say(string line)
        {
            if (_log != null) _log("partnership: " + line);
        }
    }
}
