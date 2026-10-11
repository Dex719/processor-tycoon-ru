using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-014 (FR-8): what the window of the log says about an entry. The entries are the ones the other parts of the feature write (the gate, the automation, the recorder of tenders), built here with the
    /// same helpers; each reason key must get the arguments its text takes, the details show every number with its kind, and the tender details show the whole result (FR-8 item 3).
    /// </summary>
    [Covers(typeof(LogLines), typeof(LogRowText))]
    public class LogLinesTests
    {
        private static readonly PlainArgFormatter Plain = new PlainArgFormatter();

        private static LogEntry Entry(string type, string reason, string source = "Automation")
        {
            LogEntry entry = LogEntry.Create("1986-12-31", type, source);
            entry.contractKey = "Apple|1987-1|52000|299|1";
            entry.contractLabel = "Apple - Computer, 52 000 units";
            entry.cpuId = 7;
            entry.cpuName = "Z80-A";
            entry.reasonKey = reason;
            return entry;
        }

        [Test]
        public void TheMarginReason_TakesTheMarginTheThresholdThePaymentAndTheCost()
        {
            LogEntry entry = Entry(LogTypes.AutomationDecision, "ct.reason.margin");
            entry.Put(LogDataKeys.Margin, 0.35).Put(LogDataKeys.Threshold, 0.4).Put(LogDataKeys.Payment, 299.0).Put(LogDataKeys.UnitCost, 194.35);

            PanelLine sentence = LogLines.Sentence(entry);

            Assert.Equal("ct.reason.margin", sentence.Key);
            Assert.Equal(new[] { "35", "40", "299", "194.35" }, sentence.Format(Plain));
            Assert.Equal(ArgKind.Money, sentence.Args[2].Kind);
            Assert.Equal(LineTone.Warn, sentence.Tone);
        }

        [Test]
        public void TheRequirementsReason_NamesTheFailedRequirements()
        {
            LogEntry entry = Entry(LogTypes.AutomationDecision, "ct.reason.requirements");
            entry.Put(LogDataKeys.Failed, "ips,package");

            PanelLine sentence = LogLines.Sentence(entry);

            Assert.Equal(new[] { "[ct.req.name.ips], [ct.req.name.package]" }, sentence.Format(Plain));
            Assert.Equal(LineTone.Bad, sentence.Tone);
        }

        [Test]
        public void TheSummary_TakesTheFourCounts()
        {
            LogEntry entry = Entry(LogTypes.AutomationSummary, "ct.reason.automationSummary");
            entry.Put(LogDataKeys.Sent, 3).Put(LogDataKeys.Skipped, 9).Put(LogDataKeys.SkippedMargin, 6).Put(LogDataKeys.SkippedRequirements, 2);

            Assert.Equal(new[] { "3", "9", "6", "2" }, LogLines.Sentence(entry).Format(Plain));
        }

        [Test]
        public void TheMismatchReason_NamesWhatTheModExpectedAndWhatTheGameDid()
        {
            LogEntry entry = Entry(LogTypes.Mismatch, "ct.reason.mismatch");
            entry.Put(LogDataKeys.Predicted, "SkippedMargin").Put(LogDataKeys.Observed, "sent");

            Assert.Equal(new[] { "[ct.log.decision.SkippedMargin]", "[ct.reason.sent]" }, LogLines.Sentence(entry).Format(Plain));
            Assert.Equal("[ct.reason.notSent]", LogLines.Sentence(WithObserved("notSent")).Format(Plain)[1]);
        }

        private static LogEntry WithObserved(string observed)
        {
            LogEntry entry = Entry(LogTypes.Mismatch, "ct.reason.mismatch");
            entry.Put(LogDataKeys.Predicted, "WillOffer").Put(LogDataKeys.Observed, observed);
            return entry;
        }

        [Test]
        public void TheAutoRejectReason_TakesWhatWasNeededTakenAndTheCapacity()
        {
            LogEntry entry = Entry(LogTypes.AutoRejected, "ct.reason.autoReject", LogSources.Game);
            entry.Put(LogDataKeys.Required, 3).Put(LogDataKeys.Contracted, 4).Put(LogDataKeys.Capacity, 5);

            PanelLine sentence = LogLines.Sentence(entry);

            Assert.Equal(new[] { "3", "4", "5" }, sentence.Format(Plain));
            Assert.Equal(LineTone.Bad, sentence.Tone);
        }

        [Test]
        public void AReasonWithoutNumbers_IsTheKeyAlone_AndAnEntryWithoutAReason_FallsBackToItsType()
        {
            LogEntry sent = Entry(LogTypes.OfferSent, "ct.reason.sentManual", LogSources.Manual);
            Assert.Equal("ct.reason.sentManual", LogLines.Sentence(sent).Key);
            Assert.Equal(0, LogLines.Sentence(sent).Args.Length);

            LogEntry bare = Entry(LogTypes.Mismatch, string.Empty);
            Assert.Equal("ct.log.type.Mismatch", LogLines.Sentence(bare).Key);
        }

        [Test]
        public void ATenderLine_ShowsTheOutcomeTheChancesAndTheReason()
        {
            LogEntry lost = Entry(LogTypes.TenderResult, "ct.tender.outbid", LogSources.Game);
            lost.Put(LogDataKeys.Outcome, TenderOutcomeClassifier.OutcomeLost).Put(LogDataKeys.Chance, 40).Put(LogDataKeys.Actual, 0.2111)
                .Put(LogDataKeys.Winner, "Intel").Put(LogDataKeys.WinnerCpu, "i8086");

            PanelLine line = LogLines.Sentence(lost);

            Assert.Equal("ct.log.row.tender.lost", line.Key);
            Assert.Equal(new[] { "40", "21", "Intel", "i8086", "[ct.tender.outbid]" }, line.Format(Plain));
            Assert.Equal(LineTone.Bad, line.Tone);

            LogEntry won = Entry(LogTypes.TenderResult, "ct.tender.won", LogSources.Game);
            won.Put(LogDataKeys.Outcome, TenderOutcomeClassifier.OutcomeWon);
            Assert.Equal("ct.log.row.tender.won", LogLines.Sentence(won).Key);
            Assert.Equal(LineTone.Good, LogLines.Sentence(won).Tone);

            LogEntry none = Entry(LogTypes.TenderResult, "ct.tender.noOffers", LogSources.Game);
            none.Put(LogDataKeys.Outcome, TenderOutcomeClassifier.OutcomeVoid);
            Assert.Equal("ct.log.row.tender.void", LogLines.Sentence(none).Key);
            Assert.Equal(LineTone.Muted, LogLines.Sentence(none).Tone);
        }

        [Test]
        public void ADealLine_NamesTheCompaniesTheYearsAndTheTerms()
        {
            DealInput deal = new DealInput { Provider = "Foundry", Client = "Me", Years = 5, CapacityPercent = 40, MarkupPercent = 110, ProviderFines = 1e6, AcceptanceScore = 3 };
            LogEntry entry = DealLines.LogEntryOf(deal, "1987-03-05");

            PanelLine sentence = LogLines.Sentence(entry);

            Assert.Equal("ct.reason.businessSigned", sentence.Key);
            Assert.Equal(new[] { "Foundry", "Me", "5", "40", "110" }, sentence.Format(Plain));
            Assert.Equal(LineTone.Good, sentence.Tone);
        }

        // ---- the colours ----

        [Test]
        public void TheTone_FollowsWhatHappened()
        {
            Assert.Equal(LineTone.Good, LogLines.ToneOf(Entry(LogTypes.OfferSent, "ct.reason.sentManual")));
            Assert.Equal(LineTone.Good, LogLines.ToneOf(Entry(LogTypes.OfferAutoConfirmed, "ct.reason.autoConfirmedSafe")));
            Assert.Equal(LineTone.Muted, LogLines.ToneOf(Entry(LogTypes.OfferCancelledOnReview, "ct.reason.cancelledOnReview")));
            Assert.Equal(LineTone.Good, LogLines.ToneOf(Entry(LogTypes.AutomationDecision, "ct.reason.sent")));
            Assert.Equal(LineTone.Warn, LogLines.ToneOf(Entry(LogTypes.AutomationDecision, "ct.reason.margin")));
            Assert.Equal(LineTone.Bad, LogLines.ToneOf(Entry(LogTypes.AutomationDecision, "ct.reason.requirements")));
            Assert.Equal(LineTone.Muted, LogLines.ToneOf(Entry(LogTypes.AutomationDecision, "ct.reason.alreadyOffered")));
            Assert.Equal(LineTone.Normal, LogLines.ToneOf(Entry(LogTypes.AutomationSummary, "ct.reason.automationSummary")));
            Assert.Equal(LineTone.Bad, LogLines.ToneOf(Entry(LogTypes.AutoRejected, "ct.reason.autoReject")));
            Assert.Equal(LineTone.Warn, LogLines.ToneOf(Entry(LogTypes.Mismatch, "ct.reason.mismatch")));
            Assert.Equal(LineTone.Good, LogLines.ToneOf(Entry(LogTypes.BusinessSigned, "ct.reason.businessSigned")));
            Assert.Equal(LineTone.Muted, LogLines.ToneOf(null));
        }

        [Test]
        public void TheRow_CarriesTheDateTheCpuTheContractAndTheSentence()
        {
            LogEntry entry = Entry(LogTypes.OfferSent, "ct.reason.sentManual", LogSources.Manual);

            LogRowText row = LogLines.Row(entry);

            Assert.Equal("1986-12-31", row.Date);
            Assert.Equal("Z80-A", row.Cpu);
            Assert.Equal("Apple - Computer, 52 000 units", row.Contract);
            Assert.Equal("ct.reason.sentManual", row.Sentence.Key);
            Assert.Equal(LineTone.Good, row.Tone);
        }

        [Test]
        public void ANullEntry_GivesAnEmptyRowAndNoDetails()
        {
            LogRowText row = LogLines.Row(null);

            Assert.Equal(string.Empty, row.Date);
            Assert.Equal(string.Empty, row.Sentence.Key);
            Assert.Equal(0, LogLines.Details(null).Count);
            Assert.Equal(0, LogLines.TenderDetails(null).Count);
        }

        // ---- the details ----

        [Test]
        public void Details_ListWhenWhatWhoAndEveryNumberWithItsKind()
        {
            LogEntry entry = Entry(LogTypes.AutomationDecision, "ct.reason.margin");
            entry.Put(LogDataKeys.Margin, 0.35).Put(LogDataKeys.Threshold, 0.4).Put(LogDataKeys.Payment, 299.0).Put(LogDataKeys.UnitCost, 194.35);

            List<PanelLine> details = LogLines.Details(entry);

            Assert.Equal(new[]
            {
                "ct.log.d.date", "ct.log.d.type", "ct.log.d.source", "ct.log.d.contract", "ct.log.d.cpu", "ct.reason.margin",
                "ct.log.d.data", "ct.log.d.data", "ct.log.d.data", "ct.log.d.data",
            }, details.Select(l => l.Key).ToArray());
            Assert.Equal("[ct.log.type.AutomationDecision]", details[1].Format(Plain)[0]);
            Assert.Equal("[ct.log.source.Automation]", details[2].Format(Plain)[0]);
            Assert.True(details[5].Bold, "the sentence stands out");

            Assert.Equal(new[] { "[ct.log.data.margin]", "35" }, details[6].Format(Plain));
            Assert.Equal(ArgKind.Fraction, details[6].Args[1].Kind);
            Assert.Equal(ArgKind.Money, details[8].Args[1].Kind);
            Assert.Equal(1, details[6].Indent);
        }

        [Test]
        public void Details_OfAnEntryWithoutContractOrCpu_SkipTheEmptyLines()
        {
            LogEntry entry = LogEntry.Create("1986-12-31", LogTypes.AutomationDecision, LogSources.Automation);
            entry.reasonKey = "ct.reason.automationOff";

            List<string> keys = LogLines.Details(entry).Select(l => l.Key).ToList();

            Assert.False(keys.Contains("ct.log.d.contract"));
            Assert.False(keys.Contains("ct.log.d.cpu"));
            Assert.True(keys.Contains("ct.reason.automationOff"));
        }

        [Test]
        public void ValueOf_PicksTheKindByTheNameOfTheNumber()
        {
            Assert.Equal(ArgKind.Count, LogLines.ValueOf(LogDataKeys.Units, "52000").Kind);
            Assert.Equal(ArgKind.Money, LogLines.ValueOf(LogDataKeys.Payment, "299").Kind);
            Assert.Equal(ArgKind.Actual, LogLines.ValueOf(LogDataKeys.Actual, "0.21").Kind);
            Assert.Equal(ArgKind.Whole, LogLines.ValueOf(LogDataKeys.Capacity, "5").Kind);
            Assert.Equal(ArgKind.Percent, LogLines.ValueOf(LogDataKeys.Share, "40").Kind);
            Assert.Equal(ArgKind.Text, LogLines.ValueOf(LogDataKeys.Winner, "Intel").Kind);
            Assert.Equal(ArgKind.Text, LogLines.ValueOf("something-new", "x").Kind, "a key this version does not know is shown as text");
            Assert.Equal("[ct.log.yes]", Plain.Format(LogLines.ValueOf(LogDataKeys.Own, "1")));
            Assert.Equal("[ct.log.no]", Plain.Format(LogLines.ValueOf(LogDataKeys.Exclusive, "0")));
            Assert.Equal("[ct.log.outcome.Won]", Plain.Format(LogLines.ValueOf(LogDataKeys.Outcome, "Won")));
        }

        [Test]
        public void ValueOf_ANumberThatIsNotANumber_StaysText()
        {
            PanelArg broken = LogLines.ValueOf(LogDataKeys.Payment, "lots");

            Assert.Equal(ArgKind.Text, broken.Kind);
            Assert.Equal("lots", broken.Text);
            Assert.Equal(ArgKind.Text, LogLines.ValueOf(LogDataKeys.Margin, "NaN").Kind);
        }

        [Test]
        public void TheRisksNumber_BecomesTheNamesOfTheRisks()
        {
            int flags = (int)(RiskFlags.CapacityShortNow | RiskFlags.NegativeProfit);

            PanelArg arg = LogLines.ValueOf(LogDataKeys.Risks, flags.ToString());

            Assert.Equal("[ct.risk.name.capacity], [ct.risk.name.negativeProfit]", Plain.Format(arg));
            Assert.Equal("[ct.log.none]", Plain.Format(LogLines.ValueOf(LogDataKeys.Risks, "0")));
        }

        [Test]
        public void FailedKeys_KeepOnlyTheThreeKnownRequirements()
        {
            Assert.Equal("ct.req.name.ips,ct.req.name.power,ct.req.name.package", LogLines.FailedKeys("ips, power ,package"));
            Assert.Equal("ct.req.name.ips", LogLines.FailedKeys("ips,speed"));
            Assert.Equal(string.Empty, LogLines.FailedKeys(null));
            Assert.Equal(string.Empty, LogLines.FailedKeys(string.Empty));
        }

        // ---- the tender ----

        private static TenderResultDto Tender()
        {
            var tender = new TenderResultDto
            {
                contractKey = "Apple|1987-1|52000|299|1",
                contractLabel = "Apple - Computer, 52 000 units",
                date = "1987-01-01",
                outcome = TenderOutcomeClassifier.OutcomeLost,
                reasonKey = "ct.tender.outbid",
                playerCpuId = 20,
                playerCpuName = "Z80-A",
                playerGameChance = 40,
                playerActual = 0.2105,
                winnerCpuId = 10,
                winnerCompany = "Intel",
                winnerCpuName = "i8086",
                capacityNotChecked = true,
            };
            tender.offers.Add(new OfferRowDto { company = "Intel", cpu = "i8086", ips = 60, chance = 60, actual = 0.789, player = false });
            tender.offers.Add(new OfferRowDto { company = "Me", cpu = "Z80-A", ips = 40, chance = 40, actual = 0.2105, player = true });
            tender.pruned.Add(new PrunedRowDto { company = "Me", cpu = "Z80-B", required = 3, contracted = 4, capacity = 5, maker = string.Empty, player = true });
            tender.pruned.Add(new PrunedRowDto { company = "AMD", cpu = "Am9080", required = 2, contracted = 6, capacity = 6, maker = "TSMC", player = false });
            return tender;
        }

        [Test]
        public void TenderDetails_ShowTheWholeResult()
        {
            List<PanelLine> lines = LogLines.TenderDetails(Tender());

            Assert.Equal(new[]
            {
                "ct.log.t.result", "ct.log.t.player", "ct.log.t.winner", "ct.log.t.capacityNotChecked",
                "ct.log.t.offers", "ct.log.t.offer", "ct.log.t.offer",
                "ct.log.t.pruned", "ct.log.t.prunedOwn", "ct.log.t.prunedMaker",
            }, lines.Select(l => l.Key).ToArray());

            Assert.Equal(new[] { "[ct.log.outcome.Lost]", "[ct.tender.outbid]" }, lines[0].Format(Plain));
            Assert.Equal(new[] { "Z80-A", "40", "21" }, lines[1].Format(Plain));
            Assert.Equal(new[] { "Intel", "i8086" }, lines[2].Format(Plain));
            Assert.Equal(LineTone.Warn, lines[3].Tone);

            Assert.Equal(LineTone.Good, lines[5].Tone, "the winner");
            Assert.False(lines[5].Bold);
            Assert.True(lines[6].Bold, "the player's own offer stands out");
            Assert.Equal(new[] { "Me", "Z80-A", "40", "40", "21" }, lines[6].Format(Plain));

            Assert.Equal(LineTone.Bad, lines[8].Tone, "the player's own withdrawn offer");
            Assert.Equal(new[] { "Me", "Z80-B", "3", "4", "5", string.Empty }, lines[8].Format(Plain));
            Assert.Equal("TSMC", lines[9].Format(Plain)[5], "a foundry that has no free lines is named");
        }

        [Test]
        public void TenderDetails_OfAVoidTender_HaveNoPlayerNoWinnerNoTable()
        {
            var tender = new TenderResultDto { outcome = TenderOutcomeClassifier.OutcomeVoid, reasonKey = "ct.tender.noOffers", date = "1987-01-01" };

            List<PanelLine> lines = LogLines.TenderDetails(tender);

            Assert.Equal(new[] { "ct.log.t.result" }, lines.Select(l => l.Key).ToArray());
            Assert.Equal(LineTone.Muted, lines[0].Tone);
        }

        // ---- the names ----

        [Test]
        public void EveryNameKeyTheLinesCanAskFor_IsAKeyOfTheFeature()
        {
            List<string> keys = LogLines.AllNameKeys().ToList();

            Assert.True(keys.Count >= 60, "too few keys: " + keys.Count);
            Assert.Equal(keys.Count, keys.Distinct().Count(), "a key listed twice");
            foreach (string key in keys) Assert.True(key.StartsWith("ct."), key);
            Assert.True(keys.Contains("ct.log.type.OfferCancelledOnReview"));
            Assert.True(keys.Contains("ct.log.filter.tenders"));
            Assert.True(keys.Contains("ct.log.data.providerFines"));
        }

        [Test]
        public void TheFilters_HaveSixNamesInTheOrderOfTheEnum()
        {
            Assert.Equal("ct.log.filter.all", LogLines.FilterKey(LogFilter.All));
            Assert.Equal("ct.log.filter.manual", LogLines.FilterKey(LogFilter.Manual));
            Assert.Equal("ct.log.filter.automation", LogLines.FilterKey(LogFilter.Automation));
            Assert.Equal("ct.log.filter.autoReject", LogLines.FilterKey(LogFilter.AutoReject));
            Assert.Equal("ct.log.filter.tenders", LogLines.FilterKey(LogFilter.Tenders));
            Assert.Equal("ct.log.filter.business", LogLines.FilterKey(LogFilter.Business));
            Assert.Equal(6, System.Enum.GetValues(typeof(LogFilter)).Length);
        }
    }
}
