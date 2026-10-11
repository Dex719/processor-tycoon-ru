using System;
using System.Diagnostics;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Integration;
using PTMod.Features.ScenariosAchievements.Logic;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The section "Companies compared" of a window (TSK-025, TSK-028; FR-9 item 2, FR-17, NFR-1): the heading, the table of the player's company and up to six AI companies, and the
    /// note about what the columns mean. The results of a scenario and the summary of the career show the same table, so it is made here once. The table is built when the window opens
    /// (never on a tick) and takes a few milliseconds; a build over 50 ms (NFR-1) is one line in the log per session. A table that cannot be built (the scene's singletons are not
    /// there) leaves the rest of the window as it is. Main thread only.
    /// </summary>
    internal static class ComparisonSection
    {
        private const long TableBudgetMs = 50;

        private static bool _slowTableReported;

        /// <summary>Adds the section to <paramref name="page"/>; adds nothing but a heading when the table cannot be built, and nothing at all while there is no director.</summary>
        internal static void Add(Transform page, IUiTexts texts)
        {
            RunDirector director = RunDirector.Instance;
            if (director == null) return;

            UiKit.Heading(page, texts.Text("scn.result.table"));
            var timer = Stopwatch.StartNew();
            ComparisonRow[] rows = null;
            try
            {
                rows = director.BuildComparison();
            }
            catch (Exception ex)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "results: the table of the companies could not be built: " + ex.Message);
            }
            if (rows == null) return;

            UiKit.ComparisonTable(page, ComparisonTableBuilder.Header(texts), ComparisonTableBuilder.Rows(rows, texts));
            UiKit.Line(page, texts.Text("scn.table.note"), UiTone.Muted);

            timer.Stop();
            if (timer.ElapsedMilliseconds > TableBudgetMs && !_slowTableReported)
            {
                _slowTableReported = true;
                ModLog.Warn(ScenariosFeature.FeatureId, "results: the table of the companies took " + timer.ElapsedMilliseconds + " ms (the budget is " + TableBudgetMs + " ms)");
            }
        }
    }
}
