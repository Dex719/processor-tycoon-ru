using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// What the player sees of the events (design.md "UI", FR-7), over ports so it runs without the game:
    /// <list type="bullet">
    /// <item><b>A new event for the player:</b> a letter (<c>Critical</c> for a choice, <c>Normal</c> for an event without one, tag <c>es:&lt;instanceId&gt;</c>, no timer of
    /// its own), a notification, and the modal window when the event is <c>important</c> or the setting asks for it (AC-7.1). The windows wait in a queue in the order of
    /// the firings, and not at all during a batch run (FR-7 item 7, FR-13).</item>
    /// <item><b>The delivery of the letter</b> (found by the tag, never by the mail id) starts the timer: <c>Ответить до ... (N дн.)</c>; from the next day on
    /// <c>Осталось N из M дн.</c> (AC-7.2), the buttons of the letter follow the conditions and the money (AC-4.4, AC-4.2, AC-4.6), three days before the deadline a
    /// reminder is shown.</item>
    /// <item><b>An answer</b> (a click in the letter, a button of the window) goes through <see cref="ChoiceController.Answer"/>; the letter is closed with the applied
    /// choice, the window closes; the choice taken by the timer is announced ("Выбрано автоматически: ...", AC-4.1); a cancelled instance closes its letter.</item>
    /// <item><b>News about rivals</b> (<c>notifyPlayer</c>): a <c>Low</c> letter, unless the setting is off or the company is not among the
    /// <c>PackOptions.RivalNewsTopN</c> market leaders (AC-7.4).</item>
    /// </list>
    /// Every entry catches its failures (NFR-2): a broken letter or window never stops the day. Main thread only.
    /// </summary>
    public sealed class EventUiController
    {
        /// <summary>Days before the deadline at which the reminder is shown when the configuration does not say (FR-7 item 5; <c>reminderDaysBefore</c> of <c>config.json</c>).</summary>
        public const int DefaultReminderDays = 3;

        private readonly EffectRuntime _runtime;
        private readonly ChoiceController _choices;
        private readonly ITextSource _text;
        private readonly IMailPort _mail;
        private readonly INotifyPort _notify;
        private readonly IChoiceWindowPort _window;
        private readonly IUiSettings _settings;
        private readonly Func<int> _today;
        private readonly Func<string, PackOptions> _packOptions;
        private readonly Func<string, int, bool> _isTopByMarketShare;
        private readonly Func<int> _reminderDays;
        private readonly Action<string, Exception> _onFault;

        private readonly ChoiceWindowQueue _queue = new ChoiceWindowQueue();
        private readonly HashSet<int> _reminded = new HashSet<int>();
        private readonly Dictionary<int, string> _choiceSignature = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _statusLine = new Dictionary<int, string>();
        private int _answering;

        /// <param name="today">Today in game days.</param>
        /// <param name="packOptions">The options of a pack by id (<c>RivalNewsTopN</c>); null or a null answer - news about all rivals.</param>
        /// <param name="isTopByMarketShare">Whether an AI company is among the N leaders by market share; null - always.</param>
        /// <param name="reminderDays">Days before the deadline of the reminder (<c>EventConfig.ReminderDaysBefore</c>, 0 - none); null - <see cref="DefaultReminderDays"/>.</param>
        /// <param name="onFault">A failure of an entry: the site and the exception.</param>
        public EventUiController(EffectRuntime runtime, ChoiceController choices, ITextSource text, IMailPort mail, INotifyPort notify, IChoiceWindowPort window,
            IUiSettings settings, IChoiceWorld world, Func<int> today, Func<string, PackOptions> packOptions, Func<string, int, bool> isTopByMarketShare,
            Func<int> reminderDays, Action<string, Exception> onFault)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (choices == null) throw new ArgumentNullException("choices");
            if (text == null) throw new ArgumentNullException("text");
            if (mail == null) throw new ArgumentNullException("mail");
            if (notify == null) throw new ArgumentNullException("notify");
            if (window == null) throw new ArgumentNullException("window");
            if (settings == null) throw new ArgumentNullException("settings");
            if (world == null) throw new ArgumentNullException("world");
            if (today == null) throw new ArgumentNullException("today");
            _runtime = runtime;
            _choices = choices;
            _text = text;
            _mail = mail;
            _notify = notify;
            _window = window;
            _settings = settings;
            _today = today;
            _packOptions = packOptions ?? (pack => null);
            _isTopByMarketShare = isTopByMarketShare ?? ((key, n) => true);
            _reminderDays = reminderDays ?? (() => DefaultReminderDays);
            _onFault = onFault ?? ((site, error) => { });
        }

        /// <summary>The windows that wait for their turn and the one that is open.</summary>
        public ChoiceWindowQueue Windows
        {
            get { return _queue; }
        }

        // ---- the runtime's events ----

        /// <summary>An instance was created and its own effects applied (<see cref="EffectRuntimeOptions.Fired"/>): the letter, the notification, the window.</summary>
        public void OnFired(EventInstance instance)
        {
            Guard("ui/fired", () =>
            {
                EventInstanceDto data = instance != null ? instance.Data : null;
                EventDefinition definition = instance != null ? instance.Definition : null;
                if (data == null || definition == null || string.Equals(data.kind, EventInstanceKinds.Direct, StringComparison.Ordinal)) return;

                bool choice = string.Equals(data.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal);
                if (InstanceFactory.IsPlayer(data))
                {
                    if (choice) AnnounceChoice(data, definition);
                    else AnnounceEvent(data, definition);
                }
                else if (!choice && NewsAllowed(data, definition))
                {
                    SendLetter(() => EventMailFormatter.NewsMail(data, definition, _text));
                }
            });
        }

        /// <summary>A choice was made and its effects applied (<see cref="EffectRuntimeOptions.ChoiceResolved"/>): the letter and the window close, the timer's choice is announced.</summary>
        public void OnResolved(EventInstance instance, string choiceId, bool auto)
        {
            Guard("ui/resolved", () =>
            {
                EventInstanceDto data = instance != null ? instance.Data : null;
                EventDefinition definition = instance != null ? instance.Definition : null;
                if (data == null || definition == null) return;

                if (!InstanceFactory.IsPlayer(data))
                {
                    if (NewsAllowed(data, definition)) SendLetter(() => EventMailFormatter.NewsMail(data, definition, _text));
                    return;
                }

                CloseLetter(data, choiceId);
                if (auto && _answering != data.id)
                {
                    _notify.Show(_text.L("es.ui.auto_chosen", EventMailFormatter.ChoiceLabel(definition, choiceId, _text)), null);
                }
                Forget(data.id);
                Pump();
            });
        }

        /// <summary>A waiting instance was cancelled (<see cref="EffectRuntimeOptions.Cancelled"/>): its letter is closed, no effect was applied (FR-4 item 7).</summary>
        public void OnCancelled(EventInstanceDto data)
        {
            Guard("ui/cancelled", () =>
            {
                if (data == null || !InstanceFactory.IsPlayer(data)) return;
                EventDefinition definition = _runtime.DefinitionOf(data);
                string choiceId = definition != null && !string.IsNullOrEmpty(definition.defaultChoice) ? definition.defaultChoice : FirstChoice(data);
                if (!string.IsNullOrEmpty(choiceId)) CloseLetter(data, choiceId);
                Forget(data.id);
                Pump();
            });
        }

        // ---- the mail service's events ----

        /// <summary>
        /// A letter of the engine reached the inbox (<c>ModMail.Delivered</c>; the instance is found by the tag): the timer starts from this day and the line under
        /// the letter says "Ответить до ...". Letters that are not an instance's (<c>es:fx:...</c>) are not ours to handle.
        /// </summary>
        public void OnMailDelivered(int mailId, string tag, int deliveredDay)
        {
            Guard("ui/delivered", () =>
            {
                int instanceId;
                if (!EventMailFormatter.TryParseTag(tag, out instanceId)) return;
                EventInstanceDto data = _runtime.Find(instanceId);
                if (data == null || !string.Equals(data.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal)) return;

                if (data.mailId <= 0) data.mailId = mailId;
                _choices.Delivered(data, deliveredDay);

                int today = _today();
                EventDefinition definition = _runtime.DefinitionOf(data);
                string line = EventMailFormatter.Status(EventMailFormatter.StatusDeadline(data.deadlineDay, today, _text),
                    EventMailFormatter.DebtLines(_choices.StatesOf(data), definition, _text));
                PushStatus(data, line);
            });
        }

        /// <summary>The player pressed a button of a letter (<c>ModMail.RegisterChoiceHandler</c>; the core has closed the letter already).</summary>
        public void OnLetterAnswered(string tag, string choiceId)
        {
            Guard("ui/answer", () =>
            {
                int instanceId;
                if (!EventMailFormatter.TryParseTag(tag, out instanceId)) return;
                Answer(instanceId, choiceId);
            });
        }

        // ---- the window ----

        /// <summary>The player pressed a button of the window.</summary>
        public void OnWindowChoice(int instanceId, string choiceId)
        {
            Guard("ui/window-choice", () => Answer(instanceId, choiceId));
        }

        /// <summary>The window was closed without an answer ("Решить позже" or the cross): the letter and the timer stay, the next window opens.</summary>
        public void OnWindowClosed(int instanceId)
        {
            Guard("ui/window-closed", () =>
            {
                _queue.Closed(instanceId);
                Pump();
            });
        }

        // ---- the day ----

        /// <summary>
        /// Once a game day after the scheduler: the line "Осталось N из M дн.", the buttons of the letters, the warnings about the red balance, the reminder three days
        /// before the deadline, the open window. Not during a batch run; <see cref="OnBatchEnded"/> brings everything up to date after it.
        /// </summary>
        public void DailyRefresh()
        {
            Guard("ui/daily", () =>
            {
                if (_choices.Batch.InBatch) return;
                foreach (EventInstanceDto data in _choices.WaitingForPlayer())
                {
                    EventInstanceDto waiting = data;
                    Guard("ui/refresh", () => Refresh(waiting, false));   // one broken letter does not stop the others
                }
                Pump();
            });
        }

        /// <summary>The batch run of a month is over (<c>ModTicks.BatchEnded</c>): the windows that piled up open one after another.</summary>
        public void OnBatchEnded()
        {
            DailyRefresh();
        }

        /// <summary>A new game or a save is about to be read: nothing of the old windows, lines and reminders stays.</summary>
        public void Reset()
        {
            Guard("ui/reset", () =>
            {
                bool showing = _queue.Showing != 0;
                _queue.Clear();
                _choiceSignature.Clear();
                _statusLine.Clear();
                _reminded.Clear();
                if (showing && _window.IsOpen) _window.Hide();
            });
        }

        /// <summary>
        /// After a load: the instances are tied to their letters again (found by the tag, the mail id is a hint), the delivery day is taken from the letter, the buttons and
        /// the lines are recalculated (AC-10.3). The windows do not come back.
        /// </summary>
        public void AfterLoad()
        {
            Guard("ui/after-load", () =>
            {
                ForgetWindowsAndLines();
                Relink();
                Pump();
            });
        }

        /// <summary>
        /// The feature is on again after it stood still (FR-11): the letters delivered meanwhile give their delivery day, the buttons and the lines are recalculated for the
        /// new day. The windows that are open or wait stay as they are.
        /// </summary>
        public void AfterEnable()
        {
            Guard("ui/after-enable", () =>
            {
                _choiceSignature.Clear();
                _statusLine.Clear();
                Relink();
                Pump();
            });
        }

        private void ForgetWindowsAndLines()
        {
            bool showing = _queue.Showing != 0;
            _queue.Clear();
            if (showing && _window.IsOpen) _window.Hide();
            _choiceSignature.Clear();
            _statusLine.Clear();
            _reminded.Clear();
        }

        // Every waiting choice of the player finds its letter by the tag; a choice whose letter is gone gets its window (the way to decide) and counts from its firing.
        private void Relink()
        {
            foreach (EventInstanceDto data in _choices.WaitingForPlayer())
            {
                MailInfo info = _mail.Find(EventMailFormatter.TagOf(data.id));
                if (info == null)
                {
                    // the letter is gone (it never was, or the mail was cut): nothing is on its way, the timer counts from the firing and the window is the way to decide
                    data.mailId = 0;
                    _queue.Enqueue(data.id);
                    continue;
                }

                data.mailId = info.MailId;
                if (info.Delivered) _choices.Delivered(data, info.DeliveredDay);
                EventInstanceDto waiting = data;
                Guard("ui/refresh", () => Refresh(waiting, true));
            }
        }

        // ---- announcing ----

        private void AnnounceChoice(EventInstanceDto data, EventDefinition definition)
        {
            int id = SendLetter(() => EventMailFormatter.ChoiceMail(data, definition, _choices.StatesOf(data), _text));
            if (id > 0) data.mailId = id;

            int days = _choices.DaysLeft(data, _today());
            _notify.Show(_text.L("es.ui.notify.new", EventMailFormatter.TitleOf(data, definition, _text)), _text.L("es.ui.notify.deadline", Number(days)));

            // an event the mail service did not take has no letter to answer in: the window is then the way to decide
            if (definition.important || _settings.PauseAllChoices || id <= 0)
            {
                _queue.Enqueue(data.id);
                Pump();
            }
        }

        private void AnnounceEvent(EventInstanceDto data, EventDefinition definition)
        {
            SendLetter(() => EventMailFormatter.InfoMail(data, definition, _text));
            _notify.Show(_text.L("es.ui.notify.new", EventMailFormatter.TitleOf(data, definition, _text)), null);
        }

        // The letter is built and sent; a failure of either costs the feature a fault, not the event: -1 says there is no letter.
        private int SendLetter(Func<EventMailPlan> plan)
        {
            try
            {
                return _mail.Send(plan());
            }
            catch (Exception ex)
            {
                _onFault("ui/send", ex);
                return -1;
            }
        }

        private bool NewsAllowed(EventInstanceDto data, EventDefinition definition)
        {
            if (!definition.notifyPlayer || !_settings.RivalNews) return false;
            PackOptions pack = _packOptions(definition.packId);
            int top = pack != null ? pack.RivalNewsTopN : 0;
            return top <= 0 || _isTopByMarketShare(data.companyKey, top);
        }

        // ---- answers ----

        private void Answer(int instanceId, string choiceId)
        {
            EventInstanceDto data = _runtime.Find(instanceId);
            EventDefinition definition = data != null ? _runtime.DefinitionOf(data) : null;

            _answering = instanceId;
            ChoiceAnswer answer;
            try
            {
                answer = _choices.Answer(instanceId, choiceId, _today());
            }
            finally
            {
                _answering = 0;
            }

            if (answer.Outcome == ChoiceOutcome.FellBack)
            {
                _notify.Show(_text.L("es.ui.choice_unavailable", EventMailFormatter.ChoiceLabel(definition, answer.Requested, _text)), null);
            }
        }

        private void CloseLetter(EventInstanceDto data, string choiceId)
        {
            int mailId = data.mailId;
            if (mailId <= 0)
            {
                MailInfo info = _mail.Find(EventMailFormatter.TagOf(data.id));
                if (info != null) mailId = info.MailId;
            }
            if (mailId > 0) _mail.Resolve(mailId, choiceId);
        }

        private void Forget(int instanceId)
        {
            bool showing = _queue.Showing == instanceId;
            _queue.Drop(instanceId);
            if (showing && _window.IsOpen) _window.Hide();
            _reminded.Remove(instanceId);
            _choiceSignature.Remove(instanceId);
            _statusLine.Remove(instanceId);
        }

        // ---- refreshing ----

        private void Refresh(EventInstanceDto data, bool force)
        {
            EventDefinition definition = _runtime.DefinitionOf(data);
            if (definition == null) return;

            int today = _today();
            IList<ChoiceState> states = _choices.StatesOf(data);
            int left = _choices.DaysLeft(data, today);
            bool delivered = data.deliveredDay > 0 || data.mailId <= 0;

            if (data.mailId > 0)
            {
                PushChoiceStates(data, states, force);
                if (data.deliveredDay > 0)
                {
                    string line = EventMailFormatter.Status(EventMailFormatter.StatusDaysLeft(left, definition.timerDays, _text),
                        EventMailFormatter.DebtLines(states, definition, _text));
                    PushStatus(data, line);
                }
            }

            int remind = _reminderDays();
            if (remind > 0 && delivered && definition.timerDays > remind && left == remind && _reminded.Add(data.id))
            {
                _notify.Show(_text.L("es.ui.reminder", EventMailFormatter.TitleOf(data, definition, _text), Number(left)), null);
            }

            if (_queue.Showing == data.id && _window.IsOpen) _window.Show(ChoiceWindowModel.Build(data, definition, states, today, _text));
        }

        // The state of the buttons of a letter is sent when it differs from what was sent last, and the letter is drawn once (NFR-1: at most maxPendingChoices letters a day).
        private void PushChoiceStates(EventInstanceDto data, IList<ChoiceState> states, bool force)
        {
            var signature = new StringBuilder();
            foreach (ChoiceState state in states)
            {
                signature.Append(state.ChoiceId).Append('|').Append(state.Enabled ? '1' : '0').Append('|').Append(state.ReasonKey).Append('|')
                    .Append(string.Join(";", state.ReasonArgs ?? new string[0])).Append('\n');
            }

            string text = signature.ToString();
            string last;
            if (!force && _choiceSignature.TryGetValue(data.id, out last) && string.Equals(last, text, StringComparison.Ordinal)) return;
            _choiceSignature[data.id] = text;

            foreach (ChoiceState state in states) _mail.SetChoiceState(data.mailId, state.ChoiceId, state.Enabled, state.ReasonKey, state.ReasonArgs);
            _mail.Refresh(data.mailId);
        }

        private void PushStatus(EventInstanceDto data, string line)
        {
            if (data.mailId <= 0) return;
            string last;
            if (_statusLine.TryGetValue(data.id, out last) && string.Equals(last, line, StringComparison.Ordinal)) return;
            _statusLine[data.id] = line;
            _mail.SetStatusLine(data.mailId, line);
        }

        // ---- the window queue ----

        private void Pump()
        {
            if (_queue.Showing != 0 && !_window.IsOpen) _queue.Closed(_queue.Showing);   // the window went away without telling us (the scene changed)

            int? next = _queue.TakeNext(!_choices.Batch.WindowsAllowed, IsWaitingForPlayer);
            if (next == null) return;

            EventInstanceDto data = _runtime.Find(next.Value);
            EventDefinition definition = _runtime.DefinitionOf(data);
            _window.Show(ChoiceWindowModel.Build(data, definition, _choices.StatesOf(data), _today(), _text));
        }

        private bool IsWaitingForPlayer(int instanceId)
        {
            EventInstanceDto data = _runtime.Find(instanceId);
            return data != null && InstanceFactory.IsPlayer(data) && string.Equals(data.state, EventInstanceStates.AwaitingChoice, StringComparison.Ordinal);
        }

        // ---- helpers ----

        private void Guard(string site, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                _onFault(site, ex);
            }
        }

        private static string FirstChoice(EventInstanceDto data)
        {
            foreach (ChoiceOptionDto option in data.options)
            {
                if (option != null && !string.IsNullOrEmpty(option.choiceId)) return option.choiceId;
            }
            return null;
        }

        private static string Number(int value)
        {
            return Math.Max(0, value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
