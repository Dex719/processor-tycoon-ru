using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.EventSystem
{
    /// <summary>One button of the choice window.</summary>
    public sealed class ChoiceButtonModel
    {
        public string ChoiceId;

        /// <summary>The caption; for a choice that goes into the red it carries the warning (<c>Нанять — уйдёте в минус на $350K</c>, AC-4.2).</summary>
        public string Label;

        /// <summary>The button works.</summary>
        public bool Enabled = true;

        /// <summary>Why the button is grey (empty while it works).</summary>
        public string Reason;

        /// <summary>The exact effects of the choice, one line each (the hint of the button, FR-7 item 3).</summary>
        public string Effects;
    }

    /// <summary>What the modal window of a waiting choice says (design.md "UI", FR-7 item 3): built from the instance, drawn by the game side.</summary>
    public sealed class ChoiceWindowModel
    {
        public int InstanceId;
        public string Title;
        public string Text;
        public string Cause;

        /// <summary>The countdown: <c>Осталось 9 из 14 дн. ■■■■■■■■■□□□□□</c>.</summary>
        public string Countdown;

        public int DaysLeft;
        public int TimerDays;
        public List<ChoiceButtonModel> Choices = new List<ChoiceButtonModel>();

        /// <summary>The caption of the button "Решить позже".</summary>
        public string DecideLaterLabel;

        /// <summary>The model of an instance that waits for the player's choice.</summary>
        public static ChoiceWindowModel Build(EventInstanceDto data, EventDefinition definition, IList<ChoiceState> states, int today, ITextSource text)
        {
            var model = new ChoiceWindowModel
            {
                InstanceId = data.id,
                Title = EventMailFormatter.TitleOf(data, definition, text),
                Text = definition != null ? text.Resolve(definition.text, data.args) : string.Empty,
                Cause = definition != null ? text.Resolve(definition.cause, data.args) : string.Empty,
                TimerDays = definition != null ? definition.timerDays : 0,
                DaysLeft = new EventInstance(data, definition).DaysLeft(today),
                DecideLaterLabel = text.L("es.ui.decide_later"),
            };
            if (!string.IsNullOrEmpty(model.Cause)) model.Cause = text.L("es.ui.cause", model.Cause);
            model.Countdown = CountdownBar.Format(model.DaysLeft, model.TimerDays, text);

            foreach (ChoiceOptionDto option in data.options)
            {
                ChoiceState state = StateFor(states, option.choiceId);
                string label = EventMailFormatter.ChoiceLabel(definition, option.choiceId, text);
                string warning = EventMailFormatter.DebtWarning(state, text);
                bool enabled = state == null || state.Enabled;
                model.Choices.Add(new ChoiceButtonModel
                {
                    ChoiceId = option.choiceId,
                    Label = string.IsNullOrEmpty(warning) ? label : text.L("es.ui.choice.warned", label, warning),
                    Enabled = enabled,
                    Reason = enabled || state == null || string.IsNullOrEmpty(state.ReasonKey) ? string.Empty : text.L(state.ReasonKey, state.ReasonArgs),
                    Effects = EventMailFormatter.EffectLines(option, text),
                });
            }
            return model;
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
    }

    /// <summary>The countdown line of the window: the days left and a bar with one division per day (FR-7 item 3).</summary>
    public static class CountdownBar
    {
        /// <summary>A bar longer than this is scaled down: a timer of a year is not 365 squares.</summary>
        public const int MaxCells = 30;

        public const char FilledCell = '■';
        public const char EmptyCell = '□';

        /// <summary>The bar alone: one cell per day (at most <see cref="MaxCells"/>), the days left filled; a day left is never lost to rounding.</summary>
        public static string Cells(int daysLeft, int timerDays)
        {
            int total = Math.Max(1, timerDays);
            int left = Math.Max(0, Math.Min(daysLeft, total));
            int cells = Math.Min(total, MaxCells);
            int filled = total <= MaxCells ? left : (int)Math.Ceiling((double)left * cells / total);

            var bar = new StringBuilder(cells);
            for (int i = 0; i < cells; i++) bar.Append(i < filled ? FilledCell : EmptyCell);
            return bar.ToString();
        }

        /// <summary><c>Осталось 9 из 14 дн. ■■■■■■■■■□□□□□</c>.</summary>
        public static string Format(int daysLeft, int timerDays, ITextSource text)
        {
            int total = Math.Max(1, timerDays);
            int left = Math.Max(0, Math.Min(daysLeft, total));
            return EventMailFormatter.StatusDaysLeft(left, total, text) + " " + Cells(left, total);
        }
    }

    /// <summary>
    /// The queue of the modal windows (FR-7 item 7, FR-13 item 2): one window is open at a time, the others wait in the order of the firings. Nothing opens during
    /// a batch run of a month; what piled up opens, one after another, when it ends (<c>ModTicks.BatchEnded</c>).
    /// </summary>
    public sealed class ChoiceWindowQueue
    {
        private readonly List<int> _waiting = new List<int>();

        /// <summary>The instance whose window is open now; 0 - none.</summary>
        public int Showing { get; private set; }

        public int Count
        {
            get { return _waiting.Count; }
        }

        /// <summary>The instances waiting for their window, in the order of the firings.</summary>
        public IList<int> Waiting
        {
            get { return _waiting.AsReadOnly(); }
        }

        /// <summary>An instance wants a window; one that is queued or shown already is not added again.</summary>
        public void Enqueue(int instanceId)
        {
            if (instanceId <= 0 || instanceId == Showing || _waiting.Contains(instanceId)) return;
            _waiting.Add(instanceId);
        }

        /// <summary>
        /// The next window to open: the first waiting instance for which <paramref name="stillWaits"/> is true (the others are dropped); null while a window is open or
        /// during a batch run. The instance becomes <see cref="Showing"/>.
        /// </summary>
        public int? TakeNext(bool batchRun, Func<int, bool> stillWaits)
        {
            if (batchRun || Showing != 0) return null;
            while (_waiting.Count > 0)
            {
                int id = _waiting[0];
                _waiting.RemoveAt(0);
                if (stillWaits != null && !stillWaits(id)) continue;
                Showing = id;
                return id;
            }
            return null;
        }

        /// <summary>The window of the instance was closed ("Решить позже", the cross, an answer). Another instance's window is not affected.</summary>
        public void Closed(int instanceId)
        {
            if (Showing == instanceId) Showing = 0;
        }

        /// <summary>The instance needs no window any more (answered, cancelled).</summary>
        public void Drop(int instanceId)
        {
            _waiting.Remove(instanceId);
            if (Showing == instanceId) Showing = 0;
        }

        public void Clear()
        {
            _waiting.Clear();
            Showing = 0;
        }
    }
}
