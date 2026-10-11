using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>The attributes a race is run on (<c>attribute</c> of the effect <c>AttributeRace</c>).</summary>
    public static class RaceAttributes
    {
        public const string Frequency = "frequency";
        public const string CoreCount = "coreCount";

        public static readonly string[] All = { Frequency, CoreCount };

        /// <summary>The reason shown for the contribution of a race to <c>market.rating.cpu</c> (FR-11 item 1).</summary>
        public static string ReasonKey(string attribute)
        {
            return attribute == CoreCount ? "hist.mod.race.cores" : "hist.mod.race.frequency";
        }
    }

    /// <summary>
    /// The numbers of an attribute race (the members of the effect <c>AttributeRace</c>, FR-11, design.md formula 9). The bonus is at most 1.10, which keeps the
    /// contribution of a race to the rating of a CPU in [1.00, 1.10] (FR-11 item 3).
    /// </summary>
    public sealed class RaceParams
    {
        public const float MaxBonus = 1.10f;

        public string RaceId = string.Empty;
        public string Attribute = RaceAttributes.Frequency;
        public int TopN = 3;
        public float Bonus = MaxBonus;
        public List<Segment> Segments = new List<Segment> { Segment.HighEnd, Segment.MidRange, Segment.LowEnd };
        public int DurationDays = 1460;

        /// <summary>Reads the numbers; a bad member is a line in <paramref name="problems"/> (may be null) and keeps its default. Never null.</summary>
        public static RaceParams Read(JsonObject raw, List<string> problems)
        {
            var r = new ParamReader(raw, null, "AttributeRace", problems);
            var p = new RaceParams();

            p.RaceId = r.Str("raceId", string.Empty);
            p.Attribute = r.Choice("attribute", RaceAttributes.Frequency, RaceAttributes.All);
            p.TopN = r.Int("topN", p.TopN, 1, 10);
            p.Bonus = r.Num("bonus", p.Bonus, 1f, MaxBonus);
            p.Segments = r.Segments("segments", p.Segments);
            p.DurationDays = r.Int("durationDays", p.DurationDays, 30, 7300);
            r.WarnUnknown();
            return p;
        }
    }

    /// <summary>The arithmetic of a race without the game: who is in the top.</summary>
    public static class RaceMath
    {
        /// <summary>
        /// The <paramref name="topN"/> CPUs on the market with the highest attribute: the frequency, or the number of cores; a tie goes to the faster CPU
        /// (<c>Ips</c>), then to the smaller <c>SaveId</c>, so the answer never depends on the order of the list. Only released, not retired CPUs on sale count.
        /// </summary>
        public static List<int> TopCpus(IReadOnlyList<CpuInfo> cpus, string attribute, int topN)
        {
            var candidates = new List<CpuInfo>();
            for (int i = 0; cpus != null && i < cpus.Count; i++)
            {
                if (WorldMath.IsOnMarket(cpus[i])) candidates.Add(cpus[i]);
            }

            bool cores = attribute == RaceAttributes.CoreCount;
            candidates.Sort((a, b) =>
            {
                int byAttribute = cores ? b.Cores.CompareTo(a.Cores) : b.Frequency.CompareTo(a.Frequency);
                if (byAttribute != 0) return byAttribute;
                int byIps = b.Ips.CompareTo(a.Ips);
                return byIps != 0 ? byIps : a.SaveId.CompareTo(b.SaveId);
            });

            var top = new List<int>();
            for (int i = 0; i < candidates.Count && top.Count < Math.Max(0, topN); i++) top.Add(candidates[i].SaveId);
            return top;
        }
    }

    /// <summary>What the provider of <c>market.rating.cpu</c> sums up from the rules and the races (design.md: the provider "returns export × race with the reason of the larger deviation").</summary>
    public static class HistRatingMath
    {
        /// <summary>
        /// The product of the two contributions, with the reason of the one that moves the rating more (the larger distance from 1, in ratio). A contribution of 1
        /// has no reason; when both are 1 the answer is 1 without one.
        /// </summary>
        public static ProviderResult Combine(float exportMultiplier, string exportReason, string[] exportArgs, float raceMultiplier, string raceReason, string[] raceArgs)
        {
            float export = HistMoney.Finite(exportMultiplier, 1f);
            float race = HistMoney.Finite(raceMultiplier, 1f);
            float product = export * race;

            bool hasExport = Math.Abs(export - 1f) > 1e-6f;
            bool hasRace = Math.Abs(race - 1f) > 1e-6f;
            if (!hasExport && !hasRace) return new ProviderResult(1f);
            if (hasExport && !hasRace) return new ProviderResult(product, exportReason, exportArgs);
            if (!hasExport) return new ProviderResult(product, raceReason, raceArgs);

            double exportDeviation = Math.Abs(Math.Log(export));
            double raceDeviation = Math.Abs(Math.Log(race));
            return exportDeviation >= raceDeviation ? new ProviderResult(product, exportReason, exportArgs) : new ProviderResult(product, raceReason, raceArgs);
        }
    }

    /// <summary>A race as the provider reads it: the CPUs of the top in a set, the segments as a mask.</summary>
    internal sealed class RaceView
    {
        public string RaceId;
        public string Attribute;
        public int TopN;
        public float Bonus;
        public int Mask;
        public int EndDay;
        public HashSet<int> Top;
    }

    /// <summary>
    /// The attribute races of the game in progress (design.md, Component 8: <c>RaceService</c> without the glue; FR-11): a race starts, once a month the top of the
    /// market by its attribute is worked out again and the channel <c>market.rating.cpu</c> is invalidated, when the term is over the race ends and its
    /// contribution goes. The provider asks <see cref="Contribution"/>, which is O(races) with a set lookup. The state is the list <c>races</c> of the save section.
    /// </summary>
    public sealed class RaceCore
    {
        public const string RatingChannel = "market.rating.cpu";

        private readonly Func<HistSaveData> _data;
        private readonly IHistEconomy _economy;
        private readonly IHistModifiers _modifiers;
        private readonly IWorldView _world;
        private readonly Action<string> _log;

        private HistSaveData _builtFor;
        private bool _dirty = true;
        private RaceView[] _views = new RaceView[0];

        public RaceCore(Func<HistSaveData> data, IHistEconomy economy, IHistModifiers modifiers, IWorldView world, Action<string> log = null)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (economy == null) throw new ArgumentNullException("economy");
            if (modifiers == null) throw new ArgumentNullException("modifiers");
            if (world == null) throw new ArgumentNullException("world");
            _data = data;
            _economy = economy;
            _modifiers = modifiers;
            _world = world;
            _log = log;
        }

        /// <summary>The races that are running (a view; do not change).</summary>
        public IReadOnlyList<RaceDto> Races
        {
            get { return _data().races; }
        }

        public RaceDto Find(string raceId)
        {
            if (string.IsNullOrEmpty(raceId)) return null;
            foreach (RaceDto r in _data().races)
            {
                if (r != null && string.Equals(r.raceId, raceId, StringComparison.Ordinal)) return r;
            }
            return null;
        }

        /// <summary>A race starts; its top is worked out now. False when the id is empty or already running.</summary>
        public bool Start(RaceParams p)
        {
            if (p == null || string.IsNullOrEmpty(p.RaceId)) return false;
            if (Find(p.RaceId) != null)
            {
                Log("race " + p.RaceId + " is already running; the second start is ignored");
                return false;
            }

            var race = new RaceDto
            {
                raceId = p.RaceId,
                attribute = p.Attribute,
                topN = p.TopN,
                bonus = p.Bonus,
                endDay = _economy.Today + p.DurationDays,
                topCpuIds = RaceMath.TopCpus(_world.Cpus, p.Attribute, p.TopN),
            };
            foreach (Segment s in p.Segments) race.segments.Add((int)s);

            _data().races.Add(race);
            Changed();
            return true;
        }

        /// <summary>The monthly pass (priority <see cref="HistTickPriority.Races"/>): the top is worked out again, a race whose term is over ends, the channel is invalidated.</summary>
        public void OnMonth()
        {
            HistSaveData data = _data();
            if (data.races.Count == 0) return;

            int today = _economy.Today;
            foreach (RaceDto race in new List<RaceDto>(data.races))
            {
                if (race == null) continue;
                if (today >= race.endDay)
                {
                    data.races.Remove(race);
                    continue;
                }
                race.topCpuIds = RaceMath.TopCpus(_world.Cpus, race.attribute, race.topN);
            }
            Changed();
        }

        /// <summary>Forgets the flat copy (the feature was switched off).</summary>
        public void Forget()
        {
            _builtFor = null;
            _dirty = true;
        }

        /// <summary>
        /// What the races do to the rating of the CPU <paramref name="cpuSaveId"/> in this <paramref name="segment"/>: the strongest bonus of the races whose top it is in
        /// (1 when none), with the reason key and the number of the top for the text. O(races).
        /// </summary>
        public bool Contribution(int cpuSaveId, Segment segment, out float multiplier, out string reasonKey, out int topN)
        {
            multiplier = 1f;
            reasonKey = null;
            topN = 0;

            RaceView[] views = Views();
            if (views.Length == 0) return false;

            int bit = ExportRuleMath.Bit(segment);
            if (bit == 0) return false;

            int today = _economy.Today;
            for (int i = 0; i < views.Length; i++)
            {
                RaceView view = views[i];
                if ((view.Mask & bit) == 0 || today >= view.EndDay || !view.Top.Contains(cpuSaveId)) continue;
                if (view.Bonus > multiplier)
                {
                    multiplier = view.Bonus;
                    reasonKey = RaceAttributes.ReasonKey(view.Attribute);
                    topN = view.TopN;
                }
            }
            return multiplier > 1f;
        }

        private RaceView[] Views()
        {
            HistSaveData data = _data();
            if (!_dirty && ReferenceEquals(_builtFor, data)) return _views;

            var views = new List<RaceView>(data.races.Count);
            foreach (RaceDto race in data.races)
            {
                if (race == null) continue;
                views.Add(new RaceView
                {
                    RaceId = race.raceId,
                    Attribute = race.attribute,
                    TopN = race.topN,
                    Bonus = Math.Max(1f, Math.Min(RaceParams.MaxBonus, race.bonus)),
                    Mask = ExportRuleMath.MaskOfNumbers(race.segments),
                    EndDay = race.endDay,
                    Top = new HashSet<int>(race.topCpuIds ?? new List<int>()),
                });
            }

            _views = views.ToArray();
            _builtFor = data;
            _dirty = false;
            return _views;
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
