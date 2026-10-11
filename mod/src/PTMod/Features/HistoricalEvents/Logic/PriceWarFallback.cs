using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The actions of the effect <c>PriceWar</c> (design.md, Component 8).</summary>
    public static class WarActions
    {
        /// <summary>The aggressor starts the war: its prices and those of the other AI companies in the war's segments go down.</summary>
        public const string Start = "start";

        /// <summary>The aggressor does not start it; a player who declined hands the war to the next candidate.</summary>
        public const string Decline = "decline";

        /// <summary>The player follows: the prices of its CPUs in the war's segments go down by <c>followCut</c>.</summary>
        public const string Join = "join";

        public const string Hold = "hold";

        /// <summary>Marketing instead of discounts: <c>marketingCostPct</c> of the revenue, <c>popularity.gain</c> × 1.10 for 180 days.</summary>
        public const string Marketing = "marketing";

        public static readonly string[] All = { Start, Decline, Join, Hold, Marketing };
    }

    /// <summary>
    /// The numbers of a price war (the members of the effect <c>PriceWar</c>, FR-8, design.md formula 7). What the effect leaves out comes from the block
    /// <c>priceWarDefaults</c> of <c>params.json</c> (the floor margin and the marketing), then from the code.
    /// </summary>
    public sealed class WarParams
    {
        public string WarId = string.Empty;

        /// <summary>The segments of the war, as <c>Segment</c> values (the four concrete ones).</summary>
        public List<Segment> Segments = new List<Segment> { Segment.LowEnd };

        /// <summary>The cut of the aggressor, as a share of the price (0.15 is 15 %).</summary>
        public float Cut = 0.15f;

        /// <summary>The cut of the other AI companies, and of the player who follows.</summary>
        public float FollowCut = 0.08f;

        public int DurationDays = 180;

        /// <summary>No price falls below <c>RealUnitCost × (1 + floorMargin)</c>.</summary>
        public float FloorMargin = 0.05f;

        public float MarketingCostPct = 0.01f;
        public float MarketingGain = 1.10f;
        public int MarketingDays = 180;

        /// <summary>Reads the numbers; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static WarParams Read(JsonObject raw, JsonObject defaults, List<string> problems)
        {
            var r = new ParamReader(raw, defaults, "PriceWar", problems);
            var p = new WarParams();

            r.Choice("action", WarActions.Start, WarActions.All);
            p.WarId = r.Str("warId", string.Empty);
            p.Segments = r.Segments("segments", p.Segments);
            p.Cut = r.Num("cut", p.Cut, 0f, 0.5f);
            p.FollowCut = r.Num("followCut", p.FollowCut, 0f, 0.5f);
            p.DurationDays = r.Int("durationDays", p.DurationDays, 7, 1095);
            p.FloorMargin = r.Num("floorMargin", p.FloorMargin, 0f, 0.5f);
            p.MarketingCostPct = r.Num("marketingCostPct", p.MarketingCostPct, 0f, 0.1f);
            p.MarketingGain = r.Num("marketingPopularityGain", p.MarketingGain, 1f, 1.5f);
            p.MarketingDays = r.Int("marketingDays", p.MarketingDays, 1, 1095);
            r.WarnUnknown();
            return p;
        }

        public static string ActionOf(JsonObject raw)
        {
            return raw == null ? WarActions.Start : raw.GetString("action", WarActions.Start) ?? WarActions.Start;
        }

        /// <summary>The position of the war's segments in the arguments of the firing (after the id of the war).</summary>
        public const int SegmentsArgIndex = HistArgs.FirstExtraIndex + 1;

        public const int CutArgIndex = HistArgs.FirstExtraIndex + 2;
        public const int FollowCutArgIndex = HistArgs.FirstExtraIndex + 3;
        public const int DaysArgIndex = HistArgs.FirstExtraIndex + 4;

        /// <summary>
        /// The calendar entry's own numbers. The events <c>hist.decide.pricewar.aggressor</c> and <c>.follower</c> are shared by the wars of several entries (1985, 1998, 2006,
        /// 2017), which differ in the segments and the depth, so the fire passes them as its extra arguments. The form of the calendar is two arguments,
        /// <c>[warId, "segments;cut;followCut;durationDays"]</c> (<c>Args[4]</c>, <c>Args[5]</c>: the notes of FR-14 take the positions after the second extra argument), the
        /// segments as names joined by <c>|</c> (<c>lowend|midrange</c>), the numbers in the invariant culture, a part may be left empty. The form of five arguments,
        /// <c>[warId, segments, cut, followCut, durationDays]</c> (<c>Args[4]</c> to <c>Args[8]</c>), is read too. An argument that is missing or empty keeps the value of the
        /// event; one that is not valid is ignored with a line in <paramref name="problems"/> (may be null).
        /// </summary>
        public void ApplyArgs(string[] args, List<string> problems)
        {
            string id = HistArgs.At(args, HistArgs.FirstExtraIndex);
            if (WarId.Length == 0 && id.Length > 0) WarId = id;

            string spec = HistArgs.At(args, SegmentsArgIndex);
            string segments, cutText, followText, daysText;
            if (spec.IndexOf(';') >= 0)
            {
                string[] parts = spec.Split(';');
                segments = PartOf(parts, 0);
                cutText = PartOf(parts, 1);
                followText = PartOf(parts, 2);
                daysText = PartOf(parts, 3);
            }
            else
            {
                segments = spec;
                cutText = HistArgs.At(args, CutArgIndex);
                followText = HistArgs.At(args, FollowCutArgIndex);
                daysText = HistArgs.At(args, DaysArgIndex);
            }

            if (segments.Length > 0)
            {
                var parsed = new List<Segment>();
                foreach (string name in segments.Split('|', ','))
                {
                    Segment segment;
                    if (!HistSegments.TryParse(name.Trim(), out segment))
                    {
                        if (problems != null) problems.Add("PriceWar: the segment \"" + name + "\" in the arguments of the firing is not known");
                        parsed.Clear();
                        break;
                    }
                    if (segment == Segment.Any)
                    {
                        foreach (Segment each in ParamReader.AllSegments) if (!parsed.Contains(each)) parsed.Add(each);
                    }
                    else if (!parsed.Contains(segment))
                    {
                        parsed.Add(segment);
                    }
                }
                if (parsed.Count > 0) Segments = parsed;
            }

            Cut = ShareArg(cutText, Cut, problems);
            FollowCut = ShareArg(followText, FollowCut, problems);

            int value;
            if (daysText.Length > 0)
            {
                if (int.TryParse(daysText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 7 && value <= 1095) DurationDays = value;
                else if (problems != null) problems.Add("PriceWar: the duration \"" + daysText + "\" in the arguments of the firing is not a number of days from 7 to 1095");
            }
        }

        private static string PartOf(string[] parts, int index)
        {
            return index < parts.Length && parts[index] != null ? parts[index].Trim() : string.Empty;
        }

        private static float ShareArg(string text, float fallback, List<string> problems)
        {
            if (text.Length == 0) return fallback;

            float value;
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0f && value <= 0.5f) return value;
            if (problems != null) problems.Add("PriceWar: the cut \"" + text + "\" in the arguments of the firing is not a number from 0 to 0.5");
            return fallback;
        }
    }

    /// <summary>A CPU on sale as the price war sees it: who owns it, its segment, its prices and what it costs to make.</summary>
    public sealed class MarketCpu
    {
        public int SaveId;
        public string CompanyKey;
        public bool IsPlayer;

        /// <summary>The main segment (D15): an AI company's <c>TargetMarket</c>, the player's by sales and then by price.</summary>
        public Segment Segment;

        public float Price;

        /// <summary><c>AIStrategy.TargetPrice</c>; 0 or less when the CPU has none.</summary>
        public float TargetPrice;

        public float RealUnitCost;
    }

    /// <summary>The prices of the CPUs on sale (in the game <c>GamePrices</c>: live reads and writes of <c>Cpu.Price</c> and <c>AIStrategy.TargetPrice</c>).</summary>
    public interface IMarketPrices
    {
        /// <summary>The CPUs on sale now (released, not retired, sold on the market), read fresh.</summary>
        IReadOnlyList<MarketCpu> MarketCpus();

        /// <summary>The CPU by its id, or null when it is gone or no longer on sale.</summary>
        MarketCpu Find(int saveId);

        void SetPrice(int saveId, float price);

        void SetTargetPrice(int saveId, float target);
    }

    /// <summary>
    /// The price-cut door of <c>rival-personalities</c> (<c>IRivalPersonalitiesApi</c>) without the game's types: the company is its key. In the game
    /// <c>RivalPricesApi</c> maps it; in the tests it is a recording fake.
    /// </summary>
    public interface IPriceCutApi
    {
        /// <summary>True while <c>rival-personalities</c> runs (the cuts are then its business; the feature never writes an AI price).</summary>
        bool IsEnabled { get; }

        /// <summary><c>ApplyPriceCut(reactionId, rival, segment, depth, floorMargin)</c>: the number of CPUs it cut (0: none, or the feature declined).</summary>
        int ApplyPriceCut(string reactionId, string rivalKey, Segment segment, float depth, float floorMargin);

        void RestorePriceCut(string reactionId);
    }

    /// <summary>The cut of a price with a floor (design.md formula 7, the fallback while <c>rival-personalities</c> is off; FR-8 item 2).</summary>
    public static class PriceWarFallback
    {
        /// <summary>The lowest price of a CPU in a war: <c>RealUnitCost × (1 + floorMargin)</c>.</summary>
        public static float Floor(float realUnitCost, float floorMargin)
        {
            return HistMoney.Positive(realUnitCost) * (1f + Math.Max(0f, HistMoney.Finite(floorMargin, 0f)));
        }

        /// <summary>
        /// <c>max(floor, value × (1 − cut))</c>, but never above the value itself: a price that is already below the floor (a CPU sold at a loss) is left alone, the war
        /// does not raise it. A value of 0 or less stays.
        /// </summary>
        public static float CutValue(float value, float cut, float floor)
        {
            if (!(value > 0f) || float.IsInfinity(value)) return value;
            float cutValue = value * (1f - Math.Max(0f, Math.Min(1f, HistMoney.Finite(cut, 0f))));
            return Math.Min(value, Math.Max(floor, cutValue));
        }

        /// <summary>
        /// The price and the target price after a cut by <paramref name="cut"/>: both go to <c>max(floor, original × (1 − cut))</c>; a target of 0 or less (a CPU
        /// the strategy has not priced yet) is left as it is.
        /// </summary>
        public static void Cut(float price, float targetPrice, float realUnitCost, float cut, float floorMargin, out float newPrice, out float newTarget)
        {
            float floor = Floor(realUnitCost, floorMargin);
            newPrice = CutValue(price, cut, floor);
            newTarget = targetPrice > 0f ? CutValue(targetPrice, cut, floor) : targetPrice;
        }
    }

    /// <summary>
    /// The price wars of the game in progress (design.md, Component 8: <c>PriceWarService</c> without the glue; FR-8). A war cuts the prices of the CPUs in its segments
    /// and gives them back when its term is over.
    /// <list type="bullet">
    /// <item><b>While <c>rival-personalities</c> runs</b> (<see cref="IPriceCutApi.IsEnabled"/>), the AI companies' cuts go through
    /// <c>ApplyPriceCut("hist:&lt;warId&gt;", rival, segment, depth, floorMargin)</c> — the aggressor with <c>cut</c>, the others with <c>followCut</c> — and are given
    /// back with <c>RestorePriceCut("hist:&lt;warId&gt;")</c>; the feature writes no AI price itself (AC-8.2).</item>
    /// <item><b>Otherwise</b> the target price and the price of each AI CPU on sale in the war's segments go down with the floor <c>RealUnitCost × (1 + floorMargin)</c>;
    /// the original target prices are kept in the save and put back when the war ends, the prices then move by the AI's own logic (AC-8.1).</item>
    /// <item><b>The player</b> cuts its own prices only when it chooses to (start as the aggressor, or follow): one time, never restored.</item>
    /// <item><b>Ends:</b> on its day, when the feature is switched off, and for a CPU whose company went bankrupt. A CPU that is gone is passed over.</item>
    /// </list>
    /// </summary>
    public sealed class PriceWarCore
    {
        /// <summary>The channel of the marketing choice.</summary>
        public const string PopularityChannel = "popularity.gain";

        /// <summary>The reason shown for the popularity bonus of the marketing choice.</summary>
        public const string MarketingReasonKey = "hist.mod.warMarketing";

        public const string NoteEnded = "hist.note.war.ended";

        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly IHistModifiers _modifiers;
        private readonly IPriceCutApi _api;
        private readonly IMarketPrices _prices;
        private readonly Func<IList<Segment>, IReadOnlyList<string>> _ranking;
        private readonly Action<string> _log;

        /// <param name="ranking">The companies by share in the given segments, the best first (the share cache); used to pass the war to the next candidate when the player declines. May be null.</param>
        public PriceWarCore(Func<HistSaveData> data, IHistEconomy economy, IHistModifiers modifiers, IPriceCutApi api, IMarketPrices prices,
            Func<IList<Segment>, IReadOnlyList<string>> ranking = null, Action<string> log = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (economy == null) throw new ArgumentNullException("economy");
            if (modifiers == null) throw new ArgumentNullException("modifiers");
            if (api == null) throw new ArgumentNullException("api");
            if (prices == null) throw new ArgumentNullException("prices");
            _data = data;
            _economy = economy;
            _modifiers = modifiers;
            _api = api;
            _prices = prices;
            _ranking = ranking;
            _log = log;
        }

        /// <summary>The id the war's cuts carry in <c>rival-personalities</c>: <c>hist:&lt;warId&gt;</c> (AC-8.2).</summary>
        public static string ReactionId(string warId)
        {
            return "hist:" + warId;
        }

        /// <summary>The wars in progress (a view; do not change).</summary>
        public IReadOnlyList<PriceWarDto> Wars
        {
            get { return _data().priceWars; }
        }

        public PriceWarDto Find(string warId)
        {
            if (string.IsNullOrEmpty(warId)) return null;
            foreach (PriceWarDto w in _data().priceWars)
            {
                if (w != null && string.Equals(w.warId, warId, StringComparison.Ordinal)) return w;
            }
            return null;
        }

        // ---- the actions ----

        /// <summary>
        /// The aggressor starts the war: the war is there, the aggressor's CPUs in the war's segments are cut by <c>cut</c> (the player's by the price, the AI's through
        /// the API or the fallback) and every other AI company's by <c>followCut</c>. False when the war was already started or the id is empty.
        /// </summary>
        public bool Start(WarParams p, string aggressorKey)
        {
            if (p == null || string.IsNullOrEmpty(p.WarId)) return false;

            PriceWarDto war = Ensure(p);
            if (war.viaRivalApi || war.restore.Count > 0)
            {
                Log("war " + p.WarId + " has been started already; the second start is ignored");
                return false;
            }

            if (CompanyKey.IsPlayer(aggressorKey)) CutPlayer(p, p.Cut);
            CutRivals(p, war, aggressorKey);
            return true;
        }

        /// <summary>
        /// The aggressor does not start the war. A player who declined hands it to the next candidate: the AI company with the biggest share in the war's segments that
        /// sells there. Returns the key of the company that started it, or null.
        /// </summary>
        public string Decline(WarParams p, string deciderKey)
        {
            if (p == null || !CompanyKey.IsPlayer(deciderKey)) return null;

            string next = NextCandidate(p);
            if (next == null)
            {
                Log("war " + p.WarId + ": the player declined and no AI company is left to start it");
                return null;
            }

            Start(p, next);
            return next;
        }

        /// <summary>The player follows: its CPUs in the war's segments are cut by <c>followCut</c>. Returns how many.</summary>
        public int Join(WarParams p, string companyKey)
        {
            if (p == null || string.IsNullOrEmpty(p.WarId)) return 0;
            Ensure(p);
            return CompanyKey.IsPlayer(companyKey) ? CutPlayer(p, p.FollowCut) : 0;
        }

        /// <summary>The player holds its prices: the war is there, nothing else happens.</summary>
        public void Hold(WarParams p)
        {
            if (p != null && !string.IsNullOrEmpty(p.WarId)) Ensure(p);
        }

        /// <summary>Marketing instead of discounts: <c>marketingCostPct</c> of the revenue is spent and the company's <c>popularity.gain</c> is multiplied for <c>marketingDays</c>.</summary>
        public bool Marketing(WarParams p, string companyKey)
        {
            if (p == null || !_economy.IsAlive(companyKey)) return false;
            if (!string.IsNullOrEmpty(p.WarId)) Ensure(p);

            _economy.Spend(companyKey, HistMoney.Of(p.MarketingCostPct, _economy.Revenue12m(companyKey)), HistExpense.Development);
            _modifiers.Add(PopularityChannel, companyKey, p.MarketingGain, p.MarketingDays, MarketingReasonKey, null);
            return true;
        }

        // ---- the clock ----

        /// <summary>The daily pass: a war whose term is over ends. O(wars).</summary>
        public void OnDay()
        {
            HistSaveData data = _data();
            if (data.priceWars.Count == 0) return;

            int today = _economy.Today;
            foreach (PriceWarDto war in new List<PriceWarDto>(data.priceWars))
            {
                if (war != null && today >= war.endDay) End(war, true);
            }
        }

        /// <summary>The feature was switched off in the middle of a war: every war ends now and the prices go back (FR-17 item 3).</summary>
        public void OnFeatureDisabled()
        {
            foreach (PriceWarDto war in new List<PriceWarDto>(_data().priceWars))
            {
                if (war != null) End(war, false);
            }
        }

        /// <summary>A company went bankrupt: the saved targets of its CPUs are put back now (the CPU may be gone already).</summary>
        public void OnCompaniesChanged()
        {
            foreach (PriceWarDto war in _data().priceWars)
            {
                if (war == null) continue;

                for (int i = war.restore.Count - 1; i >= 0; i--)
                {
                    PriceRestoreDto entry = war.restore[i];
                    MarketCpu cpu = _prices.Find(entry.cpuSaveId);
                    if (cpu != null && _economy.IsAlive(cpu.CompanyKey)) continue;

                    if (cpu != null) _prices.SetTargetPrice(entry.cpuSaveId, entry.originalTargetPrice);
                    war.restore.RemoveAt(i);
                }
            }
        }

        // ---- internals ----

        private PriceWarDto Ensure(WarParams p)
        {
            PriceWarDto war = Find(p.WarId);
            if (war != null) return war;

            war = new PriceWarDto { warId = p.WarId, endDay = _economy.Today + p.DurationDays };
            _data().priceWars.Add(war);
            return war;
        }

        private void End(PriceWarDto war, bool tellThePlayer)
        {
            try
            {
                if (war.viaRivalApi) _api.RestorePriceCut(ReactionId(war.warId));

                foreach (PriceRestoreDto entry in war.restore)
                {
                    // A CPU that is gone or retired is passed over (design.md, "Error Handling").
                    if (_prices.Find(entry.cpuSaveId) != null) _prices.SetTargetPrice(entry.cpuSaveId, entry.originalTargetPrice);
                }
            }
            catch (Exception ex)
            {
                Log("war " + war.warId + " could not give every price back: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                _data().priceWars.Remove(war);
            }

            if (tellThePlayer) _economy.Notify(CompanyKey.Player, NoteEnded);
        }

        /// <summary>The player's own CPUs in the war's segments: <c>max(floor, price × (1 − cut))</c>, one time. Returns how many prices went down.</summary>
        private int CutPlayer(WarParams p, float cut)
        {
            int cutCount = 0;
            foreach (MarketCpu cpu in new List<MarketCpu>(_prices.MarketCpus()))
            {
                if (!cpu.IsPlayer || !p.Segments.Contains(cpu.Segment)) continue;

                float price = PriceWarFallback.CutValue(cpu.Price, cut, PriceWarFallback.Floor(cpu.RealUnitCost, p.FloorMargin));
                if (price < cpu.Price)
                {
                    _prices.SetPrice(cpu.SaveId, price);
                    cutCount++;
                }
            }
            return cutCount;
        }

        /// <summary>Every AI company that sells in the war's segments: the aggressor by <c>cut</c>, the rest by <c>followCut</c>.</summary>
        private void CutRivals(WarParams p, PriceWarDto war, string aggressorKey)
        {
            bool viaApi = _api.IsEnabled;
            var done = new HashSet<string>(StringComparer.Ordinal);

            foreach (MarketCpu cpu in new List<MarketCpu>(_prices.MarketCpus()))
            {
                if (cpu.IsPlayer || string.IsNullOrEmpty(cpu.CompanyKey) || !p.Segments.Contains(cpu.Segment) || !_economy.IsAlive(cpu.CompanyKey)) continue;

                float depth = string.Equals(cpu.CompanyKey, aggressorKey, StringComparison.Ordinal) ? p.Cut : p.FollowCut;
                if (depth <= 0f) continue;

                if (viaApi)
                {
                    // One call per company and segment; the feature owns the prices while it runs (AC-8.2), so nothing is written here.
                    if (!done.Add(cpu.CompanyKey + "|" + (int)cpu.Segment)) continue;
                    int cutCount = _api.ApplyPriceCut(ReactionId(p.WarId), cpu.CompanyKey, cpu.Segment, depth, p.FloorMargin);
                    if (cutCount > 0) war.viaRivalApi = true;
                }
                else
                {
                    CutByFallback(war, cpu, depth, p.FloorMargin);
                }
            }
        }

        private void CutByFallback(PriceWarDto war, MarketCpu cpu, float depth, float floorMargin)
        {
            float price, target;
            PriceWarFallback.Cut(cpu.Price, cpu.TargetPrice, cpu.RealUnitCost, depth, floorMargin, out price, out target);

            if (cpu.TargetPrice > 0f && target < cpu.TargetPrice)
            {
                if (!war.restore.Exists(r => r.cpuSaveId == cpu.SaveId)) war.restore.Add(new PriceRestoreDto { cpuSaveId = cpu.SaveId, originalTargetPrice = cpu.TargetPrice });
                _prices.SetTargetPrice(cpu.SaveId, target);
            }
            if (price < cpu.Price) _prices.SetPrice(cpu.SaveId, price);
        }

        /// <summary>The AI company with the biggest share in the war's segments that has a CPU on sale there.</summary>
        private string NextCandidate(WarParams p)
        {
            var sellers = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarketCpu cpu in _prices.MarketCpus())
            {
                if (!cpu.IsPlayer && cpu.CompanyKey != null && p.Segments.Contains(cpu.Segment) && _economy.IsAlive(cpu.CompanyKey)) sellers.Add(cpu.CompanyKey);
            }

            if (_ranking != null)
            {
                IReadOnlyList<string> ranked = _ranking(p.Segments);
                for (int i = 0; ranked != null && i < ranked.Count; i++)
                {
                    if (sellers.Contains(ranked[i])) return ranked[i];
                }
            }

            // No ranking: the smallest id among the sellers, so the choice is the same every time.
            string best = null;
            foreach (string key in sellers)
            {
                if (best == null || string.CompareOrdinal(key, best) < 0) best = key;
            }
            return best;
        }

        private void Log(string message)
        {
            if (_log != null) _log(message);
        }
    }
}
