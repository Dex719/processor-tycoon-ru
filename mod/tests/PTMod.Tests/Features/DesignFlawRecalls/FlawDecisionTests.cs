using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>A scripted <see cref="IFlawDecisionHost"/>: what the event system answers is set by the test; every firing and every letter is recorded.</summary>
    internal sealed class FakeDecisionHost : IFlawDecisionHost
    {
        public sealed class Firing
        {
            public string EventId;
            public string CompanyKey;
            public string[] Args;
        }

        public sealed class Mail
        {
            public int RecordId;
            public FlawResponse Response;
            public string EventId;
        }

        public FlawBalance Balance_ = FlawBalance.Defaults();
        public FlawFireOutcome Outcome = FlawFireOutcome.Fired;
        public int Day = FlawCycleKit.Day1990 + 100;

        /// <summary>Outcomes by firing number (0 is the first), for a test that wants the second firing to fail.</summary>
        public readonly Dictionary<int, FlawFireOutcome> OutcomeOfFiring = new Dictionary<int, FlawFireOutcome>();

        public readonly List<Firing> Fired = new List<Firing>();
        public readonly List<Mail> Mails = new List<Mail>();
        public readonly List<int> AiDispatched = new List<int>();
        public readonly List<string> Logs = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool AiAnswers;
        public bool ThrowOnFire;
        public bool ThrowOnMail;

        public int Today
        {
            get { return Day; }
        }

        public FlawBalance Balance
        {
            get { return Balance_; }
        }

        public string[] BuildArgs(FlawRecord record, FlawQuote quote)
        {
            return FlawDecisionPlanner.BuildArgs(record, quote, "Nova 3", "arithmetic erratum", "major", TestFormat.Instance);
        }

        public FlawFireOutcome Fire(string eventId, string companyKey, string[] args)
        {
            if (ThrowOnFire) throw new InvalidOperationException("the event system is broken");

            int number = Fired.Count;
            Fired.Add(new Firing { EventId = eventId, CompanyKey = companyKey, Args = args });
            FlawFireOutcome outcome;
            return OutcomeOfFiring.TryGetValue(number, out outcome) ? outcome : Outcome;
        }

        public void SendAutoMail(FlawRecord record, FlawResponse response, string eventId)
        {
            if (ThrowOnMail) throw new InvalidOperationException("the mail is broken");
            Mails.Add(new Mail { RecordId = record.Id, Response = response, EventId = eventId });
        }

        public bool DispatchAi(FlawRecord record)
        {
            AiDispatched.Add(record.Id);
            return AiAnswers;
        }

        public void Log(string line)
        {
            Logs.Add(line);
        }

        public void Warn(string line)
        {
            Warnings.Add(line);
        }

        public void Error(string line, Exception exception)
        {
            Errors.Add(line);
        }
    }

    /// <summary>
    /// TSK-010 (FR-5; AC-5.1, AC-5.2, AC-5.3, AC-5.4): the six events of the pack, which one opens for which flaw, the arguments of the texts, the single open window and the
    /// queue behind it, the entry of the clients waiting for the answer, and the default choice applied with a letter when the event system cannot show the window.
    /// </summary>
    [Covers(typeof(FlawDecisionPlanner), typeof(FlawDecisionKind), typeof(IFlawFormat), typeof(FlawFireOutcome), typeof(FlawDispatchResult), typeof(IFlawDecisionHost),
        typeof(FlawDecisionQueue), typeof(FlawDecisionEngine))]
    public class FlawDecisionTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        private sealed class DecisionWorld
        {
            public readonly ResponseWorld World;
            public readonly FakeDecisionHost Host = new FakeDecisionHost();
            public readonly FlawDecisionEngine Engine;

            public DecisionWorld(params FlawRecord[] records)
            {
                World = new ResponseWorld(records);
                World.SetDay(Day);
                Engine = new FlawDecisionEngine(World.Store, World.Engine, World.Book, Host);
            }

            public FlawRecord Record(int id)
            {
                return World.Store.Find(id);
            }

            /// <summary>The flaw is found now: a hidden record becomes public and its owner is asked, as the monitor does it at the discovery.</summary>
            public FlawDispatchResult Found(FlawRecord record)
            {
                Assert.True(FlawStateMachine.Discover(record, DiscoverySource.External, Day), "the record was hidden");
                return Engine.Dispatch(record);
            }
        }

        /// <summary>A flaw that is not found yet: <see cref="DecisionWorld.Found"/> finds it.</summary>
        private static FlawRecord Hidden(int id, int cpu, string type = "security", int year = 2018)
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Hidden, id, FlawSeverity.Major, type, year: year);
            record.Data.baseCpuId = cpu;
            record.Data.cpuIds = new List<int> { cpu };
            return record;
        }

        private static FlawRecord Public(int id = 1, string type = "security", int year = 2018, FlawSeverity severity = FlawSeverity.Major)
        {
            return ResponseWorld.Known(FlawState.Public, id, severity, type, year: year);
        }

        // ---- the events (design.md, "Контент: events.json") ----

        [Test]
        public void EventIds_AreTheSixOfTheDesign()
        {
            Assert.Equal(6, FlawDecisionPlanner.EventIds.Length);
            Assert.Equal("dfr.decide.public", FlawDecisionPlanner.PublicEvent);
            Assert.Equal("dfr.decide.public.nopatch", FlawDecisionPlanner.PublicNoPatchEvent);
            Assert.Equal("dfr.decide.internal", FlawDecisionPlanner.InternalEvent);
            Assert.Equal("dfr.decide.internal.nopatch", FlawDecisionPlanner.InternalNoPatchEvent);
            Assert.Equal("dfr.decide.leaked", FlawDecisionPlanner.LeakedEvent);
            Assert.Equal("dfr.decide.leaked.nopatch", FlawDecisionPlanner.LeakedNoPatchEvent);
            foreach (string id in FlawDecisionPlanner.EventIds) Assert.True(FlawDecisionPlanner.IsDecisionEvent(id), id);

            Assert.False(FlawDecisionPlanner.IsDecisionEvent("es.something"));
            Assert.False(FlawDecisionPlanner.IsDecisionEvent("dfr.other"));
            Assert.False(FlawDecisionPlanner.IsDecisionEvent(null));
            Assert.False(FlawDecisionPlanner.IsDecisionEvent(""));
        }

        [Test]
        public void EventIdFor_PicksTheEventBySituation_AndDropsThePatchButtonWhenThereIsNone()
        {
            Assert.Equal("dfr.decide.public", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Public, true));
            Assert.Equal("dfr.decide.public.nopatch", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Public, false));
            Assert.Equal("dfr.decide.internal", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Internal, true));
            Assert.Equal("dfr.decide.internal.nopatch", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Internal, false));
            Assert.Equal("dfr.decide.leaked", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Leaked, true));
            Assert.Equal("dfr.decide.leaked.nopatch", FlawDecisionPlanner.EventIdFor(FlawDecisionKind.Leaked, false));
        }

        [Test]
        public void DefaultChoice_IsThePatchIfThereIsOne_ElseIgnore_AndAfterALeakElseTheRevision()
        {
            Assert.Equal("patch", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Public, true));
            Assert.Equal("ignore", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Public, false), "FR-5 item 4 (the decision of the owner)");
            Assert.Equal("patch", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Internal, true));
            Assert.Equal("ignore", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Internal, false));
            Assert.Equal("patch", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Leaked, true));
            Assert.Equal("stepping", FlawDecisionPlanner.DefaultChoiceFor(FlawDecisionKind.Leaked, false), "the way of ignoring is closed after a leak");
        }

        [Test]
        public void Choices_MapToTheResponses_BothWays()
        {
            foreach (string choice in new[] { "recall", "stepping", "patch", "ignore" })
            {
                FlawResponse response = FlawDecisionPlanner.ResponseOfChoice(choice);
                Assert.True(response != FlawResponse.None, choice);
                Assert.Equal(choice, FlawDecisionPlanner.ChoiceOfResponse(response));
            }
            Assert.Equal(FlawResponse.None, FlawDecisionPlanner.ResponseOfChoice("something"));
            Assert.Equal(FlawResponse.None, FlawDecisionPlanner.ResponseOfChoice(null));
            Assert.Null(FlawDecisionPlanner.ChoiceOfResponse(FlawResponse.None));
        }

        [Test]
        public void KindOf_SeparatesPublicInternalAndLeaked()
        {
            Assert.Equal(FlawDecisionKind.Public, FlawDecisionPlanner.KindOf(ResponseWorld.Known(FlawState.Public)));
            Assert.Equal(FlawDecisionKind.Internal, FlawDecisionPlanner.KindOf(ResponseWorld.Known(FlawState.InternalKnown)));
            Assert.Equal(FlawDecisionKind.Internal, FlawDecisionPlanner.KindOf(null));

            FlawRecord leaked = ResponseWorld.Known(FlawState.Public, coverUp: true);
            leaked.Source = DiscoverySource.Leak;
            Assert.Equal(FlawDecisionKind.Leaked, FlawDecisionPlanner.KindOf(leaked));

            FlawRecord coverUpOnly = ResponseWorld.Known(FlawState.Public, coverUp: true);
            Assert.Equal(FlawDecisionKind.Public, FlawDecisionPlanner.KindOf(coverUpOnly), "only a leak closes the way of ignoring");
        }

        [Test]
        public void EventsFile_SaysWhatThePlannerSays()
        {
            JsonObject root = FlawTestKit.ParseObject(FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json"));
            Assert.Equal(1, root.GetInt32("schema"));
            JsonArray records = root.GetArray("records");
            Assert.NotNull(records);
            Assert.Equal(6, records.Count, "six events");

            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            FlawBalance balance = FlawBalance.Defaults();
            var seen = new HashSet<string>();

            foreach (JsonNode node in records)
            {
                var record = (JsonObject)node;
                string id = record.GetString("id");
                Assert.True(seen.Add(id), id + " twice");
                Assert.True(Array.IndexOf(FlawDecisionPlanner.EventIds, id) >= 0, id + " is an event of the planner");
                Assert.True(id.StartsWith("dfr.", StringComparison.Ordinal), id + " carries the prefix of the feature");

                Assert.True(record.GetBool("important"), id + ": important (a Critical letter, a window and a pause)");
                Assert.Equal(balance.Responses.TimerDays, record.GetInt32("timerDays"), id + ": timerDays equals responses.timerDays of flaws.json");
                Assert.Equal("Manual", record.GetObject("trigger").GetString("type"), id + ": fired from code only");
                Assert.Equal("Specific", record.GetObject("targets").GetString("type"));
                Assert.Equal("player", record.GetObject("targets").GetString("companyKey"));

                foreach (string field in new[] { "title", "text", "cause" })
                {
                    string key = record.GetObject(field).GetString("key");
                    Assert.True(ru.ContainsKey(key) && en.ContainsKey(key), id + "." + field + ": '" + key + "' is in both tables");
                    Assert.True(key.StartsWith("dfr.", StringComparison.Ordinal), key);
                }

                bool nopatch = id.EndsWith(".nopatch", StringComparison.Ordinal);
                bool leaked = id.StartsWith("dfr.decide.leaked", StringComparison.Ordinal);
                var choices = new List<string>();
                foreach (JsonNode choiceNode in record.GetArray("choices"))
                {
                    var choice = (JsonObject)choiceNode;
                    string choiceId = choice.GetString("id");
                    choices.Add(choiceId);

                    string textKey = choice.GetString("textKey");
                    Assert.Equal("dfr.choice." + choiceId, textKey);
                    Assert.True(ru.ContainsKey(textKey) && en.ContainsKey(textKey), textKey + " is in both tables");

                    JsonArray effects = choice.GetArray("effects");
                    Assert.Equal(1, effects.Count, id + "/" + choiceId + ": one effect per button");
                    string type = ((JsonObject)effects[0]).GetString("type");
                    Assert.Equal(FlawEffectTypes.TypeOf(FlawDecisionPlanner.ResponseOfChoice(choiceId)), type, id + "/" + choiceId);
                }

                var expected = new List<string> { "recall", "stepping" };
                if (!nopatch) expected.Add("patch");
                if (!leaked) expected.Add("ignore");
                Assert.Equal(string.Join(",", expected), string.Join(",", choices), id + ": the buttons");

                FlawDecisionKind kind = id.StartsWith("dfr.decide.public", StringComparison.Ordinal) ? FlawDecisionKind.Public
                    : leaked ? FlawDecisionKind.Leaked : FlawDecisionKind.Internal;
                Assert.Equal(FlawDecisionPlanner.EventIdFor(kind, !nopatch), id);
                Assert.Equal(FlawDecisionPlanner.DefaultChoiceFor(kind, !nopatch), record.GetString("defaultChoice"), id + ": defaultChoice");
                Assert.True(choices.Contains(record.GetString("defaultChoice")), id + ": the default is one of the buttons");
            }
            Assert.Equal(6, seen.Count);
        }

        [Test]
        public void EventsFile_Texts_UseOnlyThePositionalArgumentsTheDispatcherGives()
        {
            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            JsonArray records = FlawTestKit.ParseObject(FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json")).GetArray("records");

            foreach (JsonNode node in records)
            {
                foreach (string field in new[] { "title", "text", "cause" })
                {
                    string key = ((JsonObject)((JsonObject)node).GetObject(field)).GetString("key");
                    foreach (JsonObject table in new[] { ru, en })
                    {
                        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(table.GetString(key), @"\{(\d+)\}"))
                        {
                            int index = int.Parse(m.Groups[1].Value);
                            Assert.True(index > 0 && index < FlawDecisionPlanner.ArgCount, key + ": {" + index + "} is an argument of the firing ({0} is the id of the record, for the effects)");
                        }
                    }
                }
            }
        }

        [Test]
        public void EventsFile_ConfigDefinesTheCategoryOfTheEvents()
        {
            JsonObject config = FlawTestKit.ParseObject(FlawTestKit.ReadSource("Content/design-flaw-recalls/config.json"));
            Assert.Equal(1, config.GetInt32("schema"));

            var categories = new HashSet<string>();
            foreach (JsonNode node in config.GetArray("categories")) categories.Add(((JsonObject)node).GetString("id"));
            foreach (JsonNode node in FlawTestKit.ParseObject(FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json")).GetArray("records"))
            {
                Assert.True(categories.Contains(((JsonObject)node).GetString("category")), "the category of the event is defined by the config of the pack");
            }
        }

        [Test]
        public void EventsFile_IdsPassTheIdentifierRules()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            try
            {
                ModIds.Load(prefixes);
                foreach (string id in FlawDecisionPlanner.EventIds)
                {
                    Assert.True(ModIds.IsEventIdAllowed("design-flaw-recalls", id), ModIds.Explain(IdKind.EventId, "design-flaw-recalls", id));
                }
                Assert.True(ModIds.IsTagAllowed("design-flaw-recalls", "dfr:auto:17"), "the tag of the letter about an automatic decision (FR-5 item 7)");
                Assert.True(ModIds.IsL10nKeyAllowed("design-flaw-recalls", "dfr.mail.auto.title"));
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the arguments (design.md, Component 8) ----

        [Test]
        public void BuildArgs_AreTheElevenOfTheDesign_InTheirPlaces()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 17, FlawSeverity.Major, "security", year: 2018);
            FlawQuote quote = record.Quote;

            string[] args = FlawDecisionPlanner.BuildArgs(record, quote, "Nova 3", "security vulnerability", "major", TestFormat.Instance);

            Assert.Equal(11, args.Length);
            Assert.Equal("17", args[0], "[0] the id of the record");
            Assert.Equal("Nova 3", args[1], "[1] the CPU");
            Assert.Equal("security vulnerability", args[2], "[2] the type");
            Assert.Equal("major", args[3], "[3] the severity");
            Assert.Equal("2000000", args[4], "[4] the affected units");
            Assert.Equal("$66000000", args[5], "[5] the recall");
            Assert.Equal("$" + Math.Round(quote.SteppingCost).ToString("0"), args[6], "[6] the revision");
            Assert.Equal("90", args[7], "[7] the days of the revision");
            Assert.Equal("$" + Math.Round(quote.PatchCost).ToString("0"), args[8], "[8] the patch");
            Assert.Equal("12", args[9], "[9] the loss in percent");
            Assert.Equal("18", args[10], "[10] the months of the patch");
        }

        [Test]
        public void BuildArgs_WithoutAQuote_AreZeros_AndTheRecordIdIsStillThere()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 5);

            string[] args = FlawDecisionPlanner.BuildArgs(record, null, null, null, null, null);

            Assert.Equal(11, args.Length);
            Assert.Equal("5", args[0]);
            Assert.Equal("", args[1]);
            Assert.Equal("0", args[4]);
            Assert.Throws<ArgumentNullException>(() => FlawDecisionPlanner.BuildArgs(null, null, null, null, null, null));
        }

        [Test]
        public void TryRecordId_ReadsTheFirstArgument()
        {
            int id;
            Assert.True(FlawDecisionPlanner.TryRecordId(new[] { "42", "x" }, out id));
            Assert.Equal(42, id);
            Assert.False(FlawDecisionPlanner.TryRecordId(null, out id));
            Assert.False(FlawDecisionPlanner.TryRecordId(new string[0], out id));
            Assert.False(FlawDecisionPlanner.TryRecordId(new[] { "abc" }, out id));
            Assert.False(FlawDecisionPlanner.TryRecordId(new[] { "0" }, out id));
            Assert.False(FlawDecisionPlanner.TryRecordId(new[] { "-3" }, out id));
        }

        // ---- the queue ----

        [Test]
        public void Queue_KeepsTheOrder_HasNoDuplicates_AndSkipsWhatIsNotValid()
        {
            var ids = new List<int>();
            var queue = new FlawDecisionQueue(() => ids);

            queue.Enqueue(3);
            queue.Enqueue(1);
            queue.Enqueue(3);
            queue.Enqueue(2);

            Assert.Equal(3, queue.Count);
            Assert.True(queue.Contains(1));
            Assert.Equal("3,1,2", string.Join(",", queue.Snapshot()));
            Assert.Equal(1, queue.TakeNext(id => id != 3), "3 is not valid any more: it is dropped, 1 is taken");
            Assert.Equal("2", string.Join(",", ids), "what was taken and what was dropped are out of the saved list");
            Assert.False(queue.Remove(9));
            Assert.True(queue.Remove(2));
            Assert.Equal(0, queue.TakeNext(id => true), "an empty queue gives 0");
            Assert.Equal(0, queue.Count);
        }

        [Test]
        public void Queue_IsTheSavedList_AndNoticesAReplacedOne()
        {
            var data = new FlawSaveData();
            var queue = new FlawDecisionQueue(() => data.pendingDecisionQueue);
            queue.Enqueue(5);
            Assert.Equal("5", string.Join(",", data.pendingDecisionQueue), "the queue is FlawSaveData.pendingDecisionQueue (FR-5 item 6)");

            data = new FlawSaveData();   // a load replaces the data
            Assert.Equal(0, queue.Count);
            Assert.Throws<ArgumentNullException>(() => new FlawDecisionQueue(null));
        }

        // ---- dispatching to the player ----

        [Test]
        public void Dispatch_Public_FiresTheEvent_ForThePlayer_WithTheArgs_AC51()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);

            FlawDispatchResult result = w.Engine.Dispatch(record);

            Assert.Equal(FlawDispatchResult.Fired, result);
            Assert.Equal(1, w.Host.Fired.Count);
            Assert.Equal("dfr.decide.public", w.Host.Fired[0].EventId);
            Assert.Equal("player", w.Host.Fired[0].CompanyKey);
            Assert.Equal(11, w.Host.Fired[0].Args.Length);
            Assert.Equal("1", w.Host.Fired[0].Args[0], "Args[0] is the id of the record");
            Assert.Equal(FlawState.Public, record.State, "nothing is applied until the player chooses");
            Assert.Equal(0, w.World.Host.Spent.Count);
        }

        [Test]
        public void Dispatch_Internal_FiresTheInternalEvent_AndMakesNoWaitingEntry()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.InternalKnown, 1, FlawSeverity.Major, "security", year: 2018);
            var w = new DecisionWorld(record);

            Assert.Equal(FlawDispatchResult.Fired, w.Engine.Dispatch(record));

            Assert.Equal("dfr.decide.internal", w.Host.Fired[0].EventId);
            Assert.Equal(0, w.World.Gateway.AddCount, "the clients know nothing: no waiting entry (FR-5 item 5 is for a public flaw)");
        }

        [Test]
        public void Dispatch_WithoutAPatchForTheType_FiresTheEventWithoutThePatchButton_AC52()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "arith", year: 1990);
            var w = new DecisionWorld(record);
            Assert.False(record.Quote.PatchAvailable);

            w.Engine.Dispatch(record);

            Assert.Equal("dfr.decide.public.nopatch", w.Host.Fired[0].EventId);
        }

        [Test]
        public void Dispatch_AfterALeak_FiresTheLeakedEvent()
        {
            FlawRecord record = Public();
            record.Data.coverUp = true;
            record.Source = DiscoverySource.Leak;
            var w = new DecisionWorld(record);

            w.Engine.Dispatch(record);

            Assert.Equal("dfr.decide.leaked", w.Host.Fired[0].EventId);
        }

        [Test]
        public void Dispatch_Public_MakesTheWaitingEntry_ForTheTimerPlusTheMargin()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);

            w.Engine.Dispatch(record);

            List<FakeModifierGateway.Entry> entries = w.World.Gateway.On(FlawChannelNames.ContractSignChance);
            Assert.Equal(1, entries.Count);
            Assert.Equal(ScopeKind.Company, entries[0].Scope.Kind);
            Assert.Equal("player", entries[0].Scope.CompanyKey);
            Assert.Near(0.8, entries[0].Multiplier, 1e-6, "pendingSignChanceMultiplier");
            Assert.Equal(44, entries[0].DurationDays, "timerDays 30 + pendingGraceDays 14");
            Assert.Equal("dfr.mod.pending", entries[0].ReasonKey);
            Assert.Equal(1, record.Data.modifierEntryIds.Count, "the id is kept in the record");
        }

        [Test]
        public void AnswerToTheWindow_TakesTheWaitingEntryAway_AndRemembersAnAutomaticChoice()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Engine.Dispatch(record);
            Assert.Equal(1, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending));

            // The event system applies the effect, then tells the bus.
            w.World.Engine.Apply(record, FlawResponse.Recall, true);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", true);

            Assert.Equal(0, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "FR-5 item 5: taken away at the decision");
            Assert.True(record.Data.automaticChoice, "AC-5.3: the timer chose it");
            Assert.Equal(FlawState.Resolved, record.State);
        }

        [Test]
        public void Answer_OfAClick_IsNotMarkedAutomatic()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Engine.Dispatch(record);

            w.World.Engine.Apply(record, FlawResponse.Patch, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "patch", false);

            Assert.False(record.Data.automaticChoice);
        }

        // ---- one window at a time (FR-5 item 6) ----

        [Test]
        public void SecondFlaw_WhileAWindowIsOpen_WaitsInTheQueue()
        {
            FlawRecord first = Public(1);
            FlawRecord second = Hidden(2, 200);
            var w = new DecisionWorld(first, second);

            Assert.Equal(FlawDispatchResult.Fired, w.Engine.Dispatch(first));
            FlawDispatchResult result = w.Found(second);

            Assert.Equal(FlawDispatchResult.Queued, result);
            Assert.Equal(1, w.Host.Fired.Count, "one window");
            Assert.Equal("2", string.Join(",", w.Engine.Queue.Snapshot()));
            Assert.Equal("2", string.Join(",", w.World.Store.Data.pendingDecisionQueue), "the queue is in the saved data");
            Assert.Equal(1, w.Engine.OpenWindows());
            Assert.Equal(2, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "the clients wait for the queued one too");
        }

        [Test]
        public void WhenTheOpenWindowIsAnswered_TheNextOfTheQueueOpens()
        {
            FlawRecord first = Public(1);
            FlawRecord second = Hidden(2, 200);
            var w = new DecisionWorld(first, second);
            w.Engine.Dispatch(first);
            w.Found(second);

            w.World.Engine.Apply(first, FlawResponse.Recall, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            Assert.Equal(2, w.Host.Fired.Count, "the second window opens");
            Assert.Equal("2", w.Host.Fired[1].Args[0]);
            Assert.Equal(0, w.Engine.Queue.Count);
            Assert.Equal(1, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "one waiting entry is left, the second record's, renewed when its window opened");
        }

        [Test]
        public void ThreeFlaws_AreShownOneAfterAnother_InTheOrderTheyCame()
        {
            FlawRecord a = Public(1);
            FlawRecord b = Hidden(2, 200);
            FlawRecord c = Hidden(3, 300);
            var w = new DecisionWorld(a, b, c);
            w.Engine.Dispatch(a);
            w.Found(b);
            w.Found(c);
            Assert.Equal(1, w.Host.Fired.Count);

            w.World.Engine.Apply(a, FlawResponse.Ignore, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "ignore", false);
            Assert.Equal(2, w.Host.Fired.Count);
            Assert.Equal("2", w.Host.Fired[1].Args[0]);

            w.World.Engine.Apply(b, FlawResponse.Ignore, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "2" }, "ignore", false);
            Assert.Equal(3, w.Host.Fired.Count);
            Assert.Equal("3", w.Host.Fired[2].Args[0]);
        }

        [Test]
        public void Pump_SkipsARecordThatWasClosedWhileItWaited()
        {
            FlawRecord first = Public(1);
            FlawRecord second = Hidden(2, 200);
            FlawRecord third = Hidden(3, 300);
            var w = new DecisionWorld(first, second, third);
            w.Engine.Dispatch(first);
            w.Found(second);
            w.Found(third);
            FlawStateMachine.Close(second);   // its owner went bankrupt, say

            w.World.Engine.Apply(first, FlawResponse.Recall, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            Assert.Equal(2, w.Host.Fired.Count);
            Assert.Equal("3", w.Host.Fired[1].Args[0], "the closed one is passed over");
        }

        [Test]
        public void Pump_AfterALoad_OpensTheHeadOfTheSavedQueue_ButOnlyIfNoWindowIsOpen()
        {
            FlawRecord answered = Public(1);
            FlawRecord queued = Public(2);
            queued.Data.cpuIds = new List<int> { 200 };
            var w = new DecisionWorld(answered, queued);
            w.Engine.Queue.Enqueue(2);

            // One window is open (record 1 waits and is not queued): nothing opens.
            w.Engine.Pump();
            Assert.Equal(0, w.Host.Fired.Count);

            // It was answered before the save; after the load the head of the queue opens.
            w.World.Engine.Apply(answered, FlawResponse.Recall, false);
            var reloaded = new FlawDecisionEngine(w.World.Store, w.World.Engine, w.World.Book, w.Host);
            reloaded.Pump();

            Assert.Equal(1, w.Host.Fired.Count);
            Assert.Equal("2", w.Host.Fired[0].Args[0]);
        }

        [Test]
        public void Forget_TakesTheRecordOutOfTheQueue()
        {
            FlawRecord a = Public(1);
            FlawRecord b = Hidden(2, 200);
            var w = new DecisionWorld(a, b);
            w.Engine.Dispatch(a);
            w.Found(b);
            Assert.Equal(1, w.Engine.Queue.Count);

            w.Engine.Forget(b);
            w.Engine.Forget(null);

            Assert.Equal(0, w.Engine.Queue.Count);
        }

        // ---- the window cannot be shown (FR-5 item 7) ----

        [Test]
        public void Disabled_AppliesTheDefaultChoice_AndSendsALetter()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Disabled;

            FlawDispatchResult result = w.Engine.Dispatch(record);

            Assert.Equal(FlawDispatchResult.AutoApplied, result);
            Assert.Equal(FlawState.Mitigated, record.State, "the default choice is the patch (there is one for security in 2018)");
            Assert.Equal(FlawResponse.Patch, record.Response);
            Assert.True(record.Data.automaticChoice);
            Assert.Equal(1, w.Host.Mails.Count, "one letter");
            Assert.Equal(1, w.Host.Mails[0].RecordId);
            Assert.Equal(FlawResponse.Patch, w.Host.Mails[0].Response);
            Assert.Equal("dfr.decide.public", w.Host.Mails[0].EventId);
            Assert.Equal(record.Quote.PatchCost, w.World.Host.Spent[0].Amount, "the cost is taken, the letter says how much");
            Assert.Equal(0, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "no one waits any more");
            Assert.Equal(1, w.Host.Warnings.Count);
        }

        [Test]
        public void Disabled_WithoutAPatch_Ignores()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "arith", year: 1990);
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Disabled;

            w.Engine.Dispatch(record);

            Assert.Equal(FlawResponse.Ignore, record.Response, "FR-5 item 4: without a patch the default is ignore");
            Assert.Equal(FlawState.Public, record.State);
        }

        [Test]
        public void Disabled_AfterALeak_WithoutAPatch_StartsARevision()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Major, "arith", year: 1990, coverUp: true);
            record.Source = DiscoverySource.Leak;
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Disabled;

            w.Engine.Dispatch(record);

            Assert.Equal(FlawResponse.Stepping, record.Response, "the way of ignoring is closed after a leak");
            Assert.Equal(FlawState.Stepping, record.State);
        }

        [Test]
        public void AnEventThatCannotBeShown_UnknownOrSkipped_IsAnsweredLikeADisabledOne()
        {
            foreach (FlawFireOutcome outcome in new[] { FlawFireOutcome.UnknownEvent, FlawFireOutcome.Skipped, FlawFireOutcome.Disabled })
            {
                FlawRecord record = Public();
                var w = new DecisionWorld(record);
                w.Host.Outcome = outcome;

                Assert.Equal(FlawDispatchResult.AutoApplied, w.Engine.Dispatch(record), outcome.ToString());
                Assert.Equal(FlawResponse.Patch, record.Response, outcome.ToString());
            }
        }

        [Test]
        public void Scheduled_CountsAsAnOpenWindow()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Scheduled;

            Assert.Equal(FlawDispatchResult.Fired, w.Engine.Dispatch(record), "the event system holds it back by its own limit and shows it later");
            Assert.Equal(0, w.Host.Mails.Count);
            Assert.Equal(FlawState.Public, record.State);
        }

        [Test]
        public void AFiringThatThrows_IsAnsweredLikeADisabledOne()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Host.ThrowOnFire = true;

            Assert.Equal(FlawDispatchResult.AutoApplied, w.Engine.Dispatch(record));

            Assert.Equal(1, w.Host.Errors.Count);
            Assert.Equal(FlawResponse.Patch, record.Response);
        }

        [Test]
        public void AFailingLetter_DoesNotUndoTheDecision()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Disabled;
            w.Host.ThrowOnMail = true;

            Assert.Equal(FlawDispatchResult.AutoApplied, w.Engine.Dispatch(record));

            Assert.Equal(FlawState.Mitigated, record.State);
            Assert.Equal(1, w.Host.Errors.Count);
        }

        [Test]
        public void IfTheDefaultChoiceCannotBeApplied_TheRecordKeepsWaiting()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Host.Outcome = FlawFireOutcome.Disabled;
            w.World.Host.ThrowOnSpend = true;

            Assert.Equal(FlawDispatchResult.Failed, w.Engine.Dispatch(record));

            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(0, w.Host.Mails.Count, "no letter about a decision that was not made");
        }

        [Test]
        public void ADisabledWindow_InAQueue_IsAnsweredAndTheNextOneOpens()
        {
            FlawRecord a = Public(1);
            FlawRecord b = Hidden(2, 200);
            FlawRecord c = Hidden(3, 300);
            var w = new DecisionWorld(a, b, c);
            w.Host.OutcomeOfFiring[1] = FlawFireOutcome.Disabled;   // the second firing (record 2) cannot be shown
            w.Engine.Dispatch(a);
            w.Found(b);
            w.Found(c);

            w.World.Engine.Apply(a, FlawResponse.Recall, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            Assert.Equal(3, w.Host.Fired.Count);
            Assert.Equal(FlawResponse.Patch, b.Response, "record 2 got its default choice");
            Assert.Equal("3", w.Host.Fired[2].Args[0], "record 3 opened after it");
            Assert.Equal(1, w.Host.Mails.Count);
        }

        // ---- a window that is never answered ----

        [Test]
        public void Sweep_ALostWindow_GetsTheDefaultChoice_AfterTheTimerAndTheLongestHoldOfTheEventSystem()
        {
            FlawRecord first = Public(1);
            FlawRecord second = Hidden(2, 200);
            var w = new DecisionWorld(first, second);
            w.Engine.Dispatch(first);
            w.Found(second);
            Assert.Equal(74, w.Engine.StaleWindowDays, "30 days of the timer + 30 days the event system may hold a firing back + 14 of margin");

            w.Host.Day = Day + 74;
            Assert.Equal(0, w.Engine.Sweep(), "not yet: the window has been open for 74 days, the limit is more than 74");
            Assert.Equal(FlawState.Public, first.State);

            w.Host.Day = Day + 75;
            w.World.SetDay(Day + 75);
            int answered = w.Engine.Sweep();

            Assert.Equal(1, answered);
            Assert.Equal(FlawResponse.Patch, first.Response, "the default choice (a patch for security in 2018)");
            Assert.Equal(1, w.Host.Mails.Count, "the player is told");
            Assert.Equal(2, w.Host.Fired.Count, "the queue goes on: the second window is open now");
            Assert.Equal("2", w.Host.Fired[1].Args[0]);
        }

        [Test]
        public void Sweep_AnAnsweredWindow_IsLeftAlone()
        {
            FlawRecord record = Public(1);
            var w = new DecisionWorld(record);
            w.Engine.Dispatch(record);
            w.World.Engine.Apply(record, FlawResponse.Recall, false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            w.Host.Day = Day + 500;
            Assert.Equal(0, w.Engine.Sweep());
            Assert.Equal(1, w.World.Host.Spent.Count);
        }

        [Test]
        public void Sweep_AWindowTheModLearnsAboutAfterALoad_StartsItsClockThen()
        {
            FlawRecord record = Public(1);
            var w = new DecisionWorld(record);   // never dispatched in this session: a load brought it back

            w.Host.Day = Day + 1000;
            Assert.Equal(0, w.Engine.Sweep(), "the first look starts the clock");

            w.Host.Day = Day + 1000 + 74;
            Assert.Equal(0, w.Engine.Sweep());
            w.Host.Day = Day + 1000 + 75;
            w.World.SetDay(Day + 1000 + 75);
            Assert.Equal(1, w.Engine.Sweep());
            Assert.Equal(FlawResponse.Patch, record.Response);
        }

        [Test]
        public void Sweep_DoesNothingOutsideTheGame_AndForAnAiCompany()
        {
            FlawRecord ai = ResponseWorld.Known(FlawState.Public, 1, company: "ai:3");
            var w = new DecisionWorld(ai);
            w.Host.Day = 0;
            Assert.Equal(0, w.Engine.Sweep(), "no game date");

            w.Host.Day = Day;
            w.Engine.Sweep();
            w.Host.Day = Day + 1000;
            Assert.Equal(0, w.Engine.Sweep(), "an AI company has no window to lose");
            Assert.Equal(FlawResponse.None, ai.Response);
        }

        // ---- other owners and other records ----

        [Test]
        public void Dispatch_ToAnAiCompany_IsHandedToItsPolicy_NotFired()
        {
            FlawRecord record = ResponseWorld.Known(FlawState.Public, 1, company: "ai:3");
            var w = new DecisionWorld(record);

            Assert.Equal(FlawDispatchResult.AiDeferred, w.Engine.Dispatch(record));
            Assert.Equal(0, w.Host.Fired.Count, "an AI company has no window");
            Assert.Equal("1", string.Join(",", w.Host.AiDispatched));

            w.Host.AiAnswers = true;
            Assert.Equal(FlawDispatchResult.AiHandled, w.Engine.Dispatch(record));
        }

        [Test]
        public void AFlawOfAnAiCompany_DoesNotBlockTheWindowOfThePlayer()
        {
            FlawRecord ai = ResponseWorld.Known(FlawState.Public, 1, company: "ai:3");
            FlawRecord mine = Public(2);
            mine.Data.cpuIds = new List<int> { 200 };
            var w = new DecisionWorld(ai, mine);
            w.Engine.Dispatch(ai);

            Assert.Equal(FlawDispatchResult.Fired, w.Engine.Dispatch(mine));
        }

        [Test]
        public void Dispatch_OfARecordThatDoesNotWait_DoesNothing()
        {
            FlawRecord resolved = ResponseWorld.Known(FlawState.Resolved);
            FlawRecord answered = Public(2);
            answered.Response = FlawResponse.Ignore;
            var w = new DecisionWorld(resolved, answered);

            Assert.Equal(FlawDispatchResult.NotPending, w.Engine.Dispatch(resolved));
            Assert.Equal(FlawDispatchResult.NotPending, w.Engine.Dispatch(answered));
            Assert.Equal(FlawDispatchResult.NotPending, w.Engine.Dispatch(null));
            Assert.Equal(0, w.Host.Fired.Count);
        }

        [Test]
        public void Dispatch_WithoutAQuote_Fails_WithAWarning()
        {
            FlawRecord record = Public();
            record.Data.quote = null;
            var w = new DecisionWorld(record);

            Assert.Equal(FlawDispatchResult.Failed, w.Engine.Dispatch(record));
            Assert.Equal(1, w.Host.Warnings.Count);
            Assert.Equal(0, w.Host.Fired.Count);
        }

        [Test]
        public void OnChoiceResolved_LeavesTheEventsOfOtherPacksAlone()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Engine.Dispatch(record);

            w.Engine.OnChoiceResolved("ms.fab_fire", new[] { "1" }, "standard", true);
            w.Engine.OnChoiceResolved(null, null, null, false);

            Assert.False(record.Data.automaticChoice);
            Assert.Equal(1, w.World.Gateway.CountReason(FlawChannelNames.ReasonPending), "the waiting entry stays: the window of the record is still open");
        }

        [Test]
        public void OnChoiceResolved_ForAnUnknownRecord_OrBadArguments_DoesNotThrow()
        {
            var w = new DecisionWorld(Public());

            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "999" }, "recall", false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "x" }, "recall", false);
            w.Engine.OnChoiceResolved("dfr.decide.public", null, "recall", false);
            w.Engine.OnChoiceResolved("dfr.decide.public", new string[0], "recall", false);
        }

        [Test]
        public void OnChoiceResolved_WhenTheEffectDidNotApplyTheChoice_AppliesItOnce()
        {
            FlawRecord record = Public();
            var w = new DecisionWorld(record);
            w.Engine.Dispatch(record);

            // The effect failed (the engine marked it Failed) and the record still waits; the bus reports the choice.
            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            Assert.Equal(FlawState.Resolved, record.State, "the choice stands");
            Assert.Equal(FlawResponse.Recall, record.Response);
            Assert.Equal(1, w.World.Host.Spent.Count);
            Assert.Equal(1, w.Host.Warnings.Count, "a warning says that the effect did not do it");
        }

        [Test]
        public void OnChoiceResolved_WhenTheRetryFails_TheRecordIsOfferedAgainBehindTheQueue()
        {
            FlawRecord a = Public(1);
            FlawRecord b = Hidden(2, 200);
            var w = new DecisionWorld(a, b);
            w.Engine.Dispatch(a);
            w.Found(b);
            w.World.Host.ThrowOnSpend = true;

            w.Engine.OnChoiceResolved("dfr.decide.public", new[] { "1" }, "recall", false);

            Assert.Equal(1, w.Host.Errors.Count, "the failure of the retry is logged");
            Assert.Equal(FlawState.Public, a.State, "still waiting");
            Assert.Equal(2, w.Host.Fired.Count, "record 2 is not blocked by it: its window opens");
            Assert.Equal("2", w.Host.Fired[1].Args[0]);
            Assert.Equal("1", string.Join(",", w.Engine.Queue.Snapshot()), "record 1 waits for a new window behind");
        }

        // ---- the game side by the sources ----

        [Test]
        public void DispatcherSource_FiresTheEventsThroughTheBus_AndSendsAnInformationLetter()
        {
            string code = Code("FlawDecisionDispatcher.cs");

            Assert.True(code.Contains("EventBus.Fire(eventId, new EventFireContext { CompanyKey = companyKey, Args = args, IgnoreConditions = true })"), "EventBus.Fire with the key of the company and the args");
            Assert.True(code.Contains("\"dfr:auto:\" + record.Id"), "the tag of the letter about an automatic decision");
            Assert.True(code.Contains("Priority = MailPriority.Normal"), "Normal priority");
            Assert.True(code.Contains("TitleKey = \"dfr.mail.auto.title\"") && code.Contains("BodyKey = \"dfr.mail.auto.body\""), "the keys of the letter");
            Assert.False(code.Contains("Choices ="), "the feature sends no choice letter of its own: the event system does (FR-5 item 1)");
            Assert.False(code.Contains("mailId"), "the letter is not kept in the record");
            Assert.True(code.Contains("EventFireResult.Fired") && code.Contains("EventFireResult.Scheduled") && code.Contains("EventFireResult.Skipped"), "the result of the firing is mapped");
        }

        [Test]
        public void DispatcherSource_GuardsItsHooks()
        {
            string code = Code("FlawDecisionDispatcher.cs");

            Assert.True(code.Contains("ModHooks.Guard<FlawDispatchResult>(FeatureId, \"decision/dispatch\""), "Dispatch");
            Assert.True(code.Contains("ModHooks.Guard(FeatureId, \"decision/resolved\""), "OnChoiceResolved");
            Assert.True(code.Contains("ModHooks.Guard(FeatureId, \"decision/pump\""), "Pump");
            Assert.True(code.Contains("ModHooks.Guard(FeatureId, \"decision/sweep\""), "Sweep");
        }

        [Test]
        public void MonitorSource_SendsTheDiscoveryAndTheLeakToTheDispatcher_AndTheConsequencesToTheApplier()
        {
            string code = Code("FlawMonitor.cs");

            Assert.True(code.Contains("_feature.Dispatcher.Dispatch(record)"), "the window of the player");
            Assert.True(code.Contains("_feature.Dispatcher.Sweep()"), "the lost windows are answered by the monthly pass");
            Assert.True(code.Contains("_feature.Applier.OnDiscovered(record)"), "the public base reputation");
            Assert.True(code.Contains("_feature.Applier.OnLeak(record)"), "the leak");
            Assert.True(code.Contains("_feature.Applier.CompleteStepping(record)"), "the end of the revision");
            Assert.True(code.Contains("_feature.Applier.ExpireMitigation(record)"), "the end of the patch");
            Assert.True(code.Contains("_feature.Applier.IgnoreDrain(record)"), "the drain");
            Assert.True(code.Contains("_feature.Applier.ClassActionFiled(record)"), "the lawsuit");
            Assert.True(code.Contains("_feature.Channels.RemoveAllOfRecord(record)"), "a closed record gives up its entries");
            Assert.False(code.Contains("IsBatchRun"), "the monitor does not look at the batch flag (FR-4 item 7)");
        }

        private static string Code(string relative)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Features", "DesignFlawRecalls", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }
    }
}
