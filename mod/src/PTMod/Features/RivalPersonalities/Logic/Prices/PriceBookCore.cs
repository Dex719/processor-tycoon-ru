using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// How the price book reads and writes a CPU of the game (<c>Cpu</c>: <c>Price</c>, <c>Strategy.TargetPrice</c>, <c>Strategy.TargetMarket</c>,
    /// <c>RealUnitCost</c>...). The game side implements it once for the game's type; the tests implement it on a plain class. The calls must not
    /// allocate: they run every game day.
    /// </summary>
    public interface IPriceCpuAccess<TCpu> where TCpu : class
    {
        /// <summary>The save id of the CPU (<c>SaveID</c>): the key of an entry of the book.</summary>
        int SaveIdOf(TCpu cpu);

        float PriceOf(TCpu cpu);

        void SetPrice(TCpu cpu, float price);

        /// <summary><c>Strategy.TargetPrice</c>: the price the AI moves the CPU to (5 a day down, <c>AIBehaviourMarket.ChangePrices</c>).</summary>
        float TargetPriceOf(TCpu cpu);

        void SetTargetPrice(TCpu cpu, float targetPrice);

        float RealUnitCostOf(TCpu cpu);

        /// <summary><c>Strategy.TargetMarket</c>: the segment the AI sells the CPU in, <c>(int)Segment</c>.</summary>
        int TargetMarketOf(TCpu cpu);

        /// <summary>The CPU is sold on the market: not retired and <c>SellOnMarket</c>.</summary>
        bool IsSoldOnMarket(TCpu cpu);

        /// <summary>The CPU with this save id wherever it is now (it may have moved to another company), or null when it no longer exists.</summary>
        TCpu FindCpu(int saveId);
    }

    /// <summary>
    /// The price book (design.md, Component 5; FR-7 item 3, FR-10, FR-13 item 6): the target prices that the price cuts took down, the cut itself and
    /// the way back. One mechanism for the <c>PriceCut</c> reaction (ids <c>r-&lt;n&gt;</c>) and for the price wars of other features through the API
    /// (their own ids, <c>&lt;prefix&gt;:&lt;name&gt;</c>). The entries are the list <see cref="RivalState.priceBook"/> of the section, so they
    /// survive a save; the state is asked from the provider at every call (a load replaces it).
    /// <list type="bullet">
    /// <item><see cref="ApplyCut"/>: every CPU the rival sells on the market in the segment gets <c>Price := min(Price, cut)</c> and
    /// <c>TargetPrice := min(TargetPrice, cut)</c> with <see cref="PriceCutMath.CutPrice"/>; the original target price is written down. A CPU that is
    /// under another cut already keeps the original price of the first one, so the way back always ends at the price before any cut.</item>
    /// <item><see cref="Clamp"/>, after every <c>AIBehaviourMarket.OnTick</c> of the rival: <c>TargetPrice := min(TargetPrice, cut)</c>. The game
    /// moves <c>Price</c> by itself (down to the target 5 a day, up on a shortage). An entry of a CPU that no longer exists is dropped.</item>
    /// <item><see cref="Restore"/>: the original target price comes back (and the cuts still in effect on the CPU are held again); the entries go.</item>
    /// <item><see cref="ExpireExternal"/>: a cut of an external caller that was not taken back ends after <c>priceCut.externalMaxDays</c> (400) days.</item>
    /// </list>
    /// Pure and generic over the game's CPU type. Nothing on the daily path (<see cref="Clamp"/>) allocates. Main thread only.
    /// </summary>
    public sealed class PriceBookCore<TCpu> where TCpu : class
    {
        /// <summary>The most external price cuts (distinct ids) in effect at once (FR-13 item 6, the contract of the API).</summary>
        public const int MaxExternalCuts = 20;

        private readonly Func<RivalState> _state;
        private readonly IPriceCpuAccess<TCpu> _access;
        private readonly Action<string> _warn;
        private readonly List<string> _ids = new List<string>();

        /// <param name="state">The section as it is now (never keep the object: a load replaces it).</param>
        /// <param name="access">How to read and write a CPU.</param>
        /// <param name="warn">One English line per refused call (the feature logs it); may be null.</param>
        public PriceBookCore(Func<RivalState> state, IPriceCpuAccess<TCpu> access, Action<string> warn = null)
        {
            if (access == null) throw new ArgumentNullException("access");
            _state = state;
            _access = access;
            _warn = warn;
        }

        private List<PriceBookEntry> Entries
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                if (state == null) return null;
                if (state.priceBook == null) state.priceBook = new List<PriceBookEntry>();
                return state.priceBook;
            }
        }

        // ---- applying ----

        /// <summary>
        /// Cuts the prices of the CPUs that the rival sells on the market in <paramref name="segment"/> (see the class) and returns how many CPUs the cut
        /// <paramref name="reactionId"/> holds now. 0 when the rival is bankrupt or dormant, for an unusable id or segment, when no CPU of the
        /// segment can be cut, and for a new external id while <see cref="MaxExternalCuts"/> external cuts are in effect. A second call with the same id
        /// adds the CPUs released since and does not cut the others again. <paramref name="depth"/> and <paramref name="floorMargin"/> are clipped to
        /// 0..0.5.
        /// </summary>
        /// <param name="rivalCpus">The CPUs of the rival (<c>AI.GetCpus()</c>).</param>
        /// <param name="today">The day the cut starts (<c>startedOn</c>, the start of the 400 days of an external cut).</param>
        public int ApplyCut(string reactionId, int uniqueId, IList<TCpu> rivalCpus, Segment segment, float depth, float floorMargin,
                            bool rivalBankrupt, DateTime today)
        {
            List<PriceBookEntry> entries = Entries;
            if (entries == null) return 0;
            if (!PriceCutMath.IsValidReactionId(reactionId))
            {
                Warn("price cut refused: the id '" + (reactionId ?? "null") + "' must be 1.." + Text(PriceCutMath.MaxReactionIdLength) + " characters");
                return 0;
            }
            if (segment < Segment.HighEnd || segment > Segment.Industries)
            {
                Warn("price cut '" + reactionId + "' refused: " + segment + " is not a segment of the market");
                return 0;
            }
            if (rivalBankrupt || IsDormant(uniqueId) || rivalCpus == null) return 0;

            bool external = !PriceCutMath.IsInternalId(reactionId);
            if (external && CountOf(entries, reactionId) == 0 && ExternalIdsInEffect(entries) >= MaxExternalCuts)
            {
                Warn("price cut '" + reactionId + "' refused: " + Text(MaxExternalCuts) + " external price cuts are in effect already");
                return 0;
            }

            float clippedDepth = PriceCutMath.ClipShare(depth);
            float clippedFloor = PriceCutMath.ClipShare(floorMargin);
            string startedOn = RivalDates.ToText(today);
            int market = (int)segment;

            for (int i = 0; i < rivalCpus.Count; i++)
            {
                TCpu cpu = rivalCpus[i];
                if (cpu == null || !_access.IsSoldOnMarket(cpu) || _access.TargetMarketOf(cpu) != market) continue;

                int saveId = _access.SaveIdOf(cpu);
                if (Find(entries, reactionId, saveId) != null) continue;   // held by this cut already

                float price = _access.PriceOf(cpu);
                float target = _access.TargetPriceOf(cpu);
                if (!PriceCutMath.IsUsablePrice(price) || !PriceCutMath.IsUsablePrice(target)) continue;   // not priced yet

                float cut = PriceCutMath.CutPrice(price, _access.RealUnitCostOf(cpu), clippedDepth, clippedFloor);
                if (cut >= price && cut >= target) continue;   // the floor is above the price: nothing to cut

                PriceBookEntry other = FirstOfCpu(entries, saveId);
                entries.Add(new PriceBookEntry
                {
                    cpuSaveId = saveId,
                    uid = uniqueId,
                    reactionId = reactionId,
                    baseTargetPrice = other != null ? other.baseTargetPrice : target,
                    cutTarget = cut,
                    startedOn = startedOn,
                });

                if (cut < price) _access.SetPrice(cpu, cut);
                if (cut < target) _access.SetTargetPrice(cpu, cut);
            }

            return CountOf(entries, reactionId);
        }

        /// <summary>
        /// Holds the target prices of the rival's CPUs under their cuts (after the rival's <c>AIBehaviourMarket.OnTick</c>): <c>TargetPrice :=
        /// min(TargetPrice, cut)</c>. An entry whose CPU no longer exists is dropped; a CPU that moved to another company gets its original target
        /// price back and its entries are dropped. Returns how many target prices were lowered. Allocates nothing.
        /// </summary>
        public int Clamp(int uniqueId, IList<TCpu> rivalCpus)
        {
            List<PriceBookEntry> entries = Entries;
            if (entries == null || entries.Count == 0) return 0;

            int lowered = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (i >= entries.Count) continue;   // a restore below removed more than one entry
                PriceBookEntry entry = entries[i];
                if (entry == null)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (entry.uid != uniqueId) continue;

                TCpu cpu = FindIn(rivalCpus, entry.cpuSaveId);
                if (cpu == null)
                {
                    TCpu moved = _access.FindCpu(entry.cpuSaveId);
                    if (moved != null) _access.SetTargetPrice(moved, entry.baseTargetPrice);   // no longer the rival's: give the price back
                    RemoveCpu(entries, entry.cpuSaveId, uniqueId);
                    continue;
                }

                float target = _access.TargetPriceOf(cpu);
                float held = PriceCutMath.ClampTarget(target, entry.cutTarget);
                if (held < target)
                {
                    _access.SetTargetPrice(cpu, held);
                    lowered++;
                }
            }
            return lowered;
        }

        // ---- the way back ----

        /// <summary>
        /// Ends the cut <paramref name="reactionId"/>: every CPU it held gets its original target price back, lowered again by the other cuts still in
        /// effect on it; the entries go. An entry whose CPU no longer exists is just dropped. Returns how many CPUs got a price back; 0 for an unknown id.
        /// </summary>
        public int Restore(string reactionId)
        {
            List<PriceBookEntry> entries = Entries;
            if (entries == null || string.IsNullOrEmpty(reactionId)) return 0;

            int restored = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (i >= entries.Count) continue;
                PriceBookEntry entry = entries[i];
                if (entry == null || !string.Equals(entry.reactionId, reactionId, StringComparison.Ordinal)) continue;

                entries.RemoveAt(i);
                TCpu cpu = _access.FindCpu(entry.cpuSaveId);
                if (cpu == null) continue;

                _access.SetTargetPrice(cpu, HeldTarget(entries, entry.cpuSaveId, entry.baseTargetPrice));
                restored++;
            }
            return restored;
        }

        /// <summary>Ends every cut on the rival's CPUs (a dormant or bankrupt rival, FR-13 item 3). Returns how many cuts ended.</summary>
        public int RestoreRival(int uniqueId)
        {
            List<PriceBookEntry> entries = Entries;
            if (entries == null) return 0;

            _ids.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry != null && entry.uid == uniqueId && !_ids.Contains(entry.reactionId)) _ids.Add(entry.reactionId);
            }

            int ended = 0;
            for (int i = 0; i < _ids.Count; i++)
            {
                RestoreOf(entries, _ids[i], uniqueId);
                ended++;
            }
            _ids.Clear();
            return ended;
        }

        /// <summary>
        /// The feature is switched off (FR-10 item 4): every CPU under a cut gets its original target price back, but the entries stay, so that the cuts
        /// go on when the feature is switched on again (the daily <see cref="Clamp"/> holds them again). Returns how many CPUs got a price back.
        /// </summary>
        public int ReleaseAll()
        {
            List<PriceBookEntry> entries = Entries;
            if (entries == null) return 0;

            int released = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry == null || FirstOfCpu(entries, entry.cpuSaveId) != entry) continue;   // once per CPU

                TCpu cpu = _access.FindCpu(entry.cpuSaveId);
                if (cpu == null) continue;
                _access.SetTargetPrice(cpu, entry.baseTargetPrice);
                released++;
            }
            return released;
        }

        /// <summary>
        /// Ends the external cuts that ran for <paramref name="maxDays"/> days or more (FR-13 item 6; <c>priceCut.externalMaxDays</c>, 400) and returns
        /// their ids. An entry without a readable start date counts from <paramref name="today"/>. The feature's own cuts (<c>r-&lt;n&gt;</c>) are left
        /// to the event system. Once a month, so it may allocate.
        /// </summary>
        public List<string> ExpireExternal(DateTime today, int maxDays)
        {
            var expired = new List<string>();
            List<PriceBookEntry> entries = Entries;
            if (entries == null || today == default(DateTime)) return expired;

            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry == null || PriceCutMath.IsInternalId(entry.reactionId)) continue;

                DateTime started;
                if (!RivalDates.TryParse(entry.startedOn, out started))
                {
                    entry.startedOn = RivalDates.ToText(today);
                    continue;
                }
                if (PriceCutMath.ExternalExpired(entry.reactionId, started, today, maxDays) && !expired.Contains(entry.reactionId))
                {
                    expired.Add(entry.reactionId);
                }
            }

            foreach (string id in expired) Restore(id);
            return expired;
        }

        // ---- reading ----

        /// <summary>How many CPUs the cut holds.</summary>
        public int CountOf(string reactionId)
        {
            List<PriceBookEntry> entries = Entries;
            return entries == null ? 0 : CountOf(entries, reactionId);
        }

        /// <summary>The cut is in effect on at least one CPU.</summary>
        public bool HasCut(string reactionId)
        {
            return CountOf(reactionId) > 0;
        }

        /// <summary>How many distinct external cuts are in effect.</summary>
        public int ExternalCount
        {
            get
            {
                List<PriceBookEntry> entries = Entries;
                return entries == null ? 0 : ExternalIdsInEffect(entries);
            }
        }

        /// <summary>The rival is put aside (<see cref="RivalState.dormant"/>).</summary>
        private bool IsDormant(int uniqueId)
        {
            RivalState state = _state != null ? _state() : null;
            return state != null && state.dormant != null && state.dormant.Contains(uniqueId);
        }

        // ---- internals ----

        private void RestoreOf(List<PriceBookEntry> entries, string reactionId, int uniqueId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (i >= entries.Count) continue;
                PriceBookEntry entry = entries[i];
                if (entry == null || entry.uid != uniqueId || !string.Equals(entry.reactionId, reactionId, StringComparison.Ordinal)) continue;

                entries.RemoveAt(i);
                TCpu cpu = _access.FindCpu(entry.cpuSaveId);
                if (cpu != null) _access.SetTargetPrice(cpu, HeldTarget(entries, entry.cpuSaveId, entry.baseTargetPrice));
            }
        }

        /// <summary>The original target price lowered by the cuts that are still in effect on the CPU.</summary>
        private static float HeldTarget(List<PriceBookEntry> entries, int saveId, float baseTarget)
        {
            float target = baseTarget;
            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry != null && entry.cpuSaveId == saveId) target = PriceCutMath.ClampTarget(target, entry.cutTarget);
            }
            return target;
        }

        private static void RemoveCpu(List<PriceBookEntry> entries, int saveId, int uniqueId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                PriceBookEntry entry = entries[i];
                if (entry != null && entry.cpuSaveId == saveId && entry.uid == uniqueId) entries.RemoveAt(i);
            }
        }

        private TCpu FindIn(IList<TCpu> cpus, int saveId)
        {
            if (cpus == null) return null;
            for (int i = 0; i < cpus.Count; i++)
            {
                TCpu cpu = cpus[i];
                if (cpu != null && _access.SaveIdOf(cpu) == saveId) return cpu;
            }
            return null;
        }

        private static PriceBookEntry Find(List<PriceBookEntry> entries, string reactionId, int saveId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry != null && entry.cpuSaveId == saveId && string.Equals(entry.reactionId, reactionId, StringComparison.Ordinal)) return entry;
            }
            return null;
        }

        private static PriceBookEntry FirstOfCpu(List<PriceBookEntry> entries, int saveId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].cpuSaveId == saveId) return entries[i];
            }
            return null;
        }

        private static int CountOf(List<PriceBookEntry> entries, string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId)) return 0;
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && string.Equals(entries[i].reactionId, reactionId, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private int ExternalIdsInEffect(List<PriceBookEntry> entries)
        {
            _ids.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                PriceBookEntry entry = entries[i];
                if (entry == null || PriceCutMath.IsInternalId(entry.reactionId) || _ids.Contains(entry.reactionId)) continue;
                _ids.Add(entry.reactionId);
            }
            int count = _ids.Count;
            _ids.Clear();
            return count;
        }

        private void Warn(string line)
        {
            if (_warn != null) _warn(line);
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
