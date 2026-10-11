using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Reputation;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using MarketDataProvider = ProcessorTycoon.MarketSystem.MarketDataProvider;
using MarketEventHandler = ProcessorTycoon.MarketSystem.Events.MarketEventHandler;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The quarterly notes of the press on the game's side (FR-10, design.md Component 9): in January, April, July and October (<c>TickPhase.MonthPassed</c>)
    /// the press picks up to two notes from <c>notes.json</c> whose condition holds and which are not resting (<see cref="NoteSelection"/>) and sends them in
    /// one letter "Press: market review" (<c>Low</c>, tag <c>press:notes:&lt;yyyy-MM&gt;</c>). The conditions are judged on the facts the press collects
    /// itself, not on <c>ModConditions</c>: the leader's growth and the median price from the yearly snapshots of the market
    /// (<see cref="PressState.yearStats"/>, taken on 1 January, <see cref="YearlySnapshot"/>), the PGA flag from the game's market events, the player's
    /// reputation from <c>ModReputation.History</c> and the new companies from the archive of reviews. The text carries tens of percent and words only.
    /// The dice are the stream <c>press-reviews/notes</c>. A quarter that has its letter already (the tag) is not done again.
    /// </summary>
    public sealed class PressNotesService
    {
        private const string Feature = PressReviewsFeature.FeatureId;
        private const string StreamId = Feature + "/notes";
        private const int NewEntrantMonths = 3;
        private const int PgaMonths = 12;

        private readonly Func<PressState> stateOf;

        public PressNotesService(Func<PressState> stateOf)
        {
            if (stateOf == null) throw new ArgumentNullException("stateOf");
            this.stateOf = stateOf;
        }

        /// <summary><c>TickPhase.MonthPassed</c>: in the months 1, 4, 7 and 10 the notes of the quarter; in the other months nothing.</summary>
        public void OnQuarter()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime) || !NoteSelection.IsQuarterMonth(today.Month)) return;

            IReadOnlyList<NoteTemplate> notes = PressContent.Current.Notes;
            if (notes == null || notes.Count == 0) return;

            if (ModMail.Find(Feature, NotesMailText.TagOf(today)) != null)
            {
                ModLog.Debug(Feature, "notes: the notes of " + PressDates.ToMonthText(today) + " are sent already");
                return;
            }

            PressState state = stateOf();
            if (today.Month == 1) EnsureSnapshot(state, today.Year - 1);

            NoteFacts facts = BuildFacts(state, today);
            NoteCooldowns.Prune(state.noteCooldowns, today);
            var rng = new ModRngSource(ModRandom.Stream(StreamId));
            List<NoteTemplate> picked = NoteSelection.Pick(notes, facts, state.noteCooldowns, today, NoteSelection.MaxPerQuarter, rng);
            if (picked.Count == 0)
            {
                ModLog.Debug(Feature, "notes: no note fits " + PressDates.ToMonthText(today));
                return;
            }

            var lines = new List<string>(picked.Count);
            for (int i = 0; i < picked.Count; i++)
            {
                lines.Add(NoteRenderer.Render(picked[i], facts, PressUi.Words));
                NoteCooldowns.Rest(state.noteCooldowns, picked[i].Id, NoteCooldowns.UntilOf(today, picked[i].CooldownQuarters));
            }
            PressMail.SendNotes(today, lines);
            ModLog.Info(Feature, "notes: " + PressDates.ToMonthText(today) + ": " + string.Join(", ", picked.ConvertAll(n => n.Id).ToArray()));
        }

        /// <summary>
        /// <c>TickPhase.LateYearPassed</c>: the snapshot of the market of the year that has just ended (the leader's <c>Ips</c> and the median price per
        /// segment). A year that has its snapshot is left alone.
        /// </summary>
        public void YearlySnapshot()
        {
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;
            EnsureSnapshot(stateOf(), today.Year - 1);
        }

        // ---- the snapshot ----

        private static void EnsureSnapshot(PressState state, int year)
        {
            if (state.yearStats == null) state.yearStats = new List<SegmentYearStat>();
            if (MarketStatsBook.HasYear(state.yearStats, year)) return;

            PeerConfig peers = PressContent.Current.Config.Peers;
            int stored = 0;
            for (int segment = 0; segment < 4; segment++)
            {
                MarketSample sample = MarketSampler.Sample(segment, peers);
                if (!MarketTrends.HasValue(sample.LeaderIps) && !MarketTrends.HasValue(sample.MedianPrice)) continue;

                MarketStatsBook.Upsert(state.yearStats, year, segment, sample.LeaderIps, sample.MedianPrice);
                stored++;
            }
            ModLog.Debug(Feature, "notes: the market of " + year + " is recorded for " + stored + " segment(s)");
        }

        // ---- the facts ----

        private static NoteFacts BuildFacts(PressState state, DateTime today)
        {
            var facts = new NoteFacts();
            MarketStatsBook.FillFacts(state.yearStats, today.Year - 1, facts);

            facts.PgaShift = PgaCameRecently(today);

            var player = ModCompanies.Resolve(ModCompanies.PlayerKey);
            facts.PlayerCompanyName = player == null ? string.Empty : (player.Name ?? string.Empty);

            IReadOnlyList<ReputationChange> history = ModReputation.History(ModCompanies.PlayerKey);
            facts.ReputationChange = history.Count == 0
                ? float.NaN
                : ReputationTable.ChangeOverYear(history, ModReputation.Get(ModCompanies.PlayerKey), today);

            facts.NewEntrantName = MarketStatsBook.FindNewEntrant(state.reviews, today.AddMonths(-NewEntrantMonths), today, ModCompanies.PlayerKey) ?? string.Empty;
            return facts;
        }

        /// <summary>The first PGA or LGA CPU of the world came to the market within the last year (the game remembers the day).</summary>
        private static bool PgaCameRecently(DateTime today)
        {
            try
            {
                MarketEventHandler events = MarketEventHandler.Instance;
                if (events == null || !events.HasSeenPgaEvent) return false;

                DateTime since = events.PgaDate;
                return since > today.AddMonths(-PgaMonths) && since <= today;
            }
            catch (Exception ex)
            {
                ModLog.Debug(Feature, "notes: the PGA flag of the market could not be read: " + ex.GetType().Name);
                return false;
            }
        }
    }

    /// <summary>The leader's performance and the median price of one segment, as the market shows them (NaN where unknown).</summary>
    internal struct MarketSample
    {
        public float LeaderIps;
        public float MedianPrice;
    }

    /// <summary>Reads the market of the game for the yearly snapshot: the best-selling CPUs of a segment over the last 12 full months.</summary>
    internal static class MarketSampler
    {
        private const int Months = 12;

        /// <summary>
        /// The segment's leader (the highest <c>Ips</c> among the <see cref="PeerConfig.Count"/> best sellers) and the median price of the best sellers
        /// (<see cref="PeerConfig.Candidates"/> of them), over CPUs that sold in the segment during the last 12 full months. Unknown when nothing sold, or the
        /// market has no list of CPUs yet.
        /// </summary>
        internal static MarketSample Sample(int segment, PeerConfig peers)
        {
            var sample = new MarketSample { LeaderIps = float.NaN, MedianPrice = float.NaN };
            if (segment < 0 || segment > 3) return sample;

            MarketDataProvider market = MarketDataProvider.Instance;
            if (market == null) return sample;

            List<Cpu> ranked;
            try
            {
                ranked = market.MostSoldCpus(peers.Candidates, Months, segment == 0, segment == 1, segment == 2, segment == 3, false);
            }
            catch (NullReferenceException)
            {
                return sample;   // the market has not cached its CPUs yet (before the first month of a new game)
            }
            if (ranked == null) return sample;

            var leaders = new List<float>();
            var prices = new List<float>();
            for (int i = 0; i < ranked.Count; i++)
            {
                Cpu cpu = ranked[i];
                if (cpu == null || SoldInSegment(cpu, segment) <= 0) continue;

                if (leaders.Count < peers.Count) leaders.Add(cpu.Ips);
                prices.Add(cpu.Price);
            }
            sample.LeaderIps = MarketTrends.LeaderIps(leaders);
            sample.MedianPrice = MarketTrends.Median(prices);
            return sample;
        }

        private static int SoldInSegment(Cpu cpu, int segment)
        {
            if (cpu.Market == null) return 0;

            switch (segment)
            {
                case 0: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByHighEnd, Months);
                case 1: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByMidRange, Months);
                case 2: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByLowEnd, Months);
                default: return PeerFilter.SoldOverFullMonths(cpu.Market.SoldByIndustries, Months);
            }
        }
    }
}
