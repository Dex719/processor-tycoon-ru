using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The tab "Automation preview" of the log window (design.md, Component 11; FR-9): what the switch "Auto-sign contracts" of the game would do on 31 December with the threshold of the slider as it
    /// is now, pair by pair, without sending anything. The decisions are those of <see cref="AutomationLogger.Preview"/> (the rule of the log of a real run), counted and sorted by
    /// <see cref="PreviewLines"/>: the offers that would go out first, then the pairs skipped by margin, then the ones the game would turn down.
    /// <list type="bullet">
    /// <item>The header says when the next run is and in how many days, the threshold in force and the counts; when the switch is off it says nothing will be sent (the rows are then the decisions of a run
    /// with the switch on).</item>
    /// <item>"Refresh" works the preview out again from the slider's current value (<c>Player.ContractProfitMargin</c>), so the player moves the slider, comes back and presses it (FR-9 item 2).</item>
    /// <item>The list shows at most <see cref="PreviewLines.DefaultMaxRows"/> rows; the rest is counted in one line.</item>
    /// </list>
    /// To check in the game (the owner, M5): with three pairs at 55, 38 and 20 % margin and a threshold of 40 % one offer is "will be sent" and two are "skipped by margin"; after moving the slider to 35 %
    /// and "Refresh" two are "will be sent"; on 31 December the log of the run says the same.
    /// </summary>
    internal static class AutomationPreviewPanel
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const float RefreshWidth = 150f;

        private static RectTransform _header;
        private static RectTransform _rows;

        /// <summary>Builds the tab into <paramref name="body"/> (already empty) and fills it for the state of the game now.</summary>
        internal static void Build(RectTransform body)
        {
            _header = ModUI.VerticalList(body, 4f);

            RectTransform buttonRow = UiKit.Row(body, 34f, 8f);
            ButtonUI refresh = ModUI.Button(buttonRow, ModL10n.L("ct.preview.refresh"), Fill, ButtonStyle.Standard, ModL10n.L("ct.preview.refresh.tip"));
            UiKit.SetWidth(refresh, RefreshWidth, 0f);

            _rows = UiKit.Scroll(body, 120f, 280f, 1f);
            Fill();
        }

        /// <summary>The tab is gone (the window was rebuilt or the scene left): the containers are dropped.</summary>
        internal static void Forget()
        {
            _header = null;
            _rows = null;
        }

        /// <summary>Works the preview out again and redraws it.</summary>
        internal static void Fill()
        {
            if (_header == null || _rows == null) return;

            AutomationPreview preview = AutomationLogger.Preview();
            string next = preview.NextRun == default(DateTime) ? string.Empty : DateText(preview.NextRun);
            PreviewPlan plan = PreviewLines.Build(preview.Rows, preview.AutomationOn, preview.MarginPercent, next, preview.DaysUntilRun, PreviewLines.DefaultMaxRows);

            UiKit.Clear(_header);
            UiKit.Clear(_rows);
            GameArgFormatter formatter = GameArgFormatter.Current();
            UiKit.AddLines(_header, plan.Header, formatter);

            UiKit.AddLines(_rows, plan.Rows, formatter);
            if (plan.Hidden > 0) UiKit.AddLine(_rows, new PanelLine("ct.preview.hidden", LineTone.Muted, PanelArg.Whole(plan.Hidden)), formatter);
            if (plan.Rows.Count == 0) UiKit.AddLine(_rows, new PanelLine("ct.preview.empty", LineTone.Muted), formatter);
        }

        private static string DateText(DateTime date)
        {
            try
            {
                return GameFormatter.DateToString(date);
            }
            catch (Exception)
            {
                return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
