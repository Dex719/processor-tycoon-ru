using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The resolver and the life of effects (FR-5, NFR-5, TSK-010): <c>Resolve</c> once per effect of every choice and of the event when the instance is created,
    /// the ids and the texts from the resolved numbers only, a failing handler gives <c>Failed</c> and the day goes on, a timed effect lives from its day until
    /// <c>startDay + durationDays</c>; and the public API on the logic (FR-6, TSK-014): a test effect type registered like a pack's (AC-6.1), the bus
    /// notified after the effects and isolated from its subscribers (AC-6.2), <c>ApplyDirect</c> saved, loaded and expiring like any effect (AC-6.3), the chain of AI
    /// choosers (AC-6.4).
    /// </summary>
    [Covers(typeof(EffectResolver), typeof(InstanceFactory), typeof(EffectRuntime), typeof(EffectRuntimeOptions), typeof(IEffectHandlerPort), typeof(IEffectTable),
        typeof(TypeTable<>), typeof(PriorityList<>), typeof(AiChooserChain), typeof(EventLogStore), typeof(SaveChecks), typeof(IEventSavePart),
        typeof(EventInstanceDto), typeof(ChoiceOptionDto), typeof(AppliedEffect), typeof(EventInstance), typeof(EventLogEntry), typeof(EffectRecord), typeof(ChoiceOption))]
    public class EffectRuntimeTests
    {
        private const int Today = 7000;

        private static EffectsRig RigWith(params EventDefinition[] definitions)
        {
            var rig = new EffectsRig(new FakeCatalog(Fx.DesignLimits(), definitions));
            rig.World.Today = Today;
            return rig;
        }

        [Test]
        public void Resolve_RunsOncePerEffect_ForEveryChoiceAndTheEvent_WhenTheInstanceIsCreated()
        {
            var port = new CountingPort("Test", false);
            EventDefinition definition = Fx.Event("es.counted",
                new[] { Fx.Choice("a", Fx.Effect("Test"), Fx.Effect("Test")), Fx.Choice("b", Fx.Effect("Test")), Fx.Choice("c") },
                Fx.Effect("Test"), Fx.Effect("Test", "{ 'scope': 'Global' }"));
            EffectsRig rig = RigWith(definition);
            rig.Table.Add(port);

            rig.Fire("es.counted", CompanyKey.Player);
            Assert.Equal(5, port.Resolves, "2 + 1 + 0 of the choices and 2 of the event, once each");
            Assert.Equal(2, port.Applies, "the event's own effects are applied at once, the choices wait");

            EventInstanceDto instance = rig.Instance("es.counted");
            Assert.Equal("1/a/0,1/a/1|1/b/0|", string.Join("|", instance.options.ConvertAll(o => string.Join(",", o.effects.ConvertAll(e => e.id).ToArray())).ToArray()));
            Assert.Equal("1/common/0", instance.common[0].id);
            Assert.Equal(CompanyKey.Player, instance.common[0].companyKey);
            Assert.Equal(EventCompanyKeys.Global, instance.common[1].companyKey, "scope Global");
            Assert.Equal(EventInstanceStates.AwaitingChoice, instance.state);
            Assert.Equal(Today + 14, instance.deadlineDay, "firedDay + timerDays until the mail is delivered");

            rig.Runtime.ResolveChoice(instance.id, "a", false, Today + 1);
            Assert.Equal(5, port.Resolves, "applying a choice resolves nothing again (D1)");
            Assert.Equal(4, port.Applies);
            Assert.True(rig.History.HasChosen("es.counted", "a", CompanyKey.Player), "es.choiceMade");
        }

        [Test]
        public void AnAllCompaniesGroup_SharesTheGroupId_AndCarriesTheGlobalEffectOnce()
        {
            EventDefinition definition = Fx.Event("es.shock", null,
                Fx.Effect("MarketDemand", "{ 'segment': 'lowend', 'multiplier': 0.8, 'scope': 'Global' }", 30),
                Fx.Effect("Reputation", "{ 'delta': -1 }"));
            definition.targets.type = EventTargetType.AllCompanies;
            EffectsRig rig = RigWith(definition);

            rig.Fire("es.shock");
            Assert.Equal(4, rig.Entries(EventLogKinds.Fired).Count, "the player and 3 AI companies");
            Assert.Near(0.8, rig.Channel("market.demand.lowend", "ai:2"), 1e-6, "applied once: not 0.8^4");
            Assert.Equal(4, rig.Game.CallsStartingWith("reputation "), "the target effect on every company");

            EventInstanceDto first = rig.Runtime.Instances[0];
            Assert.Equal(first.id, first.groupId);
            Assert.Equal(1, rig.Runtime.Instances.Count, "only the instance with the running global effect stays");
        }

        [Test]
        public void AFailingHandler_MarksItsEffectFailed_AndTheRestOfTheEventAndTheDayGoOn()
        {
            var resolveFails = new CountingPort("BadResolve", false) { ThrowOnResolve = true };
            var applyFails = new CountingPort("BadApply", false) { ThrowOnApply = true };
            EventDefinition definition = Fx.Event("es.faulty", null,
                Fx.Effect("BadResolve"), Fx.Effect("BadApply"), Fx.Effect("Reputation", "{ 'delta': 2 }"), Fx.Effect("NoSuchType"));
            EventDefinition after = Fx.Event("es.after", null, Fx.Effect("Reputation", "{ 'delta': 1 }"));
            EffectsRig rig = RigWith(definition, after);
            rig.Table.Add(resolveFails).Add(applyFails);

            Assert.Equal(EventFireResult.Fired, rig.Fire("es.faulty", CompanyKey.Player));
            EventLogEntry fired = rig.Entries(EventLogKinds.Fired)[0];
            Assert.Equal(4, fired.effects.Count);
            Assert.True(applyFails.Describes > 0, "the effect whose apply fails was described at resolve");
            Assert.Equal(2f, rig.Game.Reputation[CompanyKey.Player], "the good effect is applied");

            List<EventLogEntry> failed = rig.Entries(EventLogKinds.EffectFailed);
            Assert.Equal(1, failed.Count);
            Assert.Equal(3, failed[0].effects.Count, "resolve, apply and the unknown type");
            Assert.Equal(3, rig.Faults.Count, string.Join("; ", rig.Faults));

            Assert.Equal(EventFireResult.Fired, rig.Fire("es.after", CompanyKey.Player), "the next event works");
            Assert.Equal(3f, rig.Game.Reputation[CompanyKey.Player]);
        }

        [Test]
        public void Describe_TakesItsNumbersFromTheAppliedEffectOnly()
        {
            var resolver = new EffectResolver(PortTable.Standard(new FakeEffectWorld(), new ScheduledFires(new SchedulerState(), new EventHistory())), null, null, null);
            var effect = new AppliedEffect { type = "Money", value = -1041000f, state = AppliedEffectStates.Pending };
            resolver.Describe(effect);
            Assert.Equal("es.fx.money.spend", effect.textKey);
            Assert.Equal("$1.04M", effect.textArgs[0]);

            effect.value = -2000000f;   // the saved number is the only source
            resolver.Describe(effect);
            Assert.Equal("$2M", effect.textArgs[0]);

            var unknown = new AppliedEffect { type = "Gone", state = AppliedEffectStates.Pending };
            resolver.Describe(unknown);
            Assert.Equal(EffectResolver.FailedTextKey, unknown.textKey);
            Assert.Equal("1/common/2", EffectResolver.EffectId(1, null, 2));
        }

        [Test]
        public void ATimedEffect_LivesFromItsDay_UntilStartPlusDuration_AndIsRemovedThatDay()
        {
            EventDefinition definition = Fx.Event("es.strike", null, Fx.Effect("FactoryOutput", "{ 'multiplier': 0.6 }", 21));
            EffectsRig rig = RigWith(definition);

            rig.Fire("es.strike", "ai:1");
            AppliedEffect effect = rig.Runtime.ActiveEffects[0];
            Assert.Equal(AppliedEffectStates.Active, effect.state);
            Assert.Equal(Today, effect.startDay);
            Assert.Equal(Today + 21, effect.endDay);
            Assert.Near(0.6, rig.Channel("production.output", "ai:1"), 1e-6);

            rig.RunDays(Today + 1, Today + 20);
            Assert.Equal(AppliedEffectStates.Active, effect.state, "still on the day before its end");
            rig.RunDays(Today + 21, Today + 21);
            Assert.Equal(AppliedEffectStates.Done, effect.state);
            Assert.Equal(0, rig.Runtime.ActiveEffects.Count);
            Assert.Equal(1f, rig.Channel("production.output", "ai:1"), "the contribution is gone at once");
            Assert.Equal(1, rig.Entries(EventLogKinds.EffectEnded).Count);
            Assert.True(rig.Bus.Contains("expired " + effect.id), "OnEffectExpired");
            Assert.Equal(0, rig.Runtime.Instances.Count, "a resolved instance leaves the store when its last effect ends");
        }

        [Test]
        public void AC_6_1_ATestEffectTypeRegisteredLikeAPacks_IsValidatedAndWorks()
        {
            var table = new TypeTable<IEffectHandlerPort>();
            var warnings = new List<string>();
            var ramp = new CountingPort("TestRamp", true) { Value = 1.3f, Channel = "research.speed" };
            Assert.True(table.Register("TestRamp", ramp, warnings.Add));
            Assert.False(table.Register("TestRamp", new CountingPort("TestRamp", true), warnings.Add), "a second registration of the type");
            Assert.Equal(1, warnings.Count, "warned");
            Assert.True(warnings[0].Contains("is registered already"), warnings[0]);
            Assert.True(ReferenceEquals(ramp, table.Get("TestRamp")), "the first stays");
            Assert.Equal("TestRamp", string.Join(",", table.Types));
            Assert.False(table.Register(null, ramp, warnings.Add));

            // The validator accepts the type once it is registered...
            EventTestKit.WithWorld(env =>
            {
                env.EffectTypes.Add("TestRamp");
                var messages = new List<string>();
                Assert.True(EventTestKit.Check(env, "event-system", EventTestKit.Record("'effects': [ { 'type': 'TestRamp', 'durationDays': 60, 'params': { 'peak': 1.3 } } ]"), messages),
                    EventTestKit.Join(messages));
            });

            // ...and the runtime runs it like a standard one.
            EventDefinition definition = Fx.Event("es.ramp", null, Fx.Effect("TestRamp", "{ 'peak': 1.3 }", 60));
            EffectsRig rig = RigWith(definition);
            rig.Table.Add(ramp);
            rig.Fire("es.ramp", CompanyKey.Player);
            Assert.Near(1.3, rig.Channel("research.speed"), 1e-6);
            rig.RunDays(Today + 1, Today + 3);
            Assert.Equal(3, ramp.Ticks, "ticked every day");
        }

        [Test]
        public void AC_6_2_TheBusIsToldAfterTheEffects_AndAThrowingSubscriberStopsNothing()
        {
            EventDefinition definition = Fx.Event("es.vote", new[] { Fx.Choice("no"), Fx.Choice("yes", Fx.Effect("Reputation", "{ 'delta': 5 }")) });
            EffectsRig rig = RigWith(definition);
            var seen = new List<string>();
            rig.Options.ChoiceResolved = (instance, choice, auto) =>
            {
                seen.Add("reputation " + rig.Game.Reputation[CompanyKey.Player]);
                throw new InvalidOperationException("a subscriber fails");
            };

            rig.Fire("es.vote", CompanyKey.Player);
            Assert.True(rig.Runtime.ResolveChoice(rig.Instance("es.vote").id, "yes", false, Today));

            Assert.Equal("reputation 5", string.Join(",", seen), "the effect was applied before the subscriber ran");
            Assert.Equal(5f, rig.Game.Reputation[CompanyKey.Player]);
            Assert.Equal(1, rig.Entries(EventLogKinds.Resolved).Count, "the journal has the choice");
            Assert.True(rig.Faults.Exists(f => f.StartsWith("bus/resolved")), "the failure is reported: " + string.Join("; ", rig.Faults));
            Assert.Equal(EventFireResult.Fired, rig.Fire("es.vote", CompanyKey.Player), "the engine goes on");
        }

        [Test]
        public void AC_6_3_ApplyDirect_IsSaved_LoadedOnDay30_ActiveUntilDay90_AndInTheJournalWithItsSource()
        {
            EffectsRig rig = RigWith();
            EventEffect spec = Fx.Effect("ModifierTimed", "{ 'channel': 'market.demand.lowend', 'multiplier': 0.9 }", 90);

            AppliedEffect effect = rig.Runtime.ApplyDirect(spec, EventCompanyKeys.Global, "hist.1985.crash", new[] { "1985" }, Today);
            Assert.NotNull(effect);
            Assert.Equal(0.9f, effect.value);
            Assert.Equal(Today + 90, effect.endDay);
            Assert.Near(0.9, rig.Channel("market.demand.lowend", "ai:1"), 1e-6);
            EventInstanceDto direct = rig.Runtime.Find(effect.instanceId);
            Assert.Equal(EventInstanceKinds.Direct, direct.kind);
            Assert.Equal("hist.1985.crash", direct.eventId, "the source id is the event id");
            Assert.Equal("hist.1985.crash", rig.Entries(EventLogKinds.Fired)[0].eventId, "the journal shows the source");

            rig.RunDays(Today + 1, Today + 30);

            // Save on day 30, load into a fresh engine (the JSON of the side file in between).
            var section = new EventSaveSection(rig.State, rig.History, line => Assert.Fail("save: " + line), rig.Runtime);
            string text = ModJson.Write(section.Write(), false);
            EffectsRig loaded = RigWith();
            var section2 = new EventSaveSection(loaded.State, loaded.History, line => Assert.Fail("load: " + line), loaded.Runtime);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the section is JSON: " + error);
            section2.Read((JsonObject)node, EventSaveSection.Schema);
            Assert.Equal(text, ModJson.Write(section2.Write(), false), "the same section after a round trip");

            loaded.Runtime.AfterLoad(Today + 30);
            Assert.Near(0.9, loaded.Channel("market.demand.lowend", "ai:1"), 1e-6, "restored after the load");
            Assert.Equal(1, loaded.Runtime.ActiveEffects.Count);
            Assert.Equal(Today + 90, loaded.Runtime.ActiveEffects[0].endDay, "60 days left");
            Assert.Equal("hist.1985.crash", loaded.Log.Entries[0].eventId);
            Assert.True(loaded.Runtime.NextInstanceId > effect.instanceId, "ids go on");

            loaded.RunDays(Today + 31, Today + 89);
            Assert.Near(0.9, loaded.Channel("market.demand.lowend", "ai:1"), 1e-6, "still active on day 89");
            loaded.RunDays(Today + 90, Today + 90);
            Assert.Equal(1f, loaded.Channel("market.demand.lowend", "ai:1"), "expired like any effect");
            Assert.Equal("hist.1985.crash", loaded.Entries(EventLogKinds.EffectEnded)[0].eventId);

            Assert.Null(rig.Runtime.ApplyDirect(Fx.Effect("NoSuchType"), CompanyKey.Player, "x", null, Today), "an unknown type");
            Assert.Null(rig.Runtime.ApplyDirect(spec, "bank", "x", null, Today), "not a company key");
            Assert.NotNull(rig.Runtime.ApplyDirect(new EventEffect { type = "Reputation" }, CompanyKey.Player, "x", null, Today), "params may be missing");
        }

        [Test]
        public void AC_6_4_TheChainOfChoosers_ByPriority_TheFirstNonNullAnswerWins_AnExceptionIsNull()
        {
            var chain = new PriorityList<string>();
            chain.Add("p10", 10);
            chain.Add("p100", 100);
            chain.Add("p10b", 10);
            chain.Add("p50", 50);
            Assert.Equal("p100,p50,p10,p10b", string.Join(",", chain.Items), "descending priority, equal ones in the order of adding");
            chain.Add("p10", 200);
            Assert.Equal("p10,p100,p50,p10b", string.Join(",", chain.Items), "adding again moves it");
            Assert.True(chain.Remove("p50"));
            Assert.False(chain.Remove("p50"));
            Assert.Equal(3, chain.Count);

            var answers = new Dictionary<string, string> { { "p100", null }, { "p10", "settle" } };
            var asked = new List<string>();
            var two = new PriorityList<string>();
            two.Add("p10", 10);
            two.Add("p100", 100);
            Func<string, string> ask = c =>
            {
                asked.Add(c);
                return answers[c];
            };

            Assert.Equal("settle", AiChooserChain.Decide(two.Items, null, ask, id => true, null), "100 says null for es.x, 10 decides");
            Assert.Equal("p100,p10", string.Join(",", asked));

            answers["p100"] = "fight";
            asked.Clear();
            Assert.Equal("fight", AiChooserChain.Decide(two.Items, null, ask, id => true, null));
            Assert.Equal("p100", string.Join(",", asked), "the second chooser is not asked");

            var faults = new List<string>();
            asked.Clear();
            Assert.Equal("settle", AiChooserChain.Decide(two.Items, null, c => c == "p100" ? Throw() : ask(c), id => true, (c, e) => faults.Add(c)),
                "an exception counts as null");
            Assert.Equal("p100", string.Join(",", faults));

            Assert.Equal("settle", AiChooserChain.Decide(two.Items, c => c != "p100", ask, id => true, null), "a filter skips a chooser without asking it");
            Assert.Null(AiChooserChain.Decide(two.Items, null, c => "fight", id => id == "settle", null), "an unavailable choice counts as null");
        }

        [Test]
        public void Fire_AnswersFired_Scheduled_Skipped_UnknownEvent_Disabled_WithTheRealRuntime()
        {
            EventDefinition once = Fx.Event("es.once", null, Fx.Effect("Reputation", "{ 'delta': 1 }"));
            once.oncePerGame = true;
            EventDefinition many = Fx.Event("es.many", null, Fx.Effect("Reputation", "{ 'delta': 1 }"));
            EffectsRig rig = RigWith(once, many);

            Assert.Equal(EventFireResult.UnknownEvent, rig.Fire("es.nothing", CompanyKey.Player));
            Assert.Equal(EventFireResult.Scheduled, rig.Fire("es.once", CompanyKey.Player, 5));
            Assert.Equal(0, rig.Entries(EventLogKinds.Fired).Count, "a delayed firing creates nothing yet");
            Assert.Equal(EventFireResult.Fired, rig.Fire("es.once", CompanyKey.Player));
            Assert.Equal(1, rig.Entries(EventLogKinds.Fired).Count, "the instance exists when Fire returns");
            Assert.True(rig.Bus.Contains("fired es.once player"), "OnEventFired");
            Assert.Equal(EventFireResult.Skipped, rig.Fire("es.once", CompanyKey.Player), "oncePerGame is used up");
            Assert.Equal(EventFireResult.Skipped, rig.Fire("es.many", "ai:77"), "no such company");

            rig.FrozenPacks.Add(Defs.Pack);
            Assert.Equal(EventFireResult.Disabled, rig.Fire("es.once", CompanyKey.Player), "the pack is switched off");
        }

        [Test]
        public void AnAiCompany_DecidesOnTheDayOfTheFiring_AndThePlayerWaitsForTheDeadline()
        {
            EventDefinition definition = Fx.Event("es.strike",
                new[] { Fx.Choice("refuse", Fx.Effect("Reputation", "{ 'delta': -3 }")), Fx.Choice("raise", Fx.Effect("Reputation", "{ 'delta': 1 }")) });
            EffectsRig rig = RigWith(definition);
            EventInstance asked = null;
            rig.Options.DecideForAi = instance =>
            {
                asked = instance;
                return "raise";
            };

            rig.Fire("es.strike", "ai:2");
            Assert.NotNull(asked);
            Assert.Equal("es.strike", asked.Definition.id);
            Assert.False(asked.IsPlayer);
            Assert.Equal(1f, rig.Game.Reputation["ai:2"]);
            Assert.True(rig.Bus.Contains("resolved es.strike raise auto"));
            Assert.Equal(1, rig.Entries(EventLogKinds.AutoResolved).Count);

            rig.Fire("es.strike", CompanyKey.Player);
            EventInstanceDto player = rig.Instance("es.strike");
            Assert.Equal(1, rig.History.Active.PendingChoices(CompanyKey.Player), "the player's open choice is in the index");
            rig.RunDays(Today + 1, Today + 13);
            Assert.Equal(EventInstanceStates.AwaitingChoice, player.state);
            rig.RunDays(Today + 14, Today + 14);
            Assert.Equal(EventInstanceStates.Expired, player.state, "the deadline closes it with the default choice");
            Assert.Equal("refuse", player.chosen);
            Assert.True(player.auto);
            Assert.Equal(-3f, rig.Game.Reputation[CompanyKey.Player]);
            Assert.Equal(0, rig.History.Active.PendingChoices(CompanyKey.Player));
            Assert.True(EffectRuntime.DefaultDeadlineDue(player, Today + 14));
        }

        [Test]
        public void AFrozenPack_KeepsItsEffects_TheyDoNotTick_AndTheyStillEndOnTheirDay()
        {
            var ramp = new CountingPort("TestRamp", true) { Value = 1.5f, Channel = "research.speed" };
            EventDefinition definition = Fx.Event("ms.ramp", null, Fx.Effect("TestRamp", "{}", 10));
            definition.packId = "market-shocks";
            var catalog = new FakeCatalog(Fx.DesignLimits(), definition).Pack("market-shocks", new PackOptions { SettingsFeatureId = "market-shocks" });
            var rig = new EffectsRig(catalog);
            rig.Table.Add(ramp);
            rig.World.Today = Today;
            rig.Fire("ms.ramp", CompanyKey.Player);
            Assert.Near(1.5, rig.Channel("research.speed"), 1e-6);

            rig.Runtime.SyncPacks(new[] { "market-shocks" }, pack => false);
            Assert.Equal(AppliedEffectStates.Suspended, rig.Runtime.ActiveEffects[0].state);
            Assert.Equal(1f, rig.Channel("research.speed"), "the contribution is out while frozen");
            rig.RunDays(Today + 1, Today + 3);
            Assert.Equal(0, ramp.Ticks, "a frozen effect does not tick");

            rig.Runtime.SyncPacks(new[] { "market-shocks" }, pack => true);
            Assert.Near(1.5, rig.Channel("research.speed"), 1e-6, "back after the thaw");
            rig.Runtime.SuspendPack("market-shocks");
            rig.RunDays(Today + 4, Today + 10);
            Assert.Equal(0, rig.Runtime.ActiveEffects.Count, "the end date is absolute");
            Assert.Equal(1, ramp.Removes);
        }

        [Test]
        public void ATickThatThrows_EndsTheEffectAsFailed()
        {
            var ramp = new CountingPort("TestRamp", true) { ThrowOnTick = true };
            EffectsRig rig = RigWith(Fx.Event("es.ramp", null, Fx.Effect("TestRamp", "{}", 10)));
            rig.Table.Add(ramp);
            rig.Fire("es.ramp", CompanyKey.Player);

            rig.RunDays(Today + 1, Today + 1);
            Assert.Equal(0, rig.Runtime.ActiveEffects.Count);
            Assert.Equal(1, rig.Entries(EventLogKinds.EffectFailed).Count);
            Assert.Equal(1, ramp.Removes, "removed from the game");
        }

        [Test]
        public void TheSave_KeepsWaitingInstances_AndDropsWhatAHandEditCouldBreak()
        {
            EventDefinition definition = Fx.Event("es.vote", new[] { Fx.Choice("no"), Fx.Choice("yes", Fx.Effect("Reputation", "{ 'delta': 5 }")) });
            EffectsRig rig = RigWith(definition);
            rig.Fire("es.vote", CompanyKey.Player);
            var dto = new EventSystemSaveDto();
            rig.Runtime.Export(dto);
            Assert.Equal(1, dto.instances.Count);
            Assert.Equal(2, dto.nextInstanceId);

            JsonObject json = (JsonObject)ModJson.From(dto);
            var bindErrors = new List<string>();
            EventSystemSaveDto back = ModJson.Bind<EventSystemSaveDto>(json, bindErrors);
            Assert.Equal(0, bindErrors.Count, string.Join("; ", bindErrors));
            var problems = new List<string>();
            back.instances[0].options[1].effects[0].durationDays = -5;                 // a negative duration
            back.instances.Add(new EventInstanceDto { id = 9, eventId = "es.vote", companyKey = "bank", state = EventInstanceStates.AwaitingChoice });
            back.instances.Add(new EventInstanceDto
            {
                id = 10, eventId = "es.x", companyKey = CompanyKey.Player, state = EventInstanceStates.Resolved,
                common = new List<AppliedEffect>
                {
                    new AppliedEffect { id = "10/common/0", instanceId = 10, type = "ModifierTimed", channel = "taxes", value = 50f, state = AppliedEffectStates.Active, startDay = 1, endDay = 100 },
                },
            });
            back.log.Add(new EventLogEntry { kind = "Exploded", eventId = "es.vote" });

            EffectsRig loaded = RigWith(definition);
            loaded.Runtime.Import(back, problems);
            Assert.Equal(1, loaded.Runtime.Instances.Count, "the bad company and the instance with nothing left are dropped");
            Assert.Equal(0, loaded.Runtime.Instances[0].options[1].effects.Count, "the effect with a negative duration is dropped");
            Assert.Equal(4, problems.Count, string.Join("; ", problems));
            Assert.Equal(1, loaded.Log.Count, "the bad journal entry is dropped");
            Assert.Null(SaveChecks.Instance(rig.Instance("es.vote")));
            Assert.Equal("the multiplier 50", SaveChecks.Effect(new AppliedEffect { id = "1/a/0", instanceId = 1, type = "T", channel = "x", value = 50f, state = "Active" },
                new EventInstanceDto { id = 1 }));

            loaded.Runtime.AfterLoad(Today);
            Assert.Equal(1, loaded.History.Active.PendingChoices(CompanyKey.Player), "the index is built again after the load");
            Assert.True(loaded.Runtime.ResolveChoice(1, "no", false, Today));
        }

        [Test]
        public void AfterLoad_CancelsTheInstancesOfEventsNoLongerInTheRegistry()
        {
            EventDefinition kept = Fx.Event("es.kept", new[] { Fx.Choice("a") });
            EventDefinition gone = Fx.Event("es.gone", new[] { Fx.Choice("a") });
            EffectsRig rig = RigWith(kept, gone);
            rig.Fire("es.kept", CompanyKey.Player);
            rig.Fire("es.gone", CompanyKey.Player);
            var dto = new EventSystemSaveDto();
            rig.Runtime.Export(dto);

            EffectsRig loaded = RigWith(kept);
            loaded.Runtime.Import(dto, new List<string>());
            loaded.Runtime.AfterLoad(Today + 1);
            Assert.Equal(1, loaded.Runtime.Instances.Count);
            Assert.Equal("es.kept", loaded.Runtime.Instances[0].eventId);
            Assert.Equal(1, loaded.Entries(EventLogKinds.Cancelled).Count);
        }

        [Test]
        public void TheJournal_KeepsTheNewestEntriesUpToItsLimit()
        {
            int limit = 3;
            var log = new EventLogStore(() => limit);
            var added = new List<int>();
            log.Added += entry => added.Add(entry.day);
            for (int day = 1; day <= 5; day++) log.Add(new EventLogEntry { day = day, eventId = "es.x", kind = EventLogKinds.Fired });

            Assert.Equal(3, log.Count);
            Assert.Equal(3, log.Entries[0].day, "the oldest are dropped");
            Assert.Equal(5, added.Count);

            EventLogEntry entry = EventLogStore.Entry(9, new EventInstanceDto { id = 4, eventId = "es.y", companyKey = "ai:1" }, EventLogKinds.Resolved, "a",
                new[] { new AppliedEffect { type = "Money", value = -5000f, hit = true, clamped = true, channel = null } });
            Assert.Equal(4, entry.instanceId);
            Assert.Equal("a", entry.choiceId);
            Assert.Equal(string.Empty, entry.effects[0].channel);
            Assert.True(entry.effects[0].clamped);
            Assert.Equal(-5000f, entry.effects[0].value);

            var defaults = new EventLogStore();
            for (int i = 0; i < 450; i++) defaults.Add(new EventLogEntry { day = i, eventId = "es.x", kind = EventLogKinds.Fired });
            Assert.Equal(400, defaults.Count, "AC-8.2: 400 by default");
        }

        [Test]
        public void TheChoiceCost_IsTheSpendingThatHits()
        {
            var option = new ChoiceOptionDto
            {
                choiceId = "lawyers",
                effects = new List<AppliedEffect>
                {
                    new AppliedEffect { type = "Money", value = -10000f, hit = true },
                    new AppliedEffect { type = "Money", value = -40000f, hit = false },
                    new AppliedEffect { type = "Money", value = 5000f, hit = true },
                    new AppliedEffect { type = "Reputation", value = -3f, hit = true },
                },
            };
            Assert.Equal(10000f, EffectMath.ChoiceCost(option));
            var choice = new ChoiceOption { ChoiceId = "lawyers", Available = true, Cost = EffectMath.ChoiceCost(option), AiWeight = 0.5f };
            Assert.Equal(10000f, choice.Cost);
            Assert.Equal(0f, EffectMath.ChoiceCost(null));
        }

        private static string Throw()
        {
            throw new InvalidOperationException("a chooser fails");
        }
    }
}
