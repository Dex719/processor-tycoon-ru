using System;
using System.Collections.Generic;
using PTMod.Core;
using CpuDataProvider = ProcessorTycoon.Hardware.CpuDataProvider;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMarketData = ProcessorTycoon.Hardware.CpuBase.MarketData;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The ecosystem of the architectures (FR-11, stage 2; design.md Component 11): the share of every family in the sales of CPUs over the last twelve full months,
    /// market and contracts together. Once a month (<c>TickPhase.MonthPassed</c>, the first step of the monthly handler) the service adds up, over all the CPUs of the
    /// game, the sales of the window by the family each CPU is bound to (<see cref="ArchRegistry.ResolveCpu"/>) and turns them into shares
    /// (<see cref="EcosystemMath"/>). The shares feed <c>f_eco</c> of the rating (<see cref="ArchitecturesFeature.EcosystemOf"/>), the choices of the AI companies and the
    /// section "Экосистема" of the panel (with the change against the window of a year ago, which is recomputed from the same series, so nothing is saved). Nothing
    /// is stored in the save: after a load the first measurement (<see cref="OnGameReady"/>) rebuilds everything from the monthly series that the game itself keeps.
    /// <list type="bullet">
    /// <item>No sales in the window (the early game): the whole ecosystem belongs to the fallback family (<c>ecosystem.fallbackFamily</c>, FR-11 item 3).</item>
    /// <item>A CPU of a family the catalog no longer has is left out, so the shares of the families of the catalog add up to one.</item>
    /// <item>Runs only at stage 2 for a working feature whose part <see cref="ArchParts.Ecosystem"/> passed the self-check; otherwise the factor is ×1.</item>
    /// </list>
    /// </summary>
    internal sealed class EcosystemService
    {
        private const string FeatureId = ArchitecturesFeature.FeatureId;
        private const string RefreshSite = "ecosystem/refresh";

        private readonly ArchitecturesFeature _feature;
        private readonly List<GameCpu> _cpus = new List<GameCpu>();
        private readonly Dictionary<string, long> _now = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _ago = new Dictionary<string, long>(StringComparer.Ordinal);

        private Dictionary<string, float> _current;
        private Dictionary<string, float> _yearAgo;
        private bool _hasSales;

        public EcosystemService(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        /// <summary>The ecosystem is played now: the feature works, stage 2 is on and the part that reads the market passed the self-check.</summary>
        public bool CanRun
        {
            get
            {
                return _feature.Registry != null && _feature.Catalog != null && !_feature.IsNeutralNow && _feature.Balance.IsFullStage
                       && (_feature.Parts & ArchParts.Ecosystem) != 0;
            }
        }

        /// <summary>The shares have been measured (never before the first <see cref="Refresh"/> of a game).</summary>
        public bool Measured
        {
            get { return _current != null; }
        }

        /// <summary>There were sales in the last window; false while the shares are the fallback of the early game.</summary>
        public bool HasSales
        {
            get { return _hasSales; }
        }

        /// <summary>The share of every family in the sales now (a family without sales is absent); null until measured. Read only.</summary>
        public IReadOnlyDictionary<string, float> Current
        {
            get { return _current; }
        }

        /// <summary>The same for the window a year earlier (the change of the panel); null until measured.</summary>
        public IReadOnlyDictionary<string, float> YearAgo
        {
            get { return _yearAgo; }
        }

        /// <summary><c>E</c> of a family: its share; 0 for a family without sales; a negative number while nothing is measured (the factor is ×1 then).</summary>
        public float ShareOf(string familyId)
        {
            return EcosystemMath.ShareOf(_current, familyId);
        }

        /// <summary>The lines of the section of the panel: every family, its percent (they add up to 100) and the change against a year ago.</summary>
        public List<EcosystemRow> Rows()
        {
            return EcosystemMath.Rows(_feature.Catalog, _current, _yearAgo);
        }

        // ---- the moments ----

        /// <summary><c>TickPhase.MonthPassed</c>: measures the window of the month that has just ended and tells the notifier (the milestones of the letters).</summary>
        public void OnMonth()
        {
            Refresh(true);
        }

        /// <summary>
        /// A game is ready (a new game, a loaded save): the first measurement, silent - a milestone that was passed long ago is recorded, not announced (FR-15 item 3).
        /// Also the feature switched on in the middle of a game.
        /// </summary>
        public void OnGameReady()
        {
            Forget();
            if (Refresh(false) && _feature.Rating != null) _feature.Rating.InvalidateAll();   // the cached ratings were made without the shares
        }

        /// <summary>The scene ends or the feature goes off: the measurement is forgotten, the next game (or the next switching on) measures anew.</summary>
        public void Forget()
        {
            _current = null;
            _yearAgo = null;
            _hasSales = false;
        }

        // ---- the measurement ----

        /// <summary>
        /// Adds up the sales of the window over all the CPUs and replaces the shares. <paramref name="announce"/>: tell the notifier what changed against the previous
        /// measurement (silent for the first one). Returns false when nothing could be measured (stage 1, the part is off, no date, the game has no CPU provider yet).
        /// </summary>
        public bool Refresh(bool announce)
        {
            if (!CanRun) return false;

            try
            {
                CpuDataProvider provider = CpuDataProvider.Instance;
                if (provider == null || ModTicks.Today == default(DateTime)) return false;

                EcosystemConfig config = _feature.Balance.ecosystem;
                ArchRegistry registry = _feature.Registry;
                ArchCatalog catalog = _feature.Catalog;

                _now.Clear();
                _ago.Clear();
                provider.GetAllCpus(_cpus);
                for (int i = 0; i < _cpus.Count; i++)
                {
                    GameCpu cpu = _cpus[i];
                    if (cpu == null) continue;

                    GameMarketData market = cpu.Market;
                    if (market == null) continue;

                    string familyId = registry.ResolveCpu(cpu);
                    if (familyId == null || !catalog.Contains(familyId)) continue;

                    Add(_now, familyId, EcosystemMath.WindowSum(market.Sold, market.SoldByContracts, config.windowMonths));
                    Add(_ago, familyId, EcosystemMath.WindowSum(market.Sold, market.SoldByContracts, config.windowMonths, EcosystemMath.YearMonths));
                }

                Dictionary<string, float> previous = _current;
                _hasSales = EcosystemMath.HasSales(_now);
                _current = EcosystemMath.Shares(_now, config.fallbackFamily);
                _yearAgo = EcosystemMath.Shares(_ago, config.fallbackFamily);
                _cpus.Clear();

                ModLog.Debug(FeatureId, "ecosystem: measured, " + (_hasSales ? "sales in the window" : "no sales, the fallback family has it all"));

                ArchNotifier notifier = _feature.Notifier;
                if (announce && notifier != null) notifier.OnEcosystem(previous, _current, _hasSales);
                else if (notifier != null) notifier.OnEcosystemBaseline(_current, _hasSales);
                return true;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(FeatureId, RefreshSite, ex);
                return false;
            }
        }

        private static void Add(Dictionary<string, long> sales, string familyId, long units)
        {
            if (units <= 0) return;
            long total;
            sales.TryGetValue(familyId, out total);
            sales[familyId] = total + units;
        }
    }
}
