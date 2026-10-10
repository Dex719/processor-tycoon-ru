using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Mail;

namespace PTMod.Tests.Mail
{
    /// <summary>
    /// The pure rules behind the buttons of a mod mail (FR-14 items 3-7, TSK-038): who may answer, what an action does, what a feature's state call
    /// changes (<see cref="MailAnswerRules"/>), which mails a deadline answers and how many days it has left (<see cref="MailTimeoutRules"/>, AC-14.2,
    /// AC-14.8), and the rows of the panel in the open letter (<see cref="MailPanelModel"/>: AC-14.1, AC-14.4, AC-14.5, item 12).
    /// </summary>
    [Covers(typeof(MailAnswerRules), typeof(MailTimeoutRules), typeof(MailPanelModel))]
    public class MailAnswerTests
    {
        private static readonly DateTime Mar1 = new DateTime(1985, 3, 1);
        private static readonly DateTime Mar4 = new DateTime(1985, 3, 4);
        private static readonly DateTime Mar11 = new DateTime(1985, 3, 11);
        private static readonly DateTime Mar14 = new DateTime(1985, 3, 14);

        // ---- helpers ----

        /// <summary>The words as readable markers, so a test sees which key or argument came from where.</summary>
        private sealed class FakeTexts : IMailPanelTexts
        {
            public string Label(string key, LocText inline)
            {
                if (!string.IsNullOrEmpty(key)) return "<" + key + ">";
                return inline == null ? string.Empty : inline.Ru;
            }

            public string Tip(string key) { return "tip:" + key; }

            public string Reason(string key, string[] args)
            {
                return "why:" + key + (args != null && args.Length > 0 ? "(" + string.Join(",", args) + ")" : string.Empty);
            }

            public string DaysLeft(int days) { return "days:" + days; }

            public string Answer(string label, bool afterDeadline) { return (afterDeadline ? "late:" : "answer:") + label; }

            public string FeatureOff(string featureId) { return "off:" + featureId; }
        }

        private static MailRecordModel Offer(int timeoutDays = 10)
        {
            var mail = new MailRecordModel { FeatureId = "smp", Tag = "smp:offer", TitleKey = "t", BodyKey = "b" };
            mail.Choices.Add(new MailChoiceModel { Id = "yes", LabelKey = "core.common.yes" });
            mail.Choices.Add(new MailChoiceModel { Id = "no", LabelKey = "core.common.no" });
            if (timeoutDays > 0)
            {
                mail.TimeoutDays = timeoutDays;
                mail.DefaultChoiceId = "no";
            }
            return mail;
        }

        private static MailRecordModel Delivered(MailRecordModel mail, DateTime day)
        {
            mail.DeliveredOn = day;
            mail.Deadline = mail.TimeoutDays > 0 ? (DateTime?)day.AddDays(mail.TimeoutDays) : null;
            return mail;
        }

