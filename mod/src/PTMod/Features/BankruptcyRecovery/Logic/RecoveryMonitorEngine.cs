using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The letters of the feature (FR-1, FR-2) as the monitor asks for them. The game side is <c>RecoveryLetters</c> (<c>ModMail</c>, priority Critical, the action
    /// "Recovery options"); a test uses a fake. Each method returns true when the letter is sent or is already on its way, false when it could not be sent (then
    /// the monitor tries again on the next day / the next call).
    /// </summary>
    internal interface IRecoveryLetters
    {
        /// <summary>The "default risk" letter (<c>rec:prewarning</c>): the balance and the threshold (a negative sum).</summary>
        bool SendPreWarning(double balance, double threshold);

        /// <summary>The "company in default" letter (<c>rec:default</c>): the balance, the threshold (a negative sum) and the days left before the bankruptcy.</summary>
        bool SendDefault(double balance, double threshold, int daysToBankruptcy);
    }

    /// <summary>
    /// The watcher of the player's default (design.md, <c>RecoveryMonitor</c>; FR-1, FR-2) without the game: the "default risk" letter when the balance first falls
    /// below <c>-θ_warn × D</c> in an episode, forgotten again when it climbs back above <c>-θ_reset × D</c> (<see cref="RecoveryRules.PreWarning"/>), and the
    /// "company in default" letter on the day the default begins. The flags of the episode (<see cref="EpisodeData.preWarningSent"/>,
    /// <see cref="EpisodeData.defaultMailSent"/>) are in the save, so a letter is not sent twice after a load; the id of a letter is not kept anywhere.
    /// <list type="bullet">
    /// <item>The day check is cheap: a company far from the threshold costs a few reads and one comparison (NFR-1).</item>
    /// <item>The part <see cref="SubFeature.PreWarning"/> decides about sending the warning only; the flag is cleared when the balance recovers whatever the switch says.</item>
    /// <item>Only the player's company is looked at (FR-11): <see cref="IPlayerFinance"/> is the player's.</item>
    /// </list>
    /// </summary>
    internal sealed class RecoveryMonitorEngine
    {
        private readonly RecoveryEnvironment _env;
        private readonly IRecoveryLetters _letters;

        public RecoveryMonitorEngine(RecoveryEnvironment env, IRecoveryLetters letters)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (letters == null) throw new ArgumentNullException("letters");
            _env = env;
            _letters = letters;
        }

        /// <summary>
        /// The day (<c>TickPhase.LateTick</c>): sends the warning, or forgets that it was sent. Returns true when a letter was sent now.
        /// </summary>
        public bool OnDay()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable) return false;

            EpisodeData episode = _env.State.episode;
            bool warn = _env.IsOn(SubFeature.PreWarning);
            if (!warn && !episode.preWarningSent) return false;   // nothing to send, nothing to forget

            double money = finance.Money;
            double limit = finance.PlayerDebtLimit;
            var view = new BankruptcyView(money, limit, finance.IsInDefault, finance.IsBankrupt, 0, 0, finance.Today, true);

            PreWarningDecision decision = RecoveryRules.PreWarning(view, episode.preWarningSent, _env.Balance.preWarning);
            if (decision == PreWarningDecision.Reset)
            {
                episode.preWarningSent = false;
                return false;
            }
            if (decision != PreWarningDecision.Send || !warn) return false;

            if (!_letters.SendPreWarning(money, -limit)) return false;
            episode.preWarningSent = true;
            return true;
        }

        /// <summary>
        /// The player's default has just begun (patch P3): sends the letter once per episode. Returns true when a letter was sent now.
        /// </summary>
        public bool OnPlayerDefault()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || finance.IsBankrupt) return false;

            EpisodeData episode = _env.State.episode;
            if (episode.defaultMailSent) return false;

            int left = finance.DefaultTimeLimit - finance.DaysInDefault;
            if (left < 0) left = 0;

            if (!_letters.SendDefault(finance.Money, -finance.PlayerDebtLimit, left)) return false;
            episode.defaultMailSent = true;
            return true;
        }
    }
}
