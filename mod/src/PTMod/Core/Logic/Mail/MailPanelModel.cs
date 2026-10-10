using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Core.Logic.Mail
{
    /// <summary>What a row of the panel is.</summary>
    internal enum MailPanelKind
    {
        /// <summary>A line under the text of the letter: the deadline of the core, the line of the feature.</summary>
        Status = 0,

        /// <summary>The line above the buttons that says the feature is not running.</summary>
        Note,

        /// <summary>A button of an action.</summary>
        Action,

        /// <summary>A button of a choice (the mail is unanswered).</summary>
        Choice,

        /// <summary>The reason a button above is disabled, under that button.</summary>
        Reason,

        /// <summary>"Ваш ответ: ..." in place of the choices.</summary>
        Answer
    }

    /// <summary>One row of the panel: a line of text or a button with its state and hint.</summary>
    internal sealed class MailPanelItem
    {
        public MailPanelKind Kind;

        /// <summary>The id of the choice or action; null for a line.</summary>
        public string Id;

        /// <summary>The text of the line or the caption of the button.</summary>
        public string Text = string.Empty;

        /// <summary>A button can be pressed (always true for a line).</summary>
        public bool Enabled = true;

        /// <summary>The hint of a button: its own tooltip text and, when it is gray, the reason; empty for none.</summary>
        public string Tip = string.Empty;
    }

    /// <summary>The words of the panel in the current language; the runtime gives the mod's tables, a test gives its own.</summary>
    internal interface IMailPanelTexts
    {
        /// <summary>A button caption or the name of the chosen answer: the table key, else the inline text.</summary>
        string Label(string key, LocText inline);

        /// <summary>The text of a <c>TooltipKey</c>.</summary>
        string Tip(string key);

        /// <summary>The text of a <c>DisabledReasonKey</c> filled with its arguments.</summary>
        string Reason(string key, string[] args);

        /// <summary>"Осталось N дней" with the right form of the number.</summary>
        string DaysLeft(int days);

        /// <summary>"Ваш ответ: X", or "Ответ по истечении срока: X" when the deadline answered.</summary>
        string Answer(string label, bool afterDeadline);

        /// <summary>"Фича «X» выключена — ответить нельзя".</summary>
        string FeatureOff(string featureId);
    }

    /// <summary>
    /// Makes the rows of the panel from a stored mail (design.md "Панель кнопок в письме"): status lines, then the feature-off line, the actions, then the
    /// choices - or, when the mail is answered, the answer in place of the choices. A button is active only if the feature is running and the record says so
    /// (<see cref="MailAnswerRules"/>); a gray button carries its reason in the hint and, if it is the button's own reason, in a line under it.
    /// </summary>
    internal static class MailPanelModel
    {
        public static List<MailPanelItem> Build(MailRecordModel mail, DateTime today, bool featureActive, IMailPanelTexts texts)
        {
            var items = new List<MailPanelItem>();
            if (mail == null || texts == null) return items;

            bool pending = mail.IsPending;

            // 1. The status lines: the period of the core while the mail waits for an answer, then the feature's own line.
            if (pending && mail.TimeoutDays > 0 && mail.Deadline.HasValue)
                items.Add(Line(MailPanelKind.Status, texts.DaysLeft(MailTimeoutRules.DaysLeft(mail.Deadline.Value, today))));
            if (!string.IsNullOrEmpty(mail.StatusLine))
                items.Add(Line(MailPanelKind.Status, mail.StatusLine));

            // 2. A feature that is not running answers nothing: one line says so, every button is gray.
            string off = featureActive ? null : texts.FeatureOff(mail.FeatureId);
            bool anyButton = mail.Actions.Count > 0 || (pending && mail.Choices.Count > 0);
            if (off != null && anyButton) items.Add(Line(MailPanelKind.Note, off));

            // 3. The actions.
            foreach (MailActionModel action in mail.Actions)
            {
                if (action == null) continue;

                bool enabled = MailAnswerRules.IsActionActive(action, featureActive);
                bool usedUp = action.Used && !action.Repeatable;
                string reason = enabled || usedUp ? null : ReasonOf(action.DisabledReasonKey, action.DisabledReasonArgs, texts);
                AddButton(items, MailPanelKind.Action, action.Id, LabelOf(action.LabelKey, action.Label, action.Id, texts), enabled, action.TooltipKey, reason, off, texts);
            }

            // 4. The choices, or the answer.
            if (pending)
            {
                foreach (MailChoiceModel choice in mail.Choices)
                {
                    if (choice == null) continue;

                    bool enabled = MailAnswerRules.IsChoiceActive(mail, choice, featureActive);
                    string reason = enabled ? null : ReasonOf(choice.DisabledReasonKey, choice.DisabledReasonArgs, texts);
                    AddButton(items, MailPanelKind.Choice, choice.Id, LabelOf(choice.LabelKey, choice.Label, choice.Id, texts), enabled, choice.TooltipKey, reason, off, texts);
                }
            }
            else if (mail.Choices.Count > 0 && !string.IsNullOrEmpty(mail.ChoiceId))
            {
                MailChoiceModel chosen = mail.FindChoice(mail.ChoiceId);
                string name = chosen == null ? mail.ChoiceId : LabelOf(chosen.LabelKey, chosen.Label, chosen.Id, texts);
                bool afterDeadline = mail.Resolution == MailRecordModel.ResolutionTimeout;
                items.Add(Line(MailPanelKind.Answer, texts.Answer(name, afterDeadline)));
            }

            return items;
        }

        /// <summary>
        /// A text that is equal for two panels exactly when they look and behave the same (the kind, id, state, text and hint of every row); the runtime
        /// compares it to skip a redraw. The id of the mail is added by the caller: the same rows of another mail click on another mail.
        /// </summary>
        public static string Signature(IList<MailPanelItem> items)
        {
            var sb = new StringBuilder();
            if (items == null) return string.Empty;

            for (int i = 0; i < items.Count; i++)
            {
                MailPanelItem item = items[i];
                sb.Append((int)item.Kind).Append('\u001f').Append(item.Id).Append('\u001f').Append(item.Enabled ? '1' : '0').Append('\u001f')
                  .Append(item.Text).Append('\u001f').Append(item.Tip).Append('\u001e');
            }
            return sb.ToString();
        }

        // ---- internals ----

        private static void AddButton(List<MailPanelItem> items, MailPanelKind kind, string id, string label, bool enabled, string tooltipKey,
            string reason, string featureOff, IMailPanelTexts texts)
        {
            string tip = string.IsNullOrEmpty(tooltipKey) ? string.Empty : texts.Tip(tooltipKey) ?? string.Empty;
            if (!enabled)
            {
                string why = featureOff ?? reason;   // a feature that is off explains every button the same way, once, above them
                if (!string.IsNullOrEmpty(why)) tip = tip.Length == 0 ? why : tip + "\n" + why;
            }

            items.Add(new MailPanelItem { Kind = kind, Id = id, Text = label, Enabled = enabled, Tip = tip });
            if (!enabled && featureOff == null && !string.IsNullOrEmpty(reason)) items.Add(Line(MailPanelKind.Reason, reason));
        }

        private static string LabelOf(string key, LocText inline, string id, IMailPanelTexts texts)
        {
            string label = texts.Label(key, inline);
            return string.IsNullOrEmpty(label) ? id : label;
        }

        private static string ReasonOf(string key, string[] args, IMailPanelTexts texts)
        {
            return string.IsNullOrEmpty(key) ? null : texts.Reason(key, args);
        }

        private static MailPanelItem Line(MailPanelKind kind, string text)
        {
            return new MailPanelItem { Kind = kind, Text = text ?? string.Empty };
        }
    }
}
