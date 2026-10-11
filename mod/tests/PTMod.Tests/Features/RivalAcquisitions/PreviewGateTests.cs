using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The gate of the preview of a purchase (TSK-016; FR-7 item 5, FR-12 item 5, NFR-5): the button "Buy" waits for a plan without blocking errors (so for the tick of the
    /// consequences), the choice of the lines is refused where the lines are required, and a deal that runs cannot be started twice.
    /// </summary>
    [Covers(typeof(PreviewGate))]
    public class PreviewGateTests
    {
        private static AcquisitionPlan PlanWith(bool consequences, bool ack, bool keep = true, CompanySnapshot target = null)
        {
            CompanySnapshot t = target ?? RivalTestData.ExampleTarget();
            if (consequences) t.UniqueId = AcquisitionPlanBuilder.IntelId;
            PlayerSnapshot p = RivalTestData.Player();
            DealQuote q = RivalTestData.Quote(DealType.Hostile, t, p);
            return AcquisitionPlanBuilder.Build(q, t, p, new PlanOptions { KeepLines = keep, ConsequencesAcknowledged = ack });
        }

        [Test]
        public void ANewGate_KeepsTheLines_AndHasNoTick()
        {
            var gate = new PreviewGate();

            Assert.True(gate.KeepLines);
            Assert.False(gate.Acknowledged);
            Assert.False(gate.Executing);
            Assert.False(gate.Done);
            PlanOptions options = gate.Options();
            Assert.True(options.KeepLines);
            Assert.False(options.ConsequencesAcknowledged);
        }

        [Test]
        public void Buy_WithoutConsequences_IsOpenAtOnce()
        {
            var gate = new PreviewGate();

            Assert.True(gate.CanBuy(PlanWith(false, false), false));
        }

        [Test]
        public void AC121_Buy_WithConsequences_WaitsForTheTick()
        {
            var gate = new PreviewGate();

            Assert.False(gate.CanBuy(PlanWith(true, gate.Acknowledged), false), "the tick is missing");
            gate.SetAcknowledged(true);
            Assert.True(gate.Options().ConsequencesAcknowledged);
            Assert.True(gate.CanBuy(PlanWith(true, gate.Acknowledged), false), "after the tick");
            gate.SetAcknowledged(false);
            Assert.False(gate.CanBuy(PlanWith(true, gate.Acknowledged), false), "the tick taken back");
        }

        [Test]
        public void Buy_IsClosed_WhenTheExecutorIsBusy_OrThereIsNoPlan()
        {
            var gate = new PreviewGate();
            AcquisitionPlan plan = PlanWith(false, false);

            Assert.False(gate.CanBuy(plan, true), "another deal runs");
            Assert.False(gate.CanBuy(null, false));
            Assert.False(gate.TryBegin(plan, true));
            Assert.False(gate.Executing);
        }

        [Test]
        public void Buy_IsClosed_WhenThePlanIsBlocked()
        {
            var gate = new PreviewGate();
            PlayerSnapshot poor = RivalTestData.Player(money: 1000000.0);
            CompanySnapshot t = RivalTestData.ExampleTarget();
            AcquisitionPlan plan = AcquisitionPlanBuilder.Build(RivalTestData.Quote(DealType.Hostile, t, poor), t, poor, gate.Options());

            Assert.False(plan.CanExecute);
            Assert.False(gate.CanBuy(plan, false));
        }

        [Test]
        public void ADealCanStartOnlyOnce_AtATime()
        {
            var gate = new PreviewGate();
            AcquisitionPlan plan = PlanWith(false, false);

            Assert.True(gate.TryBegin(plan, false));
            Assert.True(gate.Executing);
            Assert.False(gate.TryBegin(plan, false), "a second press while the deal runs does nothing");
            Assert.False(gate.CanBuy(plan, false));
        }

        [Test]
        public void ACancelledDeal_CanBeTriedAgain_ADoneOneCannot()
        {
            var gate = new PreviewGate();
            AcquisitionPlan plan = PlanWith(false, false);

            Assert.True(gate.TryBegin(plan, false));
            gate.End(false);
            Assert.False(gate.Executing);
            Assert.False(gate.Done);
            Assert.True(gate.CanBuy(plan, false), "cancelled before any change: the player may try again");

            Assert.True(gate.TryBegin(plan, false));
            gate.End(true);
            Assert.True(gate.Done);
            Assert.False(gate.CanBuy(plan, false), "the deal is done");
            Assert.False(gate.TryBegin(plan, false));
        }

        [Test]
        public void SellingTheLines_IsRefused_WhereTheyAreRequired()
        {
            var gate = new PreviewGate();

            Assert.False(gate.SetKeepLines(false, true), "FR-7 item 5");
            Assert.True(gate.KeepLines);

            Assert.True(gate.SetKeepLines(false, false));
            Assert.False(gate.KeepLines);
            Assert.False(gate.Options().KeepLines);

            Assert.True(gate.SetKeepLines(true, true), "keeping is always allowed");
            Assert.True(gate.KeepLines);
        }

        [Test]
        public void ChangingTheChoiceOfTheLines_ClearsTheTick()
        {
            var gate = new PreviewGate();
            gate.SetAcknowledged(true);

            Assert.True(gate.SetKeepLines(true, false), "no change");
            Assert.True(gate.Acknowledged, "the same choice keeps the tick");

            Assert.True(gate.SetKeepLines(false, false));
            Assert.False(gate.Acknowledged, "the consequences may differ with the other choice");
        }

        [Test]
        public void WhileADealRuns_NothingCanBeChanged()
        {
            var gate = new PreviewGate();
            AcquisitionPlan plan = PlanWith(false, false);
            Assert.True(gate.TryBegin(plan, false));

            Assert.False(gate.SetKeepLines(false, false));
            Assert.True(gate.KeepLines);
            gate.SetAcknowledged(true);
            Assert.False(gate.Acknowledged);
            Assert.False(gate.CanChooseLines(true));
        }

        [Test]
        public void TheChoiceOfLines_NeedsLinesToChooseFrom()
        {
            var gate = new PreviewGate();

            Assert.True(gate.CanChooseLines(true));
            Assert.False(gate.CanChooseLines(false));
        }
    }
}
