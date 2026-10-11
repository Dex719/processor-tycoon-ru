using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The randomness of the engine (FR-9, D5): the same seed gives the same events, a new event in a category does not move the rolls of the category
    /// (AC-9.2), the derived generators are fixed by their key and never touch the daily stream, a saved and loaded game goes on as if it had not been saved,
    /// and the sources of the feature use neither <c>System.Random</c> nor <c>UnityEngine.Random</c>.
    /// </summary>
    [Covers(typeof(EventRandom), typeof(DerivedRng), typeof(LotteryDraw), typeof(EventSaveSection), typeof(EventSystemSaveDto))]
    public class DeterminismTests
    {
        private const int Start = 5479;   // 1986-01-01

        private static FakeCatalog Universal(params EventDefinition[] events)
        {
            return new FakeCatalog(FakeCatalog.ConfigWith(2, FakeCatalog.Category("universal", 0.05f), FakeCatalog.Category("rivals", 0.05f)), events);
        }

        private static EventDefinition[] Starter()
        {
            return new[]
            {
                Defs.Daily("es.a", "universal", weight: 2f, cooldownDays: 60),
                Defs.Daily("es.b", "universal", weight: 1f, target: EventTargetType.RandomCompany, cooldownDays: 30),
                Defs.Daily("es.c", "rivals", target: EventTargetType.RandomAI),
                Defs.Condition("es.d", Defs.When((key, day) => day % 7 == 0), chance: 0.3f),
            };
        }

        private static string Journal(RecordingRuntime runtime)
        {
            var lines = new List<string>();
            foreach (FiringPlan plan in runtime.Fired) lines.Add(plan.Today + ":" + plan.Definition.id + ":" + string.Join("+", plan.Targets));
            return string.Join("\n", lines);
        }

        [Test]
        public void TheSameSeed_GivesTheSameEvents()
        {
            var first = new SchedulerRig(Universal(Starter()), seed: 99u, worldSeed: 5u);
            var second = new SchedulerRig(Universal(Starter()), seed: 99u, worldSeed: 5u);
            first.RunDays(Start, 3000);
            second.RunDays(Start, 3000);

            Assert.True(first.Runtime.Fired.Count > 50, "the test needs events: " + first.Runtime.Fired.Count);
            Assert.Equal(Journal(first.Runtime), Journal(second.Runtime));

            var other = new SchedulerRig(Universal(Starter()), seed: 100u, worldSeed: 5u);
            other.RunDays(Start, 3000);
            Assert.NotEqual(Journal(first.Runtime), Journal(other.Runtime), "another stream, other days");
        }

        [Test]
        public void AC_9_2_ANewEventInTheCategory_DoesNotChangeTheRollsOfTheCategory()
        {
            var before = new SchedulerRig(Universal(Starter()), seed: 4242u);
            EventDefinition[] more = Starter();
            var withNew = new List<EventDefinition>(more) { Defs.Daily("es.new", "universal", weight: 5f, target: EventTargetType.RandomAI) };
            var after = new SchedulerRig(Universal(withNew.ToArray()), seed: 4242u);

            var rollsBefore = new List<string>();
            var rollsAfter = new List<string>();
            before.Scheduler.LotteryObserver = (day, category, draw) => rollsBefore.Add(day + "/" + category + "/" + draw.U1.ToString("R"));
            after.Scheduler.LotteryObserver = (day, category, draw) => rollsAfter.Add(day + "/" + category + "/" + draw.U1.ToString("R"));

            before.RunDays(Start, 1000);
            after.RunDays(Start, 1000);

            Assert.Equal(2000, rollsBefore.Count, "two categories × 1000 days");
            Assert.Equal(string.Join(";", rollsBefore), string.Join(";", rollsAfter), "u1 of every day and category is the same");
            Assert.Equal(before.Daily.Draws, after.Daily.Draws, "the same count of numbers from the daily stream");
            Assert.True(after.Runtime.FiredCount("es.new") > 0, "the new event does fire");
        }

        [Test]
        public void DrawCategory_TakesExactlyThreeNumbers_InOrder()
        {
            var counting = new CountingRng(1u);
            var random = new EventRandom(counting, () => 0u);
            LotteryDraw draw = random.DrawCategory();
            Assert.Equal(3, counting.Draws);
            Assert.Equal(3, EventRandom.NumbersPerCategory);

            var plain = new Xoshiro128(1u);
            Assert.Equal(plain.Value(), draw.U1);
            Assert.Equal(plain.Value(), draw.U2);
            Assert.Equal(plain.Value(), draw.U3);
            Assert.Equal("event-system/daily", EventRandom.DailyStreamId);
        }

        [Test]
        public void DerivedGenerators_WithOneSeed_GiveOneSequence()
        {
            var random = new EventRandom(new CountingRng(1u), () => 123456u);
            DerivedRng a = random.ForInstance(57);
            DerivedRng b = random.ForInstance(57);
            for (int i = 0; i < 100; i++) Assert.Equal(a.Value(), b.Value(), "draw " + i);

            DerivedRng t1 = random.ForTrigger("es.patent_win", 6402);
            DerivedRng t2 = random.ForTrigger("es.patent_win", 6402);
            Assert.Equal(t1.Seed, t2.Seed);
            Assert.Equal(t1.Range(0, 1000000), t2.Range(0, 1000000));
            Assert.Equal(t1.Chance(0.5f), t2.Chance(0.5f));
            Assert.Equal(t1.WeightedIndex(new List<float> { 1f, 2f, 3f }), t2.WeightedIndex(new List<float> { 1f, 2f, 3f }));
            Assert.Equal(t1.Pick(new[] { "x", "y", "z" }), t2.Pick(new[] { "x", "y", "z" }));
        }

        [Test]
        public void DerivedGenerators_DependOnTheirKey_AndTheWorld()
        {
            const uint world = 123456u;
            var seeds = new HashSet<uint>
            {
                EventRandom.InstanceSeed(world, 57),
                EventRandom.InstanceSeed(world, 58),
                EventRandom.TriggerSeed(world, "es.a", 6402),
                EventRandom.TriggerSeed(world, "es.a", 6403),
                EventRandom.TriggerSeed(world, "es.b", 6402),
                EventRandom.InstanceSeed(world + 1, 57),
            };
            Assert.Equal(6, seeds.Count, "every key and world its own seed");

            Assert.Equal(RngStreams.DeriveStreamSeed(world, "event-system/inst/57"), EventRandom.InstanceSeed(world, 57), "the seed rule of ModRandom");
            Assert.Equal(RngStreams.DeriveStreamSeed(world, "event-system/trg/es.a/6402"), EventRandom.TriggerSeed(world, "es.a", 6402));

            var derived = new DerivedRng(EventRandom.InstanceSeed(world, 57));
            var reference = new Xoshiro128(EventRandom.InstanceSeed(world, 57));
            for (int i = 0; i < 20; i++) Assert.Equal(reference.Value(), derived.Value(), "xoshiro128** of the seed");
        }

        [Test]
        public void DerivedGenerators_DoNotMoveTheDailyStream()
        {
            var counting = new CountingRng(9u);
            var random = new EventRandom(counting, () => 1u);
            random.ForInstance(1).Value();
            random.ForTrigger("es.x", 10).Value();
            Assert.Equal(0, counting.Draws);
            Assert.Equal(1u, random.WorldSeed);
            Assert.True(ReferenceEquals(counting, random.Daily));
        }

        [Test]
        public void ASavedAndLoadedGame_GoesOnAsIfItHadNotBeenSaved()
        {
            // One run of 2000 days, against a run saved after 900 days and loaded into fresh objects (the daily stream's state is saved by ModRandom).
            FakeCatalog catalog = Universal(Starter());
            const uint world = 31u;

            var continuous = new Rig(catalog, new Xoshiro128(77u), world);
            continuous.Run(Start, 2000);

            var firstHalf = new Rig(catalog, new Xoshiro128(77u), world);
            firstHalf.Run(Start, 900);
            uint[] stream = firstHalf.Daily.GetState();
            string section = ModJson.Write(firstHalf.Section.Write(), false);

            Xoshiro128 restored;
            Assert.True(Xoshiro128.TryFromState(stream, out restored));
            var secondHalf = new Rig(catalog, restored, world);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(section, out node, out error), "the section is valid JSON");
            secondHalf.Section.Read((JsonObject)node, EventSaveSection.Schema);
            secondHalf.Run(Start + 900, 1100);

            var joined = new List<FiringPlan>(firstHalf.Runtime.Fired);
            joined.AddRange(secondHalf.Runtime.Fired);
            var merged = new RecordingRuntime();
            merged.Fired.AddRange(joined);
            Assert.True(continuous.Runtime.Fired.Count > 50, "the test needs events");
            Assert.Equal(Journal(continuous.Runtime), Journal(merged), "the same events on the same days for the same companies");
        }

        [Test]
        public void TheFeatureSources_UseNoOtherRandomness()
        {
            string src = SourceScan.SrcDirOrFail();
            string folder = Path.Combine(src, "Features", "EventSystem");
            var forbidden = new Regex(@"\bSystem\s*\.\s*Random\b|\bUnityEngine\s*\.\s*Random\b|\bnew\s+Random\s*\(|\bRandom\s*\.\s*(Range|value|InitState)\b");
            var violations = new List<string>();
            List<string> files = SourceScan.Files(folder, ".cs");
            Assert.True(files.Count >= 20, "too few sources under " + folder);
            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in forbidden.Matches(code))
                {
                    violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
                }
            }
            Assert.True(violations.Count == 0, "event-system draws only from ModRandom and DerivedRng (FR-9 item 1):\n  " + string.Join("\n  ", violations));
        }

        /// <summary>A scheduler over a plain xoshiro128** daily stream and a save section, for the save/load test.</summary>
        private sealed class Rig
        {
            public readonly Xoshiro128 Daily;
            public readonly RecordingRuntime Runtime = new RecordingRuntime();
            public readonly EventSaveSection Section;
            private readonly FakeWorld _world = FakeWorld.WithCompanies(3);
            private readonly EventScheduler _scheduler;

            public Rig(FakeCatalog catalog, Xoshiro128 daily, uint worldSeed)
            {
                Daily = daily;
                var state = new SchedulerState();
                var history = new EventHistory();
                Runtime.History = history;
                Section = new EventSaveSection(state, history, line => Assert.Fail("save problem: " + line));
                _scheduler = new EventScheduler(catalog, _world, Runtime, new EventRandom(daily, () => worldSeed), state, history, new SchedulerOptions());
            }

            public void Run(int first, int count)
            {
                for (int day = first; day < first + count; day++)
                {
                    _world.Today = day;
                    _scheduler.ProcessDay(day);
                }
            }
        }
    }
}
