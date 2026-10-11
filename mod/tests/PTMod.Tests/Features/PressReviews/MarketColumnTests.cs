using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// The optional column "Press" in the market table of the CPU window (FR-13, TSK-020, a spike): what the cells say (the metascore or a dash), which review a CPU gets, and the
    /// rules the patch keeps - an optional postfix on <c>HardwareWindow.ShowMarket</c>, declared only when the player switched the column on before the start, the title added before
    /// any cell, nothing written when the rows of the game do not match the CPUs. The patch itself needs the game and is looked at by its source; the owner checks it in the game.
    /// </summary>
    [Covers(typeof(MarketColumn), typeof(MarketCell))]
    public class MarketColumnTests
    {
        private static ReviewRecord Review(int id, int score, string published = "1995-03-15")
        {
            return new ReviewRecord { cpuSaveId = id, metascore = score, publishedOn = published, companyKey = "player" };
        }

        // ---- the cells ----

        [Test]
        public void ACpuWithoutAReview_GetsADash_ThatSortsBelowEveryScore()
        {
            MarketCell cell = MarketColumn.CellFor(null);
            Assert.Equal("—", cell.Text);
            Assert.Equal(MarketColumn.NoScoreText, cell.Text);
            Assert.Equal(-1f, cell.Number);
            Assert.True(cell.Number < MarketColumn.CellFor(Review(1, 0)).Number, "even a flop is above 'not reviewed'");
        }

        [Test]
        public void ACpuWithAReview_GetsItsMetascore()
        {
            MarketCell cell = MarketColumn.CellFor(Review(1, 84));
            Assert.Equal("84", cell.Text);
            Assert.Equal(84f, cell.Number);
        }

        [Test]
        public void AScoreOutOfRange_IsHeldTo0And100()
        {
            Assert.Equal("100", MarketColumn.CellFor(Review(1, 140)).Text);
            Assert.Equal("0", MarketColumn.CellFor(Review(1, -5)).Text);
            Assert.Equal(0f, MarketColumn.CellFor(Review(1, -5)).Number);
        }

        [Test]
        public void TheColumn_IsNarrowerThanTheNarrowestFlexibleColumnItTakesRoomFrom()
        {
            // the fixed columns of the market table add up to 1145 pixels; the column of the press takes 70 of the space the column "Industries" fills
            Assert.Equal(70f, MarketColumn.Width);
            Assert.True(MarketColumn.Width <= 75f, "no wider than the game's own narrow columns (Year 60, Price 75)");
        }

        // ---- the review of a CPU ----

        [Test]
        public void TheLatestReviewOfACpu_IsFoundBySaveId()
        {
            var archive = new List<ReviewRecord>
            {
                Review(1, 60, "1995-01-10"),
                Review(2, 70, "1995-02-10"),
                Review(1, 80, "1995-06-10"),   // a second review of the same CPU (a damaged archive): the later wins
                null,
                Review(3, 50, "1995-03-10"),
                Review(3, 55, "1995-03-10"),   // the same day: the later in the archive wins
            };

            Dictionary<int, ReviewRecord> latest = MarketColumn.LatestByCpu(archive);

            Assert.Equal(3, latest.Count);
            Assert.Equal(80, latest[1].metascore);
            Assert.Equal(70, latest[2].metascore);
            Assert.Equal(55, latest[3].metascore);
            Assert.Equal(0, MarketColumn.LatestByCpu(null).Count);
        }

        // ---- the rows of the game ----

        [Test]
        public void TheRowsAreMatchedWithTheCpus_OnlyWhenTheCountIsTheSame()
        {
            Assert.True(MarketColumn.RowsMatch(0, 0), "an empty table");
            Assert.True(MarketColumn.RowsMatch(50, 50));
            Assert.False(MarketColumn.RowsMatch(49, 50), "the game lists the CPUs another way: no cell is written");
            Assert.False(MarketColumn.RowsMatch(50, 12));
            Assert.False(MarketColumn.RowsMatch(-1, -1));
        }

        // ---- the patch, by its source ----

        private static string Code(params string[] path)
        {
            string file = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews", Path.Combine(path));
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        [Test]
        public void ThePatch_IsAnOptionalPostfixOnShowMarket_AndTheOnlyPatchOfTheFeature()
        {
            string patch = Code("UI", "MarketColumnPatch.cs");
            Assert.True(Regex.IsMatch(patch, @"\[ModPatch\(typeof\(GameHardwareWindow\),\s*""ShowMarket"",\s*PatchKind\.Postfix,\s*Optional\s*=\s*true\)\]"), "an optional postfix on HardwareWindow.ShowMarket");
            Assert.True(patch.Contains("using GameHardwareWindow = ProcessorTycoon.Desktop.Hardware.HardwareWindow;"));
            Assert.Equal(1, Regex.Matches(patch, @"\[ModPatch\(").Count, "one patch in the class");

            int others = 0;
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews"), ".cs"))
            {
                if (Path.GetFileName(path) == "MarketColumnPatch.cs") continue;
                others += Regex.Matches(SourceScan.Strip(SourceScan.ReadText(path), true), @"\[\s*ModPatch\s*\(").Count;
            }
            Assert.Equal(0, others, "no other patch in the feature");
        }

        [Test]
        public void ThePatch_IsDeclaredOnlyWhenTheColumnWasOnAtStart_AndTheColumnIsOffByDefault()
        {
            string feature = Code("PressReviewsFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"DeclareParam\(ctx,\s*MarketColumnParam,\s*""press\.param\.marketColumn"",\s*false\)"), "the parameter is off by default");
            Assert.True(Regex.IsMatch(feature, @"MarketColumnRegistered\s*=\s*ModSettings\s*\.\s*GetBool\(FeatureId,\s*MarketColumnParam\)\s*;\s*if\s*\(\s*MarketColumnRegistered\s*\)\s*ctx\s*\.\s*Patch\(typeof\(MarketColumnPatch\)\)"),
                "ctx.Patch(typeof(MarketColumnPatch)) only inside the if of the flag");
            Assert.Equal(1, Regex.Matches(feature, @"ctx\s*\.\s*Patch\s*\(").Count, "the one place that declares a patch");
        }

        [Test]
        public void ThePatch_AddsTheTitleBeforeAnyCell_AndAsksTheTableToLayItselfOutAgain()
        {
            string patch = Code("UI", "MarketColumnPatch.cs");
            int title = patch.IndexOf("EnsureTitle(sheet);", System.StringComparison.Ordinal);
            int firstCell = patch.IndexOf("rows[i].Extend(", System.StringComparison.Ordinal);
            Assert.True(title > 0 && firstCell > title, "the title is added before the first cell: a row with a cell and no width would make the game's own UpdateRows throw");
            Assert.True(patch.Contains("sheet.AddTitle(ModL10n.L(\"press.market.title\"), MarketColumn.Width, GameDataType.Number,"), "a numeric title with the width of the column");
            Assert.True(Regex.IsMatch(patch, @"sheet\s*\.\s*UpdateRows\s*\(\s*\)"), "the widths, styles and sorting reach the new cells");
            Assert.True(Regex.IsMatch(patch, @"Extend\(cell\.Text,\s*cell\.Number,\s*float\.NegativeInfinity,\s*HorizontalAlignmentOptions\.Left,\s*false\)"), "the arguments of the game's own cells (no growth arrow, left)");
        }

        [Test]
        public void ThePatch_WritesNothingWhenTheGamesTableIsNotWhatItExpects_AndRunsGuarded()
        {
            string patch = Code("UI", "MarketColumnPatch.cs");
            Assert.True(patch.Contains("ModHooks.Guard(FeatureId, Site,"), "an exception is the core's to count, the game is not touched");
            Assert.True(patch.Contains("!ModFeatures.IsActive(FeatureId)"), "a feature that is off writes nothing");
            Assert.True(patch.Contains("MarketColumn.RowsMatch(active, cpus.Count)"), "rows and CPUs are matched by count first");
            Assert.True(Regex.IsMatch(patch, @"if \(RowsField == null \|\| CurrentRowField == null \|\| TopTitlesField == null\)"), "a member of the table that is missing switches the column off");
            Assert.True(patch.Contains("WarnOnce("), "with one warning, not one per refresh");
            Assert.True(patch.Contains("CpusOrderedByTotalSales(playerOnly, includeRetired)") && patch.Contains("TableLimit = 50"), "the CPUs are listed as ShowMarket lists them");
            Assert.True(patch.Contains("ReviewEffects.ReviewedSaveIdOf("), "a variant shows the review of its base design");
        }
    }
}
