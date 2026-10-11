using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The texts of the string tables as the planner needs them: whether a key exists (in the current language chain) and the text with <c>{0}</c>,
    /// <c>{1}</c>... filled. The game layer wraps <c>ModL10n</c> (<c>Has</c>, <c>L</c>); the tests use the shipped tables.
    /// </summary>
    public interface IMailTexts
    {
        /// <summary>The key has a text.</summary>
        bool Has(string key);

        /// <summary>The text of the key with the arguments filled in. Must not throw; a missing key gives some visible marker.</summary>
        string Text(string key, params object[] args);
    }

    /// <summary>One button of a planned mail (a <c>ModMailAction</c> of the core).</summary>
    public sealed class PlannedAction
    {
        /// <summary>The id the handler of the buttons gets (<see cref="OnboardingMachine.ActionSkipAll"/> and the others).</summary>
        public string Id;

        /// <summary>String key of the caption.</summary>
        public string LabelKey;

        /// <summary>String key of the hover text; null for none.</summary>
        public string TooltipKey;

        /// <summary>False: the button works once and is gray afterwards (the skip buttons).</summary>
        public bool Repeatable;

        /// <summary>False: the button is gray from the start, with <see cref="DisabledReasonKey"/> as the reason.</summary>
        public bool Enabled = true;

        /// <summary>String key of the reason a button is gray; null for none.</summary>
        public string DisabledReasonKey;
    }

    /// <summary>
    /// A mail of the tutorial ready to be handed to <c>ModMail.Send</c> (design.md, "OnboardingMailer"): every choice that does not need the game is
    /// made - the tag, the keys of the title and the body (track and condition variants), the filled arguments, the priority, the buttons and the extra
    /// line under the text. Plain fields.
    /// </summary>
    public sealed class PlannedMail
    {
        /// <summary>The step (or tip) the mail belongs to.</summary>
        public string StepId;

        /// <summary><c>onb:&lt;stepId&gt;</c>, or <c>onb:&lt;stepId&gt;:repeat</c>.</summary>
        public string Tag;

        /// <summary>The player asked for this mail again.</summary>
        public bool IsRepeat;

        /// <summary>String key of the title; for a repeat null (see <see cref="InlineTitle"/>).</summary>
        public string TitleKey;

        /// <summary>String key of the body, already chosen among the variants.</summary>
        public string BodyKey;

        /// <summary>The ready title of a repeated mail (the title with the word "(repeat)" added); null for any other mail.</summary>
        public string InlineTitle;

        /// <summary>The values of <c>{0}</c>, <c>{1}</c>... of the texts, in the order of the <c>args</c> of the step; never null.</summary>
        public string[] Args = new string[0];

        public MailPriority Priority = MailPriority.Normal;

        /// <summary>The buttons, in the order of the step; never null.</summary>
        public List<PlannedAction> Actions = new List<PlannedAction>();

        /// <summary>The line under the text: "Already done: ..." or the summary of the run; null for none.</summary>
        public string StatusLine;
    }

    /// <summary>
    /// Builds the mails of the tutorial from the steps (design.md, "OnboardingMailer and PlaceholderResolver"; FR-5, FR-8 item 1, FR-13 item 2) without the
    /// game: which body is told (a variant chosen by a condition, then the variant of the track, then the main text), the values of the placeholders, the
    /// buttons, the line "Already done: ..." of a mail that follows steps the player did on his own, the closing summary. The game layer sends the
    /// result. Pure logic; never throws (a source that fails gives a "?" for its placeholder).
    /// </summary>
    public static class MailPlanner
    {
        /// <summary>String key of the word that marks a repeated mail ("(repeat)").</summary>
        public const string RepeatSuffixKey = "onb.repeat.suffix";

        /// <summary>String key of the lead-in of the "Already done" line.</summary>
        public const string DonePrefixKey = "onb.done.prefix";

        /// <summary>String key of the heading of the summary of the run.</summary>
        public const string SummaryHeaderKey = "onb.summary.header";

        /// <summary>String key of one line of the summary: <c>{0}</c> the name of the step, <c>{1}</c> how it ended.</summary>
        public const string SummaryLineKey = "onb.summary.line";

        /// <summary>The title of the summary mail when the content has no closing step.</summary>
        public const string DefaultSummaryTitleKey = "onb.graduation.title";

        /// <summary>The body of the summary mail when the content has no closing step.</summary>
        public const string DefaultSummaryBodyKey = "onb.graduation.body";

        // ---- a step ----

        /// <summary>The mail of a step or a tip.</summary>
        /// <param name="step">The step.</param>
        /// <param name="track">The track of the run; picks the variant <c>&lt;bodyKey&gt;.&lt;track&gt;</c> of the body when the tables have it.</param>
        /// <param name="caughtUp">Ids of steps counted as done without a mail ("Already done"); null or empty for none. Not changed.</param>
        /// <param name="isRepeat">The player asked for the mail again: the tag gets <c>:repeat</c>, the title the word "(repeat)", and there is no "Already done" line.</param>
        /// <param name="texts">The string tables.</param>
        /// <param name="source">The values of the placeholders.</param>
        /// <param name="evaluate">Evaluates the condition of a body variant now; null makes every variant false.</param>
        /// <param name="warn">Receives English lines for the log (an unknown placeholder); may be null.</param>
        public static PlannedMail PlanStep(StepDefinition step, string track, IList<string> caughtUp, bool isRepeat,
                                           IMailTexts texts, IPlaceholderSource source, Func<ConditionRef, bool> evaluate, Action<string> warn = null)
        {
            if (step == null) throw new ArgumentNullException("step");
            if (texts == null) throw new ArgumentNullException("texts");

            MailSpec spec = step.Mail ?? new MailSpec();
            var mail = new PlannedMail
            {
                StepId = step.Id,
                Tag = MailTags.For(step.Id, isRepeat),
                IsRepeat = isRepeat,
                TitleKey = spec.TitleKey,
                BodyKey = ChooseBodyKey(spec, track, texts, evaluate),
                Args = PlaceholderResolver.ResolveArgs(spec.Args, source, warn),
                Priority = ParsePriority(step.Priority)
            };

            if (isRepeat)
            {
                // The core draws a title from a key and cannot add a word to it, so the repeat carries its title as a ready text.
                mail.InlineTitle = TitleOfRepeat(spec.TitleKey, mail.Args, texts);
                mail.TitleKey = null;
            }

            mail.Actions = ActionsOf(step);
            if (!isRepeat) mail.StatusLine = DoneLine(caughtUp, texts);
            return mail;
        }

        /// <summary>
        /// The key of the body to tell: the first variant whose condition is true (a variant replaces the body, for example when the recovery feature is
        /// on), else the variant of the track (<c>&lt;bodyKey&gt;.&lt;track&gt;</c>) when the tables have it, else the main body.
        /// </summary>
        public static string ChooseBodyKey(MailSpec spec, string track, IMailTexts texts, Func<ConditionRef, bool> evaluate)
        {
            if (spec == null) return null;

            BodyVariant[] variants = spec.Variants;
            if (variants != null && evaluate != null)
            {
                for (int i = 0; i < variants.Length; i++)
                {
                    BodyVariant variant = variants[i];
                    if (variant == null || variant.When == null || string.IsNullOrEmpty(variant.BodyKey)) continue;
                    if (!texts.Has(variant.BodyKey)) continue;
                    if (SafeEvaluate(evaluate, variant.When)) return variant.BodyKey;
                }
            }

            if (!string.IsNullOrEmpty(track) && !string.IsNullOrEmpty(spec.BodyKey))
            {
                string byTrack = spec.BodyKey + "." + track;
                if (texts.Has(byTrack)) return byTrack;
            }

            return spec.BodyKey;
        }

        /// <summary>The priority named in a step (<c>critical</c>, <c>normal</c>, <c>low</c>); anything else is <see cref="MailPriority.Normal"/>.</summary>
        public static MailPriority ParsePriority(string name)
        {
            if (string.Equals(name, "critical", StringComparison.OrdinalIgnoreCase)) return MailPriority.Critical;
            if (string.Equals(name, "low", StringComparison.OrdinalIgnoreCase)) return MailPriority.Low;
            return MailPriority.Normal;
        }

        // ---- buttons ----

        /// <summary>The buttons of a step, in the order of <see cref="MailSpec.Buttons"/>; an id the planner does not know is left out.</summary>
        public static List<PlannedAction> ActionsOf(StepDefinition step)
        {
            var actions = new List<PlannedAction>();
            if (step == null || step.Mail == null || step.Mail.Buttons == null) return actions;

            for (int i = 0; i < step.Mail.Buttons.Length; i++)
            {
                PlannedAction action = ActionFor(step.Mail.Buttons[i], step);
                if (action != null) actions.Add(action);
            }
            return actions;
        }

        /// <summary>
        /// One button by its id. "Skip step" and "Skip the tutorial" work once (<c>Repeatable = false</c>); the others as often as the player presses them.
        /// "Hint" is gray from the start in a step that has no hints, with the reason "no more hints".
        /// </summary>
        public static PlannedAction ActionFor(string buttonId, StepDefinition step)
        {
            switch (buttonId)
            {
                case OnboardingMachine.ActionSkipAll:
                    return new PlannedAction { Id = buttonId, LabelKey = "onb.button.skipAll", TooltipKey = "onb.button.skipAll.tip", Repeatable = false };
                case OnboardingMachine.ActionSkipStep:
                    return new PlannedAction { Id = buttonId, LabelKey = "onb.button.skipStep", TooltipKey = "onb.button.skipStep.tip", Repeatable = false };
                case OnboardingMachine.ActionHint:
                    {
                        bool hasHints = step != null && step.Reminders != null && step.Reminders.Length > 0;
                        return new PlannedAction
                        {
                            Id = buttonId,
                            LabelKey = "onb.button.hint",
                            TooltipKey = "onb.button.hint.tip",
                            Repeatable = true,
                            Enabled = hasHints,
                            DisabledReasonKey = hasHints ? null : "onb.hint.none"
                        };
                    }
                case OnboardingMachine.ActionOpenCpuDesigner:
                    return new PlannedAction { Id = buttonId, LabelKey = "onb.button.openCpu", TooltipKey = "onb.button.openCpu.tip", Repeatable = true };
                case OnboardingMachine.ActionOpenFactoryExpansion:
                    return new PlannedAction { Id = buttonId, LabelKey = "onb.button.openFactory", TooltipKey = "onb.button.openFactory.tip", Repeatable = true };
                default:
                    return null;
            }
        }

        // ---- the lines under the text ----

        /// <summary>
        /// "Already done: the research is started, the CPU project is started" for the steps the player did before their mail came; null when there is
        /// none (or none has a phrase in the tables).
        /// </summary>
        public static string DoneLine(IList<string> caughtUp, IMailTexts texts)
        {
            if (caughtUp == null || caughtUp.Count == 0) return null;

            var phrases = new List<string>();
            for (int i = 0; i < caughtUp.Count; i++)
            {
                string id = caughtUp[i];
                if (string.IsNullOrEmpty(id)) continue;

                string key = StepDefinition.KeyPrefix + id + ".done";
                if (texts.Has(key)) phrases.Add(texts.Text(key));
            }

            if (phrases.Count == 0) return null;
            return texts.Text(DonePrefixKey) + " " + string.Join(", ", phrases.ToArray());
        }

        // ---- the summary ----

        /// <summary>
        /// The closing mail of a run: the title and the body of the closing step (or the default keys), its priority, and the list of the steps with
        /// how each ended under the text. No buttons.
        /// </summary>
        public static PlannedMail PlanSummary(OnboardingState state, StepCatalog catalog, IMailTexts texts, IPlaceholderSource source, Action<string> warn = null)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (texts == null) throw new ArgumentNullException("texts");

            StepDefinition closing = catalog == null ? null : catalog.Find(OnboardingMachine.GraduationStepId);
            MailSpec spec = closing == null ? null : closing.Mail;

            return new PlannedMail
            {
                StepId = OnboardingMachine.GraduationStepId,
                Tag = MailTags.For(OnboardingMachine.GraduationStepId, false),
                TitleKey = spec != null && !string.IsNullOrEmpty(spec.TitleKey) ? spec.TitleKey : DefaultSummaryTitleKey,
                BodyKey = spec != null && !string.IsNullOrEmpty(spec.BodyKey) ? spec.BodyKey : DefaultSummaryBodyKey,
                Args = PlaceholderResolver.ResolveArgs(spec == null ? null : spec.Args, source, warn),
                Priority = closing == null ? MailPriority.Normal : ParsePriority(closing.Priority),
                StatusLine = SummaryText(state, catalog, texts)
            };
        }

        /// <summary>
        /// The heading and one line per step of the track ("Research: done"); the closing step itself is not listed. Null when the track has no steps.
        /// </summary>
        public static string SummaryText(OnboardingState state, StepCatalog catalog, IMailTexts texts)
        {
            if (state == null || catalog == null || state.Track == null) return null;

            IList<StepDefinition> sequence = catalog.StepsFor(state.Track, StepKind.Sequential);
            var lines = new List<string>();
            for (int i = 0; i < sequence.Count; i++)
            {
                StepDefinition def = sequence[i];
                if (string.Equals(def.Id, OnboardingMachine.GraduationStepId, StringComparison.Ordinal)) continue;

                string name = texts.Has(def.NameKey) ? texts.Text(def.NameKey) : def.Id;
                lines.Add(texts.Text(SummaryLineKey, name, texts.Text(SummaryStatusKey(state.StatusOf(def.Id)))));
            }

            if (lines.Count == 0) return null;
            return texts.Text(SummaryHeaderKey) + "\n" + string.Join("\n", lines.ToArray());
        }

        /// <summary>The string key of the word for how a step ended.</summary>
        public static string SummaryStatusKey(StepStatus status)
        {
            switch (status)
            {
                case StepStatus.Done: return "onb.summary.done";
                case StepStatus.Skipped: return "onb.summary.skipped";
                case StepStatus.NotApplicable: return "onb.summary.na";
                default: return "onb.summary.open";
            }
        }

        // ---- helpers ----

        private static string TitleOfRepeat(string titleKey, string[] args, IMailTexts texts)
        {
            string title = string.IsNullOrEmpty(titleKey)
                ? string.Empty
                : (args.Length > 0 ? texts.Text(titleKey, (object[])args) : texts.Text(titleKey));
            string suffix = texts.Text(RepeatSuffixKey);
            return title.Length == 0 ? suffix : title + " " + suffix;
        }

        private static bool SafeEvaluate(Func<ConditionRef, bool> evaluate, ConditionRef condition)
        {
            try
            {
                return evaluate(condition);
            }
            catch (Exception)
            {
                return false;   // a condition that throws is false (the world logs it)
            }
        }
    }
}
