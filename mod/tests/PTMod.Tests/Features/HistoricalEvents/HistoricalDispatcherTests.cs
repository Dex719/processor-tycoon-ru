using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;
using HistEvaluator = PTMod.Features.HistoricalEvents.ConditionEvaluator;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The dispatcher of firings (TSK-008; FR-2 item 1, FR-10, AC-10.1; design.md, Component 6): who an event is fired at, the <c>onlyIf</c> per company, the
    /// arguments of the table, the answers of the engine (unknown event, disabled) and the news that an entry did not happen. The engine is a recording fake
    /// that also applies the intensity the way the event system does, to show that the setting reaches the effects through the pack and not through the
    /// dispatcher.
    /// </summary>
    [Covers(typeof(DispatcherCore), typeof(FireReport), typeof(FireResult), typeof(IEventFirer))]
    public class HistoricalDispatcherTests
    {
        // ---- the fake engine ----

        private sealed class Call
        {
            public string EventId;
            public string CompanyKey;
            public string[] Args;
        }

        /// <summary>The fake of <c>EventBus.Fire</c>: records the calls, answers by event id, and applies a scalable multiplier with the severity of its pack.</summary>
        private sealed class FakeEngine : IEventFirer
        {
            public readonly List<Call> Calls = new List<Call>();
            public readonly Dictionary<string, FireResult> Answers = new Dictionary<string, FireResult>(StringComparer.Ordinal);
            public readonly List<double> AppliedMultipliers = new List<double>();
            public readonly Dictionary<string, KeyValuePair<double, bool>> Definitions = new Dictionary<string, KeyValuePair<double, bool>>(StringComparer.Ordinal);

            /// <summary><c>PackOptions.Severity</c>: the engine reads it whenever it resolves an effect.</summary>
            public float Severity = 1f;

            public Func<string, string, FireResult> Hook;

            public FireResult Fire(string eventId, string companyKey, string[] args)
            {
                Calls.Add(new Call { EventId = eventId, CompanyKey = companyKey, Args = args });
                if (Hook != null) return Hook(eventId, companyKey);

                KeyValuePair<double, bool> definition;
                if (Definitions.TryGetValue(eventId, out definition))
                {
                    // m' = 1 + (m - 1) * intensity, only for an effect marked scalable (FR-10 item 3).
                    AppliedMultipliers.Add(definition.Value ? 1.0 + (definition.Key - 1.0) * Severity : definition.Key);
                }

                FireResult answer;
                return Answers.TryGetValue(eventId, out answer) ? answer : FireResult.Fired;
            }

            public IEnumerable<Call> CallsOf(string eventId)
            {
                return Calls.Where(c => c.EventId == eventId);
            }
        }

        private sealed class Env
        {
            public FakeWorld World = new FakeWorld();
            public FakeEngine Engine = new FakeEngine();
            public MarketShareCache Shares;
            public HistState State = new HistState();
            public List<string> Log = new List<string>();
            public HistEvaluator Evaluator = HistEvaluator.For(null, new DateTime(2009, 5, 13));
            public DispatcherCore Dispatcher;

            public Env()
            {
                World.AddPlayer("Player Co");
                World.AddAi(0, "Intel");
                World.AddAi(1, "AMD");
                World.AddAi(10, "Cyrix", c => { c.IsFabless = true; c.HasManufacturing = false; });
                World.AddSteadyCpu(World.Find("player"), Segment.HighEnd, 10);
                Shares = World.Shares();
                Dispatcher = new DispatcherCore(Engine, World, Shares, Evaluator, State, key => "<" + key + ">", Log.Add);
            }
        }

        private static CalendarEntry Entry(string roles, string stages, string fallback = "skip", string fallbackFires = "[]", string history = null)
        {
            return HistFixture.Entry(
                "{ 'id': 'hist.2009.test', 'category': 'antitrust', 'anchor': '2009-05-13', 'window': { 'from': '2009-05-01', 'to': '2010-12-31' }," +
                "  'roles': " + roles + ", 'stages': " + stages + ", 'fallback': '" + fallback + "', 'fallbackFires': " + fallbackFires + "," +
                "  'history': " + (history ?? "{ 'titleKey': 'hist.2009.test.title', 'factKey': 'hist.2009.test.fact', 'sources': ['https://example.org/a'] }") + " }");
        }

        private static RoleResult Roles(params string[][] pairs)
        {
            var roles = new RoleResult();
            foreach (string[] pair in pairs) roles.Targets[pair[0]] = pair.Skip(1).ToList();
            return roles;
        }

        // ---- who is fired at ----

        [Test]
        public void EachFire_IsFiredForEachCompanyOfItsRole_InTheOrderOfTheRole()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [" +
                "  { 'eventId': 'hist.a', 'role': 'cpus' }, { 'eventId': 'hist.b', 'role': 'one' } ] } ]");
            // The roles are declared by the roles of the entry in a real run; here the result is handed over directly.
            RoleResult roles = Roles(new[] { "cpus", "ai:1", "player", "ai:0" }, new[] { "one", "ai:10" });

            FireReport report = env.Dispatcher.FireStage(entry, 0, roles);

            Assert.Equal(
                new[] { "hist.a@ai:1", "hist.a@player", "hist.a@ai:0", "hist.b@ai:10" },
                env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray());
            Assert.Equal(4, report.Attempts);
            Assert.Equal(4, report.Fired);
            Assert.Equal(FireOutcome.Fired, report.Outcome);
        }

        [Test]
        public void GlobalAndPlayerFires_GoToThePlayer()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' }, { 'eventId': 'hist.b', 'role': 'player' } ] } ]");

            env.Dispatcher.FireStage(entry, 0, new RoleResult());

            Assert.Equal(new[] { "player", "player" }, env.Engine.Calls.Select(c => c.CompanyKey).ToArray());
            Assert.Equal(2, env.Engine.Calls.Count);
        }

        // ---- one window for one company ----

        private const string WarStage =
            "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.decide.pricewar.aggressor', 'role': 'aggressor' }, { 'eventId': 'hist.decide.pricewar.follower', 'role': 'player' } ] } ]";

        [Test]
        public void TheFollowersFire_ForThePlayer_IsDropped_WhenThePlayerIsTheAggressor()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[ { 'name': 'aggressor', 'kind': 'leader' } ]", WarStage);

            FireReport report = env.Dispatcher.FireStage(entry, 0, Roles(new[] { "aggressor", "player" }, new[] { "player", "player" }));

            Assert.Equal(new[] { "hist.decide.pricewar.aggressor@player" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray(), "the aggressor is asked to start the war, not also to join it");
            Assert.Equal(1, report.Fired);
            Assert.Equal(1, report.Dropped);
            Assert.Equal(FireOutcome.Fired, report.Outcome);
        }

        [Test]
        public void TheFollowersFire_ForThePlayer_IsSent_WhenAnotherCompanyIsTheAggressor()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[ { 'name': 'aggressor', 'kind': 'leader' } ]", WarStage);

            FireReport report = env.Dispatcher.FireStage(entry, 0, Roles(new[] { "aggressor", "ai:0" }, new[] { "player", "player" }));

            Assert.Equal(new[] { "hist.decide.pricewar.aggressor@ai:0", "hist.decide.pricewar.follower@player" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray());
            Assert.Equal(0, report.Dropped);
        }

        [Test]
        public void ThePartnersOffer_StillGoesToThePlayer_WhoIsOnlyTheChallenger()
        {
            // The brand programme: the leader is asked to launch it; everybody else, the challenger included, is offered to join in.
            var env = new Env();
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.decide.brand.leader', 'role': 'target' }, { 'eventId': 'hist.decide.brand.coop', 'role': 'player' } ] } ]");

            env.Dispatcher.FireStage(entry, 0, Roles(new[] { "target", "ai:0" }, new[] { "second", "player" }, new[] { "player", "player" }));

            Assert.Equal(new[] { "hist.decide.brand.leader@ai:0", "hist.decide.brand.coop@player" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray());
        }

        [Test]
        public void AGroupFire_IsNotThinnedOut_ForAPlayerWhoHoldsAnotherRole()
        {
            // Only the built-in role "player" gives way; the groups (every CPU company) are fired at every member as before.
            var env = new Env();
            CalendarEntry entry = Entry("[ { 'name': 'target', 'kind': 'leader' } ]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'target' }, { 'eventId': 'hist.b', 'role': 'allCpuCompanies' } ] } ]");

            env.Dispatcher.FireStage(entry, 0, Roles(new[] { "target", "player" }, new[] { "allCpuCompanies", "player", "ai:0" }));

            Assert.Equal(new[] { "hist.a@player", "hist.b@player", "hist.b@ai:0" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray());
        }

        [Test]
        public void ARoleOfTheEntryWhoseNameIsPlayer_IsItsOwnRole_NotTheBuiltInOne()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[ { 'name': 'player', 'kind': 'leader' } ]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'player' } ] } ]");

            env.Dispatcher.FireStage(entry, 0, Roles(new[] { "player", "player" }));

            Assert.Equal(new[] { "hist.a@player" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray(), "the entry declared the role: the fire is for it");
        }

        [Test]
        public void ARoleThatIsNotInTheResult_FiresNothing()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'target' } ] } ]");

            FireReport report = env.Dispatcher.FireStage(entry, 0, new RoleResult());

            Assert.Equal(0, env.Engine.Calls.Count);
            Assert.Equal(0, report.Attempts);
            Assert.Equal(FireOutcome.Skipped, report.Outcome);
        }

        [Test]
        public void TheSecondStage_FiresItsOwnFires()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.first', 'role': 'global' } ] }," +
                "  { 'offsetDays': 90, 'fires': [ { 'eventId': 'hist.second', 'role': 'global' }, { 'eventId': 'hist.third', 'role': 'global' } ] } ]");

            env.Dispatcher.FireStage(entry, 1, new RoleResult());

            Assert.Equal(new[] { "hist.second", "hist.third" }, env.Engine.Calls.Select(c => c.EventId).ToArray());
        }

        [Test]
        public void ANullEntry_ABadStage_OrNoRoles_FireNothing_AndDoNotThrow()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]");

            Assert.Equal(0, env.Dispatcher.FireStage(null, 0, new RoleResult()).Attempts);
            Assert.Equal(0, env.Dispatcher.FireStage(entry, 5, new RoleResult()).Attempts);
            Assert.Equal(0, env.Dispatcher.FireStage(entry, -1, new RoleResult()).Attempts);
            Assert.Equal(1, env.Dispatcher.FireStage(entry, 0, null).Attempts, "no result is an empty result: global fires still go");
            Assert.Equal(1, env.Engine.Calls.Count);
        }

        // ---- the arguments ----

        [Test]
        public void TheArguments_FollowTheTableOfTheDesign()
        {
            var env = new Env();
            CalendarEntry entry = Entry(
                "[ { 'name': 'plaintiff', 'kind': 'named', 'uniqueId': 0 }, { 'name': 'defendant', 'kind': 'named', 'uniqueId': 1 } ]",
                "[ { 'offsetDays': 0, 'fires': [" +
                "  { 'eventId': 'hist.decide.patent.plaintiff', 'role': 'plaintiff', 'extraArgs': ['dispute1', 'x'] }," +
                "  { 'eventId': 'hist.decide.patent.withdrawal', 'role': 'defendant' } ] } ]");
            RoleResult roles = Roles(new[] { "plaintiff", "ai:0" }, new[] { "defendant", "ai:1" });

            env.Dispatcher.FireStage(entry, 0, roles);

            Call toPlaintiff = env.Engine.Calls[0];
            Assert.Equal("ai:0", toPlaintiff.CompanyKey);
            Assert.Equal(new[] { "hist.2009.test", "Intel", "AMD", "ai:1", "dispute1", "x" }, toPlaintiff.Args, "[0] entry, [1] who it is fired at, [2] and [3] the other one, [4..] extra");

            Call toDefendant = env.Engine.Calls[1];
            Assert.Equal("ai:1", toDefendant.CompanyKey);
            Assert.Equal(new[] { "hist.2009.test", "AMD", "Intel", "ai:0" }, toDefendant.Args, "from the other side the same two companies swap");
        }

        [Test]
        public void TheSecondCompany_IsTheFirstOtherRoleOfTheEntry_AndEmptyWhenThereIsNone()
        {
            var env = new Env();
            CalendarEntry entry = Entry(
                "[ { 'name': 'target', 'kind': 'leader' }, { 'name': 'second', 'kind': 'challenger' } ]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'target' }, { 'eventId': 'hist.b', 'role': 'global' } ] } ]");
            RoleResult roles = Roles(new[] { "target", "player" }, new[] { "second", "ai:0" });

            env.Dispatcher.FireStage(entry, 0, roles);

            Assert.Equal(new[] { "hist.2009.test", "Player Co", "Intel", "ai:0" }, env.Engine.Calls[0].Args);
            Assert.Equal(new[] { "hist.2009.test", "Player Co", "Intel", "ai:0" }, env.Engine.Calls[1].Args,
                "a global event goes to the player; the other company is the first role that is not the player");

            var alone = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]");
            env.Engine.Calls.Clear();
            env.Dispatcher.FireStage(alone, 0, new RoleResult());
            Assert.Equal(new[] { "hist.2009.test", "Player Co", string.Empty, string.Empty }, env.Engine.Calls[0].Args, "the positions never move: empty text for no second company");
        }

        [Test]
        public void ACompanyThatTheWorldDoesNotKnow_HasAnEmptyName()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'ghost' } ] } ]");

            env.Dispatcher.FireStage(entry, 0, Roles(new[] { "ghost", "ai:77" }));

            Assert.Equal(new[] { "hist.2009.test", string.Empty, string.Empty, string.Empty }, env.Engine.Calls[0].Args);
        }

        [Test]
        public void BuildArgs_ToleratesNothing()
        {
            var env = new Env();
            string[] args = env.Dispatcher.BuildArgs(null, null, null, null);
            Assert.Equal(new[] { string.Empty, string.Empty, string.Empty, string.Empty }, args);

            var spec = new FireSpec { EventId = "hist.a", ExtraArgs = new string[] { "one", null } };
            Assert.Equal(new[] { string.Empty, "Intel", string.Empty, string.Empty, "one", string.Empty }, env.Dispatcher.BuildArgs(null, null, spec, "ai:0"));
        }

        // ---- onlyIf ----

        [Test]
        public void OnlyIf_IsCheckedForEachCompany_AndADroppedOneIsNotFired()
        {
            var env = new Env();
            var asked = new List<string>();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.decide.export', 'role': 'cpus' } ] } ]");
            entry.Stages[0].Fires[0].OnlyIf = new DelegateCondition("subjectIsAI", ctx =>
            {
                asked.Add(ctx.SubjectKey);
                return ctx.SubjectKey.StartsWith("ai:", StringComparison.Ordinal);
            });

            FireReport report = env.Dispatcher.FireStage(entry, 0, Roles(new[] { "cpus", "player", "ai:0", "ai:1" }));

            Assert.Equal(new[] { "player", "ai:0", "ai:1" }, asked.ToArray(), "asked once per company, with the company as the subject");
            Assert.Equal(new[] { "ai:0", "ai:1" }, env.Engine.Calls.Select(c => c.CompanyKey).ToArray());
            Assert.Equal(1, report.Dropped);
            Assert.Equal(2, report.Fired);
        }

        [Test]
        public void OnlyIf_SeesTheRolesOfTheEntry()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]");
            entry.Stages[0].Fires[0].OnlyIf = new DelegateCondition("roles", ctx => HistRoleScope.Current != null && HistRoleScope.Current.Roles.Has("target"));

            env.Dispatcher.FireStage(entry, 0, Roles(new[] { "target", "ai:0" }));
            Assert.Equal(1, env.Engine.Calls.Count);

            env.Engine.Calls.Clear();
            env.Dispatcher.FireStage(entry, 0, new RoleResult());
            Assert.Equal(0, env.Engine.Calls.Count, "without the role the condition is false");
        }

        // ---- the answers of the engine ----

        [Test]
        public void UnknownEvent_IsReported_AndTheOtherFiresStillGo()
        {
            var env = new Env();
            env.Engine.Answers["hist.missing"] = FireResult.UnknownEvent;
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.missing', 'role': 'global' }, { 'eventId': 'hist.fine', 'role': 'global' } ] } ]");

            FireReport report = env.Dispatcher.FireStage(entry, 0, new RoleResult());

            Assert.True(report.UnknownEvent);
            Assert.Equal(FireOutcome.UnknownEvent, report.Outcome);
            Assert.Equal(new[] { "hist.missing" }, report.UnknownEvents.ToArray());
            Assert.Equal(2, env.Engine.Calls.Count);
            Assert.Equal(1, report.Fired);
            Assert.True(env.Log.Any(l => l.Contains("hist.missing")), string.Join(" | ", env.Log));
        }

        [Test]
        public void ADisabledEngine_StopsTheStage()
        {
            var env = new Env();
            env.Engine.Hook = (eventId, key) => FireResult.Disabled;
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'cpus' }, { 'eventId': 'hist.b', 'role': 'global' } ] } ]");

            FireReport report = env.Dispatcher.FireStage(entry, 0, Roles(new[] { "cpus", "ai:0", "ai:1" }));

            Assert.True(report.Disabled);
            Assert.Equal(FireOutcome.Disabled, report.Outcome);
            Assert.Equal(1, env.Engine.Calls.Count, "the rest would be told the same");
        }

        [Test]
        public void SkippedAndScheduledAnswers_AreCounted()
        {
            var env = new Env();
            env.Engine.Answers["hist.once"] = FireResult.Skipped;
            env.Engine.Answers["hist.later"] = FireResult.Scheduled;
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.once', 'role': 'global' }, { 'eventId': 'hist.later', 'role': 'global' } ] } ]");

            FireReport report = env.Dispatcher.FireStage(entry, 0, new RoleResult());

            Assert.Equal(1, report.Skipped);
            Assert.Equal(1, report.Fired, "a queued firing is a firing");
            Assert.Equal(FireOutcome.Fired, report.Outcome);

            env.Engine.Answers["hist.later"] = FireResult.Skipped;
            Assert.Equal(FireOutcome.Skipped, env.Dispatcher.FireStage(entry, 0, new RoleResult()).Outcome, "nothing fired at all");
        }

        [Test]
        public void AnExceptionInTheEngine_IsAFailedFiring_NotAnExceptionOfTheDispatcher()
        {
            var env = new Env();
            env.Engine.Hook = (eventId, key) =>
            {
                if (eventId == "hist.bad") throw new InvalidOperationException("bus down");
                return FireResult.Fired;
            };
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.bad', 'role': 'global' }, { 'eventId': 'hist.good', 'role': 'global' } ] } ]");

            FireReport report = env.Dispatcher.FireStage(entry, 0, new RoleResult());

            Assert.Equal(1, report.Fired);
            Assert.Equal(1, report.Skipped);
            Assert.True(env.Log.Any(l => l.Contains("hist.bad") && l.Contains("bus down")), string.Join(" | ", env.Log));
        }

        [Test]
        public void Outcome_IsTheWorstAnswer()
        {
            Assert.Equal(FireOutcome.Skipped, new FireReport().Outcome);
            Assert.Equal(FireOutcome.Fired, new FireReport { Fired = 1 }.Outcome);
            Assert.Equal(FireOutcome.Disabled, new FireReport { Fired = 1, Disabled = true }.Outcome);
            Assert.Equal(FireOutcome.UnknownEvent, new FireReport { Fired = 1, Disabled = true, UnknownEvent = true }.Outcome);
        }

        // ---- the entry did not happen ----

        [Test]
        public void TheNewsFallback_IsTheEventFizzleForThePlayer_WithTheTitleAndTheId()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]", "news");

            FireReport report = env.Dispatcher.FireFallback(entry, new RoleResult());

            Assert.Equal(1, env.Engine.Calls.Count);
            Assert.Equal("hist.fizzle", env.Engine.Calls[0].EventId);
            Assert.Equal("hist.fizzle", DispatcherCore.FizzleEventId);
            Assert.Equal("player", env.Engine.Calls[0].CompanyKey);
            Assert.Equal(new[] { "<hist.2009.test.title>", "hist.2009.test" }, env.Engine.Calls[0].Args, "the text function turns the title key into the title; the id follows");
            Assert.Equal(FireOutcome.Fired, report.Outcome);
        }

        [Test]
        public void TheNewsFallback_OfAnEntryWithoutAHistoryBlock_UsesItsId()
        {
            var env = new Env();
            CalendarEntry entry = HistFixture.Entry(
                "{ 'id': 'hist.2009.plain', 'category': 'antitrust', 'anchor': '2009-05-13', 'window': { 'from': '2009-05-01', 'to': '2010-12-31' }, 'fallback': 'news'," +
                "  'stages': [ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ] }");

            env.Dispatcher.FireFallback(entry, null);

            Assert.Equal(new[] { "hist.2009.plain", "hist.2009.plain" }, env.Engine.Calls[0].Args);
        }

        [Test]
        public void TheSkipFallback_FiresNothing()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]", "skip",
                "[ { 'eventId': 'hist.never', 'role': 'global' } ]");

            FireReport report = env.Dispatcher.FireFallback(entry, new RoleResult());

            Assert.Equal(0, env.Engine.Calls.Count);
            Assert.Equal(FireOutcome.Skipped, report.Outcome);
            Assert.Equal(0, env.Dispatcher.FireFallback(null, null).Attempts);
        }

        [Test]
        public void TheFallbackFires_ReplaceTheGenericNews()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]", "news",
                "[ { 'eventId': 'hist.plain', 'role': 'global' }, { 'eventId': 'hist.cpus', 'role': 'cpus' }, { 'eventId': 'hist.me', 'role': 'player' } ]");

            env.Dispatcher.FireFallback(entry, Roles(new[] { "cpus", "ai:0", "ai:1" }));

            Assert.Equal(new[] { "hist.plain@player", "hist.cpus@ai:0", "hist.cpus@ai:1", "hist.me@player" }, env.Engine.Calls.Select(c => c.EventId + "@" + c.CompanyKey).ToArray());
            Assert.Equal(0, env.Engine.CallsOf("hist.fizzle").Count(), "the entry's own fires are the news");
        }

        [Test]
        public void TheFallbackFires_WithoutTheRolesTheyNeed_LeaveThoseOut()
        {
            var env = new Env();
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]", "news",
                "[ { 'eventId': 'hist.cpus', 'role': 'cpus' }, { 'eventId': 'hist.plain', 'role': 'global' } ]");

            env.Dispatcher.FireFallback(entry, new RoleResult());

            Assert.Equal(new[] { "hist.plain" }, env.Engine.Calls.Select(c => c.EventId).ToArray());
        }

        // ---- the intensity, AC-10.1 ----

        [Test]
        public void TheIntensity_ReachesTheEffectsThroughThePack_NotThroughTheDispatcher_AC_10_1()
        {
            var env = new Env();
            env.Engine.Definitions["hist.2008.lehman"] = new KeyValuePair<double, bool>(0.80, true);     // demand x 0.80, scalable
            env.Engine.Definitions["hist.fixed"] = new KeyValuePair<double, bool>(0.80, false);
            CalendarEntry entry = Entry("[]",
                "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.2008.lehman', 'role': 'global' }, { 'eventId': 'hist.fixed', 'role': 'global' } ] } ]");

            env.Engine.Severity = 0.5f;
            env.Dispatcher.FireStage(entry, 0, new RoleResult());
            Assert.Near(0.90, env.Engine.AppliedMultipliers[0], 1e-9, "intensity 0.5: x 0.80 becomes x 0.90");
            Assert.Near(0.80, env.Engine.AppliedMultipliers[1], 1e-9, "an effect that is not scalable keeps its number");

            // The setting changes the options of the pack in place; the next firing sees it.
            env.Engine.Severity = 1.5f;
            env.Dispatcher.FireStage(entry, 0, new RoleResult());
            Assert.Near(0.70, env.Engine.AppliedMultipliers[2], 1e-9, "intensity 1.5: x 0.80 becomes x 0.70");

            env.Engine.Severity = 1f;
            env.Dispatcher.FireStage(entry, 0, new RoleResult());
            Assert.Near(0.80, env.Engine.AppliedMultipliers[4], 1e-9, "intensity 1.0: as designed");

            // The arguments carry no number of the intensity: the dispatcher scales nothing.
            Assert.True(env.Engine.Calls.All(c => c.Args.Length == 4));
        }

        [Test]
        public void TheDispatcher_HasNoNotionOfIntensity()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Logic", "DispatcherCore.cs")), true);
            Assert.False(Regex.IsMatch(code, @"Severity|Intensity|scalable", RegexOptions.IgnoreCase), "the pack scales the effects; the dispatcher passes the event on");
        }

        // ---- the door to the engine ----

        [Test]
        public void TheBusDoor_FiresWithTheTargetTheArgumentsAndIgnoredConditions()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "HistoricalDispatcher.cs")), true);

            Assert.True(Regex.IsMatch(code, @"EventBus\.Fire\(\s*eventId\s*,\s*new\s+EventFireContext"), "EventBus.Fire(eventId, EventFireContext)");
            Assert.True(Regex.IsMatch(code, @"CompanyKey\s*=\s*companyKey"), "the target");
            Assert.True(Regex.IsMatch(code, @"Args\s*=\s*WithNotes\(\s*eventId\s*,\s*args\s*\)"), "the arguments, with the notes of FR-14 in their slots (HistNotes)");
            Assert.True(code.Contains("HistNotes.Decorate("), "both doors of the firer put the source line and the reference into the arguments");
            Assert.True(Regex.IsMatch(code, @"IgnoreConditions\s*=\s*true"), "the calendar has decided: the conditions of the event are skipped");
            foreach (string name in new[] { "Fired", "Scheduled", "UnknownEvent", "Disabled" })
            {
                Assert.True(code.Contains("EventFireResult." + name), "the answer " + name + " is mapped");
            }
            Assert.True(code.Contains("sealed class HistoricalDispatcher"), "the dispatcher of the design");
            Assert.True(code.Contains("FireStage(") && code.Contains("FireFallbackNews("), "the two operations of the design");
        }

        [Test]
        public void FireResult_HasTheFiveAnswersOfTheEngine()
        {
            Assert.Equal(
                new[] { "Fired", "Scheduled", "Skipped", "UnknownEvent", "Disabled" },
                Enum.GetNames(typeof(FireResult)));
            Assert.Equal(new[] { "Fired", "Skipped", "Disabled", "UnknownEvent" }, Enum.GetNames(typeof(FireOutcome)));
        }

        [Test]
        public void TheConstructor_NeedsAnEngine()
        {
            var env = new Env();
            Assert.Throws<ArgumentNullException>(() => new DispatcherCore(null, env.World, env.Shares, env.Evaluator, env.State));
            // Everything else is optional: no evaluator means "onlyIf holds", no text function shows the key.
            var bare = new DispatcherCore(env.Engine, env.World, null, null, null);
            CalendarEntry entry = Entry("[]", "[ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.a', 'role': 'global' } ] } ]", "news");
            entry.Stages[0].Fires[0].OnlyIf = new DelegateCondition("never", ctx => false);
            Assert.Equal(1, bare.FireStage(entry, 0, new RoleResult()).Fired, "without an evaluator the condition is not asked");
            bare.FireFallback(entry, null);
            Assert.Equal(new[] { "hist.2009.test.title", "hist.2009.test" }, env.Engine.CallsOf("hist.fizzle").Single().Args);
        }

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
        }
    }
}
