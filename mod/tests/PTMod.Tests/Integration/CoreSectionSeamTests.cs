using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Core.Logic.Reputation;
using PTMod.Core.Logic.Rng;
using PTMod.Core.Logic.Save;
using PTMod.Tests.Common;
using PTMod.Tests.Modifiers;
using PTMod.Tests.Save;

namespace PTMod.Tests.Integration
{
    /// <summary>
    /// The seams between the packages of the logic layer, checked before the game-side adapters (ModSave, TSK-025) exist:
    /// the section "mod-framework" is built from <see cref="RngStreams"/>, <see cref="ReputationStore"/> and
    /// <see cref="ChannelRegistry"/> through <see cref="CoreSectionDto"/>, written by <see cref="SideFileIo"/>, read back into
    /// fresh objects and compared. <see cref="CoreParticipant"/> is a test-side stand-in for the adapter: it shows that
    /// the DTO fields and the public members of the three models fit together with no loss and no warning.
    /// </summary>
    public class CoreSectionSeamTests
    {
        private const string Save = "1990_1_1";
        private static readonly DateTime Start = new DateTime(1990, 1, 1);

        [Test]
        public void CoreSection_RngReputationAndModifiers_SurviveWriteAndRead()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);

            CoreParticipant before = CoreParticipant.Fresh(987654321u);
            before.Populate();

            var writeModel = new SideFileModel();
            string problem;
            Assert.True(writeModel.TryRegister(before, out problem), problem);
            SideFileWriteReport written = io.Write(writeModel, Save, new SideFileHeader { modVersion = "0.1.0", gameDate = "1990-01-01" });
            Assert.True(written.Success, written.Error);
            Assert.Equal(0, written.Warnings.Count, "the participant must not return _schema: " + string.Join("; ", written.Warnings));