        private static MailRecordModel Helpful()
        {
            var mail = new MailRecordModel { FeatureId = "smp", Tag = "smp:help", TitleKey = "t", BodyKey = "b" };
            mail.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "smp.hint" });
            mail.Actions.Add(new MailActionModel { Id = "skip", LabelKey = "smp.skip", Repeatable = false });
            return mail;
        }

        private static List<MailPanelItem> Build(MailRecordModel mail, DateTime today, bool featureActive = true)
        {
            return MailPanelModel.Build(mail, today, featureActive, new FakeTexts());
        }

        private static string Kinds(List<MailPanelItem> items)
        {
            return string.Join(",", items.Select(i => i.Kind.ToString()));
        }

        // ---- answers ----

        [Test]
        public void TryResolve_FixesTheAnswerOnce()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);

            Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryResolve(mail, "yes", MailRecordModel.ResolutionPlayer));
            Assert.Equal("yes", mail.ChoiceId);
            Assert.Equal(MailRecordModel.ResolutionPlayer, mail.Resolution);
            Assert.False(mail.IsPending);

            Assert.Equal(MailAnswerResult.AlreadyAnswered, MailAnswerRules.TryResolve(mail, "no", MailRecordModel.ResolutionPlayer), "AC-14.1: the answer is fixed once");
            Assert.Equal("yes", mail.ChoiceId, "the first answer stays");
        }

        [Test]
        public void TryResolve_RejectsWhatCannotBeAnswered_AndChangesNothing()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);

            Assert.Equal(MailAnswerResult.NoMail, MailAnswerRules.TryResolve(null, "yes", MailRecordModel.ResolutionPlayer));
            Assert.Equal(MailAnswerResult.NoChoice, MailAnswerRules.TryResolve(mail, "maybe", MailRecordModel.ResolutionPlayer));
            Assert.Equal(MailAnswerResult.NoChoice, MailAnswerRules.TryResolve(mail, null, MailRecordModel.ResolutionPlayer));
            Assert.Equal(MailAnswerResult.BadResolution, MailAnswerRules.TryResolve(mail, "yes", MailRecordModel.ResolutionPending));
            Assert.Equal(MailAnswerResult.BadResolution, MailAnswerRules.TryResolve(mail, "yes", "Whatever"));
            Assert.True(mail.IsPending, "nothing was fixed");
            Assert.Null(mail.ChoiceId);
        }

        [Test]
        public void TryResolve_TheDeadlineAndTheFeatureMayAnswerWithADisabledChoice()
        {
            MailRecordModel late = Delivered(Offer(), Mar1);
            late.FindChoice("no").Enabled = false;
            Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryResolve(late, "no", MailRecordModel.ResolutionTimeout), "AC-14.2: the default is applied whatever its state");
            Assert.Equal(MailRecordModel.ResolutionTimeout, late.Resolution);

            MailRecordModel own = Delivered(Offer(), Mar1);
            own.FindChoice("yes").Enabled = false;
            Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryResolve(own, "yes", MailRecordModel.ResolutionProgrammatic));
            Assert.Equal(MailRecordModel.ResolutionProgrammatic, own.Resolution);
        }

        [Test]
        public void IsChoiceActive_NeedsARunningFeature_AnOpenMail_AndAnEnabledChoice()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);
            MailChoiceModel yes = mail.FindChoice("yes");

            Assert.True(MailAnswerRules.IsChoiceActive(mail, yes, true));
            Assert.False(MailAnswerRules.IsChoiceActive(mail, yes, false), "item 12: a feature that is off answers nothing");
            Assert.False(MailAnswerRules.IsChoiceActive(null, yes, true));
            Assert.False(MailAnswerRules.IsChoiceActive(mail, null, true));

            yes.Enabled = false;
            Assert.False(MailAnswerRules.IsChoiceActive(mail, yes, true), "AC-14.5: a disabled choice");

            yes.Enabled = true;
            MailAnswerRules.TryResolve(mail, "no", MailRecordModel.ResolutionPlayer);
            Assert.False(MailAnswerRules.IsChoiceActive(mail, yes, true), "an answered mail has no active choices");
        }

        // ---- actions ----

        [Test]
        public void TryUseAction_ARepeatableActionStaysActive_ANonRepeatableOneGoesOff()
        {
            MailRecordModel mail = Delivered(Helpful(), Mar1);
            MailActionModel action;

            for (int i = 0; i < 3; i++)
                Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryUseAction(mail, "hint", true, out action), "AC-14.4: 'hint' works every time");
            Assert.True(mail.FindAction("hint").Used);
            Assert.True(MailAnswerRules.IsActionActive(mail.FindAction("hint"), true));

            Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryUseAction(mail, "skip", true, out action));
            Assert.False(MailAnswerRules.IsActionActive(mail.FindAction("skip"), true), "'skip' is inactive after the first click");
            Assert.Equal(MailAnswerResult.Inactive, MailAnswerRules.TryUseAction(mail, "skip", true, out action), "and a second click is refused");
            Assert.True(mail.IsPending, "an action never closes the mail");
        }

        [Test]
        public void TryUseAction_IsRefusedForAnUnknownActionAMissingMailAFeatureThatIsOffOrADisabledAction()
        {
            MailRecordModel mail = Delivered(Helpful(), Mar1);
            MailActionModel action;

            Assert.Equal(MailAnswerResult.NoMail, MailAnswerRules.TryUseAction(null, "hint", true, out action));
            Assert.Equal(MailAnswerResult.NoAction, MailAnswerRules.TryUseAction(mail, "zzz", true, out action));
            Assert.Equal(MailAnswerResult.Inactive, MailAnswerRules.TryUseAction(mail, "hint", false, out action));
            Assert.False(mail.FindAction("hint").Used, "a refused click leaves no trace");

            mail.FindAction("hint").Enabled = false;
            Assert.Equal(MailAnswerResult.Inactive, MailAnswerRules.TryUseAction(mail, "hint", true, out action));
        }

        [Test]
        public void ChoicesAndActions_DoNotAffectEachOther()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);
            mail.Actions.Add(new MailActionModel { Id = "log", LabelKey = "smp.log" });
            MailActionModel action;

            MailAnswerRules.TryUseAction(mail, "log", true, out action);
            Assert.True(mail.IsPending, "an action does not answer the mail");
            Assert.True(mail.FindChoice("yes").Enabled);

            MailAnswerRules.TryResolve(mail, "no", MailRecordModel.ResolutionPlayer);
            Assert.True(MailAnswerRules.IsActionActive(mail.FindAction("log"), true), "the action works after the answer");
            Assert.Equal(MailAnswerResult.Ok, MailAnswerRules.TryUseAction(mail, "log", true, out action));
        }

        // ---- states ----

        [Test]
        public void SetChoiceState_DisablesWithAReason_AndEnablingClearsIt()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);

            Assert.True(MailAnswerRules.SetChoiceState(mail, "yes", false, "es.reason.noMoney", new[] { "100" }));
            MailChoiceModel yes = mail.FindChoice("yes");
            Assert.False(yes.Enabled);
            Assert.Equal("es.reason.noMoney", yes.DisabledReasonKey);
            Assert.Equal(new[] { "100" }, yes.DisabledReasonArgs);

            Assert.True(MailAnswerRules.SetChoiceState(mail, "yes", true, "es.reason.noMoney", new[] { "100" }), "AC-14.5: SetChoiceState(true)");
            Assert.True(yes.Enabled);
            Assert.Null(yes.DisabledReasonKey, "a reason belongs to a disabled choice only");
            Assert.Equal(0, yes.DisabledReasonArgs.Length);
            Assert.True(mail.IsPending, "the mail is not closed");

            Assert.False(MailAnswerRules.SetChoiceState(mail, "zzz", true, null, null));
            Assert.False(MailAnswerRules.SetChoiceState(null, "yes", true, null, null));
        }

        [Test]
        public void SetChoiceState_ACopyOfTheArgumentsIsKept()
        {
            MailRecordModel mail = Delivered(Offer(), Mar1);
            var args = new[] { "a" };
            MailAnswerRules.SetChoiceState(mail, "yes", false, "k", args);
            args[0] = "changed";
            Assert.Equal(new[] { "a" }, mail.FindChoice("yes").DisabledReasonArgs, "the caller's array is its own");
        }

        [Test]
        public void SetActionState_ADisabledActionHasAReason_AUsedNonRepeatableOneStaysOff()
        {
            MailRecordModel mail = Delivered(Helpful(), Mar1);
            MailActionModel action;

            Assert.True(MailAnswerRules.SetActionState(mail, "hint", false, "onb.hint.none", null));
            Assert.False(mail.FindAction("hint").Enabled);
            Assert.Equal("onb.hint.none", mail.FindAction("hint").DisabledReasonKey);
            Assert.True(MailAnswerRules.SetActionState(mail, "hint", true, null, null));
            Assert.True(mail.FindAction("hint").Enabled);
            Assert.Null(mail.FindAction("hint").DisabledReasonKey);

            MailAnswerRules.TryUseAction(mail, "skip", true, out action);
            Assert.True(MailAnswerRules.SetActionState(mail, "skip", true, null, null));
            Assert.False(mail.FindAction("skip").Enabled, "AC-14.4: used up for good");
            Assert.False(MailAnswerRules.SetActionState(mail, "zzz", true, null, null));
        }

        // ---- deadlines ----

        [Test]
        public void Due_ReturnsTheExpiredUnansweredMailsThatHaveADefault_InOrder()
        {
            MailRecordModel first = Delivered(Offer(10), Mar1);        // deadline Mar 11
            MailRecordModel second = Delivered(Offer(3), Mar4);        // deadline Mar 7
            MailRecordModel answered = Delivered(Offer(10), Mar1);
            MailAnswerRules.TryResolve(answered, "yes", MailRecordModel.ResolutionPlayer);
            MailRecordModel noPeriod = Delivered(Offer(0), Mar1);
            MailRecordModel noDefault = Delivered(Offer(10), Mar1);
            noDefault.DefaultChoiceId = "gone";
            MailRecordModel queued = Offer(10);                         // not delivered

            var all = new List<MailRecordModel> { first, second, answered, noPeriod, noDefault, queued, null };

            Assert.Equal(0, MailTimeoutRules.Due(all, new DateTime(1985, 3, 6)).Count, "before every deadline");
            Assert.True(MailTimeoutRules.Due(all, new DateTime(1985, 3, 7)).SequenceEqual(new[] { second }), "on a deadline day");
            Assert.True(MailTimeoutRules.Due(all, Mar11).SequenceEqual(new[] { first, second }), "AC-14.2: both, in the order given");
            Assert.True(MailTimeoutRules.Due(all, new DateTime(1990, 1, 1)).SequenceEqual(new[] { first, second }), "caught up much later");
            Assert.Equal(0, MailTimeoutRules.Due(null, Mar11).Count);
        }

        [Test]
        public void Due_TheDeadlineCountsFromTheDeliveryDay_NotFromTheSendingDay()
        {
            // AC-14.8: sent on March 1, held back by the daily limits, delivered on March 4: the deadline is March 14.
            var queue = new MailQueue(200, new MailQuota(3, 2), null);
            var timed = Offer(10);
            timed.FeatureId = "smp";
            queue.Enqueue(timed, Mar1);
            Assert.Equal(0, queue.DeliverDue(Mar1, false, false).Count, "not ready: nothing is delivered");
            Assert.Equal(1, queue.DeliverDue(Mar4, true, false).Count);

            Assert.Equal(Mar14, timed.Deadline.Value, "AC-14.8");
            Assert.Equal(0, MailTimeoutRules.Due(queue.All, Mar11).Count, "ten days after SENDING nothing is due");
            Assert.True(MailTimeoutRules.Due(queue.All, Mar14).SequenceEqual(new[] { timed }), "ten days after DELIVERY the default is due");
        }

        [Test]
        public void CanApplyDefault_NeedsADefaultThatIsOneOfTheChoices()
        {
            Assert.True(MailTimeoutRules.CanApplyDefault(Offer(10)));
            Assert.False(MailTimeoutRules.CanApplyDefault(Offer(0)));
            Assert.False(MailTimeoutRules.CanApplyDefault(null));

            MailRecordModel gone = Offer(10);
            gone.DefaultChoiceId = "gone";
            Assert.False(MailTimeoutRules.CanApplyDefault(gone));
        }

        [Test]
        public void DaysLeft_CountsWholeDays_AndStopsAtZero()
        {
            Assert.Equal(3, MailTimeoutRules.DaysLeft(new DateTime(1985, 3, 13), new DateTime(1985, 3, 10)));
            Assert.Equal(10, MailTimeoutRules.DaysLeft(Mar11, Mar1));
            Assert.Equal(3, MailTimeoutRules.DaysLeft(new DateTime(1985, 3, 13, 18, 0, 0), new DateTime(1985, 3, 10, 7, 30, 0)), "the time of day does not matter");
            Assert.Equal(0, MailTimeoutRules.DaysLeft(Mar11, Mar11), "on the deadline day");
            Assert.Equal(0, MailTimeoutRules.DaysLeft(Mar1, Mar11), "after it");
        }

        // ---- the panel ----

        [Test]
        public void Build_TopToBottom_DeadlineLine_FeatureLine_Actions_Choices()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            mail.Actions.Add(new MailActionModel { Id = "log", LabelKey = "smp.log" });
            mail.StatusLine = "Осталось 3 дня";

            List<MailPanelItem> items = Build(mail, Mar4);

            Assert.Equal("Status,Status,Action,Choice,Choice", Kinds(items));
            Assert.Equal("days:7", items[0].Text, "the period of the core: delivery + 10 - today");
            Assert.Equal("Осталось 3 дня", items[1].Text, "then the feature's own line");
            Assert.Equal("<smp.log>", items[2].Text);
            Assert.Equal("log", items[2].Id);
            Assert.Equal(new[] { "yes", "no" }, items.Where(i => i.Kind == MailPanelKind.Choice).Select(i => i.Id).ToArray());
            Assert.True(items.Where(i => i.Id != null).All(i => i.Enabled));
        }

        [Test]
        public void Build_NoDeadlineLine_WithoutAPeriod_OrOnceAnswered()
        {
            Assert.Equal("Choice,Choice", Kinds(Build(Delivered(Offer(0), Mar1), Mar4)), "no period: no line");

            MailRecordModel mail = Delivered(Offer(10), Mar1);
            MailAnswerRules.TryResolve(mail, "yes", MailRecordModel.ResolutionPlayer);
            Assert.False(Build(mail, Mar4).Any(i => i.Kind == MailPanelKind.Status), "answered: the period no longer matters");
        }

        [Test]
        public void Build_AnAnsweredMail_ShowsTheAnswerInPlaceOfTheChoices_ButKeepsTheActions()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            mail.Actions.Add(new MailActionModel { Id = "log", LabelKey = "smp.log" });

            MailAnswerRules.TryResolve(mail, "yes", MailRecordModel.ResolutionPlayer);
            List<MailPanelItem> byPlayer = Build(mail, Mar4);
            Assert.Equal("Action,Answer", Kinds(byPlayer), "AC-14.1: the buttons are gone, the actions stay");
            Assert.Equal("answer:<core.common.yes>", byPlayer[1].Text);

            MailRecordModel late = Delivered(Offer(10), Mar1);
            MailAnswerRules.TryResolve(late, "no", MailRecordModel.ResolutionTimeout);
            Assert.Equal("late:<core.common.no>", Build(late, Mar14).Single(i => i.Kind == MailPanelKind.Answer).Text, "AC-14.2: 'по истечении срока'");

            MailRecordModel own = Delivered(Offer(10), Mar1);
            MailAnswerRules.TryResolve(own, "no", MailRecordModel.ResolutionProgrammatic);
            Assert.Equal("answer:<core.common.no>", Build(own, Mar4).Single(i => i.Kind == MailPanelKind.Answer).Text);
        }

        [Test]
        public void Build_AMailWithoutChoices_ShowsNoAnswerLine()
        {
            MailRecordModel mail = Delivered(Helpful(), Mar1);
            mail.Resolution = MailRecordModel.ResolutionPlayer;
            mail.ChoiceId = "yes";
            Assert.False(Build(mail, Mar4).Any(i => i.Kind == MailPanelKind.Answer));
        }

        [Test]
        public void Build_AnAnswerThatIsNoLongerOneOfTheChoices_ShowsTheStoredId()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            mail.Resolution = MailRecordModel.ResolutionPlayer;
            mail.ChoiceId = "removed";
            Assert.Equal("answer:removed", Build(mail, Mar4).Single(i => i.Kind == MailPanelKind.Answer).Text);
        }

        [Test]
        public void Build_ADisabledButton_HasItsReasonInTheHintAndInALineUnderIt()
        {
            MailRecordModel mail = Delivered(Offer(0), Mar1);
            MailChoiceModel yes = mail.FindChoice("yes");
            yes.TooltipKey = "es.tip.buy";
            MailAnswerRules.SetChoiceState(mail, "yes", false, "es.reason.noMoney", new[] { "100" });

            List<MailPanelItem> items = Build(mail, Mar1);

            Assert.Equal("Choice,Reason,Choice", Kinds(items), "AC-14.5: the reason right under its button");
            Assert.False(items[0].Enabled);
            Assert.Equal("tip:es.tip.buy\nwhy:es.reason.noMoney(100)", items[0].Tip, "the own tooltip, then the reason");
            Assert.Equal("why:es.reason.noMoney(100)", items[1].Text);
            Assert.True(items[2].Enabled);
            Assert.Equal(string.Empty, items[2].Tip);

            MailAnswerRules.SetChoiceState(mail, "yes", true, null, null);
            items = Build(mail, Mar1);
            Assert.Equal("Choice,Choice", Kinds(items), "AC-14.5: enabled again, no reason");
            Assert.True(items[0].Enabled);
            Assert.Equal("tip:es.tip.buy", items[0].Tip);
        }

        [Test]
        public void Build_ADisabledButtonWithoutAReason_IsGrayAndSaysNothing()
        {
            MailRecordModel mail = Delivered(Offer(0), Mar1);
            mail.FindChoice("yes").Enabled = false;

            List<MailPanelItem> items = Build(mail, Mar1);
            Assert.Equal("Choice,Choice", Kinds(items));
            Assert.False(items[0].Enabled);
            Assert.Equal(string.Empty, items[0].Tip);
        }

        [Test]
        public void Build_AUsedUpAction_IsGrayWithoutAReason_ARepeatableOneStaysOn()
        {
            MailRecordModel mail = Delivered(Helpful(), Mar1);
            MailActionModel action;
            MailAnswerRules.TryUseAction(mail, "skip", true, out action);
            MailAnswerRules.TryUseAction(mail, "hint", true, out action);

            List<MailPanelItem> items = Build(mail, Mar1);
            Assert.Equal("Action,Action", Kinds(items), "AC-14.4");
            Assert.True(items.Single(i => i.Id == "hint").Enabled);
            Assert.False(items.Single(i => i.Id == "skip").Enabled);
            Assert.Equal(string.Empty, items.Single(i => i.Id == "skip").Tip);
        }

        [Test]
        public void Build_AFeatureThatIsOff_GraysEveryButton_WithOneExplanationAboveThem()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            mail.Actions.Add(new MailActionModel { Id = "log", LabelKey = "smp.log" });
            MailAnswerRules.SetChoiceState(mail, "yes", false, "es.reason.noMoney", null);

            List<MailPanelItem> items = Build(mail, Mar4, false);

            Assert.Equal("Status,Note,Action,Choice,Choice", Kinds(items), "item 12: one line, no reason line per button");
            Assert.Equal("off:smp", items[1].Text);
            Assert.True(items.Where(i => i.Id != null).All(i => !i.Enabled));
            Assert.True(items.Where(i => i.Id != null).All(i => i.Tip.Contains("off:smp")), "every button explains itself in its hint");
            Assert.False(items.Any(i => i.Kind == MailPanelKind.Reason));
        }

        [Test]
        public void Build_AFeatureThatIsOff_SaysNothingForAnAnsweredMailWithoutActions()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            MailAnswerRules.TryResolve(mail, "yes", MailRecordModel.ResolutionPlayer);

            Assert.Equal("Answer", Kinds(Build(mail, Mar4, false)));
        }

        [Test]
        public void Build_APlainMail_HasNothingToShow_AndNullsAreTolerated()
        {
            var plain = new MailRecordModel { FeatureId = "smp", TitleKey = "t", BodyKey = "b" };
            Assert.Equal(0, Build(plain, Mar1).Count);
            Assert.Equal(0, MailPanelModel.Build(null, Mar1, true, new FakeTexts()).Count);
            Assert.Equal(0, MailPanelModel.Build(plain, Mar1, true, null).Count);
        }

        [Test]
        public void Build_AButtonWithoutAnyLabel_ShowsItsId()
        {
            var mail = new MailRecordModel { FeatureId = "smp", TitleKey = "t", BodyKey = "b" };
            mail.Choices.Add(new MailChoiceModel { Id = "orphan" });
            Assert.Equal("orphan", Build(mail, Mar1).Single().Text);
        }

        [Test]
        public void Build_AnInlineLabel_IsUsedWhenThereIsNoKey()
        {
            var mail = new MailRecordModel { FeatureId = "smp", TitleKey = "t", BodyKey = "b" };
            mail.Choices.Add(new MailChoiceModel { Id = "a", Label = LocText.Of("Свой текст", "Own text") });
            Assert.Equal("Свой текст", Build(mail, Mar1).Single().Text);
        }

        [Test]
        public void Signature_IsEqualForTheSamePicture_AndChangesWithAnythingTheButtonsShow()
        {
            MailRecordModel mail = Delivered(Offer(10), Mar1);
            string sig = MailPanelModel.Signature(Build(mail, Mar4));

            Assert.Equal(sig, MailPanelModel.Signature(Build(mail, Mar4)), "same state, same picture");
            Assert.NotEqual(sig, MailPanelModel.Signature(Build(mail, Mar4.AddDays(1))), "the days line counts down");

            MailAnswerRules.SetChoiceState(mail, "yes", false, "k", null);
            string disabled = MailPanelModel.Signature(Build(mail, Mar4));
            Assert.NotEqual(sig, disabled, "a button state");

            mail.StatusLine = "x";
            Assert.NotEqual(disabled, MailPanelModel.Signature(Build(mail, Mar4)), "the feature's line");

            Assert.Equal(string.Empty, MailPanelModel.Signature(null));
            Assert.Equal(string.Empty, MailPanelModel.Signature(new List<MailPanelItem>()));
        }

        [Test]
        public void Signature_TellsApartRowsThatOnlyDifferInTheirIds()
        {
            var a = new MailRecordModel { FeatureId = "smp", TitleKey = "t", BodyKey = "b" };
            a.Choices.Add(new MailChoiceModel { Id = "x", LabelKey = "core.common.yes" });
            var b = new MailRecordModel { FeatureId = "smp", TitleKey = "t", BodyKey = "b" };
            b.Choices.Add(new MailChoiceModel { Id = "y", LabelKey = "core.common.yes" });

            Assert.NotEqual(MailPanelModel.Signature(Build(a, Mar1)), MailPanelModel.Signature(Build(b, Mar1)), "a click would answer with another id");
        }
    }
}
