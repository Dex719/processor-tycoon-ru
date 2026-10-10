using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Mail;
using PTMod.Core.Logic.Save;

namespace PTMod.Tests.Mail
{
    /// <summary>
    /// The delivery queue and the mail store (FR-14, TSK-061): delivery order and daily limits (AC-14.6, AC-14.7), the deadline counted from
    /// delivery (AC-14.8), queue overflow, holding while the game is not ready or a batch run is going, Find / FindAll by tag, the checks of a
    /// new mail, and the side file round trip (MailStoreDto / MailRecordDto) including a damaged section.
    /// </summary>
    [Covers(typeof(MailQueue), typeof(MailQuota), typeof(MailRecordModel), typeof(MailChoiceModel), typeof(MailActionModel), typeof(MailPriority))]
    public class MailQueueTests
    {
        private static readonly DateTime Mar1 = new DateTime(1985, 3, 1);
        private static readonly DateTime Mar2 = new DateTime(1985, 3, 2);
        private static readonly DateTime Mar3 = new DateTime(1985, 3, 3);
        private static readonly DateTime Mar4 = new DateTime(1985, 3, 4);

        // ---- helpers ----

        private static MailQueue NewQueue(out List<string> warnings, int limit = MailQueue.DefaultQueueLimit, MailQuota quota = null)
        {
            var sink = new List<string>();
            warnings = sink;
            return new MailQueue(limit, quota, sink.Add);
        }

        private static MailChoiceModel Choice(string id)
        {
            return new MailChoiceModel { Id = id, LabelKey = "core.common." + id };
        }

        private static MailRecordModel Mail(string feature, MailPriority priority = MailPriority.Normal, string tag = null, int timeoutDays = 0)
        {
            var mail = new MailRecordModel
            {
                FeatureId = feature,
                Priority = priority,
                Tag = tag,
                TitleKey = feature + ".mail.title",
                BodyKey = feature + ".mail.body",
            };
            if (timeoutDays > 0)
            {
                mail.TimeoutDays = timeoutDays;
                mail.Choices.Add(Choice("yes"));
                mail.Choices.Add(Choice("no"));
                mail.DefaultChoiceId = "no";
            }
            return mail;
        }

        private static int Send(MailQueue queue, string feature, MailPriority priority = MailPriority.Normal, string tag = null,
            int timeoutDays = 0, DateTime? sentOn = null)
        {
            int id = queue.Enqueue(Mail(feature, priority, tag, timeoutDays), sentOn ?? Mar1);
            Assert.True(id >= MailRecordModel.FirstId, "Send must be accepted");
            return id;
        }

        private static int[] Ids(IReadOnlyList<MailRecordModel> mails)
        {
            return mails.Select(m => m.MailId).ToArray();
        }

        private static bool AnyContains(List<string> texts, string part)
        {
            return texts.Any(t => t.IndexOf(part, StringComparison.Ordinal) >= 0);
        }

        // ---- ids and Enqueue ----

        [Test]
        public void Enqueue_AssignsIdsFromFirstId_AndStampsTheSendDay()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Assert.Equal(MailRecordModel.FirstId, q.NextId);

            int a = q.Enqueue(Mail("fa"), new DateTime(1985, 3, 1, 17, 45, 0));
            int b = q.Enqueue(Mail("fb"), Mar2);

            Assert.Equal(100000, a);
            Assert.Equal(100001, b);
            Assert.Equal(100002, q.NextId);
            Assert.Equal(Mar1, q.Get(a).SentOn, "the time part is cut off");
            Assert.Equal(Mar2, q.Get(b).SentOn);
            Assert.Null(q.Get(a).DeliveredOn);
            Assert.False(q.Get(a).IsDelivered);
            Assert.Equal(2, q.QueuedCount);
            Assert.Equal(0, q.DeliveredCount);
            Assert.Equal(0, w.Count);
        }

