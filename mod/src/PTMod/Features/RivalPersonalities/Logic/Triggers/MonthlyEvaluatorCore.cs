using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>The best CPU of a rival in one segment: the one with the highest performance among those it sells on the market.</summary>
    public struct CpuBest
    {
        public CpuBest(float ips, float price)
        {
            Present = true;
            Ips = ips;
            Price = price;
        }

        /// <summary>The rival sells a CPU in the segment.</summary>
        public bool Present;

        public float Ips;
        public float Price;
    }

    /// <summary>What the monthly pass needs to know about one rival (the game side fills it).</summary>
    public sealed class RivalMarketView
    {
        public int UniqueId;
        public bool IsFoundry;
        public bool IsBankrupt;

        /// <summary>The best CPU on sale in each segment, the index is <c>(int)Segment</c>.</summary>
        public CpuBest[] Best = new CpuBest[ShareCalculator.SegmentCount];
    }

    /// <summary>A CPU of the player that is on sale, with the segment the core classifies it into (<c>ModSegments.Classify</c>).</summary>
    public sealed class PlayerCpuView
    {
        public PlayerCpuView(string name, float ips, float price, Segment segment)
        {
            Name = name;
            Ips = ips;
            Price = price;
            Segment = segment;
        }

        public string Name;
        public float Ips;
        public float Price;
        public Segment Segment;
    }

    /// <summary>The facts of one monthly pass (<see cref="MonthlyEvaluatorCore.Run"/>): collected by the game side on the first of the month.</summary>
    public sealed class MonthlyInput
    {
        /// <summary>The game date of the pass.</summary>
        public DateTime Today;

        /// <summary>The sales of the last full month, one row per company (or per CPU; rows of one company are added up). The keys are <c>player</c> and <c>ai:&lt;UniqueID&gt;</c>.</summary>
        public List<SalesRow> Sales = new List<SalesRow>();

        /// <summary>The AI companies.</summary>
        public List<RivalMarketView> Rivals = new List<RivalMarketView>();

        /// <summary>The CPUs of the player that are on sale.</summary>
        public List<PlayerCpuView> PlayerCpus = new List<PlayerCpuView>();

        /// <summary>The <c>UniqueID</c> of the other side of every active business contract of the player, one entry for each contract.</summary>
        public List<int> ContractRivals = new List<int>();
    }

    /// <summary>What the monthly pass did (for the log and the tests).</summary>
    public struct MonthlyResult
    {
        /// <summary>The shares were compared with the month before (the snapshot of the month before was there).</summary>
        public bool ComparedShares;

        public int ShareGains;
        public int Undercuts;
        public int ContractBonuses;

        /// <summary>The attitudes that moved towards the baseline.</summary>
        public int Drifted;

        /// <summary>The friendly offers of foundries that were sent this month (FR-12).</summary>
        public int Partnerships;

        /// <summary>The entries of caution and of undercut cooldowns that ran out.</summary>
        public int Expired;

        /// <summary>A step of the pass failed (it was logged and the others still ran).</summary>
        public int Failures;
    }

    /// <summary>
    /// The monthly evaluation of the market (design.md Component 7, <c>MonthlyEvaluator.Run</c>; FR-4 item 3, FR-5 items 2, 3 and 6): the pure half. It
    /// runs on the first of the month, in this order:
    /// <list type="number">
    /// <item><b>Shares.</b> The shares of the last full month (<see cref="ShareCalculator"/>) against the snapshot of the month before:
    /// <c>ShareGain</c> for each rival whose share in a segment fell while the player's grew by at least <c>shareGainPp</c> (3). The snapshot is
    /// replaced by the new month's. A snapshot of another month is not compared (a game that was off for a while).</item>
    /// <item><b>Undercut.</b> <c>PriceUndercut</c> for a rival and a segment in which a CPU of the player has at least 0.9 of the rival's best
    /// performance at most 0.85 of its price; once in <c>undercutCooldownDays</c> (90) for the pair.</item>
    /// <item><b>Contracts.</b> +2 for each active business contract with a rival.</item>
    /// <item><b>Drift.</b> <see cref="AttitudeLedger.MonthlyDrift"/>.</item>
    /// <item><b>Partnership.</b> The roll of the friendly offers of the foundries (<see cref="PartnershipOfferCore"/>, FR-12), on the attitude after the drift.</item>
    /// <item><b>Housekeeping.</b> The cooldowns and the cautions that ran out are removed.</item>
    /// </list>
    /// The game side then ends the external price cuts (<c>PriceBook.ExpireExternal</c>). A step that throws is logged and does not stop the next one.
    /// </summary>
    public sealed class MonthlyEvaluatorCore
    {
        private readonly Func<RivalState> _state;
        private readonly Func<RivalConfig> _config;
        private readonly TriggerRecorder _recorder;
        private readonly AttitudeLedger _ledger;
        private readonly Action<string> _log;
        private readonly PartnershipOfferCore _partnership;

        /// <param name="partnership">The roll of the friendly offers of the foundries (TSK-022); null leaves the step out.</param>
        public MonthlyEvaluatorCore(Func<RivalState> state, Func<RivalConfig> config, TriggerRecorder recorder, AttitudeLedger ledger, Action<string> log = null,
                                    PartnershipOfferCore partnership = null)
        {
            _state = state;
            _config = config;
            _recorder = recorder;
            _ledger = ledger;
            _log = log;
            _partnership = partnership;
        }

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        private RivalConfig Config
        {
            get
            {
                RivalConfig config = _config != null ? _config() : null;
                return config ?? RivalConfig.Defaults();
            }
        }

        /// <summary>Runs the pass on the facts of the first of the month. Never throws.</summary>
        public MonthlyResult Run(MonthlyInput input)
        {
            var result = new MonthlyResult();
            if (input == null) return result;

            int failures = 0;
            int shareGains = 0;
            bool compared = false;
            int undercuts = 0;
            int bonuses = 0;
            int drifted = 0;
            int partnerships = 0;
            int expired = 0;

            failures += Step("shares", () => shareGains = EvaluateShares(input, out compared));
            failures += Step("undercut", () => undercuts = EvaluateUndercuts(input));
            failures += Step("contracts", () => bonuses = EvaluateContracts(input));
            failures += Step("drift", () => drifted = _ledger.MonthlyDrift());
            failures += Step("partnership", () => partnerships = _partnership != null ? _partnership.Run(input).Sent : 0);
            failures += Step("housekeeping", () => expired = UndercutBook.Prune(State, input.Today) + CautionCleaner.RemoveExpired(State, input.Today));

            result.ComparedShares = compared;
            result.ShareGains = shareGains;
            result.Undercuts = undercuts;
            result.ContractBonuses = bonuses;
            result.Drifted = drifted;
            result.Partnerships = partnerships;
            result.Expired = expired;
            result.Failures = failures;
            return result;
        }

        // ---- the steps ----

        /// <summary>FR-5 item 2. Returns the number of triggers; <paramref name="compared"/> tells whether the month before was there.</summary>
        private int EvaluateShares(MonthlyInput input, out bool compared)
        {
            RivalState state = State;
            int month = ShareSnapshots.MonthNumber(input.Today);
            Dictionary<string, float[]> now = ShareCalculator.Shares(input.Sales);

            int gains = 0;
            compared = ShareSnapshots.IsComparable(state.shareSnapshot, month);
            if (compared)
            {
                Dictionary<string, float[]> before = ShareSnapshots.ToTable(state.shareSnapshot);
                float thresholdPp = Config.triggerThresholds.shareGainPp;

                for (int segment = 0; segment < ShareCalculator.SegmentCount; segment++)
                {
                    float playerBefore = ShareOf(before, CompanyKey.Player, segment);
                    float playerNow = ShareOf(now, CompanyKey.Player, segment);
                    if (!TriggerRules.PlayerShareGrew(playerBefore, playerNow, thresholdPp)) continue;   // the player did not gain enough

                    for (int i = 0; i < input.Rivals.Count; i++)
                    {
                        RivalMarketView rival = input.Rivals[i];
                        if (rival == null || rival.IsBankrupt || rival.IsFoundry) continue;

                        string key = RivalKey(rival.UniqueId);
                        float rivalBefore = ShareOf(before, key, segment);
                        float rivalNow = ShareOf(now, key, segment);
                        if (!TriggerRules.IsShareGain(playerBefore, playerNow, rivalBefore, rivalNow, thresholdPp)) continue;
                        if (_recorder.Record(TriggerType.ShareGain, rival.UniqueId, (Segment)segment, null)) gains++;
                    }
                }
            }

            state.shareSnapshot = ShareSnapshots.FromTable(month, now);
            return gains;
        }

        /// <summary>FR-5 item 3.</summary>
        private int EvaluateUndercuts(MonthlyInput input)
        {
            RivalState state = State;
            RivalConfig.TriggerThresholdSettings t = Config.triggerThresholds;
            int count = 0;

            for (int i = 0; i < input.Rivals.Count; i++)
            {
                RivalMarketView rival = input.Rivals[i];
                if (rival == null || rival.IsBankrupt || rival.IsFoundry || rival.Best == null) continue;

                for (int segment = 0; segment < ShareCalculator.SegmentCount && segment < rival.Best.Length; segment++)
                {
                    CpuBest best = rival.Best[segment];
                    if (!best.Present) continue;
                    if (UndercutBook.IsCoolingDown(state, rival.UniqueId, (Segment)segment, input.Today)) continue;

                    PlayerCpuView cheap = FindUndercutter(input.PlayerCpus, (Segment)segment, best, t);
                    if (cheap == null) continue;

                    if (!_recorder.Record(TriggerType.PriceUndercut, rival.UniqueId, (Segment)segment, cheap.Name)) continue;
                    UndercutBook.Start(state, rival.UniqueId, (Segment)segment, input.Today, t.undercutCooldownDays);
                    count++;
                }
            }
            return count;
        }

        /// <summary>FR-5 item 6: +2 for each active contract.</summary>
        private int EvaluateContracts(MonthlyInput input)
        {
            int count = 0;
            for (int i = 0; i < input.ContractRivals.Count; i++)
            {
                if (_recorder.RecordContract(ContractMove.ActiveMonthly, input.ContractRivals[i])) count++;
            }
            return count;
        }

        // ---- internals ----

        /// <summary>The CPU of the player in the segment that undercuts the rival's best one; of several, the fastest.</summary>
        private static PlayerCpuView FindUndercutter(List<PlayerCpuView> cpus, Segment segment, CpuBest best, RivalConfig.TriggerThresholdSettings t)
        {
            PlayerCpuView found = null;
            if (cpus == null) return null;
            for (int i = 0; i < cpus.Count; i++)
            {
                PlayerCpuView cpu = cpus[i];
                if (cpu == null || cpu.Segment != segment) continue;
                if (!TriggerRules.IsPriceUndercut(cpu.Ips, cpu.Price, best.Ips, best.Price, t.undercutIpsRatio, t.undercutPriceRatio)) continue;
                if (found == null || cpu.Ips > found.Ips) found = cpu;
            }
            return found;
        }

        private static float ShareOf(Dictionary<string, float[]> table, string key, int segment)
        {
            float[] row;
            return table.TryGetValue(key, out row) && row != null && segment >= 0 && segment < row.Length ? row[segment] : 0f;
        }

        private static string RivalKey(int uniqueId)
        {
            return CompanyKey.ForAi(uniqueId) ?? string.Empty;
        }

        private int Step(string name, Action body)
        {
            try
            {
                body();
                return 0;
            }
            catch (Exception e)
            {
                if (_log != null) _log("monthly step '" + name + "' failed (" + e.GetType().Name + ": " + e.Message + ")");
                return 1;
            }
        }
    }
}
