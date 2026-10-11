using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>One possible participant as the panel lists it: the company, its CPU and the speed of it.</summary>
    public struct PotentialRow
    {
        public string Company;
        public string Cpu;

        /// <summary><c>Cpu.Ips</c>.</summary>
        public float Ips;
    }

    /// <summary>What the block "possible participants" needs (FR-11); the panel fills it from <c>PotentialReport</c> and the names of the game.</summary>
    public sealed class PotentialPanelInput
    {
        /// <summary>The monthly moves of the AI left before the review date (<see cref="AiMoveCalendar.MovesBefore"/>); with none the block is not shown.</summary>
        public int MovesLeft;

        /// <summary>The AI offers that would change the contract (<see cref="PotentialEstimate.NewBidders"/> with the names of the game); the block prints them fastest CPU first.</summary>
        public IList<PotentialRow> Rows;

        /// <summary>The probability that the draw picks the candidate now, 0..1.</summary>
        public double CurrentActual;

        /// <summary>The probability with every possible participant in, 0..1.</summary>
        public double PessimisticActual;

        /// <summary>The probability under which the panel marks a result as bad (<c>risk.lowActualChance</c>).</summary>
        public double LowActualChance = 0.10;

        /// <summary>The difficulty is 5 (<c>Player.DifficultyLevel</c>): the leaders among the AI take every kind of contract.</summary>
        public bool HardDifficulty;

        /// <summary>The most rows the block prints; the rest is one line "and N more".</summary>
        public int MaxRows = DefaultMaxRows;

        public const int DefaultMaxRows = 12;
    }

    /// <summary>
    /// The block "possible participants" of the breakdown of the chance (FR-11, AC-11.1): which AI companies may still make an offer before the review date, the probability if all of them do, and the
    /// notes that this is an estimate and that on difficulty 5 the AI is more active. Pure: the decisions of what to say are here, the panel prints the lines.
    /// </summary>
    public static class PotentialLines
    {
        /// <summary>The lines of the block; empty when no monthly move of the AI is left before the review (the block is then not shown, FR-11 item 1).</summary>
        public static List<PanelLine> Build(PotentialPanelInput input)
        {
            var lines = new List<PanelLine>();
            if (input == null || input.MovesLeft <= 0) return lines;

            List<PotentialRow> rows = StrongestFirst(input.Rows);
            int count = rows.Count;
            lines.Add(new PanelLine("ct.odds.potential.title", LineTone.Normal, PanelArg.Whole(count)).AsBold());
            lines.Add(new PanelLine("ct.odds.potential.moves", LineTone.Muted, PanelArg.Whole(input.MovesLeft)).Indented(1));

            if (count == 0)
            {
                lines.Add(new PanelLine("ct.odds.potential.none", LineTone.Muted).Indented(1));
            }
            else
            {
                int shown = count < input.MaxRows || input.MaxRows < 1 ? count : input.MaxRows;
                for (int i = 0; i < shown; i++)
                {
                    PotentialRow row = rows[i];
                    lines.Add(new PanelLine("ct.odds.potential.row", LineTone.Normal, PanelArg.OfText(row.Company), PanelArg.OfText(row.Cpu), PanelArg.Ips(row.Ips)).Indented(1));
                }
                if (shown < count) lines.Add(new PanelLine("ct.odds.potential.more", LineTone.Muted, PanelArg.Whole(count - shown)).Indented(1));

                lines.Add(new PanelLine("ct.odds.potential.pessimistic", input.PessimisticActual < input.LowActualChance ? LineTone.Bad : LineTone.Normal,
                    PanelArg.Actual(input.PessimisticActual), PanelArg.Actual(input.CurrentActual)).AsBold());
            }

            lines.Add(new PanelLine("ct.odds.potential.note", LineTone.Muted));
            if (input.HardDifficulty) lines.Add(new PanelLine("ct.odds.potential.hard", LineTone.Warn));
            return lines;
        }

        /// <summary>The rows with the fastest CPU first (the competitor that matters most); rows of equal speed keep their order.</summary>
        private static List<PotentialRow> StrongestFirst(IList<PotentialRow> rows)
        {
            var sorted = new List<PotentialRow>();
            if (rows == null) return sorted;

            var order = new List<int>(rows.Count);
            for (int i = 0; i < rows.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byIps = rows[b].Ips.CompareTo(rows[a].Ips);
                return byIps != 0 ? byIps : a.CompareTo(b);
            });
            foreach (int index in order) sorted.Add(rows[index]);
            return sorted;
        }
    }
}
