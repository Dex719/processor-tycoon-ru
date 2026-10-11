using System;
using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The ring of the last 30 days of sales that the shares of the market are made of (D5, design.md "Метрики"): per day, five numbers of the
    /// player and five of the market - column 0 is every segment, column <c>1 + (int)Segment</c> is High End, Mid Range, Low End, Industries.
    /// A share is the sum of the player's days over the sum of the market's days, so a quiet day weighs as little as it sold; a window whose
    /// market sold nothing has the share 0 (never a division by zero). The window is part of the run's save section (<c>shareWindow</c>), hence the
    /// raw access to its slots for the codec.
    /// </summary>
    public sealed class RollingWindow
    {
        /// <summary>Numbers per day and side: all segments plus the four segments.</summary>
        public const int Columns = 5;

        public const int DefaultDays = 30;

        private readonly long[] _player;
        private readonly long[] _total;
        private int _head;

        public RollingWindow() : this(DefaultDays)
        {
        }

        public RollingWindow(int days)
        {
            if (days < 1) throw new ArgumentOutOfRangeException("days", "a window needs at least one day");
            Days = days;
            _player = new long[days * Columns];
            _total = new long[days * Columns];
        }

        /// <summary>The length of the ring.</summary>
        public int Days { get; private set; }

        /// <summary>The slot the next day is written to (0..Days-1); the slot under it holds the oldest day.</summary>
        public int Head
        {
            get { return _head; }
        }

        /// <summary>The column of a segment: 0 for <see cref="Segment.Any"/> (all segments), <c>1 + (int)segment</c> for a real one, -1 for anything else.</summary>
        public static int ColumnOf(Segment segment)
        {
            if (segment == Segment.Any) return 0;
            int index = (int)segment;
            return index >= 0 && index < Columns - 1 ? index + 1 : -1;
        }

        /// <summary>Adds a day, dropping the oldest. A missing array or a short one counts as zeros.</summary>
        public void Push(long[] player, long[] total)
        {
            WriteSlot(_head, player, total);
            _head = (_head + 1) % Days;
        }

        /// <summary>The player's sales over the window in a column.</summary>
        public long PlayerSum(int column)
        {
            return Sum(_player, column);
        }

        /// <summary>The market's sales over the window in a column.</summary>
        public long TotalSum(int column)
        {
            return Sum(_total, column);
        }

        /// <summary>The share of the player in a column over the window: 0..1, and 0 when the market sold nothing.</summary>
        public double Share(int column)
        {
            long total = TotalSum(column);
            if (total <= 0) return 0.0;
            long player = PlayerSum(column);
            if (player <= 0) return 0.0;
            double share = (double)player / total;
            return share > 1.0 ? 1.0 : share;
        }

        /// <summary>The raw value of the player's side in a slot (for the save codec).</summary>
        public long PlayerAt(int slot, int column)
        {
            return _player[IndexOf(slot, column)];
        }

        /// <summary>The raw value of the market's side in a slot (for the save codec).</summary>
        public long TotalAt(int slot, int column)
        {
            return _total[IndexOf(slot, column)];
        }

        /// <summary>Writes a whole slot without moving the head (for the save codec).</summary>
        public void SetSlot(int slot, long[] player, long[] total)
        {
            if (slot < 0 || slot >= Days) throw new ArgumentOutOfRangeException("slot");
            WriteSlot(slot, player, total);
        }

        /// <summary>Moves the head; a value outside the ring wraps around (a damaged save does not throw).</summary>
        public void SetHead(int head)
        {
            int wrapped = head % Days;
            _head = wrapped < 0 ? wrapped + Days : wrapped;
        }

        /// <summary>Empties the ring.</summary>
        public void Clear()
        {
            Array.Clear(_player, 0, _player.Length);
            Array.Clear(_total, 0, _total.Length);
            _head = 0;
        }

        /// <summary>
        /// Builds a window from what the save holds: the ring length, the head and the rows of both sides (row i is slot i). Missing rows are zeros,
        /// extra rows are ignored, a row shorter than <see cref="Columns"/> is padded with zeros, so a damaged or older save still loads.
        /// </summary>
        public static RollingWindow Restore(int days, int head, long[][] player, long[][] total)
        {
            var window = new RollingWindow(days < 1 ? DefaultDays : days);
            for (int slot = 0; slot < window.Days; slot++)
            {
                long[] p = player != null && slot < player.Length ? player[slot] : null;
                long[] t = total != null && slot < total.Length ? total[slot] : null;
                window.WriteSlot(slot, p, t);
            }
            window.SetHead(head);
            return window;
        }

        private void WriteSlot(int slot, long[] player, long[] total)
        {
            int offset = slot * Columns;
            for (int column = 0; column < Columns; column++)
            {
                _player[offset + column] = player != null && column < player.Length ? player[column] : 0L;
                _total[offset + column] = total != null && column < total.Length ? total[column] : 0L;
            }
        }

        private long Sum(long[] side, int column)
        {
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException("column");
            long sum = 0;
            for (int slot = 0; slot < Days; slot++) sum += side[slot * Columns + column];
            return sum;
        }

        private int IndexOf(int slot, int column)
        {
            if (slot < 0 || slot >= Days) throw new ArgumentOutOfRangeException("slot");
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException("column");
            return slot * Columns + column;
        }
    }
}
