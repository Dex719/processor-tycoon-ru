using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The saves of the engine (FR-10, NFR-3, NFR-7; TSK-021, TSK-024): the section <c>event-system</c> written and read with <c>ModJson</c> without losses and without a
    /// version field of its own, an empty or missing section, an event that is no longer in the registry (its instance is cancelled, its letter closed, the journal written),
    /// numbers a hand edit could have broken, and the scenarios AC-10.1 (the letter, the deadline and the multiplier come back), AC-10.2 (a save without the section) and
    /// AC-10.3 (the buttons are recalculated after a load). The engine is the pure one of <see cref="LifecycleRig"/>: the same <see cref="EventRuntime"/> the game calls
    /// on <c>ModTicks.GameReady</c>.
    /// </summary>
    [Covers(typeof(EventSystemSaveDto), typeof(EventRuntime), typeof(ReadyKind), typeof(SessionGate), typeof(EventSaveSection), typeof(SaveChecks))]
    public class PersistenceTests
    {
        private static readonly int Fired = Defs.Day(1988, 4, 6);
        private static readonly int SaveDay = Defs.Day(1988, 4, 10);
        private static readonly int Deadline = Defs.Day(1988, 4, 20);

        private static EventDefinition Vote(bool important = false)
        {
            return ChoiceEvents.Make("es.vote", new[]
            {
                Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }")),
                Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }")),
            }, important);
        }

        private static EventDefinition Strike()
        {
            return Fx.Event("es.strike", null, Fx.Effect("FactoryOutput", "{ 'multiplier': 0.6 }", 21));
        }

        private static LifecycleRig NewEngine(params EventDefinition[] definitions)
        {
            var rig = new LifecycleRig(definitions);
            rig.Choice.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            return rig;
        }

        /// <summary>A game at 1988-04-10: a vote that was fired on 04-06 (deadline 04-20, the letter delivered) and a strike of ai:1 (production.output x0.6 from 04-01 to 04-22).</summary>
        private static LifecycleRig GameAtSaveDay()
        {
            LifecycleRig game = NewEngine(Vote(), Strike());
            game.GameReady = true;
            game.Choice.SetDay(Defs.Day(1988, 4, 1));
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            Assert.Equal(EventFireResult.Fired, game.Rig.Fire("es.strike", "ai:1"));
            game.Choice.Days(Defs.Day(1988, 4, 2), Defs.Day(1988, 4, 5));
            game.Choice.SetDay(Fired);
            game.Choice.FireAndDeliver("es.vote");
            game.Choice.Days(Fired + 1, SaveDay);
            return game;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The shape of the section
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheSection_HasTheKeysOfTheDesign_AndNoVersionFieldOfItsOwn()
        {
            LifecycleRig game = GameAtSaveDay();
            Assert.Equal("event-system", game.Gate.SectionId);
            Assert.Equal(1, game.Gate.SchemaVersion, "the version is the core's: SchemaVersion = 1");

            JsonObject section = LifecycleRig.Parse(game.Save());
            foreach (string key in new[] { "nextInstanceId", "lastProcessedDay", "instances", "scheduled", "cooldowns", "categories", "firedOnce", "edges", "flags", "log" })
            {
                Assert.True(section.ContainsKey(key), "the section has '" + key + "'");
            }
            foreach (KeyValuePair<string, JsonNode> member in section)
            {
                string name = member.Key.ToLowerInvariant();
                Assert.False(name == "_schema" || name == "schema" || name == "version" || name == "schemaversion", "no field of the version: " + member.Key);
            }
            Assert.Equal(Defs.Day(1988, 4, 10), section.GetInt32("lastProcessedDay"));
        }

        [Test]
        public void TheSection_RoundTripsThroughModJson_WithoutLosses()
        {
            LifecycleRig game = GameAtSaveDay();
            game.Rig.History.SetFlag("patent_fought", CompanyKey.Player, SaveDay - 3);
            game.Rig.History.RecordFired("es.once", CompanyKey.Player, SaveDay - 2);
            game.Rig.State.MarkUsedUp("es.once");
            game.Rig.State.SetLastFired("es.vote", CompanyKey.Player, Fired);
            game.Rig.State.SetCategoryLastDay("universal", SaveDay - 1);
            game.Rig.State.SetEdgeLevel("es.edge", true);
            Assert.Equal(EventFireResult.Scheduled, game.Rig.Fire("es.strike", "ai:2", 9));

            string text = game.Save();
            LifecycleRig loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(SaveDay);
            LifecycleRig.CopyLetters(game.Choice.Mail, loaded.Choice.Mail);
            loaded.Load(text);

            Assert.Equal(text, loaded.Save(), "the same section after a round trip");
            Assert.Equal(0, loaded.SaveWarnings.Count, string.Join("; ", loaded.SaveWarnings));
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));

            JsonObject section = LifecycleRig.Parse(text);
            Assert.Equal(2, section.GetArray("instances").Count, "the vote and the strike");
            Assert.Equal(1, section.GetArray("scheduled").Count);
            Assert.Equal(1, section.GetArray("flags").Count);
            Assert.Equal(1, section.GetArray("firedOnce").Count);
            Assert.True(loaded.Rig.History.HasFlag("patent_fought", CompanyKey.Player));
            Assert.Equal(1, loaded.Rig.State.Scheduled.Count);
        }

        [Test]
        public void AnEmptySection_AndANullSection_GiveAnEmptyEngine_WithoutAWord()
        {
            LifecycleRig game = GameAtSaveDay();

            game.Load(ModJson.Write(new JsonObject(), false));
            Assert.Equal(0, game.Rig.Runtime.Instances.Count);
            Assert.Equal(0, game.Rig.Runtime.ActiveEffects.Count);
            Assert.Equal(0, game.Rig.Log.Count);
            Assert.Equal(SchedulerState.NoDay, game.Rig.State.LastProcessedDay);

            game.Load(null);
            Assert.Equal(0, game.Rig.Runtime.Instances.Count);
            Assert.Equal(0, game.SaveWarnings.Count, string.Join("; ", game.SaveWarnings));
            Assert.Equal(0, game.Faults.Count, string.Join("; ", game.Faults));
        }

        [Test]
        public void ASectionWithTheWrongTypes_IsReadAsFarAsItMakesSense_AndTheRestIsReported()
        {
            LifecycleRig game = GameAtSaveDay();
            JsonObject section = LifecycleRig.Parse(game.Save());
            section.Set("nextInstanceId", "many");
            section.Set("cooldowns", 5);
            section.Set("log", "none");

            var loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(SaveDay);
            loaded.Load(ModJson.Write(section, false));

            Assert.True(loaded.SaveWarnings.Count > 0, "the broken parts are reported");
            Assert.Equal(2, loaded.Rig.Runtime.Instances.Count, "the instances were readable and are kept");
            Assert.True(loaded.Rig.Runtime.NextInstanceId > 2, "the next id is above the ones in use");
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The scenarios of FR-10
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AC_10_1_TheLetter_TheDeadline_AndTheMultiplier_ComeBackAfterALoad()
        {
            LifecycleRig game = GameAtSaveDay();
            EventInstanceDto vote = game.Rig.Instance("es.vote");
            Assert.Equal(Deadline, vote.deadlineDay, "the deadline is 1988-04-20");
            Assert.Near(0.6, game.Rig.Channel("production.output", "ai:1"), 1e-6);
            string text = game.Save();

            // a new session: the mail service restored the letter of the save, with nothing written under it yet
            LifecycleRig loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(SaveDay);
            LifecycleRig.CopyLetters(game.Choice.Mail, loaded.Choice.Mail);
            FakeMail.Letter letter = loaded.Choice.Mail.Letters[0];
            letter.Status = null;
            Assert.Equal(1f, loaded.Rig.Channel("production.output", "ai:1"), "before the load nothing contributes");

            loaded.Load(text);

            EventInstanceDto back = loaded.Rig.Instance("es.vote");
            Assert.NotNull(back, "the waiting choice is back");
            Assert.Equal(Deadline, back.deadlineDay, "the deadline did not move");
            Assert.Equal(letter.Id, back.mailId, "the instance found its letter by the tag");
            Assert.Equal(Fired, back.deliveredDay);
            Assert.Equal("Осталось 10 из 14 дн.", letter.Status, "the line under the letter is written again");
            Assert.Null(letter.Answer, "the letter is open, its buttons are on");
            Assert.Near(0.6, loaded.Rig.Channel("production.output", "ai:1"), 1e-6, "the multiplier is registered again");
            Assert.Equal(1f, loaded.Rig.Channel("production.output", CompanyKey.Player), "and only for the company it belongs to");
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));
            Assert.Equal(0, loaded.Choice.UiFaults.Count, string.Join("; ", loaded.Choice.UiFaults));
        }

        [Test]
        public void AfterALoad_TheMultiplierEndsOnItsDay_AndTheChoiceIsMadeByTheTimerOnItsDay()
        {
            LifecycleRig game = GameAtSaveDay();
            string text = game.Save();
            LifecycleRig loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(SaveDay);
            LifecycleRig.CopyLetters(game.Choice.Mail, loaded.Choice.Mail);
            loaded.Load(text);

            loaded.Choice.Days(SaveDay + 1, Deadline - 1);
            Assert.NotNull(loaded.Rig.Instance("es.vote"), "still waiting the day before the deadline");
            Assert.Near(0.6, loaded.Rig.Channel("production.output", "ai:1"), 1e-6, "the strike lasts until 04-22");

            loaded.Choice.Days(Deadline, Deadline);
            Assert.Null(loaded.Rig.Instance("es.vote"), "the default choice on 04-20");
            Assert.Equal(1, loaded.Rig.Entries(EventLogKinds.AutoResolved).Count);

            loaded.Choice.Days(Deadline + 1, Defs.Day(1988, 4, 22));
            Assert.Equal(1f, loaded.Rig.Channel("production.output", "ai:1"), "the multiplier is gone on its end date");
            Assert.Equal(1, loaded.Rig.Entries(EventLogKinds.EffectEnded).Count);
        }

        [Test]
        public void ALoadIntoARunningEngine_DropsTheContributionsOfTheSaveBefore()
        {
            LifecycleRig game = GameAtSaveDay();
            string early = game.Save();

            // later in the same game a second multiplier came; then the player loads the earlier save
            AppliedEffect later = game.Rig.Runtime.ApplyDirect(Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.25 }", 60), "ai:2", "hist.x", null, SaveDay);
            Assert.NotNull(later);
            Assert.Near(1.25, game.Rig.Channel("research.speed", "ai:2"), 1e-6);

            game.Lifecycle.OnSceneLeaving();
            game.Load(early);
            Assert.Equal(1f, game.Rig.Channel("research.speed", "ai:2"), "the contribution of the other game is gone");
            Assert.Near(0.6, game.Rig.Channel("production.output", "ai:1"), 1e-6, "the one of the save is there");
            Assert.Equal(1, game.Rig.Runtime.ActiveEffects.Count);
        }

        [Test]
        public void AC_10_2_ASaveWithoutTheSection_StartsEmpty_AndTheEventsStartToCome()
        {
            EventDefinition start = Defs.Condition("es.start", Defs.When((key, day) => true), "Day");
            LifecycleRig game = NewEngine(start);
            game.Choice.SetDay(SaveDay);
            game.Load(null);

            Assert.Equal(0, game.SaveWarnings.Count, string.Join("; ", game.SaveWarnings));
            Assert.Equal(0, game.Faults.Count, string.Join("; ", game.Faults));
            Assert.Equal(0, game.Rig.Log.Count, "the journal is empty");
            Assert.Equal(1, game.PackLoads, "the packs are read on the first GameReady");

            game.Choice.Day(SaveDay + 1);
            Assert.True(game.Rig.Entries(EventLogKinds.Fired).Count > 0, "the events begin to come");
        }

        [Test]
        public void AC_10_3_AGreyButton_IsRecalculatedByTheCurrentCondition_OnTheLoad()
        {
            bool haveResearch = false;
            EventChoice careful = Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }"));
            EventChoice bold = Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }"));
            bold.requires = ChoiceEvents.Flag("researchActive", () => haveResearch);
            EventDefinition definition = ChoiceEvents.Make("es.grey", new[] { careful, bold });

            LifecycleRig game = NewEngine(definition);
            game.GameReady = true;
            game.Choice.SetDay(Fired);
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            EventInstanceDto instance = game.Choice.FireAndDeliver("es.grey");
            FakeMail.Letter letter = game.Choice.Mail.OfInstance(instance.id);
            Assert.False(letter.Buttons["b"].Enabled, "the button is grey: there is no research yet");
            string text = game.Save();

            // the research appeared before the player loaded the save: the mail service restored the letter with the old state of its buttons
            haveResearch = true;
            LifecycleRig loaded = NewEngine(definition);
            loaded.Choice.SetDay(Fired);
            LifecycleRig.CopyLetters(game.Choice.Mail, loaded.Choice.Mail);
            Assert.False(loaded.Choice.Mail.Letters[0].Buttons["b"].Enabled, "the restored letter still has the old state");
            loaded.Load(text);

            Assert.True(loaded.Choice.Mail.Letters[0].Buttons["b"].Enabled, "after GameReady(Loaded) the button follows the current condition");
            Assert.Null(loaded.Choice.Mail.Letters[0].Buttons["b"].ReasonKey);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // An event that is no longer in the registry; a new game
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AnEventThatIsNoLongerInTheRegistry_IsCancelled_ItsLetterIsClosed_AndTheJournalSaysSo()
        {
            EventDefinition gone = ChoiceEvents.Make("es.gone", new[] { Fx.Choice("a"), Fx.Choice("b") });
            LifecycleRig game = NewEngine(Vote(), gone, Strike());
            game.GameReady = true;
            game.Choice.SetDay(Fired);
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            EventInstanceDto goneInstance = game.Choice.FireAndDeliver("es.gone");
            game.Choice.FireAndDeliver("es.vote");
            game.Rig.Fire("es.strike", "ai:1");
            string text = game.Save();
            FakeMail.Letter goneLetter = game.Choice.Mail.OfInstance(goneInstance.id);

            // the pack that had es.gone is not there any more (a feature was removed, a file was edited)
            LifecycleRig loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(Fired + 2);
            LifecycleRig.CopyLetters(game.Choice.Mail, loaded.Choice.Mail);
            loaded.Load(text);

            Assert.Null(loaded.Rig.Instance("es.gone"), "the instance is gone");
            Assert.NotNull(loaded.Rig.Instance("es.vote"), "the others are kept");
            Assert.Equal(1, loaded.Rig.Entries(EventLogKinds.Cancelled).Count, "the journal has the cancellation");
            Assert.Equal("es.gone", loaded.Rig.Entries(EventLogKinds.Cancelled)[0].eventId);
            FakeMail.Letter restored = loaded.Choice.Mail.Get(goneLetter.Id);
            Assert.Equal("a", restored.Answer, "the letter is closed (ModMail.Resolve)");
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));
            Assert.Near(0.6, loaded.Rig.Channel("production.output", "ai:1"), 1e-6, "an effect of a known event is untouched");
        }

        [Test]
        public void AnUnknownEventWithARunningEffect_LeavesNoContribution()
        {
            EventDefinition shock = Fx.Event("ms.shock", null, Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 0.8 }", 90));
            LifecycleRig game = NewEngine(shock);
            game.GameReady = true;
            game.Choice.SetDay(Fired);
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            game.Rig.Fire("ms.shock", CompanyKey.Player);
            Assert.Near(0.8, game.Rig.Channel("research.speed"), 1e-6);
            string text = game.Save();

            LifecycleRig loaded = NewEngine(Vote());
            loaded.Choice.SetDay(Fired + 5);
            loaded.Load(text);

            Assert.Equal(0, loaded.Rig.Runtime.ActiveEffects.Count, "the effect of an event nobody knows is dropped");
            Assert.Equal(1f, loaded.Rig.Channel("research.speed"), "and does not multiply anything");
            Assert.Equal(1, loaded.Rig.Entries(EventLogKinds.Cancelled).Count);
        }

        [Test]
        public void ANewGame_StartsFromToday_WithNothingThatTheFastForwardLeft()
        {
            EventDefinition start = Defs.Condition("es.start", Defs.When((key, day) => true), "Day");
            LifecycleRig game = NewEngine(Vote(important: true), Strike(), start);
            game.GameReady = true;
            game.Choice.SetDay(SaveDay);
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            game.Rig.Fire("es.strike", "ai:1");
            game.Choice.FireAndDeliver("es.vote");
            game.Choice.Days(SaveDay + 1, SaveDay + 3);
            Assert.True(game.Rig.Log.Count > 0);
            Assert.True(game.Choice.Window.IsOpen, "the window of the important event of the old game is open");

            // the player starts a new game: the core resets every participant before the fast-forward; whatever the fast-forward then left is cleared on GameReady
            int day1990 = Defs.Day(1990, 1, 1);
            game.Lifecycle.OnSceneLeaving();
            game.GameReady = false;
            game.Gate.ResetToDefaults();
            Assert.Equal(0, game.Rig.Runtime.Instances.Count, "the reset of the core cleared the engine");
            game.Rig.Runtime.ApplyDirect(Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 0.8 }", 90), EventCompanyKeys.Global, "left.over", null, day1990 - 5);
            game.Choice.SetDay(day1990);
            game.GameReady = true;
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);

            Assert.Equal(0, game.Rig.Log.Count, "AC-13.1: the journal is empty");
            Assert.Equal(0, game.Rig.Runtime.ActiveEffects.Count, "AC-13.1: no effect is running");
            Assert.Equal(1f, game.Rig.Channel("research.speed", "ai:1"), "and nothing multiplies");
            Assert.Equal(day1990, game.Rig.State.LastProcessedDay, "the count of days starts from today");
            Assert.Equal(0, game.Choice.Ui.Windows.Count);
            Assert.False(game.Choice.Window.IsOpen, "and the window of the old game is closed");

            game.Choice.Day(day1990 + 1);
            Assert.True(game.Rig.Entries(EventLogKinds.Fired).Count > 0, "and the events come from there on");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // NFR-7: numbers a hand edit could have broken
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void HandEditedNumbers_AreDropped_AMultiplierOf50_ANegativeDuration_AndTheRest()
        {
            LifecycleRig game = GameAtSaveDay();
            var dto = ModJson.Bind<EventSystemSaveDto>(LifecycleRig.Parse(game.Save()), new List<string>());
            int goodId = dto.instances[0].id;

            dto.instances.Add(Broken(100, e => { e.channel = "taxes"; e.value = 50f; }));                         // a multiplier of 50
            dto.instances.Add(Broken(101, e => { e.durationDays = -5; }));                                       // a negative duration
            dto.instances.Add(Broken(102, e => { e.chance = 7f; }));                                             // a chance above 1
            dto.instances.Add(Broken(103, e => { e.channel = string.Empty; e.type = "Money"; e.value = 1e15f; }));  // an amount nobody has
            dto.instances.Add(Broken(105, e => { e.durationDays = 10000000; }));                                 // a timer that never ends
            dto.instances.Add(new EventInstanceDto { id = 106, eventId = "es.vote", companyKey = CompanyKey.Player, state = EventInstanceStates.AwaitingChoice, firedDay = Fired, deadlineDay = int.MaxValue });
            dto.instances.Add(new EventInstanceDto { id = 107, eventId = "es.vote", companyKey = "bank", state = EventInstanceStates.AwaitingChoice, firedDay = Fired, deadlineDay = Deadline });
            dto.lastProcessedDay = int.MaxValue;

            var loaded = NewEngine(Vote(), Strike());
            loaded.Choice.SetDay(SaveDay);
            loaded.Load(ModJson.Write(ModJson.From(dto), false));

            Assert.Equal(2, loaded.Rig.Runtime.Instances.Count, "only the two good instances are left; every broken one is dropped whole or loses its effect");
            Assert.Equal(goodId, loaded.Rig.Runtime.Instances[0].id);
            Assert.True(loaded.SaveWarnings.Count >= 8, "each drop is reported: " + string.Join("; ", loaded.SaveWarnings));
            Assert.Equal(SchedulerState.NoDay, loaded.Rig.State.LastProcessedDay, "a last day that is no day is forgotten");
            Assert.Equal(1f, loaded.Rig.Channel("taxes", CompanyKey.Player), "nothing multiplies by 50");
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));
        }

        private static EventInstanceDto Broken(int id, Action<AppliedEffect> damage)
        {
            var effect = new AppliedEffect
            {
                id = id + "/common/0", instanceId = id, type = "ModifierTimed", companyKey = CompanyKey.Player, channel = "taxes", value = 1.1f,
                startDay = Fired, endDay = Fired + 30, durationDays = 30, state = AppliedEffectStates.Active,
            };
            damage(effect);
            return new EventInstanceDto
            {
                id = id, eventId = "es.vote", companyKey = CompanyKey.Player, state = EventInstanceStates.Resolved, firedDay = Fired,
                common = new List<AppliedEffect> { effect },
            };
        }

        [Test]
        public void TheChecksOfASavedEffect_NameTheNumberThatIsWrong()
        {
            var instance = new EventInstanceDto { id = 1, eventId = "es.x", companyKey = CompanyKey.Player, state = EventInstanceStates.Resolved };
            Func<Action<AppliedEffect>, string> check = damage =>
            {
                var effect = new AppliedEffect { id = "1/common/0", instanceId = 1, type = "ModifierTimed", channel = "taxes", value = 1.2f, state = AppliedEffectStates.Active, startDay = 5, endDay = 50, durationDays = 45 };
                damage(effect);
                return SaveChecks.Effect(effect, instance);
            };

            Assert.Null(check(e => { }), "a good effect passes");
            Assert.Equal("the multiplier 50", check(e => e.value = 50f));
            Assert.Equal("the multiplier -1", check(e => e.value = -1f));
            Assert.Equal("a negative duration", check(e => e.durationDays = -1));
            Assert.Equal("a duration out of range", check(e => e.durationDays = SaveChecks.MaxDurationDays + 1));
            Assert.Equal("the chance 2", check(e => e.chance = 2f));
            Assert.True(check(e => { e.channel = string.Empty; e.value = 2e12f; }).StartsWith("the amount"), "an amount over a trillion");
            Assert.Null(check(e => { e.channel = string.Empty; e.value = -1041000f; }), "a spending of a million is fine");
            Assert.Equal("a number that is not finite", check(e => e.value = float.PositiveInfinity));

            Assert.Equal("days out of range", SaveChecks.Instance(new EventInstanceDto { id = 1, eventId = "e", companyKey = "player", state = "Resolved", firedDay = SaveChecks.MaxDay + 1 }));
            Assert.Null(SaveChecks.Instance(new EventInstanceDto { id = 1, eventId = "e", companyKey = "player", state = "AwaitingChoice", firedDay = 100, deadlineDay = 114 }));
        }
    }
}
