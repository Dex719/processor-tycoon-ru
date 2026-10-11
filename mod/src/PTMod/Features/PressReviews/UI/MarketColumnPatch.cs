using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameDataType = ProcessorTycoon.SpreadsheetSystem.DataType;
using GameHardwareWindow = ProcessorTycoon.Desktop.Hardware.HardwareWindow;
using GameSpreadsheet = ProcessorTycoon.SpreadsheetSystem.Spreadsheet;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The optional column "Press" of the market table of the CPU window (FR-13, TSK-020): a postfix on <c>HardwareWindow.ShowMarket</c> (the private method
    /// that fills the table <c>spreadsheetMarket</c> with the 50 best-selling CPUs) writes the metascore of each CPU, or a dash, into one more column at the end.
    /// The patch is <b>optional and off</b>: the feature declares it only when the parameter <c>marketColumn</c> was on at the start of the game
    /// (<c>PressReviewsFeature.Register</c>, so a change of the parameter takes effect after a restart), and a failure to apply it is a warning, not the end of the
    /// feature. The cells and the rule of the review they show are <see cref="MarketColumn"/> (pure, with the tests); this class only reads the game's table.
    /// <para>
    /// <b>Spike (TSK-020), from the decompiled game, not from the running game.</b> What is known:
    /// <list type="bullet">
    /// <item>The headers are not baked into a prefab: <c>HardwareWindow.InitializeSpreadsheets</c> adds every title with the public
    /// <c>Spreadsheet.AddTitle(text, width, dataType, height)</c>, so a title can be added the same way later; it must come <i>before</i> any row has the new cell,
    /// because <c>Row.SetWidths</c> reads <c>columnWidths[i]</c> for every cell of a row (a cell without a width would throw inside the game's own <c>UpdateRows</c>).
    /// This patch adds the title first and the cells after it, and a cell that was not added is harmless (the loop runs over the row's own cells).</item>
    /// <item>A cell is added with the public <c>Row.Extend(text, number, growth, alignment, overflow)</c>. A row is reused after <c>Clear()</c>: its cells are then edited by
    /// position, so the 12th <c>Extend</c> of a row edits the column the first call created. Rows keep the order of the CPUs the table lists
    /// (<c>CpusOrderedByTotalSales</c>, at most 50 unless "player only"); the sorting by a column only reorders the objects on screen. The CPUs are worked out here
    /// again in the same way and the rows are matched only when their count is the same (<see cref="MarketColumn.RowsMatch"/>); otherwise nothing is written.</item>
    /// <item>The table is sorted by a click on a title (<c>DataType.Number</c> uses the number given to <c>Extend</c>); the new title does that too.</item>
    /// </list>
    /// What cannot be known offline, and is for the owner to look at in the game (manual check, <c>mod/requests/press-reviews.md</c>): whether the 70 pixels of the
    /// new column leave the flexible column "Industries" wide enough (the width of the table is the scene's <c>masterParent</c>, not in the dump; the fixed columns add
    /// up to 1145), and whether the group of the title row positions a title added after the first layout (it is switched on and rebuilt here once, and the game may
    /// switch it off again on the next <c>Clear</c> - harmless, the positions stay). The decision recorded for design.md "Open Issues": the patch is implemented, off by
    /// default, and becomes the default only after that check.
    /// </para>
    /// The private members of the game are reached in two ways: the patch method asks Harmony for the fields of the window by name (<c>___spreadsheetMarket</c>,
    /// <c>___showPlayerOnly</c>, <c>___showRetiredToggle</c>; a missing field makes the patch fail to apply, which an optional patch survives) and the table's own
    /// <c>rows</c>, <c>currentRow</c> and <c>topTitles</c> are read with reflection (a missing one switches the column off with one warning).
    /// </summary>
    internal static class MarketColumnPatch
    {
        private const string FeatureId = PressReviewsFeature.FeatureId;
        private const string Site = "market/column";
        private const float TitleHeight = 30f;   // the height of every title of the game's tables
        private const int TableLimit = 50;       // HardwareWindow.ShowMarket takes the first 50 CPUs unless "player only" is on
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo RowsField = typeof(GameSpreadsheet).GetField("rows", Private);
        private static readonly FieldInfo CurrentRowField = typeof(GameSpreadsheet).GetField("currentRow", Private);
        private static readonly FieldInfo TopTitlesField = typeof(GameSpreadsheet).GetField("topTitles", Private);

        private static GameSpreadsheet titled;   // the table that has the title already (a new scene has a new table)
        private static bool warned;

        [ModPatch(typeof(GameHardwareWindow), "ShowMarket", PatchKind.Postfix, Optional = true)]
        private static void ShowMarketPostfix(GameHardwareWindow __instance, GameSpreadsheet ___spreadsheetMarket, bool ___showPlayerOnly, ToggleUI ___showRetiredToggle)
        {
            if (__instance == null || ___spreadsheetMarket == null || !ModFeatures.IsActive(FeatureId)) return;

            bool retired = ___showRetiredToggle != null && ___showRetiredToggle.IsOn;
            ModHooks.Guard(FeatureId, Site, () => Extend(___spreadsheetMarket, ___showPlayerOnly, retired));
        }

        private static void Extend(GameSpreadsheet sheet, bool playerOnly, bool includeRetired)
        {
            if (RowsField == null || CurrentRowField == null || TopTitlesField == null)
            {
                WarnOnce("the table of the game has other members than the patch expects (rows, currentRow, topTitles); the column is off");
                return;
            }

            var rows = RowsField.GetValue(sheet) as List<GameSpreadsheet.Row>;
            object current = CurrentRowField.GetValue(sheet);
            if (rows == null || !(current is int))
            {
                WarnOnce("the rows of the table could not be read; the column is off");
                return;
            }

            int active = (int)current;
            List<Cpu> cpus = ListedCpus(playerOnly, includeRetired);
            if (cpus == null || active > rows.Count || !MarketColumn.RowsMatch(active, cpus.Count))
            {
                WarnOnce("the rows of the table (" + active + ") do not match the CPUs it should list (" + (cpus == null ? "?" : cpus.Count.ToString()) + "); the column is not written");
                return;
            }

            EnsureTitle(sheet);

            Dictionary<int, ReviewRecord> reviews = MarketColumn.LatestByCpu(PressSaveAdapter.Instance.State.reviews);
            for (int i = 0; i < active; i++)
            {
                ReviewRecord review;
                reviews.TryGetValue(ReviewEffects.ReviewedSaveIdOf(cpus[i]), out review);

                MarketCell cell = MarketColumn.CellFor(review);
                rows[i].Extend(cell.Text, cell.Number, float.NegativeInfinity, HorizontalAlignmentOptions.Left, false);
            }
            sheet.UpdateRows();   // the widths, the styles and the sorting reach the new cells
        }

        /// <summary>The CPUs <c>ShowMarket</c> listed, in its order: the best sellers, 50 of them unless the table shows only the player's CPUs.</summary>
        private static List<Cpu> ListedCpus(bool playerOnly, bool includeRetired)
        {
            CpuDataProvider provider = CpuDataProvider.Instance;
            if (provider == null) return null;

            List<Cpu> all = provider.CpusOrderedByTotalSales(playerOnly, includeRetired);
            if (all == null) return null;
            return !playerOnly && all.Count > TableLimit ? all.GetRange(0, TableLimit) : all;
        }

        /// <summary>Adds the title of the column once per table, before any row gets its cell.</summary>
        private static void EnsureTitle(GameSpreadsheet sheet)
        {
            if (ReferenceEquals(titled, sheet)) return;

            sheet.AddTitle(ModL10n.L("press.market.title"), MarketColumn.Width, GameDataType.Number, TitleHeight);
            titled = sheet;

            try
            {
                // The group that lays the titles out may have been switched off after the first layout: switch it on and lay the new title out now.
                var topTitles = TopTitlesField.GetValue(sheet) as GameObject;
                if (topTitles == null) return;

                HorizontalLayoutGroup group = topTitles.GetComponent<HorizontalLayoutGroup>();
                var rect = topTitles.GetComponent<RectTransform>();
                if (group == null || rect == null) return;

                group.enabled = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
            catch (Exception ex)
            {
                ModLog.Warn(FeatureId, "market column: the layout of the titles could not be refreshed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            ModLog.Warn(FeatureId, "market column: " + message);
        }
    }
}
