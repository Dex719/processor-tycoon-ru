using System;
using System.Collections.Generic;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The market shares of the companies over the last full months (design.md, "Формулы и баланс", 1; FR-3), built from the monthly journals of
    /// <c>CpuBase.MarketData</c> and rebuilt once a month. The current month is never counted: the window of <c>M</c> months is the list indices
    /// <c>Count-1-M .. Count-2</c> of every journal (the last element is the month that is not over). A segment share counts
    /// <c>SoldByHighEnd</c> / <c>SoldByMidRange</c> / <c>SoldByLowEnd</c> / <c>SoldByIndustries</c> of the chosen segments; "all" counts <c>Sold</c> plus
    /// <c>SoldByContracts</c>. The share is the company's sales over the sales of every CPU in the same journals (bankrupt companies and CPUs without
    /// an owner stay in the denominator: they were part of the market); a zero denominator gives 0.
    /// <para>
    /// A rebuild is O(CPU × months): the sales of each company are summed per month and per journal, and a question is then O(months). The answers
    /// are memoized until the next rebuild. A tie is broken the same way every time: the player first, then the smaller <c>UniqueID</c>.
    /// </para>
    /// </summary>
    public sealed class MarketShareCache
    {
        /// <summary>The widest window the cache can be asked for (the months of a role are at most 120, but the shared number is <c>shareCacheMonths</c>, 36).</summary>
        public const int HardMaxMonths = 120;

        // Journals: 0..3 are the segments (Segment numbers), 4 is "all" (Sold + SoldByContracts).
        private const int SegmentJournals = 4;
        private const int AllJournal = 4;
        private const int JournalCount = 5;

        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<CompanyInfo> _companies = new List<CompanyInfo>();
        private readonly List<long[][]> _perCompany = new List<long[][]>();   // company index -> journal -> month k (1 = the last full month)
        private readonly Dictionary<long, long> _numeratorMemo = new Dictionary<long, long>();
        private readonly Dictionary<long, long> _totalMemo = new Dictionary<long, long>();
        private readonly Dictionary<long, IReadOnlyList<string>> _rankingMemo = new Dictionary<long, IReadOnlyList<string>>();

        private long[][] _total = NewJournals(1);
        private int _width;

        /// <summary>The months the cache keeps; a question for more is answered for this many.</summary>
        public int MaxMonths
        {
            get { return _width; }
        }

        /// <summary>The cache was built at least once.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary><c>MonthsSinceStart</c> of the world at the last rebuild; -1 before the first.</summary>
        public int BuiltAtMonth { get; private set; } = -1;

        /// <summary>How many rebuilds there were (the tests count them).</summary>
        public int Rebuilds { get; private set; }

        /// <summary>Drops everything: the next question finds an empty cache.</summary>
        public void Clear()
        {
            _index.Clear();
            _companies.Clear();
            _perCompany.Clear();
            _numeratorMemo.Clear();
            _totalMemo.Clear();
            _rankingMemo.Clear();
            _total = NewJournals(1);
            _width = 0;
            IsBuilt = false;
            BuiltAtMonth = -1;
        }

        /// <summary>Rebuilds when the month changed since the last build, the cache was never built or the width differs; otherwise does nothing (O(1)).</summary>
        public void EnsureFresh(IWorldView world, int maxMonths)
        {
            if (world == null) return;
            int width = Clamp(maxMonths);
            if (IsBuilt && BuiltAtMonth == world.MonthsSinceStart && _width == width) return;
            Rebuild(world, width);
        }

        /// <summary>
        /// Reads the journals of every CPU again (monthly). <paramref name="maxMonths"/> is the widest window a question may ask for (1..120). A CPU whose
        /// journal is shorter than the window contributes the months it has; a null journal contributes nothing.
        /// </summary>
        public void Rebuild(IWorldView world, int maxMonths)
        {
            if (world == null) throw new ArgumentNullException("world");

            int width = Clamp(maxMonths);
            _index.Clear();
            _companies.Clear();
            _perCompany.Clear();
            _numeratorMemo.Clear();
            _totalMemo.Clear();
            _rankingMemo.Clear();
            _width = width;
            _total = NewJournals(width);

            IReadOnlyList<CompanyInfo> companies = world.Companies;
            if (companies != null)
            {
                for (int i = 0; i < companies.Count; i++)
                {
                    CompanyInfo company = companies[i];
                    if (company == null || string.IsNullOrEmpty(company.Key) || _index.ContainsKey(company.Key)) continue;
                    _index.Add(company.Key, _companies.Count);
                    _companies.Add(company);
                    _perCompany.Add(NewJournals(width));
                }
            }

            IReadOnlyList<CpuInfo> cpus = world.Cpus;
            if (cpus != null)
            {
                for (int i = 0; i < cpus.Count; i++)
                {
                    CpuInfo cpu = cpus[i];
                    if (cpu == null) continue;

                    long[][] mine = null;
                    int owner;
                    if (cpu.CompanyKey != null && _index.TryGetValue(cpu.CompanyKey, out owner)) mine = _perCompany[owner];

                    Add(cpu.SoldByHighEnd, 0, null, mine, width);
                    Add(cpu.SoldByMidRange, 1, null, mine, width);
                    Add(cpu.SoldByLowEnd, 2, null, mine, width);
                    Add(cpu.SoldByIndustries, 3, null, mine, width);
                    Add(cpu.Sold, AllJournal, cpu.SoldByContracts, mine, width);
                }
            }

            IsBuilt = true;
            BuiltAtMonth = world.MonthsSinceStart;
            Rebuilds++;
        }

        /// <summary>
        /// The share of the company (0..1) over the last <paramref name="months"/> full months in the segments of the mask. 0 for an unknown company, an
        /// empty mask, a cache that is not built and a window with no sales. <paramref name="months"/> above the width of the cache is cut to it.
        /// </summary>
        public float Share(string companyKey, SegmentMask segments, int months)
        {
            int company;
            if (!IsBuilt || companyKey == null || !_index.TryGetValue(companyKey, out company)) return 0f;

            int m = Window(months);
            long total = Total(segments, m);
            if (total <= 0) return 0f;
            return (float)((double)Numerator(company, segments, m) / total);
        }

        /// <summary>The sales of the company over the window, in units; 0 for an unknown company.</summary>
        public long Sales(string companyKey, SegmentMask segments, int months)
        {
            int company;
            if (!IsBuilt || companyKey == null || !_index.TryGetValue(companyKey, out company)) return 0L;
            return Numerator(company, segments, Window(months));
        }

        /// <summary>The sales of every CPU of the game over the window (the denominator of every share).</summary>
        public long TotalSales(SegmentMask segments, int months)
        {
            return IsBuilt ? Total(segments, Window(months)) : 0L;
        }

        /// <summary>
        /// The companies that have a share, biggest first: only companies of the world that are not bankrupt and sold something in the window. A tie
        /// goes to the player, then to the smaller <c>UniqueID</c>. Shared list: do not modify. Empty for an empty window.
        /// </summary>
        public IReadOnlyList<string> Ranking(SegmentMask segments, int months)
        {
            if (!IsBuilt) return new string[0];

            int m = Window(months);
            long key = MemoKey(0, segments, m);
            IReadOnlyList<string> cached;
            if (_rankingMemo.TryGetValue(key, out cached)) return cached;

            var order = new List<int>();
            var sales = new long[_companies.Count];
            for (int i = 0; i < _companies.Count; i++)
            {
                if (_companies[i].IsBankrupt) continue;
                sales[i] = Numerator(i, segments, m);
                if (sales[i] > 0) order.Add(i);
            }

            // The denominator is the same for everybody, so comparing the sales is comparing the shares (and a tie is an exact tie).
            order.Sort((a, b) =>
            {
                int byShare = sales[b].CompareTo(sales[a]);
                if (byShare != 0) return byShare;
                int byId = _companies[a].UniqueId.CompareTo(_companies[b].UniqueId);
                return byId != 0 ? byId : string.CompareOrdinal(_companies[a].Key, _companies[b].Key);
            });

            var result = new string[order.Count];
            for (int i = 0; i < result.Length; i++) result[i] = _companies[order[i]].Key;
            _rankingMemo[key] = result;
            return result;
        }

        // ---- internals ----

        private long Numerator(int company, SegmentMask segments, int months)
        {
            long key = MemoKey(company + 1, segments, months);
            long value;
            if (_numeratorMemo.TryGetValue(key, out value)) return value;

            value = SumWindow(_perCompany[company], segments, months);
            _numeratorMemo[key] = value;
            return value;
        }

        private long Total(SegmentMask segments, int months)
        {
            long key = MemoKey(0, segments, months);
            long value;
            if (_totalMemo.TryGetValue(key, out value)) return value;

            value = SumWindow(_total, segments, months);
            _totalMemo[key] = value;
            return value;
        }

        private static long SumWindow(long[][] journals, SegmentMask segments, int months)
        {
            if (segments == SegmentMask.None) return 0L;
            if (segments == SegmentMask.All) return Sum(journals[AllJournal], months);

            long sum = 0;
            for (int s = 0; s < SegmentJournals; s++)
            {
                if (((int)segments & (1 << s)) != 0) sum += Sum(journals[s], months);
            }
            return sum;
        }

        private static long Sum(long[] byMonth, int months)
        {
            long sum = 0;
            int n = Math.Min(months, byMonth.Length);
            for (int k = 0; k < n; k++) sum += byMonth[k];
            return sum;
        }

        /// <summary>
        /// Adds a journal (and the one that is added to it) to the totals and to the company's sums: element <c>Count-1-k</c> is the k-th full month back
        /// (k = 1 is the last full month). The current month, the element <c>Count-1</c>, is left out.
        /// </summary>
        private void Add(IReadOnlyList<int> journal, int slot, IReadOnlyList<int> extra, long[][] mine, int width)
        {
            AddOne(journal, slot, mine, width);
            if (extra != null) AddOne(extra, slot, mine, width);
        }

        private void AddOne(IReadOnlyList<int> journal, int slot, long[][] mine, int width)
        {
            if (journal == null) return;
            int count = journal.Count;
            long[] total = _total[slot];
            long[] own = mine == null ? null : mine[slot];

            int months = Math.Min(width, count - 1);
            for (int k = 1; k <= months; k++)
            {
                int sold = journal[count - 1 - k];
                if (sold <= 0) continue;   // the game never writes a negative number; ignoring one keeps a damaged journal from lowering a share
                total[k - 1] += sold;
                if (own != null) own[k - 1] += sold;
            }
        }

        private static long[][] NewJournals(int width)
        {
            var journals = new long[JournalCount][];
            for (int i = 0; i < JournalCount; i++) journals[i] = new long[width];
            return journals;
        }

        private int Window(int months)
        {
            if (months < 1) return 1;
            return months > _width ? _width : months;
        }

        private static long MemoKey(int company, SegmentMask segments, int months)
        {
            return ((long)company << 24) | ((long)(int)segments << 16) | (long)months;
        }

        private static int Clamp(int maxMonths)
        {
            return maxMonths < 1 ? 1 : maxMonths > HardMaxMonths ? HardMaxMonths : maxMonths;
        }
    }
}
