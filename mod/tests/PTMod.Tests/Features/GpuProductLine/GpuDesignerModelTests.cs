using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The designer of the tab "Конструктор" as data (TSK-024; FR-3, FR-4, FR-17): the choices on offer follow the research, AC-3.1 and AC-3.2 (the stats and the
    /// warning "Упор в память: −12 %") come out of the model the tab shows, the sliders reach the values of the examples, a change of an input recomputes once and a
    /// read does not (NFR-1), the warnings, the block "Кристалл не помещается", the table by segment, the button "Разработать" (AC-4.1, AC-4.4, the limit of projects).
    /// </summary>
    [Covers(typeof(GpuDesignerModel), typeof(GpuStatLine), typeof(GpuWarningLine), typeof(GpuSegmentRow))]
    public class GpuDesignerModelTests
    {
        private static GpuStatLine Line(GpuDesignerModel model, string key)
        {
            foreach (GpuStatLine line in model.Lines)
            {
                if (line.Key == key) return line;
            }
            Assert.Fail("no line " + key);
            return null;
        }

        private static bool HasWarning(GpuDesignerModel model, string text)
        {
            foreach (GpuWarningLine warning in model.Warnings)
            {
                if (warning.Text == text) return true;
            }
            return false;
        }

        // ---- the choices on offer ----

        [Test]
        public void WithoutAnyPartResearched_TheDesignerHasNothingToDesignWith()
        {
            var world = new GpuWorld(new DateTime(2005, 3, 10)).Founded();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());

            Assert.False(model.HasParts);
            Assert.Equal(0, model.Archs.Count);
            Assert.True(model.MissingPartsText.StartsWith("Конструктор недоступен: нет архитектуры, типа видеопамяти, охлаждения"), model.MissingPartsText);
            Assert.False(model.CanDevelop);
            Assert.Equal(model.MissingPartsText, model.DevelopBlockText);
            Assert.False(model.Stats.Valid);
            Assert.Equal(0, model.Lines.Count);
        }

        [Test]
        public void TheChoices_AreTheResearchedParts_EarliestFirst_AndTheNodesOfTheCompany()
        {
            var world = new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.2d", "arch.gui", "arch.3d", "vram.dram", "vram.sgram", "cool.passive", "cool.fan");
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());

            Assert.True(model.HasParts);
            Assert.Equal(new[] { "2D-ускоритель", "Ускоритель интерфейса", "3D-ускоритель" }, model.ArchLabels().ToArray());
            Assert.Equal(new[] { "DRAM", "SGRAM" }, model.MemoryLabels().ToArray());
            Assert.Equal(new[] { "Пассивный радиатор", "Вентилятор" }, model.CoolerLabels().ToArray());
            Assert.Equal(new[] { "350nm", "250nm", "90nm", "5nm" }, model.NodeLabels().ToArray(), "oldest (thinnest density) first");
            Assert.Equal(2, model.ArchIndex, "a new design starts on the newest architecture");
            Assert.Equal(3, model.NodeIndex, "...and the newest node");
            Assert.Equal(1, model.MemoryIndex);
            Assert.Equal(1, model.CoolerIndex);
        }

        [Test]
        public void ThePartsSignature_ChangesWhenAPartIsResearched_AndAPeekDoesNotTouchTheChoices()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            string before = model.PartsSignature;
            Assert.Equal(before, model.PeekPartsSignature());
            int recomputes = model.Recomputes;

            world.State.Data.research.completed.Add("arch.tnl");
            Assert.NotEqual(before, model.PeekPartsSignature(), "a new architecture is on offer");
            Assert.Equal(before, model.PartsSignature, "until the model reads the parts again");
            Assert.Equal(recomputes, model.Recomputes, "peeking recomputes nothing");

            string chosen = model.ArchLabels()[model.ArchIndex];
            model.RefreshParts();
            Assert.Equal(2, model.Archs.Count);
            Assert.Equal(chosen, model.ArchLabels()[model.ArchIndex], "the choice stays on the architecture that was chosen");
            Assert.Equal(model.PartsSignature, model.PeekPartsSignature());
        }

        // ---- AC-3.1, AC-3.2 ----

        [Test]
        public void Ac31_TheDesign_GivesTheNumbersOfTheExample()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            GpuStats s = model.Stats;
            Assert.True(s.Valid, s.InvalidReasonKey);
            Assert.Equal(30, s.Units, "30 blocks");
            Assert.Near(122400.0, s.ComputeMops, 1.0, "compute power 122 400");
            Assert.Near(51200.0, s.BandwidthMBs, 1.0, "bandwidth 51 200 MB/s");
            Assert.True(s.BandwidthRatio >= 1f, "no memory bottleneck");
            Assert.Near(79.0, s.PowerW, 1.0, "about 79 W");
            Assert.Near(74.0, s.TempC, 1.0, "about 74 C");
            Assert.Near(0.114, s.FailureRate, 0.003, "about 11.4 % of the dies fail");
            Assert.Near(510.0, model.ClockMHz, 0.01, "the highest clock of the architecture: 3.4 GHz x 0.15");
            Assert.Equal(256f, model.VramMB);
            Assert.Equal(300f, model.Die);

            Assert.Equal("30", Line(model, "units").Value);
            Assert.Equal("122400 млн оп/с", Line(model, "compute").Value);
            Assert.Equal("51200 МБ/с", Line(model, "bandwidth").Value);
            Assert.Equal(GpuTone.Normal, Line(model, "bandwidth").Tone);
            Assert.False(HasWarning(model, "Упор в память: −0 %"));
            Assert.Equal(0, model.Warnings.Count, "a sound design has no warning");
        }

        [Test]
        public void Ac32_ANarrowerBus_ShowsTheMemoryBoundWarning()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            model.SelectBus(UiWorlds.BusIndex(model, 128));
            GpuStats s = model.Stats;
            Assert.Near(25600.0, s.BandwidthMBs, 1.0);
            Assert.Near(0.837, s.BandwidthRatio, 0.002, "bandwidth over the bandwidth needed");
            Assert.Near(108000.0, s.EffectivePerf, 1000.0, "performance about 108 000");
            Assert.True(HasWarning(model, "Упор в память: −12 %"), "AC-3.2: the warning with the loss");
            Assert.Equal(GpuTone.Warn, Line(model, "bandwidth").Tone);
            Assert.Equal(GpuTone.Warn, Line(model, "perf").Tone);
            Assert.True(Line(model, "perf").TipBody.Contains("12 %"), Line(model, "perf").TipBody);
            Assert.True(Line(model, "perf").TipBody.Contains("0.84"), Line(model, "perf").TipBody);
            Assert.True(model.Stats.Valid, "memory-bound is a warning, not a block");
            Assert.True(model.CanDevelop);
        }

        [Test]
        public void EveryCharacteristic_HasATooltipInPlainWords()
        {
            var world = UiWorlds.Ac31();
            foreach (string language in new[] { "ru", "en" })
            {
                var text = new TestGpuText(language);
                GpuDesignerModel model = UiWorlds.Designer(world, text);
                UiWorlds.SetAc31(model);
                Assert.Equal(13, model.Lines.Count, "the characteristics of the tab");
                foreach (GpuStatLine line in model.Lines)
                {
                    Assert.True(line.Label.Length > 0 && !line.Label.StartsWith("["), language + " label of " + line.Key);
                    Assert.True(line.Value.Length > 0, language + " value of " + line.Key);
                    Assert.True(line.TipBody.Length > 20 && !line.TipBody.StartsWith("["), language + " tooltip of " + line.Key + ": " + line.TipBody);
                    Assert.Equal(line.Label, line.TipHeader);
                    Assert.False(line.TipBody.Contains("{"), "every placeholder is filled: " + line.TipBody);
                }
                Assert.Equal(0, text.Warnings.Count, language + ": " + string.Join("; ", text.Warnings.ToArray()));
            }
        }

        // ---- warnings ----

        [Test]
        public void ADieThatRunsHot_ShowsTheOverheatWarning_AndTheBadTone()
        {
            var world = new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.shader", "vram.gddr3", "cool.passive");
            world.Game.AddTimelineNodes();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);
            model.SetClockPercent(50);   // hot, but not so hot that every die fails (that design cannot be built at all)

            Assert.True(model.Stats.TempC > 95f, "a passive heatsink on about 40 W: " + model.Stats.TempC);
            Assert.True(model.Stats.Valid, model.Stats.InvalidReasonKey);
            Assert.True(HasWarning(model, "Перегрев: нужно охлаждение мощнее"));
            Assert.Equal(GpuTone.Bad, Line(model, "temp").Tone);
            Assert.Equal(GpuTone.Bad, Line(model, "failure").Tone, "FR-3 item 4: the failure rate is red");
            Assert.True(model.CanDevelop, "overheating is a warning, not a block");
        }

        [Test]
        public void ADieThatDoesNotFitTheWafer_BlocksTheButton_WithTheReason()
        {
            var world = UiWorlds.Ac31();
            world.Game.Wafer = 20f;   // a wafer so small that the largest die of the year has no place on it
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            Assert.False(model.Stats.Valid);
            Assert.Equal("gpu.design.block.die", model.Stats.InvalidReasonKey);
            Assert.True(HasWarning(model, "Кристалл не помещается на пластину"), "AC-3.3");
            Assert.False(model.CanDevelop);
            Assert.Equal("Кристалл не помещается на пластину", model.DevelopBlockText);
            Assert.Equal(GpuTone.Bad, model.Warnings[0].Tone);

            string reason;
            Assert.Null(model.Develop(out reason), "the project is not started");
            Assert.Equal("Кристалл не помещается на пластину", reason);
            Assert.Equal(0, world.Projects.Active.Count);
        }

        // ---- the sliders ----

        [Test]
        public void TheDieSlider_RunsFromTheMinimumToTheLargestDieOfTheYear_AndKeepsTo_ThatRange()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());

            Assert.Equal(20f, model.DieRange.Min, "design.minDie");
            Assert.Equal(world.Content.MaxDie(2005), model.DieRange.Max, "the largest die of 2005");
            Assert.True(model.DieRange.Positions <= GpuDesignerModel.DiePositions);

            model.SetDie(100000f);
            Assert.Equal(model.DieRange.Max, model.Die);
            model.SetDie(1f);
            Assert.Equal(20f, model.Die);
            model.SetDie(300f);
            Assert.Equal(300f, model.Die, "AC-3.1: 300 mm2 is on the grid");
        }

        [Test]
        public void TheClockSlider_IsAShareOfTheHighestClock_AndFollowsTheArchitecture()
        {
            var world = new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.tnl", "arch.shader", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());

            Assert.Equal(25, model.ClockMinPercent, "design.clockMinShare");
            model.SelectArch(model.ArchLabels().IndexOf("Шейдеры"));
            model.SetClockPercent(10);
            Assert.Equal(25, model.ClockPercent, "held to the lowest share");
            Assert.Near(127.5, model.MaxClockMHz * 0.25f, 0.01, "a quarter of 510 MHz");

            model.SetClockPercent(100);
            Assert.Near(510.0, model.ClockMHz, 0.01, "the highest clock of arch.shader: 3.4 GHz x 0.15");
            model.SetClockPercent(50);
            Assert.Equal(255f, model.ClockMHz);

            model.SelectArch(model.ArchLabels().IndexOf("Аппаратная геометрия (T&L)"));
            Assert.Equal(50, model.ClockPercent, "the share stays when the architecture changes");
            Assert.Near(340.0, model.ClockMHz, 0.02, "half of 3.4 GHz x 0.20");
            model.SetClockPercent(100);
            Assert.Near(680.0, model.ClockMHz, 0.02, "the highest clock never goes above the highest");
            Assert.True(model.ClockMHz <= model.MaxClockMHz);
        }

        [Test]
        public void TheVideoMemory_IsPowersOfTwo_UpToTheLimitOfTheMemoryType_AndAMemoryChangeKeepsWhatItCan()
        {
            var world = new GpuWorld(new DateTime(2005, 3, 10)).Founded("arch.shader", "vram.ddr", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());

            model.SelectMemory(model.MemoryLabels().IndexOf("GDDR3"));
            Assert.Equal(1024f, model.VramValues[model.VramValues.Count - 1], "GDDR3 carries up to 1 GB");
            model.SelectBus(UiWorlds.BusIndex(model, 256));
            model.SetVramIndex(UiWorlds.VramIndex(model, 512f));
            Assert.Equal(512f, model.VramMB);
            Assert.Equal(256, model.Buses[model.BusIndex]);

            model.SelectMemory(model.MemoryLabels().IndexOf("DDR"));
            Assert.Equal(128f, model.VramValues[model.VramValues.Count - 1], "DDR carries up to 128 MB");
            Assert.Equal(128f, model.VramMB, "512 MB does not fit: the nearest value that does");
            Assert.True(model.Buses.Count > 0 && model.BusIndex < model.Buses.Count, "the bus is one of this memory's");
            Assert.Equal(new[] { "64 бит", "128 бит" }, model.BusLabels().ToArray());
        }

        [Test]
        public void ANewDesign_StartsWithEnoughVideoMemoryForTheSegment()
        {
            var world = UiWorlds.Ac31();
            world.Market.StartSession();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            float required = world.Market.OpenSegments[0].VramRequirementMB;
            Assert.True(required > 0f);
            Assert.True(model.VramMB >= required, "the first design meets the requirement of the year: " + model.VramMB + " MB for " + required);
            Assert.True(model.VramMB < required * 2f, "...and not more than the next power of two");
        }

        [Test]
        public void TheName_IsCleaned_AndTheNameDoesNotRecompute()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            int recomputes = model.Recomputes;

            model.SetName("  <b>Vortex</b> <color=#f00>X</color>  ");
            Assert.Equal("Vortex X", model.Name, "tags are removed, the ends trimmed");
            model.SetName(new string('a', 60));
            Assert.Equal(32, model.Name.Length, "design.nameMaxLength");
            Assert.Equal(32, model.NameMaxLength);
            Assert.Equal(recomputes, model.Recomputes, "the name is no input of the characteristics");
            Assert.Equal(model.Name, model.Input.Name);
        }

        // ---- the evaluation happens on a change only ----

        [Test]
        public void TheCharacteristics_AreComputedOncePerChangeOfAnInput_AndNeverOnARead()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            Assert.Equal(1, model.Recomputes, "once, when the model is made");

            // read everything the tab reads
            for (int i = 0; i < 20; i++)
            {
                GpuStats s = model.Stats;
                int n = model.Lines.Count + model.Warnings.Count + model.Segments.Count;
                n += model.ArchLabels().Count + model.BusLabels().Count + model.VramValues.Count;
                string t = model.CoDevText + model.DevelopBlockText + model.PartsSignature + model.MissingPartsText;
                Assert.True(n > 0 && s.Units > 0 && t != null);
            }
            Assert.Equal(1, model.Recomputes, "reading is free");

            model.SetDie(200f);
            Assert.Equal(2, model.Recomputes);
            model.SetClockPercent(80);
            model.SetVramIndex(0);
            model.SelectBus(0);
            model.SelectCooler(0);
            model.SelectNode(0);
            model.SelectArch(0);
            Assert.Equal(8, model.Recomputes, "one per change");
            model.SelectMemory(0);
            Assert.Equal(9, model.Recomputes, "a memory change is one change too");
            model.RefreshDevelopState();
            Assert.Equal(9, model.Recomputes, "the state of the button is not a recomputation");
        }

        [Test]
        public void AChangeOfAnInput_CostsLessThanAMillisecond()
        {
            // NFR-1 / TSK-024: the recomputation of the designer on the fake game (the services and the estimate against the market, no widgets).
            var world = UiWorlds.Ac31();
            world.Market.SeedForYear(2005);
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);
            for (int i = 0; i < 50; i++) model.SetDie(100f + i);   // warm up the code

            const int changes = 400;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < changes; i++) model.SetDie(100f + i % 200);
            watch.Stop();
            double perChange = watch.Elapsed.TotalMilliseconds / changes;
            Assert.True(perChange < 1.0, "a change of the die took " + perChange.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        }

        [Test]
        public void TheStatsOfTheModel_AreTheStatsOfTheDesignerService()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);
            GpuStats direct = world.Designer.Evaluate(model.Input);
            Assert.Equal(direct.Units, model.Stats.Units);
            Assert.Equal(direct.UnitCost, model.Stats.UnitCost, "the price on the screen is the price of the project");
            Assert.Equal(direct.ProjectCost, model.Stats.ProjectCost);
            Assert.Equal(direct.ProjectDays, model.Stats.ProjectDays);
        }

        // ---- the table by segment ----

        [Test]
        public void TheTable_HasARowPerOpenSegment_WithTheRatingAgainstTheMarket()
        {
            var world = UiWorlds.Ac31();
            world.Market.SeedForYear(2005);
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            Assert.Equal(1, model.Segments.Count);
            GpuSegmentRow row = model.Segments[0];
            Assert.Equal("consumer", row.SegmentId);
            Assert.Equal("Потребительские видеокарты", row.Name);
            Assert.True(row.Estimate.HasCompetitors, "the background card is there to compare with");
            Assert.True(row.RatingText.EndsWith(" %"), row.RatingText);
            Assert.Equal(world.Market.OpenSegments[0].Budget, row.Estimate.Budget);
            Assert.Equal("$" + GpuUiMath.Plain(world.Market.OpenSegments[0].Budget), row.BudgetText);

            model.SetDie(100f);
            float small = model.Segments[0].Estimate.RatingPercent;
            model.SetDie(300f);
            float big = model.Segments[0].Estimate.RatingPercent;
            Assert.True(big > small, "a larger die is a better card: " + big + " against " + small);
        }

        [Test]
        public void ACardAloneOnTheMarket_SaysSo_AndAMissingMemoryShowsThePenalty()
        {
            var world = UiWorlds.Ac31();   // the market is not seeded: no card on sale
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            GpuSegmentRow row = model.Segments[0];
            Assert.False(row.Estimate.HasCompetitors);
            Assert.Equal("один на рынке", row.RatingText);
            Assert.Equal(GpuTone.Good, row.RatingTone);

            float required = world.Market.OpenSegments[0].VramRequirementMB;
            Assert.True(required > 256f, "March 2005 asks for a little more than 256 MB: " + required);
            Assert.True(row.Estimate.VramFactor < 1f);
            Assert.True(row.VramText.StartsWith("×0.9"), "the penalty of the video memory: " + row.VramText);
            Assert.Equal(GpuTone.Warn, row.VramTone);

            model.SetVramIndex(UiWorlds.VramIndex(model, 1024f));
            Assert.Equal(1f, model.Segments[0].Estimate.VramFactor);
            Assert.True(model.Segments[0].VramText.StartsWith("достаточно"), model.Segments[0].VramText);
            Assert.Equal("все есть", model.Segments[0].FeaturesText);
        }

        [Test]
        public void WhenNoSegmentIsOpen_TheTableIsEmpty()
        {
            var world = UiWorlds.Ac31(new DateTime(1975, 5, 1));
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            Assert.Equal(0, model.Segments.Count);
        }

        // ---- developing ----

        [Test]
        public void Ac41_Developing_StartsTheProject_WithTheCostAndTheTimeOfTheExample()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);
            model.SetName("Vortex");

            Assert.True(model.CanDevelop);
            Assert.Equal(string.Empty, model.DevelopBlockText);
            Assert.Near(1.45e8, model.Stats.ProjectCost, 100.0, "(8e7 + 5e7 x 0.5 + 4e7) x 1.0");
            Assert.Equal(420, model.Stats.ProjectDays, "220 + 200 days");

            string reason;
            string id = model.Develop(out reason);
            Assert.NotNull(id, reason);
            Assert.Equal(1, world.Projects.Active.Count);
            GpuProjectSave project = world.Projects.Find(id);
            Assert.Near(1.45e8, project.totalCost, 100.0);
            Assert.Equal(420, project.days);
            Assert.Equal(0f, project.progress, "a project at 0 %");
            Assert.Equal("Vortex", world.Projects.Design(project.designId).name);
        }

        [Test]
        public void Ac44_AFablessCompany_PaysLess_AndTheScreenShowsIt()
        {
            var world = UiWorlds.Ac31();
            world.Game.Fabless = true;
            world.Game.Modifiers[GpuModifierKind.DevelopmentCost] = 0.67f;
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);
            Assert.Near(1.45e8 * 0.67, model.Stats.ProjectCost, 200.0, "AC-4.4: about 9.72e7");
            Assert.Equal("$" + GpuUiMath.Plain((float)model.Stats.ProjectCost), Line(model, "projectCost").Value);
        }

        [Test]
        public void TheButton_IsLockedAtTheLimitOfProjects_WithTheReason()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            UiWorlds.SetAc31(model);

            for (int i = 0; i < 3; i++)
            {
                string reason;
                Assert.NotNull(model.Develop(out reason), "project " + (i + 1) + ": " + reason);
            }
            Assert.False(model.CanDevelop, "FR-4 item 3: three at a time");
            Assert.Equal("Уже идёт наибольшее число проектов видеокарт.", model.DevelopBlockText);

            string refusal;
            Assert.Null(model.Develop(out refusal));
            Assert.Equal("Уже идёт наибольшее число проектов видеокарт.", refusal);
            Assert.Equal(3, world.Projects.Active.Count);

            world.Projects.Cancel(world.Projects.Active[0].id);
            model.RefreshDevelopState();
            Assert.True(model.CanDevelop, "a place is free again, with no recomputation");
        }

        [Test]
        public void TheDiscountOfTheCoDevelopment_IsHidden_WhenItDoesNotAct()
        {
            var world = UiWorlds.Ac31();
            GpuDesignerModel model = UiWorlds.Designer(world, new TestGpuText());
            Assert.Equal(string.Empty, model.CoDevText, "no CPU of the company on this node: the discount does not act (FR-17)");
            Assert.Near(1.0, model.Context.CoDevFactor, 1e-9);
        }
    }
}
