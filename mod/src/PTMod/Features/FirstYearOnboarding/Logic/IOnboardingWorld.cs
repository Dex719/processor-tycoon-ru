using System;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// What the state machine of the tutorial asks of the game (design.md, "OnboardingMachine"): the blocking states, the date, the track of the
    /// player's company, the conditions of the steps and the delivery of a mail. The game layer implements it (<c>GameOnboardingWorld</c>, over
    /// <c>ModTicks</c>, <c>ModConditions</c> and <c>ModMail</c>); the tests of the machine use a fake world. Main thread only.
    /// </summary>
    public interface IOnboardingWorld
    {
        /// <summary>
        /// Nothing may be checked or sent now: a save is loading, the new game fast-forwards, or the game state is not ready
        /// (<c>ModTicks.IsLoading || IsFastForwarding || !IsGameReady</c>).
        /// </summary>
        bool IsBlocked { get; }

        /// <summary>The month batch run (<c>DateController.NextMonth</c>) is in progress (<c>ModTicks.IsBatchRun</c>): no mail goes out until it ends.</summary>
        bool IsBatchRun { get; }

        /// <summary>The current game date (<c>ModTicks.Today</c>); <c>default(DateTime)</c> outside the game scene.</summary>
        DateTime GameDate { get; }

        /// <summary>
        /// The track of the player's company: <c>idm</c>, <c>fabless</c> or <c>foundry</c>; null when the company cannot be read now (the caller
        /// keeps the track it had).
        /// </summary>
        string DetectTrack();

        /// <summary>
        /// Evaluates a condition of a step against the game now. A null reference, a condition that cannot be parsed and a condition that
        /// throws are false: what a missing condition means (a step that shows at once, an information step) is the caller's decision.
        /// </summary>
        bool Evaluate(ConditionRef condition);

        /// <summary>The mail with this id reached the inbox (<c>ModMail.Get(id).IsDelivered</c>); false for an unknown id and for a mail still in the queue.</summary>
        bool IsMailDelivered(int mailId);
    }
}
