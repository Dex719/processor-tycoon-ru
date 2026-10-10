using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Mail;
using PTMod.Core.Logic.Save;
using PTMod.Tests.Common;
using PTMod.Tests.Save;

namespace PTMod.Tests.Integration
{
    /// <summary>
    /// The seam between the mail queue (TSK-061) and the side file (TSK-016): the <c>mail</c> member of the section "mod-framework" goes
    /// from <see cref="MailQueue"/> through <see cref="CoreSectionDto"/> and <see cref="SideFileIo"/> to a file and back into a fresh
    /// queue, and the mail example of design.md ("Пример бокового файла") is read as written. <see cref="MailParticipant"/> is a
    /// test-side stand-in for the part of the ModSave adapter of the core (TSK-025/037) that owns the mail.
    /// </summary>
    public class MailSeamTests
    {
        private const string Save = "1985_3_1";

        [Test]
        public void MailSection_DeliveredAndQueuedMails_SurviveWriteAndRead()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            MailParticipant before = new MailParticipant();
            BuildMidGameStore(before.Queue);

            var writeModel = new SideFileModel();
            string problem;
            Assert.True(writeModel.TryRegister(before, out problem), problem);
            SideFileWriteReport written = io.Write(writeModel, Save, new SideFileHeader { modVersion = "0.1.0", gameDate = "1985-03-01" });
            Assert.True(written.Success, written.Error);
            Assert.Equal(0, written.Warnings.Count, string.Join("; ", written.Warnings));

            MailParticipant after = new MailParticipant();
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(after, out problem), problem);
            SideFileReadReport read = io.Read(readModel, Save);

            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(0, read.Warnings.Count, string.Join("; ", read.Warnings));
            Assert.Equal(0, after.Problems.Count, string.Join("; ", after.Problems));

            // nothing lost, nothing added: the second queue writes the same JSON as the first
            Assert.Equal(JsonOf(before.Queue), JsonOf(after.Queue));
            Assert.Equal(before.Queue.NextId, after.Queue.NextId);
            Assert.Equal(before.Queue.Count, after.Queue.Count);
            Assert.Equal(before.Queue.QueuedCount, after.Queue.QueuedCount);
            Assert.Equal(2, after.Queue.QueuedCount);

            // the details that a loose copy of a DTO field would lose
            MailRecordModel welcome = after.Queue.Find("framework-sample", "smp:welcome");
            Assert.Equal(new DateTime(1985, 1, 12), welcome.Deadline.Value, "the deadline counts from delivery (AC-14.8)");
            Assert.Equal("yes", welcome.ChoiceId);
            Assert.Equal(MailRecordModel.ResolutionPlayer, welcome.Resolution);
            Assert.True(welcome.Read);

            MailRecordModel help = after.Queue.Find("framework-sample", "smp:help");
            Assert.Equal("Осталось 3 дня", help.StatusLine);
            Assert.False(help.FindAction("skip").Repeatable);
            Assert.True(help.FindAction("skip").Used);
            Assert.False(help.FindAction("skip").Enabled);
            Assert.True(help.FindAction("hint").Enabled, "'enabled' defaults to true after the round trip");

            MailRecordModel timed = after.Queue.Find("event-system", "evt:offer");
            Assert.False(timed.IsDelivered);
            Assert.False(timed.FindChoice("yes").Enabled);
            Assert.Equal("es.reason.noMoney", timed.FindChoice("yes").DisabledReasonKey);
            Assert.Equal(new[] { "100" }, timed.FindChoice("yes").DisabledReasonArgs);

