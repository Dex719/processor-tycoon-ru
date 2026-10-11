using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// What the player sees (FR-7, TSK-017/018/020): the letter, the notification and the modal window of a new choice, the line under the letter that starts at the
    /// delivery and counts down (AC-7.2), the reminder three days before the deadline, "Решить позже", the queue of the windows in the order of the firings and their
    /// wait for the end of a batch run, the news about rivals and its limits (AC-7.4), the letters tied to their instances again after a load, a broken mail service.
    /// </summary>
    [Covers(typeof(EventUiController), typeof(ChoiceWindowModel), typeof(ChoiceButtonModel), typeof(ChoiceWindowQueue), typeof(PackOptions))]
    public class UiControllerTests
    {
        private static readonly int Start = Defs.Day(1985, 6, 1);

        private static EventDefinition Vote(string id, bool important = false)
        {
            return ChoiceEvents.Make(id, new[] { Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }")), Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }")) }, important);
        }

        private static ChoiceRig RigWith(params EventDefinition[] definitions)
        {
            var rig = new ChoiceRig(definitions);
            rig.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            rig.SetDay(Start);
            return rig;
        }

        // ---- the new event ----

        [Test]
        public void AChoiceForThePlayer_IsALetterAndANotification_AndNotAWindow()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.Rig.Fire("es.vote", CompanyKey.Player);

            Assert.Equal(1, rig.Mail.Letters.Count);
            Assert.Equal(MailPriority.Critical, rig.Mail.Letters[0].Plan.Priority);
            Assert.Equal("Новое событие: Событие es.vote | Нужно решение: 14 дн.", rig.Notify.Shown[0]);
            Assert.False(rig.Window.IsOpen, "no window for an event that is not important");
            Assert.Equal(rig.Mail.Letters[0].Id, rig.Rig.Instance("es.vote").mailId, "the id of the letter is kept as a hint");
            Assert.Equal(0, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));
        }

        [Test]
        public void AnEventWithoutAChoiceForThePlayer_IsANormalLetterAndANotification_EvenWhenImportant()
        {
            EventDefinition plain = ChoiceEvents.Make("es.plain", null, true, Fx.Effect("Reputation", "{ 'delta': 3 }"));
            ChoiceRig rig = RigWith(plain);
            rig.Rig.Fire("es.plain", CompanyKey.Player);

            Assert.Equal(1, rig.Mail.Letters.Count);
            Assert.Equal(MailPriority.Normal, rig.Mail.Letters[0].Plan.Priority);
            Assert.Equal("Новое событие: Событие es.plain", rig.Notify.Shown[0]);
            Assert.False(rig.Window.IsOpen, "there is nothing to choose in a window");
        }

        [Test]
        public void AC_7_2_TheLineUnderTheLetter_StartsAtTheDelivery_CountsDown_AndTheReminderComesOnce()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.HoldMail = true;
            rig.Rig.Fire("es.vote", CompanyKey.Player);
            FakeMail.Letter letter = rig.Mail.Letters[0];
            Assert.Null(letter.Status, "before the delivery the line is empty");
            rig.Days(Start, Start);
            Assert.Null(letter.Status, "still in the queue");

            rig.HoldMail = false;
            rig.Deliver(Start);
            Assert.Equal("Ответить до 15.06.1985 (14 дн.)", letter.Status);

            rig.Day(Start + 1);
            Assert.Equal("Осталось 13 из 14 дн.", letter.Status);
            rig.Days(Start + 2, Start + 10);
            Assert.Equal("Осталось 4 из 14 дн.", letter.Status);
            Assert.Equal(0, rig.Notify.CountContaining("на решение"), "no reminder yet");

            rig.Day(Start + 11);
            Assert.Equal("Осталось 3 из 14 дн.", letter.Status);
            Assert.Equal(1, rig.Notify.CountContaining("«Событие es.vote»: осталось 3 дн. на решение"), string.Join("; ", rig.Notify.Shown));
            Assert.Null(letter.Answer, "the letter has active buttons");

            rig.Days(Start + 12, Start + 13);
            Assert.Equal(1, rig.Notify.CountContaining("на решение"), "the reminder is shown once");
        }

        [Test]
        public void AnEventWithATimerOfThreeDaysOrLess_GetsNoReminder_TheNotificationAlreadySaysHowLongItIs()
        {
            EventDefinition quick = Vote("es.quick");
            quick.timerDays = 3;
            ChoiceRig rig = RigWith(quick);
            rig.FireAndDeliver("es.quick");
            rig.Days(Start + 1, Start + 2);
            Assert.Equal(0, rig.Notify.CountContaining("на решение"));
            Assert.Equal("Новое событие: Событие es.quick | Нужно решение: 3 дн.", rig.Notify.Shown[0]);
        }

        [Test]
        public void TheReminder_FollowsTheConfiguration()
        {
            var pack = new PackConfig { PackId = Defs.Pack, MaxPendingChoices = 2, ReminderDaysBefore = 5 };
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "*", Min = 0.25f, Max = 4f, PackId = Defs.Pack });
            var catalog = new FakeCatalog(EventConfig.Merge(new[] { pack }, line => { }), Vote("es.vote"));
            var rig = new ChoiceRig(catalog);
            rig.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            rig.SetDay(Start);
            rig.FireAndDeliver("es.vote");

            rig.Days(Start + 1, Start + 8);
            Assert.Equal(0, rig.Notify.CountContaining("на решение"));
            rig.Day(Start + 9);
            Assert.Equal(1, rig.Notify.CountContaining("осталось 5 дн. на решение"), string.Join("; ", rig.Notify.Shown));
        }

        // ---- the window ----

        [Test]
        public void AnImportantEvent_OpensTheWindow_WithTheCountdownAndTheButtons()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            rig.FireAndDeliver("es.vote");

            Assert.True(rig.Window.IsOpen);
            ChoiceWindowModel model = rig.Window.Current;
            Assert.Equal("Событие es.vote", model.Title);
            Assert.Equal("Текст es.vote", model.Text);
            Assert.Equal("Почему: Причина es.vote", model.Cause);
            Assert.Equal("Осталось 14 из 14 дн. ■■■■■■■■■■■■■■", model.Countdown);
            Assert.Equal("Решить позже", model.DecideLaterLabel);
            Assert.Equal(2, model.Choices.Count);
            Assert.Equal("Принять", model.Choices[0].Label);
            Assert.True(model.Choices[0].Enabled);
            Assert.Equal("Репутация: +1", model.Choices[0].Effects, "the exact effects are the hint of the button");
            Assert.Equal("Репутация: +5", model.Choices[1].Effects);
        }

        [Test]
        public void TheSettingPauseOnEveryChoice_OpensTheWindowForAnEventThatIsNotImportant()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.Settings.PauseAll = true;
            rig.FireAndDeliver("es.vote");
            Assert.True(rig.Window.IsOpen);
        }

        [Test]
        public void AC_7_2_DecideLater_ClosesTheWindow_TheLetterAndTheTimerStay_AndTheWindowDoesNotComeBack()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            Assert.True(rig.Window.IsOpen);

            rig.Window.CloseByPlayer();
            rig.Ui.OnWindowClosed(instance.id);
            Assert.False(rig.Window.IsOpen);

            rig.Days(Start + 1, Start + 11);
            Assert.False(rig.Window.IsOpen, "it does not open again by itself");
            Assert.Equal(1, rig.Window.Opened().Count);
            Assert.Equal(EventInstanceStates.AwaitingChoice, rig.Rig.Instance("es.vote").state);
            Assert.Null(letter.Answer, "the letter has active buttons");
            Assert.Equal("Осталось 3 из 14 дн.", letter.Status);
            Assert.Equal(1, rig.Notify.CountContaining("осталось 3 дн. на решение"));
        }

        [Test]
        public void TheOpenWindow_IsDrawnAgainEveryDay_WithTheDaysLeft()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            rig.FireAndDeliver("es.vote");
            rig.Days(Start + 1, Start + 5);

            Assert.True(rig.Window.IsOpen);
            Assert.Equal(9, rig.Window.Current.DaysLeft);
            Assert.Equal("Осталось 9 из 14 дн. ■■■■■■■■■□□□□□", rig.Window.Current.Countdown);
        }

        [Test]
        public void AButtonOfTheWindow_AppliesTheChoice_ClosesTheLetterWithIt_AndTheWindow()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);

            rig.Ui.OnWindowChoice(instance.id, "b");
            Assert.Equal(5f, rig.Rig.Game.Reputation[CompanyKey.Player]);
            Assert.Equal("b", letter.Answer, "the letter closes with the choice (ModMail.Resolve)");
            Assert.False(rig.Window.IsOpen);
            Assert.Equal(0, rig.Notify.CountContaining("автоматически"), "the player's own choice is not announced as automatic");
            Assert.Equal("b", rig.Rig.Entries(EventLogKinds.Resolved)[0].choiceId);
        }

        [Test]
        public void SeveralWindows_OpenOneAfterAnother_InTheOrderOfTheFirings()
        {
            ChoiceRig rig = RigWith(Vote("es.first", important: true), Vote("es.second", important: true), Vote("es.third", important: true));
            EventInstanceDto first = rig.FireAndDeliver("es.first");
            EventInstanceDto second = rig.FireAndDeliver("es.second");
            EventInstanceDto third = rig.FireAndDeliver("es.third");

            Assert.Equal(first.id, rig.Window.Current.InstanceId, "the first one is on the screen");
            Assert.Equal(2, rig.Ui.Windows.Count, "two wait their turn");

            rig.Ui.OnWindowChoice(first.id, "a");
            Assert.Equal(second.id, rig.Window.Current.InstanceId);
            rig.Window.CloseByPlayer();
            rig.Ui.OnWindowClosed(second.id);   // "Решить позже" on the second
            Assert.Equal(third.id, rig.Window.Current.InstanceId, "the next one opens");

            rig.Ui.OnWindowChoice(third.id, "b");
            Assert.False(rig.Window.IsOpen);
            Assert.Equal(first.id + "," + second.id + "," + third.id, string.Join(",", rig.Window.Opened().ConvertAll(id => id.ToString()).ToArray()));
            Assert.Equal(EventInstanceStates.AwaitingChoice, rig.Rig.Instance("es.second").state, "the second one is still waiting for the player");
        }

        [Test]
        public void DuringABatchRun_NoWindowOpens_UntilTheRunIsOver()
        {
            ChoiceRig rig = RigWith(Vote("es.first", important: true), Vote("es.second", important: true));
            rig.World.InBatch = true;
            rig.HoldMail = true;
            rig.Rig.Fire("es.first", CompanyKey.Player);
            rig.Rig.Fire("es.second", CompanyKey.Player);
            rig.Days(Start, Start + 3);
            Assert.Equal(0, rig.Window.Models.Count, "nothing opens during the run");
            Assert.Equal(2, rig.Ui.Windows.Count);

            rig.World.InBatch = false;
            rig.HoldMail = false;
            rig.Ui.OnBatchEnded();
            Assert.Equal(1, rig.Window.Opened().Count, "one window at a time");
            EventInstanceDto first = rig.Rig.Instance("es.first");
            Assert.Equal(first.id, rig.Window.Current.InstanceId, "the order of the firings");

            rig.Ui.OnWindowChoice(first.id, "a");
            Assert.Equal(rig.Rig.Instance("es.second").id, rig.Window.Current.InstanceId);
        }

        [Test]
        public void AWindowForAnInstanceThatWasAnsweredInTheLetter_IsNotOpened()
        {
            ChoiceRig rig = RigWith(Vote("es.first", important: true), Vote("es.second", important: true));
            EventInstanceDto first = rig.FireAndDeliver("es.first");
            EventInstanceDto second = rig.FireAndDeliver("es.second");
            Assert.Equal(first.id, rig.Window.Current.InstanceId);

            rig.ClickLetter(second.id, "a");   // answered in the letter while its window waits
            rig.Ui.OnWindowChoice(first.id, "a");
            Assert.False(rig.Window.IsOpen, "nothing left to show");
            Assert.Equal(first.id.ToString(), string.Join(",", rig.Window.Opened().ConvertAll(id => id.ToString()).ToArray()));
        }

        [Test]
        public void TheWindowQueue_KeepsTheOrder_DropsTheDuplicates_AndWaitsForTheBatchRun()
        {
            var queue = new ChoiceWindowQueue();
            queue.Enqueue(5);
            queue.Enqueue(7);
            queue.Enqueue(5);
            queue.Enqueue(0);
            queue.Enqueue(9);
            Assert.Equal(3, queue.Count);
            Assert.Equal("5,7,9", string.Join(",", new List<int>(queue.Waiting).ConvertAll(i => i.ToString()).ToArray()));

            Assert.Null(queue.TakeNext(true, null), "not during a batch run");
            Assert.Equal(5, queue.TakeNext(false, id => true).Value);
            Assert.Equal(5, queue.Showing);
            Assert.Null(queue.TakeNext(false, id => true), "a window is open");
            queue.Enqueue(5);
            Assert.Equal(2, queue.Count, "the one on the screen is not queued again");

            queue.Closed(7);
            Assert.Equal(5, queue.Showing, "the window of another instance closed: this one is unaffected");
            queue.Closed(5);
            Assert.Equal(0, queue.Showing);
            Assert.Equal(9, queue.TakeNext(false, id => id != 7).Value, "7 is not waiting for a choice any more: dropped");
            queue.Drop(9);
            Assert.Equal(0, queue.Showing);
            Assert.Null(queue.TakeNext(false, null));

            queue.Enqueue(3);
            queue.Clear();
            Assert.Equal(0, queue.Count);
        }

        // ---- the news ----

        private static ChoiceRig NewsRig(PackOptions options)
        {
            EventDefinition plain = ChoiceEvents.Make("es.news", null, false, Fx.Effect("Reputation", "{ 'delta': -2 }"));
            plain.notifyPlayer = true;
            var catalog = new FakeCatalog(Fx.DesignLimits(), plain);
            if (options != null) catalog.Pack(Defs.Pack, options);
            var rig = new ChoiceRig(catalog);
            rig.SetDay(Start);
            return rig;
        }

        [Test]
        public void TheNewsAboutARival_IsALowLetter_UnlessTheSettingIsOff()
        {
            ChoiceRig rig = NewsRig(null);
            rig.Rig.Fire("es.news", "ai:1");
            Assert.Equal(1, rig.Mail.CountOf(MailPriority.Low));
            Assert.Equal(0, rig.Notify.Shown.Count, "a news letter has no pop-up");

            rig.Settings.News = false;
            rig.Rig.Fire("es.news", "ai:2");
            Assert.Equal(1, rig.Mail.Letters.Count, "the setting turns the news off");
        }

        [Test]
        public void AC_7_4_TheNewsOfACompanyOutsideTheTopN_IsNotSent_ButTheJournalHasTheEntry()
        {
            ChoiceRig rig = NewsRig(new PackOptions { RivalNewsTopN = 5 });
            var asked = new List<string>();
            rig.IsTop = (key, n) =>
            {
                asked.Add(key + "/" + n);
                return key != "ai:1";   // the 6th by market share
            };

            rig.Rig.Fire("es.news", "ai:1");
            Assert.Equal(0, rig.Mail.Letters.Count, "no news about the company in the 6th place");
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.Fired).Count, "the journal has the entry anyway");
            Assert.Equal("ai:1", rig.Rig.Entries(EventLogKinds.Fired)[0].companyKey);

            rig.Rig.Fire("es.news", "ai:2");
            Assert.Equal(1, rig.Mail.CountOf(MailPriority.Low), "a leader gets its news");
            Assert.Equal("ai:1/5,ai:2/5", string.Join(",", asked.ToArray()));
        }

        [Test]
        public void AnEventOfARivalWithoutTheFlag_HasNoNews()
        {
            EventDefinition quiet = ChoiceEvents.Make("es.quiet", null, false, Fx.Effect("Reputation", "{ 'delta': -2 }"));
            ChoiceRig rig = RigWith(quiet);
            rig.Rig.Fire("es.quiet", "ai:1");
            Assert.Equal(0, rig.Mail.Letters.Count);
        }

        // ---- the day, the language, the load ----

        [Test]
        public void TheButtonsOfALetter_AreSentOnlyWhenTheyChange()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);

            rig.Day(Start + 1);
            int refreshes = letter.Refreshes;
            Assert.True(refreshes >= 1, "the first daily check sent the state of the buttons");
            rig.Days(Start + 2, Start + 6);
            Assert.Equal(refreshes, letter.Refreshes, "the letter is not drawn again while nothing about its buttons changed");
        }

        [Test]
        public void AChangeOfLanguage_WritesTheLinesUnderTheLettersAgain()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            rig.Day(Start + 1);
            Assert.Equal("Осталось 13 из 14 дн.", letter.Status);

            rig.Text.Language = "en";
            rig.Ui.DailyRefresh();
            Assert.Equal("13 of 14 days left", letter.Status);
        }

        [Test]
        public void AfterALoad_TheInstancesAreTiedToTheirLettersByTheTag_AndTheLinesAreWrittenAgain()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            rig.Days(Start + 1, Start + 3);

            // a loaded game: the saved instance has lost what the runtime knew, the letter was restored by the mail service
            int deliveredDay = instance.deliveredDay;
            instance.mailId = 0;
            instance.deliveredDay = 0;
            letter.Status = null;
            rig.Window.CloseByPlayer();

            rig.Ui.AfterLoad();
            Assert.Equal(letter.Id, instance.mailId);
            Assert.Equal(deliveredDay, instance.deliveredDay);
            Assert.Equal(Start + 14, instance.deadlineDay, "the deadline did not move");
            Assert.Equal("Осталось 11 из 14 дн.", letter.Status);
            Assert.False(rig.Window.IsOpen, "the windows do not come back after a load");
            Assert.Equal(0, rig.Ui.Windows.Count);
        }

        [Test]
        public void AfterALoad_AnInstanceWhoseLetterIsGone_DoesNotWaitForIt_AndGetsItsWindow()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.HoldMail = true;
            rig.Rig.Fire("es.vote", CompanyKey.Player);
            EventInstanceDto instance = rig.Rig.Instance("es.vote");
            Assert.True(ChoiceController.WaitsForDelivery(instance), "a letter in the queue");

            rig.Mail.Letters.Clear();   // the mail section of the save does not have it
            rig.Ui.AfterLoad();
            Assert.Equal(0, instance.mailId);
            Assert.False(ChoiceController.WaitsForDelivery(instance), "it does not wait for a letter that will never come");
            Assert.True(rig.Window.IsOpen, "the window is the way to decide");
            Assert.Equal(instance.id, rig.Window.Current.InstanceId);

            rig.Days(Start + 1, Start + 14);
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "and the timer counts from the firing");
        }

        [Test]
        public void ANewGame_ForgetsTheWindowsAndTheLines()
        {
            ChoiceRig rig = RigWith(Vote("es.vote", important: true));
            rig.FireAndDeliver("es.vote");
            Assert.True(rig.Window.IsOpen);

            rig.Ui.Reset();
            Assert.False(rig.Window.IsOpen);
            Assert.Equal(0, rig.Ui.Windows.Count);
        }

        [Test]
        public void ABrokenMailService_CostsTheFeatureAFault_ButNotTheEventNorTheDay()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.Mail.ThrowOnSend = true;

            Assert.Equal(EventFireResult.Fired, rig.Rig.Fire("es.vote", CompanyKey.Player));
            Assert.Equal(1, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));
            Assert.True(rig.UiFaults[0].StartsWith("ui/send"), rig.UiFaults[0]);
            Assert.Equal(EventInstanceStates.AwaitingChoice, rig.Rig.Instance("es.vote").state, "the event exists");
            Assert.Equal(0, rig.Rig.Instance("es.vote").mailId);
            Assert.Equal(1, rig.Notify.CountContaining("Новое событие"), "the player is told");
            Assert.True(rig.Window.IsOpen, "without a letter the window is the way to decide");

            rig.Ui.OnWindowChoice(rig.Rig.Instance("es.vote").id, "b");
            Assert.Equal(5f, rig.Rig.Game.Reputation[CompanyKey.Player]);
            Assert.False(rig.Window.IsOpen);
        }

        [Test]
        public void AMailServiceThatRefusesTheLetter_LeavesTheWindowAsTheWayToDecide_AndTheTimerCountsFromTheFiring()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.Mail.Refuse = true;

            rig.Rig.Fire("es.vote", CompanyKey.Player);
            Assert.Equal(0, rig.UiFaults.Count, "a refusal is not a failure");
            Assert.True(rig.Window.IsOpen, "an event that is not important gets its window when there is no letter");
            Assert.Equal(0, rig.Rig.Instance("es.vote").mailId);

            rig.Window.CloseByPlayer();
            rig.Ui.OnWindowClosed(rig.Rig.Instance("es.vote").id);
            rig.Days(Start + 1, Start + 14);
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "no letter, no waiting for it: the timer counts from the firing");
        }

        [Test]
        public void AnInstanceThatIsGone_IsNeverTheCauseOfAFault()
        {
            ChoiceRig rig = RigWith(Vote("es.vote"));
            rig.Ui.OnMailDelivered(100500, "es:77", Start);
            rig.Ui.OnMailDelivered(100501, "es:fx:1/common/0", Start);
            rig.Ui.OnMailDelivered(100502, "onb:welcome", Start);
            rig.Ui.OnLetterAnswered("es:77", "a");
            rig.Ui.OnLetterAnswered("es:fx:3/common/0", "a");
            rig.Ui.OnWindowChoice(77, "a");
            rig.Ui.OnWindowClosed(77);
            rig.Ui.OnFired(null);
            rig.Ui.OnResolved(null, "a", true);
            rig.Ui.OnCancelled(null);
            Assert.Equal(0, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));
            Assert.Equal(0, rig.Notify.Shown.Count);
        }
    }
}