        [Test]
        public void Enqueue_RejectsBadMails_WithAWarning_AndKeepsTheCounter()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);

            var noFeature = Mail("x");
            noFeature.FeatureId = null;
            var noTitle = Mail("fa");
            noTitle.TitleKey = null;
            var noBody = Mail("fa");
            noBody.BodyKey = "";
            var badPriority = Mail("fa");
            badPriority.Priority = (MailPriority)7;
            var negativeTimeout = Mail("fa");
            negativeTimeout.TimeoutDays = -1;
            var hugeTimeout = Mail("fa", timeoutDays: 5);
            hugeTimeout.TimeoutDays = MailRecordModel.MaxTimeoutDays + 1;
            var timeoutWithoutDefault = Mail("fa", timeoutDays: 5);
            timeoutWithoutDefault.DefaultChoiceId = null;
            var defaultNotAChoice = Mail("fa", timeoutDays: 5);
            defaultNotAChoice.DefaultChoiceId = "maybe";
            var duplicateChoice = Mail("fa", timeoutDays: 5);
            duplicateChoice.Choices.Add(Choice("yes"));
            var choiceNoLabel = Mail("fa", timeoutDays: 5);
            choiceNoLabel.Choices[0].LabelKey = null;
            var actionNoId = Mail("fa");
            actionNoId.Actions.Add(new MailActionModel { LabelKey = "k" });
            var actionNoLabel = Mail("fa");
            actionNoLabel.Actions.Add(new MailActionModel { Id = "hint" });
            var duplicateAction = Mail("fa");
            duplicateAction.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "k" });
            duplicateAction.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "k" });

            MailRecordModel[] bad =
            {
                null, noFeature, noTitle, noBody, badPriority, negativeTimeout, hugeTimeout, timeoutWithoutDefault, defaultNotAChoice,
                duplicateChoice, choiceNoLabel, actionNoId, actionNoLabel, duplicateAction,
            };
            foreach (MailRecordModel mail in bad)
                Assert.Equal(MailQueue.InvalidId, q.Enqueue(mail, Mar1));

            Assert.Equal(bad.Length, w.Count, "one warning per rejected mail");
            Assert.Equal(0, q.Count);
            Assert.Equal(MailRecordModel.FirstId, q.NextId, "a rejected mail does not use an id");
            Assert.True(AnyContains(w, "TimeoutDays"));
            Assert.True(AnyContains(w, "DefaultChoiceId"));
            Assert.True(AnyContains(w, "no feature id"));
        }

        [Test]
        public void Enqueue_RejectsAMailThatAlreadyHasAnId()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            MailRecordModel mail = Mail("fa");
            int id = q.Enqueue(mail, Mar1);

            Assert.Equal(MailQueue.InvalidId, q.Enqueue(mail, Mar2), "the same record cannot be queued twice");

            Assert.Equal(1, q.Count);
            Assert.Equal(id, mail.MailId);
            Assert.Equal(1, w.Count);
        }

        [Test]
        public void Enqueue_DiscardsDeliveryStateSetByTheCaller()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            MailRecordModel mail = Mail("fa");
            mail.SentOn = new DateTime(2000, 1, 1);

            int id = q.Enqueue(mail, Mar1);

            Assert.Equal(Mar1, q.Get(id).SentOn);
            Assert.Null(q.Get(id).DeliveredOn);
            Assert.Null(q.Get(id).Deadline);
        }

        // ---- delivery order ----

        [Test]
        public void Ac146_CriticalAndThreeNormal_RestOnTheNextDay()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int n1 = Send(q, "fa");
            int n2 = Send(q, "fb");
            int n3 = Send(q, "fc");
            int c = Send(q, "fd", MailPriority.Critical);
            int n4 = Send(q, "fe");
            int n5 = Send(q, "ff");

            IReadOnlyList<MailRecordModel> first = q.DeliverDue(Mar2, true, false);

            Assert.Equal(new[] { c, n1, n2, n3 }, Ids(first), "Critical first, then three Normal in sending order");
            Assert.Equal(2, q.QueuedCount);

            IReadOnlyList<MailRecordModel> second = q.DeliverDue(Mar3, true, false);

            Assert.Equal(new[] { n4, n5 }, Ids(second), "the held mails go next day in sending order");
            Assert.Equal(0, q.QueuedCount);
            Assert.Equal(0, q.DeliverDue(Mar4, true, false).Count);
        }

        [Test]
        public void Ac146_ManyCriticalMails_AreNotLimited()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            var critical = new List<int>();
            for (int i = 0; i < 8; i++) critical.Add(Send(q, "same", MailPriority.Critical));
            int n1 = Send(q, "same");

            IReadOnlyList<MailRecordModel> delivered = q.DeliverDue(Mar2, true, false);

            Assert.Equal(critical.Concat(new[] { n1 }).ToArray(), Ids(delivered), "8 Critical from one feature bypass both limits and do not use the quota");
        }

        [Test]
        public void Ac147_FeatureQuota_OneFeatureFourMails()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int a1 = Send(q, "fa");
            int a2 = Send(q, "fa");
            int a3 = Send(q, "fa");
            int a4 = Send(q, "fa");

            Assert.Equal(new[] { a1, a2 }, Ids(q.DeliverDue(Mar2, true, false)));
            Assert.Equal(new[] { a3, a4 }, Ids(q.DeliverDue(Mar3, true, false)));
        }

        [Test]
        public void Ac147_OtherFeaturesAreNotAffectedByTheQuotaOfOne()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int a1 = Send(q, "fa");
            int a2 = Send(q, "fa");
            int a3 = Send(q, "fa");
            int a4 = Send(q, "fa");
            int b1 = Send(q, "fb");
            int b2 = Send(q, "fb");

            // a3 and a4 wait for their feature's quota, b1 goes ahead of them; the day total (3) then closes the day.
            Assert.Equal(new[] { a1, a2, b1 }, Ids(q.DeliverDue(Mar2, true, false)));
            Assert.Equal(new[] { a3, a4, b2 }, Ids(q.DeliverDue(Mar3, true, false)));
        }

        [Test]
        public void Delivery_PriorityOrder_CriticalNormalLow_SendOrderInside()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, quota: new MailQuota(50, 50));
            int low1 = Send(q, "fa", MailPriority.Low);
            int normal1 = Send(q, "fb");
            int low2 = Send(q, "fc", MailPriority.Low);
            int critical1 = Send(q, "fd", MailPriority.Critical);
            int normal2 = Send(q, "fe");
            int critical2 = Send(q, "ff", MailPriority.Critical);

            Assert.Equal(new[] { critical1, critical2, normal1, normal2, low1, low2 }, Ids(q.DeliverDue(Mar2, true, false)));
        }

        [Test]
        public void Delivery_LowMailsGetOnlyTheRemainingSlots()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int l1 = Send(q, "fa", MailPriority.Low);
            int l2 = Send(q, "fb", MailPriority.Low);
            int l3 = Send(q, "fc", MailPriority.Low);
            int l4 = Send(q, "fd", MailPriority.Low);
            int n1 = Send(q, "fe");
            int n2 = Send(q, "ff");

            Assert.Equal(new[] { n1, n2, l1 }, Ids(q.DeliverDue(Mar2, true, false)), "Normal first, Low takes the one slot left");
            Assert.Equal(new[] { l2, l3, l4 }, Ids(q.DeliverDue(Mar3, true, false)));
        }

        [Test]
        public void Delivery_LowAndNormalOfOneFeatureShareItsQuota()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int n1 = Send(q, "fa");
            int l1 = Send(q, "fa", MailPriority.Low);
            int l2 = Send(q, "fa", MailPriority.Low);
            int other = Send(q, "fb", MailPriority.Low);

            Assert.Equal(new[] { n1, l1, other }, Ids(q.DeliverDue(Mar2, true, false)), "2 per feature across Normal and Low");
            Assert.Equal(new[] { l2 }, Ids(q.DeliverDue(Mar3, true, false)));
        }

        [Test]
        public void Delivery_TwoPassesOnOneDay_ShareTheDaysAllowance()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            for (int i = 0; i < 5; i++) Send(q, "f" + i);

            Assert.Equal(3, q.DeliverDue(Mar2, true, false).Count);
            Assert.Equal(0, q.DeliverDue(Mar2, true, false).Count, "the same day again: nothing more (daily tick, then BatchEnded)");

            int critical = Send(q, "fz", MailPriority.Critical, sentOn: Mar2);
            Assert.Equal(new[] { critical }, Ids(q.DeliverDue(Mar2, true, false)), "Critical still goes");
            Assert.Equal(2, q.DeliverDue(Mar3, true, false).Count, "next day the allowance is back");
        }

        [Test]
        public void Delivery_NothingQueued_ReturnsEmpty()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);

            Assert.NotNull(q.DeliverDue(Mar2, true, false));
            Assert.Equal(0, q.DeliverDue(Mar2, true, false).Count);
        }

        [Test]
        public void Delivery_SetsDeliveredOn_AndKeepsReturnedRecordsInTheStore()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int id = Send(q, "fa", tag: "smp:welcome", sentOn: Mar1);

            IReadOnlyList<MailRecordModel> delivered = q.DeliverDue(new DateTime(1985, 3, 2, 6, 0, 0), true, false);

            Assert.Equal(1, delivered.Count);
            Assert.True(ReferenceEquals(delivered[0], q.Get(id)));
            Assert.Equal(Mar2, delivered[0].DeliveredOn.Value);
            Assert.True(delivered[0].IsDelivered);
            Assert.Equal(1, q.DeliveredCount);
            Assert.Equal(0, q.QueuedCount);
            Assert.Equal(1, q.All.Count, "delivered mails stay in the store");
            Assert.Equal(1, q.GetDelivered().Count);
            Assert.Equal(0, q.GetQueued().Count);
        }

        // ---- deadline ----

        [Test]
        public void Ac148_DeadlineIsCountedFromTheDeliveryDay()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            // Nine mails of other features are ahead of ours: three a day, so ours is delivered on 4 March.
            for (int i = 0; i < 9; i++) Send(q, "other" + i);
            int id = Send(q, "framework-sample", tag: "smp:help", timeoutDays: 10, sentOn: Mar1);

            Assert.Equal(3, q.DeliverDue(Mar1, true, false).Count);
            Assert.Equal(3, q.DeliverDue(Mar2, true, false).Count);
            Assert.Equal(3, q.DeliverDue(Mar3, true, false).Count);
            Assert.Null(q.Get(id).DeliveredOn);
            Assert.Null(q.Get(id).Deadline, "no deadline before delivery");

            IReadOnlyList<MailRecordModel> fourth = q.DeliverDue(Mar4, true, false);

            Assert.Equal(new[] { id }, Ids(fourth));
            MailRecordModel mail = q.Get(id);
            Assert.Equal(Mar1, mail.SentOn);
            Assert.Equal(Mar4, mail.DeliveredOn.Value);
            Assert.Equal(new DateTime(1985, 3, 14), mail.Deadline.Value, "deadline = delivery + 10 days, not sending + 10");
        }

        [Test]
        public void Deadline_WithoutTimeout_IsNull()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int id = Send(q, "fa");

            q.DeliverDue(Mar2, true, false);

            Assert.Null(q.Get(id).Deadline);
            Assert.False(q.Get(id).IsExpired(new DateTime(2000, 1, 1)));
        }

        [Test]
        public void IsExpired_BoundariesAndStates()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int id = Send(q, "fa", timeoutDays: 10, sentOn: Mar1);
            MailRecordModel mail = q.Get(id);

            Assert.False(mail.IsExpired(new DateTime(1985, 4, 1)), "not delivered yet");

            q.DeliverDue(Mar4, true, false);   // deadline 14 March

            Assert.False(mail.IsExpired(new DateTime(1985, 3, 13)));
            Assert.True(mail.IsExpired(new DateTime(1985, 3, 14)), "on the deadline day the default is due");
            Assert.True(mail.IsExpired(new DateTime(1985, 3, 20, 9, 0, 0)));

            mail.Resolution = MailRecordModel.ResolutionPlayer;
            Assert.False(mail.IsExpired(new DateTime(1985, 3, 20)), "an answered mail does not expire");
        }

        // ---- holding ----

        [Test]
        public void Hold_WhileTheGameIsNotReady_NothingIsDelivered_EvenCritical()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int critical = Send(q, "fa", MailPriority.Critical);
            int normal = Send(q, "fb");

            Assert.Equal(0, q.DeliverDue(Mar2, false, false).Count);
            Assert.Equal(0, q.DeliverDue(Mar3, false, true).Count);
            Assert.Equal(2, q.QueuedCount);
            Assert.Null(q.Get(critical).DeliveredOn);

            Assert.Equal(new[] { critical, normal }, Ids(q.DeliverDue(Mar4, true, false)), "after GameReady the held mails go");
            Assert.Equal(Mar4, q.Get(critical).DeliveredOn.Value, "delivery date = the day it really happens");
        }

        [Test]
        public void Hold_DuringABatchRun_NothingIsDelivered_ThenBatchEndedDelivers()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int critical = Send(q, "fa", MailPriority.Critical);
            int n1 = Send(q, "fb");
            int n2 = Send(q, "fc");
            int n3 = Send(q, "fd");
            int n4 = Send(q, "fe");

            // 30 days of a batch month: the daily tick runs, IsBatchRun is true all the time.
            for (int day = 0; day < 30; day++)
                Assert.Equal(0, q.DeliverDue(Mar1.AddDays(day), true, true).Count, "day " + day);
            Assert.Equal(5, q.QueuedCount);

            // BatchEnded: one pass on the last day of the batch, then the usual daily ticks go on.
            DateTime end = Mar1.AddDays(30);
            Assert.Equal(new[] { critical, n1, n2, n3 }, Ids(q.DeliverDue(end, true, false)));
            Assert.Equal(new[] { n4 }, Ids(q.DeliverDue(end.AddDays(1), true, false)));
        }

        [Test]
        public void Hold_DoesNotUseUpTheDaysAllowance()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            for (int i = 0; i < 4; i++) Send(q, "f" + i);

            q.DeliverDue(Mar2, false, false);
            q.DeliverDue(Mar2, true, true);

            Assert.Equal(3, q.DeliverDue(Mar2, true, false).Count, "full allowance after the holds");
        }

        // ---- quota ----

        [Test]
        public void Quota_Defaults_And_Clamping()
        {
            var quota = new MailQuota();
            Assert.Equal(3, quota.MaxPerDay);
            Assert.Equal(2, quota.MaxPerFeature);
            Assert.Equal(MailQuota.DefaultMaxPerDay, quota.MaxPerDay);
            Assert.Equal(MailQuota.DefaultMaxPerFeature, quota.MaxPerFeature);
            Assert.Null(quota.Day);

            quota.Configure(0, -5);
            Assert.Equal(1, quota.MaxPerDay, "a zero limit would hold every mail for ever");
            Assert.Equal(1, quota.MaxPerFeature);

            quota.Configure(5000, 2000);
            Assert.Equal(MailQuota.LimitCeiling, quota.MaxPerDay);
            Assert.Equal(MailQuota.LimitCeiling, quota.MaxPerFeature);

            quota.Configure(4, 3);
            Assert.Equal(4, quota.MaxPerDay);
            Assert.Equal(3, quota.MaxPerFeature);
        }

        [Test]
        public void Quota_CountsPerDayAndFeature_ResetsOnANewDate()
        {
            var quota = new MailQuota();
            quota.StartDay(new DateTime(1985, 3, 1, 14, 0, 0));
            Assert.Equal(Mar1, quota.Day.Value);

            Assert.True(quota.Allows(MailPriority.Normal, "fa"));
            quota.Record(MailPriority.Normal, "fa");
            quota.Record(MailPriority.Low, "fa");

            Assert.Equal(2, quota.DeliveredToday);
            Assert.Equal(2, quota.DeliveredTodayBy("fa"));
            Assert.Equal(0, quota.DeliveredTodayBy("fb"));
            Assert.False(quota.Allows(MailPriority.Normal, "fa"), "feature at its limit");
            Assert.True(quota.Allows(MailPriority.Normal, "fb"));
            Assert.False(quota.IsDayFull);

            quota.Record(MailPriority.Normal, "fb");
            Assert.True(quota.IsDayFull);
            Assert.False(quota.Allows(MailPriority.Low, "fc"), "the day is full");

            quota.StartDay(Mar1.AddHours(23));
            Assert.Equal(3, quota.DeliveredToday, "the same date keeps the counters");

            quota.StartDay(Mar2);
            Assert.Equal(0, quota.DeliveredToday);
            Assert.Equal(0, quota.DeliveredTodayBy("fa"));
            Assert.True(quota.Allows(MailPriority.Normal, "fa"));
        }

        [Test]
        public void Quota_Critical_IsAlwaysAllowed_AndNotCounted()
        {
            var quota = new MailQuota(1, 1);
            quota.StartDay(Mar1);
            quota.Record(MailPriority.Normal, "fa");

            Assert.True(quota.IsDayFull);
            Assert.True(quota.Allows(MailPriority.Critical, "fa"));

            quota.Record(MailPriority.Critical, "fa");
            quota.Record(MailPriority.Critical, null);

            Assert.Equal(1, quota.DeliveredToday);
            Assert.Equal(1, quota.DeliveredTodayBy("fa"));
        }

        [Test]
        public void Quota_Configure_KeepsTheCounters_AndTakesEffectAtOnce()
        {
            var quota = new MailQuota();
            quota.StartDay(Mar1);
            quota.Record(MailPriority.Normal, "fa");
            quota.Record(MailPriority.Normal, "fa");
            Assert.False(quota.Allows(MailPriority.Normal, "fa"));

            quota.Configure(3, 5);
            Assert.True(quota.Allows(MailPriority.Normal, "fa"), "a raised feature limit applies to the counters already made");

            quota.Configure(2, 5);
            Assert.False(quota.Allows(MailPriority.Normal, "fb"), "a lowered day limit closes the day");
            Assert.Equal(2, quota.DeliveredToday);
        }

        [Test]
        public void Quota_Reset_ForgetsTheDay()
        {
            var quota = new MailQuota();
            quota.StartDay(Mar1);
            quota.Record(MailPriority.Normal, "fa");

            quota.Reset();

            Assert.Null(quota.Day);
            Assert.Equal(0, quota.DeliveredToday);
            Assert.Equal(0, quota.DeliveredTodayBy("fa"));
        }

        [Test]
        public void Quota_NullFeatureId_IsACounterOfItsOwn()
        {
            var quota = new MailQuota();
            quota.StartDay(Mar1);
            quota.Record(MailPriority.Normal, null);
            quota.Record(MailPriority.Normal, "");

            Assert.Equal(2, quota.DeliveredTodayBy(null));
            Assert.Equal(2, quota.DeliveredTodayBy(""));
        }

        [Test]
        public void ConfiguredQuota_ChangesWhatTheQueueDelivers()
        {
            List<string> w;
            var quota = new MailQuota(5, 1);
            MailQueue q = NewQueue(out w, quota: quota);
            Assert.True(ReferenceEquals(quota, q.Quota));
            int a1 = Send(q, "fa");
            int a2 = Send(q, "fa");
            int b1 = Send(q, "fb");

            Assert.Equal(new[] { a1, b1 }, Ids(q.DeliverDue(Mar2, true, false)), "one per feature");

            quota.Configure(5, 5);
            Assert.Equal(new[] { a2 }, Ids(q.DeliverDue(Mar2, true, false)), "raised limit applies the same day");
        }

        // ---- overflow ----

        [Test]
        public void Overflow_DropsTheOldestLow_ThenTheOldestNormal_NeverCritical()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 5);
            int low1 = Send(q, "fa", MailPriority.Low);
            int n1 = Send(q, "fb");
            int n2 = Send(q, "fc");
            int n3 = Send(q, "fd");
            int n4 = Send(q, "fe");
            Assert.Equal(5, q.QueuedCount);
            Assert.Equal(0, w.Count);

            int n5 = Send(q, "ff");   // 6 > 5: the only Low goes
            Assert.Null(q.Get(low1));
            Assert.Equal(5, q.QueuedCount);
            Assert.Equal(1, w.Count);
            Assert.True(AnyContains(w, "feature \"fa\""), "the log names the feature");
            Assert.True(AnyContains(w, "Low"));
            Assert.True(AnyContains(w, low1.ToString()));

            int n6 = Send(q, "fg");   // no Low left: the oldest Normal goes
            Assert.Null(q.Get(n1));
            Assert.True(AnyContains(w, "feature \"fb\""));

            int c1 = Send(q, "fh", MailPriority.Critical);   // a Critical arrives, the oldest Normal pays
            Assert.Null(q.Get(n2));
            Assert.NotNull(q.Get(c1));
            Assert.Equal(new[] { n3, n4, n5, n6, c1 }, Ids(q.GetQueued()));
            Assert.Equal(3, w.Count);
        }

        [Test]
        public void Overflow_AnOlderLowGoesBeforeANewerLow_AndLowBeforeAnyNormal()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 3);
            int n1 = Send(q, "fa");
            int l1 = Send(q, "fb", MailPriority.Low);
            int l2 = Send(q, "fc", MailPriority.Low);

            int n2 = Send(q, "fd");   // 4 > 3: the older Low (l1), although n1 is older than both

            Assert.Equal(new[] { n1, l2, n2 }, Ids(q.GetQueued()));
            Assert.Null(q.Get(l1));
        }

        [Test]
        public void Overflow_CriticalMailsAreNeverDropped_EvenBeyondTheLimit()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 3);
            var ids = new List<int>();
            for (int i = 0; i < 6; i++) ids.Add(Send(q, "f" + i, MailPriority.Critical));

            Assert.Equal(6, q.QueuedCount, "a queue of Critical mails alone may exceed the limit");
            Assert.Equal(ids.ToArray(), Ids(q.GetQueued()));
            Assert.Equal(0, w.Count);
        }

        [Test]
        public void Overflow_ANewMailThatIsTheOldestOfItsKind_IsDroppedItself()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 3);
            int c1 = Send(q, "fa", MailPriority.Critical);
            int c2 = Send(q, "fa", MailPriority.Critical);
            int c3 = Send(q, "fa", MailPriority.Critical);

            int id = q.Enqueue(Mail("fnew"), Mar1);

            Assert.Equal(MailQueue.InvalidId, id, "no Low or Normal is older, so the new Normal is the one dropped");
            Assert.Equal(new[] { c1, c2, c3 }, Ids(q.GetQueued()));
            Assert.Equal(1, w.Count);
            Assert.True(AnyContains(w, "feature \"fnew\""));
            Assert.Equal(c3 + 2, q.NextId, "the id was used up");

            // A Low arriving into a queue of Normal mails: the Low is the only Low, so it goes itself.
            MailQueue q2 = NewQueue(out w, 2);
            int n1 = Send(q2, "fa");
            int n2 = Send(q2, "fb");
            Assert.Equal(MailQueue.InvalidId, q2.Enqueue(Mail("fc", MailPriority.Low), Mar1));
            Assert.Equal(new[] { n1, n2 }, Ids(q2.GetQueued()));
        }

        [Test]
        public void Overflow_OnlyTheQueueCounts_NotDeliveredMails()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 2);
            Send(q, "fa");
            Send(q, "fb");
            q.DeliverDue(Mar2, true, false);
            Assert.Equal(0, q.QueuedCount);

            int n3 = Send(q, "fc", sentOn: Mar2);
            int n4 = Send(q, "fd", sentOn: Mar2);

            Assert.Equal(4, q.Count);
            Assert.Equal(new[] { n3, n4 }, Ids(q.GetQueued()));
            Assert.Equal(0, w.Count);
        }

        [Test]
        public void SetQueueLimit_CutsALongerQueueAtOnce()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 10);
            int l1 = Send(q, "fa", MailPriority.Low);
            int l2 = Send(q, "fa", MailPriority.Low);
            int n1 = Send(q, "fb");
            int n2 = Send(q, "fb");
            int c1 = Send(q, "fc", MailPriority.Critical);

            q.SetQueueLimit(2);

            Assert.Equal(2, q.QueueLimit);
            Assert.Equal(new[] { n2, c1 }, Ids(q.GetQueued()), "both Low and the oldest Normal are gone");
            Assert.Equal(3, w.Count);
            Assert.Null(q.Get(l1));
            Assert.Null(q.Get(l2));
            Assert.Null(q.Get(n1));

            q.SetQueueLimit(0);
            Assert.Equal(1, q.QueueLimit, "the limit is at least 1");
            q.SetQueueLimit(1000000);
            Assert.Equal(10000, q.QueueLimit);
        }

        [Test]
        public void Overflow_StateStaysConsistent_AfterManyDropsAndDeliveries()
        {
            List<string> w;
            MailQueue q = NewQueue(out w, 6);
            for (int i = 0; i < 40; i++)
            {
                MailPriority priority = i % 7 == 0 ? MailPriority.Critical : (i % 3 == 0 ? MailPriority.Low : MailPriority.Normal);
                q.Enqueue(Mail("f" + (i % 5), priority), Mar1.AddDays(i / 4));
                if (i % 5 == 4) q.DeliverDue(Mar1.AddDays(i / 4), true, false);
            }

            Assert.Equal(q.Count, q.QueuedCount + q.DeliveredCount);
            Assert.Equal(q.QueuedCount, q.GetQueued().Count);
            Assert.Equal(q.DeliveredCount, q.GetDelivered().Count);
            Assert.True(q.GetQueued().Count(m => m.Priority != MailPriority.Critical) <= 6);
            int[] all = Ids(q.All);
            Assert.Equal(all.OrderBy(x => x).ToArray(), all, "the store stays in id order");
            Assert.Equal(all.Length, all.Distinct().Count());
            foreach (MailRecordModel m in q.All) Assert.True(ReferenceEquals(m, q.Get(m.MailId)));
        }

        // ---- lookup ----

        [Test]
        public void Find_ReturnsTheLatestMailWithTheExactTag_DeliveredOrQueued()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int first = Send(q, "onb", tag: "onb:step1");
            Send(q, "onb", tag: "onb:step10");
            int second = Send(q, "onb", tag: "onb:step1");
            Send(q, "other", tag: "onb:step1");
            q.DeliverDue(Mar2, true, false);   // delivers first, step10 and "other"; the second step1 waits for its feature's quota

            Assert.Equal(second, q.Find("onb", "onb:step1").MailId, "the latest one, not the first");
            Assert.NotEqual(first, q.Find("onb", "onb:step1").MailId);
            Assert.False(q.Find("onb", "onb:step1").IsDelivered, "a queued mail is found as well");
            Assert.True(q.Find("other", "onb:step1").IsDelivered, "and a delivered one");
        }

        [Test]
        public void Find_NoMatch_ReturnsNull()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "onb", tag: "onb:step1");
            Send(q, "onb");

            Assert.Null(q.Find("onb", "onb:step"), "the tag must match exactly, not as a prefix");
            Assert.Null(q.Find("onb", "ONB:step1"), "case-sensitive");
            Assert.Null(q.Find("onb", "onb:step2"));
            Assert.Null(q.Find("nobody", "onb:step1"), "the feature is part of the key");
            Assert.Null(q.Find("onb", null));
            Assert.Null(q.Find("onb", ""));
            Assert.Null(q.Find(null, "onb:step1"));
            Assert.Null(q.Find("", "onb:step1"));
        }

        [Test]
        public void FindAll_ByFeature_AndTagPrefix_InSendingOrder()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int s1 = Send(q, "scn", tag: "scn:briefing:r1");
            Send(q, "other", tag: "scn:briefing:r9");
            int s2 = Send(q, "scn", tag: "scn:result:r1");
            int s3 = Send(q, "scn", tag: "scn:briefing:r2");
            int s4 = Send(q, "scn");
            q.DeliverDue(Mar2, true, false);

            Assert.Equal(new[] { s1, s3 }, Ids(q.FindAll("scn", "scn:briefing:")));
            Assert.Equal(new[] { s1, s2, s3 }, Ids(q.FindAll("scn", "scn:")));
            Assert.Equal(new[] { s1, s2, s3, s4 }, Ids(q.FindAll("scn")), "no prefix: all of the feature, tagged or not");
            Assert.Equal(new[] { s1, s2, s3, s4 }, Ids(q.FindAll("scn", "")));
            Assert.Equal(new[] { s1 }, Ids(q.FindAll("scn", "scn:briefing:r1")));
            Assert.Equal(1, q.FindAll("other", "scn:").Count);
            Assert.Equal(new[] { s1, s2, s3 }, Ids(q.FindAll("scn", "scn:")), "delivered and queued alike (s3 is queued here)");
        }

        [Test]
        public void FindAll_NothingFound_IsAnEmptyListNotNull()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "scn", tag: "scn:a");

            Assert.NotNull(q.FindAll("scn", "xyz:"));
            Assert.Equal(0, q.FindAll("scn", "xyz:").Count);
            Assert.Equal(0, q.FindAll("nobody").Count);
            Assert.Equal(0, q.FindAll(null).Count);
            Assert.Equal(0, q.FindAll("").Count);
            Assert.Equal(0, q.FindAll("scn", "SCN:").Count, "case-sensitive");
        }

        [Test]
        public void FindAll_ReturnsASnapshot()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "scn", tag: "scn:a");
            IReadOnlyList<MailRecordModel> found = q.FindAll("scn");

            Send(q, "scn", tag: "scn:b");

            Assert.Equal(1, found.Count);
            Assert.Equal(2, q.FindAll("scn").Count);
        }

        [Test]
        public void Get_UnknownId_IsNull_AndAllIsInIdOrder()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int a = Send(q, "fa");
            int b = Send(q, "fb", MailPriority.Critical);

            Assert.Null(q.Get(0));
            Assert.Null(q.Get(-1));
            Assert.Null(q.Get(99999));
            Assert.Null(q.Get(b + 1));
            Assert.Equal(new[] { a, b }, Ids(q.All));
            Assert.Equal(2, q.Count);
        }

        // ---- record model ----

        [Test]
        public void RecordModel_FindChoiceAndAction()
        {
            MailRecordModel mail = Mail("fa", timeoutDays: 3);
            mail.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "k" });

            Assert.Equal("yes", mail.FindChoice("yes").Id);
            Assert.Null(mail.FindChoice("maybe"));
            Assert.Null(mail.FindChoice(null));
            Assert.Equal("hint", mail.FindAction("hint").Id);
            Assert.Null(mail.FindAction("yes"));
            Assert.Null(mail.FindAction(""));
        }

        [Test]
        public void RecordModel_Defaults()
        {
            var mail = new MailRecordModel();

            Assert.Equal(MailPriority.Normal, mail.Priority);
            Assert.Equal(MailRecordModel.ResolutionPending, mail.Resolution);
            Assert.True(mail.IsPending);
            Assert.NotNull(mail.Args);
            Assert.Equal(0, mail.Args.Length);
            Assert.Equal(0, mail.Choices.Count);
            Assert.Equal(0, mail.Actions.Count);
            Assert.Equal(0, mail.MailId);
            Assert.False(mail.Read);

            mail.Args = null;
            Assert.NotNull(mail.Args);
            var choice = new MailChoiceModel();
            Assert.True(choice.Enabled);
            Assert.NotNull(choice.DisabledReasonArgs);
            var action = new MailActionModel();
            Assert.True(action.Enabled);
            Assert.True(action.Repeatable);
            Assert.False(action.Used);
            Assert.False(action.HasLabel);
            action.Label = LocText.Of("Подсказки", "Hints");
            Assert.True(action.HasLabel);
        }

        [Test]
        public void MailPriority_Values()
        {
            Assert.Equal(0, (int)MailPriority.Low);
            Assert.Equal(1, (int)MailPriority.Normal);
            Assert.Equal(2, (int)MailPriority.Critical);
        }

        // ---- side file: round trip ----

        private static MailStoreDto ThroughJson(MailStoreDto dto)
        {
            var errors = new List<string>();
            JsonNode node = ModJson.From(dto, errors);
            Assert.Equal(0, errors.Count, "From: " + string.Join("; ", errors));
            string text = ModJson.Write(node, false);

            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out parsed, out error), "the written JSON must parse");
            MailStoreDto back = ModJson.Bind<MailStoreDto>(parsed, errors);
            Assert.Equal(0, errors.Count, "Bind: " + string.Join("; ", errors));
            return back;
        }

        private static string JsonOf(MailStoreDto dto)
        {
            return ModJson.Write(ModJson.From(dto), false);
        }

        /// <summary>A store like the example of the side file: two delivered mails (one answered, one with actions) and two queued ones.</summary>
        private static MailQueue BuildRichQueue(out List<string> warnings, out int welcome, out int help, out int digest, out int timed)
        {
            MailQueue q = NewQueue(out warnings);

            MailRecordModel w = Mail("framework-sample", MailPriority.Critical, "smp:welcome", timeoutDays: 10);
            w.Args = new[] { "JAT" };
            w.Choices[0].TooltipKey = "smp.tip.yes";
            welcome = q.Enqueue(w, new DateTime(1985, 1, 2));
            q.DeliverDue(new DateTime(1985, 1, 2), true, false);
            w.Read = true;
            w.ChoiceId = "yes";
            w.Resolution = MailRecordModel.ResolutionPlayer;

            MailRecordModel h = Mail("framework-sample", MailPriority.Normal, "smp:help");
            h.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "smp.mail.hint", Repeatable = true });
            h.Actions.Add(new MailActionModel
            {
                Id = "skip",
                Label = LocText.Of("Пропустить шаг", "Skip the step"),
                Repeatable = false,
                Used = true,
                Enabled = false,
                DisabledReasonKey = "smp.reason.used",
                DisabledReasonArgs = new[] { "1", "x" },
            });
            h.StatusLine = "Осталось 3 дня";
            help = q.Enqueue(h, new DateTime(1985, 2, 10));
            q.DeliverDue(new DateTime(1985, 2, 11), true, false);

            MailRecordModel d = Mail("framework-sample", MailPriority.Low, "smp:digest");
            d.TitleKey = null;
            d.BodyKey = null;
            d.Title = LocText.Of("Дайджест", "Digest");
            d.Body = new LocText("smp.mail.digestBody", "Тело", "Body");
            digest = q.Enqueue(d, new DateTime(1985, 2, 28));

            MailRecordModel t = Mail("event-system", MailPriority.Normal, "evt:offer", timeoutDays: 7);
            t.Choices[0].Enabled = false;
            t.Choices[0].DisabledReasonKey = "es.reason.noMoney";
            t.Choices[0].DisabledReasonArgs = new[] { "100" };
            t.Choices[1].Label = LocText.FromKey("es.no");
            timed = q.Enqueue(t, new DateTime(1985, 2, 28));
            return q;
        }

        [Test]
        public void Dto_Shape_OfDeliveredAndQueuedMails()
        {
            List<string> w;
            int welcome, help, digest, timed;
            MailQueue q = BuildRichQueue(out w, out welcome, out help, out digest, out timed);

            MailStoreDto dto = q.ToDto();

            Assert.Equal(q.NextId, dto.nextId);
            Assert.Equal(timed + 1, dto.nextId);
            Assert.Equal(new[] { welcome, help }, dto.mails.Select(m => m.id).ToArray());
            Assert.Equal(new[] { digest, timed }, dto.queue.Select(m => m.id).ToArray());

            MailRecordDto a = dto.mails[0];
            Assert.Equal("framework-sample", a.feature);
            Assert.Equal("smp:welcome", a.tag);
            Assert.Equal("Critical", a.priority);
            Assert.Equal("1985-01-02", a.sent);
            Assert.Equal("1985-01-02", a.delivered);
            Assert.Equal("1985-01-12", a.deadline, "delivery + 10 days");
            Assert.Equal("yes", a.choice);
            Assert.Equal("Player", a.resolution);
            Assert.Equal("no", a.defaultChoice);
            Assert.True(a.read);
            Assert.Equal(new[] { "JAT" }, a.args);
            Assert.Equal("framework-sample.mail.title", a.titleKey);
            Assert.Null(a.title);
            Assert.Equal(2, a.choices.Count);
            Assert.Equal("smp.tip.yes", a.choices[0].tooltipKey);
            Assert.Null(a.actions);

            MailRecordDto b = dto.mails[1];
            Assert.Equal("1985-02-10", b.sent);
            Assert.Equal("1985-02-11", b.delivered);
            Assert.Null(b.deadline);
            Assert.Equal("Осталось 3 дня", b.status);
            Assert.Equal("Pending", b.resolution);
            Assert.False(b.read);
            Assert.Null(b.choices);
            Assert.Equal(2, b.actions.Count);
            Assert.True(b.actions[0].repeatable);
            Assert.False(b.actions[1].repeatable);
            Assert.True(b.actions[1].used);
            Assert.False(b.actions[1].enabled);
            Assert.Equal("smp.reason.used", b.actions[1].disabledReasonKey);
            Assert.Equal(new[] { "1", "x" }, b.actions[1].disabledReasonArgs);
            Assert.Equal("Пропустить шаг", b.actions[1].label.ru);
            Assert.Equal("Skip the step", b.actions[1].label.en);

            MailRecordDto c = dto.queue[0];
            Assert.Null(c.delivered, "a queued mail has no delivery date");
            Assert.Null(c.deadline);
            Assert.Equal("Low", c.priority);
            Assert.Null(c.titleKey);
            Assert.Equal("Дайджест", c.title.ru);
            Assert.Equal("Digest", c.title.en);
            Assert.Null(c.title.key);
            Assert.Equal("smp.mail.digestBody", c.body.key);

            MailRecordDto e = dto.queue[1];
            Assert.Null(e.delivered);
            Assert.Equal("1985-03-07", e.deadline, "a queued mail carries sent + timeout (28 Feb + 7 days) so that the timeout survives the file");
            Assert.False(e.choices[0].enabled);
            Assert.Equal("es.reason.noMoney", e.choices[0].disabledReasonKey);
            Assert.Equal("es.no", e.choices[1].label.key);
        }

        [Test]
        public void Dto_RoundTripThroughJson_RestoresTheSameStore()
        {
            List<string> w;
            int welcome, help, digest, timed;
            MailQueue q = BuildRichQueue(out w, out welcome, out help, out digest, out timed);
            string before = JsonOf(q.ToDto());

            MailStoreDto viaJson = ThroughJson(q.ToDto());
            var warnings = new List<string>();
            var restored = new MailQueue(MailQueue.DefaultQueueLimit, null, warnings.Add);
            restored.Load(viaJson);

            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
            Assert.Equal(before, JsonOf(restored.ToDto()), "writing the restored store gives the same section");
            Assert.Equal(q.NextId, restored.NextId);
            Assert.Equal(4, restored.Count);
            Assert.Equal(2, restored.QueuedCount);
            Assert.Equal(new[] { welcome, help, digest, timed }, Ids(restored.All));

            MailRecordModel a = restored.Get(welcome);
            Assert.Equal(new DateTime(1985, 1, 2), a.DeliveredOn.Value);
            Assert.Equal(new DateTime(1985, 1, 12), a.Deadline.Value);
            Assert.Equal(10, a.TimeoutDays);
            Assert.Equal("yes", a.ChoiceId);
            Assert.Equal(MailRecordModel.ResolutionPlayer, a.Resolution);
            Assert.True(a.Read);
            Assert.Equal(MailPriority.Critical, a.Priority);
            Assert.Equal(new[] { "JAT" }, a.Args);
            Assert.Equal("smp.tip.yes", a.FindChoice("yes").TooltipKey);

            MailRecordModel b = restored.Get(help);
            Assert.Equal("Осталось 3 дня", b.StatusLine);
            Assert.False(b.FindAction("skip").Repeatable);
            Assert.True(b.FindAction("skip").Used);
            Assert.False(b.FindAction("skip").Enabled);
            Assert.Equal(new[] { "1", "x" }, b.FindAction("skip").DisabledReasonArgs);
            Assert.Equal("Skip the step", b.FindAction("skip").Label.En);

            MailRecordModel d = restored.Get(digest);
            Assert.False(d.IsDelivered);
            Assert.Equal("Дайджест", d.Title.Ru);
            Assert.Equal("smp.mail.digestBody", d.Body.Key);
            Assert.Equal("Body", d.Body.En);
        }

        [Test]
        public void Dto_RoundTrip_KeepsTheTimeoutOfAQueuedMail_DeadlineComesAtDelivery()
        {
            List<string> w;
            int welcome, help, digest, timed;
            MailQueue q = BuildRichQueue(out w, out welcome, out help, out digest, out timed);
            var restored = new MailQueue();
            restored.Load(ThroughJson(q.ToDto()));

            MailRecordModel mail = restored.Get(timed);
            Assert.Equal(7, mail.TimeoutDays);
            Assert.Null(mail.Deadline, "no deadline before delivery");
            Assert.Equal(new DateTime(1985, 2, 28), mail.SentOn);

            restored.DeliverDue(Mar4, true, false);

            Assert.Equal(Mar4, mail.DeliveredOn.Value);
            Assert.Equal(new DateTime(1985, 3, 11), mail.Deadline.Value, "7 days from delivery, as for a mail that never left the game");
        }

        [Test]
        public void Dto_RoundTrip_OfAnEmptyStore()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);

            MailStoreDto dto = q.ToDto();
            Assert.Equal(MailRecordModel.FirstId, dto.nextId);
            Assert.NotNull(dto.mails);
            Assert.NotNull(dto.queue);
            Assert.Equal(0, dto.mails.Count);
            Assert.Equal(0, dto.queue.Count);

            var restored = new MailQueue();
            restored.Load(ThroughJson(dto));
            Assert.Equal(0, restored.Count);
            Assert.Equal(MailRecordModel.FirstId, restored.NextId);
        }

        [Test]
        public void Dto_PriorityNames_RoundTrip()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            int low = Send(q, "fa", MailPriority.Low);
            int normal = Send(q, "fa", MailPriority.Normal);
            int critical = Send(q, "fa", MailPriority.Critical);

            MailStoreDto dto = q.ToDto();
            Assert.Equal(new[] { "Low", "Normal", "Critical" }, dto.queue.Select(m => m.priority).ToArray());

            var restored = new MailQueue();
            restored.Load(ThroughJson(dto));
            Assert.Equal(MailPriority.Low, restored.Get(low).Priority);
            Assert.Equal(MailPriority.Normal, restored.Get(normal).Priority);
            Assert.Equal(MailPriority.Critical, restored.Get(critical).Priority);
        }

        [Test]
        public void Load_ReplacesWhatWasThere_AndNullMeansEmpty()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "fa");
            Send(q, "fb");
            q.DeliverDue(Mar2, true, false);

            q.Load(null);

            Assert.Equal(0, q.Count);
            Assert.Equal(0, q.QueuedCount);
            Assert.Equal(MailRecordModel.FirstId, q.NextId);
            Assert.Null(q.Quota.Day);
            Assert.Equal(0, w.Count);

            q.Load(new MailStoreDto());
            Assert.Equal(0, q.Count);
            Assert.Equal(MailRecordModel.FirstId, q.NextId, "missing lists and a zero nextId are fine");
        }

        [Test]
        public void Clear_ForgetsMailsAndCounters_KeepsTheLimits()
        {
            List<string> w;
            var quota = new MailQuota(4, 3);
            MailQueue q = NewQueue(out w, 50, quota);
            Send(q, "fa");
            q.DeliverDue(Mar2, true, false);

            q.Clear();

            Assert.Equal(0, q.Count);
            Assert.Equal(MailRecordModel.FirstId, q.NextId);
            Assert.Null(quota.Day);
            Assert.Equal(4, quota.MaxPerDay);
            Assert.Equal(50, q.QueueLimit);
            Assert.Equal(MailRecordModel.FirstId, Send(q, "fa"), "numbering starts again");
        }

        // ---- side file: damaged data ----

        private static MailRecordDto Rec(int id, string feature = "fa", string sent = "1985-03-01", string delivered = null, string priority = "Normal",
            string tag = "fa:x")
        {
            return new MailRecordDto { id = id, feature = feature, tag = tag, titleKey = "t", bodyKey = "b", sent = sent, delivered = delivered, priority = priority };
        }

        [Test]
        public void Load_DropsRecordsThatBreakTheRules_AndReadsTheRest()
        {
            var warnings = new List<string>();
            var q = new MailQueue(MailQueue.DefaultQueueLimit, null, warnings.Add);
            var dto = new MailStoreDto
            {
                nextId = 100100,
                mails = new List<MailRecordDto>
                {
                    Rec(100001, delivered: "1985-03-02"),                          // fine
                    Rec(99999, delivered: "1985-03-02"),                           // id below 100000
                    Rec(0, delivered: "1985-03-02"),                               // the Year Summary id
                    Rec(100002, sent: "1985-3-1", delivered: "1985-03-02"),        // not yyyy-MM-dd
                    Rec(100003, delivered: "03/02/1985"),                          // delivery date unreadable
                    Rec(100004),                                                   // in 'mails' without 'delivered'
                    Rec(100005, feature: null, delivered: "1985-03-02"),           // no feature
                    Rec(100001, delivered: "1985-03-05"),                          // id used twice
                    null,
                },
                queue = new List<MailRecordDto>
                {
                    Rec(100010),                                                   // fine
                    Rec(100011, delivered: "1985-03-02"),                          // in 'queue' with 'delivered'
                    Rec(100012, sent: ""),                                         // no sending date
                    Rec(100001),                                                   // id of a delivered mail
                },
            };

            q.Load(dto);

            Assert.Equal(new[] { 100001, 100010 }, Ids(q.All), "only the two good records survive");
            Assert.True(q.Get(100001).IsDelivered);
            Assert.Equal(new DateTime(1985, 3, 2), q.Get(100001).DeliveredOn.Value, "the first record with the id wins");
            Assert.False(q.Get(100010).IsDelivered);
            Assert.Equal(1, q.QueuedCount);
            Assert.Equal(11, warnings.Count, "one warning per dropped record (8 in mails, 3 in queue)");
            Assert.True(AnyContains(warnings, "99999"));
            Assert.True(AnyContains(warnings, "used twice"));
            Assert.True(AnyContains(warnings, "'sent'"));
            Assert.True(AnyContains(warnings, "'delivered'"));
            Assert.Equal(100100, q.NextId);
        }

        [Test]
        public void Load_RaisesNextIdAboveEveryId_AndAboveTheFirstId()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            var dto = new MailStoreDto { nextId = 5, mails = new List<MailRecordDto> { Rec(100050, delivered: "1985-03-02") } };

            q.Load(dto);
            Assert.Equal(100051, q.NextId, "a stale counter must not hand out a used id");

            q.Load(new MailStoreDto { nextId = 7 });
            Assert.Equal(MailRecordModel.FirstId, q.NextId);

            q.Load(new MailStoreDto { nextId = -3 });
            Assert.Equal(MailRecordModel.FirstId, q.NextId);

            int fresh = q.Enqueue(Mail("fa"), Mar1);
            Assert.Equal(MailRecordModel.FirstId, fresh);
        }

        [Test]
        public void Load_RepairsSoftProblems_WithWarnings()
        {
            var warnings = new List<string>();
            var q = new MailQueue(MailQueue.DefaultQueueLimit, null, warnings.Add);
            MailRecordDto odd = Rec(100001, priority: "Urgent");
            odd.resolution = "Weird";
            odd.choices = new List<MailChoiceDto>
            {
                new MailChoiceDto { id = "yes", labelKey = "k" },
                new MailChoiceDto { id = "", labelKey = "k" },
                new MailChoiceDto { id = "yes", labelKey = "other" },
                null,
            };
            odd.actions = new List<MailActionDto>
            {
                new MailActionDto { id = "hint", labelKey = "k" },
                new MailActionDto { id = "hint", labelKey = "k" },
            };
            MailRecordDto noPriority = Rec(100002, priority: null);
            MailRecordDto lower = Rec(100003, priority: "critical");
            lower.resolution = "timeout";

            q.Load(new MailStoreDto { queue = new List<MailRecordDto> { odd, noPriority, lower } });

            MailRecordModel m = q.Get(100001);
            Assert.NotNull(m);
            Assert.Equal(MailPriority.Normal, m.Priority);
            Assert.Equal(MailRecordModel.ResolutionPending, m.Resolution);
            Assert.Equal(1, m.Choices.Count, "no id, repeated id and null choices are skipped");
            Assert.Equal("k", m.Choices[0].LabelKey);
            Assert.Equal(1, m.Actions.Count);
            Assert.Equal(MailPriority.Normal, q.Get(100002).Priority, "no priority: Normal, quietly");
            Assert.Equal(MailPriority.Critical, q.Get(100003).Priority, "names are not case-sensitive");
            Assert.Equal(MailRecordModel.ResolutionTimeout, q.Get(100003).Resolution);
            Assert.Equal(6, warnings.Count, string.Join("; ", warnings));
            Assert.True(AnyContains(warnings, "Urgent"));
            Assert.True(AnyContains(warnings, "Weird"));
        }

        [Test]
        public void Load_NumericPriorityText_IsNotAPriority()
        {
            var warnings = new List<string>();
            var q = new MailQueue(MailQueue.DefaultQueueLimit, null, warnings.Add);

            q.Load(new MailStoreDto { queue = new List<MailRecordDto> { Rec(100001, priority: "2"), Rec(100002, priority: "7") } });

            Assert.Equal(MailPriority.Normal, q.Get(100001).Priority);
            Assert.Equal(MailPriority.Normal, q.Get(100002).Priority);
            Assert.Equal(2, warnings.Count);
        }

        [Test]
        public void Load_DeadlineBeforeTheSendingDay_IsIgnoredWithAWarning()
        {
            var warnings = new List<string>();
            var q = new MailQueue(MailQueue.DefaultQueueLimit, null, warnings.Add);
            MailRecordDto queued = Rec(100001);
            queued.deadline = "1985-02-01";   // before 'sent' 1985-03-01
            MailRecordDto delivered = Rec(100002, delivered: "1985-03-05");
            delivered.deadline = "1985-03-01";   // before 'delivered'
            MailRecordDto badDate = Rec(100003, delivered: "1985-03-05");
            badDate.deadline = "soon";

            q.Load(new MailStoreDto { mails = new List<MailRecordDto> { delivered, badDate }, queue = new List<MailRecordDto> { queued } });

            Assert.Equal(0, q.Get(100001).TimeoutDays);
            Assert.Equal(0, q.Get(100002).TimeoutDays);
            Assert.Null(q.Get(100002).Deadline);
            Assert.Null(q.Get(100003), "an unreadable deadline drops the record");
            Assert.Equal(3, warnings.Count);
        }

        [Test]
        public void Load_ADeliveredMailKeepsItsDeadline_AndExpiresOnIt()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            MailRecordDto rec = Rec(100001, delivered: "1985-03-04");
            rec.deadline = "1985-03-14";
            rec.defaultChoice = "no";
            rec.choices = new List<MailChoiceDto> { new MailChoiceDto { id = "yes", labelKey = "k" }, new MailChoiceDto { id = "no", labelKey = "k" } };

            q.Load(new MailStoreDto { mails = new List<MailRecordDto> { rec } });

            MailRecordModel mail = q.Get(100001);
            Assert.Equal(10, mail.TimeoutDays);
            Assert.Equal(new DateTime(1985, 3, 14), mail.Deadline.Value);
            Assert.False(mail.IsExpired(new DateTime(1985, 3, 13)));
            Assert.True(mail.IsExpired(new DateTime(1985, 3, 14)));
            Assert.Equal(0, w.Count);
        }

        [Test]
        public void Load_SortsByIdWhateverTheOrderInTheFile()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            var dto = new MailStoreDto
            {
                mails = new List<MailRecordDto> { Rec(100007, delivered: "1985-03-02"), Rec(100003, delivered: "1985-03-02") },
                queue = new List<MailRecordDto> { Rec(100009), Rec(100005) },
            };

            q.Load(dto);

            Assert.Equal(new[] { 100003, 100005, 100007, 100009 }, Ids(q.All));
            Assert.Equal(new[] { 100005, 100009 }, Ids(q.GetQueued()));
            Assert.Equal(new[] { 100003, 100007 }, Ids(q.GetDelivered()));
            Assert.Equal(100010, q.NextId);
        }

        [Test]
        public void Load_AppliesTheQueueLimit_WithWarnings()
        {
            var warnings = new List<string>();
            var q = new MailQueue(2, null, warnings.Add);
            var dto = new MailStoreDto
            {
                queue = new List<MailRecordDto>
                {
                    Rec(100001, priority: "Low", feature: "fa"),
                    Rec(100002, priority: "Normal", feature: "fb"),
                    Rec(100003, priority: "Critical", feature: "fc"),
                    Rec(100004, priority: "Normal", feature: "fd"),
                },
            };

            q.Load(dto);

            Assert.Equal(new[] { 100003, 100004 }, Ids(q.GetQueued()), "the Low and the oldest Normal are dropped");
            Assert.Equal(2, warnings.Count);
            Assert.True(AnyContains(warnings, "feature \"fa\""));
        }

        [Test]
        public void Load_RestoresTheDaysCounters_SoALoadedSaveDoesNotGetASecondAllowance()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            for (int i = 0; i < 5; i++) Send(q, "f" + i);
            Assert.Equal(3, q.DeliverDue(Mar2, true, false).Count);

            MailStoreDto saved = ThroughJson(q.ToDto());   // saved on 2 March after the day's deliveries
            var loaded = new MailQueue();
            loaded.Load(saved);

            Assert.Equal(Mar2, loaded.Quota.Day.Value);
            Assert.Equal(3, loaded.Quota.DeliveredToday);
            Assert.Equal(0, loaded.DeliverDue(Mar2, true, false).Count, "GameReady on the same day: the day is already full");
            Assert.Equal(2, loaded.DeliverDue(Mar3, true, false).Count, "next day the rest goes");
        }

        [Test]
        public void Load_Counters_CountOnlyTheLatestDeliveryDay_AndNotCritical()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "fa");
            Send(q, "fa");
            Send(q, "fb");
            Send(q, "fc", MailPriority.Critical);
            Send(q, "fd");
            q.DeliverDue(Mar2, true, false);   // Critical + fa, fa, fb
            q.DeliverDue(Mar3, true, false);   // fd

            var loaded = new MailQueue();
            loaded.Load(ThroughJson(q.ToDto()));

            Assert.Equal(Mar3, loaded.Quota.Day.Value);
            Assert.Equal(1, loaded.Quota.DeliveredToday);
            Assert.Equal(1, loaded.Quota.DeliveredTodayBy("fd"));
            Assert.Equal(0, loaded.Quota.DeliveredTodayBy("fa"));
        }

        [Test]
        public void Load_WithoutDeliveredMails_LeavesTheQuotaWithoutADay()
        {
            List<string> w;
            MailQueue q = NewQueue(out w);
            Send(q, "fa");

            var loaded = new MailQueue();
            loaded.Load(q.ToDto());

            Assert.Null(loaded.Quota.Day);
            Assert.Equal(1, loaded.DeliverDue(Mar2, true, false).Count);
        }

        [Test]
        public void RecordDto_ToDtoThenFromDto_ForSingleRecords()
        {
            MailRecordModel mail = Mail("fa", MailPriority.Low, "fa:x", timeoutDays: 4);
            mail.MailId = 100123;
            mail.SentOn = Mar1;

            MailRecordDto dto = mail.ToDto();
            Assert.Null(dto.delivered);
            Assert.Equal("1985-03-05", dto.deadline);

            var warnings = new List<string>();
            MailRecordModel back = MailRecordModel.FromDto(dto, true, warnings.Add);

            Assert.NotNull(back);
            Assert.Equal(100123, back.MailId);
            Assert.Equal(4, back.TimeoutDays);
            Assert.Equal(MailPriority.Low, back.Priority);
            Assert.Equal("no", back.DefaultChoiceId);
            Assert.Equal(0, warnings.Count);
            Assert.Null(MailRecordModel.FromDto(null, true, warnings.Add));
            Assert.Equal(1, warnings.Count);
            Assert.Null(MailRecordModel.FromDto(null, false, null), "a null warning sink is fine");
        }
    }
}
