using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The small pieces the windows are built from (TSK-022...TSK-026): the tabs and what is kept of the last one in the save, the ranges of the sliders (both ends on
    /// the grid, so a round value like 300 mm2 or the highest clock can be reached), the video memory in powers of two, the loss to memory, spans of time in plural
    /// words (AC-2.2: about ten years), the number a player types in the price field, and the words of the tables of the feature.
    /// </summary>
    [Covers(typeof(GpuTabs), typeof(GpuTab), typeof(GpuSliderRange), typeof(GpuUiMath), typeof(GpuTone), typeof(IGpuText), typeof(GpuNames))]
    public class GpuUiBasicsTests
    {
        // ---- tabs ----

        [Test]
        public void TheTabs_AreFour_InTheOrderOfTheBar_WithTheirCaptions()
        {
            Assert.Equal(new[] { GpuTab.Research, GpuTab.Designer, GpuTab.Lineup, GpuTab.Market }, GpuTabs.All);
            var ru = new TestGpuText("ru");
            var en = new TestGpuText("en");
            string[] expectedRu = { "Исследования", "Конструктор", "Линейка", "Рынок" };
            string[] expectedEn = { "Research", "Designer", "Lineup", "Market" };
            for (int i = 0; i < GpuTabs.All.Length; i++)
            {
                Assert.Equal(expectedRu[i], ru.L(GpuTabs.TitleKey(GpuTabs.All[i])));
                Assert.Equal(expectedEn[i], en.L(GpuTabs.TitleKey(GpuTabs.All[i])));
            }
        }

        [Test]
        public void TheLastTab_IsKeptAsANumber_AndAnythingElseIsTheFirstTab()
        {
            foreach (GpuTab tab in GpuTabs.All) Assert.Equal(tab, GpuTabs.FromStored(GpuTabs.ToStored(tab)), "a tab survives the save");

            Assert.Equal(0, GpuTabs.ToStored(GpuTab.Auto));
            Assert.Equal(GpuTab.Research, GpuTabs.FromStored(0), "never stored");
            Assert.Equal(GpuTab.Research, GpuTabs.FromStored(99), "a save of another version");
            Assert.Equal(GpuTab.Research, GpuTabs.FromStored(-3));
            Assert.Equal(GpuTab.Lineup, GpuTabs.Resolve(GpuTab.Auto, 3), "Auto is the tab last open");
            Assert.Equal(GpuTab.Market, GpuTabs.Resolve(GpuTab.Market, 1), "a tab that is asked for wins");
            Assert.Equal("lastTab", GpuTabs.FlagKey);
        }

        // ---- sliders ----

        [Test]
        public void ASliderRange_HasBothEndsOnTheGrid_AndAtMostTheAskedPositions()
        {
            GpuSliderRange die = GpuSliderRange.Nice(20f, 350f, 240);
            Assert.Equal(20f, die.Min);
            Assert.Equal(350f, die.Max);
            Assert.Equal(2f, die.Step);
            Assert.True(die.Positions <= 240, "positions: " + die.Positions);
            Assert.Equal(300f, die.Snap(300f), "AC-3.1: 300 mm2 can be reached");

            GpuSliderRange wide = GpuSliderRange.Nice(20f, 850f, 240);
            Assert.Equal(5f, wide.Step);
            Assert.Equal(300f, wide.Snap(300f));
            Assert.Equal(850f, wide.Snap(10000f), "held to the end");
            Assert.Equal(20f, wide.Snap(-5f), "held to the start");

            GpuSliderRange units = GpuSliderRange.Nice(0f, 10000f, 100);
            Assert.True(units.Positions <= 100);
            Assert.Equal(0f, units.Min);
            Assert.Equal(10000f, units.Max, "the end is a multiple of the step (200)");
        }

        [Test]
        public void ASliderRange_OfOneValueOrOfGarbage_IsStillUsable()
        {
            GpuSliderRange point = GpuSliderRange.Nice(50f, 50f, 100);
            Assert.Equal(50f, point.Min);
            Assert.Equal(50f, point.Max);
            Assert.Equal(1, point.Positions);

            GpuSliderRange reversed = GpuSliderRange.Nice(100f, 10f, 100);
            Assert.Equal(100f, reversed.Min, "an empty range is one position at the low end");
            Assert.Equal(100f, reversed.Max);

            GpuSliderRange nan = GpuSliderRange.Nice(float.NaN, float.PositiveInfinity, 100);
            Assert.False(float.IsNaN(nan.Min) || float.IsInfinity(nan.Max));
            Assert.Equal(nan.Min, nan.Snap(float.NaN), "NaN snaps to the start");
        }

        [Test]
        public void TheVideoMemoryOnOffer_IsPowersOfTwo_UpToTheLimitOfTheMemory()
        {
            List<float> gddr3 = GpuUiMath.PowersOfTwo(1024f, 12, 0.0625f);
            Assert.Equal(12, gddr3.Count);
            Assert.Equal(0.5f, gddr3[0]);
            Assert.Equal(1024f, gddr3[gddr3.Count - 1]);
            Assert.True(gddr3.Contains(256f), "AC-3.1 uses 256 MB");

            List<float> dram = GpuUiMath.PowersOfTwo(0.25f, 12, 0.0625f);
            Assert.Equal(new[] { 0.0625f, 0.125f, 0.25f }, dram.ToArray(), "the oldest memory: nothing below 64 KB");

            Assert.Equal(8192f, GpuUiMath.PowersOfTwo(12288f, 3, 0.0625f)[2], "a limit that is no power of two gives the one below it");
            Assert.Equal(0, GpuUiMath.PowersOfTwo(0f, 12, 0.0625f).Count);
            Assert.Equal(0, GpuUiMath.PowersOfTwo(float.NaN, 12, 0.0625f).Count);
            Assert.Equal(0, GpuUiMath.PowersOfTwo(0.01f, 12, 0.0625f).Count, "below the floor");
        }

        [Test]
        public void TheNearestAndTheFirstEnoughVideoMemory_AreFound()
        {
            var values = new List<float> { 1f, 2f, 4f, 8f, 16f };
            Assert.Equal(2, GpuUiMath.NearestIndex(values, 5f), "4 is nearer to 5 than 8 on the log scale");
            Assert.Equal(3, GpuUiMath.NearestIndex(values, 6f));
            Assert.Equal(0, GpuUiMath.NearestIndex(values, 0.001f));
            Assert.Equal(4, GpuUiMath.NearestIndex(values, 1000f));
            Assert.Equal(-1, GpuUiMath.NearestIndex(new List<float>(), 3f));

            Assert.Equal(2, GpuUiMath.FirstAtLeast(values, 3f));
            Assert.Equal(2, GpuUiMath.FirstAtLeast(values, 4f));
            Assert.Equal(4, GpuUiMath.FirstAtLeast(values, 100f), "more than there is: the largest");
            Assert.Equal(-1, GpuUiMath.FirstAtLeast(null, 3f));
        }

        // ---- the loss to memory (AC-3.2) ----

        [Test]
        public void TheLossToMemory_IsOneMinusTheRatioToTheExponent()
        {
            Assert.Near(0.117, GpuUiMath.BandwidthLoss(0.837f, 0.05f, 0.7f), 0.001, "AC-3.2: 0.837^0.7 = 0.883, a loss of 12 %");
            Assert.Equal(0f, GpuUiMath.BandwidthLoss(1f, 0.05f, 0.7f), "memory keeps up");
            Assert.Equal(0f, GpuUiMath.BandwidthLoss(3.5f, 0.05f, 0.7f), "memory to spare");
            Assert.Equal(0f, GpuUiMath.BandwidthLoss(float.NaN, 0.05f, 0.7f));

            float floor = GpuUiMath.BandwidthLoss(0.001f, 0.05f, 0.7f);
            Assert.Near(1d - Math.Pow(0.05, 0.7), floor, 1e-5, "the floor of the ratio holds the loss");
        }

        // ---- spans ----

        [Test]
        public void ASpan_IsWrittenInDays_ThenInWholeYears_WithTheRightPluralForm()
        {
            var ru = new TestGpuText("ru");
            Assert.Equal("1 день", GpuUiMath.Span(ru, 1));
            Assert.Equal("2 дня", GpuUiMath.Span(ru, 2));
            Assert.Equal("5 дней", GpuUiMath.Span(ru, 5));
            Assert.Equal("21 день", GpuUiMath.Span(ru, 21));
            Assert.Equal("200 дней", GpuUiMath.Span(ru, 200));
            Assert.Equal("548 дней", GpuUiMath.Span(ru, GpuUiMath.MaxDaysSpan));
            Assert.Equal("2 года", GpuUiMath.Span(ru, GpuUiMath.MaxDaysSpan + 1), "a year and a half and more: years");
            Assert.Equal("10 лет", GpuUiMath.Span(ru, 3524), "AC-2.2: about ten years");
            Assert.Equal("—", GpuUiMath.Span(ru, -1), "the research does not move");

            var en = new TestGpuText("en");
            Assert.Equal("1 day", GpuUiMath.Span(en, 1));
            Assert.Equal("2 days", GpuUiMath.Span(en, 2));
            Assert.Equal("10 years", GpuUiMath.Span(en, 3524));
            Assert.Equal(0, ru.Warnings.Count + en.Warnings.Count, "no key is missing: " + string.Join("; ", ru.Warnings.ToArray()));
        }

        // ---- names ----

        [Test]
        public void TheNames_ComeFromTheKeysOfTheContent_AndFallBackToTheId()
        {
            GpuContentDb content = GpuWorld.Shipped();
            var ru = new TestGpuText("ru");
            Assert.Equal("gpu.arch.3d", GpuNames.PartKey(content, "arch.3d"));
            Assert.Equal("gpu.mem.gddr3", GpuNames.PartKey(content, "vram.gddr3"));
            Assert.Equal("gpu.cool.fan", GpuNames.PartKey(content, "cool.fan"));
            Assert.Null(GpuNames.PartKey(content, "arch.unknown"));
            Assert.Null(GpuNames.PartKey(null, "arch.3d"));

            Assert.Equal("3D-ускоритель", GpuNames.Part(content, ru, "arch.3d"));
            Assert.Equal("arch.unknown", GpuNames.Part(content, ru, "arch.unknown"), "an id nobody knows is shown as it is");
            Assert.Equal("SGRAM", GpuNames.Node(content, ru, "vram.sgram"), "a node is named after the part it opens");
            Assert.Equal("nope", GpuNames.Node(content, ru, "nope"));
            Assert.Equal("2D-ускоритель, Ускоритель интерфейса", GpuNames.Nodes(content, ru, new[] { "arch.2d", "arch.gui" }));
            Assert.Equal(string.Empty, GpuNames.Nodes(content, ru, null));
            Assert.Equal("Потребительские видеокарты", GpuNames.Segment(content, ru, "consumer"));
            Assert.Equal("ghost", GpuNames.Segment(content, ru, "ghost"));
            Assert.Equal("3D", GpuNames.Feature(ru, "3d"));
            Assert.Equal("2D", GpuNames.Feature(ru, "2d"));
            Assert.Equal("someday", GpuNames.Feature(ru, "someday"), "a feature with no text is shown as it is");
            Assert.Equal("3D, шейдеры", GpuNames.Features(ru, new[] { "3d", "shader" }));
            Assert.Equal("fallback", GpuNames.Localized(ru, "gpu.no.such.key", "fallback"));
            Assert.Equal("fallback", GpuNames.Localized(ru, null, "fallback"));

            Assert.Equal("gpu.research.branch.architecture", GpuNames.BranchKey(GpuContentDb.BranchArchitecture));
            Assert.Equal("gpu.research.branch.memory", GpuNames.BranchKey(GpuContentDb.BranchMemory));
            Assert.Equal("gpu.research.branch.cooling", GpuNames.BranchKey(GpuContentDb.BranchCooling));
            Assert.Equal("gpu.lineup.status.ready", GpuNames.StatusKey(GpuState.StatusReady));
            Assert.Equal("gpu.lineup.status.selling", GpuNames.StatusKey(GpuState.StatusSelling));
            Assert.Equal("gpu.lineup.status.retired", GpuNames.StatusKey(GpuState.StatusRetired));
            Assert.Equal(0, ru.Warnings.Count, "a name that the tables lack is not looked up: " + string.Join("; ", ru.Warnings.ToArray()));
        }

        // ---- what the player types ----

        [Test]
        public void ANumberTyped_ToleratesSpacesSignsAndBothSeparators()
        {
            float value;
            Assert.True(GpuUiMath.TryParseNumber("249", out value));
            Assert.Equal(249f, value);
            Assert.True(GpuUiMath.TryParseNumber("1 249,5", out value));
            Assert.Equal(1249.5f, value);
            Assert.True(GpuUiMath.TryParseNumber("1,249.5", out value));
            Assert.Equal(1249.5f, value);
            Assert.True(GpuUiMath.TryParseNumber("$1,249", out value));
            Assert.Equal(1249f, value, "a lone comma before three digits groups thousands");
            Assert.True(GpuUiMath.TryParseNumber("12,5", out value));
            Assert.Equal(12.5f, value, "a lone comma before one or two digits is a decimal point");
            Assert.True(GpuUiMath.TryParseNumber("1.249,5", out value));
            Assert.Equal(1249.5f, value);
            Assert.True(GpuUiMath.TryParseNumber("-5", out value));
            Assert.Equal(-5f, value, "the sign is the caller's business");

            foreach (string bad in new[] { null, string.Empty, "abc", "  ", ".", "-", "--" })
            {
                Assert.False(GpuUiMath.TryParseNumber(bad, out value), "not a number: " + (bad ?? "null"));
            }
            Assert.Equal("249", GpuUiMath.Plain(249f));
            Assert.Equal("12.5", GpuUiMath.Plain(12.5f));
            Assert.Equal("0.33", GpuUiMath.Plain(1f / 3f));
        }
    }
}
