using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    // Fakes of the surroundings of the choices and the letters, shared by the tests of TSK-016..020. Everything is internal, so the runner never takes it for a test.

    /// <summary>The world of the choices as a table: the money and the default threshold of every company, who is gone, the batch run, a clock for the conditions.</summary>
    internal sealed class FakeChoiceWorld : IChoiceWorld
    {
        public readonly Dictionary<string, float> MoneyOf = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly HashSet<string> Gone = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The money of a company that has no entry.</summary>
        public float DefaultMoney = 100000000f;

        /// <summary>The default threshold of every company (a negative number).</summary>
        public float Threshold = -40000000f;

        public int Today;

        public bool InBatch { get; set; }

        public bool IsAlive(string companyKey)
        {
            return companyKey != null && !Gone.Contains(companyKey);
        }

        public float Money(string companyKey)
        {
            float value;
            return companyKey != null && MoneyOf.TryGetValue(companyKey, out value) ? value : DefaultMoney;
        }

        public float DefaultThreshold(string companyKey)
        {
            return Threshold;
        }

        public bool Check(ICondition condition, string companyKey)
        {
            if (condition == null) return true;
            return ConditionEvaluator.Evaluate(condition, new ConditionContext(null, EventDays.ToDate(Today), companyKey));
        }
    }

    /// <summary>
    /// The texts from the real files <c>L10n/ru/event-system.json</c> and <c>L10n/en/event-system.json</c> through the core's own resolver, plus the keys of the test
    /// events; the date is written the way the Russian game writes it (<c>dd.MM.yyyy</c>), the companies have names from a table.
    /// </summary>
    internal sealed class TableText : ITextSource
    {
        private readonly L10nTables _tables = new L10nTables();
        private readonly LocTextResolver _resolver;

        public readonly List<string> Warnings = new List<string>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        public TableText(string language = "ru")
        {
            var errors = new List<string>();
            foreach (string lang in new[] { "ru", "en" })
            {
                string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", lang, "event-system.json");
                Assert.True(File.Exists(path), "no table " + path);
                _tables.AddJson(lang, "event-system", SourceScan.ReadText(path), errors);
            }
            Assert.True(errors.Count == 0, "the tables have problems: " + string.Join("; ", errors));

            _resolver = new LocTextResolver(_tables, line => Warnings.Add(line));
            _resolver.Language = language;
        }

        public string Language
        {
            get { return _resolver.Language; }
            set { _resolver.Language = value; }
        }

        public L10nTables Tables
        {
            get { return _tables; }
        }

        /// <summary>A key of a test event, in both languages.</summary>
        public TableText With(string key, string ru, string en)
        {
            _tables.Set("ru", key, ru);
            _tables.Set("en", key, en);
            return this;
        }

        public string L(string key, params string[] args)
        {
            return _resolver.L(key, (object[])(args ?? new string[0]));
        }

        public string Find(string key)
        {
            return _resolver.Has(key) ? _resolver.L(key) : null;
        }

        public string Resolve(LocText text, string[] args)
        {
            return text == null ? string.Empty : _resolver.Resolve(text, (object[])(args ?? new string[0]));
        }

        public string Date(int day)
        {
            return EventDays.ToDate(day).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        public string Company(string companyKey)
        {
            string name;
            return companyKey != null && Names.TryGetValue(companyKey, out name) ? name : companyKey;
        }
    }

    /// <summary>The mail service as a list of letters: what was sent, delivered, answered, what the buttons and the line under the text say.</summary>
    internal sealed class FakeMail : IMailPort
    {
        public sealed class Button
        {
            public bool Enabled;
            public string ReasonKey;
            public string[] ReasonArgs;
        }

        public sealed class Letter
        {
            public int Id;
            public EventMailPlan Plan;
            public bool Delivered;
            public int DeliveredDay;
            public string Status;
            public string Answer;
            public int Resolves;
            public int Refreshes;
            public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        }

        public readonly List<Letter> Letters = new List<Letter>();
        public readonly List<string> StatusHistory = new List<string>();

        /// <summary>The mail service does not accept letters (<c>ModMail.Send</c> gives -1).</summary>
        public bool Refuse;

        /// <summary>Sending throws.</summary>
        public bool ThrowOnSend;

        private int _next = 100000;

        public int Send(EventMailPlan plan)
        {
            if (ThrowOnSend) throw new InvalidOperationException("the mail service is broken on purpose");
            if (Refuse) return -1;

            var letter = new Letter { Id = _next++, Plan = plan };
            foreach (MailChoicePlan choice in plan.Choices)
            {
                letter.Buttons[choice.Id] = new Button { Enabled = choice.Enabled, ReasonKey = choice.ReasonKey, ReasonArgs = choice.ReasonArgs };
            }
            Letters.Add(letter);
            return letter.Id;
        }

        public bool Resolve(int mailId, string choiceId)
        {
            Letter letter = Get(mailId);
            if (letter == null || letter.Answer != null) return false;
            letter.Answer = choiceId;
            letter.Resolves++;
            return true;
        }

        public void SetStatusLine(int mailId, string text)
        {
            Letter letter = Get(mailId);
            if (letter == null) return;
            letter.Status = text;
            StatusHistory.Add(text);
        }

        public void SetChoiceState(int mailId, string choiceId, bool enabled, string reasonKey, string[] reasonArgs)
        {
            Letter letter = Get(mailId);
            Button button;
            if (letter == null || !letter.Buttons.TryGetValue(choiceId, out button)) return;
            button.Enabled = enabled;
            button.ReasonKey = enabled ? null : reasonKey;
            button.ReasonArgs = enabled ? null : reasonArgs;
        }

        public void Refresh(int mailId)
        {
            Letter letter = Get(mailId);
            if (letter != null) letter.Refreshes++;
        }

        public MailInfo Find(string tag)
        {
            Letter found = null;
            foreach (Letter letter in Letters)
            {
                if (letter.Plan.Tag == tag) found = letter;
            }
            return found == null ? null : new MailInfo { MailId = found.Id, Delivered = found.Delivered, DeliveredDay = found.DeliveredDay };
        }

        public Letter Get(int mailId)
        {
            foreach (Letter letter in Letters)
            {
                if (letter.Id == mailId) return letter;
            }
            return null;
        }

        /// <summary>The letter of an instance, by its tag.</summary>
        public Letter OfInstance(int instanceId)
        {
            foreach (Letter letter in Letters)
            {
                if (letter.Plan.Tag == EventMailFormatter.TagOf(instanceId)) return letter;
            }
            return null;
        }

        /// <summary>Delivers the letters that wait, like the delivery pass of a day: each one is reported to <paramref name="delivered"/> (id, tag, day).</summary>
        public void DeliverQueued(int day, Action<int, string, int> delivered)
        {
            foreach (Letter letter in Letters.ToArray())
            {
                if (letter.Delivered) continue;
                letter.Delivered = true;
                letter.DeliveredDay = day;
                delivered(letter.Id, letter.Plan.Tag, day);
            }
        }

        public int CountOf(MailPriority priority)
        {
            int count = 0;
            foreach (Letter letter in Letters)
            {
                if (letter.Plan.Priority == priority) count++;
            }
            return count;
        }
    }

    internal sealed class FakeNotify : INotifyPort
    {
        public readonly List<string> Shown = new List<string>();

        public void Show(string text, string secondary)
        {
            Shown.Add(secondary == null ? text : text + " | " + secondary);
        }

        public int CountContaining(string part)
        {
            int count = 0;
            foreach (string line in Shown)
            {
                if (line.Contains(part)) count++;
            }
            return count;
        }
    }

    internal sealed class FakeWindow : IChoiceWindowPort
    {
        public readonly List<ChoiceWindowModel> Models = new List<ChoiceWindowModel>();
        public int Hides;

        public bool IsOpen { get; private set; }

        public ChoiceWindowModel Current { get; private set; }

        public void Show(ChoiceWindowModel model)
        {
            IsOpen = true;
            Current = model;
            Models.Add(model);
        }

        public void Hide()
        {
            IsOpen = false;
            Current = null;
            Hides++;
        }

        /// <summary>The ids of the instances whose window was opened, in order, without the redraws of the same window.</summary>
        public List<int> Opened()
        {
            var ids = new List<int>();
            foreach (ChoiceWindowModel model in Models)
            {
                if (ids.Count == 0 || ids[ids.Count - 1] != model.InstanceId) ids.Add(model.InstanceId);
            }
            return ids;
        }

        /// <summary>The player closes the window ("Решить позже" or the cross): it is gone, the engine is told by the test.</summary>
        public void CloseByPlayer()
        {
            IsOpen = false;
            Current = null;
        }
    }

    internal sealed class FakeSettings : IUiSettings
    {
        public bool PauseAll { get; set; }
        public bool News { get; set; }

        public FakeSettings()
        {
            News = true;
        }

        public bool PauseAllChoices
        {
            get { return PauseAll; }
        }

        public bool RivalNews
        {
            get { return News; }
        }
    }

    /// <summary>Events for the tests of choices: with titles, texts and causes written inline in both languages, and the keys of their buttons.</summary>
    internal static class ChoiceEvents
    {
        /// <summary>A manual event for the player: title "Событие X", with the given choices (the first is the default) and a timer of 14 days.</summary>
        public static EventDefinition Make(string id, EventChoice[] choices, bool important = false, params EventEffect[] effects)
        {
            EventDefinition definition = Fx.Event(id, choices, effects);
            definition.title = LocText.Of("Событие " + id, "Event " + id);
            definition.text = LocText.Of("Текст " + id, "Text of " + id);
            definition.cause = LocText.Of("Причина " + id, "Cause of " + id);
            definition.important = important;
            return definition;
        }

        /// <summary>A choice that costs money: a <c>Money</c> effect that spends exactly this amount.</summary>
        public static EventChoice Costing(string id, float amount)
        {
            return Fx.Choice(id, Fx.Effect("Money", "{ 'direction': 'Spend', 'min': " + amount.ToString("0", CultureInfo.InvariantCulture) + ", 'max': "
                                                    + amount.ToString("0", CultureInfo.InvariantCulture) + " }"));
        }

        /// <summary>A condition that reads a flag the test flips; the core describes it as <paramref name="name"/>.</summary>
        public static ICondition Flag(string name, Func<bool> value)
        {
            return new DelegateCondition(name, ctx => value());
        }
    }

    /// <summary>
    /// The choices, the letters and the windows over the scheduler and the effect runtime of <see cref="EffectsRig"/>: the choice controller and the UI controller are
    /// plugged into the runtime's options exactly as the engine plugs them in, the mail service, the notifications and the window are recorders.
    /// </summary>
    internal sealed class ChoiceRig
    {
        public readonly EffectsRig Rig;
        public readonly FakeChoiceWorld World = new FakeChoiceWorld();
        public readonly TableText Text = new TableText();
        public readonly FakeMail Mail = new FakeMail();
        public readonly FakeNotify Notify = new FakeNotify();
        public readonly FakeWindow Window = new FakeWindow();
        public readonly FakeSettings Settings = new FakeSettings();
        public readonly ChoiceController Choices;
        public readonly EventUiController Ui;
        public readonly List<string> UiFaults = new List<string>();

        /// <summary>The letters are not delivered (a batch run, a queue): <see cref="Day"/> leaves them in the queue.</summary>
        public bool HoldMail;

        /// <summary>The market-share ranking the news policy asks.</summary>
        public Func<string, int, bool> IsTop = (key, n) => true;

        /// <summary>A rig over these events with room for ten open choices of the player (the limit of the scheduler is not what these tests are about).</summary>
        public ChoiceRig(params EventDefinition[] definitions)
            : this(new FakeCatalog(FakeCatalog.ConfigWith(10), definitions))
        {
        }

        /// <param name="realTexts">The captions of the choices come from the shipped tables (the starter set); otherwise each choice gets a made-up "Вариант x" / "Option x".</param>
        public ChoiceRig(FakeCatalog catalog, RegistrySink sink = null, bool realTexts = false)
        {
            Rig = new EffectsRig(catalog, null, 777u, sink);
            foreach (EventDefinition definition in catalog.All)
            {
                if (realTexts) break;
                foreach (EventChoice choice in definition.choices ?? new EventChoice[0])
                {
                    if (!string.IsNullOrEmpty(choice.textKey)) Text.With(choice.textKey, "Вариант " + choice.id, "Option " + choice.id);
                }
            }

            var availability = new ChoiceAvailability(World, new RequirementText(Text.Find));
            Choices = new ChoiceController(Rig.Runtime, World, availability, line => Rig.Infos.Add(line));
            Ui = new EventUiController(Rig.Runtime, Choices, Text, Mail, Notify, Window, Settings, World, () => Rig.World.Today,
                packId => catalog.OptionsOf(packId), (key, n) => IsTop(key, n), () => catalog.Config.ReminderDaysBefore, (site, error) => UiFaults.Add(site + ": " + error.Message));

            EffectRuntimeOptions options = Rig.Options;
            Action<EventInstance> fired = options.Fired;
            Action<EventInstance, string, bool> resolved = options.ChoiceResolved;
            options.Fired = instance =>
            {
                Ui.OnFired(instance);
                fired(instance);
            };
            options.ChoiceResolved = (instance, choice, auto) =>
            {
                Ui.OnResolved(instance, choice, auto);
                resolved(instance, choice, auto);
            };
            options.Cancelled = data => Ui.OnCancelled(data);
            options.DeadlineDue = Choices.DeadlineDue;
            options.TargetGone = Choices.TargetGone;
        }

        public FakeCatalog Catalog
        {
            get { return Rig.Catalog; }
        }

        public int Today
        {
            get { return Rig.World.Today; }
        }

        /// <summary>Sets the day without processing it (the clock of the world, the conditions and the money).</summary>
        public void SetDay(int day)
        {
            Rig.World.Today = day;
            World.Today = day;
        }

        /// <summary>
        /// One game day the way the engine runs it: the scheduler's day, the daily refresh of the letters and windows, then the delivery pass of the mail service
        /// (unless the letters are held back).
        /// </summary>
        public void Day(int day)
        {
            SetDay(day);
            Rig.Scheduler.ProcessDay(day);
            Ui.DailyRefresh();
            Deliver(day);
        }

        public void Days(int first, int last)
        {
            for (int day = first; day <= last; day++) Day(day);
        }

        /// <summary>The delivery pass of the mail service for a day.</summary>
        public void Deliver(int day)
        {
            if (HoldMail) return;
            Mail.DeliverQueued(day, (id, tag, d) => Ui.OnMailDelivered(id, tag, d));
        }

        /// <summary>Fires an event for the company today and delivers its letter the same day (the usual course of a <c>Critical</c> letter).</summary>
        public EventInstanceDto FireAndDeliver(string eventId, string companyKey = CompanyKey.Player)
        {
            Assert.Equal(EventFireResult.Fired, Rig.Fire(eventId, companyKey), "the event fires");
            Deliver(Today);
            return Rig.Instance(eventId);
        }

        /// <summary>The player presses a button of the letter of an instance (the core closes the letter with that answer, then the engine's handler is called).</summary>
        public void ClickLetter(int instanceId, string choiceId)
        {
            FakeMail.Letter letter = Mail.OfInstance(instanceId);
            Assert.NotNull(letter, "the instance has a letter");
            if (letter.Answer == null) letter.Answer = choiceId;
            Ui.OnLetterAnswered(letter.Plan.Tag, choiceId);
        }
    }
}
