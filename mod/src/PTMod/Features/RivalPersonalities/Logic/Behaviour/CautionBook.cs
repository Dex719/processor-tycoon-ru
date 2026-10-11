using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What <see cref="CautionBook.OnPublicFlaw"/> did with a flaw that became public.</summary>
    public enum CautionOutcome
    {
        /// <summary>A caution entry was written for the company.</summary>
        Started = 0,

        /// <summary>The flaw has its entry already (a caution is made once for a flaw record, FR-17 item 1).</summary>
        Duplicate = 1,

        /// <summary>The flaw is less severe than <c>flawCaution.minSeverity</c> (<c>Major</c> by default).</summary>
        TooMild = 2,

        /// <summary>The caution would have ended already (the flaw became public more than <c>flawCaution.months</c> months ago): nothing is written.</summary>
        AlreadyOver = 3,

        /// <summary>The company or the date is not usable (a negative id, an unknown date): nothing is written.</summary>
        Invalid = 4,
    }

    /// <summary>
    /// The caution of AI companies after a public design flaw (design.md, Component 15; FR-17): <see cref="RivalState.cautions"/> holds one entry
    /// <c>{ uid, flawId, until }</c> per flaw record. Pure: the state comes from the provider (read again at every call, a load replaces the object),
    /// the numbers from the config (<c>flawCaution</c>: <c>months</c> 24, <c>minSeverity</c> <c>Major</c>), the dates from the caller.
    /// <list type="bullet">
    /// <item><b>Once per flaw.</b> The caution is anchored on the day the flaw first became public (<c>FlawRecordView.PublicSinceDay</c>), not on the day
    /// of the notification: a flaw that goes public again, or a notification that arrives twice or after a load, finds its entry (or the end of the
    /// term) and writes nothing new.</item>
    /// <item><b>In force</b> while <c>today &lt; until</c>; several entries of one company do not stack (the factor is applied once).</item>
    /// <item><b>At most <see cref="MaxEntries"/></b> entries: past the limit the ones that ended go first, then the ones that end first. The monthly pass
    /// (<see cref="CautionCleaner"/>) removes the ended entries.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class CautionBook
    {
        /// <summary>The longest list of entries in the section (design.md, "Validation Rules").</summary>
        public const int MaxEntries = 50;

        private readonly Func<RivalState> _state;
        private readonly Func<RivalConfig> _config;

        /// <param name="state">The section as it is now (never keep the object: a load replaces it); null gives an empty state that nothing keeps.</param>
        /// <param name="config">The numbers of the feature; null gives the defaults.</param>
        public CautionBook(Func<RivalState> state, Func<RivalConfig> config)
        {
            _state = state;
            _config = config;
        }

        private RivalState State
        {
            get
            {
                RivalState state = _state != null ? _state() : null;
                return state ?? new RivalState();
            }
        }

        private RivalConfig.FlawCautionSettings Settings
        {
            get
            {
                RivalConfig config = _config != null ? _config() : null;
                return (config != null && config.flawCaution != null ? config.flawCaution : RivalConfig.Defaults().flawCaution);
            }
        }

        /// <summary>The entries in the section, ended ones included.</summary>
        public int Count
        {
            get
            {
                List<CautionEntry> list = State.cautions;
                return list == null ? 0 : list.Count;
            }
        }

        /// <summary>The factor of the acceptable failure rate for the company on <paramref name="today"/>: <c>flawCaution.factor</c> while a caution is in force, 1 otherwise.</summary>
        public float FactorFor(int uniqueId, DateTime today)
        {
            return CautionMath.Factor(IsActive(uniqueId, today), Settings.factor);
        }

        /// <summary>A caution of the company is in force on <paramref name="today"/>: some entry of it ends after that day.</summary>
        public bool IsActive(int uniqueId, DateTime today)
        {
            List<CautionEntry> list = State.cautions;
            if (list == null || today == default(DateTime)) return false;

            for (int i = 0; i < list.Count; i++)
            {
                CautionEntry entry = list[i];
                if (entry == null || entry.uid != uniqueId) continue;
                DateTime until;
                if (RivalDates.TryParse(entry.until, out until) && today.Date < until.Date) return true;
            }
            return false;
        }

        /// <summary>The day the caution of the company ends (the latest of its entries in force), or null when none is in force on <paramref name="today"/>.</summary>
        public DateTime? UntilOf(int uniqueId, DateTime today)
        {
            List<CautionEntry> list = State.cautions;
            if (list == null || today == default(DateTime)) return null;

            DateTime? latest = null;
            for (int i = 0; i < list.Count; i++)
            {
                CautionEntry entry = list[i];
                if (entry == null || entry.uid != uniqueId) continue;
                DateTime until;
                if (!RivalDates.TryParse(entry.until, out until) || today.Date >= until.Date) continue;
                if (latest == null || until > latest.Value) latest = until;
            }
            return latest;
        }

        /// <summary>
        /// A flaw of an AI company became public (<c>FlawRegistry.OnFlawStateChanged</c> with the state <c>Public</c>): writes the entry when the
        /// severity reaches the minimum, the flaw has none yet and the term has not ended. <paramref name="publicSince"/> is the day the flaw first
        /// became public (<c>default(DateTime)</c> counts as <paramref name="today"/>).
        /// </summary>
        /// <param name="severity">The number of the severity (<see cref="CautionMath.Minor"/>, <see cref="CautionMath.Major"/>, <see cref="CautionMath.Critical"/>).</param>
        public CautionOutcome OnPublicFlaw(int uniqueId, int flawId, int severity, DateTime publicSince, DateTime today)
        {
            if (uniqueId < 0 || today == default(DateTime)) return CautionOutcome.Invalid;

            RivalConfig.FlawCautionSettings settings = Settings;
            if (!CautionMath.IsSevereEnough(severity, settings.minSeverity)) return CautionOutcome.TooMild;

            RivalState state = State;
            if (state.cautions == null) state.cautions = new List<CautionEntry>();
            if (Find(state.cautions, uniqueId, flawId) != null) return CautionOutcome.Duplicate;

            DateTime start = publicSince == default(DateTime) ? today : publicSince;
            DateTime until = CautionMath.UntilDate(start, settings.months);
            if (today.Date >= until.Date) return CautionOutcome.AlreadyOver;

            state.cautions.Add(new CautionEntry { uid = uniqueId, flawId = flawId, until = RivalDates.ToText(until) });
            Trim(state.cautions, today);
            return CautionOutcome.Started;
        }

        // ---- internals ----

        private static CautionEntry Find(List<CautionEntry> list, int uniqueId, int flawId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                CautionEntry entry = list[i];
                if (entry != null && entry.uid == uniqueId && entry.flawId == flawId) return entry;
            }
            return null;
        }

        /// <summary>Keeps the list within <see cref="MaxEntries"/>: the entries that ended go first, then the ones that end first; the newest entry stays.</summary>
        private static void Trim(List<CautionEntry> list, DateTime today)
        {
            while (list.Count > MaxEntries)
            {
                int victim = -1;
                DateTime victimUntil = DateTime.MaxValue;
                bool victimEnded = false;

                // the last entry is the new one: it is never the victim
                for (int i = 0; i < list.Count - 1; i++)
                {
                    CautionEntry entry = list[i];
                    DateTime until = default(DateTime);
                    bool readable = entry != null && RivalDates.TryParse(entry.until, out until);
                    if (!readable)
                    {
                        victim = i;
                        victimEnded = true;
                        victimUntil = DateTime.MinValue;
                        break;
                    }

                    bool ended = today.Date >= until.Date;
                    bool better = victim < 0 || (ended && !victimEnded) || (ended == victimEnded && until < victimUntil);
                    if (!better) continue;
                    victim = i;
                    victimUntil = until;
                    victimEnded = ended;
                }

                if (victim < 0) victim = 0;
                list.RemoveAt(victim);
            }
        }
    }
}
