using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The mail of the core for the news and the console orders (<see cref="IGpuMail"/>, <see cref="IGpuOrderMail"/>): <c>ModMail.Send</c>, <c>ModMail.Find</c>, the state of the
    /// buttons of a letter and the handlers of its answers. The id of a letter, its delivery, the order by priority and the daily limits
    /// (<c>mail.maxDeliveredPerDay</c>, <c>mail.maxPerFeaturePerDay</c>) are the core's (FR-14 item 7): the adapter picks no id, keeps no counter and holds nothing back. The core keeps a
    /// letter that arrives during the fast-forward or a load; the news do not make one then (<see cref="GpuNews.Silently"/>). This is the only class of the feature that touches
    /// <c>ModMail</c>.
    /// </summary>
    internal sealed class GpuMailAdapter : IGpuOrderMail
    {
        /// <summary>The tags of the letters of the console orders start with this (<c>gpu:console:&lt;orderId&gt;</c>, FR-19 item 1).</summary>
        private const string ConsoleTagPrefix = "gpu:console:";

        public int Send(GpuMailLetter letter)
        {
            if (letter == null) return -1;
            return ModMail.Send(new ModMailMessage
            {
                FeatureId = GpuFeature.FeatureId,
                Tag = letter.Tag,
                TitleKey = letter.TitleKey,
                BodyKey = letter.BodyKey,
                Args = letter.Args,
                Priority = letter.Priority,
                TimeoutDays = letter.TimeoutDays,
                DefaultChoiceId = string.IsNullOrEmpty(letter.DefaultChoiceId) ? null : letter.DefaultChoiceId,
                Choices = ChoicesOf(letter.Choices),
                Actions = ActionsOf(letter.Actions),
            });
        }

        public bool Exists(string tag)
        {
            return !string.IsNullOrEmpty(tag) && ModMail.Find(GpuFeature.FeatureId, tag) != null;
        }

        public void SetChoiceState(int mailId, string choiceId, bool enabled, string disabledReasonKey)
        {
            ModMail.SetChoiceState(mailId, choiceId, enabled, enabled || string.IsNullOrEmpty(disabledReasonKey) ? null : disabledReasonKey);
        }

        public void Refresh(int mailId)
        {
            ModMail.Refresh(mailId);
        }

        public bool Resolve(int mailId, string choiceId)
        {
            return ModMail.Resolve(mailId, choiceId);
        }

        /// <summary>
        /// Listens to the letters of the console orders: <paramref name="onChoice"/> when the player presses a choice or the answer period passes (<c>mailId, choiceId</c>),
        /// <paramref name="onAction"/> at each click on a button that leaves the letter open (<c>mailId, actionId</c>) and <paramref name="onDelivered"/> when such a letter reaches the
        /// inbox (<c>mailId</c>). The handlers arrive already guarded; they are only called for the letters of this feature with a tag of the console orders. The handler of a feature
        /// is one, a second registration replaces the first, so this is safe to call again.
        /// </summary>
        public static void BindConsoleHandlers(Action<int, string> onChoice, Action<int, string> onAction, Action<int> onDelivered)
        {
            ModMail.RegisterChoiceHandler(GpuFeature.FeatureId, (record, choiceId) =>
            {
                if (record != null && IsConsoleLetter(record.Tag) && onChoice != null) onChoice(record.MailId, choiceId);
            });
            ModMail.RegisterActionHandler(GpuFeature.FeatureId, (record, actionId) =>
            {
                if (record != null && IsConsoleLetter(record.Tag) && onAction != null) onAction(record.MailId, actionId);
            });

            ModMail.Delivered -= DeliveredHandler;
            _onDelivered = onDelivered;
            ModMail.Delivered += DeliveredHandler;
        }

        private static Action<int> _onDelivered;

        private static void DeliveredHandler(ModMailRecord record)
        {
            if (record == null || record.FeatureId != GpuFeature.FeatureId || !IsConsoleLetter(record.Tag)) return;
            Action<int> handler = _onDelivered;
            if (handler != null) handler(record.MailId);
        }

        private static bool IsConsoleLetter(string tag)
        {
            return tag != null && tag.StartsWith(ConsoleTagPrefix, StringComparison.Ordinal);
        }

        private static List<ModMailChoice> ChoicesOf(List<GpuMailChoice> choices)
        {
            if (choices == null || choices.Count == 0) return null;
            var result = new List<ModMailChoice>(choices.Count);
            foreach (GpuMailChoice choice in choices)
            {
                result.Add(new ModMailChoice
                {
                    Id = choice.Id,
                    LabelKey = choice.LabelKey,
                    TooltipKey = string.IsNullOrEmpty(choice.TooltipKey) ? null : choice.TooltipKey,
                    Enabled = choice.Enabled,
                    DisabledReasonKey = choice.Enabled || string.IsNullOrEmpty(choice.DisabledReasonKey) ? null : choice.DisabledReasonKey,
                });
            }
            return result;
        }

        private static List<ModMailAction> ActionsOf(List<GpuMailAction> actions)
        {
            if (actions == null || actions.Count == 0) return null;
            var result = new List<ModMailAction>(actions.Count);
            foreach (GpuMailAction action in actions)
            {
                result.Add(new ModMailAction
                {
                    Id = action.Id,
                    LabelKey = action.LabelKey,
                    TooltipKey = string.IsNullOrEmpty(action.TooltipKey) ? null : action.TooltipKey,
                    Repeatable = action.Repeatable,
                });
            }
            return result;
        }
    }
}
