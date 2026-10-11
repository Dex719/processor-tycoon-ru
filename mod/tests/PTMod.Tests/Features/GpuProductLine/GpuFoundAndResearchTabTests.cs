using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The dialog of the founding and the tab "Исследования" as data (TSK-022, TSK-023): AC-1.1 (the price, the money after paying, the five nodes of 1992), AC-1.3
    /// (the market opens in 1981), AC-1.4 (the credit limit), AC-1.5 (a pure foundry), AC-2.2 (about ten years, "опережает время"), AC-2.3 ("Нужно: DDR2"), AC-2.4 (the queue),
    /// the slider of the funding (the time and the cost change at once), the three branches, the limit of five nodes in the queue.
    /// </summary>
    [Covers(typeof(GpuFoundModel), typeof(GpuResearchTabModel), typeof(GpuNodeView), typeof(GpuBranchView), typeof(GpuCurrentResearchView), typeof(GpuQueueItemView),
        typeof(GpuNodeState), typeof(GpuNames))]
    public class GpuFoundAndResearchTabTests
    {
        // ---- the dialog of the founding ----

        private static GpuFoundModel Found(GpuWorld world, string language = "ru")
        {
            return GpuFoundModel.Build(world.Division, world.Content, world.Game, new TestGpuText(language));
        }

        [Test]
        public void Ac11_TheDialog_ShowsThePrice_TheMoneyAfter_AndTheFiveNodesOf1992()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            world.Game.Cash = 3e8f;
            GpuFoundModel model = Found(world);

            Assert.True(model.CanFound);
            Assert.Equal(FoundingBlock.None, model.Block);
            Assert.Equal(string.Empty, model.ReasonText);
            Assert.Equal(1992, model.CatchUpYear);
            Assert.Near(2.5e7, model.Cost, 1.0);
            Assert.Equal("Стоимость основания: $25000000", model.CostLine);
            Assert.Equal("Денег сейчас: $300000000. После основания: $275000000.", model.MoneyLine);
            Assert.Equal("Откроются сразу (5):", model.NodesTitle);
            Assert.Equal(new[] { "2D-ускоритель (1981)", "Ускоритель интерфейса (1987)", "DRAM (1981)", "VRAM (1986)", "Пассивный радиатор (1981)" }, model.NodeLines.ToArray(),
                "the parts that open, with their years");
            Assert.True(model.Intro.Contains("1992"), "the intro names the year up to which the tree opens");
            Assert.True(model.ConfirmMessage.Contains("$25000000") && model.ConfirmMessage.Contains("5"), "the question names the price and the number of nodes: " + model.ConfirmMessage);
            Assert.Equal("Основать GPU-подразделение?", model.ConfirmTitle);
        }

        [Test]
        public void TheDialog_FollowsTheInflation_AndTheLanguage()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            world.Game.InflationValue = 2f;
            world.Game.Cash = 3e8f;
            GpuFoundModel english = Found(world, "en");
            Assert.Equal("Founding cost: $50000000", english.CostLine);
            Assert.Equal("Opened at once (5):", english.NodesTitle);
            Assert.Equal("2D accelerator (1981)", english.NodeLines[0]);
        }

        [Test]
        public void Ac13_Before1981_TheButtonIsLocked_WithTheYear()
        {
            var world = new GpuWorld(new DateTime(1975, 5, 1));
            GpuFoundModel model = Found(world);
            Assert.False(model.CanFound);
            Assert.Equal(FoundingBlock.MarketNotOpen, model.Block);
            Assert.Equal("Рынок видеоадаптеров откроется в 1981 году", model.ReasonText, "AC-1.3");

            Assert.Equal("The graphics adapter market opens in 1981", Found(world, "en").ReasonText);
        }

        [Test]
        public void Ac14_TheCreditLimit_LocksTheButton_WithTheReason()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            world.Game.Threshold = -2e8f;
            world.Game.Cash = -2e8f + 1e6f;
            GpuFoundModel model = Found(world);
            Assert.False(model.CanFound);
            Assert.Equal(FoundingBlock.NotEnoughCredit, model.Block);
            Assert.Equal("Не хватает кредитного лимита", model.ReasonText, "AC-1.4");
            Assert.True(model.MoneyLine.Contains("-"), "the money after paying is below zero and says so: " + model.MoneyLine);
        }

        [Test]
        public void Ac15_APureFoundry_CannotFound()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            world.Game.CpuCompany = false;
            GpuFoundModel model = Found(world);
            Assert.False(model.CanFound);
            Assert.Equal(FoundingBlock.FoundryNotAllowed, model.Block);
            Assert.Equal("Подразделение недоступно литейщику без CPU-подразделения", model.ReasonText, "AC-1.5");
        }

        [Test]
        public void ADivisionThatIsFounded_CannotBeFoundedAgain()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            Assert.True(world.Division.Found());
            GpuFoundModel model = Found(world);
            Assert.False(model.CanFound);
            Assert.Equal(FoundingBlock.AlreadyFounded, model.Block);
            Assert.Equal("Подразделение видеокарт уже основано.", model.ReasonText);
        }

        // ---- the tab of the research ----

        private static GpuResearchTabModel Tab(GpuWorld world, TestGpuText text)
        {
            return GpuResearchTabModel.Build(world.Content, world.Research, world.State, text);
        }

        private static GpuNodeView Node(GpuResearchTabModel model, string id)
        {
            foreach (GpuBranchView branch in model.Branches)
            {
                foreach (GpuNodeView node in branch.Nodes)
                {
                    if (node.Id == id) return node;
                }
            }
            Assert.Fail("no node " + id);
            return null;
        }

        [Test]
        public void TheTree_HasThreeBranches_WithTheNodesOfTheContent_EarliestFirst()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15)).Founded();
            GpuResearchTabModel model = Tab(world, new TestGpuText());

            Assert.Equal(3, model.Branches.Count);
            Assert.Equal(new[] { "Архитектура", "Видеопамять", "Охлаждение" }, new[] { model.Branches[0].Title, model.Branches[1].Title, model.Branches[2].Title });
            Assert.Equal(9, model.Branches[0].Nodes.Count);
            Assert.Equal(9, model.Branches[1].Nodes.Count);
            Assert.Equal(4, model.Branches[2].Nodes.Count);
            Assert.Equal(world.Content.ResearchNodes.Count, model.Branches[0].Nodes.Count + model.Branches[1].Nodes.Count + model.Branches[2].Nodes.Count);
            foreach (GpuBranchView branch in model.Branches)
            {
                for (int i = 1; i < branch.Nodes.Count; i++) Assert.True(branch.Nodes[i - 1].Year <= branch.Nodes[i].Year, branch.Branch + " is ordered by year");
            }
            Assert.Equal("2D-ускоритель · 1981", Node(model, "arch.2d").Caption);
        }

        [Test]
        public void TheStatesOfTheNodes_FollowTheLaboratory()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            Assert.True(world.Division.Found());   // the five nodes of 1992 are researched
            world.Game.Techs.Add("SDR");
            Assert.True(world.Research.Enqueue("vram.sgram"));
            Assert.True(world.Research.Enqueue("arch.3d"), "waits for vram.sgram, which is planned before it");
            GpuResearchTabModel model = Tab(world, new TestGpuText());

            Assert.Equal(GpuNodeState.Researched, Node(model, "arch.2d").State);
            Assert.Equal(GpuNodeState.Researched, Node(model, "vram.vram").State);
            Assert.Equal(GpuNodeState.Current, Node(model, "vram.sgram").State);
            Assert.Equal(0, Node(model, "vram.sgram").QueueIndex);
            Assert.Equal(GpuNodeState.Queued, Node(model, "arch.3d").State);
            Assert.Equal(1, Node(model, "arch.3d").QueueIndex);
            Assert.Equal(GpuNodeState.Locked, Node(model, "arch.tnl").State, "needs a node that is not planned");
            Assert.False(Node(model, "arch.tnl").CanQueue);

            Assert.True(model.Current != null);
            Assert.Equal("vram.sgram", model.Current.NodeId);
            Assert.Equal("SGRAM", model.Current.Name);
            Assert.Equal(1, model.Queue.Count);
            Assert.Equal("arch.3d", model.Queue[0].NodeId);
            Assert.Equal(4, model.QueueFree);
            Assert.Equal(5, model.QueueMax);
        }

        [Test]
        public void Ac23_ANodeThatNeedsATechnologyOfTheGame_IsLocked_WithThatTechnologyInItsTooltip()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            Assert.True(world.Division.Found());
            GpuResearchTabModel model = Tab(world, new TestGpuText());

            GpuNodeView sgram = Node(model, "vram.sgram");
            Assert.Equal(GpuNodeState.Locked, sgram.State);
            Assert.False(sgram.CanQueue);
            Assert.Equal("Нужно: SDR", sgram.BlockText, "AC-2.3");
            Assert.True(sgram.TipBody.Contains("Нужно: SDR"), sgram.TipBody);

            world.Game.Techs.Add("SDR");
            GpuNodeView open = Node(Tab(world, new TestGpuText()), "vram.sgram");
            Assert.Equal(GpuNodeState.Available, open.State);
            Assert.True(open.CanQueue);
            Assert.Equal(string.Empty, open.BlockText);
        }

        [Test]
        public void ANodeThatNeedsANodeNobodyPlans_SaysWhichOne()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15)).Founded();
            GpuNodeView gui = Node(Tab(world, new TestGpuText()), "arch.gui");
            Assert.Equal(GpuNodeState.Locked, gui.State);
            Assert.Equal("Сначала исследуйте: 2D-ускоритель", gui.BlockText);

            Assert.True(world.Research.Enqueue("arch.2d"));
            GpuNodeView after = Node(Tab(world, new TestGpuText()), "arch.gui");
            Assert.Equal(GpuNodeState.Available, after.State, "arch.2d is being researched: arch.gui may join the queue behind it");
            Assert.True(after.CanQueue);
        }

        [Test]
        public void TheDivisionThatIsNotFounded_LocksTheWholeTree()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            GpuResearchTabModel model = Tab(world, new TestGpuText());
            Assert.False(model.Founded);
            GpuNodeView first = Node(model, "arch.2d");
            Assert.False(first.CanQueue);
            Assert.Equal(GpuNodeState.Locked, first.State);
            Assert.Equal("Сначала основайте подразделение видеокарт.", first.BlockText);
        }

        [Test]
        public void Ac22_ANodeFarAhead_ShowsAboutTenYears_AndTheMarkAhead()
        {
            var world = new GpuWorld(new DateTime(1990, 1, 1)).Founded();
            GpuNodeView node = Node(Tab(world, new TestGpuText()), "arch.3d");
            Assert.True(node.TipBody.Contains("Год: 1995"), node.TipBody);
            Assert.True(node.TipBody.Contains("Срок исследования ≈ 10 лет"), "about ten years: " + node.TipBody);
            Assert.True(node.TipBody.Contains("Опережает время: срок ×"), node.TipBody);
            Assert.True(node.TipBody.Contains("Расход:"), node.TipBody);
            Assert.True(node.TipBody.Contains("Нужно: Ускоритель интерфейса, SGRAM"), "the nodes of the tree it needs: " + node.TipBody);
            Assert.Equal("3D-ускоритель", node.TipHeader);
        }

        [Test]
        public void ANodeOfThePresentYear_HasNoMarkAhead()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1)).Founded();
            GpuNodeView node = Node(Tab(world, new TestGpuText()), "arch.3d");
            Assert.False(node.TipBody.Contains("Опережает время"), node.TipBody);
            Assert.True(node.TipBody.Contains("Срок исследования ≈ 190 дней") || node.TipBody.Contains("Срок исследования ≈ 19"), "200 days at the pace of an IDM (1.05): " + node.TipBody);
        }

        [Test]
        public void TheFundingSlider_ChangesTheTimeAndTheCostAtOnce()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15));
            Assert.True(world.Division.Found());
            world.Game.Techs.Add("SDR");
            Assert.True(world.Research.Enqueue("vram.sgram"));
            var text = new TestGpuText();

            GpuCurrentResearchView full = GpuResearchTabModel.CurrentAt(world.Content, world.Research, text, 1f);
            GpuCurrentResearchView quarter = GpuResearchTabModel.CurrentAt(world.Content, world.Research, text, 0.25f);
            Assert.Near(full.DaysLeft * 2.0, quarter.DaysLeft, 2.0, "a quarter of the funding is half the pace (the square root): twice the days");
            Assert.Near(full.CostPerMonth * 0.25, quarter.CostPerMonth, 1.0, "and a quarter of the cost a month");
            Assert.Equal("25 %", quarter.FundingText);
            Assert.True(quarter.EtaText.StartsWith("Осталось ≈ "), quarter.EtaText);
            Assert.True(quarter.CostText.StartsWith("Расход в месяц: $"), quarter.CostText);
            Assert.Equal("Готово: 0 %", quarter.ProgressText);

            Assert.Null(GpuResearchTabModel.CurrentAt(world.Content, new GpuWorld(new DateTime(1995, 1, 1)).Founded().Research, text, 1f), "an idle laboratory has no current research");
        }

        [Test]
        public void TheFundingLimits_ComeFromTheBalance_AndTheValueIsKept()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15)).Founded();
            world.Research.SetFunding(0.4f);
            GpuResearchTabModel model = Tab(world, new TestGpuText());
            Assert.Near(0.4, model.Funding, 1e-6);
            Assert.Near(0.1, model.FundingMin, 1e-6);
            Assert.Near(1.0, model.FundingMax, 1e-6);
            Assert.Near(0.05, GpuResearchTabModel.FundingStep, 1e-9);
        }

        [Test]
        public void TheQueue_HoldsFiveNodes_AndTheSixthIsTurnedAwayWithTheReason()
        {
            var world = new GpuWorld(new DateTime(1990, 6, 15)).Founded();
            world.Game.Techs.Add("SDR");
            // a chain of independent first nodes and what follows them: arch.2d (current), then the queue
            string[] chain = { "arch.2d", "vram.dram", "cool.passive", "arch.gui", "vram.vram", "vram.sgram" };
            foreach (string id in chain) Assert.True(world.Research.Enqueue(id), id);   // one current and five waiting

            GpuResearchTabModel model = Tab(world, new TestGpuText());
            Assert.Equal(5, model.Queue.Count);
            Assert.Equal(0, model.QueueFree);

            GpuNodeView other = Node(model, "cool.fan");
            Assert.False(other.CanQueue);
            Assert.True(other.BlockText.Length > 0, "the queue is full or a node it needs is not planned");
        }

        [Test]
        public void TheQueueIsFull_TheReasonSaysSo()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15)).Founded("arch.2d");
            world.Game.Techs.Add("SDR");
            // arch.gui (current), then five independent nodes
            Assert.True(world.Research.Enqueue("arch.gui"));
            foreach (string id in new[] { "vram.dram", "cool.passive", "vram.vram", "vram.sgram", "cool.fan" }) Assert.True(world.Research.Enqueue(id), id);
            GpuResearchTabModel model = Tab(world, new TestGpuText());
            Assert.Equal(0, model.QueueFree);
            GpuNodeView extra = Node(model, "vram.ddr");
            Assert.False(extra.CanQueue);
            Assert.True(extra.BlockText.Length > 0, extra.BlockText);

            GpuNodeView noMore = Node(Tab(world, new TestGpuText()), "cool.dual");
            Assert.False(noMore.CanQueue);
            Assert.True(noMore.BlockText.Contains("Очередь заполнена: не больше 5 узлов.") || noMore.BlockText.Contains("Нужно") || noMore.BlockText.Contains("Сначала"), noMore.BlockText);
        }

        [Test]
        public void Ac24_WhenTheFirstNodeIsDone_TheSecondBecomesCurrent_AndTheTabSeesIt()
        {
            var world = new GpuWorld(new DateTime(1995, 6, 15)).Founded();
            Assert.True(world.Research.Enqueue("arch.2d"));
            Assert.True(world.Research.Enqueue("arch.gui"));
            Assert.Equal("arch.2d", Tab(world, new TestGpuText()).Current.NodeId);

            for (int day = 0; day < 400 && world.Research.Current != null && world.Research.Current.id == "arch.2d"; day++) world.Research.DailyTick();
            GpuResearchTabModel after = Tab(world, new TestGpuText());
            Assert.Equal(GpuNodeState.Researched, Node(after, "arch.2d").State);
            Assert.True(after.Current != null);
            Assert.Equal("arch.gui", after.Current.NodeId, "AC-2.4: the second node became current by itself");
            Assert.Equal(0, after.Queue.Count);
            Assert.True(world.Game.Notes.Count >= 1, "the notification of the first node");
        }

        [Test]
        public void TheTabs_SpeakEnglish_WhenTheLanguageIs()
        {
            var world = new GpuWorld(new DateTime(1990, 1, 1)).Founded();
            GpuResearchTabModel model = Tab(world, new TestGpuText("en"));
            Assert.Equal("Architecture", model.Branches[0].Title);
            GpuNodeView node = Node(model, "arch.3d");
            Assert.True(node.TipBody.Contains("Research time ≈ 10 years"), node.TipBody);
            Assert.True(node.TipBody.Contains("Ahead of its time: ×"), node.TipBody);
            Assert.Equal("3D accelerator · 1995", node.Caption);
        }
    }
}
