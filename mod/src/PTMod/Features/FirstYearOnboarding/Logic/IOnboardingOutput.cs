using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// What the state machine of the tutorial asks the game to show (design.md, "OnboardingMachine"): mail, hints, the notice at the start, the
    /// end of a run. The game layer implements it (<c>OnboardingMailer</c>, over <c>ModMail</c> and <c>ModNotify</c>); the tests record the calls.
    /// Main thread only. The machine does not catch exceptions of these methods (the director guards the entry points), but it changes its
    /// state only after a call returned, so a call that threw leaves nothing half done.
    /// </summary>
    public interface IOnboardingOutput
    {
        /// <summary>
        /// Sends the mail of a step or of a tip and returns its id (<c>ModMail.Send</c>); 0 or less when the mail could not be sent. The mail has the
        /// tag <c>onb:&lt;stepId&gt;</c>, or <c>onb:&lt;stepId&gt;:repeat</c> when <paramref name="isRepeat"/> is true.
        /// </summary>
        /// <param name="step">The step.</param>
        /// <param name="track">The track of the run, for the variant of the text.</param>
        /// <param name="caughtUp">Ids of steps counted as done without a mail, for the line "Already done: ..."; empty for none. Never null; the list is the callee's to keep.</param>
        /// <param name="isRepeat">The player asked for the mail again.</param>
        int SendStepMail(StepDefinition step, string track, IList<string> caughtUp, bool isRepeat);

        /// <summary>
        /// Sends the closing mail with the list of the steps and how each ended (the mail of the <c>graduation</c> step) and returns its id; 0 or
        /// less when it could not be sent.
        /// </summary>
        int SendSummaryMail(OnboardingState state, StepCatalog catalog);

        /// <summary>Shows the reminder hint number <paramref name="hintIndex"/> (0-based, as in <see cref="StepDefinition.Reminders"/>) of the step as a popup.</summary>
        void ShowHint(StepDefinition step, int hintIndex);

        /// <summary>Switches the "Hint" button of the mail with this id on or off (off when the hints of the step are used up).</summary>
        void SetHintAvailable(int mailId, bool available);

        /// <summary>Tells the player that the first mail comes as soon as time runs (the tutorial just started).</summary>
        void NotifyStarted();

        /// <summary>The run ended: completed, skipped or aborted. Called once per run; the game layer records that the profile has seen the tutorial and raises the event of the API.</summary>
        void RaiseCompleted(OnboardingResult result);
    }
}
