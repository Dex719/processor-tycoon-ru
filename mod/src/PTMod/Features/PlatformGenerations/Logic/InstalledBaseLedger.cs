using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// One CPU as the installed base sees it (FR-4): who owns it now, on which socket and generation, and the monthly sales that the game
    /// keeps in <c>CpuBase.MarketData</c>. Built by the adapter (<c>GameAdapter</c>) from a <c>Cpu</c>; a plain struct so the model needs
    /// neither Unity nor the game's types.
    /// </summary>
    public struct CpuLedgerView
    {
        /// <summary>The CPU's save id (<c>Cpu.SaveID</c>): the identity of the CPU between months.</summary>
        public int CpuId;

        /// <summary>The owner now (<c>ModCompanies.KeyOf(Cpu.Company)</c>): <c>player</c> or <c>ai:&lt;UniqueID&gt;</c>. A CPU without a key counts only in the grand total.</summary>
        public string CompanyKey;

        /// <summary>The key of the CPU's socket (<see cref="SocketId.Key"/>); null = unknown, the CPU counts only in the totals.</summary>
        public string SocketKey;

        /// <summary>The name of the socket's base package (<see cref="SocketId.BasePackageName"/>); null when it cannot be read. The timeline needs it to judge socket compatibility.</summary>
        public string BasePackageName;

        /// <summary>The generation of the socket; null = outside the generations.</summary>
        public string GenId;

        /// <summary><c>CpuBase.MarketData.Sold</c>: units sold per month, index 0 = the first month of the game. Null or shorter than two months = no contribution (FR-4 item 5).</summary>
        public IReadOnlyList<int> Sold;

        /// <summary><c>CpuBase.MarketData.SoldByContracts</c>: units sold through contracts per month, the same indexing. May be null.</summary>
        public IReadOnlyList<int> SoldByContracts;
    }

    /// <summary>One line of the installed-base table (the window "Платформы", FR-9): a company on a socket.</summary>
    public struct IbRow
    {
        public string CompanyKey;
        public string SocketKey;

        /// <summary>The weighted, faded units of the company's CPUs on the socket.</summary>
        public double Units;

        /// <summary><see cref="Units"/> divided by the grand total (0 when the total is 0).</summary>
        public double Share;
    }

    /// <summary>
    /// The installed base of CPUs (FR-4, formula F1): derived from the monthly sales lists of the game, never stored in the save (design D2).
    /// <c>ib_cpu(L) = Σ_{i≤L} (Sold[i] + w·SoldByContracts[i]) · δ^(L−i)</c> with <c>δ = 1 − 1/lifetimeMonths</c>; the base of a company on a
    /// socket is the sum over the CPUs it owns <b>now</b> (design D3: the base goes with the CPU). <see cref="RebuildFull"/> computes it from the
    /// lists, <see cref="ApplyMonth"/> advances it by one month; both end with the aggregates by company and socket. Pure logic: every call takes
    /// a snapshot of the CPUs as <see cref="CpuLedgerView"/>. Not thread-safe; the aggregates are reused between months.
    /// </summary>
    public sealed class InstalledBaseLedger
    {
        private sealed class CpuState
        {
            public double Ib;
            public string CompanyKey;
            public string SocketKey;
            public string BasePackageName;
            public string GenId;
            public int Stamp;
        }

        private enum Pass
        {
            Full,
            Step,
            Refresh,
        }

        private readonly Dictionary<int, CpuState> _cpus = new Dictionary<int, CpuState>();
        private readonly Dictionary<CompanySocketKey, double> _byCompanySocket = new Dictionary<CompanySocketKey, double>();
        private readonly Dictionary<string, double> _bySocket = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _byGeneration = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _byCompany = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, SocketId> _sockets = new Dictionary<string, SocketId>(StringComparer.Ordinal);
        private readonly List<int> _stale = new List<int>();

        private double _total;
        private int _stamp;
        private int _month = -1;
        private bool _hasMonth;

        /// <summary>The whole installed base (every CPU with a usable ledger, whatever its owner and socket); 0 before the first pass.</summary>
        public double Total
        {
            get { return _total; }
        }

        /// <summary>How many CPUs with a usable ledger the last pass saw.</summary>
        public int CpuCount
        {
            get { return _cpus.Count; }
        }

        /// <summary>True after a pass: the ledger knows the month <see cref="LastMonth"/>.</summary>
        public bool HasState
        {
            get { return _hasMonth; }
        }

        /// <summary>The ledger month (<c>MonthsSinceStart − 1</c>) of the last pass, or -1 when there was none.</summary>
        public int LastMonth
        {
            get { return _month; }
        }

        /// <summary>Forgets everything (a new game, leaving to the menu).</summary>
        public void Clear()
        {
            _cpus.Clear();
            _byCompanySocket.Clear();
            _bySocket.Clear();
            _byGeneration.Clear();
            _byCompany.Clear();
            _sockets.Clear();
            _total = 0;
            _month = -1;
            _hasMonth = false;
        }

        /// <summary>
        /// FR-4 item 3: the whole base from the lists of every CPU (loading a save, the end of the fast-forward, the feature switched on again).
        /// <paramref name="lastFullMonth"/> is the last complete month, <c>MonthsSinceStart − 1</c>; a negative one (the first month of the game)
        /// gives an empty base. A CPU whose list is null or shorter than two months contributes 0 and is not kept. Null <paramref name="cpus"/> = no CPUs.
        /// </summary>
        public void RebuildFull(IEnumerable<CpuLedgerView> cpus, int lastFullMonth, PlatformBalance b)
        {
            Update(cpus, lastFullMonth, b ?? PlatformBalance.Defaults(), Pass.Full);
        }

        /// <summary>
        /// FR-4 item 2: advances the base by one month, <c>ib_cpu = ib_cpu·δ + units[lastFullMonth]</c>, and rebuilds the aggregates by the
        /// <b>current</b> owner of each CPU. A CPU the ledger does not know yet takes its whole history (as <see cref="RebuildFull"/> would).
        /// The call is safe against a missed or a repeated tick: the first call, a month that is not the next one after <see cref="LastMonth"/>
        /// or an earlier one fall back to <see cref="RebuildFull"/>, and the same month again only refreshes the owners without fading anything twice.
        /// </summary>
        public void ApplyMonth(IEnumerable<CpuLedgerView> cpus, int lastFullMonth, PlatformBalance b)
        {
            Pass pass;
            if (!_hasMonth || lastFullMonth < _month || lastFullMonth > _month + 1) pass = Pass.Full;
            else if (lastFullMonth == _month) pass = Pass.Refresh;
            else pass = Pass.Step;

            Update(cpus, lastFullMonth, b ?? PlatformBalance.Defaults(), pass);
        }

        /// <summary>The base of the company on the socket (F1); 0 for an unknown pair or a null key.</summary>
        public double Of(string companyKey, string socketKey)
        {
            if (companyKey == null || socketKey == null) return 0;
            double value;
            return _byCompanySocket.TryGetValue(new CompanySocketKey(companyKey, socketKey), out value) ? value : 0;
        }

        /// <summary>The base of all companies on the socket (<c>IB[·][s]</c>).</summary>
        public double OfSocket(string socketKey)
        {
            double value;
            return socketKey != null && _bySocket.TryGetValue(socketKey, out value) ? value : 0;
        }

        /// <summary>The base on the sockets of the generation (<c>IB_G</c>, FR-8 item 1).</summary>
        public double OfGeneration(string genId)
        {
            double value;
            return genId != null && _byGeneration.TryGetValue(genId, out value) ? value : 0;
        }

        /// <summary>The base of the company on all its sockets.</summary>
        public double OfCompany(string companyKey)
        {
            double value;
            return companyKey != null && _byCompany.TryGetValue(companyKey, out value) ? value : 0;
        }

        /// <summary>
        /// The share of the whole installed base that the company's best socket holds (FR-11 item 1, the predicate <c>pg.installedBaseShare</c>):
        /// <c>max over its sockets of IB[company][socket] / total</c>, in 0..1; 0 for an unknown company or an empty base.
        /// </summary>
        public double BestSocketShareOf(string companyKey)
        {
            if (companyKey == null || !(_total > 0)) return 0;

            double best = 0;
            foreach (KeyValuePair<CompanySocketKey, double> pair in _byCompanySocket)
            {
                if (!string.Equals(pair.Key.Company, companyKey, StringComparison.Ordinal)) continue;
                if (pair.Value > best) best = pair.Value;
            }

            double share = best / _total;
            return share > 1 ? 1 : share;
        }

        /// <summary>
        /// The company's main socket (FR-6 item 1): the one with the largest base among all its sockets except <paramref name="excludeSocketKey"/>
        /// (null excludes nothing). Null when the company has no other socket with a base. Equal bases go to the smaller key (ordinal), so the answer does not depend on the order of the CPUs.
        /// </summary>
        public string MainSocketOf(string companyKey, string excludeSocketKey)
        {
            if (companyKey == null) return null;

            string best = null;
            double bestValue = 0;
            foreach (KeyValuePair<CompanySocketKey, double> pair in _byCompanySocket)
            {
                if (!string.Equals(pair.Key.Company, companyKey, StringComparison.Ordinal)) continue;
                string socket = pair.Key.Socket;
                if (excludeSocketKey != null && string.Equals(socket, excludeSocketKey, StringComparison.Ordinal)) continue;
                if (pair.Value <= 0) continue;

                if (best == null || pair.Value > bestValue || (pair.Value == bestValue && string.CompareOrdinal(socket, best) < 0))
                {
                    best = socket;
                    bestValue = pair.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// The socket behind a key as the CPUs reported it (key, base package, standard or custom by the key); a default value (no key) for an
        /// unknown key. The timeline asks it for the main socket, whose compatibility with a new socket it must judge.
        /// </summary>
        public SocketId SocketOf(string socketKey)
        {
            SocketId id;
            return socketKey != null && _sockets.TryGetValue(socketKey, out id) ? id : default(SocketId);
        }

        /// <summary>The table for the window: every company x socket with a base, the largest first (then by company and socket, ordinal).</summary>
        public IReadOnlyList<IbRow> Rows()
        {
            var rows = new List<IbRow>(_byCompanySocket.Count);
            foreach (KeyValuePair<CompanySocketKey, double> pair in _byCompanySocket)
            {
                if (pair.Value <= 0) continue;
                rows.Add(new IbRow
                {
                    CompanyKey = pair.Key.Company,
                    SocketKey = pair.Key.Socket,
                    Units = pair.Value,
                    Share = _total > 0 ? pair.Value / _total : 0,
                });
            }

            rows.Sort((x, y) =>
            {
                int byUnits = y.Units.CompareTo(x.Units);
                if (byUnits != 0) return byUnits;
                int byCompany = string.CompareOrdinal(x.CompanyKey, y.CompanyKey);
                return byCompany != 0 ? byCompany : string.CompareOrdinal(x.SocketKey, y.SocketKey);
            });
            return rows;
        }

        /// <summary>
        /// The invariant after a pass (design, "Logging Strategy"): the grand total, the sum over the CPUs and the sum over the companies agree
        /// within <paramref name="tolerance"/> (relative to the total, at least absolute). False means a bug; the caller logs an error.
        /// </summary>
        public bool IsConsistent(double tolerance = 1e-6)
        {
            double byCpu = 0;
            foreach (KeyValuePair<int, CpuState> pair in _cpus) byCpu += pair.Value.Ib > 0 ? pair.Value.Ib : 0;

            double byCompany = 0;
            foreach (KeyValuePair<string, double> pair in _byCompany) byCompany += pair.Value;

            double allowed = tolerance * Math.Max(1.0, Math.Abs(_total));
            return Math.Abs(byCpu - _total) <= allowed && Math.Abs(byCompany - _total) <= allowed;
        }

        // ---- the pass ----

        private void Update(IEnumerable<CpuLedgerView> cpus, int lastFullMonth, PlatformBalance b, Pass pass)
        {
            double decay = b.InstalledBase.Decay;
            double weight = b.InstalledBase.ContractUnitWeight;

            _stamp++;
            if (cpus != null && lastFullMonth >= 0)
            {
                foreach (CpuLedgerView view in cpus)
                {
                    if (!IsUsable(view)) continue;

                    CpuState state;
                    bool known = _cpus.TryGetValue(view.CpuId, out state);
                    if (known && state.Stamp == _stamp) continue;   // the same id twice in one pass: the first one stands

                    if (!known)
                    {
                        state = new CpuState();
                        _cpus[view.CpuId] = state;
                    }

                    if (!known || pass == Pass.Full) state.Ib = FullIb(view, lastFullMonth, decay, weight);
                    else if (pass == Pass.Step) state.Ib = state.Ib * decay + UnitsOf(view, lastFullMonth, weight);
                    // Refresh: the numbers stay, only the owner and the socket are read again below.

                    state.CompanyKey = view.CompanyKey;
                    state.SocketKey = view.SocketKey;
                    state.BasePackageName = view.BasePackageName;
                    state.GenId = view.GenId;
                    state.Stamp = _stamp;
                }
            }

            _stale.Clear();
            foreach (KeyValuePair<int, CpuState> pair in _cpus)
            {
                if (pair.Value.Stamp != _stamp) _stale.Add(pair.Key);
            }
            for (int i = 0; i < _stale.Count; i++) _cpus.Remove(_stale[i]);

            _month = lastFullMonth;
            _hasMonth = true;
            Aggregate();
        }

        private void Aggregate()
        {
            _byCompanySocket.Clear();
            _bySocket.Clear();
            _byGeneration.Clear();
            _byCompany.Clear();
            _sockets.Clear();

            double total = 0;
            foreach (KeyValuePair<int, CpuState> pair in _cpus)
            {
                CpuState s = pair.Value;

                if (s.SocketKey != null)
                {
                    SocketId known;
                    if (!_sockets.TryGetValue(s.SocketKey, out known) || (known.BasePackageName == null && s.BasePackageName != null))
                    {
                        _sockets[s.SocketKey] = new SocketId
                        {
                            Key = s.SocketKey,
                            BasePackageName = s.BasePackageName,
                            IsStandard = s.SocketKey.StartsWith(SocketIdentity.StandardPrefix, StringComparison.Ordinal),
                        };
                    }
                }

                double ib = s.Ib;
                if (!(ib > 0)) continue;   // also drops NaN

                total += ib;
                if (s.CompanyKey != null) Add(_byCompany, s.CompanyKey, ib);
                if (s.SocketKey != null)
                {
                    Add(_bySocket, s.SocketKey, ib);
                    if (s.CompanyKey != null) Add(_byCompanySocket, new CompanySocketKey(s.CompanyKey, s.SocketKey), ib);
                }
                if (s.GenId != null) Add(_byGeneration, s.GenId, ib);
            }
            _total = total;
        }

        private static void Add(Dictionary<string, double> table, string key, double value)
        {
            double sum;
            table.TryGetValue(key, out sum);
            table[key] = sum + value;
        }

        private static void Add(Dictionary<CompanySocketKey, double> table, CompanySocketKey key, double value)
        {
            double sum;
            table.TryGetValue(key, out sum);
            table[key] = sum + value;
        }

        // ---- F1 ----

        /// <summary>FR-4 item 5: a CPU needs a list of at least two months to count.</summary>
        private static bool IsUsable(CpuLedgerView view)
        {
            return view.Sold != null && view.Sold.Count >= 2;
        }

        /// <summary>The units a CPU sold in the ledger month: market units plus the contract units at their weight (negative numbers count as 0).</summary>
        private static double UnitsOf(CpuLedgerView view, int month, double weight)
        {
            double units = 0;
            if (month < view.Sold.Count && view.Sold[month] > 0) units = view.Sold[month];

            IReadOnlyList<int> byContracts = view.SoldByContracts;
            if (byContracts != null && month < byContracts.Count && byContracts[month] > 0) units += weight * byContracts[month];
            return units;
        }

        /// <summary>
        /// <c>Σ_{i≤L} units[i]·δ^(L−i)</c> in one pass (Horner: <c>ib = ib·δ + units[i]</c>); months after the end of the lists fade the result by
        /// <c>δ^gap</c>, months of the lists after <c>L</c> are ignored.
        /// </summary>
        private static double FullIb(CpuLedgerView view, int last, double decay, double weight)
        {
            IReadOnlyList<int> sold = view.Sold;
            IReadOnlyList<int> byContracts = view.SoldByContracts;
            int soldCount = sold.Count;
            int contractCount = byContracts == null ? 0 : byContracts.Count;

            int n = Math.Max(soldCount, contractCount);
            if (n > last + 1) n = last + 1;

            double ib = 0;
            for (int i = 0; i < n; i++)
            {
                double units = 0;
                if (i < soldCount && sold[i] > 0) units = sold[i];
                if (i < contractCount && byContracts[i] > 0) units += weight * byContracts[i];
                ib = ib * decay + units;
            }

            int gap = last + 1 - n;
            if (gap > 0 && ib != 0) ib *= Math.Pow(decay, gap);
            return ib;
        }
    }
}
