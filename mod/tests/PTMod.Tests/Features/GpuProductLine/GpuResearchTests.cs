using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The GPU laboratory (TSK-014, FR-2; F15, F16) on the shipped tree and the fake game: AC-2.1 (the pace and the cost of a day of <c>arch.3d</c> on 1 January
    /// 1996), AC-2.2 ("ahead of its time" in 1990), AC-2.3 (a technology of the game the node needs), AC-2.4 (the next node of the queue starts by itself and a
    /// notification comes), the catch-up of the founding (exactly five nodes up to 1992), the queue of five, the funding of 10-100 %, nothing without the division.
    /// </summary>
    [Covers(typeof(GpuResearch), typeof(NodeAvailability), typeof(GpuResearchQuote))]
    public class GpuResearchTests
    {
        private static readonly string[] Before1995 = { "arch.2d", "arch.gui", "vram.dram", "vram.vram", "vram.sgram", "cool.passive" };

        [Test]
        public void UnlockUpToYear1992_OpensExactlyTheFiveNodesOfStageOne()
        {
            var world = new GpuWorld(new DateTime(1995, 1, 1));
            List<string> opened = world.Research.UnlockUpToYear(1992);
            Assert.Equal(new[] { "arch.2d", "arch.gui", "vram.dram", "vram.vram", "cool.passive" }, opened.ToArray());
            Assert.Equal(opened.ToArray(), world.State.Data.research.completed.ToArray());
            Assert.Equal(0, world.Research.UnlockUpToYear(1992).Count, "a second call opens nothing new");
            Assert.Equal(new[] { "vram.sgram" }, world.Research.UnlockUpToYear(1994).ToArray());
        }

        [Test]
        public void UnlockUpToYear_TakesTheOpenedNodesOutOfTheQueue()
        {
            var world = new GpuWorld(new DateTime(1990, 1, 1)).Founded("arch.2d", "vram.dram", "cool.passive");
            Assert.True(world.Research.Enqueue("arch.gui"), "arch.gui becomes current");
            Assert.True(world.Research.Enqueue("vram.vram"), "vram.vram waits");
            Assert.Equal("arch.gui", world.Research.Current.id);

            world.Research.UnlockUpToYear(1987);
            Assert.True(world.Research.IsResearched("arch.gui"));
            Assert.True(world.Research.IsResearched("vram.vram"));
            Assert.Null(world.Research.Current, "nothing left to research");
            Assert.Equal(0, world.Research.Queue.Count);
        }

        [Test]
        public void Ac21_ADayOfArch3d_On1January1996()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded(Before1995);
            Assert.Equal(NodeAvailability.Available, world.Research.Availability("arch.3d"));
            Assert.True(world.Research.Enqueue("arch.3d"));
            Assert.Equal("arch.3d", world.Research.Current.id, "an idle laboratory starts the node at once");

            GpuResearchQuote quote = world.Research.Quote("arch.3d", 1f);
            Assert.Equal(200f, quote.EffectiveDays, "T_eff = 200: exactly 365 days after 1 January 1995, no discount yet");

            world.Research.DailyTick();
            Assert.Near(Math.Sqrt(1.0) * 1.05 / 200.0, world.Research.Progress, 1e-7, "progress = sqrt(1.0) x 1.05 / T_eff");
            Assert.Equal(1, world.Game.Spent.Count);
            Assert.Near(666667, world.Game.SpentOn(GpuExpense.Research), 1.0, "2e7 / 30 a day under ExpenseOrigin.Research");
            Assert.Near(666667 * 30.0, quote.CostPerMonth, 30.0);
        }

        [Test]
        public void Ac21_TheCostModifierOfTheGame_Applies()
        {
            // Modifiers.GetModifier(ResearchCost): x6 for a company lacking patents
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded(Before1995);
            world.Game.Modifiers[GpuModifierKind.ResearchCost] = 6f;
            world.Research.Enqueue("arch.3d");
            world.Research.DailyTick();
            Assert.Near(666667 * 6.0, world.Game.SpentOn(GpuExpense.Research), 6.0);
        }

        [Test]
        public void Ac22_ANodeOf1995_In1990_IsAheadOfItsTime()
        {
            var world = new GpuWorld(new DateTime(1990, 1, 1)).Founded(Before1995);
            GpuResearchQuote quote = world.Research.Quote("arch.3d", 1f);
            Assert.Near(3700, quote.EffectiveDays, 3700 * 0.01, "200 x (1 + 3.5 x 5) ~ 3700 days");
            Assert.True(quote.AheadFactor > 18f, "the mark \"опережает время\": x" + quote.AheadFactor);
            Assert.Near(quote.EffectiveDays / 1.05, quote.DaysLeft, 1.0, "about ten years at full funding");
            Assert.True(quote.DaysLeft / 365f > 9f && quote.DaysLeft / 365f < 10.5f, quote.DaysLeft + " days");
        }

        [Test]
        public void Ac23_ANodeThatNeedsATechnologyOfTheGame_IsLocked()
        {
            var world = new GpuWorld(new DateTime(2004, 6, 1)).Founded("vram.dram", "vram.vram", "vram.sgram", "vram.ddr");
            Assert.Equal(NodeAvailability.LockedByGameTech, world.Research.Availability("vram.gddr3"));
            Assert.Equal(new[] { "DDR2" }, world.Research.MissingGameTechs("vram.gddr3").ToArray(), "\"Нужно: DDR2\"");

            string reasonKey;
            string[] args;
            Assert.False(world.Research.Enqueue("vram.gddr3", out reasonKey, out args));
            Assert.Equal("gpu.research.block.tech", reasonKey);
            Assert.Equal(new[] { "DDR2" }, args);

            world.Game.Techs.Add("DDR2");
            Assert.Equal(NodeAvailability.Available, world.Research.Availability("vram.gddr3"));
            Assert.True(world.Research.Enqueue("vram.gddr3"));
        }

        [Test]
        public void Availability_OfEveryState()
        {
            var world = new GpuWorld(new DateTime(1990, 1, 1)).Founded("arch.2d");
            Assert.Equal(NodeAvailability.Researched, world.Research.Availability("arch.2d"));
            Assert.Equal(NodeAvailability.Available, world.Research.Availability("arch.gui"));
            Assert.Equal(NodeAvailability.LockedByNodes, world.Research.Availability("arch.3d"));
            Assert.Equal(new[] { "arch.gui", "vram.sgram" }, world.Research.MissingNodes("arch.3d").ToArray());
            Assert.Equal(NodeAvailability.LockedByNodes, world.Research.Availability("no.such.node"));
        }

        [Test]
        public void Ac24_WhenTheFirstNodeEnds_TheSecondStartsAndANotificationComes()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded(Before1995);
            var finished = new List<string>();
            world.Research.Completed += node => finished.Add(node.id);

            Assert.True(world.Research.Enqueue("cool.fan"));
            Assert.True(world.Research.Enqueue("arch.3d"));
            Assert.Equal("cool.fan", world.Research.Current.id);
            Assert.Equal(new[] { "arch.3d" }, ToArray(world.Research.Queue));

            int days = 0;
            while (finished.Count == 0 && days < 400)
            {
                world.Research.DailyTick();
                world.NextDay();
                days++;
            }
            Assert.Equal(new[] { "cool.fan" }, finished.ToArray());

            // the same days by hand: 60 base days, 1.05 a day, the discount of a node of last year growing by 0.15 / 365 a day
            float progress = 0f;
            int expected = 0;
            for (DateTime day = new DateTime(1996, 1, 1); progress < 1f; day = day.AddDays(1))
            {
                progress += 1.05f / GpuResearchMath.EffectiveDays(60, 1995, day, 3.5f, 0.15f, 0.75f);
                expected++;
            }
            Assert.Equal(expected, days, "the day the node ends");
            Assert.True(days >= 56 && days <= 58, days + " days for a node of 60 at x1.05");
            Assert.True(world.Research.IsResearched("cool.fan"));
            Assert.Equal("arch.3d", world.Research.Current.id, "the second node is current without the player");
            Assert.Equal(0f, world.Research.Progress);
            Assert.Equal(0, world.Research.Queue.Count);

            Assert.Equal(1, world.Game.Notes.Count);
            Assert.Equal("gpu.notify.researchDone", world.Game.Notes[0].Key);
            Assert.Equal("gpu.cool.fan", world.Game.Notes[0].Argument, "the name of the cooler");
            Assert.True(world.Game.Notes[0].ArgumentIsKey);

            world.Research.DailyTick();
            Assert.True(world.Research.Progress > 0f, "the second node moves the next day");
        }

        [Test]
        public void TheQueue_HoldsFiveNodes()
        {
            var world = new GpuWorld(new DateTime(2010, 1, 1)).Founded("arch.2d", "vram.dram", "cool.passive");
            foreach (string tech in new[] { "SDR", "DDR", "DDR2", "DDR3", "250nm", "130nm", "90nm", "45nm" }) world.Game.Techs.Add(tech);

            Assert.True(world.Research.Enqueue("arch.gui"), "current");
            foreach (string id in new[] { "vram.vram", "cool.fan", "cool.dual", "vram.sgram", "arch.3d" })
                Assert.True(world.Research.Enqueue(id), id);
            Assert.Equal(5, world.Research.Queue.Count);

            string reasonKey;
            string[] args;
            Assert.False(world.Research.Enqueue("vram.ddr", out reasonKey, out args), "the sixth waiting node");
            Assert.Equal("gpu.research.block.queueFull", reasonKey);
            Assert.Equal(new[] { "5" }, args);

            Assert.False(world.Research.Enqueue("cool.fan", out reasonKey, out args));
            Assert.Equal("gpu.research.block.queued", reasonKey);
            Assert.False(world.Research.Enqueue("arch.2d", out reasonKey, out args));
            Assert.Equal("gpu.research.block.done", reasonKey);
            Assert.False(world.Research.Enqueue("nope", out reasonKey, out args));
            Assert.Equal("gpu.research.block.unknown", reasonKey);
        }

        [Test]
        public void ANodeWhoseNodesAreNotPlanned_IsRefused_APlannedOneWaits()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded("arch.2d", "vram.dram", "vram.vram", "cool.passive");
            world.Game.Techs.Add("SDR");
            string reasonKey;
            string[] args;
            Assert.True(world.Research.Enqueue("arch.gui"));
            Assert.False(world.Research.Enqueue("arch.3d", out reasonKey, out args), "vram.sgram is neither done nor planned");
            Assert.Equal("gpu.research.block.nodes", reasonKey);
            Assert.Equal(new[] { "vram.sgram" }, args);

            Assert.True(world.Research.Enqueue("vram.sgram"));
            Assert.True(world.Research.Enqueue("arch.3d"), "both nodes it needs are planned before it");
            Assert.Equal(new[] { "vram.sgram", "arch.3d" }, ToArray(world.Research.Queue));
        }

        [Test]
        public void Dequeue_OfTheCurrentNode_StartsTheNext_AndDropsTheOrphans()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded("arch.2d", "vram.dram", "vram.vram", "cool.passive");
            world.Game.Techs.Add("SDR");
            Assert.True(world.Research.Enqueue("vram.sgram"));
            Assert.True(world.Research.Enqueue("arch.gui"));
            Assert.True(world.Research.Enqueue("arch.3d"));
            world.Research.DailyTick();
            Assert.True(world.Research.Progress > 0f);

            world.Research.Dequeue("vram.sgram");
            Assert.Equal("arch.gui", world.Research.Current.id, "the next open node starts");
            Assert.Equal(0f, world.Research.Progress, "the progress of the stopped node is lost");
            Assert.Equal(0, world.Research.Queue.Count, "arch.3d waited for vram.sgram, which nobody plans now: dropped");
        }

        [Test]
        public void Funding_IsHeldToTenToHundredPercent_AndChangesThePace()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded(Before1995);
            world.Research.SetFunding(0.01f);
            Assert.Near(0.1, world.Research.Funding, 1e-6);
            world.Research.SetFunding(5f);
            Assert.Near(1.0, world.Research.Funding, 1e-6);
            world.Research.SetFunding(float.NaN);
            Assert.Near(1.0, world.Research.Funding, 1e-6);

            // the tab shows the new pace at once (FR-2 item 7)
            GpuResearchQuote full = world.Research.Quote("arch.3d", 1f);
            GpuResearchQuote half = world.Research.Quote("arch.3d", 0.5f);
            Assert.Near(full.DailyProgress * Math.Sqrt(0.5), half.DailyProgress, 1e-7);
            Assert.Near(full.CostPerMonth * 0.5, half.CostPerMonth, 1.0);

            world.Research.SetFunding(0.5f);
            world.Research.Enqueue("arch.3d");
            world.Research.DailyTick();
            Assert.Near(Math.Sqrt(0.5) * 1.05 / 200.0, world.Research.Progress, 1e-7);
            Assert.Near(666667 * 0.5, world.Game.SpentOn(GpuExpense.Research), 1.0);
        }

        [Test]
        public void WithoutTheDivision_NothingMoves()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1));
            world.State.Data.research.completed.AddRange(Before1995);
            string reasonKey;
            string[] args;
            Assert.False(world.Research.Enqueue("arch.3d", out reasonKey, out args));
            Assert.Equal("gpu.research.block.notFounded", reasonKey);

            world.State.Data.research.currentId = "arch.3d";   // a section that says otherwise
            world.Research.DailyTick();
            Assert.Equal(0, world.Game.Spent.Count);
            Assert.Equal(0f, world.State.Data.research.currentProgress);
        }

        [Test]
        public void AnIdleLaboratory_CostsNothing()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1)).Founded(Before1995);
            for (int day = 0; day < 10; day++) world.Research.DailyTick();
            Assert.Equal(0, world.Game.Spent.Count);
        }

        [Test]
        public void NameKeyOf_IsTheNameOfThePartTheNodeOpens()
        {
            var world = new GpuWorld(new DateTime(1996, 1, 1));
            Assert.Equal("gpu.arch.3d", world.Research.NameKeyOf(world.Content.Node("arch.3d")));
            Assert.Equal("gpu.mem.gddr3", world.Research.NameKeyOf(world.Content.Node("vram.gddr3")));
            Assert.Equal("gpu.cool.vapor", world.Research.NameKeyOf(world.Content.Node("cool.vapor")));
            Assert.Null(world.Research.NameKeyOf(null));
        }

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < list.Count; i++) array[i] = list[i];
            return array;
        }
    }
}
