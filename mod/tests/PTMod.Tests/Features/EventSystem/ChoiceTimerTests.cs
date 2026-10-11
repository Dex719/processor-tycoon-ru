using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The timer of the player's choices (FR-4, FR-13 item 2, TSK-016/020): the deadline counted from the delivery of the letter and only forward (AC-4.1, AC-4.5), an
    /// instance whose letter is still in the queue does not expire, the batch run of a month does not shorten the timer (AC-13.2), a choice answered once, and the
    /// cancelling of a choice whose target went bankrupt. The scheduler, the effect runtime, the choice controller and the UI controller run together over the fakes.
    /// </summary>
    [Covers(typeof(ChoiceController), typeof(ChoiceAnswer), typeof(ChoiceOutcome), typeof(ChoiceWindowQueue), typeof(IChoiceWorld), typeof(IChoiceWindowPort),
        typeof(IMailPort), typeof(INotifyPort), typeof(IUiSettings), typeof(MailInfo), typeof(MailChoicePlan), typeof(EventMailPlan))]
    public class ChoiceTimerTests
    {
        private static readonly int Start = Defs.Day(1985, 6, 1);

        private static ChoiceRig RigWithVote()
        {
            EventDefinition vote = ChoiceEvents.Make("es.vote", new[]
            {
                Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }")),
                Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }")),
            });
            var rig = new ChoiceRig(vote);
            rig.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            rig.SetDay(Start);
            return rig;
        }

        [Test]
        public void AC_4_1_TheDefaultChoiceIsAppliedOnTheDeadline_TheLetterIsClosed_AndTheChoiceIsAnnounced()
        {
            ChoiceRig rig = RigWithVote();
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            Assert.Equal(Start, instance.firedDay);
            Assert.Equal(Start, instance.deliveredDay, "the letter of a Critical event is delivered the day it is sent");
            Assert.Equal(Start + 14, instance.deadlineDay);

            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);
            Assert.Equal(MailPriority.Critical, letter.Plan.Priority);

            rig.Days(Start + 1, Start + 13);
            Assert.Equal(EventInstanceStates.AwaitingChoice, rig.Rig.Instance("es.vote").state, "still waiting on the day before the deadline");
            Assert.Equal("Осталось 1 из 14 дн.", letter.Status);
            Assert.Null(letter.Answer);

            rig.Days(Start + 14, Start + 14);
            Assert.Null(rig.Rig.Instance("es.vote"), "the choice is made on the deadline day, 1985-06-15");
            EventLogEntry auto = rig.Rig.Entries(EventLogKinds.AutoResolved)[0];
            Assert.Equal("a", auto.choiceId, "defaultChoice");
            Assert.Equal("a", letter.Answer, "the letter is closed with that answer (ModMail.Resolve)");
            Assert.Equal(1, rig.Notify.CountContaining("Выбрано автоматически: Принять"), string.Join("; ", rig.Notify.Shown));
            Assert.Equal(1f, rig.Rig.Game.Reputation[CompanyKey.Player], "the effects of the default choice, not of the other one");
            Assert.Equal(0, rig.UiFaults.Count, string.Join("; ", rig.UiFaults));
        }

        [Test]
        public void AC_4_5_TheDeadlineIsCountedFromTheDayTheLetterIsDelivered_NotFromTheFiring()
        {
            ChoiceRig rig = RigWithVote();

            // the 5th day of the batch run of a month: the event fires, the letter waits for the end of the run
            rig.World.InBatch = true;
            rig.HoldMail = true;
            rig.SetDay(Start + 4);
            Assert.Equal(EventFireResult.Fired, rig.Rig.Fire("es.vote", CompanyKey.Player));
            EventInstanceDto instance = rig.Rig.Instance("es.vote");
            Assert.Equal(Start + 4 + 14, instance.deadlineDay, "provisional: the day of the firing + timerDays");

            rig.Days(Start + 5, Start + 29);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            Assert.Equal(Start + 4 + 14 + 25, instance.deadlineDay, "every processed day of the run moves it by one");

            // the run is over, the letter is delivered on the 31st day
            rig.World.InBatch = false;
            rig.HoldMail = false;
            rig.Day(Start + 30);
            Assert.Equal(Start + 30, instance.deliveredDay);
            Assert.Equal(Start + 30 + 14, instance.deadlineDay, "the day of the delivery + timerDays");
            Assert.Equal(14, instance.deadlineDay - instance.deliveredDay);
        }

        [Test]
        public void AnInstanceWhoseLetterIsStillInTheQueue_DoesNotExpire_AndItsDeadlineStartsWithTheDelivery()
        {
            ChoiceRig rig = RigWithVote();
            rig.HoldMail = true;
            rig.Rig.Fire("es.vote", CompanyKey.Player);
            EventInstanceDto instance = rig.Rig.Instance("es.vote");
            Assert.True(instance.mailId > 0, "the letter was sent");
            Assert.True(ChoiceController.WaitsForDelivery(instance));

            rig.Days(Start + 1, Start + 60);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state, "60 days after the provisional deadline, still waiting");
            Assert.Equal(Start + 14, instance.deadlineDay, "outside a batch run nothing moves it");
            Assert.Equal(0, rig.Rig.Entries(EventLogKinds.AutoResolved).Count);

            rig.HoldMail = false;
            rig.Day(Start + 61);
            Assert.Equal(Start + 61, instance.deliveredDay);
            Assert.Equal(Start + 61 + 14, instance.deadlineDay);
            Assert.False(ChoiceController.WaitsForDelivery(instance));
        }

        [Test]
        public void TheDeadlineMovesOnlyForward_AndOnlyTheFirstDeliveryCounts()
        {
            ChoiceRig rig = RigWithVote();
            rig.HoldMail = true;
            rig.Rig.Fire("es.vote", CompanyKey.Player);
            EventInstanceDto instance = rig.Rig.Instance("es.vote");

            instance.deadlineDay = Start + 100;   // a deadline pushed far ahead (a pause of the feature)
            Assert.True(rig.Choices.Delivered(instance, Start + 2));
            Assert.Equal(Start + 100, instance.deadlineDay, "max(deadline, delivery + timer): never earlier than before");
            Assert.Equal(Start + 2, instance.deliveredDay);

            Assert.False(rig.Choices.Delivered(instance, Start + 50), "a second delivery changes nothing");
            Assert.Equal(Start + 2, instance.deliveredDay);
            Assert.False(rig.Choices.Delivered(null, Start));
        }

        [Test]
        public void AC_13_2_ABatchRun_DoesNotShortenTheTimer()
        {
            ChoiceRig rig = RigWithVote();
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);

            rig.Days(Start + 1, Start + 9);
            Assert.Equal(5, rig.Choices.DaysLeft(instance, Start + 9), "5 days left");
            Assert.Equal("Осталось 5 из 14 дн.", letter.Status);

            // "Следующий месяц": 31 days in one frame
            rig.World.InBatch = true;
            rig.HoldMail = true;
            rig.Days(Start + 10, Start + 40);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            Assert.Equal(5, rig.Choices.DaysLeft(instance, Start + 40), "after the run: still 5 days");

            rig.World.InBatch = false;
            rig.HoldMail = false;
            rig.Ui.OnBatchEnded();
            Assert.Equal("Осталось 5 из 14 дн.", letter.Status, "the line says the same after the run");

            rig.Days(Start + 41, Start + 44);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            rig.Day(Start + 45);
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "5 days after the run the default choice is applied");
        }

        [Test]
        public void AnInstanceWithoutALetter_ExpiresByItsFiringDate()
        {
            ChoiceRig rig = RigWithVote();
            rig.Mail.Refuse = true;   // the mail service did not accept the letter
            rig.Rig.Fire("es.vote", CompanyKey.Player);
            EventInstanceDto instance = rig.Rig.Instance("es.vote");
            Assert.Equal(0, instance.mailId);

            rig.Days(Start + 1, Start + 13);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            rig.Day(Start + 14);
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "nothing to wait for: the timer counts from the firing");
        }

        [Test]
        public void ASecondAnswerIsIgnored_AndTheEffectsAreAppliedOnce()
        {
            ChoiceRig rig = RigWithVote();
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            int id = instance.id;

            rig.ClickLetter(id, "b");
            Assert.Equal(5f, rig.Rig.Game.Reputation[CompanyKey.Player]);
            Assert.Equal("b", rig.Rig.Entries(EventLogKinds.Resolved)[0].choiceId);
            Assert.Equal("b", rig.Mail.OfInstance(id).Answer);

            Assert.Equal(ChoiceOutcome.Ignored, rig.Choices.Answer(id, "a", Start).Outcome, "the instance does not wait any more");
            rig.Ui.OnLetterAnswered(EventMailFormatter.TagOf(id), "a");
            rig.Ui.OnWindowChoice(id, "a");
            Assert.Equal(5f, rig.Rig.Game.Reputation[CompanyKey.Player], "applied once");
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.Resolved).Count);
            Assert.Equal(0, rig.Notify.CountContaining("автоматически"), "an answer of the player is not announced as automatic");
        }

        [Test]
        public void AnAnswerWithAnUnknownChoice_IsIgnored_AndTheInstanceKeepsWaiting()
        {
            ChoiceRig rig = RigWithVote();
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            Assert.Equal(ChoiceOutcome.Ignored, rig.Choices.Answer(instance.id, "nope", Start).Outcome);
            Assert.Equal(ChoiceOutcome.Ignored, rig.Choices.Answer(9999, "a", Start).Outcome);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
        }

        [Test]
        public void ABankruptTarget_CancelsItsWaitingChoice_WithoutApplyingAnything_AndTheLetterIsClosed()
        {
            ChoiceRig rig = RigWithVote();
            EventInstanceDto instance = rig.FireAndDeliver("es.vote");
            FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);

            rig.World.Gone.Add(CompanyKey.Player);
            rig.Day(Start + 1);
            Assert.Null(rig.Rig.Instance("es.vote"));
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.Cancelled).Count);
            Assert.Equal(0, rig.Rig.Entries(EventLogKinds.Resolved).Count + rig.Rig.Entries(EventLogKinds.AutoResolved).Count);
            Assert.False(rig.Rig.Game.Reputation.ContainsKey(CompanyKey.Player), "no effect of any choice was applied");
            Assert.Equal("a", letter.Answer, "the letter is closed (ModMail.Resolve)");
            Assert.Equal(0, rig.Notify.CountContaining("автоматически"));
        }

        [Test]
        public void AnAiInstanceThatWaits_IsCancelledWhenItsCompanyGoesBankrupt()
        {
            ChoiceRig rig = RigWithVote();
            rig.Rig.Options.DecideForAi = instance => null;   // nobody decides: the instance waits
            rig.Rig.Fire("es.vote", "ai:1");
            Assert.NotNull(rig.Rig.Instance("es.vote"));

            rig.World.Gone.Add("ai:1");
            rig.Day(Start + 1);
            Assert.Null(rig.Rig.Instance("es.vote"));
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.Cancelled).Count);
            Assert.Equal(0, rig.Mail.Letters.Count, "there was no letter for an AI company");
        }

        [Test]
        public void ADeadlineStepThatFails_IsReported_AndTheOtherInstancesStillExpire()
        {
            EventDefinition one = ChoiceEvents.Make("es.one", new[] { Fx.Choice("a"), Fx.Choice("b") });
            EventDefinition two = ChoiceEvents.Make("es.two", new[] { Fx.Choice("a"), Fx.Choice("b") });
            var rig = new ChoiceRig(one, two);
            rig.SetDay(Start);
            rig.FireAndDeliver("es.one");
            rig.FireAndDeliver("es.two");

            rig.Rig.Options.DeadlineDue = (instance, today) =>
            {
                if (instance.eventId == "es.one") throw new System.InvalidOperationException("the deadline check failed on purpose");
                return rig.Choices.DeadlineDue(instance, today);
            };
            rig.Days(Start + 1, Start + 14);
            Assert.True(rig.Rig.Faults.Count > 0, "the failure is reported");
            Assert.Equal(1, rig.Rig.Entries(EventLogKinds.AutoResolved).Count, "the other instance expired");
            Assert.Equal("es.two", rig.Rig.Entries(EventLogKinds.AutoResolved)[0].eventId);
        }
    }
}
