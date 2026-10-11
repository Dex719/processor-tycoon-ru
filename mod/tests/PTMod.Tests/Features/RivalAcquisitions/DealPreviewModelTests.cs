using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The model of the preview of a purchase (TSK-016; FR-3, FR-4, FR-5, FR-7, FR-8, FR-9, FR-12, NFR-5): the blocks "Valuation" (with the cash-floor note), "Money",
    /// "What passes", "What is retired or terminated", "Warnings" and "Consequences", the reasons that block the deal, and the question of the confirmation.
    /// </summary>
    [Covers(typeof(DealPreviewModel), typeof(PreviewLine), typeof(PreviewSection))]
    public class DealPreviewModelTests
    {
        private const double Million = 1000000.0;

        private static DealPreviewModel Build(CompanySnapshot t, PlayerSnapshot p = null, DealType type = DealType.Hostile, bool keep = true, bool ack = true,
            AcquisitionsState s = null)
        {
            p = p ?? RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(type, t, p, s);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions { KeepLines = keep, ConsequencesAcknowledged = ack });
            return DealPreviewModel.Build(q, plan, t, p, UiTestData.Format());
        }

        // ---- the example ----

        [Test]
        public void TheExampleDeal_HasTheThreeBlocksAndNoWarnings()
        {
            DealPreviewModel m = Build(RivalTestData.ExampleTarget());

            Assert.True(m.Available);
            Assert.True(m.CanBuy);
            Assert.False(m.NeedsAck);
            Assert.Equal(0, m.Problems.Count);
            Assert.Equal(new[] { UiKeys.SectionValuation, UiKeys.SectionMoney, UiKeys.SectionTransfers }, m.Sections.Select(s => s.TitleKey).ToArray());
            Assert.Equal("Texas Instruments", m.TargetName);
            Assert.Equal(UiKeys.PreviewTitleHostile + "[Texas Instruments]", m.Title);
        }

        [Test]
        public void TheValuationBlock_ShowsTheComponentsTheValueTheMultiplierAndThePrice()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p);

            PreviewSection s = UiTestData.Section(Build(t, p), UiKeys.SectionValuation);

            PreviewLine parts = UiTestData.StartingWith(s, UiKeys.PreviewValuation);
            Assert.NotNull(parts);
            Assert.True(parts.Text.Contains("$" + System.Math.Round(q.Valuation.AFab)), "the fab");
            Assert.True(parts.Text.Contains("$" + System.Math.Round(q.Valuation.ATech)), "the technologies");
            Assert.True(parts.Text.Contains("$" + System.Math.Round(q.Valuation.ACpu)), "the CPUs");
            Assert.True(parts.HasTip, "the technologies and CPUs are listed in the tooltip");
            Assert.True(parts.TipBody.Contains("T100"), "a technology by name");
            Assert.True(parts.TipBody.Contains("C1201"), "a CPU by name");

            Assert.Equal("$" + System.Math.Round(q.Valuation.E), UiTestData.Pair(s, UiKeys.LabelValuation).Text);
            Assert.Equal("1.5", UiTestData.Pair(s, UiKeys.LabelMultiplier).Text);
            Assert.Equal("$" + System.Math.Round(q.Price), UiTestData.Pair(s, UiKeys.LabelPrice).Text);
            Assert.Null(UiTestData.Pair(s, UiKeys.LabelChannel), "a channel of 1 is not shown");
            Assert.Null(UiTestData.Pair(s, UiKeys.LabelOwnedShare), "no stake is not shown");
        }

        [Test]
        public void TheChannelAndTheOwnedShare_AreShownWhenTheyMatter()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            var st = new AcquisitionsState();
            st.stakes.Add(RivalTestData.Stake(t, 0.25));
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p, st, 0.8);
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions());

            PreviewSection s = UiTestData.Section(DealPreviewModel.Build(q, plan, t, p, UiTestData.Format()), UiKeys.SectionValuation);

            Assert.Equal("0.8", UiTestData.Pair(s, UiKeys.LabelChannel).Text);
            Assert.Equal("25%", UiTestData.Pair(s, UiKeys.LabelOwnedShare).Text);
        }

        [Test]
        public void WhenTheCashFloorSetsThePrice_TheBlockSaysSo()
        {
            // The cash of the target is large against its value: the price is not below the cash plus the minimum fee.
            CompanySnapshot t = RivalTestData.Plain(100.0 * Million, 900.0 * Million, RivalTestData.DebtLimit, CompanyStatus.InDefault);
            t.UniqueId = 5;
            t.SaveId = 500;
            t.Name = "Rich";
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Distressed, t, p);
            Assert.True(q.CashFloorApplied, "precondition");

            PreviewSection s = UiTestData.Section(Build(t, p, DealType.Distressed), UiKeys.SectionValuation);

            PreviewLine note = UiTestData.StartingWith(s, UiKeys.PreviewCashFloor);
            Assert.NotNull(note);
            Assert.Equal(ViewTone.Warning, note.Tone);
            Assert.True(note.Text.Contains("$" + System.Math.Round(q.CashFloor)));

            Assert.Null(UiTestData.StartingWith(UiTestData.Section(Build(RivalTestData.ExampleTarget()), UiKeys.SectionValuation), UiKeys.PreviewCashFloor), "not when the formula decides");
        }

        // ---- money ----

        [Test]
        public void TheMoneyBlock_ShowsThePriceTheCashTheNetAndTheBalanceAfter()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p);

            DealPreviewModel m = Build(t, p);
            PreviewSection s = UiTestData.Section(m, UiKeys.SectionMoney);

            Assert.Equal("$" + System.Math.Round(q.Price), UiTestData.Pair(s, UiKeys.LabelPrice).Text);
            PreviewLine cash = UiTestData.Pair(s, UiKeys.LabelCash);
            Assert.Equal("$" + System.Math.Round(q.AssumedCash), cash.Text, "the 30 million that come with the company");
            Assert.Equal(ViewTone.Positive, cash.Tone);
            PreviewLine net = UiTestData.Pair(s, UiKeys.LabelNet);
            Assert.Equal(ViewTone.Negative, net.Tone, "the deal always costs");
            Assert.True(q.NetCash < 0.0);
            Assert.Equal("$" + System.Math.Round(q.MoneyAfter), UiTestData.Pair(s, UiKeys.LabelBalanceAfter).Text);
            Assert.Near(q.MoneyAfter, m.MoneyAfter, 1e-6);
            Assert.Near(q.Price, m.Price, 1e-6);
        }

        [Test]
        public void ADebtOfTheTarget_IsShownAsADebtYouTakeOver()
        {
            CompanySnapshot t = UiTestData.Company(2, "Indebted", CompanyStatus.InDefault, cash: -60 * Million);

            PreviewSection s = UiTestData.Section(Build(t, null, DealType.Distressed), UiKeys.SectionMoney);

            Assert.Null(UiTestData.Pair(s, UiKeys.LabelCash));
            PreviewLine debt = UiTestData.Pair(s, UiKeys.LabelDebt);
            Assert.NotNull(debt);
            Assert.Equal("$60000000", debt.Text, "the size of the debt, not a minus");
            Assert.Equal(ViewTone.Negative, debt.Tone);
        }

        [Test]
        public void TheAssetsOfABankrupt_LeaveItsCashAndDebtsWithIt()
        {
            CompanySnapshot t = UiTestData.Company(3, "Gone", CompanyStatus.BankruptAuction);
            var st = new AcquisitionsState();
            st.auctions.Add(RivalTestData.Auction(t, "1986-02-03"));

            DealPreviewModel m = Build(t, null, DealType.BankruptAssets, s: st);
            PreviewSection s = UiTestData.Section(m, UiKeys.SectionMoney);

            Assert.True(m.CanBuy);
            Assert.NotNull(UiTestData.StartingWith(s, UiKeys.PreviewCashStays));
            Assert.Null(UiTestData.Pair(s, UiKeys.LabelCash));
            Assert.Null(UiTestData.Pair(s, UiKeys.LabelDebt));
            Assert.Equal(UiKeys.PreviewTitleBankrupt + "[Gone]", m.Title);
        }

        [Test]
        public void TheReputation_IsShownWithItsSign()
        {
            PreviewSection hostile = UiTestData.Section(Build(RivalTestData.ExampleTarget()), UiKeys.SectionMoney);
            PreviewLine rep = UiTestData.Pair(hostile, UiKeys.LabelReputation);
            Assert.Equal("-5", rep.Text, "a hostile takeover costs 5 points");
            Assert.Equal(ViewTone.Negative, rep.Tone);

            CompanySnapshot t = UiTestData.Company(2, "Indebted", CompanyStatus.InDefault, cash: -60 * Million);
            Assert.Null(UiTestData.Pair(UiTestData.Section(Build(t, null, DealType.Distressed), UiKeys.SectionMoney), UiKeys.LabelReputation), "no change, no line");
        }

        // ---- lines ----

        [Test]
        public void KeptLines_AreListedWithTheirWaferSizes_AndTheUpkeepRise()
        {
            DealPreviewModel m = Build(RivalTestData.ExampleTarget());
            PreviewSection s = UiTestData.Section(m, UiKeys.SectionTransfers);

            PreviewLine lines = UiTestData.StartingWith(s, UiKeys.PreviewLines);
            Assert.NotNull(lines);
            Assert.True(lines.Text.Contains(UiKeys.PreviewLineItem + "[40|100]"), lines.Text);
            Assert.NotNull(UiTestData.StartingWith(s, UiKeys.PreviewOpex), "the rise of the upkeep");
            Assert.True(m.HasLines);
            Assert.True(m.KeepLines);
            Assert.False(m.KeepLinesRequired);
            Assert.Equal(0.0, m.SaleIncome);
            Assert.Near(147.2 * Million, m.LinesSaleValue, 1.0, "the number on the button 'Sell the lines'");
        }

        [Test]
        public void SoldLines_BringTheirSaleCost_IntoTheBalanceAfter()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p);

            DealPreviewModel m = Build(t, p, keep: false);
            PreviewSection money = UiTestData.Section(m, UiKeys.SectionMoney);
            PreviewSection transfers = UiTestData.Section(m, UiKeys.SectionTransfers);

            Assert.Near(147.2 * Million, m.SaleIncome, 1.0);
            Assert.Near(q.MoneyAfter + 147.2 * Million, m.MoneyAfter, 1.0, "the money of the sale is in the balance after");
            Assert.NotNull(UiTestData.Pair(money, UiKeys.LabelSaleIncome));
            Assert.NotNull(UiTestData.StartingWith(transfers, UiKeys.PreviewLinesSold));
            Assert.Null(UiTestData.StartingWith(transfers, UiKeys.PreviewLines + "["), "nothing is kept");
            Assert.Null(UiTestData.StartingWith(transfers, UiKeys.PreviewOpex));
        }

        [Test]
        public void LinesThatMoveToASmallerWaferSize_AreListedAsMoved()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Lines.Clear();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 12, 1.0, 50.0 * Million));   // the player knows 75 and 100 mm

            PreviewSection s = UiTestData.Section(Build(t), UiKeys.SectionTransfers);

            PreviewLine lines = UiTestData.StartingWith(s, UiKeys.PreviewLines);
            Assert.NotNull(lines);
            Assert.True(lines.Text.Contains(UiKeys.PreviewLineItemMoved + "[12|150|100]"), lines.Text);
        }

        [Test]
        public void TheLinesThatMustBeKept_LockTheChoice_AndSellingIsAProblem()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.PlayerCpuIdsMadeByTarget.Add(5);   // the target makes a CPU of the player

            DealPreviewModel keep = Build(t);
            DealPreviewModel sell = Build(t, keep: false);

            Assert.True(keep.KeepLinesRequired);
            Assert.True(keep.CanBuy);
            Assert.False(sell.CanBuy);
            Assert.True(UiTestData.Contains(sell.Problems, AcqKeys.PlanMustKeepLines), "FR-7 item 5");
        }

        [Test]
        public void ACompanyWithoutLines_HasNoLinesChoice()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Lines.Clear();

            Assert.False(Build(t).HasLines);
        }

        // ---- technologies, CPUs, contracts ----

        [Test]
        public void TheTransfersBlock_CountsTechnologiesCpusAndContracts()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.PartialTech.Add(new TechProgressSnapshot { Id = 7, Name = "Partial", TargetProgress = 0.6, PlayerProgress = 0.2 });
            t.Nodes.Add(new NodeSnapshot { NodeId = 3, Name = "N3", TargetExperience = 0.8, PlayerExperience = 0.2 });
            t.Cpus[0].OemContracts = 2;
            t.ProviderContracts.Add(RivalTestData.Contract(0, t.SaveId, 900, providerName: "Texas", clientName: "ClientCo"));
            t.HadFoundryServices = true;

            PreviewSection s = UiTestData.Section(Build(t), UiKeys.SectionTransfers);

            PreviewLine techs = UiTestData.StartingWith(s, UiKeys.PreviewTechs);
            Assert.Equal(UiKeys.PreviewTechs + "[3]", techs.Text);
            Assert.True(techs.TipBody.Contains("T100"));
            Assert.Equal(UiKeys.PreviewTechsPartial + "[1]", UiTestData.StartingWith(s, UiKeys.PreviewTechsPartial).Text);
            Assert.Equal(UiKeys.PreviewNodes + "[1]", UiTestData.StartingWith(s, UiKeys.PreviewNodes).Text);
            Assert.Equal(UiKeys.PreviewCpus + "[2]", UiTestData.StartingWith(s, UiKeys.PreviewCpus + "[").Text);
            Assert.Equal(UiKeys.PreviewOem + "[2]", UiTestData.StartingWith(s, UiKeys.PreviewOem).Text);
            PreviewLine providers = UiTestData.StartingWith(s, UiKeys.PreviewProviderContracts);
            Assert.Equal(UiKeys.PreviewProviderContracts + "[1]", providers.Text);
            Assert.True(providers.TipBody.Contains("ClientCo"), "the clients in the tooltip");
            Assert.NotNull(UiTestData.StartingWith(s, UiKeys.PreviewFoundryFlag), "the foundry-services flag");
        }

        [Test]
        public void ACientContractOfTheTarget_PassesToYou_AndIsNamed()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Clear();
            CpuSnapshot made = RivalTestData.Cpu(1301);
            made.MadeInHouseByTarget = false;
            made.IsOutsourced = true;
            made.OutsourcedId = 777;
            made.MakerName = "BigFoundry";
            t.Cpus.Add(made);
            t.ClientContracts.Add(RivalTestData.Contract(0, 777, t.SaveId, exclusive: false, providerName: "BigFoundry", clientName: "Texas Instruments"));

            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(RivalTestData.Quote(DealType.Hostile, t, RivalTestData.Player()), t, RivalTestData.Player(), new PlanOptions());
            Assert.Equal(1, plan.RepointClientContractIds.Count, "precondition");

            PreviewSection s = UiTestData.Section(Build(t), UiKeys.SectionTransfers);

            Assert.NotNull(UiTestData.StartingWith(s, UiKeys.PreviewClientContract + "[Texas Instruments|BigFoundry]"), "FR-9: the client contract is listed");
        }

        [Test]
        public void ANothingToTransfer_SaysSo()
        {
            CompanySnapshot t = UiTestData.Company(1, "Empty", CompanyStatus.Healthy);

            PreviewSection s = UiTestData.Section(Build(t), UiKeys.SectionTransfers);

            Assert.Equal(1, s.Lines.Count);
            Assert.Equal(UiKeys.PreviewNothing, s.Lines[0].Text);
        }

        [Test]
        public void TheRemovedBlock_ListsTheRetiredCpusAndTheContractsThatEnd()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Clear();
            CpuSnapshot inHouse = RivalTestData.Cpu(1201);
            t.Cpus.Add(inHouse);
            t.ProviderContracts.Add(RivalTestData.Contract(0, t.SaveId, 900, providerName: "Texas", clientName: "ClientCo"));
            RivalTestData.Player();

            DealPreviewModel sell = Build(t, keep: false);
            PreviewSection removed = UiTestData.Section(sell, UiKeys.SectionRemoved);

            Assert.NotNull(removed, "selling the lines retires the CPUs made in the target's factory and ends its supply contracts");
            Assert.NotNull(UiTestData.StartingWith(removed, UiKeys.PreviewRetireCpus));
            Assert.True(UiTestData.StartingWith(removed, UiKeys.PreviewRetireCpus).TipBody.Contains("C1201"));
            Assert.NotNull(UiTestData.StartingWith(removed, UiKeys.PreviewProviderDropped));

            Assert.Null(UiTestData.Section(Build(t), UiKeys.SectionRemoved), "nothing is retired when the lines are kept");
        }

        [Test]
        public void AnExclusivityConflict_ShowsTheContractThatWillBeTerminated()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Cpus.Clear();
            CpuSnapshot made = RivalTestData.Cpu(1301);
            made.MadeInHouseByTarget = false;
            made.IsOutsourced = true;
            made.OutsourcedId = 777;
            t.Cpus.Add(made);
            t.ClientContracts.Add(RivalTestData.Contract(0, 777, t.SaveId, exclusive: true, providerName: "BigFoundry", clientName: "Texas Instruments"));
            PlayerSnapshot p = RivalTestData.Player();
            p.HasExclusiveClientContract = true;

            PreviewSection removed = UiTestData.Section(Build(t, p), UiKeys.SectionRemoved);

            Assert.NotNull(removed);
            Assert.NotNull(UiTestData.StartingWith(removed, UiKeys.PreviewContractDropped + "[BigFoundry - Texas Instruments]"), "FR-9 item 4");
        }

        [Test]
        public void TheTooltipLists_AreCapped()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.TechGap.Clear();
            for (int i = 0; i < 40; i++) t.TechGap.Add(RivalTestData.Tech(100 + i));

            PreviewSection s = UiTestData.Section(Build(t), UiKeys.SectionTransfers);
            PreviewLine techs = UiTestData.StartingWith(s, UiKeys.PreviewTechs);

            Assert.True(techs.TipBody.Contains(UiKeys.TipMore + "[" + (40 - DealPreviewModel.TipNames) + "]"), techs.TipBody);
            Assert.False(techs.TipBody.Contains("T139"), "the names past the cap are only counted");
        }

        // ---- consequences ----

        [Test]
        public void IntelsConsequences_NeedTheTick_BeforeYouCanBuy()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.UniqueId = AcquisitionPlanBuilder.IntelId;
            t.Name = "Intel";

            DealPreviewModel unticked = Build(t, ack: false);
            DealPreviewModel ticked = Build(t, ack: true);

            PreviewSection s = UiTestData.Section(unticked, UiKeys.SectionConsequences);
            Assert.NotNull(s, "AC-12.1");
            Assert.NotNull(UiTestData.StartingWith(s, AcqKeys.ConsequenceX86));
            Assert.Equal(ViewTone.Warning, UiTestData.StartingWith(s, AcqKeys.ConsequenceX86).Tone);
            Assert.True(unticked.NeedsAck);
            Assert.False(unticked.CanBuy, "the button is off until the tick");
            Assert.Equal(0, unticked.Problems.Count, "the missing tick is the toggle, not a red line");
            Assert.True(ticked.CanBuy);
        }

        [Test]
        public void TheInformationOnlyConsequences_NeedNoTick()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.UniqueId = AcquisitionPlanBuilder.GlobalFoundriesId;
            t.Name = "GlobalFoundries";
            t.ClientContracts.Clear();
            t.ProviderContracts.Add(RivalTestData.Contract(0, t.SaveId, 601, exclusive: true, providerName: "GlobalFoundries", clientName: "AMD"));

            DealPreviewModel m = Build(t, ack: false);

            PreviewSection s = UiTestData.Section(m, UiKeys.SectionConsequences);
            PreviewLine contract = UiTestData.StartingWith(s, AcqKeys.ConsequenceGfAmdContract);
            Assert.NotNull(contract, "FR-12 item 4");
            Assert.Equal(ViewTone.Neutral, contract.Tone);
            Assert.False(m.NeedsAck);
            Assert.True(m.CanBuy);
        }

        [Test]
        public void BecomingAnIdm_IsAConsequenceWithAnArgumentFreeText()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.IsFabless = true;
            p.Wafers.Clear();
            p.Wafers.Add(RivalTestData.Wafer(RivalTestData.WaferOld, 100, 0));

            DealPreviewModel m = Build(RivalTestData.ExampleTarget(), p);

            PreviewSection s = UiTestData.Section(m, UiKeys.SectionConsequences);
            Assert.NotNull(s);
            Assert.NotNull(UiTestData.StartingWith(s, AcqKeys.ConsequenceBecomesIdm));
        }

        [Test]
        public void TheWarnings_GetABlockOfTheirOwn()
        {
            PlayerSnapshot p = RivalTestData.Player();
            p.IsCpuCompany = false;   // the CPUs of the target will be retired instead of passing over

            DealPreviewModel m = Build(RivalTestData.ExampleTarget(), p);

            PreviewSection s = UiTestData.Section(m, UiKeys.SectionWarnings);
            Assert.NotNull(s);
            Assert.NotNull(UiTestData.StartingWith(s, AcqKeys.WarnNoCpuDivision));
            Assert.Equal(ViewTone.Warning, s.Lines[0].Tone);
        }

        // ---- problems and the question ----

        [Test]
        public void ADealThatIsClosed_ShowsItsReasonInRed_WithMoneyFormatted()
        {
            PlayerSnapshot p = RivalTestData.Player(money: 100.0 * Million);   // the example costs about 700 million

            DealPreviewModel m = Build(RivalTestData.ExampleTarget(), p);

            Assert.False(m.Available);
            Assert.False(m.CanBuy);
            Assert.Equal(1, m.Problems.Count);
            Assert.Equal(ViewTone.Negative, m.Problems[0].Tone);
            Assert.True(m.Problems[0].Text.StartsWith(AcqKeys.ReasonNoCash + "[$"), "the shortfall as money: " + m.Problems[0].Text);
        }

        [Test]
        public void ADateInAReason_IsPassedAsItIs()
        {
            var s = new AcquisitionsState { lastHostileOn = "1984-12-01" };

            DealPreviewModel m = Build(RivalTestData.ExampleTarget(), null, DealType.Hostile, s: s);

            Assert.Equal(AcqKeys.ReasonCooldown + "[01.12.1985]", m.Problems[0].Text);
        }

        [Test]
        public void TheQuestionOfTheConfirmation_NamesTheCompanyThePriceAndTheBalance()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p);

            DealPreviewModel m = Build(t, p);

            Assert.Equal(UiKeys.ConfirmTitle, m.ConfirmTitle);
            Assert.Equal(UiKeys.ConfirmHostile + "[Texas Instruments|$" + System.Math.Round(q.Price) + "|$" + System.Math.Round(q.MoneyAfter) + "]", m.ConfirmMessage);
            Assert.Equal(UiKeys.ConfirmHostile, DealPreviewModel.ConfirmKey(DealType.Hostile));
            Assert.Equal(UiKeys.ConfirmDistressed, DealPreviewModel.ConfirmKey(DealType.Distressed));
            Assert.Equal(UiKeys.ConfirmBankrupt, DealPreviewModel.ConfirmKey(DealType.BankruptAssets));
            Assert.Equal(UiKeys.PreviewTitleDistressed, DealPreviewModel.TitleKey(DealType.Distressed));
            Assert.Equal(UiKeys.PreviewTitleBankrupt, DealPreviewModel.TitleKey(DealType.BankruptAssets));
        }

        [Test]
        public void ABrokenInput_StillBuildsAModel()
        {
            DealPreviewModel m = DealPreviewModel.Build(new DealQuote { Type = DealType.Hostile }, null, null, null, null);

            Assert.NotNull(m);
            Assert.NotNull(m.Sections);
            Assert.True(m.CanBuy, "an empty plan has no error; the quote's own reason is in the plan when there is one");
        }

        [Test]
        public void NameList_JoinsAndCaps()
        {
            ViewFormat f = UiTestData.Format();

            Assert.Equal("", DealPreviewModel.NameList(null, f));
            Assert.Equal("", DealPreviewModel.NameList(new List<string>(), f));
            Assert.Equal("a, b", DealPreviewModel.NameList(new List<string> { "a", "b" }, f));
            var many = new List<string>();
            for (int i = 0; i < DealPreviewModel.TipNames + 3; i++) many.Add("n" + i);
            Assert.True(DealPreviewModel.NameList(many, f).EndsWith(UiKeys.TipMore + "[3]"));
        }

        [Test]
        public void SaleValueOf_SumsTheSaleCostOfTheLines()
        {
            CompanySnapshot t = RivalTestData.ExampleTarget();
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferNew, 150, 5, 1.0, 10.0 * Million));
            t.Lines.Add(RivalTestData.Wafer(RivalTestData.WaferSmall, 75, 0, 1.0, 99.0 * Million));   // no lines: not counted

            Assert.Near(157.2 * Million, DealPreviewModel.SaleValueOf(t), 1.0);
            Assert.Equal(0.0, DealPreviewModel.SaleValueOf(null));
        }
    }
}