            // the queue goes on as if the game was never closed: the queued mails come the next day, the answer period starts then
            IReadOnlyList<MailRecordModel> due = after.Queue.DeliverDue(new DateTime(1985, 3, 2), true, false);
            Assert.Equal(2, due.Count);
            Assert.Equal(new DateTime(1985, 3, 9), after.Queue.Find("event-system", "evt:offer").Deadline.Value, "7 days from the delivery day, not from the sending day (AC-14.8)");
            Assert.Equal(before.Queue.NextId, after.Queue.NextId);
        }

        [Test]
        public void MailSection_QuotaOfTheSavedDay_IsNotRefilledByLoading()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            MailParticipant before = new MailParticipant();
            DateTime day = new DateTime(1985, 3, 1);
            for (int i = 0; i < 5; i++) before.Queue.Enqueue(NewMail("fa" + i, MailPriority.Normal), day);
            Assert.Equal(3, before.Queue.DeliverDue(day, true, false).Count, "the day's allowance");

            var writeModel = new SideFileModel();
            string problem;
            Assert.True(writeModel.TryRegister(before, out problem), problem);
            Assert.True(io.Write(writeModel, Save, null).Success);

            MailParticipant after = new MailParticipant();
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(after, out problem), problem);
            io.Read(readModel, Save);

            Assert.Equal(2, after.Queue.QueuedCount);
            Assert.Equal(0, after.Queue.DeliverDue(day, true, false).Count, "saving and loading in the middle of the day does not give a second allowance");
            Assert.Equal(2, after.Queue.DeliverDue(day.AddDays(1), true, false).Count);
        }

        [Test]
        public void MailSection_ExampleOfDesign_IsReadWithoutWarnings()
        {
            // The mail member of the example file in design.md ("Пример бокового файла"), as written there.
            string core = "\"mod-framework\":{\"_schema\":1,\"mail\":{"
                          + "\"nextId\":100004,"
                          + "\"mails\":["
                          + "{\"id\":100001,\"feature\":\"framework-sample\",\"tag\":\"smp:welcome\",\"priority\":\"Critical\",\"titleKey\":\"smp.mail.title\",\"bodyKey\":\"smp.mail.body\",\"args\":[\"JAT\"],\"sent\":\"1985-01-02\",\"delivered\":\"1985-01-02\",\"read\":true,"
                          + "\"choices\":[{\"id\":\"yes\",\"labelKey\":\"core.common.yes\"},{\"id\":\"no\",\"labelKey\":\"core.common.no\"}],\"defaultChoice\":\"no\",\"deadline\":\"1985-01-12\",\"choice\":\"yes\",\"resolution\":\"Player\"},"
                          + "{\"id\":100002,\"feature\":\"framework-sample\",\"tag\":\"smp:help\",\"priority\":\"Normal\",\"titleKey\":\"smp.mail.helpTitle\",\"bodyKey\":\"smp.mail.helpBody\",\"sent\":\"1985-02-10\",\"delivered\":\"1985-02-11\",\"read\":false,\"status\":\"Осталось 3 дня\",\"resolution\":\"Pending\","
                          + "\"actions\":[{\"id\":\"hint\",\"labelKey\":\"smp.mail.hint\",\"repeatable\":true},{\"id\":\"skip\",\"labelKey\":\"smp.mail.skip\",\"repeatable\":false,\"used\":true,\"enabled\":false}]}"
                          + "],"
                          + "\"queue\":["
                          + "{\"id\":100003,\"feature\":\"framework-sample\",\"tag\":\"smp:digest\",\"priority\":\"Low\",\"titleKey\":\"smp.mail.digestTitle\",\"bodyKey\":\"smp.mail.digestBody\",\"sent\":\"1985-02-28\",\"resolution\":\"Pending\"}"
                          + "]}}";
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(SideFileTestKit.SidePath(Save), SideFileTestKit.File1(core));
            SideFileIo io = SideFileTestKit.NewIo(fs);

            MailParticipant part = new MailParticipant();
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(part, out problem), problem);
            SideFileReadReport read = io.Read(model, Save);

            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(0, read.Warnings.Count, string.Join("; ", read.Warnings));
            Assert.Equal(0, part.Problems.Count, string.Join("; ", part.Problems));
            Assert.Equal(3, part.Queue.Count);
            Assert.Equal(1, part.Queue.QueuedCount);
            Assert.Equal(100004, part.Queue.NextId);
            Assert.Equal(new[] { 100001, 100002 }, part.Queue.GetDelivered().Select(m => m.MailId).ToArray());
            Assert.Equal(new[] { 100003 }, part.Queue.GetQueued().Select(m => m.MailId).ToArray());

            MailRecordModel welcome = part.Queue.Get(100001);
            Assert.Equal(MailPriority.Critical, welcome.Priority);
            Assert.Equal(new DateTime(1985, 1, 12), welcome.Deadline.Value);
            Assert.Equal(10, welcome.TimeoutDays, "the answer period is the distance from delivery to the deadline");
            Assert.Equal(new[] { "JAT" }, welcome.Args);
            Assert.False(welcome.IsPending);
            Assert.Equal(MailPriority.Low, part.Queue.Get(100003).Priority);
            Assert.True(part.Queue.Get(100002).FindAction("skip").Used);

            // a mail sent now continues the numbering
            int next = part.Queue.Enqueue(NewMail("framework-sample", MailPriority.Normal), new DateTime(1985, 3, 1));
            Assert.Equal(100004, next);
        }

        [Test]
        public void MailSection_DamagedRecords_AreDroppedAndTheRestIsRead()
        {
            // an id below 100000 (a vanilla mail id), a queued mail with a delivery date, a repeated id, and one good mail
            string core = "\"mod-framework\":{\"_schema\":1,\"mail\":{\"nextId\":1,"
                          + "\"mails\":["
                          + "{\"id\":7,\"feature\":\"f\",\"sent\":\"1985-01-02\",\"delivered\":\"1985-01-02\"},"
                          + "{\"id\":100010,\"feature\":\"f\",\"tag\":\"f:ok\",\"titleKey\":\"t\",\"bodyKey\":\"b\",\"sent\":\"1985-01-02\",\"delivered\":\"1985-01-02\"},"
                          + "{\"id\":100010,\"feature\":\"f\",\"tag\":\"f:twice\",\"titleKey\":\"t\",\"bodyKey\":\"b\",\"sent\":\"1985-01-03\",\"delivered\":\"1985-01-03\"}],"
                          + "\"queue\":["
                          + "{\"id\":100011,\"feature\":\"f\",\"sent\":\"1985-01-02\",\"delivered\":\"1985-01-02\"}]}}";
            FakeFileSystem fs = SideFileTestKit.NewFs();
            fs.Seed(SideFileTestKit.SidePath(Save), SideFileTestKit.File1(core));
            SideFileIo io = SideFileTestKit.NewIo(fs);

            MailParticipant part = new MailParticipant();
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(part, out problem), problem);
            SideFileReadReport read = io.Read(model, Save);

            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(1, part.Queue.Count, "only the good mail is kept");
            Assert.Equal("f:ok", part.Queue.Get(100010).Tag);
            Assert.True(part.Warnings.Count >= 3, "three records were wrong: " + string.Join("; ", part.Warnings));
            Assert.True(part.Queue.NextId > 100010, "the counter stays above the highest id read");
        }

        // ---- helpers ----

        /// <summary>The state of a game on March 1st 1985: two delivered mails, one answered and one with actions, and two waiting.</summary>
        private static void BuildMidGameStore(MailQueue q)
        {
            MailRecordModel welcome = NewMail("framework-sample", MailPriority.Critical, "smp:welcome", 10);
            welcome.Args = new[] { "JAT" };
            welcome.Choices[0].TooltipKey = "smp.tip.yes";
            q.Enqueue(welcome, new DateTime(1985, 1, 2));
            q.DeliverDue(new DateTime(1985, 1, 2), true, false);
            welcome.Read = true;
            welcome.ChoiceId = "yes";
            welcome.Resolution = MailRecordModel.ResolutionPlayer;

            MailRecordModel help = NewMail("framework-sample", MailPriority.Normal, "smp:help");
            help.Actions.Add(new MailActionModel { Id = "hint", LabelKey = "smp.mail.hint" });
            help.Actions.Add(new MailActionModel { Id = "skip", LabelKey = "smp.mail.skip", Repeatable = false, Used = true, Enabled = false });
            help.StatusLine = "Осталось 3 дня";
            q.Enqueue(help, new DateTime(1985, 2, 10));
            q.DeliverDue(new DateTime(1985, 2, 11), true, false);

            MailRecordModel digest = NewMail("framework-sample", MailPriority.Low, "smp:digest");
            digest.TitleKey = null;
            digest.BodyKey = null;
            digest.Title = LocText.Of("Дайджест", "Digest");
            digest.Body = new LocText("smp.mail.digestBody", "Тело", "Body");
            q.Enqueue(digest, new DateTime(1985, 2, 28));

            MailRecordModel timed = NewMail("event-system", MailPriority.Normal, "evt:offer", 7);
            timed.Choices[0].Enabled = false;
            timed.Choices[0].DisabledReasonKey = "es.reason.noMoney";
            timed.Choices[0].DisabledReasonArgs = new[] { "100" };
            q.Enqueue(timed, new DateTime(1985, 2, 28));
        }

        private static MailRecordModel NewMail(string feature, MailPriority priority, string tag = null, int timeoutDays = 0)
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
                mail.Choices.Add(new MailChoiceModel { Id = "yes", LabelKey = "core.common.yes" });
                mail.Choices.Add(new MailChoiceModel { Id = "no", LabelKey = "core.common.no" });
                mail.DefaultChoiceId = "no";
            }
            return mail;
        }

        private static string JsonOf(MailQueue queue)
        {
            return ModJson.Write(ModJson.From(queue.ToDto()), false);
        }

        /// <summary>
        /// What the ModSave adapter will do with the mail (TSK-025/037): the core section carries only the <c>mail</c> member here.
        /// Problems of the DTO conversion and the warnings of the queue are collected for the assertions.
        /// </summary>
        private sealed class MailParticipant : ISideFileParticipant
        {
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly MailQueue Queue;

            public MailParticipant()
            {
                Queue = new MailQueue(MailQueue.DefaultQueueLimit, null, Warnings.Add);
            }

            public string SectionId
            {
                get { return "mod-framework"; }
            }

            public int SchemaVersion
            {
                get { return 1; }
            }

            public void ResetToDefaults()
            {
                Queue.Clear();
            }

            public void Read(JsonObject section, int schema)
            {
                var errors = new List<string>();
                CoreSectionDto dto = ModJson.Bind<CoreSectionDto>(section, errors);
                Problems.AddRange(errors);
                Queue.Load(dto.mail);
            }

            public JsonObject Write()
            {
                var errors = new List<string>();
                var json = (JsonObject)ModJson.From(new CoreSectionDto { mail = Queue.ToDto() }, errors);
                Problems.AddRange(errors);
                json.Remove("_schema");   // the model owns it
                return json;
            }
        }
    }
}
