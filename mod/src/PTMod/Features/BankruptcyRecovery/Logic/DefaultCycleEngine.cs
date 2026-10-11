using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>
    /// The two moments of the game's bankruptcy cycle the feature takes part in (design.md, patches P1 and P2 on <c>BankruptcyHandler.TickBankruptcies</c>),
    /// for the player's company only (FR-11):
    /// <list type="bullet">
    /// <item><see cref="BeforeTick"/> (P1, prefix): on the last day of the default, when the bankruptcy is due and the player may still be rescued
    /// (<see cref="RecoveryRules.MustHoldForBailout"/>, the part on, the offers of the difficulty not used up, no refusal), the counter of the default
    /// is put two days back, so that the game's own increment leaves it one day short of the limit, and the offer is requested from
    /// <see cref="BailoutEngine"/>. The counter is held only when the offer is (or will be) shown.</item>
    /// <item><see cref="AfterTick"/> (P2, postfix): the early exit (FR-9): <c>streakDays</c> days in a row at or above the threshold cancel the
    /// default; and when the player is not in default any more, the flags of the episode go back to their start.</item>
    /// </list>
    /// The decision is made on the live numbers of the game (the threshold of <c>ModCredit</c>), never on a copy.
    /// </summary>
    internal sealed class DefaultCycleEngine
    {
        private readonly RecoveryEnvironment _env;
        private readonly BailoutEngine _bailout;

        public DefaultCycleEngine(RecoveryEnvironment env, BailoutEngine bailout)
        {
            if (env == null) throw new ArgumentNullException("env");
            if (bailout == null) throw new ArgumentNullException("bailout");
            _env = env;
            _bailout = bailout;
        }

        /// <summary>
        /// P1, before the game's <c>TickBankruptcies</c>. Returns true when the counter of the default was held this time (it is put to
        /// <c>defaultTimeLimit − 2</c>); the caller always lets the game's method run.
        /// </summary>
        public bool BeforeTick()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable || !finance.IsInDefault) return false;   // the cheap exit for the many days without a default (NFR-1)
            if (!_env.IsOn(SubFeature.Bailout)) return false;

            BankruptcyView view = finance.View();

            int allowed = BailoutMath.Allowed(_env.Balance.bailout, finance.Difficulty);
            RecoveryState state = _env.State;
            if (!RecoveryRules.MustHoldForBailout(view, state.bailoutsUsed, allowed, state.episode.bailoutDeclined)) return false;

            if (!_bailout.TryHold()) return false;

            finance.DaysInDefault = view.DefaultTimeLimit - 2;
            return true;
        }

        /// <summary>
        /// P2, after the game's <c>TickBankruptcies</c>. Returns true when the default was cancelled by the early exit now.
        /// </summary>
        public bool AfterTick()
        {
            IPlayerFinance finance = _env.Finance;
            if (!finance.IsAvailable) return false;

            EpisodeData episode = _env.State.episode;
            if (!finance.IsInDefault)
            {
                EndEpisode(episode);
                return false;
            }

            if (!_env.IsOn(SubFeature.EarlyExit))
            {
                episode.recoveryStreak = 0;
                return false;
            }

            BankruptcyView view = finance.View();
            episode.recoveryStreak = RecoveryRules.NextRecoveryStreak(view, episode.recoveryStreak);
            if (!RecoveryRules.IsStreakComplete(episode.recoveryStreak, _env.Balance.earlyExit)) return false;

            episode.recoveryStreak = 0;
            finance.CancelDefault();
            return true;
        }

        // The default is over (cancelled by the game, by the early exit or by the rescue): what belonged to it is forgotten.
        private void EndEpisode(EpisodeData episode)
        {
            if (episode.defaultMailSent) episode.defaultMailSent = false;
            if (episode.bailoutDeclined) episode.bailoutDeclined = false;
            if (episode.recoveryStreak != 0) episode.recoveryStreak = 0;
            _bailout.EpisodeEnded();
        }
    }
}
