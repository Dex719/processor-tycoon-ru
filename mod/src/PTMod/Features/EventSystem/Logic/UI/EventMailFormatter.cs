using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the letters and the lines of the engine say (design.md "UI", FR-7), built from the saved instance only: the numbers are the ones of the
    /// <see cref="AppliedEffect"/> that the choice will apply (NFR-5), the words come from the tables through <see cref="ITextSource"/>.
    /// <list type="bullet">
    /// <item>The letter of a choice for the player: title <c>[Событие] ...</c>, the text, the cause, what the event does at once, every choice with the exact effects
    /// and their chances (the roll itself stays hidden until the decision), buttons with the grey reasons. Priority <c>Critical</c> (FR-7 item 1).</item>
    /// <item>The letter of an event without a choice for the player (<c>Normal</c>) and the news about an event of a rival (<c>Low</c>, FR-7 item 8).</item>
    /// <item>The line under the letter: <c>Ответить до ... (N дн.)</c> at the delivery, then <c>Осталось N из M дн.</c>, and a warning line for every choice that puts
    /// the balance below zero (AC-4.2).</item>
    /// </list>
    /// The texts of a letter are written in the language of the moment it is sent (the arguments of a mail are ready strings).
    /// </summary>
    public static class EventMailFormatter
    {
        /// <summary>The tag of the letter of an instance: <c>es:&lt;instanceId&gt;</c>. The letters of the <c>SendMail</c> effect are tagged <c>es:fx:...</c> and are not instances.</summary>
        public const string TagPrefix = "es:";

        public const string TitleKey = "es.ui.mail.title";
        public const string NewsTitleKey = "es.ui.mail.news";
        public const string BodyKey = "es.ui.mail.body";

        public static string TagOf(int instanceId)
        {
            return TagPrefix + instanceId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The instance a tag names; false for any other tag (also <c>es:fx:...</c>).</summary>
        public static bool TryParseTag(string tag, out int instanceId)
        {
            instanceId = 0;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.Ordinal) || tag.Length == TagPrefix.Length) return false;
            for (int i = TagPrefix.Length; i < tag.Length; i++)
            {
                if (tag[i] < '0' || tag[i] > '9') return false;
            }
            return int.TryParse(tag.Substring(TagPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out instanceId) && instanceId > 0;
        }

        // ---- letters ----

        /// <summary>The letter of an event with choices for the player, <c>Critical</c>.</summary>
        public static EventMailPlan ChoiceMail(EventInstanceDto data, EventDefinition definition, IList<ChoiceState> states, ITextSource text)
        {
            var body = new StringBuilder();
            AppendHead(body, data, definition, text);
            AppendImmediate(body, data, text);

            if (body.Length > 0) body.Append("\n\n");
            body.Append(text.L("es.ui.mail.options"));
            foreach (ChoiceOptionDto option in data.options)
            {
                body.Append('\n').Append(text.L("es.ui.mail.option", ChoiceLabel(definition, option.choiceId, text)));
                foreach (AppliedEffect effect in option.effects) body.Append('\n').Append(text.L("es.ui.mail.effect", EffectLine(effect, text)));
            }

            EventMailPlan plan = Plan(data, definition, MailPriority.Critical, TitleKey, body.ToString(), text);
            foreach (ChoiceOptionDto option in data.options)
            {
                ChoiceState state = StateFor(states, option.choiceId);
                plan.Choices.Add(new MailChoicePlan
                {
                    Id = option.choiceId,
                    LabelKey = LabelKeyOf(definition, option.choiceId),
                    Enabled = state == null || state.Enabled,
                    ReasonKey = state != null && !state.Enabled ? state.ReasonKey : null,
                    ReasonArgs = state != null && !state.Enabled && state.ReasonArgs != null ? state.ReasonArgs : new string[0],
                });
            }
            return plan;
        }

        /// <summary>The letter of an event without a choice for the player: it informs, <c>Normal</c>.</summary>
        public static EventMailPlan InfoMail(EventInstanceDto data, EventDefinition definition, ITextSource text)
        {
            var body = new StringBuilder();
            AppendHead(body, data, definition, text);
            AppendImmediate(body, data, text);
            return Plan(data, definition, MailPriority.Normal, TitleKey, body.ToString(), text);
        }

        /// <summary>The news about an event of a rival, <c>Low</c>: who, what, what they decided (when there was a choice) and what it did.</summary>
        public static EventMailPlan NewsMail(EventInstanceDto data, EventDefinition definition, ITextSource text)
        {
            var body = new StringBuilder();
            body.Append(text.L("es.ui.mail.company", text.Company(data.companyKey)));
            AppendHead(body, data, definition, text);
            AppendImmediate(body, data, text);

            if (!string.IsNullOrEmpty(data.chosen))
            {
                body.Append('\n').Append(text.L("es.ui.mail.decision", ChoiceLabel(definition, data.chosen, text)));
                ChoiceOptionDto option = OptionOf(data, data.chosen);
                if (option != null)
                {
                    foreach (AppliedEffect effect in option.effects)
                    {
                        if (effect != null && effect.hit && !IsFailed(effect)) body.Append('\n').Append(text.L("es.ui.mail.effect", EffectLine(effect, text)));
                    }
                }
            }
            return Plan(data, definition, MailPriority.Low, NewsTitleKey, body.ToString(), text);
        }

        private static EventMailPlan Plan(EventInstanceDto data, EventDefinition definition, MailPriority priority, string titleKey, string body, ITextSource text)
        {
            return new EventMailPlan
            {
                Tag = TagOf(data.id),
                Priority = priority,
                TitleKey = titleKey,
                BodyKey = BodyKey,
                Args = new[] { TitleOf(data, definition, text), body },
            };
        }

        // The event's text and its cause. The first line starts the body, so nothing is appended before it for a letter that has no company line.
        private static void AppendHead(StringBuilder body, EventInstanceDto data, EventDefinition definition, ITextSource text)
        {
            string main = definition != null ? text.Resolve(definition.text, data.args) : string.Empty;
            if (!string.IsNullOrEmpty(main))
            {
                if (body.Length > 0) body.Append('\n');
                body.Append(main);
            }

            string cause = definition != null ? text.Resolve(definition.cause, data.args) : string.Empty;
            if (!string.IsNullOrEmpty(cause))
            {
                if (body.Length > 0) body.Append("\n\n");
                body.Append(text.L("es.ui.cause", cause));
            }
        }

        // What the event itself does, whatever is chosen: only what happened (a missed chance did not).
        private static void AppendImmediate(StringBuilder body, EventInstanceDto data, ITextSource text)
        {
            bool header = false;
            foreach (AppliedEffect effect in data.common)
            {
                if (effect == null || !effect.hit || IsFailed(effect)) continue;
                if (!header)
                {
                    if (body.Length > 0) body.Append("\n\n");
                    body.Append(text.L("es.ui.mail.immediate"));
                    header = true;
                }
                body.Append('\n').Append(text.L("es.ui.mail.effect", EffectLine(effect, text)));
            }
        }

        // ---- lines ----

        /// <summary>
        /// The line of an effect: the text built from its saved key and arguments, then its chance when it is below 100% (<c>(50% шанс)</c>) and the mark when its
        /// number was cut by the limits of a channel (<c>(ограничено)</c>, AC-5.5). The numbers are those of the <see cref="AppliedEffect"/>, in the same notation in
        /// every language.
        /// </summary>
        public static string EffectLine(AppliedEffect effect, ITextSource text)
        {
            if (effect == null) return string.Empty;
            string key = string.IsNullOrEmpty(effect.textKey) ? EffectResolver.FailedTextKey : effect.textKey;
            string line = text.L(key, WithChannelName(effect.channel, effect.textArgs, text));
            if (effect.chance < 0.9995f) line += " " + text.L("es.fx.chance", Percent(effect.chance));
            if (effect.clamped) line += " " + text.L("es.fx.clamped");
            return line;
        }

        /// <summary>
        /// The arguments of an effect's line with the name of its channel in the current language: the standard modifier effects save the channel id as the first
        /// argument (<c>research.speed</c>), the table <c>es.channel.&lt;id&gt;</c> names it (<c>Скорость исследований</c>). The saved arguments themselves stay as they are,
        /// so the same record reads right in every language and the numbers never change.
        /// </summary>
        public static string[] WithChannelName(string channel, string[] args, ITextSource text)
        {
            string[] source = args ?? new string[0];
            if (string.IsNullOrEmpty(channel) || source.Length == 0 || !string.Equals(source[0], channel, StringComparison.Ordinal)) return source;

            string name = text.Find("es.channel." + channel);
            if (string.IsNullOrEmpty(name)) return source;
            var named = (string[])source.Clone();
            named[0] = name;
            return named;
        }

        /// <summary>A share 0..1 as a whole percentage: <c>50%</c>.</summary>
        public static string Percent(float share)
        {
            return Math.Round(share * 100.0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>The effects of a choice, one line each (the grey tooltip of the button in the window).</summary>
        public static string EffectLines(ChoiceOptionDto option, ITextSource text)
        {
            if (option == null || option.effects == null || option.effects.Count == 0) return string.Empty;
            var lines = new List<string>();
            foreach (AppliedEffect effect in option.effects) lines.Add(EffectLine(effect, text));
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>The name of the event in the current language; its id for an event that is no longer there (a <c>Direct</c> source).</summary>
        public static string TitleOf(EventInstanceDto data, EventDefinition definition, ITextSource text)
        {
            string title = definition != null ? text.Resolve(definition.title, data != null ? data.args : null) : string.Empty;
            if (!string.IsNullOrEmpty(title)) return title;
            return data != null && !string.IsNullOrEmpty(data.eventId) ? data.eventId : string.Empty;
        }

        /// <summary>The caption of a choice in the current language; the id of the choice when the event has no such text.</summary>
        public static string ChoiceLabel(EventDefinition definition, string choiceId, ITextSource text)
        {
            string key = LabelKeyOf(definition, choiceId);
            return string.IsNullOrEmpty(key) ? choiceId : text.L(key);
        }

        // ---- the status line ----

        /// <summary>At the delivery: <c>Ответить до 15.06.1985 (14 дн.)</c>.</summary>
        public static string StatusDeadline(int deadlineDay, int today, ITextSource text)
        {
            return text.L("es.ui.deadline", text.Date(deadlineDay), Days(deadlineDay - today));
        }

        /// <summary>From then on: <c>Осталось 9 из 14 дн.</c></summary>
        public static string StatusDaysLeft(int daysLeft, int timerDays, ITextSource text)
        {
            return text.L("es.ui.days_left", Days(daysLeft), Days(timerDays));
        }

        /// <summary>One line for every choice that would put the balance below zero: <c>«Нанять»: уйдёте в минус на $350K</c> (AC-4.2); empty when there is none.</summary>
        public static string DebtLines(IList<ChoiceState> states, EventDefinition definition, ITextSource text)
        {
            var lines = new List<string>();
            if (states != null)
            {
                foreach (ChoiceState state in states)
                {
                    if (state == null || state.InTheRed <= 0f) continue;
                    lines.Add(text.L("es.ui.status.debt", ChoiceLabel(definition, state.ChoiceId, text), DebtWarning(state, text)));
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary><c>уйдёте в минус на $350K</c> for a choice that is open and goes into the red; empty for any other.</summary>
        public static string DebtWarning(ChoiceState state, ITextSource text)
        {
            return state != null && state.InTheRed > 0f ? text.L("es.ui.debt_warning", EffectFormat.Money(state.InTheRed)) : string.Empty;
        }

        /// <summary>The status line: the timer, then the warnings.</summary>
        public static string Status(string timer, string debtLines)
        {
            return string.IsNullOrEmpty(debtLines) ? timer : timer + "\n" + debtLines;
        }

        // ---- helpers ----

        private static string Days(int days)
        {
            return Math.Max(0, days).ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsFailed(AppliedEffect effect)
        {
            return string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal);
        }

        private static ChoiceState StateFor(IList<ChoiceState> states, string choiceId)
        {
            if (states == null) return null;
            foreach (ChoiceState state in states)
            {
                if (state != null && string.Equals(state.ChoiceId, choiceId, StringComparison.Ordinal)) return state;
            }
            return null;
        }

        private static ChoiceOptionDto OptionOf(EventInstanceDto data, string choiceId)
        {
            foreach (ChoiceOptionDto option in data.options)
            {
                if (option != null && string.Equals(option.choiceId, choiceId, StringComparison.Ordinal)) return option;
            }
            return null;
        }

        private static string LabelKeyOf(EventDefinition definition, string choiceId)
        {
            if (definition != null && definition.choices != null)
            {
                foreach (EventChoice choice in definition.choices)
                {
                    if (choice != null && string.Equals(choice.id, choiceId, StringComparison.Ordinal) && !string.IsNullOrEmpty(choice.textKey)) return choice.textKey;
                }
            }
            return null;
        }
    }
}
