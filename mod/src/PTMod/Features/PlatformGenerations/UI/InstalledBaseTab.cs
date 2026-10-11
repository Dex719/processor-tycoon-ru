using System;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The tab "Установленная база" (FR-9 item 3): the table of the installed base, the largest row first - the name of the socket (<c>Package.Name</c> or
    /// <c>CustomSocket.Name</c>), whether it is a standard or the company's own, the company, the base in units, its share of the whole base and how long the
    /// company has had the socket. A list "Мои / Все" above the table keeps the player's rows or all of them (the choice is kept while the game runs). The
    /// rows are <see cref="InstalledBaseView"/>'s (pure, tested); this class only lays them out.
    /// </summary>
    internal static class InstalledBaseTab
    {
        private const float SocketWidth = 200f;
        private const float KindWidth = 100f;
        private const float CompanyWidth = 160f;
        private const float UnitsWidth = 80f;
        private const float ShareWidth = 70f;
        private const float AgeWidth = 70f;
        private const float RowHeight = 20f;
        private const float RowSpacing = 6f;

        private static bool _onlyMine;

        /// <summary>The filter is on "Мои" (the player's rows only).</summary>
        public static bool OnlyMine
        {
            get { return _onlyMine; }
        }

        /// <summary>Fills the emptied body of the window; <paramref name="refresh"/> rebuilds the tab (the filter changed).</summary>
        public static void Build(RectTransform body, PlatformGenerationsFeature feature, Action refresh)
        {
            PlatformSnapshot snapshot = PlatformWindowData.Snapshot(feature);
            ViewText text = PlatformViewText.Instance;

            var options = new[] { ModL10n.L("pg.ib.filter.mine"), ModL10n.L("pg.ib.filter.all") };
            ModUI.Dropdown(body, ModL10n.L("pg.ib.filter"), options, _onlyMine ? 0 : 1, index =>
            {
                _onlyMine = index == 0;
                if (refresh != null) refresh();
            });

            IbTable table = InstalledBaseView.Build(snapshot.InstalledBase, snapshot.Timeline, snapshot.Today, ModCompanies.PlayerKey,
                _onlyMine, InstalledBaseView.DefaultMaxRows, text);

            ModUI.Text(body, table.TotalText);
            if (table.IsEmpty)
            {
                ModUI.Text(body, ModL10n.L("pg.ib.empty"));
                return;
            }

            AddRow(body, ModL10n.L("pg.ib.col.socket"), ModL10n.L("pg.ib.col.kind"), ModL10n.L("pg.ib.col.company"),
                ModL10n.L("pg.ib.col.units"), ModL10n.L("pg.ib.col.share"), ModL10n.L("pg.ib.col.age"), true);

            for (int i = 0; i < table.Rows.Count; i++)
            {
                IbRowView row = table.Rows[i];
                AddRow(body, row.Socket, row.Kind, row.Company, row.Units, row.Share, row.Age, false);
            }

            if (table.Hidden > 0) ModUI.Text(body, ModL10n.L("pg.ib.more", PlatformNumbers.Int(table.Hidden)));
        }

        private static void AddRow(RectTransform body, string socket, string kind, string company, string units, string share, string age, bool header)
        {
            RectTransform row = PlatformUi.Row(body, "PTModPgIbRow", RowHeight, RowSpacing, false);
            PlatformUi.Cell(row, socket, SocketWidth, header, false);
            PlatformUi.Cell(row, kind, KindWidth, header, false);
            PlatformUi.Cell(row, company, CompanyWidth, header, false);
            PlatformUi.Cell(row, units, UnitsWidth, header, true);
            PlatformUi.Cell(row, share, ShareWidth, header, true);
            PlatformUi.Cell(row, age, AgeWidth, header, true);
        }
    }
}
