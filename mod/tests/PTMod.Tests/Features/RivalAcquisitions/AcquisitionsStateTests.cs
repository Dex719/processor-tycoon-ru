using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The books of the feature (TSK-001, FR-14, NFR-3, NFR-7): an empty state is the default, the section binds to the DTO and back without a
    /// version field (the core owns <c>_schema</c>), the reads behind the API answer from the records, and <c>Sanitize</c> drops what cannot be
    /// right. The contract types <c>DealType</c>, <c>DealRecord</c> and <c>WaferLines</c> are covered here too: the state is their user.
    /// </summary>
    [Covers(typeof(AcquisitionsState), typeof(StakeData), typeof(AuctionData), typeof(RescueOrderData), typeof(DealType), typeof(DealRecord), typeof(WaferLines), typeof(IRivalAcquisitionsService))]
    public class AcquisitionsStateTests
    {
        [Test]
        public void TheNullService_AnswersLikeAFeatureThatIsOff()
        {
            IRivalAcquisitionsService service = NullRivalAcquisitionsService.Instance;

            Assert.NotNull(service.Deals);
            Assert.Equal(0, service.Deals.Count);
            Assert.Equal(0.0, service.GetPlayerShare(3));
            Assert.False(service.IsAbsorbedByPlayer(3));
            service.OpenWindow(3);   // nothing happens, nothing throws
        }

        /// <summary>
        /// The section of design.md, "Пример секции бокового файла" (the object under features.rival-acquisitions, without _schema), with the field <c>lastDividend</c> that the
        /// tab "My stakes" needs (TSK-015) added to the stake; a save without it reads 0.
        /// </summary>
        private const string DesignSection = @"{
          ""deals"": [
            { ""id"": 1, ""type"": ""Hostile"", ""targetUniqueId"": 3, ""targetSaveId"": 412, ""targetName"": ""Texas Instruments"",
              ""date"": ""1985-03-01"", ""price"": 696888000.0, ""assumedCash"": 30000000.0, ""share"": 0.0,
              ""keptLines"": true, ""lines"": [ { ""waferId"": 46, ""lines"": 40 } ],
              ""techCount"": 3, ""cpuSaveIds"": [ 1201, 1207 ], ""retiredCpuSaveIds"": [],
              ""providerContracts"": 0, ""clientContracts"": 1, ""partial"": false, ""notes"": """" }
          ],
          ""stakes"": [
            { ""targetUniqueId"": 10, ""targetSaveId"": 388, ""share"": 0.25, ""invested"": 116150000.0,
              ""dividendsPaid"": 3000000.0, ""lastDividend"": 1000000.0, ""since"": ""1986-01-01"" }
          ],
          ""auctions"": [
            { ""targetUniqueId"": 7, ""targetSaveId"": 355, ""bankruptOn"": ""1987-05-03"", ""until"": ""1988-05-02"",
              ""snapshotCpuSaveIds"": [ 980, 991, 1015 ], ""closed"": false }
          ],
          ""rescueOrders"": [ { ""targetUniqueId"": 10, ""targetSaveId"": 388, ""maxAmount"": 100000000.0, ""active"": true } ],
          ""defaultMailSentForSaveIds"": [],
          ""lastHostileOn"": ""1985-03-01"",
          ""backfillDone"": true,
          ""nextDealId"": 2
        }";

        // ---- helpers ----

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static AcquisitionsState Bind(string json)
        {
            var errors = new List<string>();
            AcquisitionsState state = ModJson.Bind<AcquisitionsState>(Parse(json), errors);
            Assert.Equal(0, errors.Count, "bind errors: " + string.Join(" | ", errors));
            return state;
        }

        private static DealRecord Deal(int id, DealType type, int uniqueId, string date = "1985-03-01")
        {
            return new DealRecord { id = id, type = type.ToString(), targetUniqueId = uniqueId, targetSaveId = 100 + uniqueId, targetName = "T" + uniqueId, date = date, price = 1000 };
        }

        private static bool Mentions(List<string> problems, string text)
        {
            return problems.Any(p => p.Contains(text));
        }

        // ---- the default and the section ----

        [Test]
        public void NewState_IsEmpty_AndStartsNumberingAtOne()
        {
            var state = new AcquisitionsState();

            Assert.Equal(0, state.deals.Count);
            Assert.Equal(0, state.stakes.Count);
            Assert.Equal(0, state.auctions.Count);
            Assert.Equal(0, state.rescueOrders.Count);
            Assert.Equal(0, state.defaultMailSentForSaveIds.Count);
            Assert.Equal("", state.lastHostileOn);
            Assert.False(state.backfillDone);
            Assert.Equal(1, state.nextDealId);
        }

        [Test]
        public void EmptyState_WritesAnObjectWithoutAVersionField_AndReadsBack()
        {
            var problems = new List<string>();
            JsonObject written = (JsonObject)ModJson.From(new AcquisitionsState(), problems);

            Assert.Equal(0, problems.Count);
            Assert.False(written.ContainsKey("_schema"), "the version of the section is the core's business");
            Assert.False(written.ContainsKey("schema"));
            Assert.False(written.ContainsKey("version"));
            Assert.True(written.ContainsKey("deals") && written.ContainsKey("stakes") && written.ContainsKey("auctions") && written.ContainsKey("rescueOrders"));

            AcquisitionsState back = ModJson.Bind<AcquisitionsState>(written, new List<string>());
            Assert.Equal(0, back.deals.Count);
            Assert.Equal(1, back.nextDealId);
        }

        [Test]
        public void MissingSectionFields_KeepTheDefaults()
        {
            AcquisitionsState state = Bind("{}");

            Assert.Equal(0, state.deals.Count);
            Assert.Equal(1, state.nextDealId);
            Assert.False(state.backfillDone);
            Assert.Equal(0, state.Sanitize(null));
        }

        [Test]
        public void DesignExampleSection_BindsWithEveryFieldAndRoundTripsLosslessly()
        {
            JsonObject source = Parse(DesignSection);
            AcquisitionsState state = Bind(DesignSection);

            Assert.Equal(1, state.deals.Count);
            DealRecord deal = state.deals[0];
            Assert.Equal(1, deal.id);
            Assert.Equal("Hostile", deal.type);
            Assert.Equal(3, deal.targetUniqueId);
            Assert.Equal(412, deal.targetSaveId);
            Assert.Equal("Texas Instruments", deal.targetName);
            Assert.Equal("1985-03-01", deal.date);
            Assert.Near(696888000.0, deal.price);
            Assert.Near(30000000.0, deal.assumedCash);
            Assert.True(deal.keptLines);
            Assert.Equal(1, deal.lines.Count);
            Assert.Equal(46, deal.lines[0].waferId);
            Assert.Equal(40, deal.lines[0].lines);
            Assert.Equal(3, deal.techCount);
            Assert.Equal(new[] { 1201, 1207 }, deal.cpuSaveIds.ToArray());
            Assert.Equal(0, deal.retiredCpuSaveIds.Count);
            Assert.Equal(1, deal.clientContracts);
            Assert.False(deal.partial);

            Assert.Equal(1, state.stakes.Count);
            Assert.Near(0.25, state.stakes[0].share);
            Assert.Near(116150000.0, state.stakes[0].invested);
            Assert.Near(1000000.0, state.stakes[0].lastDividend);
            Assert.Equal("1986-01-01", state.stakes[0].since);

            Assert.Equal(1, state.auctions.Count);
            Assert.Equal("1988-05-02", state.auctions[0].until);
            Assert.Equal(new[] { 980, 991, 1015 }, state.auctions[0].snapshotCpuSaveIds.ToArray());
            Assert.False(state.auctions[0].closed);

            Assert.Equal(1, state.rescueOrders.Count);
            Assert.True(state.rescueOrders[0].active);
            Assert.Equal("1985-03-01", state.lastHostileOn);
            Assert.True(state.backfillDone);
            Assert.Equal(2, state.nextDealId);

            var problems = new List<string>();
            Assert.Equal(0, state.Sanitize(problems));
            Assert.Equal(0, problems.Count, "the example of the design is valid: " + string.Join(" | ", problems));

            JsonNode written = ModJson.From(state);
            Assert.True(source.Equals(written), "the section survives Bind and From unchanged: " + ModJson.Write(written, false));
        }

        [Test]
        public void NullListsInTheFile_AreRestoredBySanitize()
        {
            AcquisitionsState state = Bind("{ \"deals\": null, \"stakes\": null, \"auctions\": null, \"rescueOrders\": null, \"defaultMailSentForSaveIds\": null, \"lastHostileOn\": null }");
            var problems = new List<string>();

            Assert.Equal(0, state.Sanitize(problems));
            Assert.NotNull(state.deals);
            Assert.NotNull(state.stakes);
            Assert.NotNull(state.auctions);
            Assert.NotNull(state.rescueOrders);
            Assert.NotNull(state.defaultMailSentForSaveIds);
            Assert.Equal("", state.lastHostileOn);
        }

        // ---- the reads behind the API ----

        [Test]
        public void ShareOf_AnswersFromTheStake_AndZeroWithoutOne()
        {
            var state = new AcquisitionsState();
            state.stakes.Add(new StakeData { targetUniqueId = 10, targetSaveId = 388, share = 0.25 });
            state.stakes.Add(new StakeData { targetUniqueId = 11, targetSaveId = 389, share = 0.49 });
            state.stakes.Add(new StakeData { targetUniqueId = 12, targetSaveId = 390, share = 0.8 });   // not a valid stake

            Assert.Near(0.25, state.ShareOf(10));
            Assert.Near(0.49, state.ShareOf(11));
            Assert.Near(0.0, state.ShareOf(12), 0, "an impossible share reads as no stake");
            Assert.Near(0.0, state.ShareOf(99));
            Assert.Near(0.0, state.ShareOf(-1));
        }

        [Test]
        public void IsAbsorbed_IsTrueForTheThreeTakeoverTypesOnly()
        {
            var state = new AcquisitionsState();
            state.deals.Add(Deal(1, DealType.BankruptAssets, 1));
            state.deals.Add(Deal(2, DealType.Distressed, 2));
            state.deals.Add(Deal(3, DealType.Hostile, 3));
            state.deals.Add(Deal(4, DealType.Stake, 4));
            state.deals.Add(Deal(5, DealType.RescueOrder, 5));

            Assert.True(state.IsAbsorbed(1));
            Assert.True(state.IsAbsorbed(2));
            Assert.True(state.IsAbsorbed(3));
            Assert.False(state.IsAbsorbed(4), "a stake does not take the company out of the game");
            Assert.False(state.IsAbsorbed(5));
            Assert.False(state.IsAbsorbed(6));
        }

        [Test]
        public void OpenAuctionOf_SkipsClosedAuctions()
        {
            var state = new AcquisitionsState();
            state.auctions.Add(new AuctionData { targetUniqueId = 7, targetSaveId = 355, bankruptOn = "1987-05-03", until = "1988-05-03", closed = true });
            state.auctions.Add(new AuctionData { targetUniqueId = 8, targetSaveId = 356, bankruptOn = "1987-06-03", until = "1988-06-03" });

            Assert.Null(state.OpenAuctionOf(7));
            Assert.NotNull(state.OpenAuctionOf(8));
            Assert.Null(state.OpenAuctionOf(9));
        }

        [Test]
        public void DealTypeNames_AreRecognisedExactly()
        {
            foreach (string name in new[] { "BankruptAssets", "Distressed", "Hostile", "Stake", "RescueOrder" })
            {
                Assert.True(AcquisitionsState.IsKnownType(name), name);
                Assert.Equal(name, ((DealType)System.Enum.Parse(typeof(DealType), name)).ToString());
            }
            foreach (string name in new[] { "", null, "hostile", "Hostile ", "1", "Takeover", "Hostile,Stake" })
            {
                Assert.False(AcquisitionsState.IsKnownType(name), "'" + name + "'");
            }

            Assert.True(AcquisitionsState.IsAbsorbingType("Hostile"));
            Assert.False(AcquisitionsState.IsAbsorbingType("Stake"));
            Assert.False(AcquisitionsState.IsAbsorbingType(null));
        }

        [Test]
        public void ValueChecks_ShareAndDate()
        {
            Assert.True(AcquisitionsState.IsValidShare(0.01));
            Assert.True(AcquisitionsState.IsValidShare(0.49));
            Assert.True(AcquisitionsState.IsValidShare(0.4999));
            Assert.False(AcquisitionsState.IsValidShare(0));
            Assert.False(AcquisitionsState.IsValidShare(0.5));
            Assert.False(AcquisitionsState.IsValidShare(-0.1));
            Assert.False(AcquisitionsState.IsValidShare(double.NaN));
            Assert.False(AcquisitionsState.IsValidShare(double.PositiveInfinity));

            Assert.True(AcquisitionsState.IsValidDate("1985-03-01"));
            Assert.True(AcquisitionsState.IsValidDate("2024-02-29"));
            Assert.False(AcquisitionsState.IsValidDate("2023-02-29"));
            Assert.False(AcquisitionsState.IsValidDate("1985-3-1"));
            Assert.False(AcquisitionsState.IsValidDate("01.03.1985"));
            Assert.False(AcquisitionsState.IsValidDate(""));
            Assert.False(AcquisitionsState.IsValidDate(null));
        }

        // ---- Sanitize ----

        [Test]
        public void Sanitize_DropsDealsThatCannotBeRight()
        {
            var state = new AcquisitionsState();
            state.deals.Add(Deal(1, DealType.Hostile, 3));                                   // fine
            state.deals.Add(null);                                                           // null
            state.deals.Add(Deal(0, DealType.Hostile, 3));                                   // id below 1
            state.deals.Add(Deal(1, DealType.Distressed, 4));                                // repeated id
            state.deals.Add(new DealRecord { id = 5, type = "Takeover", date = "1985-03-01" });
            state.deals.Add(Deal(6, DealType.Hostile, -1));                                  // negative company
            state.deals.Add(new DealRecord { id = 7, type = "Hostile", targetSaveId = -4, date = "1985-03-01" });
            state.deals.Add(new DealRecord { id = 8, type = "Hostile", date = "1985-03-01", price = -1 });
            state.deals.Add(new DealRecord { id = 9, type = "Hostile", date = "1985-03-01", price = double.NaN });
            state.deals.Add(new DealRecord { id = 10, type = "Hostile", date = "1985-03-01", assumedCash = double.PositiveInfinity });
            state.deals.Add(new DealRecord { id = 11, type = "Stake", date = "1985-03-01", share = 0.5 });
            state.deals.Add(new DealRecord { id = 12, type = "Stake", date = "1985-03-01", share = -0.1 });
            state.deals.Add(Deal(13, DealType.Hostile, 3, "March 1985"));                    // bad date
            state.deals.Add(Deal(14, DealType.Stake, 5));                                    // fine
            state.deals.Last().share = 0.25;

            var problems = new List<string>();
            int dropped = state.Sanitize(problems);

            Assert.Equal(12, dropped, string.Join(" | ", problems));
            Assert.Equal(new[] { 1, 14 }, state.deals.Select(d => d.id).ToArray());
            Assert.Equal(12, problems.Count(p => p.EndsWith("the record is dropped")));
            Assert.Equal(15, state.nextDealId, "the next number is above every deal that is left");
        }

        [Test]
        public void Sanitize_RepairsTheFieldsOfAKeptDeal()
        {
            var deal = Deal(3, DealType.Distressed, 3);
            deal.targetName = null;
            deal.notes = null;
            deal.lines = null;
            deal.cpuSaveIds = new List<int> { 5, -1, 7 };
            deal.retiredCpuSaveIds = null;
            deal.techCount = -4;
            deal.providerContracts = -1;
            deal.clientContracts = -2;
            var state = new AcquisitionsState();
            state.deals.Add(deal);
            state.nextDealId = 4;

            var problems = new List<string>();
            Assert.Equal(0, state.Sanitize(problems));

            Assert.Equal("", deal.targetName);
            Assert.Equal("", deal.notes);
            Assert.NotNull(deal.lines);
            Assert.Equal(new[] { 5, 7 }, deal.cpuSaveIds.ToArray());
            Assert.NotNull(deal.retiredCpuSaveIds);
            Assert.Equal(0, deal.techCount);
            Assert.Equal(0, deal.providerContracts);
            Assert.Equal(0, deal.clientContracts);
            Assert.True(Mentions(problems, "deals[0].cpuSaveIds"));
            Assert.Equal(4, state.nextDealId, "a good counter is left alone");
        }

        [Test]
        public void Sanitize_DropsBadWaferEntries_ButKeepsTheDeal()
        {
            var deal = Deal(1, DealType.Hostile, 3);
            deal.lines.Add(new WaferLines { waferId = 46, lines = 40 });
            deal.lines.Add(null);
            deal.lines.Add(new WaferLines { waferId = -1, lines = 5 });
            deal.lines.Add(new WaferLines { waferId = 47, lines = -5 });
            var state = new AcquisitionsState();
            state.deals.Add(deal);

            var problems = new List<string>();
            Assert.Equal(0, state.Sanitize(problems));

            Assert.Equal(1, deal.lines.Count);
            Assert.Equal(46, deal.lines[0].waferId);
            Assert.Equal(3, problems.Count(p => p.Contains(".lines[")));
        }

        [Test]
        public void Sanitize_ResetsABadLastDividend_AndKeepsTheStake()
        {
            var state = new AcquisitionsState();
            state.stakes.Add(new StakeData { targetUniqueId = 10, targetSaveId = 388, share = 0.25, dividendsPaid = 5.0, lastDividend = double.NaN });
            state.stakes.Add(new StakeData { targetUniqueId = 11, targetSaveId = 389, share = 0.25, lastDividend = -3.0 });
            state.stakes.Add(new StakeData { targetUniqueId = 12, targetSaveId = 390, share = 0.25, lastDividend = 7.0 });
            var problems = new List<string>();

            Assert.Equal(0, state.Sanitize(problems), "no stake is dropped for it");
            Assert.Equal(3, state.stakes.Count);
            Assert.Equal(0.0, state.stakes[0].lastDividend);
            Assert.Equal(0.0, state.stakes[1].lastDividend);
            Assert.Equal(7.0, state.stakes[2].lastDividend, "a good value is kept");
            Assert.Equal(2, problems.Count(p => p.Contains("lastDividend")));
        }

        [Test]
        public void ASaveWithoutLastDividend_ReadsZero()
        {
            AcquisitionsState state = Bind("{ \"stakes\": [ { \"targetUniqueId\": 10, \"targetSaveId\": 388, \"share\": 0.25, \"invested\": 1.0, \"dividendsPaid\": 2.0, \"since\": \"1986-01-01\" } ] }");

            Assert.Equal(1, state.stakes.Count);
            Assert.Equal(0.0, state.stakes[0].lastDividend);
            Assert.Equal(0, state.Sanitize(new List<string>()));
        }

        [Test]
        public void Sanitize_DropsBadStakesAuctionsAndRescueOrders()
        {
            var state = new AcquisitionsState();
            state.stakes.Add(new StakeData { targetUniqueId = 10, targetSaveId = 388, share = 0.25 });
            state.stakes.Add(new StakeData { targetUniqueId = 10, targetSaveId = 388, share = 0.1 });    // second stake in the same company
            state.stakes.Add(new StakeData { targetUniqueId = 11, targetSaveId = 389, share = 0.5 });
            state.stakes.Add(new StakeData { targetUniqueId = 12, targetSaveId = 390, share = 0 });
            state.stakes.Add(new StakeData { targetUniqueId = -1, targetSaveId = 391, share = 0.2 });
            state.stakes.Add(new StakeData { targetUniqueId = 13, targetSaveId = 392, share = 0.2, invested = -5 });
            state.stakes.Add(new StakeData { targetUniqueId = 14, targetSaveId = 393, share = 0.2, dividendsPaid = double.NaN });
            state.stakes.Add(null);
            state.stakes.Add(new StakeData { targetUniqueId = 15, targetSaveId = 394, share = 0.3, since = "yesterday" });

            state.auctions.Add(new AuctionData { targetUniqueId = 7, targetSaveId = 355, bankruptOn = "1987-05-03", until = "1988-05-03" });
            state.auctions.Add(new AuctionData { targetUniqueId = 7, targetSaveId = 355, bankruptOn = "1987-05-03", until = "1988-05-03" });
            state.auctions.Add(new AuctionData { targetUniqueId = 8, targetSaveId = 356, bankruptOn = "", until = "1988-05-03" });
            state.auctions.Add(new AuctionData { targetUniqueId = 9, targetSaveId = 357, bankruptOn = "1987-05-03", until = "soon" });
            state.auctions.Add(new AuctionData { targetUniqueId = 2, targetSaveId = -1, bankruptOn = "1987-05-03", until = "1988-05-03" });
            state.auctions.Add(new AuctionData { targetUniqueId = 3, targetSaveId = 358, bankruptOn = "1987-05-03", until = "1988-05-03", snapshotCpuSaveIds = null });
            state.auctions.Add(null);

            state.rescueOrders.Add(new RescueOrderData { targetUniqueId = 10, targetSaveId = 388, maxAmount = 1e8, active = true });
            state.rescueOrders.Add(new RescueOrderData { targetUniqueId = 10, targetSaveId = 388, maxAmount = 5e7 });
            state.rescueOrders.Add(new RescueOrderData { targetUniqueId = 11, targetSaveId = 389, maxAmount = -1 });
            state.rescueOrders.Add(new RescueOrderData { targetUniqueId = 12, targetSaveId = 390, maxAmount = double.PositiveInfinity });
            state.rescueOrders.Add(new RescueOrderData { targetUniqueId = -3, targetSaveId = 390, maxAmount = 1 });
            state.rescueOrders.Add(null);

            var problems = new List<string>();
            int dropped = state.Sanitize(problems);

            Assert.Equal(7 + 5 + 5, dropped, string.Join(" | ", problems));
            Assert.Equal(new[] { 10, 15 }, state.stakes.Select(s => s.targetUniqueId).ToArray());
            Assert.Equal("", state.stakes[1].since, "an unreadable date of a stake is cleared, the stake stays");
            Assert.Equal(new[] { 7, 3 }, state.auctions.Select(a => a.targetUniqueId).ToArray());
            Assert.NotNull(state.auctions[1].snapshotCpuSaveIds);
            Assert.Equal(new[] { 10 }, state.rescueOrders.Select(r => r.targetUniqueId).ToArray());
            Assert.Near(1e8, state.rescueOrders[0].maxAmount);
        }

        [Test]
        public void Sanitize_RepairsTheCounters()
        {
            var state = new AcquisitionsState();
            state.deals.Add(Deal(5, DealType.Hostile, 3));
            state.deals.Add(Deal(9, DealType.Distressed, 4));
            state.nextDealId = 3;
            state.lastHostileOn = "never";
            state.defaultMailSentForSaveIds = new List<int> { 401, -2, 402 };

            var problems = new List<string>();
            Assert.Equal(0, state.Sanitize(problems));

            Assert.Equal(10, state.nextDealId);
            Assert.Equal("", state.lastHostileOn);
            Assert.Equal(new[] { 401, 402 }, state.defaultMailSentForSaveIds.ToArray());
            Assert.True(Mentions(problems, "nextDealId"));
            Assert.True(Mentions(problems, "lastHostileOn"));
            Assert.True(Mentions(problems, "defaultMailSentForSaveIds"));

            state.nextDealId = 0;
            state.deals.Clear();
            state.Sanitize(null);
            Assert.Equal(1, state.nextDealId, "never below 1");
        }

        [Test]
        public void Sanitize_OnAValidState_ChangesNothingAndReportsNothing()
        {
            AcquisitionsState state = Bind(DesignSection);
            JsonNode before = ModJson.From(state);
            var problems = new List<string>();

            Assert.Equal(0, state.Sanitize(problems));
            Assert.Equal(0, problems.Count);
            Assert.True(before.Equals(ModJson.From(state)), "Sanitize leaves a valid state as it was");
            Assert.Equal(0, state.Sanitize(null), "a null problem list is fine");
        }

        [Test]
        public void SectionWithWrongTypes_BindsWhatItCan_AndTheRestIsSane()
        {
            var errors = new List<string>();
            AcquisitionsState state = ModJson.Bind<AcquisitionsState>(Parse(
                "{ \"deals\": \"none\", \"stakes\": [ { \"targetUniqueId\": \"ten\", \"share\": 0.25 }, { \"targetUniqueId\": 4, \"targetSaveId\": 9, \"share\": 0.3 } ], \"nextDealId\": \"x\", \"backfillDone\": true }"), errors);

            Assert.True(errors.Count > 0, "the binder reports what it could not read");
            var problems = new List<string>();
            state.Sanitize(problems);

            Assert.NotNull(state.deals);
            Assert.True(state.backfillDone);
            Assert.Equal(1, state.nextDealId);
            Assert.True(state.stakes.Any(s => s.targetUniqueId == 4 && s.share == 0.3), "the readable stake is kept");
        }
    }
}
