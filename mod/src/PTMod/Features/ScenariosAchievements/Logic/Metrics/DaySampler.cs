using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// One CPU of the game as the pass over the market reads it: plain numbers, no game type. The game-side layer
    /// (<c>GameMetricsSource</c>) fills one row per CPU and hands it to <see cref="DaySampler.Add"/>; the sales are those of yesterday
    /// (<c>SoldLastDay*</c>), the way the game's own <c>MarketDataProvider.MarketShare</c> counts them.
    /// </summary>
    public struct CpuRow
    {
        public int SaveId;

        /// <summary>The CPU was designed by the player's company (<c>Cpu.Company</c> is the player's): its sales are the player's sales.</summary>
        public bool IsPlayerCpu;

        public bool IsReleased;

        /// <summary>The CPU was withdrawn from sale.</summary>
        public bool IsRetired;

        /// <summary>Pieces sold yesterday: all segments, then High End, Mid Range, Low End, Industries.</summary>
        public long Sold, SoldHighEnd, SoldMidRange, SoldLowEnd, SoldIndustries;

        /// <summary>The sales the CPU missed yesterday.</summary>
        public int MissedSales;

        /// <summary>Instructions per second.</summary>
        public double Ips;

        /// <summary>The highest of the popularity numbers of the CPU (0..1).</summary>
        public double Popularity;

        public string Name;

        /// <summary>Frequency in kHz.</summary>
        public float Frequency;

        public int Cores;

        public bool Smt;

        /// <summary>Power in watts.</summary>
        public float Power;
    }

    /// <summary>
    /// The arithmetic of <c>GameMetricsSource.SampleDay</c> (TSK-016, design.md "Метрики" and "Performance"): folds the CPUs of the game, one at a
    /// time, into a <see cref="DaySample"/> - the sales of the player and of the whole market by segment, the sales the player missed, the best
    /// <c>Ips</c> among the rivals' CPUs on sale, the highest popularity among the player's own CPUs, and the list of the player's released CPUs.
    /// It allocates nothing once its pool of <see cref="CpuInfo"/> has grown to the size of the player's list (the pool is kept between days):
    /// the pass over 400 CPUs is a loop of additions.
    /// <para>
    /// A CPU that came to the player by a transfer (<c>transferredCpuIds</c>, FR-5 item 6) is the player's CPU for the sales (the game counts
    /// the sales of a CPU for the company it belongs to), stays in the list of the player's CPUs (the calculator leaves it out of the
    /// metrics of characteristics and of the performance crown) and is left out of the popularity here.
    /// </para>
    /// </summary>
    public sealed class DaySampler
    {
        private static readonly int ColumnAll = RollingWindow.ColumnOf(Segment.Any);
        private static readonly int ColumnHighEnd = RollingWindow.ColumnOf(Segment.HighEnd);
        private static readonly int ColumnMidRange = RollingWindow.ColumnOf(Segment.MidRange);
        private static readonly int ColumnLowEnd = RollingWindow.ColumnOf(Segment.LowEnd);
        private static readonly int ColumnIndustries = RollingWindow.ColumnOf(Segment.Industries);

        private readonly List<CpuInfo> _pool = new List<CpuInfo>();
        private int _used;
        private DaySample _target;
        private HashSet<int> _transferred;

        /// <summary>How many <see cref="CpuInfo"/> objects the pool holds now (it only grows).</summary>
        public int PoolSize
        {
            get { return _pool.Count; }
        }

        /// <summary>
        /// Starts a pass: empties the sums and the list of the sample (the arrays of the sample are made when they are missing or short and are
        /// reused otherwise). <paramref name="transferredCpuIds"/> are the save ids of the CPUs the player got by a transfer; null for none.
        /// </summary>
        public void Begin(DaySample target, HashSet<int> transferredCpuIds)
        {
            if (target == null) throw new ArgumentNullException("target");
            _target = target;
            _transferred = transferredCpuIds;
            _used = 0;

            target.PlayerSold = Zeroed(target.PlayerSold);
            target.TotalSold = Zeroed(target.TotalSold);
            target.MissedSales = 0;
            target.RivalBestIps = 0;
            target.MaxPopularity = 0;
            if (target.PlayerCpus == null) target.PlayerCpus = new List<CpuInfo>();
            else target.PlayerCpus.Clear();
        }

        /// <summary>Folds one CPU into the sample. Call <see cref="Begin"/> first.</summary>
        public void Add(ref CpuRow row)
        {
            DaySample target = _target;
            if (target == null) throw new InvalidOperationException("Begin was not called");

            AddSales(target.TotalSold, ref row);
            if (!row.IsPlayerCpu)
            {
                if (row.IsReleased && !row.IsRetired && row.Ips > target.RivalBestIps) target.RivalBestIps = row.Ips;
                return;
            }

            AddSales(target.PlayerSold, ref row);
            target.MissedSales += row.MissedSales;
            if (!row.IsReleased) return;

            target.PlayerCpus.Add(Fill(Take(), ref row));
            bool transferred = _transferred != null && _transferred.Contains(row.SaveId);
            if (!row.IsRetired && !transferred && row.Popularity > target.MaxPopularity) target.MaxPopularity = row.Popularity;
        }

        /// <summary>Ends the pass and lets go of the sample (the pool stays).</summary>
        public void End()
        {
            _target = null;
            _transferred = null;
        }

        // ---- internals ----

        private static void AddSales(long[] into, ref CpuRow row)
        {
            into[ColumnAll] += row.Sold;
            into[ColumnHighEnd] += row.SoldHighEnd;
            into[ColumnMidRange] += row.SoldMidRange;
            into[ColumnLowEnd] += row.SoldLowEnd;
            into[ColumnIndustries] += row.SoldIndustries;
        }

        private static long[] Zeroed(long[] array)
        {
            if (array == null || array.Length < RollingWindow.Columns) return new long[RollingWindow.Columns];
            for (int i = 0; i < array.Length; i++) array[i] = 0;
            return array;
        }

        private CpuInfo Take()
        {
            if (_used == _pool.Count) _pool.Add(new CpuInfo());
            return _pool[_used++];
        }

        private static CpuInfo Fill(CpuInfo info, ref CpuRow row)
        {
            info.SaveId = row.SaveId;
            info.Name = row.Name;
            info.Frequency = row.Frequency;
            info.Cores = row.Cores;
            info.Smt = row.Smt;
            info.Power = row.Power;
            info.Ips = row.Ips;
            info.OnMarket = !row.IsRetired;
            info.CompanyType = null;
            info.ReleasedOn = default(DateTime);
            return info;
        }
    }
}
