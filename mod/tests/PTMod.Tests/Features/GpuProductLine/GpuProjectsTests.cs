using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The development of cards (TSK-015, FR-4; F13, F14) and the designer behind it, on the fake game: AC-4.1 (1.45e8 and 420 days for the design of AC-3.1), AC-4.2
    /// (a paused project neither moves nor costs), AC-4.4 (the fabless modifier of <c>Modifiers.GetModifier(DevelopmentCost)</c>), the limit of parallel projects,
    /// the daily cost with the inflation since the start, a cancel without a refund, and the end: a product <c>ready</c> with the snapshot of the design and the
    /// event <c>ReadyToRelease</c>.
    /// </summary>
    [Covers(typeof(GpuProjects), typeof(GpuDesigner))]
    public class GpuProjectsTests
    {
        private static GpuWorld World2005()
        {
            return new GpuWorld(new DateTime(2005, 1, 1)).Founded("arch.shader", "vram.gddr3", "cool.fan");
        }

        [Test]
        public void TheDesignOfAc31_EvaluatesAsInTheDesignDocument()
        {
            GpuWorld world = World2005();
            GpuStats stats = world.Designer.Evaluate(GpuWorld.Ac31Input());
            Assert.True(stats.Valid, "valid: " + stats.InvalidReasonKey);
            Assert.Equal(30, stats.Units);
            Assert.Near(122400, stats.ComputeMops, 1.0);
            Assert.Near(51200, stats.BandwidthMBs, 0.5);
            Assert.Near(79, stats.PowerW, 0.5);
            Assert.Near(74.5, stats.TempC, 0.5);
            Assert.Near(0.1142, stats.FailureRate, 0.001);
            Assert.True(stats.DieCost > 0f, "the cost of a die comes from CpuMath.RealUnitCost: " + stats.DieCost);
            Assert.True(stats.UnitCost > stats.DieCost, "plus memory, board and cooler");
        }

        [Test]
        public void Ac41_TheProject_Costs145Million_AndTakes420Days()
        {
            GpuWorld world = World2005();
            world.Game.InflationValue = 1.2f;
            string reasonKey;
            string projectId = world.Projects.Start(GpuWorld.Ac31Input(), out reasonKey);
            Assert.NotNull(projectId, "started: " + reasonKey);

            GpuProjectSave project = world.Projects.Find(projectId);
            Assert.Near(1.45e8, project.totalCost, 16.0, "(8e7 + 5e7 x 0.5 + 4e7) x 1.0");
            Assert.Equal(420, project.days, "220 + 200");
            Assert.Equal(0f, project.progress, "\"В разработке 0 %\"");
            Assert.Near(1.2, project.inflationAtStart, 1e-6);
            Assert.False(project.paused);
            Assert.Equal(1, world.Projects.Active.Count);
            Assert.Equal(0, world.Game.Spent.Count, "nothing is paid at the start");

            // the design is kept with its snapshot (FR-3 item 7)
            GpuDesignSave design = world.Projects.Design(project.designId);
            Assert.NotNull(design);
            Assert.Equal("Vortex", design.name);
            Assert.Equal("arch.shader", design.archId);
            Assert.Equal("90nm", design.node);
            Assert.Equal(256, design.busWidth);
            Assert.NotNull(design.stats);
            Assert.Equal(30, design.stats.units);
            Assert.Near(122400, design.stats.computeMops, 1.0);
            Assert.Near(1.45e8, design.stats.projectCost, 16.0);
            Assert.Equal(420, design.stats.projectDays);
            Assert.Equal(10f, design.stats.dieMaterial, "arch.dieMaterialCost");
            // 256 MB x 0.1 x 0.8^1 (2005, GDDR3 of 2004) + 20 + 256 x 0.08 + fan 8
            Assert.Near(256 * 0.1 * 0.8 + 20 + 256 * 0.08 + 8, design.stats.perGoodExtras, 0.01);
            Assert.Equal(new[] { "2d", "gui", "3d", "tnl", "shader" }, design.stats.features.ToArray());
        }

        [Test]
        public void Ac44_AFablessCompany_PaysTwoThirds()
        {
            GpuWorld world = World2005();
            world.Game.Modifiers[GpuModifierKind.DevelopmentCost] = 0.67f;   // Modifiers.GetModifier(DevelopmentCost) of a fabless company
            string projectId = world.Projects.Start(GpuWorld.Ac31Input());
            Assert.Near(9.72e7, world.Projects.Find(projectId).totalCost, 9.72e7 * 0.001, "1.45e8 x 0.67");
            Assert.Equal(420, world.Projects.Find(projectId).days, "the time does not change");
        }

        [Test]
        public void ADay_CostsTheShareOfTheDay_WithTheInflationSinceTheStart()
        {
            GpuWorld world = World2005();
            string projectId = world.Projects.Start(GpuWorld.Ac31Input());
            world.Projects.DailyTick();
            Assert.Near(1.45e8 / 420.0, world.Game.SpentOn(GpuExpense.Development), 1.0, "F14 with no inflation");
            Assert.Near(1.0 / 420.0, world.Projects.Find(projectId).progress, 1e-6);

            world.Game.Spent.Clear();
            world.Game.InflationValue = 1.1f;
            world.Projects.DailyTick();
            Assert.Near(1.45e8 / 420.0 * 1.1, world.Game.SpentOn(GpuExpense.Development), 1.0, "cost / days x inflation now / inflation at the start");
        }

        [Test]
        public void Ac42_APausedProject_NeitherMovesNorCosts()
        {
            GpuWorld world = World2005();
            string projectId = world.Projects.Start(GpuWorld.Ac31Input());
            for (int day = 0; day < 168; day++) world.Projects.DailyTick();
            GpuProjectSave project = world.Projects.Find(projectId);
            Assert.Near(0.40, project.progress, 1e-4, "168 of 420 days");

            world.Projects.SetPaused(projectId, true);
            float paid = world.Game.SpentTotal();
            for (int day = 0; day < 30; day++)
            {
                world.Projects.DailyTick();
                world.NextDay();
            }
            Assert.Near(0.40, project.progress, 1e-4, "still 40 %");
            Assert.Equal(paid, world.Game.SpentTotal(), "no payment in 30 days");

            world.Projects.SetPaused(projectId, false);
            world.Projects.DailyTick();
            Assert.Near(169.0 / 420.0, project.progress, 1e-4, "goes on from the same progress");
        }

        [Test]
        public void TheEnd_MakesAReadyProduct_WithTheSnapshot_AndRaisesReadyToRelease()
        {
            GpuWorld world = World2005();
            var ready = new List<string>();
            world.Projects.ReadyToRelease += ready.Add;
            string projectId = world.Projects.Start(GpuWorld.Ac31Input());
            string designId = world.Projects.Find(projectId).designId;

            for (int day = 0; day < 419; day++) world.Projects.DailyTick();
            Assert.Equal(0, ready.Count, "419 days: not yet");
            Assert.NotNull(world.Projects.Find(projectId));
            world.Projects.DailyTick();

            Assert.Equal(1, ready.Count, "the 420th day ends it");
            Assert.Null(world.Projects.Find(projectId), "the project is gone");
            Assert.Near(1.45e8, world.Game.SpentOn(GpuExpense.Development), 1.45e8 * 1e-4, "the whole cost was paid day by day");

            GpuProductSave product = world.State.Data.products.Find(p => p.id == ready[0]);
            Assert.NotNull(product);
            Assert.Equal(GpuState.StatusReady, product.status, "\"Готов к выпуску\"");
            Assert.Equal(GpuState.OwnerPlayer, product.owner);
            Assert.Equal("player", product.companyKey);
            Assert.Equal(designId, product.designId);
            Assert.Equal("Vortex", product.name);
            Assert.Equal("90nm", product.node);
            Assert.Equal(300f, product.die);
            Assert.Equal(256, product.busWidth);
            Assert.Equal(256f, product.vramMB);
            Assert.Equal(0, product.lines);
            Assert.Equal(0, product.stock);
            Assert.Equal(30, product.stats.units, "the snapshot of the design");
            Assert.False(ReferenceEquals(product.stats, world.Projects.Design(designId).stats), "a copy: the product lives on its own snapshot");

            Assert.Equal(1, world.Game.Notes.Count);
            Assert.Equal("gpu.notify.projectDone", world.Game.Notes[0].Key);
            Assert.Equal("Vortex", world.Game.Notes[0].Argument);
            Assert.False(world.Game.Notes[0].ArgumentIsKey);

            // the product survives the save
            var loaded = new GpuState(world.Content);
            Assert.True(loaded.Read(world.State.Write(), GpuState.SchemaVersion));
            Assert.Equal(GpuState.StatusReady, loaded.Data.products.Find(p => p.id == product.id).status);
        }

        [Test]
        public void AtMostThreeProjects_RunAtATime()
        {
            GpuWorld world = World2005();
            for (int i = 0; i < 3; i++) Assert.NotNull(world.Projects.Start(GpuWorld.Ac31Input()), "project " + (i + 1));
            string reasonKey;
            Assert.False(world.Projects.CanStart(out reasonKey));
            Assert.Equal("gpu.project.block.limit", reasonKey);
            Assert.Null(world.Projects.Start(GpuWorld.Ac31Input(), out reasonKey));
            Assert.Equal("gpu.project.block.limit", reasonKey);

            // a paused project still counts; a cancelled one does not
            world.Projects.SetPaused(world.Projects.Active[0].id, true);
            Assert.False(world.Projects.CanStart(out reasonKey));
            world.Projects.Cancel(world.Projects.Active[0].id);
            Assert.True(world.Projects.CanStart(out reasonKey));
        }

        [Test]
        public void Cancel_GivesNoMoneyBack()
        {
            GpuWorld world = World2005();
            string projectId = world.Projects.Start(GpuWorld.Ac31Input());
            for (int day = 0; day < 10; day++) world.Projects.DailyTick();
            float paid = world.Game.SpentTotal();
            Assert.True(paid > 0f);

            Assert.True(world.Projects.Cancel(projectId));
            Assert.Null(world.Projects.Find(projectId));
            Assert.Equal(0, world.Game.Earned.Count, "no refund");
            Assert.Equal(paid, world.Game.SpentTotal());
            world.Projects.DailyTick();
            Assert.Equal(paid, world.Game.SpentTotal(), "a cancelled project costs nothing more");
            Assert.False(world.Projects.Cancel(projectId), "twice");
        }

        [Test]
        public void ADesignThatCannotBeDeveloped_DoesNotStart()
        {
            GpuWorld world = World2005();
            GpuDesignInput input = GpuWorld.Ac31Input();
            input.DieMm2 = 400f;   // the largest die of 2005 is 350 mm2
            string reasonKey;
            Assert.Null(world.Projects.Start(input, out reasonKey));
            Assert.Equal(GpuDesignMath.ReasonMaxDie, reasonKey);

            input = GpuWorld.Ac31Input();
            input.NodeName = "3nm";   // not a node of the company
            Assert.Null(world.Projects.Start(input, out reasonKey));
            Assert.Equal(GpuDesignMath.ReasonNode, reasonKey);
            Assert.Equal(0, world.Projects.Active.Count);
            Assert.Equal(0, world.State.Data.designs.Count, "no design is kept for a project that did not start");
        }

        [Test]
        public void WithoutTheDivision_NoProjectStarts()
        {
            var world = new GpuWorld(new DateTime(2005, 1, 1));
            string reasonKey;
            Assert.Null(world.Projects.Start(GpuWorld.Ac31Input(), out reasonKey));
            Assert.Equal("gpu.project.block.notFounded", reasonKey);
        }

        [Test]
        public void TheName_IsCleaned_AndAnEmptyOneGetsANumber()
        {
            GpuWorld world = World2005();
            GpuDesignInput input = GpuWorld.Ac31Input();
            input.Name = "  <b>Turbo</b> 9000 with a name far longer than thirty two characters ";
            string projectId = world.Projects.Start(input);
            string name = world.Projects.Design(world.Projects.Find(projectId).designId).name;
            Assert.True(name.Length <= 32, name);
            Assert.False(name.Contains("<"), name);
            Assert.True(name.StartsWith("Turbo 9000", StringComparison.Ordinal), name);

            input.Name = "   ";
            projectId = world.Projects.Start(input);
            name = world.Projects.Design(world.Projects.Find(projectId).designId).name;
            Assert.True(name.StartsWith("GPU ", StringComparison.Ordinal), name);
        }

        [Test]
        public void TheDesigner_PutsTheChannelsAndTheFablessYieldIntoTheContext()
        {
            GpuWorld world = World2005();
            world.Game.UnitCostMul = 1.5f;
            world.Game.VramCostMul = 2f;
            GpuDesignContext context = world.Designer.ContextFor(GpuWorld.Ac31Input());
            Assert.Equal(1.5f, context.UnitCostMul);
            Assert.Equal(2f, context.VramCostMul);
            Assert.Equal(1f, context.DevModifier);
            Assert.Equal(1f, context.CoDevFactor, "no CPU of the company on the node: co-development does not act (FR-17)");
            Assert.Equal(2005, context.Year);
            Assert.Equal(350f, context.MaxDieMm2);
            Assert.Equal(0.9f, context.Yield, "ICompany.CurrentNodeYield on the own factory");
            Assert.Near(3e5 * 300, context.Transistors, 1.0);

            world.Game.Fabless = true;
            context = world.Designer.ContextFor(GpuWorld.Ac31Input());
            Assert.Near(0.5 + (0.95 - 0.5) * 0.75, context.Yield, 1e-6, "fabless: Lerp(min, max, fablessYieldProgress)");
            GpuStats fabless = world.Designer.Evaluate(GpuWorld.Ac31Input());
            world.Game.Fabless = false;
            GpuStats own = world.Designer.Evaluate(GpuWorld.Ac31Input());
            Assert.True(fabless.DieCost > own.DieCost * 1.5f, "the foundry's markup: " + fabless.DieCost + " vs " + own.DieCost);
        }
    }
}
