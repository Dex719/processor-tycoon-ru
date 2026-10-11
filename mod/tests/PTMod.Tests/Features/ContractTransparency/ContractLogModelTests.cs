using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-006: the contract log as data (FR-8, NFR-3, NFR-6, NFR-7): the ring of 500, the age of the tender results, the way through the JSON of the side file, a missing or
    /// half-broken section, the size of a full log, the filters and the counters.
    /// </summary>
    [Covers(typeof(ContractLogModel), typeof(ContractLogCodec), typeof(LogRing<>), typeof(LogFilter), typeof(LogFilters), typeof(LogTypes), typeof(LogSources),
        typeof(LogDataKeys), typeof(LogEntry), typeof(ContractTransparencySave), typeof(TenderResultDto), typeof(OfferRowDto), typeof(PrunedRowDto), typeof(LogDates))]
    public class ContractLogModelTests
    {
        private static readonly DateTime Today = new DateTime(1986, 1, 1);

        // ---- builders ----

        private static LogEntry Entry(string date, string type, int n)
        {
            LogEntry entry = LogEntry.Create(date, type, LogSources.Manual);
            entry.contractKey = "Apple|1986-1|52000|299|1";
            entry.contractLabel = "Apple — Computer, 52 000 шт./мес.";
            entry.cpuId = 4000 + n;
            entry.cpuName = "Z-" + n.ToString(CultureInfo.InvariantCulture);
            entry.reasonKey = "ct.reason.margin";
            return entry;
        }

        private static LogEntry Decision(int n)
        {
            LogEntry entry = Entry("1985-12-31", LogTypes.AutomationDecision, n);
            entry.Put(LogDataKeys.Margin, 0.35);
            entry.Put(LogDataKeys.Threshold, 0.4);
            entry.Put(LogDataKeys.Payment, 199);
            entry.Put(LogDataKeys.UnitCost, 129.35);
            return entry;
        }

        private static TenderResultDto Tender(string date, string outcome, string key)
        {
            var tender = new TenderResultDto
            {
                contractKey = key,
                contractLabel = "Apple — Computer, 52 000 шт./мес.",
                date = date,
                outcome = outcome,
                reasonKey = outcome == TenderOutcomeClassifier.OutcomeWon ? "ct.tender.won" : "ct.tender.outbid",
                playerCpuId = 4110,
                playerCpuName = "Z-386",
                playerGameChance = 40,
                playerActual = 0.2105,
                winnerCpuId = 3977,
                winnerCompany = "Intel",
                winnerCpuName = "i386",
            };
            tender.offers.Add(new OfferRowDto { company = "Intel", cpu = "i386", ips = 60, chance = 60, actual = 0.7895 });
            tender.offers.Add(new OfferRowDto { company = "Игрок", cpu = "Z-386", ips = 40, chance = 40, actual = 0.2105, player = true });
            return tender;
        }

        private static ContractLogModel Roundtrip(ContractLogModel model, DateTime today, out string text)
        {
            JsonObject section = ContractLogCodec.Write(model, null);
            text = ModJson.Write(section, true);

            JsonNode parsed;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out parsed, out error), "the written section is valid JSON: " + error);

            var copy = new ContractLogModel();
            ContractLogCodec.Read(copy, (JsonObject)parsed, ContractLogCodec.SchemaVersion, ContractRules.Default, today, null);
            return copy;
        }

        // ---- the ring ----

        [Test]
        public void Ring_KeepsTheNewestItems_AndPushesTheOldestOut()
        {
            var ring = new LogRing<int>(3);
            Assert.False(ring.Add(1));
            Assert.False(ring.Add(2));
            Assert.False(ring.Add(3));
            Assert.True(ring.Add(4), "a full ring pushes the oldest out");
            Assert.True(ring.Add(5));

            Assert.Equal(3, ring.Count);
            Assert.Equal(new[] { 3, 4, 5 }, ring.ToList().ToArray());
            Assert.Equal(3, ring[0]);
            Assert.Equal(5, ring[2]);
            Assert.Throws<ArgumentOutOfRangeException>(() => { int ignored = ring[3]; });
            Assert.Throws<ArgumentOutOfRangeException>(() => new LogRing<int>(0));
        }

        [Test]
        public void Ring_GrowsToItsCapacity_AndResizesWithoutLosingTheNewest()
        {
            var ring = new LogRing<int>(100);
            for (int i = 0; i < 100; i++) ring.Add(i);
            Assert.Equal(100, ring.Count);

            int lost = ring.Resize(10);
            Assert.Equal(90, lost);
            Assert.Equal(10, ring.Count);
            Assert.Equal(90, ring[0]);
            Assert.Equal(99, ring[9]);

            ring.Resize(20);
            for (int i = 100; i < 125; i++) ring.Add(i);
            Assert.Equal(20, ring.Count);
            Assert.Equal(105, ring[0]);
            Assert.Equal(124, ring[19]);

            ring.Clear();
            Assert.Equal(0, ring.Count);
            Assert.Equal(20, ring.Capacity);
        }

        // ---- the section ----

        [Test]
        public void SchemaVersion_IsOne_AndTheSectionIsTheFeaturesId()
        {
            Assert.Equal(1, ContractLogCodec.SchemaVersion);
            Assert.Equal("contract-transparency", ContractLogCodec.SectionId);
        }

        [Test]
        public void Write_HasNoVersionField_TheCoreAddsItItself()
        {
            var model = new ContractLogModel();
            model.Add(Decision(1));
            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "k1"));

            JsonObject section = ContractLogCodec.Write(model, null);

            Assert.False(section.ContainsKey("_schema"), "the core writes _schema, the data does not");
            Assert.False(section.ContainsKey("schema"));
            Assert.True(section.ContainsKey("log"));
            Assert.True(section.ContainsKey("tenders"));
            Assert.True(section.ContainsKey("offersSent"));
            Assert.True(section.ContainsKey("tendersWon"));
        }

        [Test]
        public void Reset_GivesAnEmptyLog_AndZeroCounters()
        {
            var model = new ContractLogModel();
            model.Add(Decision(1));
            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeWon, "k1"));
            model.CountOfferSent();

            model.Reset();

            Assert.Equal(0, model.Count);
            Assert.Equal(0, model.TenderCount);
            Assert.Equal(0, model.PendingTenderCount);
            Assert.Equal(0, model.OffersSent);
            Assert.Equal(0, model.TendersWon);
            Assert.Equal(0, model.Query(LogFilter.All).Count);
        }

        [Test]
        public void LoadsWithoutSection_GivesAnEmptyLog()
        {
            var model = new ContractLogModel();
            model.Add(Decision(1));

            ContractLogCodec.Read(model, null, ContractLogCodec.SchemaVersion, ContractRules.Default, Today, null);

            Assert.Equal(0, model.Count);
            Assert.Equal(0, model.TenderCount);
            Assert.Equal(0, model.FromSave(null, 1, ContractRules.Default));
        }

        [Test]
        public void LoadsAnEmptyObject_GivesAnEmptyLog()
        {
            var model = new ContractLogModel();
            ContractLogCodec.Read(model, new JsonObject(), 1, ContractRules.Default, Today, null);

            Assert.Equal(0, model.Count);
            Assert.Equal(0, model.TenderCount);
            Assert.Equal(0, model.OffersSent);
        }

        // ---- capacity ----

        [Test]
        public void Add_520Entries_Keeps_The500Newest()
        {
            var model = new ContractLogModel();
            for (int i = 0; i < 520; i++) Assert.True(model.Add(Entry("1985-12-31", LogTypes.OfferSent, i)));

            Assert.Equal(500, model.Count);
            IReadOnlyList<LogEntry> newestFirst = model.Query(LogFilter.All);
            Assert.Equal("Z-519", newestFirst[0].cpuName);
            Assert.Equal("Z-20", newestFirst[499].cpuName);
        }

        [Test]
        public void FromSave_520Entries_Keeps_The500Newest_AndSaysSo()
        {
            var save = new ContractTransparencySave();
            for (int i = 0; i < 520; i++) save.log.Add(Entry("1985-12-31", LogTypes.OfferSent, i));

            var model = new ContractLogModel();
            var problems = new List<string>();
            int notKept = model.FromSave(save, 1, ContractRules.Default, Today, problems);

            Assert.Equal(20, notKept);
            Assert.Equal(500, model.Count);
            Assert.Equal("Z-519", model.Query(LogFilter.All)[0].cpuName);
            Assert.Equal("Z-20", model.Query(LogFilter.All)[499].cpuName);
            Assert.True(problems.Any(p => p.Contains("over the limit")), string.Join("; ", problems));
        }

        [Test]
        public void Capacity_FollowsTheRules_AndShrinksAtOnce()
        {
            var model = new ContractLogModel();
            for (int i = 0; i < 300; i++) model.Add(Entry("1985-12-31", LogTypes.OfferSent, i));

            ContractRules small = ContractRules.FromJson((JsonObject)Parse("{ \"schema\": 1, \"log\": { \"capacity\": 100 } }"), null);
            Assert.Equal(100, small.LogCapacity, "the capacity comes from rules.json");
            model.SetRules(small);

            Assert.Equal(100, model.LogCapacity);
            Assert.Equal(100, model.Count);
            Assert.Equal("Z-299", model.Query(LogFilter.All)[0].cpuName);

            // A save with more records than the capacity of the rules it is loaded with keeps the newest of them.
            ContractTransparencySave save = model.ToSave();
            for (int i = 300; i < 340; i++) save.log.Add(Entry("1985-12-31", LogTypes.OfferSent, i));
            var loaded = new ContractLogModel();
            loaded.FromSave(save, 1, small, Today, null);
            Assert.Equal(100, loaded.Count);
            Assert.Equal("Z-339", loaded.Query(LogFilter.All)[0].cpuName);
        }

        // ---- the age of the results ----

        [Test]
        public void FromSave_DropsTenderResultsOlderThanTenYears_AndTheirLinesInTheLog()
        {
            var save = new ContractTransparencySave();
            save.tenders.Add(Tender("1975-06-01", TenderOutcomeClassifier.OutcomeLost, "old"));
            save.tenders.Add(Tender("1976-01-01", TenderOutcomeClassifier.OutcomeLost, "border"));
            save.tenders.Add(Tender("1985-01-01", TenderOutcomeClassifier.OutcomeWon, "new"));

            LogEntry oldLine = Entry("1975-06-01", LogTypes.TenderResult, 1);
            LogEntry oldOffer = Entry("1975-06-01", LogTypes.OfferSent, 2);
            save.log.Add(oldLine);
            save.log.Add(oldOffer);
            save.log.Add(Entry("1985-01-01", LogTypes.TenderResult, 3));

            var model = new ContractLogModel();
            var problems = new List<string>();
            int notKept = model.FromSave(save, 1, ContractRules.Default, Today, problems);

            Assert.Equal(2, notKept, "the old result and its line");
            Assert.Equal(2, model.TenderCount);
            Assert.NotNull(model.FindTender("border", "1976-01-01"), "exactly ten years old is kept");
            Assert.NotNull(model.FindTender("new", "1985-01-01"));
            Assert.Null(model.FindTender("old", "1975-06-01"));
            Assert.Equal(2, model.Count, "the old line of a result goes, an old line of another kind stays");
            Assert.Equal(1, model.Query(LogFilter.Manual).Count);
            Assert.True(problems.Any(p => p.Contains("older than 10 years")), string.Join("; ", problems));
        }

        [Test]
        public void FromSave_WithoutADate_MeasuresTheAgeFromTheNewestRecord()
        {
            var save = new ContractTransparencySave();
            save.tenders.Add(Tender("1970-03-01", TenderOutcomeClassifier.OutcomeLost, "old"));
            save.tenders.Add(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "new"));

            var model = new ContractLogModel();
            model.FromSave(save, 1, ContractRules.Default);

            Assert.Equal(1, model.TenderCount);
            Assert.NotNull(model.FindTender("new", "1986-01-01"));
        }

        [Test]
        public void FromSave_HonoursTheYearsOfTheRules()
        {
            ContractRules short3 = ContractRules.FromJson((JsonObject)Parse("{ \"schema\": 1, \"log\": { \"tenderHistoryYears\": 3 } }"), null);
            var save = new ContractTransparencySave();
            save.tenders.Add(Tender("1982-12-31", TenderOutcomeClassifier.OutcomeLost, "old"));
            save.tenders.Add(Tender("1983-01-01", TenderOutcomeClassifier.OutcomeLost, "kept"));

            var model = new ContractLogModel();
            model.FromSave(save, 1, short3, Today, null);

            Assert.Equal(1, model.TenderCount);
            Assert.NotNull(model.FindTender("kept", "1983-01-01"));
        }

        // ---- the way through the JSON ----

        [Test]
        public void Roundtrip_LosesNothing()
        {
            var model = new ContractLogModel();
            model.Add(Decision(1));
            LogEntry sent = Entry("1985-12-20", LogTypes.OfferAutoConfirmed, 2);
            sent.Put(LogDataKeys.Units, 52000);
            sent.Put(LogDataKeys.Actual, 0.2105);
            model.Add(sent);
            model.Add(Entry("1985-12-21", LogTypes.AutoRejected, 3));
            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "Apple|1986-1|52000|299|1"));
            TenderResultDto won = Tender("1986-01-01", TenderOutcomeClassifier.OutcomeWon, "Ibm|1986-1|1000|99|0");
            won.capacityNotChecked = true;
            won.pruned.Add(new PrunedRowDto { company = "Игрок", cpu = "Z-286", required = 3, contracted = 4, capacity = 5, maker = "Foundry", player = true });
            model.AddTender(won);
            model.CountOfferSent();
            model.CountOfferSent();

            string text;
            ContractLogModel copy = Roundtrip(model, Today, out text);

            Assert.Equal(model.Count, copy.Count);
            Assert.Equal(model.TenderCount, copy.TenderCount);
            Assert.Equal(2, copy.OffersSent);
            Assert.Equal(1, copy.TendersWon);

            // Same bytes when written again: nothing was lost or changed on the way.
            string again;
            Roundtrip(copy, Today, out again);
            Assert.Equal(text, again);

            LogEntry back = copy.Query(LogFilter.Manual).First(e => e.type == LogTypes.OfferAutoConfirmed);
            Assert.Equal("Apple — Computer, 52 000 шт./мес.", back.contractLabel);
            Assert.Equal("52000", back.Get(LogDataKeys.Units));
            Assert.Equal("0.2105", back.Get(LogDataKeys.Actual));
            Assert.Equal(4002, back.cpuId);

            TenderResultDto tender = copy.FindTender("Ibm|1986-1|1000|99|0", "1986-01-01");
            Assert.NotNull(tender);
            Assert.True(tender.capacityNotChecked);
            Assert.Equal(1, tender.pruned.Count);
            Assert.True(tender.pruned[0].player);
            Assert.Equal("Foundry", tender.pruned[0].maker);
            Assert.Equal(2, tender.offers.Count);
            Assert.True(tender.offers[1].player);
            Assert.Near(0.2105, tender.offers[1].actual, 1e-12);
        }

        [Test]
        public void ToSave_HoldsTheRecordsOldestFirst_AndNoPendingList()
        {
            var model = new ContractLogModel();
            model.Add(Entry("1985-12-01", LogTypes.OfferSent, 1));
            model.Add(Entry("1985-12-02", LogTypes.OfferSent, 2));
            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "k"));

            ContractTransparencySave save = model.ToSave();

            Assert.Equal(3, save.log.Count, "two lines and the line of the result");
            Assert.Equal("Z-1", save.log[0].cpuName);
            Assert.Equal(LogTypes.TenderResult, save.log[2].type);
            Assert.Equal(1, save.tenders.Count);
            Assert.Equal(1, model.PendingTenderCount, "the waiting results are not part of the save, the letter is due today");

            var loaded = new ContractLogModel();
            loaded.FromSave(save, 1, ContractRules.Default, Today, null);
            Assert.Equal(0, loaded.PendingTenderCount);
        }

        // ---- damaged data ----

        [Test]
        public void FromSave_IsSteadyOnAPartlyBrokenSection()
        {
            const string json = @"{
              ""offersSent"": -5,
              ""tendersWon"": ""many"",
              ""unknownField"": [1, 2, 3],
              ""log"": [
                { ""date"": ""1985-12-31"", ""type"": ""OfferSent"", ""source"": ""Manual"", ""cpuName"": ""ok"", ""data"": { ""units"": ""100"" } },
                { ""date"": ""1985-13-45"", ""type"": ""OfferSent"" },
                { ""date"": ""1985-12-31"", ""type"": ""Teleport"" },
                { ""date"": ""1985-12-31"", ""type"": ""AutoRejected"", ""cpuId"": -7, ""data"": null, ""reasonKey"": null },
                7,
                null,
                { ""date"": 19851231, ""type"": ""OfferSent"" }
              ],
              ""tenders"": [
                { ""date"": ""1985-06-01"", ""outcome"": ""Maybe"" },
                { ""date"": ""1985-06-01"", ""outcome"": ""Lost"", ""playerGameChance"": 400, ""playerActual"": 7.5, ""offers"": [ null, { ""company"": ""A"", ""cpu"": ""B"", ""ips"": -3, ""chance"": -9, ""actual"": 2 } ], ""pruned"": null },
                ""not an object""
              ]
            }";
            var model = new ContractLogModel();
            var problems = new List<string>();
            ContractLogCodec.Read(model, (JsonObject)Parse(json), 1, ContractRules.Default, Today, problems);

            Assert.Equal(0, model.OffersSent, "a negative counter becomes 0");
            Assert.Equal(0, model.TendersWon, "a counter of the wrong kind keeps its default");
            Assert.Equal(2, model.Count, "the two usable lines of the log");
            Assert.Equal(1, model.TenderCount);
            Assert.True(problems.Count >= 5, "every refusal and every field of the wrong kind has a line: " + string.Join("; ", problems));

            LogEntry cleaned = model.Query(LogFilter.AutoReject)[0];
            Assert.Equal(0, cleaned.cpuId);
            Assert.Equal(string.Empty, cleaned.reasonKey);
            Assert.NotNull(cleaned.data);

            TenderResultDto tender = model.Tenders()[0];
            Assert.Equal(100, tender.playerGameChance);
            Assert.Near(1.0, tender.playerActual);
            Assert.Equal(1, tender.offers.Count);
            Assert.Near(0.0, tender.offers[0].ips);
            Assert.Equal(0, tender.offers[0].chance);
            Assert.Near(1.0, tender.offers[0].actual);
            Assert.NotNull(tender.pruned);
        }

        [Test]
        public void FromSave_CutsLongStrings_AndTooManyNumbers()
        {
            string longText = new string('x', 300);
            var entry = Entry("1985-12-31", LogTypes.OfferSent, 1);
            entry.cpuName = longText;
            entry.contractLabel = longText;
            entry.reasonKey = longText;
            for (int i = 0; i < 40; i++) entry.data["k" + i.ToString(CultureInfo.InvariantCulture)] = longText;

            var save = new ContractTransparencySave();
            save.log.Add(entry);
            var tender = Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "k");
            tender.playerCpuName = longText;
            for (int i = 0; i < 80; i++) tender.offers.Add(new OfferRowDto { company = longText, cpu = longText });
            save.tenders.Add(tender);

            var model = new ContractLogModel();
            model.FromSave(save, 1, ContractRules.Default, Today, null);

            LogEntry kept = model.Query(LogFilter.All)[0];
            Assert.Equal(128, kept.cpuName.Length);
            Assert.Equal(128, kept.contractLabel.Length);
            Assert.Equal(128, kept.reasonKey.Length);
            Assert.Equal(LogEntry.MaxData, kept.data.Count);
            Assert.True(kept.data.Values.All(v => v.Length == 128));

            TenderResultDto keptTender = model.Tenders()[0];
            Assert.Equal(128, keptTender.playerCpuName.Length);
            Assert.Equal(TenderResultDto.MaxRows, keptTender.offers.Count);
            Assert.Equal(128, keptTender.offers[TenderResultDto.MaxRows - 1].company.Length);
        }

        [Test]
        public void Add_RefusesWhatCannotBeKept()
        {
            var model = new ContractLogModel();

            Assert.False(model.Add(null));
            Assert.False(model.Add(LogEntry.Create("1986-01-01", "Nope", LogSources.Game)));
            Assert.False(model.Add(LogEntry.Create("yesterday", LogTypes.OfferSent, LogSources.Game)));
            Assert.False(model.AddTender(null));
            Assert.False(model.AddTender(Tender("1986-01-01", "Draw", "k")));
            Assert.False(model.AddTender(Tender("not a date", TenderOutcomeClassifier.OutcomeWon, "k")));
            Assert.Equal(0, model.Count);
            Assert.Equal(0, model.TenderCount);
            Assert.Equal(0, model.TendersWon);
        }

        // ---- size ----

        [Test]
        public void ALogOf500Entries_FitsIn256Kilobytes_AsTheCoreWritesIt_WithoutLosingAnything()
        {
            var model = new ContractLogModel();
            for (int i = 0; i < 500; i++) model.Add(Decision(i));
            var problems = new List<string>();

            JsonObject section = ContractLogCodec.Write(model, problems);
            long bytes = ContractLogCodec.SizeInFile(section);

            Assert.True(bytes <= 256 * 1024, "500 decisions with four numbers each take " + bytes + " bytes in the file, the limit is " + 256 * 1024);
            Assert.Equal(0, problems.Count, "nothing had to be left out: " + string.Join("; ", problems));
            Assert.Equal(500, section.GetArray("log").Count);
        }

        [Test]
        public void ALogOverTheLimit_IsTrimmedInTheFile_TheOldestFirst_AndNotInTheModel()
        {
            string longText = new string('ы', 120);
            var model = new ContractLogModel();
            for (int i = 0; i < 500; i++)
            {
                LogEntry entry = Entry("1985-12-31", LogTypes.AutomationDecision, i);
                entry.contractLabel = longText;
                entry.cpuName = longText;
                for (int k = 0; k < LogEntry.MaxData; k++) entry.Put("key" + k.ToString(CultureInfo.InvariantCulture), longText);
                model.Add(entry);
            }
            for (int i = 0; i < 100; i++)
            {
                TenderResultDto tender = Tender("1985-01-01", TenderOutcomeClassifier.OutcomeLost, "Client" + i.ToString(CultureInfo.InvariantCulture) + "|1986-1|52000|299|1");
                for (int row = 0; row < 12; row++) tender.offers.Add(new OfferRowDto { company = longText, cpu = longText, ips = 12.5 + row, chance = 25, actual = 0.25 });
                model.AddTender(tender);
            }
            Assert.True(ContractLogCodec.SizeInFile(ToObject(model.ToSave())) > 256 * 1024, "the test needs a log that is over the limit");
            var problems = new List<string>();

            JsonObject section = ContractLogCodec.Write(model, problems);

            Assert.True(ContractLogCodec.SizeInFile(section) <= ContractLogCodec.MaxSectionBytes, "the section was cut to the limit: " + ContractLogCodec.SizeInFile(section));
            Assert.True(problems.Any(p => p.Contains("size limit")), string.Join("; ", problems));

            JsonArray log = section.GetArray("log");
            Assert.True(log.Count > 0 && log.Count < 500, "some of the log is kept: " + log.Count);
            Assert.Equal(LogTypes.TenderResult, ((JsonObject)log[log.Count - 1]).GetString("type"), "entries keep their order: the newest line is the last");
            Assert.True(((JsonObject)log[0]).GetInt32("cpuId", -1) > 4100, "the oldest lines are the ones that went (the ring had already pushed out the first hundred)");

            Assert.Equal(500, model.Count, "the model keeps everything: only the file is cut");
            Assert.Equal(100, model.TenderCount);

            // What the file holds reads back as a valid log.
            var copy = new ContractLogModel();
            ContractLogCodec.Read(copy, section, 1, ContractRules.Default, Today, null);
            Assert.Equal(log.Count, copy.Count);
        }

        private static JsonObject ToObject(ContractTransparencySave save)
        {
            return (JsonObject)ModJson.From(save);
        }

        // ---- queries ----

        [Test]
        public void Query_GivesTheNewestFirst_AndEveryFilterItsOwnTypes()
        {
            var model = new ContractLogModel();
            model.Add(Entry("1985-12-01", LogTypes.OfferSent, 1));
            model.Add(Entry("1985-12-02", LogTypes.OfferCancelledOnReview, 2));
            model.Add(Entry("1985-12-03", LogTypes.OfferAutoConfirmed, 3));
            model.Add(Entry("1985-12-31", LogTypes.AutomationDecision, 4));
            model.Add(Entry("1985-12-31", LogTypes.AutomationSummary, 5));
            model.Add(Entry("1985-12-31", LogTypes.Mismatch, 6));
            model.Add(Entry("1986-01-01", LogTypes.AutoRejected, 7));
            model.Add(Entry("1986-01-01", LogTypes.TenderResult, 8));
            model.Add(Entry("1986-02-01", LogTypes.BusinessSigned, 9));

            Assert.Equal(9, model.Query(LogFilter.All).Count);
            Assert.Equal("Z-9", model.Query(LogFilter.All)[0].cpuName);
            Assert.Equal(new[] { "Z-3", "Z-2", "Z-1" }, model.Query(LogFilter.Manual).Select(e => e.cpuName).ToArray());
            Assert.Equal(new[] { "Z-6", "Z-5", "Z-4" }, model.Query(LogFilter.Automation).Select(e => e.cpuName).ToArray());
            Assert.Equal(new[] { "Z-7" }, model.Query(LogFilter.AutoReject).Select(e => e.cpuName).ToArray());
            Assert.Equal(new[] { "Z-8" }, model.Query(LogFilter.Tenders).Select(e => e.cpuName).ToArray());
            Assert.Equal(new[] { "Z-9" }, model.Query(LogFilter.Business).Select(e => e.cpuName).ToArray());
            Assert.Equal(9, model.Query((LogFilter)99).Count, "an unknown filter shows everything");
        }

        [Test]
        public void EveryKnownTypeBelongsToExactlyOneTab()
        {
            string[] types =
            {
                LogTypes.OfferSent, LogTypes.OfferCancelledOnReview, LogTypes.OfferAutoConfirmed, LogTypes.AutomationDecision, LogTypes.AutomationSummary,
                LogTypes.AutoRejected, LogTypes.TenderResult, LogTypes.BusinessSigned, LogTypes.Mismatch,
            };
            LogFilter[] tabs = { LogFilter.Manual, LogFilter.Automation, LogFilter.AutoReject, LogFilter.Tenders, LogFilter.Business };

            foreach (string type in types)
            {
                Assert.True(LogTypes.IsKnown(type), type);
                Assert.Equal(1, tabs.Count(tab => LogFilters.Matches(tab, type)), type + " is on one tab");
                Assert.True(LogFilters.Matches(LogFilter.All, type), type + " is on 'All'");
            }
            Assert.False(LogTypes.IsKnown("nope"));
            Assert.False(LogTypes.IsKnown(null));
        }

        // ---- tenders, the letter and the counters ----

        [Test]
        public void AddTender_StoresIt_WaitsForTheLetter_AddsAnEntry_AndCountsWins()
        {
            var model = new ContractLogModel();

            Assert.True(model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "a")));
            Assert.True(model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeWon, "b")));
            Assert.True(model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeVoid, "c")));

            Assert.Equal(3, model.TenderCount);
            Assert.Equal(3, model.PendingTenderCount);
            Assert.Equal(1, model.TendersWon);
            Assert.Equal(3, model.Query(LogFilter.Tenders).Count, "each result has a line in the log");

            LogEntry line = model.Query(LogFilter.Tenders).Last(e => e.contractKey == "a");
            Assert.Equal(LogSources.Game, line.source);
            Assert.Equal("Lost", line.Get(LogDataKeys.Outcome));
            Assert.Equal(40, line.GetInt(LogDataKeys.Chance, -1));
            Assert.Near(0.2105, line.GetDouble(LogDataKeys.Actual, -1), 1e-12);
            Assert.Equal("Intel", line.Get(LogDataKeys.Winner));
            Assert.Equal("i386", line.Get(LogDataKeys.WinnerCpu));
            Assert.Equal(4110, line.cpuId);
        }

        [Test]
        public void DrainTodayTenders_GivesTheResultsOnce_AndThenNothing()
        {
            var model = new ContractLogModel();
            Assert.Equal(0, model.DrainTodayTenders().Count);

            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "a"));
            model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeWon, "b"));

            IReadOnlyList<TenderResultDto> first = model.DrainTodayTenders();
            Assert.Equal(new[] { "a", "b" }, first.Select(t => t.contractKey).ToArray());
            Assert.Equal(0, model.PendingTenderCount);
            Assert.Equal(0, model.DrainTodayTenders().Count);
            Assert.Equal(2, model.TenderCount, "the results stay in the log");
        }

        [Test]
        public void Tenders_AreKeptInARingOfTheSameSize()
        {
            var model = new ContractLogModel();
            for (int i = 0; i < 520; i++) model.AddTender(Tender("1986-01-01", TenderOutcomeClassifier.OutcomeLost, "k" + i.ToString(CultureInfo.InvariantCulture)));

            Assert.Equal(500, model.TenderCount);
            Assert.Equal("k519", model.Tenders()[0].contractKey);
            Assert.Null(model.FindTender("k19", "1986-01-01"));
            Assert.NotNull(model.FindTender("k20", "1986-01-01"));
            Assert.Equal(500, model.Count);
        }

        [Test]
        public void CountOfferSent_CountsUp()
        {
            var model = new ContractLogModel();
            model.CountOfferSent();
            model.CountOfferSent();
            model.CountOfferSent();

            Assert.Equal(3, model.OffersSent);
            Assert.Equal(3, model.ToSave().offersSent);
        }

        // ---- the entry ----

        [Test]
        public void Entry_PutsAndGetsNumbers_WithTheLimits()
        {
            LogEntry entry = LogEntry.Create("1986-01-01", LogTypes.OfferSent, LogSources.Manual);
            entry.Put("a", "text").Put("b", 7).Put("c", 0.12345).Put("d", double.NaN).Put("", "ignored").Put(null, "ignored");

            Assert.Equal("text", entry.Get("a"));
            Assert.Equal(7, entry.GetInt("b", -1));
            Assert.Equal("0.1235", entry.Get("c"));
            Assert.Equal("0", entry.Get("d"), "a value that is not a number is written as 0");
            Assert.Equal(4, entry.data.Count);
            Assert.Null(entry.Get("missing"));
            Assert.Equal(-1, entry.GetInt("a", -1), "text is not a whole number");
            Assert.Near(0.5, entry.GetDouble("missing", 0.5));

            for (int i = 0; i < 40; i++) entry.Put("k" + i.ToString(CultureInfo.InvariantCulture), i);
            Assert.Equal(LogEntry.MaxData, entry.data.Count);
            entry.Put("a", "changed");
            Assert.Equal("changed", entry.Get("a"), "a key that is already there can still be changed when the entry is full");
        }

        [Test]
        public void Dates_AreWrittenAndReadInOneFormat()
        {
            DateTime parsed;

            Assert.Equal("1986-01-01", LogDates.Write(new DateTime(1986, 1, 1)));
            Assert.Equal(string.Empty, LogDates.Write(default(DateTime)));
            Assert.True(LogDates.TryParse("1986-12-31", out parsed));
            Assert.Equal(new DateTime(1986, 12, 31), parsed);
            Assert.False(LogDates.TryParse("31.12.1986", out parsed));
            Assert.False(LogDates.TryParse("1986-1-1", out parsed));
            Assert.False(LogDates.TryParse(null, out parsed));
            Assert.False(LogDates.TryParse("1986-02-30", out parsed));
        }

        private static JsonNode Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "test JSON: " + error);
            return node;
        }
    }
}
