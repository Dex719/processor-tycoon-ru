using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>TSK-016 (FR-10): the question before a deal between companies is signed, and the log entry written when it is. The example is AC-10.1: 5 years, 40 % of the lines, a mark-up of 110 %.</summary>
    [Covers(typeof(DealLines), typeof(DealInput))]
    public class DealLinesTests
    {
        private static DealInput Ac101()
        {
            return new DealInput
            {
                Provider = "Foundry Inc",
                Client = "My Company",
                Years = 5,
                CapacityPercent = 40,
                MarkupPercent = 110,
                Exclusive = false,
                ProviderFines = 250000,
                ClientFines = 0,
                AcceptanceScore = 12,
            };
        }

        [Test]
        public void TheQuestion_NamesTheCompaniesTheTermTheShareTheMarkupAndTheVerdict()
        {
            List<PanelLine> lines = DealLines.Build(Ac101());

            Assert.Equal(new[]
            {
                "ct.business.provider", "ct.business.client", "ct.business.term", "ct.business.share", "ct.business.markup",
                "ct.business.exclusive", "ct.business.providerFines", "ct.business.score",
            }, lines.Select(l => l.Key).ToArray());

            var plain = new PlainArgFormatter();
            Assert.Equal("Foundry Inc", lines[0].Format(plain)[0]);
            Assert.Equal("My Company", lines[1].Format(plain)[0]);
            Assert.Equal("5", lines[2].Format(plain)[0]);
            Assert.Equal("40", lines[3].Format(plain)[0]);
            Assert.Equal("110", lines[4].Format(plain)[0]);
            Assert.Equal("[ct.log.no]", lines[5].Format(plain)[0]);
            Assert.Equal(ArgKind.Money, lines[6].Args[0].Kind);
            Assert.Equal(LineTone.Good, lines[7].Tone, "they will accept");
        }

        [Test]
        public void AnExclusiveDeal_AddsTheFinesOfTheClient()
        {
            DealInput deal = Ac101();
            deal.Exclusive = true;
            deal.ClientFines = 5000000;

            List<PanelLine> lines = DealLines.Build(deal);

            Assert.True(lines.Any(l => l.Key == "ct.business.clientFines"));
            Assert.Equal("[ct.log.yes]", lines.First(l => l.Key == "ct.business.exclusive").Format(new PlainArgFormatter())[0]);
        }

        [Test]
        public void AVerdictOfZeroOrLess_IsRed()
        {
            DealInput deal = Ac101();
            deal.AcceptanceScore = 0;

            Assert.Equal(LineTone.Bad, DealLines.Build(deal).Last().Tone);
        }

        [Test]
        public void TheLogEntry_HoldsTheTermsAsNumbers()
        {
            DealInput deal = Ac101();
            deal.Exclusive = true;
            deal.ClientFines = 1.5e6;

            LogEntry entry = DealLines.LogEntryOf(deal, "1987-03-05");

            Assert.Equal(LogTypes.BusinessSigned, entry.type);
            Assert.Equal(LogSources.Manual, entry.source);
            Assert.Equal("1987-03-05", entry.date);
            Assert.Equal("ct.reason.businessSigned", entry.reasonKey);
            Assert.Equal("Foundry Inc > My Company", entry.contractLabel);
            Assert.Equal(5, entry.GetInt(LogDataKeys.Years, -1));
            Assert.Equal(40, entry.GetInt(LogDataKeys.Share, -1));
            Assert.Equal(110, entry.GetInt(LogDataKeys.Markup, -1));
            Assert.Equal(1, entry.GetInt(LogDataKeys.Exclusive, -1));
            Assert.Near(250000.0, entry.GetDouble(LogDataKeys.ProviderFines, -1), 1e-6);
            Assert.Near(1.5e6, entry.GetDouble(LogDataKeys.ClientFines, -1), 1e-6);
            Assert.Equal(12, entry.GetInt(LogDataKeys.Score, -1));
            Assert.Equal("Foundry Inc", entry.Get(LogDataKeys.Provider));
        }

        [Test]
        public void TheLogEntry_IsAcceptedByTheLog_WithoutAContractKey()
        {
            var model = new ContractLogModel();

            Assert.True(model.Add(DealLines.LogEntryOf(Ac101(), "1987-03-05")), "the model keeps it");
            Assert.Equal(1, model.Query(LogFilter.Business).Count);
            Assert.Equal(0, model.Query(LogFilter.Manual).Count);
        }

        [Test]
        public void TheNamesOfTheCompanies_AreCutToTheLimitOfTheLog()
        {
            DealInput deal = Ac101();
            deal.Provider = new string('p', 300);
            deal.Client = new string('c', 300);

            LogEntry entry = DealLines.LogEntryOf(deal, "1987-03-05");

            Assert.True(entry.contractLabel.Length <= TextSafe.MaxLength);
            Assert.True(entry.Get(LogDataKeys.Provider).Length <= TextSafe.MaxLength);
        }
    }
}
