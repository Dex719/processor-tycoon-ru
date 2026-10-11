using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>One row of the table of the installed base: a company on a socket.</summary>
    public sealed class IbRowView
    {
        public string CompanyKey;
        public string SocketKey;

        /// <summary>The name of the socket: <c>CustomSocket.Name</c> or <c>Package.Name</c>.</summary>
        public string Socket;

        /// <summary>"Стандартный" or "Свой".</summary>
        public string Kind;

        public string Company;
        public bool IsPlayer;

        /// <summary>The base in units: "83 120".</summary>
        public string Units;

        /// <summary>The share of the whole base in percent: "12.5 %".</summary>
        public string Share;

        /// <summary>How long the company has had the socket: "14 мес.", "—" when the chronology has no record.</summary>
        public string Age;
    }

    /// <summary>The table of the tab "Установленная база".</summary>
    public sealed class IbTable
    {
        public readonly List<IbRowView> Rows = new List<IbRowView>();

        /// <summary>How many more rows there were than <c>maxRows</c> (they are not in <see cref="Rows"/>).</summary>
        public int Hidden;

        /// <summary>No row at all (nothing sold yet, or the filter leaves nothing).</summary>
        public bool IsEmpty
        {
            get { return Rows.Count == 0 && Hidden == 0; }
        }

        /// <summary>"Всего в базе: 83 120 шт." - the whole base of the market, whatever the filter.</summary>
        public string TotalText;
    }

    /// <summary>
    /// The model of the tab "Установленная база" (FR-9 item 3): the rows "socket x company" with a base, the largest first - the name of the
    /// socket, standard or its own, the company, the base in units, its share of the whole base and how long the company has had the socket.
    /// The filter "мои / все" keeps the player's rows or all of them. Pure logic over the ledger and the chronology.
    /// </summary>
    public static class InstalledBaseView
    {
        /// <summary>The largest number of rows the window draws (each is six widgets); the rest are counted in <see cref="IbTable.Hidden"/>.</summary>
        public const int DefaultMaxRows = 60;

        /// <summary>
        /// Builds the table. <paramref name="playerKey"/> is the key of the player's company (for the filter and the mark "(вы)"). Rows come in the
        /// order of <see cref="InstalledBaseLedger.Rows"/>: by base, then by company and socket. A null ledger gives an empty table.
        /// </summary>
        public static IbTable Build(InstalledBaseLedger ib, PlatformTimeline timeline, GameDate today, string playerKey, bool onlyMine, int maxRows, ViewText text)
        {
            var table = new IbTable();
            if (text == null) text = new ViewText();
            table.TotalText = text.L("pg.ib.total", text.Count(ib == null ? 0 : ib.Total));
            if (ib == null) return table;

            int limit = Math.Max(0, maxRows);
            IReadOnlyList<IbRow> rows = ib.Rows();
            for (int i = 0; i < rows.Count; i++)
            {
                IbRow row = rows[i];
                bool mine = playerKey != null && string.Equals(row.CompanyKey, playerKey, StringComparison.Ordinal);
                if (onlyMine && !mine) continue;
                if (table.Rows.Count >= limit)
                {
                    table.Hidden++;
                    continue;
                }

                SocketId socket = ib.SocketOf(row.SocketKey);
                bool standard = socket.IsValid ? socket.IsStandard : (row.SocketKey ?? string.Empty).StartsWith(SocketIdentity.StandardPrefix, StringComparison.Ordinal);
                if (!socket.IsValid) socket = new SocketId { Key = row.SocketKey, IsStandard = standard };

                int age = timeline == null ? -1 : timeline.SocketAgeMonths(row.CompanyKey, row.SocketKey, today);
                string name = text.Company(row.CompanyKey);
                table.Rows.Add(new IbRowView
                {
                    CompanyKey = row.CompanyKey,
                    SocketKey = row.SocketKey,
                    Socket = text.Socket(socket),
                    Kind = text.L(standard ? "pg.ib.kind.standard" : "pg.ib.kind.custom"),
                    Company = mine ? text.L("pg.company.you", name) : name,
                    IsPlayer = mine,
                    Units = text.Count(row.Units),
                    Share = PlatformNumbers.Percent(row.Share) + " %",
                    Age = age < 0 ? text.L("pg.gen.none") : text.L("pg.ib.age", PlatformNumbers.Int(age)),
                });
            }
            return table;
        }
    }
}
