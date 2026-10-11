using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// Chains and mail effects (FR-5, FR-6, D9, NFR-7; design.md "Цепочка FireEvent"): <c>FireEvent</c> puts the next event into <c>scheduled</c> for
    /// <c>today + delay</c> (the delay drawn once at <c>Resolve</c>), with the target <c>Same</c> / <c>Player</c> / <c>Definition</c>; the next instance knows its
    /// parent and its depth, and a chain stops at 8 levels; a <c>flag</c> is set for <c>es.eventFlag</c>; an event that fires itself without a delay is refused by
    /// the validator; <c>SendMail</c> sends an information mail to the player with the tag <c>es:fx:...</c>.
    /// </summary>
    [Covers(typeof(FireEventEffect), typeof(SendMailEffect), typeof(ScheduledFires), typeof(ChainFlags), typeof(IChainHost))]
    public class ChainTests
    {
        private const int Today = 7000;

        private static EventDefinition Troll(string target = null, string flag = null)
        {
            string extra = (target != null ? ", 'target': '" + target + "'" : string.Empty) + (flag != null ? ", 'flag': '" + flag + "'" : string.Empty);
            return Fx.Event("es.patent_troll", new[]
            {
                Fx.Choice("settle"),
                Fx.Choice("fight", Fx.Effect("FireEvent", "{ 'eventId': 'es.patent_win', 'delayDays': [60, 120]" + extra + " }")),
            });
        }

        private static EventDefinition Win()
        {
            return Fx.Event("es.patent_win", null, Fx.Effect("Reputation", "{ 'delta': 3 }"));
        }

        [Test]
        public void FireEvent_SchedulesTheNextEvent_ForTodayPlusTheDelay_AndItFiresThatDay()
        {
            EventDefinition troll = Troll();
            EventDefinition win = Win();
            var rig = new EffectsRig(new FakeCatalog(null, troll, win));
            rig.World.Today = Today;
            rig.Fire("es.patent_troll", CompanyKey.Player);
            EventInstanceDto parent = rig.Instance("es.patent_troll");
            AppliedEffect fire = parent.options[1].effects[0];
            int delay = (int)fire.value;
            Assert.InRange(delay, 60, 120, "the delay is drawn once at Resolve");
            Assert.Equal("es.fx.chain", fire.textKey);
            Assert.Equal(delay.ToString(), fire.textArgs[0]);

            rig.Runtime.ResolveChoice(parent.id, "fight", false, Today + 2);
            Assert.Equal(1, rig.State.Scheduled.Count);
            ScheduledFire scheduled = rig.State.Scheduled[0];
            Assert.Equal("es.patent_win", scheduled.eventId);
            Assert.Equal(Today + 2 + delay, scheduled.dueDay, "today + delay");
            Assert.Equal(CompanyKey.Player, scheduled.companyKey, "target Same: the company of the instance");
            Assert.Equal(parent.id, scheduled.parentId);
            Assert.Equal(1, scheduled.depth);
            Assert.True(scheduled.ignoreConditions, "the parent has decided");

            rig.RunDays(Today + 3, Today + 1 + delay);
            Assert.Equal(0, rig.Entries(EventLogKinds.Fired).FindAll(e => e.eventId == "es.patent_win").Count, "not before its day");
            rig.RunDays(Today + 2 + delay, Today + 2 + delay);
            List<EventLogEntry> fired = rig.Entries(EventLogKinds.Fired).FindAll(e => e.eventId == "es.patent_win");
            Assert.Equal(1, fired.Count, "fired on its day");
            Assert.Equal(3f, rig.Game.Reputation[CompanyKey.Player]);
            Assert.Equal(0, rig.State.Scheduled.Count);
        }

        [Test]
        public void TheNextInstance_KnowsItsParentAndItsDepth()
        {
            // A chain of three manual events with a common FireEvent: es.c0 → es.c1 → es.c2.
            EventDefinition c0 = Fx.Event("es.c0", null, Fx.Effect("FireEvent", "{ 'eventId': 'es.c1', 'delayDays': 1 }"));
            EventDefinition c1 = Fx.Event("es.c1", null,
                Fx.Effect("FireEvent", "{ 'eventId': 'es.c2', 'delayDays': 1 }"), Fx.Effect("ModifierTimed", "{ 'channel': 'taxes', 'multiplier': 0.9 }", 30));
            EventDefinition c2 = Fx.Event("es.c2", null, Fx.Effect("ModifierTimed", "{ 'channel': 'taxes', 'multiplier': 0.9 }", 30));
            var rig = new EffectsRig(new FakeCatalog(null, c0, c1, c2));
            rig.World.Today = Today;

            rig.Fire("es.c0", CompanyKey.Player);
            rig.RunDays(Today + 1, Today + 2);

            EventInstanceDto second = rig.Instance("es.c1");
            EventInstanceDto third = rig.Instance("es.c2");
            Assert.NotNull(second);
            Assert.NotNull(third);
            Assert.Equal(1, second.chainDepth);
            Assert.Equal(2, third.chainDepth);
            Assert.Equal(second.id, third.parentId);
        }

        [Test]
        public void AChain_StopsAtEightLevels()
        {
            // An event that fires itself with a delay: allowed by the validator, held by the depth (NFR-7).
            EventDefinition loop = Fx.Event("es.loop", null, Fx.Effect("FireEvent", "{ 'eventId': 'es.loop', 'delayDays': 1 }"));
            var rig = new EffectsRig(new FakeCatalog(null, loop));
            rig.World.Today = Today;

            rig.Fire("es.loop", CompanyKey.Player);
            rig.RunDays(Today + 1, Today + 20);

            List<EventLogEntry> fired = rig.Entries(EventLogKinds.Fired);
            Assert.Equal(1 + ChainFlags.MaxDepth, fired.Count, "the first firing and eight more");
            Assert.Equal(0, rig.State.Scheduled.Count, "the ninth step is not scheduled");

            Assert.Equal(1, ChainFlags.NextDepth(0));
            Assert.Equal(8, ChainFlags.NextDepth(7));
            Assert.Equal(-1, ChainFlags.NextDepth(8));
        }

        [Test]
        public void TheTarget_Same_Player_Definition()
        {
            Assert.Equal("ai:3", ChainFlags.TargetOf("Same", "ai:3"));
            Assert.Equal("ai:3", ChainFlags.TargetOf(null, "ai:3"), "Same by default");
            Assert.Equal(CompanyKey.Player, ChainFlags.TargetOf("Player", "ai:3"));
            Assert.Null(ChainFlags.TargetOf("Definition", "ai:3"), "the targets of the next event decide");
            Assert.Null(ChainFlags.TargetOf("Same", EventCompanyKeys.Global), "a global instance has no company of its own");

            foreach (string target in new[] { "Same", "Player", "Definition" })
            {
                var rig = new EffectsRig(new FakeCatalog(null, Troll(target), Win()));
                rig.Options.DecideForAi = instance => "fight";
                rig.World.Today = Today;
                Assert.Equal(EventFireResult.Fired, rig.Fire("es.patent_troll", "ai:1"));
                Assert.Null(rig.Instance("es.patent_troll"), "the AI company decided on the day of the firing");
                string expected = target == "Same" ? "ai:1" : target == "Player" ? CompanyKey.Player : null;
                Assert.Equal(expected, rig.State.Scheduled[0].companyKey, target);
            }
        }

        [Test]
        public void AFlag_IsSetWhenTheEffectApplies_ForTheCompanyOfTheInstance()
        {
            var rig = new EffectsRig(new FakeCatalog(null, Troll(null, "patent_fought"), Win()));
            rig.World.Today = Today;
            rig.Fire("es.patent_troll", CompanyKey.Player);
            Assert.False(rig.History.HasFlag("patent_fought", CompanyKey.Player));

            rig.Runtime.ResolveChoice(rig.Instance("es.patent_troll").id, "fight", false, Today + 1);
            Assert.True(rig.History.HasFlag("patent_fought", CompanyKey.Player), "es.eventFlag reads it");
            Assert.True(ChainFlags.IsFlagName("patent_fought"));
            Assert.False(ChainFlags.IsFlagName("bad flag"));
        }

        [Test]
        public void TheValidator_RefusesAnEventThatFiresItselfWithoutADelay_AndABadFlag()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                string self = EventTestKit.Record("'effects': [ { 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': 0 } } ]");
                Assert.False(EventTestKit.Check(env, "event-system", self, messages, "universal"), "a self call with delay 0 loops within the day");
                Assert.True(EventTestKit.AnyContains(messages, "without a delay"), EventTestKit.Join(messages));

                messages.Clear();
                string later = EventTestKit.Record("'effects': [ { 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': 30, 'flag': 'again' } } ]");
                Assert.True(EventTestKit.Check(env, "event-system", later, messages, "universal"), "with a delay the depth holds it: " + EventTestKit.Join(messages));

                messages.Clear();
                string badFlag = EventTestKit.Record("'effects': [ { 'type': 'FireEvent', 'params': { 'eventId': 'es.sample', 'delayDays': 30, 'flag': 'no spaces' } } ]");
                Assert.False(EventTestKit.Check(env, "event-system", badFlag, messages, "universal"));
                Assert.True(EventTestKit.AnyContains(messages, "'flag'"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void SendMail_SendsAnInformationMailToThePlayer_WithItsTag_AndSkipsAnAiCompany()
        {
            EventDefinition win = Fx.Event("es.patent_win", null, Fx.Effect("SendMail", "{ 'title': 'es.patent_win.mail.title', 'text': 'es.patent_win.mail.text' }"));
            var rig = new EffectsRig(new FakeCatalog(null, win));
            rig.World.Today = Today;

            rig.Scheduler.Fire("es.patent_win", new EventFireContext { CompanyKey = CompanyKey.Player, Args = new[] { "Intel" } });
            Assert.Equal(1, rig.Game.Mails.Count);
            Assert.Equal("es.patent_win.mail.title", rig.Game.Mails[0][0]);
            Assert.Equal("es.patent_win.mail.text", rig.Game.Mails[0][1]);
            Assert.Equal("es:fx:1/common/0", rig.Game.Mails[0][2], "the tag es:fx:<effect id>; the mail of the instance itself is es:<id>");
            Assert.Equal("Intel", rig.Game.Mails[0][3], "the arguments of the instance");
            Assert.Equal("SendMail", rig.Entries(EventLogKinds.Fired)[0].effects[0].type);

            rig.Scheduler.Fire("es.patent_win", new EventFireContext { CompanyKey = "ai:2" });
            Assert.Equal(1, rig.Game.Mails.Count, "only the player reads mail");

            rig.Game.RefuseMail = true;
            rig.Scheduler.Fire("es.patent_win", new EventFireContext { CompanyKey = CompanyKey.Player });
            Assert.Equal(1, rig.Entries(EventLogKinds.EffectFailed).Count, "a refused mail is a failed effect");
            Assert.Equal(1, rig.Faults.Count, string.Join("; ", rig.Faults));
        }

        [Test]
        public void ScheduledFires_KeepsTheChainInTheSavedQueue()
        {
            var state = new SchedulerState();
            var history = new EventHistory();
            var fires = new ScheduledFires(state, history);

            fires.Schedule("es.next", 7100, "ai:2", 12, new[] { "a", null }, 3);
            fires.Schedule("es.other", 7200, "", 0, null, 0);
            fires.SetFlag("seen", "ai:2", 7000);

            Assert.Equal(2, fires.Pending.Count);
            Assert.Equal("ai:2", state.Scheduled[0].companyKey);
            Assert.Equal("", state.Scheduled[0].args[1]);
            Assert.Equal(3, state.Scheduled[0].depth);
            Assert.Null(state.Scheduled[1].companyKey, "empty - the definition decides");
            Assert.True(history.HasFlag("seen", "ai:2"));

            var dto = new EventSystemSaveDto();
            state.Export(dto);
            var restored = new SchedulerState();
            restored.Import(dto, new List<string>());
            Assert.Equal(3, restored.Scheduled[0].depth, "the depth is saved");
            Assert.Throws<System.ArgumentException>(() => fires.Schedule("", 1, null, 0, null, 0));
        }
    }
}
