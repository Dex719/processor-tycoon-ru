using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The eras and the segments of the market (design.md, "GpuEras, GpuRivals, GpuRivalGenerator, GpuNews"; FR-11; F33, F34). The segments of the market mode in force
    /// (<c>balance.marketMode</c>) open and close with the calendar: a segment is open from 1 January of <c>opensYear</c> up to 1 January of <c>closesYear</c> (0: never
    /// closes). A closed segment sells nothing - the market asks <see cref="IsOpen"/> every day - but the cards in it are not deleted (FR-11 item 5).
    /// <list type="bullet">
    /// <item><b>The month</b> (<see cref="MonthTick"/>, <c>TickPhase.MonthPassed</c>): by the DIFFERENCE OF DATES - what opened or closed, and which era began, since the last
    /// comparison (<c>market.erasCheckedOn</c>) is announced once (<see cref="SegmentOpened"/>, <see cref="SegmentClosed"/>, <see cref="EraStarted"/>), so a feature that was
    /// off for years catches up on its first month; the news decide which of the events are fresh enough for a letter. A new segment gives the player's cards on sale the
    /// starting popularity <c>popularity.start</c> (0.05) in it (FR-11 item 4).</item>
    /// <item><b>The requirements</b> of a segment: the features the architecture must have (<see cref="RequiredFeatures"/>, F34 - each missing one multiplies the rating by
    /// <c>missingFeatureFactor</c>) and the video memory (<see cref="VramRequirement"/>, F33 - log-linear between control years, the rating is multiplied by
    /// <c>clamp(vram / required, 0.25, 1)</c>). The market applies both to every card in every segment (<see cref="GpuMarketMath.FeatureFactor"/>,
    /// <see cref="GpuMarketMath.VramFactor"/>).</item>
    /// <item><b>The mode</b> (<see cref="ApplyModeSwitchIfNeeded"/>, on the game being ready): a save of stage 1 (<c>single</c>) in the build of stage 2 (<c>eras</c>) keeps the
    /// popularity of its cards (the segment <c>consumer</c> is copied with the factor 1.0 into every open segment of the eras), and the background and the vendors are made
    /// again for the year by the seed (FR-11 item 6).</item>
    /// </list>
    /// Eras (<c>eras.json</c>) exist in the mode <c>eras</c> only.
    /// </summary>
    public sealed class GpuEras
    {
        private readonly GpuContentDb _content;
        private readonly GpuState _state;
        private readonly IGpuGame _game;

        public GpuEras(GpuContentDb content, GpuState state, IGpuGame game)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (state == null) throw new ArgumentNullException("state");
            if (game == null) throw new ArgumentNullException("game");
            _content = content;
            _state = state;
            _game = game;
        }

        /// <summary>A segment opened (it is open now; announced on the first month tick after its 1 January).</summary>
        public event Action<GpuSegmentDto> SegmentOpened;

        /// <summary>A segment closed (announced on the first month tick after its closing 1 January).</summary>
        public event Action<GpuSegmentDto> SegmentClosed;

        /// <summary>An era began (announced on the first month tick after its 1 January); only in the mode <c>eras</c>.</summary>
        public event Action<GpuEraDto> EraStarted;

        private GpuBalanceDto Balance
        {
            get { return _content.Balance ?? new GpuBalanceDto(); }
        }

        private GpuMarketSave Market
        {
            get
            {
                GpuSaveSection data = _state.Data;
                if (data.market == null) data.market = new GpuMarketSave();
                return data.market;
            }
        }

        /// <summary>The market is in the mode of the eras (the segments are the six of the eras, the eras of <c>eras.json</c> are announced).</summary>
        public bool IsErasMode
        {
            get { return Balance.marketMode == GpuContentDb.ModeEras; }
        }

        // ---- the calendar ----

        /// <summary>A segment is open on a date: <c>opensYear &lt;= year</c> and (<c>closesYear</c> is 0 or <c>year &lt; closesYear</c>). The one rule of the market.</summary>
        public static bool IsOpen(GpuSegmentDto seg, DateTime date)
        {
            return seg != null && seg.opensYear <= date.Year && (seg.closesYear <= 0 || date.Year < seg.closesYear);
        }

        /// <summary>The segments of the market mode that are open at the date, in the order of the content (FR-11 item 1).</summary>
        public List<GpuSegmentDto> OpenAt(DateTime date)
        {
            var open = new List<GpuSegmentDto>();
            foreach (GpuSegmentDto seg in _content.Segments)
            {
                if (IsOpen(seg, date)) open.Add(seg);
            }
            return open;
        }

        /// <summary>The era in force at the date: the one with the latest <c>startYear</c> not after the year; null before the first (and always null outside the mode <c>eras</c>).</summary>
        public GpuEraDto CurrentEra(DateTime date)
        {
            if (!IsErasMode) return null;
            GpuEraDto best = null;
            foreach (GpuEraDto era in _content.Eras)
            {
                if (era.startYear <= date.Year && (best == null || era.startYear >= best.startYear)) best = era;
            }
            return best;
        }

        /// <summary>The year of the next era after the date, or 0 when none is left (the tab "Рынок" shows when the next one comes).</summary>
        public int NextEraYear(DateTime date)
        {
            if (!IsErasMode) return 0;
            int next = 0;
            foreach (GpuEraDto era in _content.Eras)
            {
                if (era.startYear > date.Year && (next == 0 || era.startYear < next)) next = era.startYear;
            }
            return next;
        }

        // ---- the requirements (F33, F34) ----

        /// <summary>F34: the features the segment asks for in a year (the entry of <c>featureReqs</c> with the latest year not after it).</summary>
        public string[] RequiredFeatures(GpuSegmentDto segment, int year)
        {
            return segment == null ? new string[0] : GpuMarketMath.RequiredFeatures(segment.featureReqs, year);
        }

        /// <summary>F33: the video memory the segment asks for at a moment of a year (<c>year + (month - 1) / 12</c>), MB: log-linear between the control years; 0 - none.</summary>
        public float VramRequirement(GpuSegmentDto segment, float year)
        {
            return segment == null ? 0f : GpuMarketMath.VramRequirement(segment.vramReq, year);
        }

        // ---- the month ----

        /// <summary>
        /// A month passed (<c>TickPhase.MonthPassed</c>; FR-11 items 1, 4, 5): announces what the calendar did since the last comparison and gives the player's cards the
        /// starting popularity in a segment that opened. Nothing happens before the market is seeded, or on the first comparison of a save that has none (it only sets the date).
        /// </summary>
        public void MonthTick()
        {
            GpuMarketSave market = Market;
            if (!market.seeded) return;

            DateTime today = _game.Today;
            GpuDate last = market.erasCheckedOn;
            market.erasCheckedOn = DateOf(today);
            if (last == null) return;
            DateTime since = DateTimeOf(last);
            if (today <= since) return;

            var opened = new List<GpuSegmentDto>();
            var closed = new List<GpuSegmentDto>();
            foreach (GpuSegmentDto seg in _content.Segments)
            {
                DateTime opens = FirstOfYear(seg.opensYear);
                if (opens > since && opens <= today && IsOpen(seg, today)) opened.Add(seg);
                if (seg.closesYear > 0)
                {
                    DateTime closes = FirstOfYear(seg.closesYear);
                    if (closes > since && closes <= today && opens <= since) closed.Add(seg);
                }
            }
            var started = new List<GpuEraDto>();
            if (IsErasMode)
            {
                foreach (GpuEraDto era in _content.Eras)
                {
                    DateTime begins = FirstOfYear(era.startYear);
                    if (begins > since && begins <= today) started.Add(era);
                }
                started.Sort((a, b) => a.startYear != b.startYear ? a.startYear.CompareTo(b.startYear) : string.CompareOrdinal(a.id, b.id));
            }

            foreach (GpuSegmentDto seg in opened) GiveStartPopularity(seg);

            foreach (GpuSegmentDto seg in closed)
            {
                Action<GpuSegmentDto> handlers = SegmentClosed;
                if (handlers != null) handlers(seg);
            }
            foreach (GpuSegmentDto seg in opened)
            {
                Action<GpuSegmentDto> handlers = SegmentOpened;
                if (handlers != null) handlers(seg);
            }
            foreach (GpuEraDto era in started)
            {
                Action<GpuEraDto> handlers = EraStarted;
                if (handlers != null) handlers(era);
            }
        }

        // FR-11 item 4: the cards of the player that are on sale get the starting popularity in the segment that opened. A card of a vendor is made for its own
        // segment, the vendors launch their cards in the new segment themselves.
        private void GiveStartPopularity(GpuSegmentDto seg)
        {
            float start = Balance.popularity.start;
            foreach (GpuProductSave p in _state.Data.products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer || p.status != GpuState.StatusSelling) continue;
                if (p.segIds == null) p.segIds = new List<string>();
                if (p.pop == null) p.pop = new List<float>();
                if (p.segIds.Contains(seg.id)) continue;
                while (p.pop.Count < p.segIds.Count) p.pop.Add(start);
                p.segIds.Add(seg.id);
                p.pop.Add(start);
            }
        }

        // ---- the mode (FR-11 item 6) ----

        /// <summary>
        /// A save of another market mode than the content's (stage 1 <c>single</c> in the build of stage 2 <c>eras</c>, or back): the popularity of the player's cards in the
        /// segments the new mode does not have (<c>consumer</c>) is copied, with the factor 1.0, into every open segment of the new mode (the larger value when the card has
        /// one there already) and those entries are dropped; the background and the cards and the states of the vendors are thrown away and <c>market.seeded</c> is cleared, so
        /// the next <c>GpuMarketService.StartSession</c> seeds the market of the year again. A game that was never seeded just takes the mode. True when it switched.
        /// </summary>
        public bool ApplyModeSwitchIfNeeded(GpuState state)
        {
            GpuState target = state ?? _state;
            GpuSaveSection data = target.Data;
            string mode = Balance.marketMode;
            if (data.marketModeApplied == mode) return false;

            if (data.market == null) data.market = new GpuMarketSave();
            GpuMarketSave market = data.market;
            if (!market.seeded)
            {
                data.marketModeApplied = mode;
                return false;
            }

            DateTime today = _game.Today;
            List<GpuSegmentDto> open = OpenAt(today);
            foreach (GpuProductSave p in data.products)
            {
                if (p == null || p.owner != GpuState.OwnerPlayer || p.segIds == null) continue;
                if (p.pop == null) p.pop = new List<float>();
                while (p.pop.Count < p.segIds.Count) p.pop.Add(Balance.popularity.start);

                float carried = 0f;
                bool any = false;
                for (int i = p.segIds.Count - 1; i >= 0; i--)
                {
                    if (_content.Segment(p.segIds[i]) != null) continue;   // a segment of the new mode stays as it is
                    carried = Math.Max(carried, p.pop[i]);
                    any = true;
                    p.segIds.RemoveAt(i);
                    p.pop.RemoveAt(i);
                }
                if (!any) continue;

                foreach (GpuSegmentDto seg in open)
                {
                    int at = p.segIds.IndexOf(seg.id);
                    if (at >= 0)
                    {
                        p.pop[at] = Math.Max(p.pop[at], carried);
                    }
                    else
                    {
                        p.segIds.Add(seg.id);
                        p.pop.Add(carried);
                    }
                }
            }

            data.products.RemoveAll(p => p != null && (p.owner == GpuState.OwnerBackground || p.owner == GpuState.OwnerVendor));
            data.vendors = new List<GpuVendorSave>();
            market.seeded = false;
            market.lastBackgroundRefresh = null;
            market.erasCheckedOn = DateOf(today);
            data.marketModeApplied = mode;
            return true;
        }

        // ---- dates ----

        private static DateTime FirstOfYear(int year)
        {
            return new DateTime(Math.Min(9999, Math.Max(1, year)), 1, 1);
        }

        private static DateTime DateTimeOf(GpuDate date)
        {
            return new DateTime(Math.Min(9999, Math.Max(1, date.y)), Math.Min(12, Math.Max(1, date.m)), 1).AddDays(Math.Max(1, date.d) - 1);
        }

        private static GpuDate DateOf(DateTime date)
        {
            return new GpuDate { y = date.Year, m = date.Month, d = date.Day };
        }
    }
}
