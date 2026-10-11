using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The string tables of the core as <see cref="IMailTexts"/>: <c>ModL10n.Has</c> and <c>ModL10n.L</c>, so the planner reads the texts in the language of
    /// the player. Never throws (a text that fails to fill is the key itself in brackets, which is what <c>ModL10n</c> shows for a missing key).
    /// </summary>
    internal sealed class GameMailTexts : IMailTexts
    {
        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && ModL10n.Has(key);
        }

        public string Text(string key, params object[] args)
        {
            return args == null || args.Length == 0 ? ModL10n.L(key) : ModL10n.L(key, args);
        }
    }

    /// <summary>
    /// The game side of <see cref="IOnboardingOutput"/> (TSK-012; FR-5, FR-6, FR-8, FR-12; design.md, "OnboardingMailer and PlaceholderResolver"): what the
    /// machine of the tutorial asks the game to show. The choices are <see cref="MailPlanner"/>'s (the pure part, with the tests); this class hands the
    /// result to the services of the core.
    /// <list type="bullet">
    /// <item><b>Mail.</b> <c>ModMail.Send</c> with the feature id, the tag <c>onb:&lt;stepId&gt;</c> (<c>:repeat</c> for a copy), the keys of the title and the
    /// body, the arguments filled at this moment, the priority of the step and the buttons (the actions of the core: "Skip step" and "Skip the tutorial" work
    /// once, the rest as often as pressed). The id the core gives goes back to the machine, which keeps it in the state of the step. A mail the core
    /// refuses gives 0 and the machine tries again the next game day.</item>
    /// <item><b>Under the text.</b> The line "Already done: ..." of a mail that follows steps the player did on his own, and the list of the steps in the
    /// closing mail, are the status line of the mail (<c>ModMail.SetStatusLine</c>): the core shows it under the text and keeps it with the mail.</item>
    /// <item><b>Hints and the notice at the start</b> go through <c>ModNotify</c>, which the core holds back during a load and a fast-forward and limits per day.</item>
    /// <item><b>The end of a run</b> is <see cref="OnboardingApiBridge.PublishCompleted"/>: the profile remembers that the player has seen the tutorial and
    /// the subscribers of <c>OnboardingService.Completed</c> hear of it.</item>
    /// </list>
    /// Main thread only. The methods do not catch exceptions; the director guards every entry, and the machine changes its state only after a call returned.
    /// </summary>
    internal sealed class OnboardingMailer : IOnboardingOutput
    {
        private readonly IMailTexts _texts;
        private readonly IPlaceholderSource _source;
        private readonly Func<ConditionRef, bool> _evaluate;
        private readonly Func<StepCatalog> _catalog;

        /// <param name="texts">The string tables.</param>
        /// <param name="source">The values of the placeholders.</param>
        /// <param name="evaluate">Evaluates the condition of a body variant (the world of the machine).</param>
        /// <param name="catalog">Gives the steps (for the summary); asked at each use.</param>
        public OnboardingMailer(IMailTexts texts, IPlaceholderSource source, Func<ConditionRef, bool> evaluate, Func<StepCatalog> catalog)
        {
            if (texts == null) throw new ArgumentNullException("texts");
            if (source == null) throw new ArgumentNullException("source");

            _texts = texts;
            _source = source;
            _evaluate = evaluate;
            _catalog = catalog;
        }

        public int SendStepMail(StepDefinition step, string track, IList<string> caughtUp, bool isRepeat)
        {
            PlannedMail plan = MailPlanner.PlanStep(step, track, caughtUp, isRepeat, _texts, _source, _evaluate, Warn);
            return Send(plan);
        }

        public int SendSummaryMail(OnboardingState state, StepCatalog catalog)
        {
            PlannedMail plan = MailPlanner.PlanSummary(state, catalog ?? (_catalog == null ? null : _catalog()), _texts, _source, Warn);
            return Send(plan);
        }

        public void ShowHint(StepDefinition step, int hintIndex)
        {
            if (step == null || step.Reminders == null || hintIndex < 0 || hintIndex >= step.Reminders.Length) return;

            ReminderSpec reminder = step.Reminders[hintIndex];
            if (reminder == null || string.IsNullOrEmpty(reminder.HintKey)) return;

            string[] args = PlaceholderResolver.ResolveArgs(reminder.Args, _source, Warn);
            string text = args.Length > 0 ? ModL10n.L(reminder.HintKey, (object[])args) : ModL10n.L(reminder.HintKey);
            ModNotify.Show(OnboardingFeature.FeatureId, ModL10n.L("onb.notify.title"), text);
        }

        public void SetHintAvailable(int mailId, bool available)
        {
            if (mailId <= 0) return;
            ModMail.SetActionState(mailId, OnboardingMachine.ActionHint, available, available ? null : "onb.hint.none");
        }

        public void NotifyStarted()
        {
            ModNotify.ShowKey(OnboardingFeature.FeatureId, "onb.start.notify");
        }

        public void RaiseCompleted(Logic.OnboardingResult result)
        {
            OnboardingApiBridge.PublishCompleted(result);
        }

        // ---- helpers ----

        private static int Send(PlannedMail plan)
        {
            var message = new ModMailMessage
            {
                FeatureId = OnboardingFeature.FeatureId,
                Tag = plan.Tag,
                BodyKey = plan.BodyKey,
                Args = plan.Args.Length > 0 ? plan.Args : null,
                Priority = plan.Priority
            };

            if (plan.InlineTitle != null) message.Title = LocText.Of(plan.InlineTitle, plan.InlineTitle);
            else message.TitleKey = plan.TitleKey;

            if (plan.Actions.Count > 0)
            {
                message.Actions = new List<ModMailAction>(plan.Actions.Count);
                foreach (PlannedAction action in plan.Actions)
                {
                    message.Actions.Add(new ModMailAction
                    {
                        Id = action.Id,
                        LabelKey = action.LabelKey,
                        TooltipKey = action.TooltipKey,
                        Repeatable = action.Repeatable,
                        Enabled = action.Enabled,
                        DisabledReasonKey = action.DisabledReasonKey
                    });
                }
            }

            int id = ModMail.Send(message);
            if (id < ModMail.FirstModMailId)
            {
                ModLog.Warn(OnboardingFeature.FeatureId, "mail '" + plan.Tag + "' was not accepted by the core; the tutorial tries again on the next game day");
                return 0;
            }

            if (!string.IsNullOrEmpty(plan.StatusLine)) ModMail.SetStatusLine(id, plan.StatusLine);
            return id;
        }

        private static void Warn(string message)
        {
            ModLog.Warn(OnboardingFeature.FeatureId, "mail: " + message);
        }
    }
}
