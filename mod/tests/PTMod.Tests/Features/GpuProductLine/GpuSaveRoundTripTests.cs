using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The save of gpu-product-line (TSK-004, FR-9, NFR-3, NFR-8): the section round trip through <c>ModJson</c> without a version field, the checks
    /// of design.md "Правила проверки при загрузке" (finite numbers, clamps, limits, unknown ids, company keys), migration by <c>schema</c>, the
    /// participant inside the core's <see cref="SideFileModel"/> (a section without <c>_schema</c> is version 1, a newer one is kept as it is) and the id generator.
    /// </summary>
    [Covers(typeof(GpuState), typeof(GpuSectionParticipant), typeof(GpuSaveSection), typeof(GpuDate), typeof(GpuResearchSave), typeof(GpuStatsSave),
        typeof(GpuDesignSave), typeof(GpuProjectSave), typeof(GpuLedgerSave), typeof(GpuProductSave), typeof(GpuSegLaunch), typeof(GpuVendorSave),
        typeof(GpuMarketSave), typeof(GpuSynergyEntry), typeof(GpuSynergySave), typeof(GpuNewsSave), typeof(GpuConsoleOrderSave))]
    public class GpuSaveRoundTripTests
    {
        // ---- helpers ----

        private sealed class Loaded
        {
            public GpuState State;
            public GpuSectionParticipant Participant;
            public List<string> Logged = new List<string>();
            public GpuSaveSection Data
            {
                get { return State.Data; }
            }
        }

        private static Loaded NewState(GpuContentDb content)
        {
            var loaded = new Loaded();
            loaded.State = new GpuState(content, message => loaded.Logged.Add(message));
            loaded.Participant = new GpuSectionParticipant(loaded.State);
            return loaded;
        }

        private static Loaded ReadSection(GpuContentDb content, JsonObject section, int schema = 1)
        {
            Loaded loaded = NewState(content);
            loaded.Participant.Read(section, schema);
            return loaded;
        }

        private static GpuContentDb Shipped()
        {
            var problems = new List<string>();
            GpuContentDb db = GpuFiles.Load(GpuFiles.Shipped(), problems);
            Assert.Equal(0, problems.Count, "the shipped content has problems:" + Lines(problems));
            return db;
        }

        private static GpuContentDb Minimal()
        {
            return GpuFiles.Load(GpuFiles.Minimal(), new List<string>());
        }

        private static string Lines(IEnumerable<string> lines)
        {
            return "\n  " + string.Join("\n  ", lines.ToArray());
        }

        private static void AssertHas(IEnumerable<string> lines, string part)
        {
            List<string> all = lines.ToList();
            Assert.True(all.Any(line => line.Contains(part)), "expected a line with '" + part + "' in:" + Lines(all));
        }

        private static JsonObject Section(GpuSaveSection section)
        {
            return (JsonObject)ModJson.From(section);
        }

        private static GpuProductSave Product(string id, string owner = "player", string status = "selling")
        {
            return new GpuProductSave { id = id, owner = owner, status = status, stats = new GpuStatsSave() };
        }

        private static bool HasKey(JsonNode node, string key)
        {
            var obj = node as JsonObject;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    if (pair.Key == key || HasKey(pair.Value, key)) return true;
                }
                return false;
            }
            var array = node as JsonArray;
            if (array != null)
            {
                foreach (JsonNode item in array)
                {
                    if (HasKey(item, key)) return true;
                }
            }
            return false;
        }

        /// <summary>Every value of <paramref name="expected"/> is in <paramref name="actual"/> (which may have more); numbers within 1e-6.</summary>
        private static void Subset(JsonNode expected, JsonNode actual, string path, List<string> diffs)
        {
            var eo = expected as JsonObject;
            if (eo != null)
            {
                var ao = actual as JsonObject;
                if (ao == null)
                {
                    diffs.Add(path + ": expected an object");
                    return;
                }
                foreach (KeyValuePair<string, JsonNode> pair in eo)
                {
                    JsonNode value;
                    if (!ao.TryGetValue(pair.Key, out value)) diffs.Add(path + "." + pair.Key + ": missing");
                    else Subset(pair.Value, value, path + "." + pair.Key, diffs);
                }
                return;
            }

            var ea = expected as JsonArray;
            if (ea != null)
            {
                var aa = actual as JsonArray;
                if (aa == null || aa.Count != ea.Count)
                {
                    diffs.Add(path + ": expected an array of " + ea.Count);
                    return;
                }
                for (int i = 0; i < ea.Count; i++) Subset(ea[i], aa[i], path + "[" + i + "]", diffs);
                return;
            }

            var ev = (JsonValue)expected;
            var av = actual as JsonValue;
            if (av == null || av.Kind != ev.Kind)
            {
                diffs.Add(path + ": the kind differs");
                return;
            }
            bool same = true;
            switch (ev.Kind)
            {
                case JsonKind.Number:
                    same = Math.Abs(ev.NumberValue - av.NumberValue) <= 1e-6 * Math.Max(1.0, Math.Abs(ev.NumberValue));
                    break;
                case JsonKind.String:
                    same = ev.StringValue == av.StringValue;
                    break;
                case JsonKind.Bool:
                    same = ev.BoolValue == av.BoolValue;
                    break;
            }
            if (!same) diffs.Add(path + ": " + ModJson.Write(ev, false) + " became " + ModJson.Write(av, false));
        }

        // ---- the round trip ----

        [Test]
        public void RoundTrip_DesignExample_KeepsEveryValue_AndWritesNoVersion()
        {
            JsonObject section = GpuFiles.FixtureJson("save-design-example.json");
            Loaded loaded = ReadSection(null, section);

            Assert.Equal(0, loaded.State.Warnings.Count, "a clean section is read without a single change:" + Lines(loaded.State.Warnings));
            Assert.Equal(0, loaded.Logged.Count);
            Assert.True(loaded.Data.divisionFounded);
            Assert.Equal(1995, loaded.Data.foundedOn.y);
            Assert.Equal("eras", loaded.Data.marketModeApplied);
            Assert.Equal(43L, loaded.Data.nextId);
            Assert.Equal("arch.tnl", loaded.Data.research.currentId);
            Assert.Equal(0.37f, loaded.Data.research.currentProgress);
            Assert.Equal(1, loaded.Data.designs.Count);
            Assert.Equal(2, loaded.Data.products.Count);
            Assert.Equal(225.9, loaded.Data.products[0].stats.effectivePerf, "the snapshot of the characteristics");
            Assert.Equal(new[] { 9120, 15310 }, loaded.Data.products[0].ledger.sold.ToArray());
            Assert.Equal(3, loaded.Data.vendors.Count);
            Assert.Equal("ai:1", loaded.Data.vendors[2].companyKey);
            Assert.Equal(1, loaded.Data.consoleOrders.Count);

            JsonObject written = loaded.Participant.Write();
            Assert.False(HasKey(written, "_schema"), "the version of the section is the core's, not ours");

            var diffs = new List<string>();
            Subset(section, written, "$", diffs);
            Assert.Equal(0, diffs.Count, "values lost on the way:" + Lines(diffs));

            // reading what was written gives the same section again
            Loaded again = ReadSection(null, written);
            Assert.Equal(0, again.State.Warnings.Count, Lines(again.State.Warnings));
            Assert.Equal(ModJson.Write(written, true), ModJson.Write(again.Participant.Write(), true));
        }

        [Test]
        public void RoundTrip_DesignExample_WithTheShippedContent_ReadsWithoutAWarning_TheConsoleOrderStands()
        {
            // the example names parts of the shipped content; its console order "sony.2" is an offer of consoles.json (stage 3) and the card g-9 is the player's
            Loaded loaded = ReadSection(Shipped(), GpuFiles.FixtureJson("save-design-example.json"));

            Assert.Equal(0, loaded.State.Warnings.Count, Lines(loaded.State.Warnings));
            Assert.Equal("active", loaded.Data.consoleOrders[0].status);
            Assert.Equal("g-9", loaded.Data.consoleOrders[0].productId);
            Assert.Equal(8, loaded.Data.research.completed.Count);
            Assert.Equal("arch.tnl", loaded.Data.research.currentId);
            Assert.Equal(1, loaded.Data.projects.Count);
            Assert.Equal(2, loaded.Data.products.Count);
        }

        [Test]
        public void ResetToDefaults_IsAnEmptyDivision()
        {
            Loaded loaded = ReadSection(null, GpuFiles.FixtureJson("save-design-example.json"));
            GpuSaveSection before = loaded.Data;
            loaded.Participant.ResetToDefaults();

            Assert.False(ReferenceEquals(before, loaded.Data), "a new object, the old one is not reused");
            Assert.False(loaded.Data.divisionFounded);
            Assert.Null(loaded.Data.foundedOn);
            Assert.Equal("single", loaded.Data.marketModeApplied);
            Assert.Equal(1L, loaded.Data.nextId);
            Assert.False(loaded.Data.market.seeded, "the market is seeded on GameReady when the section had none");
            Assert.Equal(0, loaded.Data.products.Count + loaded.Data.designs.Count + loaded.Data.projects.Count + loaded.Data.vendors.Count);
            Assert.Equal(1f, loaded.Data.research.funding);
            Assert.Equal(0, loaded.State.Warnings.Count);

            JsonObject written = loaded.Participant.Write();
            Assert.False(written.GetBool("divisionFounded", true));
            Assert.False(written.GetObject("market").GetBool("seeded", true));
            Assert.False(HasKey(written, "_schema"));
        }

        [Test]
        public void Write_GivesAFreshObjectEveryTime()
        {
            Loaded loaded = NewState(null);
            JsonObject first = loaded.Participant.Write();
            first.Set("divisionFounded", true);
            first.Set("injected", 1);
            JsonObject second = loaded.Participant.Write();

            Assert.False(second.GetBool("divisionFounded", true), "the state is not the returned object");
            Assert.False(second.ContainsKey("injected"));
        }

        [Test]
        public void Participant_IsTheSectionOfTheFeature_VersionOne()
        {
            var participant = new GpuSectionParticipant(new GpuState());
            Assert.Equal("gpu-product-line", participant.SectionId);
            Assert.Equal(1, participant.SchemaVersion);
            Assert.Equal(GpuState.SectionId, participant.SectionId);
            Assert.Equal(GpuState.SchemaVersion, participant.SchemaVersion);
            Assert.Throws<ArgumentNullException>(() => new GpuSectionParticipant(null));
        }

        // ---- the schema ----

        [Test]
        public void Read_CurrentSchema_DoesNotMigrate_AndDoesNotTouchTheObject()
        {
            JsonObject section = GpuFiles.FixtureJson("save-design-example.json");
            string before = ModJson.Write(section, true);
            Loaded loaded = ReadSection(null, section, 1);

            Assert.Equal(-1, loaded.State.MigratedFrom);
            Assert.True(loaded.Data.divisionFounded);
            Assert.Equal(before, ModJson.Write(section, true), "Read must not modify the object it is given");
        }

        [Test]
        public void Read_OlderSchema_Migrates_AndReadsTheSection()
        {
            JsonObject section = GpuFiles.FixtureJson("save-design-example.json");
            string before = ModJson.Write(section, true);

            foreach (int old in new[] { 0, -3 })
            {
                Loaded loaded = ReadSection(null, section, old);
                Assert.Equal(0, loaded.State.MigratedFrom, "schema " + old + " is older than 1: migrated from 0");
                AssertHas(loaded.State.Warnings, "migrated from schema 0 to 1");
                Assert.True(loaded.Data.divisionFounded, "schema " + old);
                Assert.Equal(43L, loaded.Data.nextId, "schema " + old);
                Assert.Equal(2, loaded.Data.products.Count, "schema " + old);
                Assert.Equal(before, ModJson.Write(section, true), "the migration works on a copy");
            }
        }

        [Test]
        public void Read_NewerSchema_IsNotRead()
        {
            Loaded loaded = NewState(null);
            loaded.Participant.Read(GpuFiles.FixtureJson("save-design-example.json"), 2);

            Assert.False(loaded.Data.divisionFounded, "the core never calls Read for this, but if it does the data is not interpreted");
            Assert.Equal(0, loaded.Data.products.Count);
            AssertHas(loaded.State.Warnings, "newer than 1");
        }

        [Test]
        public void Read_NullSection_GivesTheEmptyState()
        {
            Loaded loaded = NewState(null);
            loaded.Participant.Read(GpuFiles.FixtureJson("save-design-example.json"), 1);
            Assert.True(loaded.Data.divisionFounded);

            loaded.Participant.Read(null, 1);
            Assert.False(loaded.Data.divisionFounded);
            Assert.NotNull(loaded.Data);
        }

        // ---- inside the core's side file ----

        [Test]
        public void SideFile_ASectionWithoutSchema_IsReadAsVersionOne_AndWrittenWithIt()
        {
            Loaded loaded = NewState(null);
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(loaded.Participant, out problem), problem);

            SideFileReadReport report = model.Apply(GpuFiles.Fixture("save-sidefile-noschema.json"));
            Assert.Equal(SideFileStatus.Loaded, report.Status);
            AssertHas(report.Warnings, "features.gpu-product-line._schema: missing, the section is read as version 1");
            Assert.Equal(-1, loaded.State.MigratedFrom, "read as version 1: nothing to migrate");
            Assert.True(loaded.Data.divisionFounded);
            Assert.Equal(5L, loaded.Data.nextId);
            Assert.Equal(0.5f, loaded.Data.research.funding);
            Assert.Equal(new[] { "arch.2d" }, loaded.Data.research.completed.ToArray());

            SideFileBuildReport build = model.Build(new SideFileHeader { saveName = "Test" });
            JsonObject features = build.Document.GetObject("features");
            JsonObject mine = features.GetObject("gpu-product-line");
            Assert.NotNull(mine);
            Assert.Equal(1, mine.GetInt32("_schema"), "the core writes the version");
            Assert.True(mine.GetBool("divisionFounded"));
            Assert.Equal(5L, mine.GetInt64("nextId"));
            Assert.Equal(2, features.GetObject("some-other-feature").GetInt32("_schema"), "a section nobody here owns is kept");
            Assert.Equal(9, features.GetObject("some-other-feature").GetInt32("value"));
        }

        [Test]
        public void SideFile_ANewerSection_GivesDefaults_AndIsKeptAsItWas()
        {
            // AC-9.5
            Loaded loaded = NewState(null);
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(loaded.Participant, out problem), problem);

            string text = GpuFiles.Fixture("save-sidefile-newer.json");
            SideFileReadReport report = model.Apply(text);
            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.False(loaded.Data.divisionFounded, "the feature plays with the empty state");

            loaded.Data.divisionFounded = true;   // this session's progress must not reach the stored section
            SideFileBuildReport build = model.Build(new SideFileHeader());
            JsonObject kept = build.Document.GetObject("features").GetObject("gpu-product-line");
            JsonObject original = GpuFiles.ParseObject(text, "newer").GetObject("features").GetObject("gpu-product-line");
            Assert.Equal(ModJson.Write(original, true), ModJson.Write(kept, true));
            Assert.Equal(7, kept.GetInt32("_schema"));
            Assert.True(kept.ContainsKey("somethingFromTheFuture"));
        }

        [Test]
        public void SideFile_NoSection_ResetsTheState()
        {
            Loaded loaded = ReadSection(null, GpuFiles.FixtureJson("save-design-example.json"));
            var model = new SideFileModel();
            string problem;
            Assert.True(model.TryRegister(loaded.Participant, out problem), problem);

            SideFileReadReport report = model.Apply("{ \"schema\": 1, \"features\": { } }");
            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.False(loaded.Data.divisionFounded, "a save without the section: the division is not founded");
            Assert.False(loaded.Data.market.seeded);

            model.StartNewGame();
            Assert.False(loaded.Data.divisionFounded);
        }

        // ---- the checks of a dirty section ----

        private static Loaded ReadDirty()
        {
            return ReadSection(Shipped(), GpuFiles.FixtureJson("save-dirty.json"));
        }

        [Test]
        public void Dirty_TopLevelAndResearch()
        {
            Loaded loaded = ReadDirty();
            GpuSaveSection s = loaded.Data;

            Assert.Equal("single", s.marketModeApplied);
            Assert.Null(s.foundedOn, "month 13 is not a date");
            Assert.True(s.divisionFounded);
            Assert.Equal(new[] { "arch.2d", "vram.dram" }, s.research.completed.ToArray(), "duplicates, unknown and empty ids are out");
            Assert.Equal(string.Empty, s.research.currentId, "an unknown node is not researched");
            Assert.Equal(0f, s.research.currentProgress);
            Assert.Equal(new[] { "vram.vram", "cool.fan" }, s.research.queue.ToArray(), "repeats, finished nodes and unknown ids leave the queue");
            Assert.Equal(1f, s.research.funding, "funding is kept inside [fundingMin, fundingMax]");
            Assert.Equal(5L, s.nextId, "lifted above the ids in use (g-4)");
            AssertHas(loaded.State.Warnings, "marketModeApplied 'weird'");
            AssertHas(loaded.State.Warnings, "foundedOn");
            AssertHas(loaded.State.Warnings, "'arch.nope' is not a research node of the content");
            AssertHas(loaded.State.Warnings, "nextId");
        }

        [Test]
        public void Dirty_DesignsAndProjects()
        {
            Loaded loaded = ReadDirty();
            GpuSaveSection s = loaded.Data;

            Assert.Equal(new[] { "d-1", "d-2" }, s.designs.Select(d => d.id).ToArray(), "a repeated id and a design without an id are dropped");
            GpuDesignSave d1 = s.designs[0];
            Assert.Equal("Old card", d1.name, "tags and control characters are removed");
            Assert.Equal("arch.unknown", d1.archId, "an unknown part does not delete the design");
            Assert.Equal(0f, d1.die);
            Assert.Equal(0, d1.busWidth);
            Assert.Equal(0f, d1.stats.powerW);
            Assert.Equal(225.9, d1.stats.effectivePerf);

            Assert.Equal(new[] { "p-2" }, s.projects.Select(p => p.id).ToArray(), "a project of a design with an unknown part, and one of a missing design, are cancelled");
            GpuProjectSave p2 = s.projects[0];
            Assert.Equal(1f, p2.progress);
            Assert.Equal(0f, p2.totalCost);
            Assert.Equal(0, p2.days);
            Assert.Equal(1f, p2.inflationAtStart, "0 is not an inflation");
            AssertHas(loaded.State.Warnings, "projects[p-1]");
            AssertHas(loaded.State.Warnings, "'d-404' is not in the section");
        }

        [Test]
        public void Dirty_Products()
        {
            // AC-9.4: stock -5 becomes 0, the product with an unknown part keeps its snapshot and a line goes to the log
            Loaded loaded = ReadDirty();
            GpuSaveSection s = loaded.Data;

            Assert.Equal(new[] { "g-1", "g-2", "g-4" }, s.products.Select(p => p.id).ToArray(),
                "an unknown owner and a repeated id are dropped");
            GpuProductSave g1 = s.products[0];
            Assert.Equal(0f, g1.price);
            Assert.Equal(0, g1.stock);
            Assert.Equal(0, g1.lines);
            Assert.Equal(0, g1.unitsPerDay);
            Assert.Equal("player", g1.companyKey, "the numeric id \"42\" is not a key; the player's product belongs to the player");
            Assert.Null(g1.released, "30 February is not a date");
            Assert.Equal(225.9, g1.stats.effectivePerf, "the snapshot");
            Assert.Equal(37.7f, g1.stats.perGoodExtras);
            Assert.Equal(new[] { "consumer", "other" }, g1.segIds.ToArray());
            Assert.Equal(new[] { 1f, 0.05f }, g1.pop.ToArray(), "popularity in [0, 1], missing values start at the floor");
            Assert.Equal(0, g1.ledger.startMonthIndex);
            Assert.Equal(new[] { 10, 0, 30 }, g1.ledger.sold.ToArray());
            Assert.Equal(new[] { 1 }, g1.ledger.produced.ToArray());
            Assert.Equal(new[] { 5f, 0f }, g1.ledger.income.ToArray());

            Assert.Equal("ai:12", s.products[1].companyKey);
            GpuProductSave g4 = s.products[2];
            Assert.Equal("retired", g4.status, "an unknown status is the safe one");
            Assert.Equal(string.Empty, g4.companyKey, "\"ai:\" has no number");
            Assert.Equal(new[] { 0.05f }, g4.pop.ToArray());

            AssertHas(loaded.State.Warnings, "products[g-1].stock -5 is negative");
            AssertHas(loaded.State.Warnings, "designs[d-1].archId 'arch.unknown' is not in the content");
            AssertHas(loaded.Logged, "stock");
            Assert.Equal(loaded.State.Warnings.Count, loaded.Logged.Count, "every change reaches the log callback");
        }

        [Test]
        public void Dirty_VendorsMarketSynergyNewsAndOrders()
        {
            Loaded loaded = ReadDirty();
            GpuSaveSection s = loaded.Data;

            Assert.Equal(1, s.vendors.Count);
            GpuVendorSave ati = s.vendors[0];
            Assert.Equal("pending", ati.state);
            Assert.Equal(string.Empty, ati.companyKey, "\"player \" with a space is not a key");
            Assert.Equal(0, ati.launchCount);
            Assert.Equal(1, ati.lastLaunch.Count, "a repeated segment, an empty one and one with a bad date are dropped");
            Assert.Equal(1998, ati.lastLaunch[0].date.y);

            Assert.Equal(new[] { "consumer", "compute" }, s.market.openSegIds.ToArray());
            Assert.Equal(new[] { 310f, 0f }, s.market.potentialSales.ToArray(), "aligned with the segments");
            Assert.Equal(new[] { 0.5f, 1f, 0f }, s.market.playerShareDaily.ToArray());
            Assert.Equal(new[] { 5, 0 }, s.market.playerSoldMonthly.ToArray());

            Assert.Equal(1, s.synergy.entries.Count);
            Assert.Equal("player", s.synergy.entries[0].companyKey);
            Assert.Equal(1f, s.synergy.entries[0].sCpu);
            Assert.Equal(0f, s.synergy.entries[0].sGpu);

            Assert.Equal(1, s.news.lastLeaderMail.Count, "an entry without a date is dropped");

            // "k.unknown" is no offer of consoles.json, and the "sony.2" that is left names a card nobody has: neither order has anything to stand on
            Assert.Equal(new[] { "k.unknown", "sony.2" }, s.consoleOrders.Select(o => o.id).ToArray());
            Assert.True(s.consoleOrders.All(o => o.status == "cancelled"));
            Assert.Equal(new[] { 1, 0 }, s.consoleOrders[1].deliveredWindow.ToArray());
        }

        // ---- numbers that are not numbers ----

        [Test]
        public void Sanitize_NotFiniteNumbers_GoBackToTheirDefaults()
        {
            var section = new GpuSaveSection();
            section.research.funding = float.NaN;
            section.research.currentId = "some-node";
            section.research.currentProgress = float.NaN;
            section.designs.Add(new GpuDesignSave { id = "d-1", die = float.PositiveInfinity, clockMHz = float.NaN, stats = new GpuStatsSave { powerW = float.NegativeInfinity, effectivePerf = double.NaN } });
            section.projects.Add(new GpuProjectSave { id = "p-1", designId = "d-1", progress = float.NaN, totalCost = float.PositiveInfinity, inflationAtStart = float.NaN });
            GpuProductSave product = Product("g-1");
            product.price = float.NaN;
            product.vramMB = float.PositiveInfinity;
            product.segIds.Add("consumer");
            product.pop.Add(float.NaN);
            product.ledger.income.Add(float.NaN);
            product.ledger.cost.Add(float.PositiveInfinity);
            section.products.Add(product);
            section.market.playerShareDaily.Add(float.NaN);
            section.synergy.entries.Add(new GpuSynergyEntry { companyKey = "player", sCpu = float.NaN, sGpu = float.PositiveInfinity });

            var state = new GpuState();
            state.Sanitize(section);

            Assert.Equal(1f, section.research.funding);
            Assert.Equal(0f, section.research.currentProgress);
            Assert.Equal(0f, section.designs[0].die);
            Assert.Equal(0f, section.designs[0].clockMHz);
            Assert.Equal(0f, section.designs[0].stats.powerW);
            Assert.Equal(0.0, section.designs[0].stats.effectivePerf);
            Assert.Equal(0f, section.projects[0].progress);
            Assert.Equal(0f, section.projects[0].totalCost);
            Assert.Equal(1f, section.projects[0].inflationAtStart);
            Assert.Equal(0f, section.products[0].price);
            Assert.Equal(0f, section.products[0].vramMB);
            Assert.Equal(new[] { 0.05f }, section.products[0].pop.ToArray());
            Assert.Equal(new[] { 0f }, section.products[0].ledger.income.ToArray());
            Assert.Equal(new[] { 0f }, section.products[0].ledger.cost.ToArray());
            Assert.Equal(new[] { 0f }, section.market.playerShareDaily.ToArray());
            Assert.Equal(0f, section.synergy.entries[0].sCpu);
            Assert.Equal(0f, section.synergy.entries[0].sGpu);

            // what is left writes cleanly: no "null" where a number was
            var errors = new List<string>();
            JsonNode node = ModJson.From(section, errors);
            Assert.Equal(0, errors.Count, Lines(errors));
            Assert.NotNull(node);
        }

        [Test]
        public void Sanitize_FundingIsKeptInsideTheLimitsOfTheBalance()
        {
            foreach (var pair in new[] { new[] { 0f, 0.1f }, new[] { 0.05f, 0.1f }, new[] { 0.5f, 0.5f }, new[] { 1f, 1f }, new[] { 1.5f, 1f }, new[] { -2f, 0.1f } })
            {
                var section = new GpuSaveSection();
                section.research.funding = pair[0];
                new GpuState().Sanitize(section);
                Assert.Equal(pair[1], section.research.funding, "funding " + pair[0]);
            }
        }

        [Test]
        public void Sanitize_Queue_IsCutToTheLimitOfTheBalance()
        {
            var section = new GpuSaveSection();
            section.research.completed.Add("done");
            section.research.currentId = "now";
            for (int i = 0; i < 9; i++) section.research.queue.Add("q" + i);
            section.research.queue.Insert(2, "done");
            section.research.queue.Insert(4, "now");

            new GpuState().Sanitize(section);   // without content any id will do; queueMax is 5
            Assert.Equal(new[] { "q0", "q1", "q2", "q3", "q4" }, section.research.queue.ToArray());
        }

        // ---- limits ----

        [Test]
        public void Limits_CutTheLists()
        {
            var section = new GpuSaveSection();
            for (int i = 1; i <= 501; i++)
            {
                section.designs.Add(new GpuDesignSave { id = "d-" + i, stats = new GpuStatsSave() });
                section.products.Add(Product("g-" + i));
            }
            for (int i = 1; i <= 33; i++)
            {
                section.vendors.Add(new GpuVendorSave { id = "v" + i, state = "active" });
                section.consoleOrders.Add(new GpuConsoleOrderSave { id = "k" + i, status = "ended" });
            }
            var ledger = section.products[0].ledger;
            for (int i = 1; i <= 601; i++) ledger.sold.Add(i);

            var state = new GpuState();
            state.Sanitize(section);

            Assert.Equal(500, section.products.Count);
            Assert.Equal("g-500", section.products[499].id, "the end of the list is what is cut");
            Assert.Equal(500, section.designs.Count);
            Assert.Equal(32, section.vendors.Count);
            Assert.Equal(32, section.consoleOrders.Count);
            Assert.Equal(600, ledger.sold.Count);
            Assert.Equal(2, ledger.sold[0], "of a ring the oldest months are cut");
            Assert.Equal(601, ledger.sold[599]);
            AssertHas(state.Warnings, "products: more than 500");
            AssertHas(state.Warnings, "vendors: more than 32");
            AssertHas(state.Warnings, "consoleOrders: more than 32");
            AssertHas(state.Warnings, "ledger.sold has 601 entries, the limit is 600");
        }

        [Test]
        public void Limits_FollowTheBalanceOfTheContent()
        {
            TextContentSource source = GpuFiles.Minimal().Override("balance", "{ \"schema\": 1, \"limits\": { \"products\": 3, \"ledgerMonths\": 2 }, \"console\": { \"graceDays\": 4 } }");
            GpuContentDb db = GpuFiles.Load(source, new List<string>());

            var section = new GpuSaveSection();
            for (int i = 1; i <= 5; i++) section.products.Add(Product("g-" + i));
            section.products[0].ledger.sold.AddRange(new[] { 1, 2, 3 });
            section.consoleOrders.Add(new GpuConsoleOrderSave { id = "k.one", status = "ended", deliveredWindow = new List<int> { 1, 2, 3, 4, 5, 6 } });
            new GpuState(db).Sanitize(section);

            Assert.Equal(3, section.products.Count);
            Assert.Equal(new[] { 2, 3 }, section.products[0].ledger.sold.ToArray());
            Assert.Equal(new[] { 3, 4, 5, 6 }, section.consoleOrders[0].deliveredWindow.ToArray(), "graceDays of the balance");
        }

        // ---- keys, dates, names ----

        [Test]
        public void CompanyKeys_OnlyPlayerAiNumberOrEmpty()
        {
            var good = new[] { string.Empty, "player", "ai:0", "ai:12", "ai:123456789012" };
            var bad = new[] { "42", "ai:", "ai:x", "AI:1", "player ", " ai:1", "ai:-1", "ai:1.5", "Player", "company" };

            var section = new GpuSaveSection();
            int n = 0;
            foreach (string key in good.Concat(bad))
            {
                GpuProductSave product = Product("g-" + (++n), "vendor");
                product.companyKey = key;
                section.products.Add(product);
            }
            var state = new GpuState();
            state.Sanitize(section);

            for (int i = 0; i < good.Length; i++) Assert.Equal(good[i], section.products[i].companyKey, "valid key '" + good[i] + "'");
            for (int i = 0; i < bad.Length; i++) Assert.Equal(string.Empty, section.products[good.Length + i].companyKey, "invalid key '" + bad[i] + "'");
            Assert.Equal(bad.Length, state.Warnings.Count(w => w.Contains(".companyKey")));
        }

        [Test]
        public void CompanyKeys_NoNumericSaveIdIsEverWritten()
        {
            var section = new GpuSaveSection();
            GpuProductSave product = Product("g-1", "player");
            product.companyKey = "1234";   // a SaveID of the game
            section.products.Add(product);
            section.vendors.Add(new GpuVendorSave { id = "ati", state = "active", companyKey = "77" });
            new GpuState().Sanitize(section);

            Assert.Equal("player", section.products[0].companyKey);
            Assert.Equal(string.Empty, section.vendors[0].companyKey);
        }

        [Test]
        public void Dates_MustBeRealDates()
        {
            var good = new[] { new[] { 1995, 3, 1 }, new[] { 2000, 2, 29 }, new[] { 1971, 12, 31 }, new[] { 2200, 1, 1 } };
            var bad = new[] { new[] { 1995, 13, 1 }, new[] { 1995, 0, 1 }, new[] { 1995, 2, 30 }, new[] { 1900, 2, 29 }, new[] { 0, 1, 1 }, new[] { 1995, 4, 31 }, new[] { 2201, 1, 1 }, new[] { 1995, 1, 0 } };

            foreach (int[] date in good)
            {
                var section = new GpuSaveSection { foundedOn = new GpuDate { y = date[0], m = date[1], d = date[2] } };
                new GpuState().Sanitize(section);
                Assert.NotNull(section.foundedOn, "valid date " + string.Join("-", date));
            }
            foreach (int[] date in bad)
            {
                var section = new GpuSaveSection { foundedOn = new GpuDate { y = date[0], m = date[1], d = date[2] } };
                new GpuState().Sanitize(section);
                Assert.Null(section.foundedOn, "invalid date " + string.Join("-", date));
            }
        }

        [Test]
        public void CleanName_RemovesTagsAndControlCharacters_AndCutsTheLength()
        {
            Assert.Equal("Vortex 2", GpuState.CleanName("Vortex 2", 32));
            Assert.Equal("Vortex 2", GpuState.CleanName("  Vortex 2  ", 32));
            Assert.Equal("Bold name", GpuState.CleanName("<b>Bold</b> name", 32));
            Assert.Equal("Red", GpuState.CleanName("<color=#ff0000>Red</color>", 32));
            Assert.Equal("Cut", GpuState.CleanName("Cut<size=100", 32), "an unclosed tag takes the rest of the text");
            Assert.Equal("AB", GpuState.CleanName("A\u0001\u0007B\u007f", 32));
            Assert.Equal(string.Empty, GpuState.CleanName(null, 32));
            Assert.Equal(string.Empty, GpuState.CleanName("<b></b>", 32));
            Assert.Equal("Radeon", GpuState.CleanName("Radeon HD 7970 GHz Edition", 6));
            Assert.Equal("Radeon HD 7970 GHz Edition", GpuState.CleanName("Radeon HD 7970 GHz Edition", 0), "no limit");
            Assert.Equal("Name", GpuState.CleanName("Name stays", 4).TrimEnd());
            Assert.Equal("a", GpuState.CleanName("a😀", 2), "a character is not cut in two");
            Assert.Equal("Видеокарта", GpuState.CleanName("Видеокарта", 32));
        }

        [Test]
        public void Names_AreCleanedInTheSection()
        {
            var section = new GpuSaveSection();
            section.designs.Add(new GpuDesignSave { id = "d-1", name = "<b>" + new string('x', 50) + "</b>", stats = new GpuStatsSave() });
            section.products.Add(Product("g-1"));
            section.products[0].name = "<i>Card</i>";
            new GpuState().Sanitize(section);

            Assert.Equal(new string('x', 32), section.designs[0].name, "nameMaxLength 32");
            Assert.Equal("Card", section.products[0].name);
        }

        // ---- products: popularity, status ----

        [Test]
        public void Popularity_IsAlignedWithTheSegments()
        {
            var section = new GpuSaveSection();
            GpuProductSave shortPop = Product("g-1");
            shortPop.segIds.AddRange(new[] { "a", "b", "c" });
            shortPop.pop.Add(0.5f);
            GpuProductSave longPop = Product("g-2");
            longPop.segIds.AddRange(new[] { "a", "b" });
            longPop.pop.AddRange(new[] { 0.1f, 0.2f, 0.3f, 0.4f });
            section.products.Add(shortPop);
            section.products.Add(longPop);
            new GpuState().Sanitize(section);

            Assert.Equal(new[] { 0.5f, 0.05f, 0.05f }, section.products[0].pop.ToArray());
            Assert.Equal(new[] { 0.1f, 0.2f }, section.products[1].pop.ToArray());
            Assert.Equal(section.products[0].segIds.Count, section.products[0].pop.Count);
            Assert.Equal(section.products[1].segIds.Count, section.products[1].pop.Count);
        }

        [Test]
        public void ProductWithAnUnknownArchitecture_LivesOnItsSnapshot()
        {
            // AC-9.4 in its smallest form
            JsonObject section = GpuFiles.ParseObject(
                "{ \"designs\": [ { \"id\": \"d-1\", \"name\": \"Old\", \"archId\": \"arch.unknown\", \"memId\": \"vram.sgram\", \"coolerId\": \"cool.fan\" } ],"
                + " \"products\": [ { \"id\": \"g-1\", \"designId\": \"d-1\", \"owner\": \"player\", \"status\": \"selling\", \"price\": 249, \"stock\": -5,"
                + " \"stats\": { \"effectivePerf\": 225.9, \"powerW\": 6.19 } } ] }", "section");
            Loaded loaded = ReadSection(Shipped(), section);

            Assert.Equal(1, loaded.Data.products.Count);
            GpuProductSave product = loaded.Data.products[0];
            Assert.Equal(0, product.stock);
            Assert.Equal("selling", product.status);
            Assert.Equal(249f, product.price);
            Assert.Equal(225.9, product.stats.effectivePerf);
            Assert.Equal(1, loaded.Data.designs.Count);
            AssertHas(loaded.Logged, "arch.unknown");
            AssertHas(loaded.Logged, "stock");
        }

        // ---- console orders (Could) ----

        private static GpuConsoleOrderSave OrderAfterRead(string id, string status, string productId, params GpuProductSave[] products)
        {
            var section = new GpuSaveSection();
            section.products.AddRange(products);
            section.consoleOrders.Add(new GpuConsoleOrderSave { id = id, status = status, productId = productId });
            new GpuState(Minimal()).Sanitize(section);
            return section.consoleOrders[0];
        }

        [Test]
        public void ConsoleOrders_NeedAnOfferOfTheContentAndAProductOfThePlayer()
        {
            GpuProductSave mine = Product("g-1", "player");
            GpuProductSave theirs = Product("g-2", "vendor");

            Assert.Equal("active", OrderAfterRead("k.one", "active", "g-1", mine, theirs).status);
            Assert.Equal("cancelled", OrderAfterRead("k.one", "active", "g-2", mine, theirs).status, "a vendor's product is not the player's");
            Assert.Equal("cancelled", OrderAfterRead("k.one", "active", "g-404", mine, theirs).status);
            Assert.Equal("cancelled", OrderAfterRead("k.one", "active", string.Empty, mine, theirs).status, "an active order delivers a product");
            Assert.Equal("offered", OrderAfterRead("k.one", "offered", string.Empty, mine, theirs).status, "an offer has no product yet");
            Assert.Equal("cancelled", OrderAfterRead("k.one", "offered", "g-404", mine, theirs).status);
            Assert.Equal("cancelled", OrderAfterRead("k.nope", "offered", string.Empty, mine, theirs).status, "no such offer in the content");
            Assert.Equal("cancelled", OrderAfterRead("k.nope", "active", "g-1", mine, theirs).status);
            Assert.Equal("ended", OrderAfterRead("k.nope", "ended", "g-404", mine, theirs).status, "a finished order is history, it is left alone");
            Assert.Equal("declined", OrderAfterRead("k.nope", "declined", string.Empty, mine, theirs).status);
            Assert.Equal("cancelled", OrderAfterRead("k.one", "paused", "g-1", mine, theirs).status, "an unknown status is cancelled");
        }

        // ---- wrong shapes ----

        [Test]
        public void WrongTypes_AreReported_AndTheRestIsStillRead()
        {
            JsonObject section = GpuFiles.ParseObject(
                "{ \"divisionFounded\": \"yes\", \"nextId\": \"x\", \"products\": { \"a\": 1 }, \"research\": { \"funding\": 0.5, \"queue\": 7 },"
                + " \"designs\": [ 5, { \"id\": \"d-1\", \"die\": \"big\", \"busWidth\": 128 } ] }", "section");
            Loaded loaded = ReadSection(null, section);

            Assert.False(loaded.Data.divisionFounded);
            Assert.Equal(2L, loaded.Data.nextId, "the counter did not read, so it starts at 1 and is lifted above d-1");
            Assert.Equal(0, loaded.Data.products.Count);
            Assert.Equal(0.5f, loaded.Data.research.funding, "a field that is fine is read");
            Assert.Equal(1, loaded.Data.designs.Count);
            Assert.Equal(128, loaded.Data.designs[0].busWidth);
            Assert.Equal(0f, loaded.Data.designs[0].die);
            Assert.True(loaded.State.Warnings.Count >= 4, "the problems of the shapes are reported:" + Lines(loaded.State.Warnings));
            AssertHas(loaded.State.Warnings, "divisionFounded");
        }

        [Test]
        public void ASectionOfOtherTypes_DoesNotThrow()
        {
            foreach (string text in new[] { "{}", "{ \"market\": null, \"research\": null, \"synergy\": null, \"news\": null, \"designs\": null, \"products\": [ null, 3 ] }",
                "{ \"products\": [ { \"id\": \"g-1\", \"owner\": \"player\", \"segIds\": null, \"pop\": null, \"ledger\": null, \"stats\": null } ] }" })
            {
                Loaded loaded = ReadSection(null, GpuFiles.ParseObject(text, "section"));
                Assert.NotNull(loaded.Data.market);
                Assert.NotNull(loaded.Data.research);
                Assert.NotNull(loaded.Data.synergy);
                Assert.NotNull(loaded.Data.news);
                foreach (GpuProductSave product in loaded.Data.products)
                {
                    Assert.NotNull(product.ledger);
                    Assert.NotNull(product.stats);
                    Assert.NotNull(product.segIds);
                    Assert.NotNull(product.pop);
                }
                Assert.NotNull(loaded.Participant.Write());
            }
        }

        [Test]
        public void ALoggerThatThrows_DoesNotBreakLoading()
        {
            var state = new GpuState(null, message => { throw new InvalidOperationException("log is broken"); });
            state.Read(GpuFiles.FixtureJson("save-dirty.json"), 1);
            Assert.True(state.Warnings.Count > 5);
            Assert.True(state.Data.divisionFounded);
        }

        // ---- ids ----

        [Test]
        public void NextId_CountsUp_ForAllKinds()
        {
            var state = new GpuState();
            Assert.Equal("d-1", state.NextId("d"));
            Assert.Equal("p-2", state.NextId("p"));
            Assert.Equal("g-3", state.NextId("g"));
            Assert.Equal(4L, state.Data.nextId);
            Assert.True(state.NextId(null).StartsWith("x-4"), "a missing prefix still gives an id");
        }

        [Test]
        public void NextId_ComesBackWithTheSave_AndNeverRepeatsAnIdInUse()
        {
            var state = new GpuState();
            string a = state.NextId("g");
            string b = state.NextId("g");
            Assert.NotEqual(a, b);

            Loaded loaded = ReadSection(null, state.Write());
            Assert.Equal(3L, loaded.Data.nextId);
            Assert.Equal("g-3", loaded.State.NextId("g"));

            // a section whose counter was lost or set too low is lifted above the ids it contains
            var section = new GpuSaveSection { nextId = 3 };
            section.designs.Add(new GpuDesignSave { id = "d-17", stats = new GpuStatsSave() });
            section.products.Add(Product("g-9"));
            section.projects.Add(new GpuProjectSave { id = "p-12", designId = "d-17" });
            var healed = new GpuState();
            healed.Sanitize(section);
            Assert.Equal(18L, section.nextId);
            AssertHas(healed.Warnings, "nextId 3 is not above the ids in use, 18 is used");

            var zero = new GpuSaveSection { nextId = -4 };
            new GpuState().Sanitize(zero);
            Assert.Equal(1L, zero.nextId);
        }

        [Test]
        public void NumberOf_ReadsTheCounterOfAnId()
        {
            Assert.Equal(9L, GpuState.NumberOf("g-9"));
            Assert.Equal(17L, GpuState.NumberOf("d-17"));
            Assert.Equal(5L, GpuState.NumberOf("my-card-5"));
            Assert.Equal(0L, GpuState.NumberOf("amd.gpu"));
            Assert.Equal(0L, GpuState.NumberOf("g-"));
            Assert.Equal(0L, GpuState.NumberOf("g-9x"));
            Assert.Equal(0L, GpuState.NumberOf(null));
            Assert.Equal(0L, GpuState.NumberOf("g-99999999999999999999"));
        }
    }
}
