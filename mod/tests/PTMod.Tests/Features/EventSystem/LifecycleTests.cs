using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The feature switched off and on in the middle of a game (FR-11 items 2-4, D10; TSK-023): nothing is removed or created again while it is off (AC-11.1), the player's
    /// choices get back the days it stood still (AC-11.3), the effects that ended meanwhile are closed at once, letters delivered meanwhile give their delivery day, and
    /// the same for a single pack whose switch is off. Also the gate in front of the section: a hand-over by the core after <c>GameReady</c> does not replace the state in
    /// memory, and a feature that was off at <c>GameReady</c> makes its start when it comes on. The core's rule for a feature that is off - its providers are passed by -
    /// is the real one (<c>ChannelRegistry</c> with the switch).
    /// </summary>
    [Covers(typeof(EventRuntime), typeof(SessionGate), typeof(BatchGuard), typeof(ReadyKind))]
    public class LifecycleTests
    {
        private static readonly int Start = Defs.Day(1991, 1, 1);

        private static LifecycleRig NewGame(int day, params EventDefinition[] definitions)
        {
            return NewGame(day, new FakeCatalog(WithRoom(), definitions));
        }

        private static LifecycleRig NewGame(int day, FakeCatalog catalog)
        {
            var game = new LifecycleRig(catalog);
            foreach (EventDefinition definition in catalog.All)
            {
                foreach (EventChoice choice in definition.choices ?? new EventChoice[0])
                {
                    if (!string.IsNullOrEmpty(choice.textKey)) game.Choice.Text.With(choice.textKey, "Вариант " + choice.id, "Option " + choice.id);
                }
            }
            game.Choice.SetDay(day);
            game.GameReady = true;
            game.Lifecycle.OnGameReady(ReadyKind.NewGame);
            return game;
        }

        /// <summary>The configuration of the tests: ten open choices of the player, the limits of the design (<c>*</c> [0.25; 4]) on the channels.</summary>
        private static EventConfig WithRoom()
        {
            var pack = new PackConfig { PackId = "event-system", MaxPendingChoices = 10 };
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "*", Min = 0.25f, Max = 4f, PackId = "event-system" });
            return EventConfig.Merge(new[] { pack }, line => { });
        }

        private static EventDefinition Vote(string id = "es.vote", string pack = null)
        {
            EventDefinition definition = ChoiceEvents.Make(id, new[]
            {
                Fx.Choice("a", Fx.Effect("Reputation", "{ 'delta': 1 }")),
                Fx.Choice("b", Fx.Effect("Reputation", "{ 'delta': 5 }")),
            });
            if (pack != null) definition.packId = pack;
            return definition;
        }

        private static EventEffect Demand(int days)
        {
            return Fx.Effect("ModifierTimed", "{ 'channel': 'market.demand.highend', 'multiplier': 0.8 }", days);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The gate in front of the section
        // ---------------------------------------------------------------------------------------------------------------

        private sealed class CountingSection : ISideFileParticipant
        {
            public int Resets;
            public int Reads;
            public int Writes;

            public string SectionId { get { return "event-system"; } }
            public int SchemaVersion { get { return 1; } }

            public void ResetToDefaults()
            {
                Resets++;
            }

            public void Read(JsonObject section, int schema)
            {
                Reads++;
            }

            public JsonObject Write()
            {
                Writes++;
                return new JsonObject();
            }
        }

        [Test]
        public void TheGate_TakesWhatTheCoreHandsOutBeforeGameReady_IgnoresAHandOverAfterIt_AndReportsALateOne()
        {
            var inner = new CountingSection();
            bool ready = false;
            var infos = new List<string>();
            var gate = new SessionGate(inner, () => ready, infos.Add);
            Assert.Equal("event-system", gate.SectionId);
            Assert.Equal(1, gate.SchemaVersion);

            // a load, a new game, the menu: before GameReady the core is right
            gate.Read(new JsonObject(), 1);
            gate.ResetToDefaults();
            Assert.Equal(1, inner.Reads);
            Assert.Equal(1, inner.Resets);
            Assert.False(gate.Bound);
            Assert.False(gate.HasLateData);

            // the game becomes ready: the state is the game's
            ready = true;
            gate.Bind();
            Assert.True(gate.Bound);
            gate.Read(new JsonObject(), 1);
            gate.ResetToDefaults();
            Assert.Equal(1, inner.Reads, "a hand-over after GameReady (the feature came back from off) would replace the newer state in memory");
            Assert.Equal(1, inner.Resets);
            Assert.Equal(2, infos.Count, "and the log says so");
            gate.Write();
            Assert.Equal(1, inner.Writes, "writing is never held back");

            // the scene is left, the next load reads again; a section that arrives after GameReady to a state that holds nothing is a late one
            gate.Unbind();
            ready = false;
            gate.Read(new JsonObject(), 1);
            Assert.Equal(2, inner.Reads);
            ready = true;
            gate.Unbind();
            gate.Read(new JsonObject(), 1);
            Assert.Equal(3, inner.Reads);
            Assert.True(gate.HasLateData);
            Assert.True(gate.TakeLate());
            Assert.False(gate.TakeLate(), "once");
            Assert.True(gate.Bound);

            Assert.Throws<ArgumentNullException>(() => new SessionGate(null, () => true));
            Assert.Throws<ArgumentNullException>(() => new SessionGate(inner, null));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // AC-11.1, AC-11.3
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AC_11_1_ASwitchedOffFeature_StandsStill_AndWhenOnAgainTheMultiplierIsBackWithItsDate()
        {
            LifecycleRig game = NewGame(Defs.Day(1991, 1, 1));
            int end = Defs.Day(1992, 1, 1);
            AppliedEffect effect = game.Rig.Runtime.ApplyDirect(Demand(end - game.Today), EventCompanyKeys.Global, "hist.x", null, game.Today);
            Assert.Equal(end, effect.endDay);
            Assert.Near(0.8, game.Rig.Channel("market.demand.highend", "ai:1"), 1e-6);
            game.Choice.Days(game.Today + 1, Defs.Day(1991, 6, 1));
            Assert.Near(0.8, game.Rig.Channel("market.demand.highend", "ai:1"), 1e-6);

            int providers = game.Rig.Sink.Registry.ProvidersOf("event-system");
            int invalidations = game.Rig.Sink.Invalidations.Count;
            Assert.Equal(1, providers);

            game.SwitchOff();   // 1991-06-01
            Assert.True(game.Lifecycle.IsFrozen);
            Assert.Equal(Defs.Day(1991, 6, 1), game.Lifecycle.FrozenSince);
            Assert.Equal(1f, game.Rig.Channel("market.demand.highend", "ai:1"), "ModModifiers.Get = 1.0: the core passes by the provider of a feature that is off");
            Assert.Equal(1, game.Rig.Runtime.ActiveEffects.Count, "the effect is not dropped");
            Assert.True(game.Save().Contains("market.demand.highend"), "the record is in the section that would be written");
            Assert.Equal(providers, game.Rig.Sink.Registry.ProvidersOf("event-system"), "no provider is removed");
            Assert.Equal(invalidations, game.Rig.Sink.Invalidations.Count, "nothing is invalidated, nothing recreated by OnDisabled");

            game.Choice.SetDay(Defs.Day(1991, 9, 1));   // three months pass; the engine is not called
            game.SwitchOn();
            Assert.False(game.Lifecycle.IsFrozen);
            Assert.Near(0.8, game.Rig.Channel("market.demand.highend", "ai:1"), 1e-6, "back at 0.8");
            Assert.Equal(end, game.Rig.Runtime.ActiveEffects[0].endDay, "the end date is the same");
            Assert.Equal(providers, game.Rig.Sink.Registry.ProvidersOf("event-system"), "no second provider");
            Assert.Equal(0, game.Faults.Count, string.Join("; ", game.Faults));

            game.Choice.Days(Defs.Day(1991, 9, 2), end - 1);
            Assert.Near(0.8, game.Rig.Channel("market.demand.highend", "ai:1"), 1e-6, "still on the last day");
            game.Choice.Days(end, end);
            Assert.Equal(1f, game.Rig.Channel("market.demand.highend", "ai:1"), "gone on 1992-01-01");
            Assert.Equal(1, game.Rig.Entries(EventLogKinds.EffectEnded).Count);
        }

        [Test]
        public void TheHandOverOfTheCore_WhenTheFeatureComesBack_DoesNotReplaceTheStateInMemory()
        {
            LifecycleRig game = NewGame(Start, Vote());
            game.Rig.Runtime.ApplyDirect(Demand(90), EventCompanyKeys.Global, "hist.x", null, Start);
            string older = game.Save();
            game.Choice.Days(Start + 1, Start + 10);
            game.Choice.FireAndDeliver("es.vote");

            game.SwitchOff();
            game.Choice.SetDay(Start + 20);
            game.SwitchOn();

            // ModSave.OnFeatureStateChanged hands the participant its data again: the text it remembers (older) or, with none, a reset
            game.Gate.ResetToDefaults();
            game.Gate.Read(LifecycleRig.Parse(older), EventSaveSection.Schema);

            Assert.NotNull(game.Rig.Instance("es.vote"), "the choice of day 10 is still there");
            Assert.Equal(1, game.Rig.Runtime.ActiveEffects.Count);
            Assert.Near(0.8, game.Rig.Channel("market.demand.highend", "ai:1"), 1e-6);
            Assert.False(game.Gate.HasLateData);
            Assert.True(game.Infos.Exists(line => line.Contains("ignored")), "the log says the hand-over was ignored: " + string.Join("; ", game.Infos));
        }

        [Test]
        public void AC_11_3_TheTimersOfThePlayersChoices_StandStillWhileTheFeatureIsOff()
        {
            LifecycleRig game = NewGame(Start, Vote());
            EventInstanceDto instance = game.Choice.FireAndDeliver("es.vote");
            FakeMail.Letter letter = game.Choice.Mail.OfInstance(instance.id);
            game.Choice.Days(Start + 1, Start + 4);
            Assert.Equal(Start + 14, instance.deadlineDay);
            Assert.Equal("Осталось 10 из 14 дн.", letter.Status, "ten days left when the feature is switched off");

            game.SwitchOff();
            game.Choice.SetDay(Start + 24);   // twenty days in which the engine does nothing
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state, "the timer did not run");
            game.SwitchOn();

            Assert.Equal(Start + 34, instance.deadlineDay, "the deadline moved by the twenty days");
            Assert.Equal("Осталось 10 из 14 дн.", letter.Status, "ten days left, as before");
            Assert.Null(letter.Answer, "the letter is open");

            game.Choice.Days(Start + 25, Start + 33);
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            Assert.Equal("Осталось 1 из 14 дн.", letter.Status);
            game.Choice.Day(Start + 34);
            Assert.Equal(1, game.Rig.Entries(EventLogKinds.AutoResolved).Count, "and the default choice comes on the new deadline");
            Assert.Equal("a", letter.Answer);
        }

        [Test]
        public void OnlyTheChoicesThatWait_GetTheirDaysBack()
        {
            LifecycleRig game = NewGame(Start, Vote("es.one"), Vote("es.two"));
            EventInstanceDto open = game.Choice.FireAndDeliver("es.one");
            EventInstanceDto answered = game.Choice.FireAndDeliver("es.two");
            game.Choice.ClickLetter(answered.id, "b");
            Assert.Null(game.Rig.Instance("es.two"), "answered: it is gone");
            int closedDeadline = answered.deadlineDay;

            game.SwitchOff();
            game.Choice.SetDay(Start + 7);
            game.SwitchOn();
            Assert.Equal(Start + 14 + 7, open.deadlineDay);
            Assert.Equal(closedDeadline, answered.deadlineDay, "an answered choice keeps its record as it was");
            Assert.Equal(0, game.Choice.Choices.ShiftDeadlines(0), "zero days move nothing");
            Assert.Equal(0, game.Choice.Choices.ShiftDeadlines(-5), "and days back are not a thing");
            Assert.Equal(Start + 14 + 7, open.deadlineDay);
            Assert.Equal(int.MaxValue, BatchGuard.AddDays(int.MaxValue - 3, 100), "a deadline cannot wrap around");
        }

        [Test]
        public void TheEffectsThatEndedWhileTheFeatureWasOff_AreClosedAtOnce_WithAJournalEntry()
        {
            LifecycleRig game = NewGame(Defs.Day(1991, 4, 2));
            int end = Defs.Day(1991, 8, 1);
            game.Rig.Runtime.ApplyDirect(Demand(end - game.Today), EventCompanyKeys.Global, "hist.x", null, game.Today);
            game.Rig.Runtime.ApplyDirect(Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.2 }", 300), EventCompanyKeys.Global, "hist.y", null, game.Today);
            game.Choice.Days(game.Today + 1, Defs.Day(1991, 6, 1));

            game.SwitchOff();
            game.Choice.SetDay(Defs.Day(1991, 9, 1));
            game.SwitchOn();

            Assert.Equal(1f, game.Rig.Channel("market.demand.highend", "ai:1"), "the one that ended on 08-01 does not come back");
            Assert.Near(1.2, game.Rig.Channel("research.speed", "ai:1"), 1e-6, "the other one does");
            Assert.Equal(1, game.Rig.Entries(EventLogKinds.EffectEnded).Count, "the journal has the end");
            Assert.Equal(Defs.Day(1991, 9, 1), game.Rig.Entries(EventLogKinds.EffectEnded)[0].day, "written on the day the feature came back");
            Assert.Equal(1, game.Rig.Runtime.ActiveEffects.Count);
        }

        [Test]
        public void ALetterDeliveredWhileTheFeatureWasOff_GivesItsDeliveryDay_WhenOn()
        {
            LifecycleRig game = NewGame(Start, Vote());
            game.Choice.HoldMail = true;
            game.Rig.Fire("es.vote", CompanyKey.Player);
            EventInstanceDto instance = game.Rig.Instance("es.vote");
            Assert.True(ChoiceController.WaitsForDelivery(instance), "the letter is in the queue");

            game.SwitchOff();
            FakeMail.Letter letter = game.Choice.Mail.OfInstance(instance.id);
            letter.Delivered = true;                 // the mail service delivers; the handler of the feature that is off is not called
            letter.DeliveredDay = Start + 5;
            game.Choice.SetDay(Start + 8);
            game.SwitchOn();

            Assert.Equal(Start + 5, instance.deliveredDay);
            Assert.Equal(Start + 14 + 8, instance.deadlineDay, "the eight days of the pause are added; the delivery would have given only day 5 + 14");
            Assert.False(ChoiceController.WaitsForDelivery(instance));
            Assert.Equal("Осталось 14 из 14 дн.", letter.Status);
        }

        [Test]
        public void ASwitchOffTwice_RemembersTheFirstDay()
        {
            LifecycleRig game = NewGame(Start, Vote());
            EventInstanceDto instance = game.Choice.FireAndDeliver("es.vote");
            game.SwitchOff();
            game.Choice.SetDay(Start + 5);
            game.Lifecycle.OnDisabled();   // the core reports it again (Faulted, then Disabled)
            Assert.Equal(Start, game.Lifecycle.FrozenSince);

            game.Choice.SetDay(Start + 9);
            game.SwitchOn();
            Assert.Equal(Start + 14 + 9, instance.deadlineDay, "nine days, not four");
            game.Lifecycle.OnEnabled();
            Assert.Equal(Start + 14 + 9, instance.deadlineDay, "and a second OnEnabled moves nothing");
        }

        [Test]
        public void ASwitchInTheMenu_OrBeforeGameReady_HoldsNothing()
        {
            var game = new LifecycleRig(Vote());
            game.Choice.SetDay(0);
            game.Lifecycle.OnDisabled();
            Assert.False(game.Lifecycle.IsFrozen, "no game, no state to freeze");
            game.Lifecycle.OnEnabled();
            Assert.Equal(0, game.Faults.Count);

            game = NewGame(Start, Vote());
            game.Lifecycle.OnSceneLeaving();
            game.SwitchOff();
            Assert.False(game.Lifecycle.IsFrozen, "the scene was left: the state belongs to no game");
            Assert.False(game.Gate.Bound);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // A pack whose own switch is off: the others are not touched
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void APackWhoseSwitchIsOff_StandsStill_ItsPlayersChoiceKeepsItsTimer_AndTheOtherPacksRunOn()
        {
            EventDefinition shocks = Vote("ms.vote", "market-shocks");
            EventDefinition history = Vote("hist.vote", "historical-events");
            EventDefinition boom = Fx.Event("ms.boom", null, Fx.Effect("ModifierTimed", "{ 'channel': 'market.demand.highend', 'multiplier': 0.7 }", 100));
            boom.packId = "market-shocks";
            EventDefinition law = Fx.Event("hist.law", null, Fx.Effect("ModifierTimed", "{ 'channel': 'taxes', 'multiplier': 1.3 }", 100));
            law.packId = "historical-events";
            FakeCatalog catalog = new FakeCatalog(WithRoom(), shocks, history, boom, law)
                .Pack("market-shocks", new PackOptions { SettingsFeatureId = "market-shocks" })
                .Pack("historical-events", new PackOptions { SettingsFeatureId = "historical-events" });
            LifecycleRig game = NewGame(Start, catalog);
            var packs = new[] { "market-shocks", "historical-events" };

            EventInstanceDto frozenVote = game.Choice.FireAndDeliver("ms.vote");
            EventInstanceDto otherVote = game.Choice.FireAndDeliver("hist.vote");
            game.Rig.Fire("ms.boom", CompanyKey.Player);
            game.Rig.Fire("hist.law", CompanyKey.Player);
            game.Choice.Days(Start + 1, Start + 4);

            // the feature of the pack market-shocks is switched off in the hub (ModFeatures.StateChanged): the engine freezes the pack
            game.Rig.Runtime.SyncPacks(packs, pack => pack != "market-shocks");
            Assert.Equal(1f, game.Rig.Channel("market.demand.highend"), "its contribution is out");
            Assert.Near(1.3, game.Rig.Channel("taxes"), 1e-6, "the other pack is not touched");

            game.Choice.Days(Start + 5, Start + 20);
            Assert.Equal(EventInstanceStates.AwaitingChoice, frozenVote.state, "the frozen pack's choice has not expired");
            Assert.Equal(Start + 14 + 16, frozenVote.deadlineDay, "its deadline moved by each of the sixteen days");
            Assert.Equal("Осталось 10 из 14 дн.", game.Choice.Mail.OfInstance(frozenVote.id).Status, "ten days left when it was frozen, ten now");
            Assert.Equal(EventInstanceStates.Expired, otherVote.state, "the other pack's choice ran out on its day");

            game.Rig.Runtime.SyncPacks(packs, pack => true);
            Assert.Near(0.7, game.Rig.Channel("market.demand.highend"), 1e-6, "back after the thaw");
            Assert.Equal(Start + 100, game.Rig.Runtime.ActiveEffects.First(e => e.type == "ModifierTimed" && e.channel == "market.demand.highend").endDay, "with its own end date");

            game.Choice.Days(Start + 21, Start + 29);
            Assert.Equal(EventInstanceStates.AwaitingChoice, frozenVote.state);
            game.Choice.Day(Start + 30);
            Assert.Equal(EventInstanceStates.Expired, frozenVote.state, "ten days after the thaw, on the new deadline");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // A feature that was off at GameReady; time the saved state did not run
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void AFeatureThatWasOffAtGameReady_MakesItsStartWhenItComesOn_AndTheTimeItWasOffIsNotCharged()
        {
            int saveDay = Defs.Day(1988, 4, 10);
            EventDefinition strike = Fx.Event("es.strike", null, Fx.Effect("FactoryOutput", "{ 'multiplier': 0.6 }", 40));
            LifecycleRig first = NewGame(Defs.Day(1988, 4, 1), Vote(), strike);
            first.Rig.Fire("es.strike", "ai:1");
            first.Choice.SetDay(Defs.Day(1988, 4, 6));
            first.Choice.FireAndDeliver("es.vote");
            first.Choice.Days(Defs.Day(1988, 4, 7), saveDay);
            string text = first.Save();
            int deadline = first.Rig.Instance("es.vote").deadlineDay;

            // the player loads this save with the feature switched off: the core hands nothing out and the handler of GameReady is not called
            LifecycleRig loaded = new LifecycleRig(Vote(), strike);
            loaded.Choice.Text.With("es.t.a", "Принять", "Accept").With("es.t.b", "Отказаться", "Refuse");
            loaded.Choice.SetDay(saveDay);
            LifecycleRig.CopyLetters(first.Choice.Mail, loaded.Choice.Mail);
            loaded.GameReady = true;
            Assert.Equal(0, loaded.Rig.Runtime.Instances.Count);

            // twelve days later the player switches it on: OnEnabled, then the core hands the section over, then StateChanged completes the start
            loaded.Choice.SetDay(saveDay + 12);
            loaded.Lifecycle.OnEnabled();
            Assert.Equal(0, loaded.Rig.Runtime.Instances.Count, "OnEnabled alone has nothing to restore");
            loaded.Gate.Read(LifecycleRig.Parse(text), EventSaveSection.Schema);
            Assert.True(loaded.Gate.HasLateData);
            Assert.True(loaded.Lifecycle.CompleteLateStart(ReadyKind.Loaded));
            Assert.False(loaded.Lifecycle.CompleteLateStart(ReadyKind.Loaded), "once");

            EventInstanceDto vote = loaded.Rig.Instance("es.vote");
            Assert.NotNull(vote, "the saved choice is back");
            Assert.Equal(deadline + 12, vote.deadlineDay, "the twelve days the feature was off are not charged");
            Assert.Equal(1, loaded.PackLoads);
            Assert.Near(0.6, loaded.Rig.Channel("production.output", "ai:1"), 1e-6, "the multiplier of the save is back");
            Assert.Equal(0, loaded.Faults.Count, string.Join("; ", loaded.Faults));
        }

        [Test]
        public void ALoadWithTheFeatureOn_ChargesNoTimeAfterAnOrdinarySave_ButAnOlderSectionKeepsTheTimersStill()
        {
            LifecycleRig first = NewGame(Start, Vote());
            first.Choice.FireAndDeliver("es.vote");
            first.Choice.Days(Start + 1, Start + 3);
            string text = first.Save();
            int deadline = first.Rig.Instance("es.vote").deadlineDay;

            LifecycleRig same = new LifecycleRig(Vote());
            same.Choice.SetDay(Start + 3);
            LifecycleRig.CopyLetters(first.Choice.Mail, same.Choice.Mail);
            same.Load(text);
            Assert.Equal(deadline, same.Rig.Instance("es.vote").deadlineDay, "saved and loaded on the same day: nothing moves");

            // the section is older than the game: it was written by the last save made while the feature was on, the player played on with it off
            LifecycleRig later = new LifecycleRig(Vote());
            later.Choice.SetDay(Start + 40);
            LifecycleRig.CopyLetters(first.Choice.Mail, later.Choice.Mail);
            later.Load(text);
            Assert.Equal(deadline + 37, later.Rig.Instance("es.vote").deadlineDay, "37 days in which nobody ran the engine");
            Assert.True(later.Infos.Exists(line => line.Contains("stood still for 37")), string.Join("; ", later.Infos));
        }

        [Test]
        public void TheSceneLeaving_UnbindsTheState_AndForgetsAPause()
        {
            LifecycleRig game = NewGame(Start, Vote());
            Assert.True(game.Gate.Bound);
            game.SwitchOff();
            Assert.True(game.Lifecycle.IsFrozen);

            game.Lifecycle.OnSceneLeaving();
            Assert.False(game.Gate.Bound);
            Assert.False(game.Lifecycle.IsFrozen);
            game.GameReady = false;
            game.Gate.Read(new JsonObject(), 1);
            Assert.False(game.Gate.HasLateData, "before GameReady the hand-over is the ordinary one");
        }

        [Test]
        public void AStepThatFails_IsReported_AndTheRestOfTheStartStillRuns()
        {
            LifecycleRig game = new LifecycleRig(Vote());
            var faults = new List<string>();
            int synced = 0;
            var lifecycle = new EventRuntime(game.Rig.Runtime, game.Rig.Scheduler, game.Choice.Choices, game.Choice.Ui, game.Gate, () => 100,
                () => { throw new InvalidOperationException("the packs are broken"); }, () => synced++, (site, error) => faults.Add(site), null);

            lifecycle.OnGameReady(ReadyKind.NewGame);
            Assert.Equal("ready/packs", string.Join(",", faults));
            Assert.Equal(1, synced, "the packs are brought in line with their switches all the same");
            Assert.Equal(100, game.Rig.State.LastProcessedDay, "and the new game starts from today");

            Assert.Throws<ArgumentNullException>(() => new EventRuntime(null, game.Rig.Scheduler, game.Choice.Choices, game.Choice.Ui, game.Gate, () => 1, null, null, null, null));
        }
    }
}
