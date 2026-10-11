using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The actions of the effect <c>ExportControl</c> (design.md, Component 8).</summary>
    public static class ExportActions
    {
        /// <summary>The rule comes into force (the global event).</summary>
        public const string Open = "open";

        /// <summary>The rule is tightened: a new percentile, a new share, a new term (the second stage, 2023-10-17).</summary>
        public const string Update = "update";

        /// <summary>The company complies; nothing is paid (the letter only names the threshold).</summary>
        public const string Comply = "comply";

        public const string License = "license";
        public const string Lobby = "lobby";

        public static readonly string[] All = { Open, Update, Comply, License, Lobby };
    }

    /// <summary>The kinds of exemption from a rule (<c>ExemptionDto.kind</c>).</summary>
    public static class ExemptionKinds
    {
        /// <summary>The company may sell above the threshold (the rule does not touch it).</summary>
        public const string License = "license";

        /// <summary>The company lobbied: the rule's share is multiplied by the factor of the exemption.</summary>
        public const string Lobby = "lobby";
    }

    /// <summary>The keys of the notes about the answers of the regulator (all in the string tables of the feature).</summary>
    public static class ExportKeys
    {
        public const string NoteLicenseGranted = "hist.note.export.licenseGranted";
        public const string NoteLicenseDenied = "hist.note.export.licenseDenied";
        public const string NoteLobbyWon = "hist.note.export.lobbyWon";
        public const string NoteLobbyLost = "hist.note.export.lobbyLost";

        /// <summary>The reason of the contribution of a rule to <c>market.rating.cpu</c>; its argument is the threshold.</summary>
        public const string ReasonExport = "hist.mod.export";
    }

    /// <summary>
    /// The numbers of an export rule (the members of the effect <c>ExportControl</c>, FR-7, design.md formula 6). Every member has a default; the percentile and the
    /// share are those of the first stage of the real rule. The blocked share is at most 0.20, which is what keeps the contribution of the rule to the rating of
    /// a CPU in [0.80, 1.00] (FR-7 item 7).
    /// </summary>
    public sealed class ExportParams
    {
        public const float MaxBlockedShare = 0.20f;

        public string RuleId = string.Empty;

        /// <summary>The threshold <c>T0</c> is this percentile of the <c>Ips</c> of the CPUs on the market when the rule is introduced.</summary>
        public float Percentile = 0.90f;

        /// <summary>The share of the buyers of a CPU above the threshold that the rule takes away.</summary>
        public float BlockedShare = 0.15f;

        /// <summary>How fast the threshold follows the market: <c>T = T0 × (AverageIps / AverageIps0) ^ indexing</c>.</summary>
        public float Indexing = 0.8f;

        public List<Segment> Segments = new List<Segment> { Segment.HighEnd };
        public int DurationDays = 3650;

        public float LicenseCostPct = 0.02f;
        public float LicenseSuccess = 0.6f;
        public int LicenseMonths = 18;
        public float LobbyCostPct = 0.01f;
        public float LobbySuccess = 0.5f;

        /// <summary>The multiplier of the blocked share for a company whose lobbying succeeded (AC-7.2: 0.5, so 1 − 0.15 × 0.5 = 0.925).</summary>
        public float LobbyFactor = 0.5f;

        public int LobbyMonths = 24;

        /// <summary>Reads the numbers; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static ExportParams Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "ExportControl", problems);
            var p = new ExportParams();

            r.Choice("action", ExportActions.Open, ExportActions.All);
            p.RuleId = r.Str("ruleId", string.Empty);
            p.Percentile = r.Num("percentile", p.Percentile, 0.05f, 0.99f);
            p.BlockedShare = r.Num("blockedShare", p.BlockedShare, 0f, MaxBlockedShare);
            p.Indexing = r.Num("indexing", p.Indexing, 0f, 2f);
            p.Segments = r.Segments("segments", p.Segments);
            p.DurationDays = r.Int("durationDays", p.DurationDays, 30, 7300);
            p.LicenseCostPct = r.Num("licenseCostPct", p.LicenseCostPct, 0f, 0.2f);
            p.LicenseSuccess = r.Num("licenseSuccess", p.LicenseSuccess, 0f, 1f);
            p.LicenseMonths = r.Int("licenseMonths", p.LicenseMonths, 1, 240);
            p.LobbyCostPct = r.Num("lobbyCostPct", p.LobbyCostPct, 0f, 0.2f);
            p.LobbySuccess = r.Num("lobbySuccess", p.LobbySuccess, 0f, 1f);
            p.LobbyFactor = r.Num("lobbyFactor", p.LobbyFactor, 0f, 1f);
            p.LobbyMonths = r.Int("lobbyMonths", p.LobbyMonths, 1, 240);
            r.WarnUnknown();
            return p;
        }

        public static string ActionOf(JsonObject raw)
        {
            return raw == null ? ExportActions.Open : raw.GetString("action", ExportActions.Open) ?? ExportActions.Open;
        }
    }

    /// <summary>What an action of the effect costs, worked out when the event is resolved (the numbers under the buttons).</summary>
    public sealed class ExportQuote
    {
        public string Action;
        public string TextKey;

        /// <summary>The money paid when the action is applied.</summary>
        public float Upfront;

        public float Success;
        public int Months;
        public float Factor;
    }

    /// <summary>The arithmetic of an export rule without the game (design.md formula 6; FR-7).</summary>
    public static class ExportRuleMath
    {
        /// <summary>
        /// The <paramref name="p"/>-th percentile (0 to 1) of the values by linear interpolation between the closest ranks: <c>h = (n − 1) × p</c>, the value at
        /// <c>h</c>. 11 values give the 90th percentile at the tenth value exactly. An empty list gives 0; values that are not finite are left out.
        /// </summary>
        public static float Percentile(IList<float> values, float p)
        {
            if (values == null || values.Count == 0) return 0f;

            var sorted = new List<float>(values.Count);
            foreach (float v in values)
            {
                if (!float.IsNaN(v) && !float.IsInfinity(v)) sorted.Add(v);
            }
            if (sorted.Count == 0) return 0f;

            sorted.Sort();
            double rank = (sorted.Count - 1) * Math.Max(0.0, Math.Min(1.0, p));
            int low = (int)Math.Floor(rank);
            int high = Math.Min(sorted.Count - 1, low + 1);
            double fraction = rank - low;
            return (float)(sorted[low] + (sorted[high] - sorted[low]) * fraction);
        }

        /// <summary>
        /// The threshold now, indexed to the market: <c>T0 × (avgNow / avg0) ^ indexing</c>. Without a usable average (0 or less, not a number) the threshold
        /// stays where it was.
        /// </summary>
        public static float Threshold(float threshold0, float avgIps0, float avgIpsNow, float indexing)
        {
            if (!(avgIps0 > 0f) || !(avgIpsNow > 0f) || float.IsInfinity(avgIps0) || float.IsInfinity(avgIpsNow)) return threshold0;
            double t = threshold0 * Math.Pow(avgIpsNow / avgIps0, indexing);
            return float.IsNaN((float)t) || float.IsInfinity((float)t) ? threshold0 : (float)t;
        }

        /// <summary>The multiplier of the rating of a CPU the rule reaches: <c>1 − blockedShare × lobbyFactor</c>, in [0.80, 1.00] for the shares the validator allows.</summary>
        public static float Multiplier(float blockedShare, float lobbyFactor)
        {
            float share = Math.Max(0f, Math.Min(ExportParams.MaxBlockedShare, HistMoney.Finite(blockedShare, 0f)));
            float factor = Math.Max(0f, Math.Min(1f, HistMoney.Finite(lobbyFactor, 1f)));
            return 1f - share * factor;
        }

        /// <summary>What an action costs (the up-front money and the figures the line shows).</summary>
        public static ExportQuote Quote(string action, ExportParams p, float revenue12m)
        {
            var q = new ExportQuote { Action = action };
            switch (action)
            {
                case ExportActions.Comply:
                    q.TextKey = "hist.fx.export.comply";
                    break;
                case ExportActions.License:
                    q.Upfront = HistMoney.Of(p.LicenseCostPct, revenue12m);
                    q.Success = p.LicenseSuccess;
                    q.Months = p.LicenseMonths;
                    q.TextKey = "hist.fx.export.license";
                    break;
                case ExportActions.Lobby:
                    q.Upfront = HistMoney.Of(p.LobbyCostPct, revenue12m);
                    q.Success = p.LobbySuccess;
                    q.Months = p.LobbyMonths;
                    q.Factor = p.LobbyFactor;
                    q.TextKey = "hist.fx.export.lobby";
                    break;
                case ExportActions.Update:
                    q.TextKey = "hist.fx.export.update";
                    break;
                default:
                    q.TextKey = "hist.fx.export.open";
                    break;
            }
            return q;
        }

        /// <summary>The bit of a segment in a mask of segments; 0 for <see cref="Segment.Any"/> and unknown values. No allocation (the provider calls it for every CPU).</summary>
        public static int Bit(Segment segment)
        {
            switch (segment)
            {
                case Segment.HighEnd: return 1;
                case Segment.MidRange: return 2;
                case Segment.LowEnd: return 4;
                case Segment.Industries: return 8;
                default: return 0;
            }
        }

        /// <summary>The mask of a list of segments.</summary>
        public static int MaskOf(IEnumerable<Segment> segments)
        {
            int mask = 0;
            if (segments != null) foreach (Segment s in segments) mask |= Bit(s);
            return mask;
        }

        /// <summary>The mask of the segments saved as numbers (<c>ExportRuleDto.segments</c>).</summary>
        public static int MaskOfNumbers(IEnumerable<int> segments)
        {
            int mask = 0;
            if (segments != null) foreach (int s in segments) mask |= Bit((Segment)s);
            return mask;
        }
    }

    /// <summary>One company's exemption in a rule as the provider reads it.</summary>
    internal struct ExemptionView
    {
        public float Factor;
        public int EndDay;
    }

    /// <summary>A rule as the provider of <c>market.rating.cpu</c> reads it: flat, with the exemptions in dictionaries (O(1) per question).</summary>
    internal sealed class ExportRuleView
    {
        public string RuleId;
        public float Threshold;
        public float BlockedShare;
        public int Mask;
        public int EndDay;
        public string ThresholdText;
        public readonly Dictionary<string, ExemptionView> Licenses = new Dictionary<string, ExemptionView>(StringComparer.Ordinal);
        public readonly Dictionary<string, ExemptionView> Lobbies = new Dictionary<string, ExemptionView>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The export rules of the game in progress (design.md, Component 8: <c>ExportRuleService</c> without the glue; FR-7): the rule comes into force from the market
    /// as it is, is tightened, is indexed once a month, ends; a company may buy a licence or lobby. The state is the list <c>exportRules</c> of the save section; the
    /// provider of <c>market.rating.cpu</c> reads <see cref="Contribution"/>, which answers from a flat copy rebuilt when the state changes and when the section
    /// object is replaced by a load, so a loaded rule works at once (AC: the provider reads the restored rule). Every change of the state calls
    /// <c>ModModifiers.Invalidate("market.rating.cpu")</c>.
    /// </summary>
    public sealed class ExportCore
    {
        public const string RatingChannel = "market.rating.cpu";

        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly IHistModifiers _modifiers;
        private readonly IWorldView _world;
        private readonly Func<float, string> _formatIps;
        private readonly Action<string> _log;

        private HistSaveData _builtFor;
        private bool _dirty = true;
        private ExportRuleView[] _views = new ExportRuleView[0];

        public ExportCore(Func<HistSaveData> data, IHistEconomy economy, IHistModifiers modifiers, IWorldView world, Func<float, string> formatIps = null,
            Action<string> log = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (economy == null) throw new ArgumentNullException("economy");
            if (modifiers == null) throw new ArgumentNullException("modifiers");
            if (world == null) throw new ArgumentNullException("world");
            _data = data;
            _economy = economy;
            _modifiers = modifiers;
            _world = world;
            _formatIps = formatIps ?? (ips => ips.ToString("0"));
            _log = log;
        }

        /// <summary>The rules in force (a view; do not change).</summary>
        public IReadOnlyList<ExportRuleDto> Rules
        {
            get { return _data().exportRules; }
        }

        public ExportRuleDto Find(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId)) return null;
            foreach (ExportRuleDto r in _data().exportRules)
            {
                if (r != null && string.Equals(r.ruleId, ruleId, StringComparison.Ordinal)) return r;
            }
            return null;
        }

        /// <summary>The threshold of the rule in IPS, or null when the rule is not in force (the condition <c>hist.PlayerHasCpuAbove</c>).</summary>
        public float? ThresholdOf(string ruleId)
        {
            ExportRuleDto rule = Find(ruleId);
            return rule == null ? (float?)null : rule.threshold;
        }

        /// <summary>The <c>Ips</c> of the CPUs on the market now (released, not retired, on sale).</summary>
        public List<float> MarketIps()
        {
            var ips = new List<float>();
            IReadOnlyList<CpuInfo> cpus = _world.Cpus;
            for (int i = 0; cpus != null && i < cpus.Count; i++)
            {
                if (WorldMath.IsOnMarket(cpus[i])) ips.Add(cpus[i].Ips);
            }
            return ips;
        }

        /// <summary>The threshold a rule with this percentile would start at now (the preview the line of the event shows); 0 when no CPU is on the market.</summary>
        public float PreviewThreshold(float percentile)
        {
            return ExportRuleMath.Percentile(MarketIps(), percentile);
        }

        // ---- the rule ----

        /// <summary>The rule comes into force: <c>T0</c> is the percentile of the market now. False when the id is empty or already in force.</summary>
        public bool Open(ExportParams p)
        {
            if (p == null || string.IsNullOrEmpty(p.RuleId)) return false;
            if (Find(p.RuleId) != null)
            {
                Log("rule " + p.RuleId + " is already in force; the second introduction is ignored");
                return false;
            }

            float avg = _world.AverageIps;
            float t0 = StartingThreshold(p.Percentile, avg);
            var rule = new ExportRuleDto
            {
                ruleId = p.RuleId,
                threshold0 = t0,
                avgIps0 = avg,
                threshold = t0,
                percentile = p.Percentile,
                blockedShare = p.BlockedShare,
                indexing = p.Indexing,
                endDay = _economy.Today + p.DurationDays,
            };
            foreach (Segment s in p.Segments) rule.segments.Add((int)s);

            _data().exportRules.Add(rule);
            Changed();
            return true;
        }

        /// <summary>
        /// The rule is tightened (the second stage): a new <c>T0</c> from the new percentile of the market, the average re-based to today, the new share and term.
        /// A rule that is not in force is introduced instead. The exemptions that were bought stay until their own end.
        /// </summary>
        public bool Update(ExportParams p)
        {
            if (p == null || string.IsNullOrEmpty(p.RuleId)) return false;
            ExportRuleDto rule = Find(p.RuleId);
            if (rule == null) return Open(p);

            float avg = _world.AverageIps;
            rule.threshold0 = StartingThreshold(p.Percentile, avg);
            rule.avgIps0 = avg;
            rule.threshold = rule.threshold0;
            rule.percentile = p.Percentile;
            rule.blockedShare = p.BlockedShare;
            rule.indexing = p.Indexing;
            rule.endDay = Math.Max(rule.endDay, _economy.Today + p.DurationDays);
            rule.segments.Clear();
            foreach (Segment s in p.Segments) rule.segments.Add((int)s);

            Changed();
            return true;
        }

        /// <summary>
        /// A company buys a licence: <c>licenseCostPct</c> of its revenue is paid whatever the answer; with <c>licenseSuccess</c> (<paramref name="roll"/> below it) the
        /// company is exempt for <c>licenseMonths</c>. False when the rule is not in force (nothing is paid).
        /// </summary>
        public bool License(string ruleId, string companyKey, ExportParams p, float roll)
        {
            ExportRuleDto rule = Find(ruleId);
            if (rule == null || !_economy.IsAlive(companyKey)) return false;

            _economy.Spend(companyKey, HistMoney.Of(p.LicenseCostPct, _economy.Revenue12m(companyKey)), HistExpense.Fines);
            bool granted = roll < p.LicenseSuccess;
            if (granted)
            {
                SetExemption(rule, companyKey, ExemptionKinds.License, 0f, _economy.Today + HistTime.MonthsToDays(p.LicenseMonths));
                Changed();
            }
            _economy.Notify(companyKey, granted ? ExportKeys.NoteLicenseGranted : ExportKeys.NoteLicenseDenied);
            return true;
        }

        /// <summary>
        /// A company lobbies: <c>lobbyCostPct</c> of its revenue is paid whatever the answer; with <c>lobbySuccess</c> the share of the rule for the company is
        /// multiplied by <c>lobbyFactor</c> for <c>lobbyMonths</c>. A second success replaces the first. False when the rule is not in force.
        /// </summary>
        public bool Lobby(string ruleId, string companyKey, ExportParams p, float roll)
        {
            ExportRuleDto rule = Find(ruleId);
            if (rule == null || !_economy.IsAlive(companyKey)) return false;

            _economy.Spend(companyKey, HistMoney.Of(p.LobbyCostPct, _economy.Revenue12m(companyKey)), HistExpense.Fines);
            bool won = roll < p.LobbySuccess;
            if (won)
            {
                SetExemption(rule, companyKey, ExemptionKinds.Lobby, p.LobbyFactor, _economy.Today + HistTime.MonthsToDays(p.LobbyMonths));
                Changed();
            }
            _economy.Notify(companyKey, won ? ExportKeys.NoteLobbyWon : ExportKeys.NoteLobbyLost);
            return true;
        }

        // ---- the month ----

        /// <summary>
        /// The monthly pass (priority <see cref="HistTickPriority.ExportRules"/>): the threshold follows the market, exemptions that ran out are dropped, a rule whose term
        /// is over ends. The channel is invalidated whenever a rule is in force (the threshold moved) or one ended.
        /// </summary>
        public void OnMonth()
        {
            HistSaveData data = _data();
            if (data.exportRules.Count == 0) return;

            int today = _economy.Today;
            float avg = _world.AverageIps;
            foreach (ExportRuleDto rule in new List<ExportRuleDto>(data.exportRules))
            {
                if (rule == null) continue;
                if (today >= rule.endDay)
                {
                    data.exportRules.Remove(rule);
                    continue;
                }

                rule.threshold = ExportRuleMath.Threshold(rule.threshold0, rule.avgIps0, avg, rule.indexing);
                for (int i = rule.exemptions.Count - 1; i >= 0; i--)
                {
                    ExemptionDto x = rule.exemptions[i];
                    if (x == null || x.endDay <= today || !_economy.IsAlive(x.companyKey)) rule.exemptions.RemoveAt(i);
                }
            }
            Changed();
        }

        /// <summary>Drops everything the rules contribute (the feature was switched off): the provider answers 1 until the views are rebuilt.</summary>
        public void Forget()
        {
            _builtFor = null;
            _dirty = true;
        }

        // ---- the provider ----

        /// <summary>
        /// What the rules do to the rating of a CPU of <paramref name="companyKey"/> with this <paramref name="ips"/> in this <paramref name="segment"/>: the multiplier
        /// (the strongest of the rules that reach it, 1 when none does) and the text of the threshold for the reason. O(rules): the copy is flat and the exemptions are
        /// dictionaries.
        /// </summary>
        public bool Contribution(float ips, Segment segment, string companyKey, out float multiplier, out string thresholdText)
        {
            multiplier = 1f;
            thresholdText = null;

            ExportRuleView[] views = Views();
            if (views.Length == 0) return false;

            int bit = ExportRuleMath.Bit(segment);
            if (bit == 0) return false;

            int today = _economy.Today;
            for (int i = 0; i < views.Length; i++)
            {
                ExportRuleView view = views[i];
                if ((view.Mask & bit) == 0 || !(ips >= view.Threshold) || today >= view.EndDay) continue;

                ExemptionView exemption;
                if (companyKey != null && view.Licenses.TryGetValue(companyKey, out exemption) && exemption.EndDay > today) continue;

                float factor = 1f;
                if (companyKey != null && view.Lobbies.TryGetValue(companyKey, out exemption) && exemption.EndDay > today) factor = exemption.Factor;

                float m = ExportRuleMath.Multiplier(view.BlockedShare, factor);
                if (m < multiplier)
                {
                    multiplier = m;
                    thresholdText = view.ThresholdText;
                }
            }
            return multiplier < 1f;
        }

        private ExportRuleView[] Views()
        {
            HistSaveData data = _data();
            if (!_dirty && ReferenceEquals(_builtFor, data)) return _views;

            var views = new List<ExportRuleView>(data.exportRules.Count);
            foreach (ExportRuleDto rule in data.exportRules)
            {
                if (rule == null) continue;

                var view = new ExportRuleView
                {
                    RuleId = rule.ruleId,
                    Threshold = rule.threshold,
                    BlockedShare = rule.blockedShare,
                    Mask = ExportRuleMath.MaskOfNumbers(rule.segments),
                    EndDay = rule.endDay,
                    ThresholdText = _formatIps(rule.threshold),
                };
                foreach (ExemptionDto x in rule.exemptions)
                {
                    if (x == null || string.IsNullOrEmpty(x.companyKey)) continue;
                    var e = new ExemptionView { Factor = x.factor, EndDay = x.endDay };
                    if (x.kind == ExemptionKinds.License) view.Licenses[x.companyKey] = e;
                    else if (x.kind == ExemptionKinds.Lobby) view.Lobbies[x.companyKey] = e;
                }
                views.Add(view);
            }

            _views = views.ToArray();
            _builtFor = data;
            _dirty = false;
            return _views;
        }

        // ---- internals ----

        private float StartingThreshold(float percentile, float avgIps)
        {
            float t0 = ExportRuleMath.Percentile(MarketIps(), percentile);
            if (t0 > 0f) return t0;

            // No CPU on the market: a threshold of 0 would block every CPU that appears. Two averages is above what the market makes.
            Log("no CPU on the market for the percentile; the threshold starts at twice the average");
            return avgIps > 0f ? avgIps * 2f : 1f;
        }

        private static void SetExemption(ExportRuleDto rule, string companyKey, string kind, float factor, int endDay)
        {
            ExemptionDto existing = rule.exemptions.Find(x => x != null && x.kind == kind && string.Equals(x.companyKey, companyKey, StringComparison.Ordinal));
            if (existing == null)
            {
                rule.exemptions.Add(new ExemptionDto { companyKey = companyKey, kind = kind, factor = factor, endDay = endDay });
                return;
            }

            // A second success keeps the stronger factor and the later end.
            existing.factor = kind == ExemptionKinds.Lobby ? Math.Min(existing.factor, factor) : factor;
            existing.endDay = Math.Max(existing.endDay, endDay);
        }

        private void Changed()
        {
            _dirty = true;
            _modifiers.Invalidate(RatingChannel);
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