            CoreParticipant after = CoreParticipant.Fresh(1u);
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(after, out problem), problem);
            SideFileReadReport read = io.Read(readModel, Save);

            Assert.Equal(SideFileStatus.Loaded, read.Status);
            Assert.Equal(0, read.Warnings.Count, string.Join("; ", read.Warnings));
            Assert.Equal(0, after.Problems.Count, string.Join("; ", after.Problems));

            // random: the next 1000 values of every stream continue exactly where the saved game stopped
            Assert.Equal(before.Rng.WorldSeed, after.Rng.WorldSeed);
            foreach (string id in new[] { "mod-framework.core", "event-system.events" })
            {
                IModRng a = before.Rng.Get(id);
                IModRng b = after.Rng.Get(id);
                for (int i = 0; i < 1000; i++) Assert.Equal(a.Range(0, 1000000), b.Range(0, 1000000), id + " #" + i);
            }

            // reputation: values and histories (the date, the requested delta, the result and the reason)
            Assert.Equal(before.Reputation.Keys().ToArray(), after.Reputation.Keys().ToArray());
            foreach (string key in before.Reputation.Keys())
            {
                Assert.Equal(before.Reputation.Get(key), after.Reputation.Get(key), key);
                IReadOnlyList<ReputationChange> h1 = before.Reputation.History(key);
                IReadOnlyList<ReputationChange> h2 = after.Reputation.History(key);
                Assert.Equal(h1.Count, h2.Count, key + " history length");
                for (int i = 0; i < h1.Count; i++)
                {
                    Assert.Equal(h1[i].Date, h2[i].Date);
                    Assert.Equal(h1[i].Delta, h2[i].Delta);
                    Assert.Equal(h1[i].Value, h2[i].Value);
                    Assert.Equal(h1[i].Reason, h2[i].Reason);
                }
            }

            // modifiers: the same entries with their ids, scopes, end dates and reasons ...
            IReadOnlyList<ModifierEntry> e1 = before.Modifiers.Entries;
            IReadOnlyList<ModifierEntry> e2 = after.Modifiers.Entries;
            Assert.Equal(e1.Count, e2.Count);
            Assert.Equal(5, e2.Count);
            for (int i = 0; i < e1.Count; i++)
            {
                Assert.Equal(e1[i].Id, e2[i].Id);
                Assert.Equal(e1[i].FeatureId, e2[i].FeatureId);
                Assert.Equal(e1[i].Channel, e2[i].Channel);
                Assert.Equal(e1[i].Scope, e2[i].Scope);
                Assert.Equal(e1[i].Multiplier, e2[i].Multiplier);
                Assert.Equal(e1[i].ExpiresOn, e2[i].ExpiresOn);
                Assert.Equal(e1[i].ReasonKey, e2[i].ReasonKey);
                Assert.Equal(e1[i].ReasonArgs, e2[i].ReasonArgs);
            }

            // ... and the same channel values for the players that matter
            foreach (string channel in new[] { "market.demand.lowend", "research.speed", "taxes", "market.rating.cpu", "production.output" })
            {
                foreach (ModifierQuery q in Queries())
                {
                    Assert.Equal(before.Modifiers.Get(channel, q), after.Modifiers.Get(channel, q), channel + " for " + q.CompanyKey);
                }
            }

            // a new entry does not reuse a restored id
            ModifierEntry fresh = after.Modifiers.Add("framework-sample", "research.cost", ModifierScope.Global(), 1.1f);
            Assert.True(e2.All(e => e.Id != fresh.Id), "new id " + fresh.Id);
        }

        [Test]
        public void CoreSection_ZeroMultiplierEntry_IsNotDroppedBySavingAndLoading()
        {
            // FR-12 allows 0 on the channels whose fuse starts at 0 (a tax holiday); the section must not lose it.
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            CoreParticipant before = CoreParticipant.Fresh(1u);
            before.Modifiers.Add("event-system", "taxes", ModifierScope.Player(), 0f, 30, "es.tax.holiday");

            var writeModel = new SideFileModel();
            string problem;
            Assert.True(writeModel.TryRegister(before, out problem), problem);
            Assert.True(io.Write(writeModel, Save, null).Success);

            CoreParticipant after = CoreParticipant.Fresh(1u);
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(after, out problem), problem);
            io.Read(readModel, Save);

            Assert.Equal(1, after.Modifiers.EntryCount);
            Assert.Equal(0f, after.Modifiers.Get("taxes", new ModifierQuery("player")));
            Assert.Equal(1f, after.Modifiers.Get("taxes", new ModifierQuery("ai:3")), "the Player scope is kept");
            Assert.Equal(0, after.Problems.Count, string.Join("; ", after.Problems));
        }

        [Test]
        public void CoreSection_EntryThatEndedBeforeTheSavedDate_IsLeftOutOnLoad()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            CoreParticipant before = CoreParticipant.Fresh(1u);
            before.Modifiers.Add("a", "research.speed", ModifierScope.Global(), 1.5f, 10);
            before.Modifiers.Add("a", "research.speed", ModifierScope.Global(), 2f);

            var writeModel = new SideFileModel();
            string problem;
            Assert.True(writeModel.TryRegister(before, out problem), problem);
            Assert.True(io.Write(writeModel, Save, null).Success);

            // the same file is loaded in a game whose calendar is already past the first entry
            CoreParticipant later = CoreParticipant.Fresh(1u);
            later.Modifiers.SetToday(Start.AddDays(11));
            var readModel = new SideFileModel();
            Assert.True(readModel.TryRegister(later, out problem), problem);
            io.Read(readModel, Save);

            Assert.Equal(1, later.Modifiers.EntryCount);
            Assert.Equal(2f, later.Modifiers.Get("research.speed", new ModifierQuery("player")));
        }

        [Test]
        public void CoreSection_WithBrokenParts_KeepsTheRestAndSaysWhat()
        {
            FakeFileSystem fs = SideFileTestKit.NewFs();
            SideFileIo io = SideFileTestKit.NewIo(fs);
            // a hand-edited section: one stream with three words, a company key that does not exist, a scope that does not exist, one good entry
            string core = "\"mod-framework\":{\"_schema\":1,\"startDate\":\"1990-01-01\","
                          + "\"random\":{\"worldSeed\":42,\"streams\":{\"a\":[1,2,3],\"b\":[1,2,3,4]}},"
                          + "\"modifiers\":["
                          + "{\"id\":\"m1\",\"feature\":\"f\",\"channel\":\"taxes\",\"scope\":\"Company\",\"company\":\"rival\",\"mult\":0.5},"
                          + "{\"id\":\"m2\",\"feature\":\"f\",\"channel\":\"taxes\",\"scope\":\"Everyone\",\"mult\":0.5},"
                          + "{\"id\":\"m3\",\"feature\":\"f\",\"channel\":\"taxes\",\"scope\":\"AIOnly\",\"mult\":0.5}],"
                          + "\"reputation\":{\"player\":{\"value\":120},\"nobody\":{\"value\":10},\"ai:2\":{\"value\":30,\"history\":[{\"date\":\"1990-01-05\",\"delta\":-20,\"value\":30,\"reason\":\"x\"}]}}}";
            fs.Seed(SideFileTestKit.SidePath(Save), SideFileTestKit.File1(core));

            CoreParticipant part = CoreParticipant.Fresh(1u);
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(part, out problem), problem);
            SideFileReadReport report = io.Read(model, Save);

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal(42u, part.Rng.WorldSeed);
            Assert.True(part.Problems.Any(p => p.Contains("'a'")), "the damaged stream is reported: " + string.Join("; ", part.Problems));
            Assert.Equal(1, part.Modifiers.EntryCount, "only m3 is valid");
            Assert.Equal("m3", part.Modifiers.Entries[0].Id);
            Assert.Equal(100f, part.Reputation.Get("player"), "120 is clamped");
            Assert.Equal(50f, part.Reputation.Get("nobody"), "a bad key is not stored");
            Assert.Equal(1, part.Reputation.History("ai:2").Count);
            Assert.True(part.Problems.Count >= 3, "three things were wrong: " + string.Join("; ", part.Problems));
        }

        private static IEnumerable<ModifierQuery> Queries()
        {
            yield return new ModifierQuery("player", 7, Segment.LowEnd);
            yield return new ModifierQuery("player", 7, Segment.HighEnd);
            yield return new ModifierQuery("ai:3", 11, Segment.MidRange);
            yield return new ModifierQuery("ai:3", 12, Segment.LowEnd);
            yield return new ModifierQuery("ai:9", 42, Segment.Industries);
            yield return new ModifierQuery(null);
        }

        /// <summary>
        /// What the ModSave adapter of the core will do (TSK-025), written against the public members of the models.
        /// Problems of the DTO conversion go to <see cref="Problems"/> (the adapter sends them to ModLog).
        /// </summary>
        private sealed class CoreParticipant : ISideFileParticipant
        {
            public RngStreams Rng;
            public ReputationStore Reputation;
            public ChannelRegistry Modifiers;
            public readonly List<string> Problems = new List<string>();

            private uint newGameSeed;

            public static CoreParticipant Fresh(uint seed)
            {
                var part = new CoreParticipant { newGameSeed = seed };
                part.Rng = new RngStreams(seed);
                part.Reputation = new ReputationStore(50, text => part.Problems.Add("reputation: " + text));
                part.Modifiers = new ChannelRegistry(null, Start);
                part.Modifiers.Warning += (feature, text) => part.Problems.Add("modifiers: " + text);
                foreach (ChannelSpec spec in ModifierFixtures.CoreChannels()) part.Modifiers.Declare(spec);
                return part;
            }

            public string SectionId
            {
                get { return "mod-framework"; }
            }

            public int SchemaVersion
            {
                get { return 1; }
            }

            /// <summary>A mid-game state: both random streams used, reputation moved and drifted, five modifier entries.</summary>
            public void Populate()
            {
                IModRng core = Rng.Get("mod-framework.core");
                IModRng events = Rng.Get("event-system.events");
                for (int i = 0; i < 1000; i++)
                {
                    core.Range(0, 100);
                    events.Chance(0.3f);
                }

                Reputation.Add("player", 10f, "press.review", Start);
                Reputation.Add("player", -3.5f, "es.event", Start.AddDays(40));
                Reputation.Add("ai:3", -5f, "press.review", Start.AddDays(2));
                Reputation.Add("ai:12", 95f, "press.review", Start);
                Reputation.DriftAll(new[] { "player", "ai:3", "ai:12", "ai:4" }, 0.03f, 50f);

                Modifiers.Add("market-shocks", "market.demand.lowend", ModifierScope.Global(), 0.7f, 90, "ms.event.recession", new[] { "рецессия" });
                Modifiers.Add("framework-sample", "research.speed", ModifierScope.Player(), 1.25f);
                Modifiers.Add("event-system", "taxes", ModifierScope.AIOnly(), 0f, 30);
                Modifiers.Add("press-reviews", "market.rating.cpu", ModifierScope.Company("ai:3", Segment.LowEnd), 1.8f, 0, "press.mod.hype");
                Modifiers.Add("rival-personalities", "production.output", ModifierScope.Cpu(11, Segment.MidRange), 0.85f, 365, "rival.mod.recall", new[] { "a", "б" });
            }

            public void ResetToDefaults()
            {
                Rng.Reset(newGameSeed);
                Reputation.Clear();
                Modifiers.ClearEntries();
            }

            public void Read(JsonObject section, int schema)
            {
                var errors = new List<string>();
                CoreSectionDto dto = ModJson.Bind<CoreSectionDto>(section, errors);
                Problems.AddRange(errors);

                Modifiers.ClearEntries();
                Reputation.Clear();

                if (dto.random != null) Problems.AddRange(Rng.Restore(dto.random.worldSeed, dto.random.streams));
                else Rng.Reset(newGameSeed);

                if (dto.reputation != null)
                {
                    foreach (KeyValuePair<string, ReputationDto> pair in dto.reputation)
                    {
                        var history = new List<ReputationChange>();
                        if (pair.Value.history != null)
                        {
                            foreach (ReputationChangeDto h in pair.Value.history)
                            {
                                DateTime date;
                                if (!TryDate(h.date, out date)) { Problems.Add("reputation " + pair.Key + ": bad date '" + h.date + "'"); continue; }
                                history.Add(new ReputationChange(date, h.delta, h.value, h.reason));
                            }
                        }
                        Reputation.Load(pair.Key, pair.Value.value, history);
                    }
                }

                if (dto.modifiers != null)
                {
                    foreach (ModifierEntryDto m in dto.modifiers)
                    {
                        ModifierScope scope;
                        string error;
                        if (!ModifierScope.TryCreate(m.scope, m.company, m.cpu, m.segment, out scope, out error))
                        {
                            Problems.Add("modifier " + m.id + ": " + error);
                            continue;
                        }
                        DateTime? expires = null;
                        if (m.expires != null)
                        {
                            DateTime date;
                            if (!TryDate(m.expires, out date)) { Problems.Add("modifier " + m.id + ": bad date '" + m.expires + "'"); continue; }
                            expires = date;
                        }
                        Modifiers.Restore(m.id, m.feature, m.channel, scope, m.mult, expires, m.reason, m.reasonArgs);
                    }
                }
            }

            public JsonObject Write()
            {
                var dto = new CoreSectionDto
                {
                    startDate = Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    random = new RandomDto { worldSeed = Rng.WorldSeed, streams = Rng.ExportStates() },
                    modifiers = Modifiers.Entries.Select(ToDto).ToList(),
                    reputation = new Dictionary<string, ReputationDto>()
                };
                foreach (string key in Reputation.Keys())
                {
                    dto.reputation[key] = new ReputationDto
                    {
                        value = Reputation.Get(key),
                        history = Reputation.History(key).Select(h => new ReputationChangeDto
                        {
                            date = h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            delta = h.Delta,
                            value = h.Value,
                            reason = h.Reason
                        }).ToList()
                    };
                }

                var json = (JsonObject)ModJson.From(dto);
                json.Remove("_schema"); // the model owns it
                return json;
            }

            private static ModifierEntryDto ToDto(ModifierEntry e)
            {
                return new ModifierEntryDto
                {
                    id = e.Id,
                    feature = e.FeatureId,
                    channel = e.Channel,
                    scope = e.Scope.Kind.ToString(),
                    company = e.Scope.CompanyKey,
                    cpu = e.Scope.CpuSaveId,
                    segment = (int)e.Scope.Segment,
                    mult = e.Multiplier,
                    expires = e.ExpiresOn.HasValue ? e.ExpiresOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    reason = e.ReasonKey,
                    reasonArgs = e.ReasonArgs != null && e.ReasonArgs.Length > 0 ? e.ReasonArgs : null
                };
            }

            private static bool TryDate(string text, out DateTime date)
            {
                return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }
        }
    }
}
