using System;
using System.Collections.Generic;
using PTMod.Core;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The one place that makes the report of the modifiers panel and its tooltip from the game (FR-5): reads the player's company
    /// (<see cref="ModifierSnapshotReader"/>), asks the blocks other features registered (<c>IModifierLineSource</c>, through the seam of the feature)
    /// and hands both to the pure <see cref="ModifierReportBuilder"/>. The panel and the tooltip call it, so they show the same lines. The time of a
    /// build goes to the debug log (NFR-1: 1 ms). Never throws: a failure gives null and the caller shows its line "cannot read".
    /// </summary>
    internal static class ModifierReports
    {
        /// <summary>The report for the player's company now; null when the company cannot be read.</summary>
        public static ModifierReport Build(ResearchTreeUxFeature feature)
        {
            long started = PerfProbe.Begin();
            try
            {
                GameCompany company = PlayerCompany();
                if (company == null) return null;

                ModifierSnapshot snapshot = ModifierSnapshotReader.Read(company);
                if (snapshot == null) return null;

                IReadOnlyList<ExtraModifierGroup> extra = feature == null ? null : feature.ModifierGroupsFor(company);
                ModifierReport report = ModifierReportBuilder.Build(snapshot, extra, GameTipFormat.Instance);

                if (started != 0L) PerfProbe.End(PerfOp.ModifiersReport, started, "modifiers: report built (" + report.Groups.Count + " blocks)");
                return report;
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "modifiers: the report cannot be built", e);
                return null;
            }
        }

        /// <summary>The tooltip of the button "Modifiers": the report as a header and a body; a line "cannot read" when there is no report.</summary>
        public static TipText Tooltip(ResearchTreeUxFeature feature)
        {
            ModifierReport report = Build(feature);
            if (report == null) return new TipText(ModL10n.L("rtux.mod.title"), ModL10n.L("rtux.mod.panel.error"));
            return report.ToTooltip(GameTipFormat.Instance);
        }

        /// <summary>
        /// Turns the blocks of other features into the pure copies the builder takes: the heading and every label are translated here, with the
        /// string tables of the feature that owns the key. A block or a line without a key is dropped.
        /// </summary>
        public static List<ExtraModifierGroup> ConvertGroups(IReadOnlyList<ModifierLineGroup> groups)
        {
            var result = new List<ExtraModifierGroup>();
            if (groups == null) return result;

            foreach (ModifierLineGroup group in groups)
            {
                if (group == null || group.Lines == null || group.Lines.Count == 0) continue;

                var converted = new ExtraModifierGroup { Title = string.IsNullOrEmpty(group.TitleKey) ? string.Empty : ModL10n.L(group.TitleKey) };
                foreach (ModifierLine line in group.Lines)
                {
                    if (line == null || string.IsNullOrEmpty(line.LabelKey)) continue;

                    converted.Lines.Add(new ExtraModifierLine
                    {
                        Label = ModL10n.L(line.LabelKey, line.Args ?? new object[0]),
                        Multiplier = line.Multiplier,
                        Capped = line.Capped,
                        SourceFeatureId = line.SourceFeatureId,
                    });
                }
                if (converted.Lines.Count > 0) result.Add(converted);
            }
            return result;
        }

        private static GameCompany PlayerCompany()
        {
            GamePlayer player = GamePlayer.Instance;
            return player == null ? null : player.Company;
        }
    }
}
